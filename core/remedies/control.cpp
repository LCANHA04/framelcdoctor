// Control surface implementation: owns the frame limiter and lets IPC retarget it live.
#include "flcd/control.h"
#include "flcd/frame_limiter.h"
#include "flcd/log.h"

namespace flcd::control {
namespace {
FrameLimiter g_lim;
int g_fps = 0, g_ppf = 1;   // ppf kept for API compatibility; pacing is per displayed frame
}

void Init() { g_lim.Init(); }

void SetLimiter(int displayFps, int /*ppf - auto via frame boundaries*/)
{
    g_fps = displayFps < 0 ? 0 : displayFps;
    g_lim.SetTarget(g_fps, 1);   // Tick() is called once per displayed frame
    flcd::Log("control: limiter -> %d fps", g_fps);
}

void GetLimiter(int* displayFps, int* ppf) { if (displayFps) *displayFps = g_fps; if (ppf) *ppf = g_ppf; }

void Tick() { g_lim.Tick(); }   // called by the orchestrator only on frame boundaries

} // namespace flcd::control
