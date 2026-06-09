using System.IO;
using System.Text.Json;

namespace FrameLCDoctor;

/// <summary>Remembers the last diagnosed bottleneck per game (exe). The dashboard records
/// it live; the launcher (game closed) reads it to tailor the auto-preset to your case.</summary>
public static class BottleneckMemory
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameLCDoctor");
    private static string JsonPath => Path.Combine(Dir, "bottlenecks.json");

    public static void Record(string exe, string bottleneck)
    {
        if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(bottleneck) || bottleneck == "unknown") return;
        try
        {
            var d = Load();
            if (d.TryGetValue(exe, out var cur) && cur == bottleneck) return;   // no change
            d[exe] = bottleneck;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(JsonPath, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    public static string Get(string exe)
    {
        try { return Load().TryGetValue(exe, out var b) ? b : ""; } catch { return ""; }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(JsonPath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(JsonPath))
                       ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
