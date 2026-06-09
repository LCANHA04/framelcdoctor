// flcd_inject.exe - load the FrameLCDoctor core into a RUNNING game by remote thread.
//
// Used for targets we can't reach with a beside-the-exe proxy: OpenGL games (Minecraft Java
// presents through gdi32!SwapBuffers, a KnownDLL that can't be proxied) and, generally, any
// process we'd rather not drop files next to. Standard CreateRemoteThread(LoadLibraryW)
// injection; the core's DllMain then installs the hooks + pipe just like the proxy path.
//
//   flcd_inject.exe --pid <n> [--dll <path>]
//   flcd_inject.exe --exe <name.exe> [--dll <path>]   (first matching process)
//
// --dll defaults to flcd_core.dll next to this exe. Prints status; exit 0 on success.
// Target must be the same bitness (x64). No anti-cheat targets (the companion guards that).

#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <cstring>
#include <string>

static DWORD FindPidByExe(const wchar_t* exe)
{
    DWORD pid = 0;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe = { sizeof(pe) };
    if (Process32FirstW(snap, &pe)) {
        do { if (_wcsicmp(pe.szExeFile, exe) == 0) { pid = pe.th32ProcessID; break; } }
        while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return pid;
}

int wmain(int argc, wchar_t** argv)
{
    DWORD pid = 0; std::wstring exeName, dll;
    for (int i = 1; i < argc; ++i) {
        if (!wcscmp(argv[i], L"--pid") && i + 1 < argc) pid = (DWORD)_wtoi(argv[++i]);
        else if (!wcscmp(argv[i], L"--exe") && i + 1 < argc) exeName = argv[++i];
        else if (!wcscmp(argv[i], L"--dll") && i + 1 < argc) dll = argv[++i];
    }

    if (dll.empty()) {   // default: flcd_core.dll next to this exe
        wchar_t self[MAX_PATH]; GetModuleFileNameW(nullptr, self, MAX_PATH);
        if (wchar_t* s = wcsrchr(self, L'\\')) *s = 0;
        dll = std::wstring(self) + L"\\flcd_core.dll";
    }
    if (!pid && !exeName.empty()) pid = FindPidByExe(exeName.c_str());
    if (!pid) { wprintf(L"RESULT=FAIL step=target\n(no encontre el proceso objetivo)\n"); return 1; }
    if (GetFileAttributesW(dll.c_str()) == INVALID_FILE_ATTRIBUTES) {
        wprintf(L"RESULT=FAIL step=dll\n(falta %ls)\n", dll.c_str()); return 1;
    }

    HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION
                            | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (!proc) { wprintf(L"RESULT=FAIL step=OpenProcess status=%lu\n(permisos? corre como el mismo usuario)\n", GetLastError()); return 1; }

    SIZE_T bytes = (dll.size() + 1) * sizeof(wchar_t);
    void* remote = VirtualAllocEx(proc, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) { wprintf(L"RESULT=FAIL step=VirtualAllocEx status=%lu\n", GetLastError()); CloseHandle(proc); return 1; }

    int rc = 1;
    if (WriteProcessMemory(proc, remote, dll.c_str(), bytes, nullptr)) {
        // LoadLibraryW lives at the same address in every process on this OS session.
        auto loadLib = (LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleW(L"kernel32"), "LoadLibraryW");
        HANDLE th = CreateRemoteThread(proc, nullptr, 0, loadLib, remote, 0, nullptr);
        if (th) {
            WaitForSingleObject(th, 10000);
            DWORD exit = 0; GetExitCodeThread(th, &exit);   // low 32 bits of the HMODULE (non-zero = loaded)
            CloseHandle(th);
            if (exit) { wprintf(L"RESULT=OK pid=%lu\n", pid); rc = 0; }
            else wprintf(L"RESULT=FAIL step=LoadLibrary\n(el core no cargo en el proceso)\n");
        } else wprintf(L"RESULT=FAIL step=CreateRemoteThread status=%lu\n", GetLastError());
    } else wprintf(L"RESULT=FAIL step=WriteProcessMemory status=%lu\n", GetLastError());

    VirtualFreeEx(proc, remote, 0, MEM_RELEASE);
    CloseHandle(proc);
    return rc;
}
