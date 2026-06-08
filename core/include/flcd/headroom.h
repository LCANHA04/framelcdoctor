// Headroom Index: is the PC being squeezed to the max by the game, and is there a
// realistic path to MORE fps? Answers "am I leaving performance on the table?".
//
// Idea: find the bottleneck (the longest pole), then measure how much of the OTHER
// resources sits idle. Idle capacity on the non-bottleneck = untapped fps potential.
#pragma once
#include "profiler.h"
#include <string>

namespace flcd {

enum class Bottleneck {
    Unknown,
    Gpu,             // GPU ~maxed -> only settings/resolution buy fps
    CpuSingleThread, // one core pegged, GPU idle -> untapped GPU (DXVK / fewer draw calls)
    CpuMultiThread,  // many cores busy -> CPU-limited overall
    FrameCap,        // fps pinned by a limiter/vsync below hardware capability
    Balanced         // GPU & CPU both high and even -> near optimal
};

struct HeadroomReport {
    Bottleneck bottleneck   = Bottleneck::Unknown;
    double     gpuBusyPct    = -1;
    double     cpuPeakCorePct= -1;  // busiest single core (single-thread signature)
    double     cpuTotalPct   = -1;

    // 0..100: how "squeezed" the limiting resource is. ~100 = maxed out.
    double     utilizationIndex = 0;
    // 0..100: idle capacity on the non-bottleneck resource (the opportunity).
    double     headroomIndex    = 0;
    // Is there a realistic way to get more fps right now?
    bool       moreFpsLikely    = false;

    std::string verdict;   // e.g. "GPU-bound at 98%. Near max; lower resolution/shadows for more fps."
    std::string suggestion;// concrete next step tied to a remedy/setting
};

// TODO P1: compute from FrameSignals (+ OS metrics merged over IPC from the companion).
namespace headroom {
    HeadroomReport Assess(const FrameSignals& s);
}

} // namespace flcd
