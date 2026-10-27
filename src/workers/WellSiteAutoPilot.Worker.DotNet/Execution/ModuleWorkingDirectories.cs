using System.Text;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

// Leases live beside, not inside, package extraction. Archive entries can never
// replace them. Unknown/legacy directories and reparse points are left untouched.
internal sealed class ModuleWorkingDirectories(string root)
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("WSA-MODULE-WORK-V1");
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    private readonly object _gate = new();
    private bool _reconciled;
    private string LeaseRoot => _root + ".leases";

    public Lease Acquire()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(LeaseRoot);
            if (IsLink(_root) || IsLink(LeaseRoot)) throw new IOException("Module working directories cannot be symbolic links.");
            if (!_reconciled) { Reconcile(); _reconciled = true; }
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_root, id);
            var path = Path.Combine(LeaseRoot, id + ".lease");
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                stream.Write(Marker);
                stream.Flush();
                Directory.CreateDirectory(directory);
                return new Lease(directory, path, stream);
            }
            catch
            {
                stream.Dispose();
                File.Delete(path);
                throw;
            }
        }
    }

    private void Reconcile()
    {
        // A crash leaves only the bounded set of in-flight runs; cap the scan in
        // case an operator points the service at a very large pre-existing root.
        foreach (var path in Directory.EnumerateFiles(LeaseRoot, "*.lease").Take(1000))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            var directory = Path.Combine(_root, id);
            try
            {
                if (IsLink(path) || Directory.Exists(directory) && IsLink(directory)) continue;
                // An active run, including one in another worker process, holds
                // this file without sharing. Failure to acquire it means skip.
                using (var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (lease.Length != Marker.Length) continue;
                    var content = new byte[Marker.Length];
                    lease.ReadExactly(content);
                    if (!content.AsSpan().SequenceEqual(Marker)) continue;
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
                File.Delete(path);
            }
            catch (IOException) { /* Active or already reconciled; leave it for a later start. */ }
            catch (UnauthorizedAccessException) { /* Ownership is not established; do not broaden access. */ }
        }
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal sealed class Lease(string directory, string path, FileStream stream) : IDisposable
    {
        public string DirectoryPath { get; } = directory;
        public void Dispose()
        {
            stream.Dispose();
            // A failed directory cleanup retains the ownership marker for next startup.
            if (!Directory.Exists(DirectoryPath)) File.Delete(path);
        }
    }
}
