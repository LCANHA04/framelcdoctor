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

    public static (bool ok, string msg) Launch(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return (false, "No hay un juego conectado.");
        var proc = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName))
                          .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (proc == null) return (false, "No encontre la ventana del juego en ejecucion.");

        string exe = Path.Combine(AppContext.BaseDirectory, "upscaler", "flcd_upscaler.exe");
        if (!File.Exists(exe)) return (false, "Falta flcd_upscaler.exe en la carpeta de la app.");
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--hwnd {(long)proc.MainWindowHandle}") { UseShellExecute = false });
            return (true, "Upscaling activado. Pone el juego en VENTANA a baja resolucion (ej. 1280x720). En el upscaler: END sale, HOME activa el cursor para menus.");
        }
        catch (Exception ex) { return (false, "No pude lanzar el upscaler: " + ex.Message); }
    }
}
