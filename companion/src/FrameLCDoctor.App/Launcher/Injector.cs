using System.Diagnostics;
using System.IO;

namespace FrameLCDoctor.Launcher;

/// <summary>Runs flcd_inject.exe to load the core into a running game that we can't reach with
/// a beside-the-exe proxy (OpenGL games like Minecraft Java present through gdi32!SwapBuffers,
/// a KnownDLL). Once injected, the core hooks the present + starts its pipe, so the panel shows
/// live fps/bottleneck exactly like the proxy path.</summary>
public static class Injector
{
    private static string ExeDir => Path.Combine(AppContext.BaseDirectory, "inject");

    public static bool Available =>
        File.Exists(Path.Combine(ExeDir, "flcd_inject.exe")) &&
        File.Exists(Path.Combine(ExeDir, "flcd_core.dll"));

    public static (bool ok, string msg) Inject(int pid)
    {
        if (pid <= 0) return (false, "No tengo el proceso objetivo.");
        string inj = Path.Combine(ExeDir, "flcd_inject.exe");
        if (!Available) return (false, "Falta flcd_inject.exe / flcd_core.dll en la carpeta de la app.");
        try
        {
            var psi = new ProcessStartInfo(inj, $"--pid {pid}")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(12000);
            if (p.ExitCode == 0)
                return (true, "Diagnostico activado (core inyectado). Abri el panel: en unos segundos vas a ver fps y cuello en vivo.");
            // surface the helper's step/status for diagnosis
            string detail = o.Contains("step=") ? o.Replace("\r", " ").Replace("\n", " ").Trim() : "";
            return (false, "No pude inyectar el diagnostico. " + detail);
        }
        catch (Exception ex) { return (false, "No pude inyectar: " + ex.Message); }
    }
}
