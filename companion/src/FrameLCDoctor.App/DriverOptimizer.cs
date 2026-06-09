using System.Diagnostics;
using System.IO;

namespace FrameLCDoctor;

/// <summary>Driver-level optimization (tasklist item #2): forces a per-game driver profile
/// with settings the game doesn't expose. NVIDIA path via flcd_nvdrs.exe (nvapi); AMD (ADLX)
/// pending. Everything is a named per-game profile, so reverting just deletes it.</summary>
public static class DriverOptimizer
{
    public static DxvkAdvice Advise(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => new(DxvkRec.Recommended, "Si - perfil de driver por juego",
            "Forzamos en el driver NVIDIA, solo para este juego: maximo rendimiento (saca el downclock de la GPU), baja latencia (menos input lag) y 'threaded optimization' (clave en juegos OpenGL como Minecraft Java). Reversible: borra el perfil."),
        GpuVendor.Amd => new(DxvkRec.Recommended, "Si - ajustes globales (experimental)",
            "Forzamos en el driver AMD (ADLX): Anti-Lag (menos input lag) y, opcional, tope de fps a nivel driver. OJO: en AMD esto es GLOBAL (afecta todos los juegos, no solo este) - es como lo expone el SDK. Reversible. Sin validar todavia en hardware AMD."),
        GpuVendor.Intel => new(DxvkRec.NotApplicable, "Intel: no aplica",
            "No hay perfil de driver tipo NVIDIA/AMD para la GPU Intel."),
        _ => new(DxvkRec.Unknown, "GPU desconocida",
            "No pude detectar el fabricante de la GPU."),
    };

    public static bool IsNvidia(GpuVendor v) => v == GpuVendor.Nvidia;
    public static bool IsSupported(GpuVendor v) => v is GpuVendor.Nvidia or GpuVendor.Amd;

    /// <summary>Apply the driver optimization. NVIDIA = per-game profile; AMD = global. fpsCap 0 = no cap.</summary>
    public static (bool ok, string msg) Apply(GpuVendor vendor, string exeName, int fpsCap = 0)
    {
        if (vendor == GpuVendor.Amd) return ApplyAmd(fpsCap);

        var (ok, app, err) = ResolveNv(exeName, out string exe);
        if (!ok) return (false, err);
        string args = $"--apply --app \"{app}\" --maxperf --ogl-threaded --lowlatency"
                    + (fpsCap > 0 ? $" --fpscap {fpsCap}" : "");
        var (code, _) = Run(exe, args);
        if (code == 0)
            return (true, $"Perfil de driver aplicado a {app}: maximo rendimiento + baja latencia + threaded optimization"
                        + (fpsCap > 0 ? $" + tope {fpsCap} fps" : "") + ". (reversible con 'Revertir')");
        return (false, FailMsg(code));
    }

    public static (bool ok, string msg) Revert(GpuVendor vendor, string exeName)
    {
        if (vendor == GpuVendor.Amd) return RevertAmd();

        var (ok, app, err) = ResolveNv(exeName, out string exe);
        if (!ok) return (false, err);
        var (code, _) = Run(exe, $"--revert --app \"{app}\"");
        return code == 0
            ? (true, $"Perfil de driver de {app} eliminado. El driver vuelve a sus valores por defecto.")
            : (false, FailMsg(code));
    }

    private static (bool ok, string msg) ApplyAmd(int fpsCap)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "amddrs", "flcd_amddrs.exe");
        if (!File.Exists(exe)) return (false, "Falta flcd_amddrs.exe (se construye con el SDK de ADLX en la PC AMD).");
        var (code, _) = Run(exe, "--apply" + (fpsCap > 0 ? $" --fpscap {fpsCap}" : ""));
        return code == 0
            ? (true, "Driver AMD optimizado (GLOBAL): Anti-Lag activado" + (fpsCap > 0 ? $" + tope {fpsCap} fps" : "") + ". (reversible con 'Revertir')")
            : (false, FailMsg(code));
    }

    private static (bool ok, string msg) RevertAmd()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "amddrs", "flcd_amddrs.exe");
        if (!File.Exists(exe)) return (false, "Falta flcd_amddrs.exe.");
        var (code, _) = Run(exe, "--revert");
        return code == 0
            ? (true, "Driver AMD revertido: Anti-Lag y tope de fps desactivados.")
            : (false, FailMsg(code));
    }

    private static (bool ok, string app, string err) ResolveNv(string exeName, out string exe)
    {
        exe = Path.Combine(AppContext.BaseDirectory, "nvdrs", "flcd_nvdrs.exe");
        if (string.IsNullOrWhiteSpace(exeName)) return (false, "", "No hay un juego conectado.");
        if (!File.Exists(exe)) return (false, "", "Falta flcd_nvdrs.exe en la carpeta de la app.");
        // NvAPI matches by exe file name (e.g. javaw.exe for Minecraft Java).
        string app = Path.GetFileName(exeName);
        if (!app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app += ".exe";
        return (true, app, "");
    }

    private static string FailMsg(int code) => code switch
    {
        1 => "El driver rechazo el cambio (revisa que la GPU/driver coincida y este al dia).",
        2 => "Argumentos invalidos (bug interno).",
        _ => $"No se pudo aplicar (codigo {code}).",
    };

    private static (int code, string output) Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
