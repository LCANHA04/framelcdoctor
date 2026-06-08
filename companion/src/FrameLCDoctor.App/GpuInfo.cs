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

public enum DxvkRec { Unknown, Recommended, Unlikely, NotApplicable }

/// <summary>A plain-language DXVK recommendation: a verdict anyone can read + the why.</summary>
public record DxvkAdvice(DxvkRec Level, string Verdict, string Reason);

/// <summary>Vendor + bottleneck aware DXVK advisor, phrased for non-technical users.</summary>
public static class DxvkAdvisor
{
    public static DxvkAdvice Advise(string bottleneck, GpuVendor vendor)
    {
        bool cpuBound = bottleneck is "cpu-single" or "cpu-multi";
        if (cpuBound)
        {
            return vendor switch
            {
                GpuVendor.Amd => new(DxvkRec.Recommended,
                    "Si - probablemente te de bastantes mas FPS",
                    "Tu juego esta frenado por el procesador (CPU) y tenes una placa AMD. En las AMD, DirectX hace trabajar de mas a la CPU; DXVK lo evita y suele subir los fps. Vale la pena probar."),
                GpuVendor.Nvidia => new(DxvkRec.Unlikely,
                    "Probablemente no cambie nada",
                    "Estas frenado por el procesador, pero con NVIDIA el DirectX ya viene bien resuelto. Aca el limite suele ser la logica del juego, que DXVK no toca. Para mas fps conviene bajar opciones que cargan la CPU (distancia de dibujado, densidad de objetos, sombras)."),
                GpuVendor.Intel => new(DxvkRec.Unknown,
                    "Quizas - proba y compara aca",
                    "Estas frenado por el procesador. Con placas Intel DXVK puede ayudar o no. Instalalo, jugá un rato y compará los fps en este panel."),
                _ => new(DxvkRec.Unknown,
                    "Quizas - proba y compara aca",
                    "Estas frenado por el procesador. DXVK ayuda sobre todo en placas AMD. Probalo y medí los fps aca."),
            };
        }
        if (bottleneck == "gpu")
            return new(DxvkRec.NotApplicable, "No - el limite es la placa de video",
                "Tu placa de video esta al maximo. DXVK no crea fps cuando el cuello es la GPU. Para mas fps, baja la calidad grafica o la resolucion.");
        if (bottleneck == "cap")
            return new(DxvkRec.NotApplicable, "No - tenes un tope de fps puesto",
                "Tus fps estan limitados por un tope (vsync o un limite), no por el hardware. Saca el tope para mas fps; DXVK no cambia esto.");
        if (bottleneck == "balanced")
            return new(DxvkRec.NotApplicable, "Casi no hay nada para ganar",
                "La placa y el procesador estan bien aprovechados. DXVK dificilmente cambie algo.");
        return new(DxvkRec.Unknown, "Conecta un juego",
            "Abri un juego con FrameLCDoctor instalado para ver si DXVK te conviene.");
    }
}
