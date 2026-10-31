using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WellSiteAutoPilot.Infrastructure.Authoring;

public sealed record IsolatedProcessSpec(
    string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, long MemoryLimitBytes, TimeSpan Timeout,
    bool AppContainer = false, IReadOnlyList<string>? ReadWriteDirectories = null);

public sealed record IsolatedProcessResult(int ExitCode, string Output, bool TimedOut, bool Isolated, string Mode);

/// <summary>Runs one child process and everything it spawns under an enforced boundary.</summary>
public interface IIsolatedProcessRunner
{
    Task<IsolatedProcessResult> RunAsync(IsolatedProcessSpec spec, CancellationToken cancellationToken);
}

/// <summary>
/// Windows Job object (kill-on-close, job memory limit, tree termination on timeout or cancellation) with an
/// optional AppContainer token (no capabilities: no network, no broad file access; only the listed directories are
/// granted). Mechanism follows modules/authoring/probes/windows_isolation_probe.py. Elsewhere it degrades to a
/// plain process whose tree is killed on timeout or cancellation, and reports <c>Isolated = false</c>.
/// </summary>
public sealed class JobObjectProcessRunner : IIsolatedProcessRunner
{
    private const int MaximumOutputBytes = 64 * 1024;
    public const string AppContainerProfile = "WsaAuthoringBuild";

    public async Task<IsolatedProcessResult> RunAsync(IsolatedProcessSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return OperatingSystem.IsWindows()
            ? await RunWindowsAsync(spec, cancellationToken)
            : await RunPortableAsync(spec, cancellationToken);
    }

