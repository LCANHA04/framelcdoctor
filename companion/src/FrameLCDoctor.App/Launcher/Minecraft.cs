using System.Diagnostics;
using System.IO;

namespace FrameLCDoctor.Launcher;

/// <summary>Minecraft is a special case: it isn't on Steam, and Java Edition renders through
/// the JVM (javaw.exe) with OpenGL loaded at runtime by LWJGL - so PE import analysis sees
/// nothing and the proxy/diagnosis path can't hook it. We recognize it by name/path (or a
/// running process) and route it to the EXTERNAL tools (driver optimizer, upscaling, frame-gen)
/// which work on any window regardless of API.</summary>
public static class Minecraft
{
    public enum Edition { None, Java, Bedrock }

    public static string Friendly(Edition e) => e switch
    {
        Edition.Java    => "Minecraft (Java, OpenGL)",
        Edition.Bedrock => "Minecraft (Bedrock, D3D11)",
        _               => "",
    };

    /// <summary>Classify an exe path as a Minecraft edition (or None).</summary>
    public static Edition Classify(string exePath)
    {
        string name = Path.GetFileName(exePath).ToLowerInvariant();
        string path = exePath.ToLowerInvariant();
        if (name == "minecraft.windows.exe") return Edition.Bedrock;
        bool mcPath = path.Contains("minecraft");   // covers .minecraft, Minecraft Launcher, etc.
        if ((name == "javaw.exe" || name == "java.exe") && mcPath) return Edition.Java;
        if (name == "minecraftlauncher.exe" || name == "minecraft.exe") return Edition.Java;
        return Edition.None;
    }

    /// <summary>Find a RUNNING Minecraft and return its edition + real exe path + pid.</summary>
    public static (Edition ed, string exePath, int pid) FindRunning()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string n = p.ProcessName.ToLowerInvariant();
                if (n == "minecraft.windows")
                    return (Edition.Bedrock, SafePath(p) ?? "Minecraft.Windows.exe", p.Id);
                if ((n == "javaw" || n == "java") && p.MainWindowHandle != IntPtr.Zero)
                {
                    string title = p.MainWindowTitle ?? "";
                    string? path = SafePath(p);
                    if (title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)
                        || (path?.ToLowerInvariant().Contains("minecraft") ?? false))
                        return (Edition.Java, path ?? "javaw.exe", p.Id);
                }
            }
            catch { /* protected process */ }
        }
        return (Edition.None, "", 0);
    }

    private static string? SafePath(Process p) { try { return p.MainModule?.FileName; } catch { return null; } }
}
