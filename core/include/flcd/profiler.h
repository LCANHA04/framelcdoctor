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

// TODO P1: Start()/Stop(); a present-hook callback feeds timestamps in; GPU/CPU
// occupancy is merged from the companion (PDH) over IPC and/or timestamp queries.
namespace profiler {
    void Start();
    void Stop();
    FrameSignals Snapshot();
}

} // namespace flcd
