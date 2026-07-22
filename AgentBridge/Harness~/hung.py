"""Check if a process is hung; optionally kill it.

Usage:
  hung.py                         Check Unity.exe; exit 0 responding, 1 hung/missing
  hung.py --kill                  Kill if hung
  hung.py --name=<exe>            Process to check (default: Unity.exe)
"""
import ctypes
import ctypes.wintypes
import sys

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

PROCESS_TERMINATE = 0x0001


def find_pids_by_name(exe_name):
    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize",              ctypes.wintypes.DWORD),
            ("cntUsage",            ctypes.wintypes.DWORD),
            ("th32ProcessID",       ctypes.wintypes.DWORD),
            ("th32DefaultHeapID",   ctypes.POINTER(ctypes.c_ulong)),
            ("th32ModuleID",        ctypes.wintypes.DWORD),
            ("cntThreads",          ctypes.wintypes.DWORD),
            ("th32ParentProcessID", ctypes.wintypes.DWORD),
            ("pcPriClassBase",      ctypes.c_long),
            ("dwFlags",             ctypes.wintypes.DWORD),
            ("szExeFile",           ctypes.c_wchar * 260),
        ]

    TH32CS_SNAPPROCESS = 0x00000002
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    pids = []
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
    if kernel32.Process32FirstW(snap, ctypes.byref(entry)):
        while True:
            if entry.szExeFile.lower() == exe_name.lower():
                pids.append(entry.th32ProcessID)
            if not kernel32.Process32NextW(snap, ctypes.byref(entry)):
                break
    kernel32.CloseHandle(snap)
    return pids


def windows_for_pids(pids):
    pid_set = set(pids)
    results = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)
    def callback(hwnd, _):
        if user32.IsWindowVisible(hwnd):
            pid = ctypes.wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if pid.value in pid_set:
                results.append((hwnd, pid.value))
        return True

    user32.EnumWindows(callback, 0)
    return results


def kill_pid(pid):
    handle = kernel32.OpenProcess(PROCESS_TERMINATE, False, pid)
    if not handle:
        return False
    ok = kernel32.TerminateProcess(handle, 1)
    kernel32.CloseHandle(handle)
    return bool(ok)


def main():
    flags = {}
    for arg in sys.argv[1:]:
        if arg.startswith("--"):
            if "=" in arg:
                key, val = arg.lstrip("-").split("=", 1)
                flags[key] = val
            else:
                flags[arg.lstrip("-")] = True

    exe_name = flags.get("name", "Unity.exe")
    do_kill  = "kill" in flags

    pids = find_pids_by_name(exe_name)
    if not pids:
        print(f"{exe_name}: not running.", file=sys.stderr)
        sys.exit(1)

    windows = windows_for_pids(pids)
    if not windows:
        print(f"{exe_name}: running but no visible windows — cannot determine state.", file=sys.stderr)
        sys.exit(1)

    hung_pids = {pid for hwnd, pid in windows if user32.IsHungAppWindow(hwnd)}

    if not hung_pids:
        print(f"{exe_name}: responding.")
        sys.exit(0)

    label = ", ".join(str(p) for p in hung_pids)
    print(f"{exe_name}: not responding (PID {label}).")

    if do_kill:
        for pid in hung_pids:
            if kill_pid(pid):
                print(f"Killed PID {pid}.")
            else:
                print(f"Failed to kill PID {pid}.", file=sys.stderr)

    sys.exit(1)


if __name__ == "__main__":
    main()
