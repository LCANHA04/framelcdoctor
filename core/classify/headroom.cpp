// Headroom Index (P1): find the bottleneck and how much of the other resources is
// idle = untapped fps. Answers "am I maxing the PC / can I get more fps?".
#include "flcd/headroom.h"

namespace flcd::headroom {
namespace {
double clamp01x100(double v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }
}

HeadroomReport Assess(const FrameSignals& s)
{
    HeadroomReport r;
    r.gpuBusyPct     = s.gpuBusyPct;
    r.cpuPeakCorePct = s.cpuMainPct;
    r.cpuTotalPct    = s.cpuTotalPct;

    const bool haveGpu = s.gpuBusyPct >= 0;
    const bool haveCpu = s.cpuMainPct >= 0;
    if (!haveGpu && !haveCpu) {
        r.bottleneck = Bottleneck::Unknown;
        r.verdict    = "Sin metricas de GPU/CPU todavia.";
        r.suggestion = "Conecta el companion para medir el aprovechamiento.";
        return r;
    }

    const double gpu      = haveGpu ? s.gpuBusyPct : 0;
    const double cpuPeak  = haveCpu ? s.cpuMainPct : 0;
    const double cpuTotal = s.cpuTotalPct >= 0 ? s.cpuTotalPct : cpuPeak;

    // Thresholds (tunable; later per-profile). No gaps: every (gpu,cpu) lands somewhere.
    const double GPU_MAX = 85;   // GPU considered maxed
    const double CPU_BND = 80;   // a core (or total) this busy => CPU is the pole
    const double GPU_IDLE = 70;  // GPU below this while CPU busy => clearly CPU-bound
    const double LOW = 65;       // nothing near max => cap / light scene

    if (gpu >= GPU_MAX && gpu >= cpuPeak) {
        r.bottleneck       = Bottleneck::Gpu;
        r.utilizationIndex = gpu;
        r.headroomIndex    = clamp01x100(100 - cpuTotal);   // idle CPU = little to gain
        r.moreFpsLikely    = false;
        r.verdict          = "GPU-bound: la placa esta al maximo.";
        r.suggestion       = "Para mas fps: bajar resolucion / sombras / efectos. Casi no hay margen 'gratis'.";
    } else if (cpuPeak >= CPU_BND && gpu < GPU_IDLE) {
        r.bottleneck       = Bottleneck::CpuSingleThread;
        r.utilizationIndex = cpuPeak;
        r.headroomIndex    = clamp01x100(100 - gpu);        // idle GPU = untapped fps
        r.moreFpsLikely    = true;
        r.verdict          = "CPU-bound (un solo core saturado), GPU ociosa: hay fps sin aprovechar.";
        r.suggestion       = "Reducir overhead CPU: DXVK (sobre todo en AMD DX11), menos draw-calls (sombras/LOD/poblacion).";
    } else if (cpuTotal >= CPU_BND && gpu < GPU_IDLE) {
        r.bottleneck       = Bottleneck::CpuMultiThread;
        r.utilizationIndex = cpuTotal;
        r.headroomIndex    = clamp01x100(100 - gpu);
        r.moreFpsLikely    = true;
        r.verdict          = "CPU-bound (varios cores), GPU ociosa.";
        r.suggestion       = "Bajar settings que cargan CPU (fisica/IA/draw-calls); cerrar apps de fondo.";
    } else if (gpu < LOW && cpuPeak < CPU_BND) {
        r.bottleneck       = Bottleneck::FrameCap;
        r.utilizationIndex = (gpu > cpuPeak ? gpu : cpuPeak);
        r.headroomIndex    = clamp01x100(100 - (gpu > cpuPeak ? gpu : cpuPeak));
        r.moreFpsLikely    = true;
        r.verdict          = "Ni GPU ni CPU al maximo: fps limitado por un cap (vsync/limitador) o escena liviana.";
        r.suggestion       = "Si hay un cap, subirlo/sacarlo. Ojo: motores de timestep fijo se aceleran arriba de su fps base.";
    } else {
        r.bottleneck       = Bottleneck::Balanced;
        r.utilizationIndex = (gpu > cpuTotal ? gpu : cpuTotal);
        r.headroomIndex    = clamp01x100(100 - r.utilizationIndex);
        r.moreFpsLikely    = false;
        r.verdict          = "Balanceado: GPU y CPU bien usadas, cerca del maximo.";
        r.suggestion       = "Poco margen libre. Ganancias solo bajando settings.";
    }
    return r;
}

} // namespace flcd::headroom
