using System.IO;

namespace FrameLCDoctor;

/// <summary>Per-game knowledge loaded from a profiles/*.toml by exe name. Tiny TOML
/// reader (sections + key=value); enough for our flat profiles, no NuGet needed.</summary>
public sealed class GameProfile
{
    public string Name = "";
    public string Engine = "";
    public bool FixedTimestep;
    public int Ppf;

    public static string ProfilesDir => Path.Combine(AppContext.BaseDirectory, "profiles");

    public static GameProfile? Load(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName) || !Directory.Exists(ProfilesDir)) return null;
        foreach (var file in Directory.GetFiles(ProfilesDir, "*.toml"))
        {
            var d = Parse(file);
            if (d.TryGetValue("game.exe", out var exe) && exe.Equals(exeName, StringComparison.OrdinalIgnoreCase))
            {
                return new GameProfile
                {
                    Name = d.GetValueOrDefault("game.name", exeName),
                    Engine = d.GetValueOrDefault("game.engine", ""),
                    FixedTimestep = d.GetValueOrDefault("timing.fixed_timestep", "") == "true",
                    Ppf = int.TryParse(d.GetValueOrDefault("graphics.presents_per_frame", ""), out var p) ? p : 0,
                };
            }
        }
        return null;
    }

    private static Dictionary<string, string> Parse(string file)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (var raw in File.ReadAllLines(file))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[' && line.EndsWith("]")) { section = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();
            if (val.StartsWith("{") || val.StartsWith("[")) continue;   // skip inline tables/arrays
            if (val.StartsWith("\""))
            {
                int end = val.IndexOf('"', 1);
                val = end > 0 ? val[1..end] : val.Trim('"');
            }
            else
            {
                int h = val.IndexOf('#');                                // strip trailing comment
                if (h >= 0) val = val[..h];
                val = val.Trim();
            }
            d[$"{section}.{key}"] = val;
        }
        return d;
    }
}
