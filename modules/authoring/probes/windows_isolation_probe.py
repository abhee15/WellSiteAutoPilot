"""Disposable Windows build-host isolation probe (AppContainer + Job object).

Measures whether a child started in a fresh AppContainer with no capabilities,
inside a Job object with a memory limit and kill-on-close, is denied:
  - reading a file outside its granted work directory,
  - connecting to a loopback listener,
  - surviving tree cancellation (grandchild must die with the job).
It measures; it does not deploy a sandbox. Results describe this account only.
"""
import argparse, ctypes, json, os, platform, shutil, socket, subprocess, sys, tempfile, threading, time
from ctypes import wintypes as w

if platform.system() != "Windows":
    sys.exit("windows only")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
userenv = ctypes.WinDLL("userenv", use_last_error=True)
advapi = ctypes.WinDLL("advapi32", use_last_error=True)
ole32 = ctypes.WinDLL("ole32")

PROFILE = "WsaAuthoringProbe"
EXTENDED_STARTUPINFO_PRESENT = 0x00080000
CREATE_SUSPENDED = 0x4
CREATE_NO_WINDOW = 0x08000000
PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES = 0x00020009
JobObjectExtendedLimitInformation = 9
JOB_KILL_ON_CLOSE = 0x2000
JOB_PROCESS_MEMORY = 0x100
JOB_ACTIVE_PROCESS = 0x8


class SEC_CAPS(ctypes.Structure):
    _fields_ = [("AppContainerSid", ctypes.c_void_p), ("Capabilities", ctypes.c_void_p),
                ("CapabilityCount", w.DWORD), ("Reserved", w.DWORD)]


class STARTUPINFOW(ctypes.Structure):
    _fields_ = [("cb", w.DWORD), ("lpReserved", w.LPWSTR), ("lpDesktop", w.LPWSTR), ("lpTitle", w.LPWSTR),
                ("dwX", w.DWORD), ("dwY", w.DWORD), ("dwXSize", w.DWORD), ("dwYSize", w.DWORD),
                ("dwXCountChars", w.DWORD), ("dwYCountChars", w.DWORD), ("dwFillAttribute", w.DWORD),
                ("dwFlags", w.DWORD), ("wShowWindow", w.WORD), ("cbReserved2", w.WORD),
                ("lpReserved2", ctypes.c_void_p), ("hStdInput", w.HANDLE), ("hStdOutput", w.HANDLE),
                ("hStdError", w.HANDLE)]


class STARTUPINFOEXW(ctypes.Structure):
    _fields_ = [("StartupInfo", STARTUPINFOW), ("lpAttributeList", ctypes.c_void_p)]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [("hProcess", w.HANDLE), ("hThread", w.HANDLE), ("dwProcessId", w.DWORD), ("dwThreadId", w.DWORD)]


class BASIC_LIMIT(ctypes.Structure):
    _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                ("LimitFlags", w.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", w.DWORD),
                ("Affinity", ctypes.c_size_t), ("PriorityClass", w.DWORD), ("SchedulingClass", w.DWORD)]


class IO_COUNTERS(ctypes.Structure):
    _fields_ = [(n, ctypes.c_uint64) for n in ("a", "b", "c", "d", "e", "f")]


class EXT_LIMIT(ctypes.Structure):
    _fields_ = [("BasicLimitInformation", BASIC_LIMIT), ("IoInfo", IO_COUNTERS),
                ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]


k32.CreateJobObjectW.restype = w.HANDLE
k32.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
k32.ResumeThread.argtypes = [w.HANDLE]
k32.OpenProcess.restype = w.HANDLE
k32.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k32.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
k32.GetExitCodeProcess.argtypes = [w.HANDLE, ctypes.POINTER(w.DWORD)]
k32.CloseHandle.argtypes = [w.HANDLE]
userenv.CreateAppContainerProfile.argtypes = [w.LPCWSTR, w.LPCWSTR, w.LPCWSTR, ctypes.c_void_p, w.DWORD,
                                              ctypes.POINTER(ctypes.c_void_p)]
userenv.DeriveAppContainerSidFromAppContainerName.argtypes = [w.LPCWSTR, ctypes.POINTER(ctypes.c_void_p)]
advapi.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(w.LPWSTR)]


