using System.Diagnostics;
using System.IO;

namespace FrameLCDoctor;

/// <summary>Drives the external window upscaler (flcd_upscaler.exe). Only worth it when
/// GPU-bound (fewer pixels -> more fps); useless when CPU-bound (GPU already idle).</summary>
public static class Upscaler
{
    public static DxvkAdvice Advise(string bottleneck) => bottleneck switch
    {
        "gpu" => new(DxvkRec.Recommended, "Si - te conviene",
            "Estas GPU-bound (la placa al maximo). Poné el juego en ventana a baja resolucion y activá el upscaling: la GPU dibuja menos pixeles y subis fps, manteniendo la pantalla completa."),
        "cpu-single" or "cpu-multi" => new(DxvkRec.Unlikely, "No te va a ayudar",
            "Estas CPU-bound: la GPU ya esta ociosa, asi que dibujar menos pixeles no sube los fps. Mira 'Como ganar FPS' (bajar settings de CPU)."),
        "cap" => new(DxvkRec.NotApplicable, "No aplica - tenes un tope",
            "Tus fps estan topeados, no limitados por la GPU. Saca el cap primero; el upscaling no cambia esto."),
        "balanced" => new(DxvkRec.NotApplicable, "Margen chico",
            "GPU y CPU bien usadas; el upscaling daria poca ganancia."),
        _ => new(DxvkRec.Unknown, "Conecta un juego",
            "Abri un juego con FrameLCDoctor para ver si el upscaling te conviene."),
    };

    /// <summary>Frame-gen advice. Opposite of upscaling: it COSTS GPU (optical flow + warp),
    /// so it only pays off when the GPU has spare time. GPU-bound = counterproductive.</summary>
    public static DxvkAdvice AdviseFrameGen(string bottleneck, double gpuPct)
    {
        bool known = bottleneck is "gpu" or "cpu-single" or "cpu-multi" or "cap" or "balanced";
        if (!known)
            return new(DxvkRec.Unknown, "Conecta un juego",
                "Abri un juego para ver si te conviene generar frames.");
        string g = gpuPct.ToString("F0");
        if (gpuPct >= 90)
            return new(DxvkRec.Unlikely, "No te conviene",
                $"Tu GPU esta al maximo ({g}%): generar frames le roba tiempo a los reales, asi que bajarian los fps reales para darte interpolados. El frame-gen necesita GPU libre. (El upscaling SI sirve cuando estas GPU-bound.)");
        if (gpuPct >= 75)
            return new(DxvkRec.Unlikely, "Margen justo",
                $"Tu GPU esta bastante cargada ({g}%). Podes probar, pero el costo de interpolar puede comerse la ganancia. Rinde mejor con mas GPU libre.");
        return new(DxvkRec.Recommended, "Si - tenes margen",
            $"Tu GPU tiene margen libre ({g}% usado): podes gastar esa GPU ociosa en interpolar frames y ganar fluidez sin sacrificar los reales. Ideal si estas CPU-bound o por debajo del refresh de tu monitor.");
    }

    public static (bool ok, string msg) Launch(string exeName, bool frameGen = false)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return (false, "No hay un juego conectado.");
        var proc = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName))
                          .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (proc == null) return (false, "No encontre la ventana del juego en ejecucion.");

        string exe = Path.Combine(AppContext.BaseDirectory, "upscaler", "flcd_upscaler.exe");
        if (!File.Exists(exe)) return (false, "Falta flcd_upscaler.exe en la carpeta de la app.");
        try
        {
            string args = $"--hwnd {(long)proc.MainWindowHandle}" + (frameGen ? " --framegen" : "");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
            string fg = frameGen ? " PAGE UP activa/desactiva la generacion de frames (2x)." : "";
            return (true, "Upscaling activado. Pone el juego en VENTANA a baja resolucion (ej. 1280x720). En el upscaler: END sale, HOME activa el cursor para menus." + fg);
        }
        catch (Exception ex) { return (false, "No pude lanzar el upscaler: " + ex.Message); }
    }
}