    private static async Task<IsolatedProcessResult> RunPortableAsync(IsolatedProcessSpec spec, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(spec.FileName)
        {
            WorkingDirectory = spec.WorkingDirectory, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in spec.Arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var (name, value) in spec.Environment) start.Environment[name] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The process did not start.");
        var buffer = new StringBuilder();
        void Collect(string? line)
        {
            if (line is null) return;
            lock (buffer) if (buffer.Length < MaximumOutputBytes) buffer.AppendLine(line);
        }
        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(spec.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new(-1, string.Empty, true, false, "portable");
        }
        lock (buffer) return new(process.ExitCode, buffer.ToString(), false, false, "portable");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IsolatedProcessResult> RunWindowsAsync(IsolatedProcessSpec spec, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "wsa-iso-" + Guid.NewGuid().ToString("N") + ".log");
        using var job = NativeMethods.CreateJob(spec.MemoryLimitBytes);
        IntPtr appContainerSid = IntPtr.Zero;
        try
        {
            if (spec.AppContainer)
            {
                appContainerSid = NativeMethods.AppContainerSid(AppContainerProfile);
                var sid = new SecurityIdentifier(appContainerSid);
                foreach (var directory in spec.ReadWriteDirectories ?? [])
                    GrantDirectory(directory, sid);
            }
            using var log = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            if (spec.AppContainer)
            {
                var sid = new SecurityIdentifier(appContainerSid);
                var security = new FileInfo(outputPath).GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                new FileInfo(outputPath).SetAccessControl(security);
            }
            using var process = NativeMethods.Launch(spec, log.SafeFileHandle, appContainerSid, job);
            var timedOut = false;
            try
            {
                using var registration = cancellationToken.Register(() => NativeMethods.TerminateJob(job));
                var deadline = DateTime.UtcNow + spec.Timeout;
                while (!NativeMethods.WaitFor(process, 50))
                {
                    if (DateTime.UtcNow >= deadline) { timedOut = true; NativeMethods.TerminateJob(job); break; }
                    if (cancellationToken.IsCancellationRequested) break;
                    await Task.Yield();
                }
                // The tree is the unit: a surviving grandchild would hold the work folder open.
                NativeMethods.TerminateJob(job);
                NativeMethods.WaitFor(process, 10000);
                cancellationToken.ThrowIfCancellationRequested();
                var exit = NativeMethods.ExitCode(process);
                log.Position = 0;
                var bytes = new byte[(int)Math.Min(log.Length, MaximumOutputBytes)];
                var read = await log.ReadAsync(bytes, CancellationToken.None);
                return new(timedOut ? -1 : exit, Encoding.UTF8.GetString(bytes, 0, read), timedOut, true,
                    spec.AppContainer ? "job+appcontainer" : "job");
            }
            finally { }
        }
        finally
        {
            if (appContainerSid != IntPtr.Zero) NativeMethods.FreeSid(appContainerSid);
            try { File.Delete(outputPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void GrantDirectory(string directory, SecurityIdentifier sid, bool subtree = true)
    {
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        var inheritance = subtree ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify | FileSystemRights.ReadAndExecute,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        private const uint ExtendedStartupInfoPresent = 0x00080000, CreateSuspended = 0x4, CreateNoWindow = 0x08000000,
            CreateUnicodeEnvironment = 0x400, JobKillOnClose = 0x2000, JobMemory = 0x200, StartfUseStdHandles = 0x100;
        private const int JobObjectExtendedLimitInformation = 9, SecurityCapabilitiesAttribute = 0x00020009;

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityCapabilities { public IntPtr AppContainerSid, Capabilities; public uint CapabilityCount, Reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint cb; public string? Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort ShowWindow, Reserved2; public IntPtr Reserved2Pointer, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx { public StartupInfo Startup; public IntPtr AttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimit
        {
            public long PerProcessUserTime, PerJobUserTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimit
        {
            public BasicLimit Basic; public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimit info, int length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(string? application, char[] commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment,
            string? directory, ref StartupInfoEx startup, out ProcessInformation information);
        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        private static extern int CreateAppContainerProfile(string name, string display, string description, IntPtr capabilities, uint count, out IntPtr sid);
        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
        [DllImport("advapi32.dll")] public static extern IntPtr FreeSid(IntPtr sid);

        public static SafeFileHandle CreateJob(long memoryLimitBytes)
        {
            var job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limit = new ExtendedLimit { JobMemory = checked((UIntPtr)(ulong)Math.Max(64L * 1024 * 1024, memoryLimitBytes)) };
            limit.Basic.LimitFlags = JobKillOnClose | JobMemory;
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limit, Marshal.SizeOf<ExtendedLimit>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out int exitCode);

        public static bool WaitFor(SafeProcessHandle process, uint milliseconds) => WaitForSingleObject(process, milliseconds) == 0;

        public static int ExitCode(SafeProcessHandle process) => GetExitCodeProcess(process, out var code) ? code : -1;

        public static void TerminateJob(SafeFileHandle job)
        {
            if (!job.IsClosed && !job.IsInvalid) TerminateJobObject(job, 1);
        }

        public static IntPtr AppContainerSid(string profile)
        {
            if (CreateAppContainerProfile(profile, profile, profile, IntPtr.Zero, 0, out var sid) == 0) return sid;
            var hr = DeriveAppContainerSidFromAppContainerName(profile, out sid);
            if (hr != 0) throw new Win32Exception(hr, "The AppContainer profile is unavailable.");
            return sid;
        }

        public static SafeProcessHandle Launch(IsolatedProcessSpec spec, SafeFileHandle log, IntPtr appContainerSid, SafeFileHandle job)
        {
            var commandLine = new StringBuilder(Quote(spec.FileName));
            foreach (var argument in spec.Arguments) commandLine.Append(' ').Append(Quote(argument));
            var environment = new StringBuilder();
            foreach (var (name, value) in spec.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                environment.Append(name).Append('=').Append(value).Append('\0');
            environment.Append('\0');
            if (!SetHandleInformation(log, 1, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new StartupInfoEx();
            startup.Startup.cb = (uint)Marshal.SizeOf<StartupInfoEx>();
            startup.Startup.Flags = StartfUseStdHandles;
            startup.Startup.StdOutput = startup.Startup.StdError = log.DangerousGetHandle();
            startup.Startup.StdInput = IntPtr.Zero;
            var attributeList = IntPtr.Zero;
            var capabilities = IntPtr.Zero;
            var environmentPointer = Marshal.StringToHGlobalUni(environment.ToString());
            try
            {
                var flags = CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment;
                if (appContainerSid != IntPtr.Zero)
                {
                    var size = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                    attributeList = Marshal.AllocHGlobal(size);
                    if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var value = new SecurityCapabilities { AppContainerSid = appContainerSid };
                    capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
                    Marshal.StructureToPtr(value, capabilities, false);
                    if (!UpdateProcThreadAttribute(attributeList, 0, SecurityCapabilitiesAttribute, capabilities,
                            Marshal.SizeOf<SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    startup.AttributeList = attributeList;
                    flags |= ExtendedStartupInfoPresent;
                }
                if (!CreateProcessW(null, (commandLine.ToString() + "\0").ToCharArray(), IntPtr.Zero, IntPtr.Zero, true, flags, environmentPointer,
                        spec.WorkingDirectory, ref startup, out var information))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    // Suspended until it is inside the job, so no descendant can ever start outside it.
                    if (!AssignProcessToJobObject(job, information.Process)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (ResumeThread(information.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                catch
                {
                    TerminateJob(job);
                    throw;
                }
                finally { CloseHandle(information.Thread); }
                return new SafeProcessHandle(information.Process, ownsHandle: true);
            }
            finally
            {
                Marshal.FreeHGlobal(environmentPointer);
                if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
                if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            }
        }

        private static string Quote(string value) =>
            value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"') ? value : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
