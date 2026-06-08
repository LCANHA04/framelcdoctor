// Control surface implementation: owns the frame limiter and lets IPC retarget it live.
#include "flcd/control.h"
#include "flcd/frame_limiter.h"
#include "flcd/profiler.h"
#include "flcd/log.h"

namespace flcd::control {
namespace {
FrameLimiter g_lim;
int g_fps = 0, g_ppf = 1;
}

void Init() { g_lim.Init(); }

void SetLimiter(int displayFps, int ppf)
{
    g_fps = displayFps < 0 ? 0 : displayFps;
    g_ppf = ppf < 1 ? 1 : ppf;
    g_lim.SetTarget(g_fps, g_ppf);
    profiler::SetPpf(g_ppf);
    flcd::Log("control: limiter -> %d fps (ppf %d)", g_fps, g_ppf);
}

void GetLimiter(int* displayFps, int* ppf) { if (displayFps) *displayFps = g_fps; if (ppf) *ppf = g_ppf; }

void Tick() { g_lim.Tick(); }

} // namespace flcd::control
