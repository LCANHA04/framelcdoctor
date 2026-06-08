// Per-frame telemetry: the signals the classifier reasons over. (P1)
#pragma once
#include <cstdint>

namespace flcd {

struct FrameSignals {
    double   displayFps    = 0;   // unique displayed frames / s
    double   presentRate   = 0;   // Present calls / s
    int      ppf           = 1;   // present calls per displayed frame (engine quirk)
    double   frametimeMs   = 0;   // mean
    double   frametimeP99  = 0;   // stutter tail
    double   gpuBusyPct    = -1;  // from OS PDH or timestamp queries; -1 = unknown
    double   cpuMainPct    = -1;  // render-thread occupancy; -1 = unknown
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
    void OnPresent();             // call once per present (from the hook orchestrator)
    void SetPpf(int ppf);         // presents per displayed frame (from game profile)
    void MergeOsMetrics(double gpuBusyPct, double cpuMainPct);  // from companion over IPC
    FrameSignals Snapshot();
}

} // namespace flcd