def app_container_sid():
    sid = ctypes.c_void_p()
    hr = userenv.CreateAppContainerProfile(PROFILE, PROFILE, PROFILE, None, 0, ctypes.byref(sid))
    if hr != 0:
        sid = ctypes.c_void_p()
        hr = userenv.DeriveAppContainerSidFromAppContainerName(PROFILE, ctypes.byref(sid))
        if hr != 0:
            raise OSError(f"AppContainer profile unavailable: 0x{hr & 0xffffffff:x}")
    s = w.LPWSTR()
    advapi.ConvertSidToStringSidW(sid, ctypes.byref(s))
    return sid, s.value


def launch(cmdline, cwd, sid, job):
    size = ctypes.c_size_t()
    k32.InitializeProcThreadAttributeList(None, 1, 0, ctypes.byref(size))
    buf = ctypes.create_string_buffer(size.value)
    k32.InitializeProcThreadAttributeList(buf, 1, 0, ctypes.byref(size))
    caps = SEC_CAPS(sid, None, 0, 0)  # no capabilities: no network, no broad file access
    k32.UpdateProcThreadAttribute.argtypes = [ctypes.c_void_p, w.DWORD, ctypes.c_size_t, ctypes.c_void_p,
                                              ctypes.c_size_t, ctypes.c_void_p, ctypes.c_void_p]
    if not k32.UpdateProcThreadAttribute(buf, 0, PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES,
                                         ctypes.byref(caps), ctypes.sizeof(caps), None, None):
        raise ctypes.WinError(ctypes.get_last_error())
    si = STARTUPINFOEXW()
    si.StartupInfo.cb = ctypes.sizeof(si)
    si.lpAttributeList = ctypes.cast(buf, ctypes.c_void_p)
    pi = PROCESS_INFORMATION()
    k32.CreateProcessW.argtypes = [w.LPCWSTR, w.LPWSTR, ctypes.c_void_p, ctypes.c_void_p, w.BOOL, w.DWORD,
                                   ctypes.c_void_p, w.LPCWSTR, ctypes.c_void_p, ctypes.c_void_p]
    ok = k32.CreateProcessW(None, ctypes.create_unicode_buffer(cmdline), None, None, False,
                            EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED | CREATE_NO_WINDOW,
                            None, cwd, ctypes.byref(si), ctypes.byref(pi))
    if not ok:
        raise ctypes.WinError(ctypes.get_last_error())
    if not k32.AssignProcessToJobObject(job, pi.hProcess):
        raise ctypes.WinError(ctypes.get_last_error())
    k32.ResumeThread(pi.hThread)
    return pi


def make_job(memory_mb):
    job = k32.CreateJobObjectW(None, None)
    info = EXT_LIMIT()
    info.BasicLimitInformation.LimitFlags = JOB_KILL_ON_CLOSE | JOB_PROCESS_MEMORY | JOB_ACTIVE_PROCESS
    info.BasicLimitInformation.ActiveProcessLimit = 8
    info.ProcessMemoryLimit = memory_mb * 1024 * 1024
    k32.SetInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]
    if not k32.SetInformationJobObject(job, JobObjectExtendedLimitInformation, ctypes.byref(info),
                                       ctypes.sizeof(info)):
        raise ctypes.WinError(ctypes.get_last_error())
    return job


def alive(pid):
    h = k32.OpenProcess(0x1000 | 0x100000, False, pid)
    if not h:
        return False
    code = w.DWORD()
    k32.GetExitCodeProcess(h, ctypes.byref(code))
    k32.CloseHandle(h)
    return code.value == 259


