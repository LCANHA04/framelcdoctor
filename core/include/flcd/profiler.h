// Per-frame telemetry: the signals the classifier reasons over. (P1)
#pragma once
#include <cstdint>

namespace flcd {

struct FrameSignals {
    double   displayFps    = 0;   // unique displayed frames / s
    double   presentRate   = 0;   // Present calls / s
    int      ppf           = 1;   // present calls per displayed frame (engine quirk)
    double   frametimeMs   = 0;   // mean
    double   frametimeP99  = 0;   // stutter tail (window peak)
    double   low1Fps       = 0;   // 1% low  (1000 / p99 frametime)
    double   low01Fps      = 0;   // 0.1% low (1000 / p99.9 frametime)
    double   gpuBusyPct    = -1;  // from OS PDH or timestamp queries; -1 = unknown
    double   cpuMainPct    = -1;  // busiest single core % (single-thread signature); -1 = unknown
    double   cpuTotalPct   = -1;  // total CPU %; -1 = unknown
    bool     vsyncOn       = false;
    uint32_t swapEffect    = 0;   // DXGI_SWAP_EFFECT
    uint32_t swapFlags     = 0;
};

// P0: the present hook feeds OnPresent() once per top-level present; the profiler
// derives frametime / present-rate / displayFps. GPU/CPU occupancy (gpuBusyPct,
// cpuMainPct) is merged from the companion (PDH) over IPC and/or timestamp queries (P1).
namespace profiler {
    void Start();
    void Stop();
    // Call once per present. Returns true if this present begins a NEW displayed frame
    // (auto-detected from the inter-present gap, so engines that present N times per
    // frame are handled without a hard-coded ppf). The orchestrator paces the limiter
    // only on frame boundaries.
    bool OnPresent();
    void MergeOsMetrics(double gpuBusyPct, double cpuPeakCorePct, double cpuTotalPct);  // from companion over IPC
    FrameSignals Snapshot();
    // Copies up to maxN most-recent displayed-frame frametimes (ms, chronological) for
    // the live graph. Returns the count written.
    int CopyRecentFrametimes(double* dst, int maxN);
}

} // namespace flcd
