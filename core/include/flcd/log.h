// Minimal logging for the core. Writes next to the loaded DLL.
#pragma once
#include <windows.h>
#include <stdio.h>
#include <stdarg.h>

namespace flcd {

inline HMODULE& SelfModule() { static HMODULE m = nullptr; return m; }
inline void SetSelfModule(HMODULE m) { SelfModule() = m; }

inline void Log(const char* fmt, ...)
{
    char buf[1024];
    va_list a; va_start(a, fmt);
    vsnprintf(buf, sizeof(buf), fmt, a); va_end(a);

    wchar_t path[MAX_PATH];
    GetModuleFileNameW(SelfModule(), path, MAX_PATH);
    if (wchar_t* s = wcsrchr(path, L'\\'))
        wcscpy_s(s + 1, MAX_PATH - (s + 1 - path), L"framelcdoctor.log");

    FILE* f = nullptr;
    if (_wfopen_s(&f, path, L"a") == 0 && f) {
        fprintf(f, "[%lu] %s\n", GetTickCount(), buf);
        fclose(f);
    }
}

} // namespace flcd
