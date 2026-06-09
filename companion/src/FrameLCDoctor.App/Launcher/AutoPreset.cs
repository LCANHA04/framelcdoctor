using System.IO;
using System.Text.RegularExpressions;

namespace FrameLCDoctor;

public sealed record AutoPreset(string EngineName, string Path, Dictionary<string, string> Preset);

/// <summary>Generates an fps preset automatically from the engine, with no hand-authored
/// profile. Unreal Engine 4/5 is the big win: standardized config + sg.* scalability keys,
/// so any UE game gets a one-click preset.</summary>
public static class AutoPresetDetector
{
    public static AutoPreset? Detect(string exePath) => DetectUnreal(exePath);

    private static AutoPreset? DetectUnreal(string exePath)
    {
        string exe = Path.GetFileNameWithoutExtension(exePath);
        // UE shipping exe -> project name (AVGame-Win64-Shipping -> AVGame)
        string project = Regex.Replace(exe, "-(Win64|WinGDK|Win32)?-?Shipping$", "", RegexOptions.IgnoreCase);
        if (project.Length == 0) project = exe;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var sub in new[] { "WindowsNoEditor", "Windows", "WinGDK" })   // UE4 / UE5
        {
            string cfg = Path.Combine(local, project, "Saved", "Config", sub, "GameUserSettings.ini");
            if (!File.Exists(cfg)) continue;
            var preset = BuildUnrealPreset(cfg);
            if (preset.Count > 0) return new AutoPreset("Unreal Engine", cfg, preset);
        }
        return null;
    }

    // Set every scalability *Quality group to 0 (max fps), except resolution (would only blur).
    private static Dictionary<string, string> BuildUnrealPreset(string cfg)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(cfg))
        {
            var m = Regex.Match(line, @"^\s*(sg\.\w*Quality)\s*=", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string key = m.Groups[1].Value;
            if (key.Equals("sg.ResolutionQuality", StringComparison.OrdinalIgnoreCase)) continue;
            d[key] = "0";
        }
        return d;
    }
}
