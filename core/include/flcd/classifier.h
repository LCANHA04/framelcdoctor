// Rule engine: FrameSignals -> ranked diagnoses. (P1)
#pragma once
#include "profiler.h"
#include <vector>
#include <string>

namespace flcd {

enum class Problem {
    None,
    CpuBoundDriver,    // GPU idle, CPU(main) saturated, DX11 + AMD -> DXVK
    GpuBound,          // GPU ~100%
    SoftwareFpsCap,    // engine limiter pinning below capability
    FixedTimestep,     // sim speed scales with fps (speedup)
    VsyncDoubleBlock,  // 60 -> 30 from forced vsync missing vblank
    Stutter            // high frametime variance
};

struct Diagnosis {
    Problem     problem;
    double      confidence;   // 0..1
    std::string detail;       // human-readable, what fired
    std::string remedy;       // remedy id to apply/recommend
};

// TODO P1: evaluate rules over signals + game profile; return sorted by confidence.
namespace classify {
    std::vector<Diagnosis> Evaluate(const FrameSignals& s /*, const GameProfile&*/);
}

} // namespace flcd
