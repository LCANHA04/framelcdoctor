using System.IO;
using System.Text.RegularExpressions;

namespace FrameLCDoctor;

public sealed record AutoPreset(string EngineName, string Path, Dictionary<string, string> Preset);

/// <summary>Generates an fps preset automatically from the engine, with no hand-authored
/// profile. Unreal Engine 4/5 is the big win: standardized config + sg.* scalability keys,
/// so any UE game gets a one-click preset.</summary>
public static class AutoPresetDetector
{
    // Scalability groups that cost mostly CPU (draw calls / sim) vs the rest (GPU).
    private static readonly HashSet<string> CpuKeys = new(StringComparer.OrdinalIgnoreCase)
    { "sg.ViewDistanceQuality", "sg.FoliageQuality", "sg.EffectsQuality" };

    /// <param name="bottleneck">live diagnosis (cpu-single/cpu-multi/gpu/...) to tailor the
    /// preset; empty = generic max-fps (lower everything).</param>
    public static AutoPreset? Detect(string exePath, string bottleneck = "") => DetectUnreal(exePath, bottleneck);

    private static AutoPreset? DetectUnreal(string exePath, string bottleneck)
    {
        string exe = Path.GetFileNameWithoutExtension(exePath);
        string project = Regex.Replace(exe, "-(Win64|WinGDK|Win32)?-?Shipping$", "", RegexOptions.IgnoreCase);
        if (project.Length == 0) project = exe;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var sub in new[] { "WindowsNoEditor", "Windows", "WinGDK" })   // UE4 / UE5
        {
            string cfg = Path.Combine(local, project, "Saved", "Config", sub, "GameUserSettings.ini");
            if (!File.Exists(cfg)) continue;
            var preset = BuildUnrealPreset(cfg, bottleneck);
            if (preset.Count > 0) return new AutoPreset("Unreal Engine", cfg, preset);
        }
        return null;
    }

    // Tailor by bottleneck: CPU-bound -> lower only CPU-side groups (keep the visuals you
    // can afford); GPU-bound -> lower only GPU-side; otherwise lower everything.
    // Resolution is never touched (use upscaling instead).
    private static Dictionary<string, string> BuildUnrealPreset(string cfg, string bottleneck)
    {
        bool cpu = bottleneck is "cpu-single" or "cpu-multi";
        bool gpu = bottleneck == "gpu";

        var all = new List<string>();
        foreach (var line in File.ReadAllLines(cfg))
        {
            var m = Regex.Match(line, @"^\s*(sg\.\w*Quality)\s*=", RegexOptions.IgnoreCase);
            if (m.Success && !m.Groups[1].Value.Equals("sg.ResolutionQuality", StringComparison.OrdinalIgnoreCase))
                all.Add(m.Groups[1].Value);
        }

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in all)
        {
            bool isCpu = CpuKeys.Contains(key);
            if (cpu && !isCpu) continue;   // CPU-bound -> only CPU keys
            if (gpu && isCpu) continue;    // GPU-bound -> only GPU keys
            d[key] = "0";
        }
        if (d.Count == 0) foreach (var key in all) d[key] = "0";   // fallback: lower everything
        return d;
    }
}
