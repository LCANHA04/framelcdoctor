// Frame profiler (P1+): derive frametime / present-rate / displayFps from present calls.
// Auto-detects displayed-frame boundaries from the inter-present gap so engines that
// present multiple times per frame (e.g. NieR's 2x) need no hard-coded ppf.
#ifndef NOMINMAX
#  define NOMINMAX
#endif
#include "flcd/profiler.h"
#include <windows.h>
#include <algorithm>

namespace flcd::profiler {
namespace {

CRITICAL_SECTION g_cs;
bool             g_init = false;
LARGE_INTEGER    g_freq{};
LONGLONG         g_frameGapTicks = 0;     // gap above which a present starts a new frame

LONGLONG g_lastPresent   = 0;
LONGLONG g_lastFrameStart= 0;
LONGLONG g_windowStart   = 0;
int      g_presentsInWin = 0;
int      g_framesInWin   = 0;

double g_emaFrametimeMs = 0;   // displayed-frame interval, EMA
double g_maxFrametimeMs = 0;   // window peak (rough p99)
double g_presentRate    = 0;
double g_displayFps     = 0;
double g_ppfEst         = 1;   // smoothed presents-per-frame estimate

double g_gpuBusyPct  = -1;
double g_cpuMainPct  = -1;     // busiest single core
double g_cpuTotalPct = -1;

double TicksToMs(LONGLONG t) { return (double)t * 1000.0 / g_freq.QuadPart; }
static double Ema(double cur, double sample) { return cur < 0 ? sample : cur * 0.6 + sample * 0.4; }

} // namespace

void Start()
{
    if (!g_init) { InitializeCriticalSection(&g_cs); g_init = true; }
    QueryPerformanceFrequency(&g_freq);
    g_frameGapTicks = (LONGLONG)(g_freq.QuadPart * 0.0025);   // 2.5 ms separates frames from paired presents
    g_lastPresent = g_lastFrameStart = g_windowStart = 0;
    g_presentsInWin = g_framesInWin = 0;
}

void Stop() {}

void MergeOsMetrics(double gpuBusyPct, double cpuPeakCorePct, double cpuTotalPct)
{
    EnterCriticalSection(&g_cs);
    if (gpuBusyPct     >= 0) g_gpuBusyPct  = Ema(g_gpuBusyPct,  gpuBusyPct);
    if (cpuPeakCorePct >= 0) g_cpuMainPct  = Ema(g_cpuMainPct,  cpuPeakCorePct);
    if (cpuTotalPct    >= 0) g_cpuTotalPct = Ema(g_cpuTotalPct, cpuTotalPct);
    LeaveCriticalSection(&g_cs);
}

bool OnPresent()
{
    if (!g_init) return true;
    LARGE_INTEGER now; QueryPerformanceCounter(&now);
    EnterCriticalSection(&g_cs);

    bool boundary = (g_lastPresent == 0) || (now.QuadPart - g_lastPresent >= g_frameGapTicks);
    g_lastPresent = now.QuadPart;
    g_presentsInWin++;

    if (boundary) {
        g_framesInWin++;
        if (g_lastFrameStart) {
            double ftMs = TicksToMs(now.QuadPart - g_lastFrameStart);
            g_emaFrametimeMs = g_emaFrametimeMs ? (g_emaFrametimeMs * 0.95 + ftMs * 0.05) : ftMs;
            g_maxFrametimeMs = std::max(g_maxFrametimeMs, ftMs);
        }
        g_lastFrameStart = now.QuadPart;
    }

    if (!g_windowStart) g_windowStart = now.QuadPart;
    if (g_presentsInWin >= 120) {
        double sec = TicksToMs(now.QuadPart - g_windowStart) / 1000.0;
        if (sec > 0) {
            g_presentRate = g_presentsInWin / sec;
            g_displayFps  = g_framesInWin / sec;
            if (g_displayFps > 0) g_ppfEst = g_ppfEst * 0.7 + (g_presentRate / g_displayFps) * 0.3;
        }
        g_presentsInWin = g_framesInWin = 0; g_windowStart = now.QuadPart; g_maxFrametimeMs = 0;
    }
    LeaveCriticalSection(&g_cs);
    return boundary;
}

FrameSignals Snapshot()
{
    FrameSignals s;
    if (!g_init) return s;
    EnterCriticalSection(&g_cs);
    s.presentRate  = g_presentRate;
    s.displayFps   = g_displayFps;
    s.ppf          = (int)(g_ppfEst + 0.5); if (s.ppf < 1) s.ppf = 1;
    s.frametimeMs  = g_emaFrametimeMs;
    s.frametimeP99 = g_maxFrametimeMs;
    s.gpuBusyPct   = g_gpuBusyPct;
    s.cpuMainPct   = g_cpuMainPct;
    s.cpuTotalPct  = g_cpuTotalPct;
    LeaveCriticalSection(&g_cs);
    return s;
}

} // namespace flcd::profiler
