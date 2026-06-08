// Frame profiler (P0): derive frametime / present-rate / displayFps from present calls.
#ifndef NOMINMAX
#  define NOMINMAX   // keep windows.h from shadowing std::max/std::min
#endif
#include "flcd/profiler.h"
#include <windows.h>
#include <algorithm>

namespace flcd::profiler {
namespace {

CRITICAL_SECTION g_cs;
bool             g_init = false;
LARGE_INTEGER    g_freq{};
LONGLONG         g_lastPresent = 0;
LONGLONG         g_windowStart = 0;
int              g_windowCount = 0;
int              g_ppf = 1;

double g_emaFrametimeMs = 0;   // present-to-present, EMA
double g_maxFrametimeMs = 0;   // window peak (rough p99 stand-in)
double g_presentRate    = 0;   // presents/s (last window)
double g_gpuBusyPct      = -1;
double g_cpuMainPct      = -1;   // busiest single core
double g_cpuTotalPct     = -1;

double TicksToMs(LONGLONG t) { return (double)t * 1000.0 / g_freq.QuadPart; }

} // namespace

void Start()
{
    if (!g_init) { InitializeCriticalSection(&g_cs); g_init = true; }
    QueryPerformanceFrequency(&g_freq);
    g_lastPresent = g_windowStart = 0;
    g_windowCount = 0;
}

void Stop() {}

void SetPpf(int ppf) { g_ppf = ppf < 1 ? 1 : ppf; }

void MergeOsMetrics(double gpuBusyPct, double cpuPeakCorePct, double cpuTotalPct)
{
    EnterCriticalSection(&g_cs);
    if (gpuBusyPct     >= 0) g_gpuBusyPct  = gpuBusyPct;
    if (cpuPeakCorePct >= 0) g_cpuMainPct  = cpuPeakCorePct;
    if (cpuTotalPct    >= 0) g_cpuTotalPct = cpuTotalPct;
    LeaveCriticalSection(&g_cs);
}

void OnPresent()
{
    if (!g_init) return;
    LARGE_INTEGER now; QueryPerformanceCounter(&now);
    EnterCriticalSection(&g_cs);
    if (g_lastPresent) {
        double dtMs = TicksToMs(now.QuadPart - g_lastPresent);
        g_emaFrametimeMs = g_emaFrametimeMs ? (g_emaFrametimeMs * 0.95 + dtMs * 0.05) : dtMs;
        g_maxFrametimeMs = std::max(g_maxFrametimeMs, dtMs);
    }
    g_lastPresent = now.QuadPart;

    if (!g_windowStart) g_windowStart = now.QuadPart;
    if (++g_windowCount >= 120) {
        double sec = TicksToMs(now.QuadPart - g_windowStart) / 1000.0;
        if (sec > 0) g_presentRate = g_windowCount / sec;
        g_windowStart = now.QuadPart; g_windowCount = 0; g_maxFrametimeMs = 0;
    }
    LeaveCriticalSection(&g_cs);
}

FrameSignals Snapshot()
{
    FrameSignals s;
    if (!g_init) return s;
    EnterCriticalSection(&g_cs);
    s.presentRate  = g_presentRate;
    s.ppf          = g_ppf;
    s.displayFps   = g_presentRate / g_ppf;
    s.frametimeMs  = g_emaFrametimeMs;
    s.frametimeP99 = g_maxFrametimeMs;
    s.gpuBusyPct   = g_gpuBusyPct;
    s.cpuMainPct   = g_cpuMainPct;
    s.cpuTotalPct  = g_cpuTotalPct;
    LeaveCriticalSection(&g_cs);
    return s;
}

} // namespace flcd::profiler
