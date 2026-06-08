// FrameLCDoctor core - injection bootstrap + frame orchestration (P0).
//
// Loaded into the target game as a proxy DLL (d3d11/dxgi/winmm, chosen per game).
// When packaged for a specific title, a generated forwarders header re-exports the
// real DLL's symbols (see tools/gen_forwarders.py). The framework code here is
// game-agnostic.

#include <windows.h>

#if __has_include("forwarders.generated.h")
#  include "forwarders.generated.h"   // per-game export forwarders (packaging step)
#endif

#include "flcd/log.h"
#include "flcd/hooks.h"
#include "flcd/profiler.h"
#include "flcd/control.h"
#include "flcd/ipc.h"

namespace {

// Called once per top-level present by the hook. Order: pace, then sample.
void OnPresent()
{
    flcd::control::Tick();
    flcd::profiler::OnPresent();
}

int IniInt(const wchar_t* section, const wchar_t* key, int def)
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(GetModuleHandleW(nullptr), path, MAX_PATH);   // next to the .exe
    if (wchar_t* s = wcsrchr(path, L'\\'))
        wcscpy_s(s + 1, MAX_PATH - (s + 1 - path), L"framelcdoctor.ini");
    return GetPrivateProfileIntW(section, key, def, path);
}

DWORD WINAPI BootThread(LPVOID)
{
    flcd::Log("=== FrameLCDoctor core (P0) ===");

    // P0 bootstrap config (a placeholder until the companion/profile drives this live).
    int ppf      = IniInt(L"core", L"Ppf", 1);
    int limitFps = IniInt(L"core", L"LimitFps", 0);   // 0 = uncapped

    flcd::control::Init();
    flcd::control::SetLimiter(limitFps, ppf);
    flcd::profiler::Start();
    flcd::Log("config: ppf=%d limitFps=%d", ppf, limitFps);

    flcd::hooks::SetOnPresent(&OnPresent);
    if (!flcd::hooks::InstallD3D11PresentHook())
        flcd::Log("WARN: present hook not installed (non-D3D11 target?)");

    flcd::ipc::StartServer();
    flcd::Log("=== core ready: hook + profiler + ipc up ===");
    return 0;
}

} // namespace

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        flcd::SetSelfModule(h);
        DisableThreadLibraryCalls(h);
        CloseHandle(CreateThread(nullptr, 0, BootThread, nullptr, 0, nullptr));
    }
    return TRUE;
}
