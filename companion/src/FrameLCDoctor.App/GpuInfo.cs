using Microsoft.Win32;

namespace FrameLCDoctor;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

/// <summary>Detects the primary GPU vendor from the registry (no WMI package needed).</summary>
public static class GpuInfo
{
    public static (GpuVendor vendor, string name) Detect()
    {
        try
        {
            const string path = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var classKey = Registry.LocalMachine.OpenSubKey(path);
            if (classKey is null) return (GpuVendor.Unknown, "");

            string best = "";
            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(sub, out _)) continue;
                using var k = classKey.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is string desc && desc.Length > 0)
                {
                    var v = Classify(desc);
                    if (v != GpuVendor.Unknown) return (v, desc);   // prefer a known discrete GPU
                    if (best.Length == 0) best = desc;
                }
            }
            return (Classify(best), best);
        }
        catch { return (GpuVendor.Unknown, ""); }
    }

    private static GpuVendor Classify(string desc)
    {
        string d = desc.ToUpperInvariant();
        if (d.Contains("NVIDIA") || d.Contains("GEFORCE") || d.Contains("RTX") || d.Contains("GTX")) return GpuVendor.Nvidia;
        if (d.Contains("AMD") || d.Contains("RADEON")) return GpuVendor.Amd;
        if (d.Contains("INTEL")) return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }
}

/// <summary>Vendor + bottleneck aware DXVK recommendation (the "smart advisor").</summary>
public static class DxvkAdvisor
{
    public static string Advise(string bottleneck, GpuVendor vendor)
    {
        bool cpuBound = bottleneck is "cpu-single" or "cpu-multi";
        if (cpuBound)
        {
            return vendor switch
            {
                GpuVendor.Amd =>
                    "Cuello CPU + GPU AMD: DXVK suele dar ganancia GRANDE (baja el overhead del driver DX11). Recomendado probar.",
                GpuVendor.Nvidia =>
                    "Cuello CPU + GPU NVIDIA: el driver NV ya es multihilo, DXVK rara vez ayuda (a veces peor). Si el core saturado es game-logic (UE4), no aplica. Mejor: bajar settings de CPU (distancia/densidad/sombras dinamicas).",
                GpuVendor.Intel =>
                    "Cuello CPU + GPU Intel: DXVK puede ayudar si es overhead de driver. Proba y medi con esta herramienta.",
                _ =>
                    "Cuello CPU: DXVK ayuda si el costo es del driver DX11 (sobre todo AMD). Proba y medi.",
            };
        }
        if (bottleneck == "gpu")
            return "GPU-bound: DXVK no sube fps (el cuello es la GPU). Para mas fps, baja settings graficos / resolucion.";
        if (bottleneck == "cap")
            return "Limitado por un cap: saca/sube el cap. DXVK no aplica aca.";
        if (bottleneck == "balanced")
            return "Balanceado: poco margen. DXVK improbable que ayude.";
        return "Conecta el juego para una recomendacion.";
    }
}
