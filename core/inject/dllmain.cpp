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
#include "flcd/frame_limiter.h"
#include "flcd/ipc.h"

namespace {

flcd::FrameLimiter g_limiter;

// Called once per top-level present by the hook. Order: pace, then sample.
void OnPresent()
{
    g_limiter.Tick();
    flcd::profiler::OnPresent();
}

DWORD WINAPI BootThread(LPVOID)
{
    flcd::Log("=== FrameLCDoctor core (P0) ===");

    g_limiter.Init();
    g_limiter.SetTarget(0, 1);          // uncapped by default; companion/profile sets this
    flcd::profiler::Start();
    flcd::profiler::SetPpf(1);          // overridden per game profile

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
