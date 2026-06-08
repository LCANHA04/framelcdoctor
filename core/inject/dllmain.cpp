// FrameLCDoctor core - injection bootstrap (P0).
//
// Loaded into the target game as a proxy DLL (d3d11/dxgi/winmm, chosen per game).
// When packaged for a specific title, a generated forwarders header re-exports the
// real DLL's symbols (see tools/gen_forwarders.py). The framework code here is
// game-agnostic: it boots a worker thread that will bring up the IPC server, install
// the graphics/timing hooks, start the profiler, and wait for the companion app.

#include <windows.h>

#if __has_include("forwarders.generated.h")
#  include "forwarders.generated.h"   // per-game export forwarders (packaging step)
#endif

#include "flcd/log.h"
// #include "flcd/ipc.h"       // P0
// #include "flcd/hooks.h"     // P0/P1
// #include "flcd/profiler.h"  // P1

static HMODULE g_self = nullptr;

static DWORD WINAPI BootThread(LPVOID)
{
    flcd::Log("FrameLCDoctor core attached (P0 bootstrap).");
    // TODO P0: ipc::StartPipeServer();
    // TODO P0: hooks::InstallPresentHook();   // factory -> swapchain -> Present
    // TODO P1: profiler::Start();             // frametime / ppf / gpu-cpu time
    return 0;
}

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = h;
        flcd::SetSelfModule(h);
        DisableThreadLibraryCalls(h);
        CloseHandle(CreateThread(nullptr, 0, BootThread, nullptr, 0, nullptr));
    }
    return TRUE;
}
