using System.IO;

namespace FrameLCDoctor;

/// <summary>Identifies a game's engine from its files. Used to show the engine and to
/// decide auto-preset support (Unreal = file-based config we can edit; Unity stores
/// settings in the registry, so no safe auto-preset yet).</summary>
public static class EngineDetector
{
    public static string Detect(string exePath)
    {
        try
        {
            string dir = Path.GetDirectoryName(exePath) ?? "";
            string exe = Path.GetFileNameWithoutExtension(exePath);

            // Unity: UnityPlayer.dll / GameAssembly.dll beside the exe, or a <exe>_Data folder
            if (File.Exists(Path.Combine(dir, "UnityPlayer.dll"))
                || File.Exists(Path.Combine(dir, "GameAssembly.dll"))
                || Directory.Exists(Path.Combine(dir, exe + "_Data")))
                return "Unity";

            // Unreal: shipping exe, or an Engine\Binaries folder up the tree
            if (exe.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase)) return "Unreal Engine";
            var d = new DirectoryInfo(dir);
            for (int i = 0; i < 4 && d != null; i++, d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "Engine", "Binaries"))) return "Unreal Engine";

            // a few more by signature dll beside the exe
            if (File.Exists(Path.Combine(dir, "GameOverlayRenderer64.dll"))) { } // (Steam overlay, not an engine)
            if (File.Exists(Path.Combine(dir, "bink2w64.dll")) && File.Exists(Path.Combine(dir, "oo2core_9_win64.dll")))
                return ""; // common middleware, inconclusive

            return "";
        }
        catch { return ""; }
    }

    public static bool AutoPresetSupported(string engine) =>
        engine.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase);
}
