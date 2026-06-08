// A remedy = detector predicate + apply + revert. Reversible by contract. (P2+)
#pragma once
#include "profiler.h"
#include <string>

namespace flcd {

struct IRemedy {
    virtual ~IRemedy() = default;
    virtual const char* Id() const = 0;                 // e.g. "frame-limiter"
    virtual bool        Applies(const FrameSignals&) const = 0;
    virtual bool        Apply() = 0;                    // install/enable
    virtual void        Revert() = 0;                   // undo cleanly
    virtual std::string Describe() const = 0;           // what it will do
};

// Planned remedies (see DESIGN.md S3):
//   frame-limiter   (P2)  QPC cap at Present  [ported from NieR limiter]
//   sync-override   (P2)  force SyncInterval / tearing
//   dxvk-advisor    (P3)  detect AMD DX11 CPU-bound -> recommend/install DXVK
//   timestep-fix    (P4)  scale fixed dt by real frametime (profile-driven)
//   frame-pacing    (P5)  reduce microstutter / latency

} // namespace flcd