def run_child(ps_script, work, sid, job, timeout=45):
    cmd = ('C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -NonInteractive '
           '-ExecutionPolicy Bypass -File "%s"' % ps_script)
    pi = launch(cmd, work, sid, job)
    k32.WaitForSingleObject(pi.hProcess, timeout * 1000)
    code = w.DWORD()
    k32.GetExitCodeProcess(pi.hProcess, ctypes.byref(code))
    return pi, code.value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", required=True)
    args = ap.parse_args()

    root = tempfile.mkdtemp(prefix="wsa-iso-")
    work = os.path.join(root, "work")
    outside = os.path.join(root, "outside")
    os.makedirs(work); os.makedirs(outside)
    secret = os.path.join(outside, "secret.txt")
    open(secret, "w").write("synthetic-secret")

    listener = socket.socket(); listener.bind(("127.0.0.1", 0)); listener.listen(5)
    port = listener.getsockname()[1]
    accepted = []
    threading.Thread(target=lambda: [accepted.append(1) for _ in iter(lambda: listener.accept(), None)],
                     daemon=True).start()

    sid, sid_text = app_container_sid()
    # Grant only the work directory to the AppContainer; the outside directory stays ungranted.
    subprocess.run(["icacls", work, "/grant", f"*{sid_text}:(OI)(CI)F", "/T"], check=True,
                   capture_output=True)
    subprocess.run(["icacls", root, "/grant", f"*{sid_text}:(X)"], check=True, capture_output=True)

    probe = os.path.join(work, "child.ps1")
    result_file = os.path.join(work, "child.json")
    pidfile = os.path.join(work, "grandchild.pid")
    open(probe, "w").write(r'''
$r = @{}
try { $null = Get-Content -LiteralPath '%s' -ErrorAction Stop; $r.readOutside = $true } catch { $r.readOutside = $false }
try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', %d); $r.loopback = $true } catch { $r.loopback = $false }
try { Set-Content -LiteralPath '%s' -Value 'x' -ErrorAction Stop; $r.writeOutside = $true; Remove-Item '%s' } catch { $r.writeOutside = $false }
try { [IO.File]::WriteAllText('%s', 'ok'); $r.writeWork = $true } catch { $r.writeWork = $false }
try {
  $psi = New-Object Diagnostics.ProcessStartInfo 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe', '-NoProfile -Command Start-Sleep 300'
  $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
  $g = [Diagnostics.Process]::Start($psi); $r.grandchildError = $null
  [string]$g.Id | Set-Content '%s'
} catch { $r.grandchildError = $_.Exception.Message }
$r | ConvertTo-Json | Set-Content '%s'
Start-Sleep 300
''' % (secret, port, os.path.join(outside, "w.txt"), os.path.join(outside, "w.txt"),
       os.path.join(work, "ok.txt"), pidfile, result_file))

    report = {"probe": "windows-appcontainer-job-boundary", "platform": platform.platform(),
              "status": "measurement-completed", "observed": {}, "notEstablished": [
                  "Deployment identity and secret denial on the intended server",
                  "Hosted compiler/SDK access under AppContainer (build toolchain not run inside)",
                  "Clean offline SDK provisioning", "Authenticated preview/delivery"]}
    job = make_job(memory_mb=512)
    try:
        pi, _ = run_child(probe, work, sid, job, timeout=20)  # blocks until timeout: child sleeps
        deadline = time.time() + 30
        while time.time() < deadline and not os.path.exists(result_file):
            time.sleep(0.5)
        child = json.loads(open(result_file, encoding="utf-8-sig").read()) if os.path.exists(result_file) else None
        txt = open(pidfile, encoding="utf-8-sig", errors="ignore").read().strip() if os.path.exists(pidfile) else ""
        report["observed"]["pidfileRaw"] = txt
        gpid = int(txt) if txt.isdigit() else None
        report["observed"]["child"] = child
        report["observed"]["grandchildStarted"] = bool(gpid and alive(gpid))
        report["observed"]["listenerAccepted"] = bool(accepted)
        k32.CloseHandle(job)  # kill-on-close: cancel the whole tree
        time.sleep(2)
        report["observed"]["rootAliveAfterCancel"] = alive(pi.dwProcessId)
        report["observed"]["grandchildAliveAfterCancel"] = bool(gpid and alive(gpid))
    except Exception as e:
        report["status"] = "failed"
        report["error"] = repr(e)
    finally:
        shutil.rmtree(root, ignore_errors=True)

    o = report["observed"]; c = o.get("child") or {}
    report["isolationHeld"] = bool(c) and not c.get("readOutside") and not c.get("loopback") \
        and not c.get("writeOutside") and not o.get("listenerAccepted")
    report["treeCancellationHeld"] = not o.get("rootAliveAfterCancel", True) \
        and not o.get("grandchildAliveAfterCancel", True) and o.get("grandchildStarted", False)
    report["releaseGate"] = "blocked"
    report["conclusion"] = ("AppContainer+Job measured on this account only; release remains blocked until "
                            "verified on the intended deployment with the real build toolchain.")
    os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
    json.dump(report, open(args.report, "w"), indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
