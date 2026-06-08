using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FrameLCDoctor.Launcher;

public sealed class SteamGame
{
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string InstallDir { get; set; } = "";   // full path (WPF binds to properties, not fields)
}

/// <summary>Enumerates installed Steam games (libraryfolders.vdf + appmanifests).</summary>
public static class SteamLibrary
{
    public static string? SteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string p && Directory.Exists(p)) return p;
        }
        catch { }
        foreach (var c in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (Directory.Exists(c)) return c;
        return null;
    }

    public static List<SteamGame> InstalledGames()
    {
        var games = new List<SteamGame>();
        string? steam = SteamPath();
        if (steam is null) return games;

        var libs = new List<string> { steam };
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        var seenLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in libs)
        {
            string norm; try { norm = Path.GetFullPath(lib).TrimEnd('\\'); } catch { continue; }
            if (!seenLibs.Add(norm)) continue;
            string apps = Path.Combine(norm, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    string t = File.ReadAllText(acf);
                    string appid = Field(t, "appid"), name = Field(t, "name"), inst = Field(t, "installdir");
                    if (inst.Length == 0) continue;
                    string full = Path.Combine(apps, "common", inst);
                    if (Directory.Exists(full))
                        games.Add(new SteamGame { AppId = appid, Name = name.Length > 0 ? name : inst, InstallDir = full });
                }
                catch { }
            }
        }
        return games
            .GroupBy(g => g.AppId).Select(grp => grp.First())   // a game installs once; dedup by appid
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Field(string t, string key)
    {
        var m = Regex.Match(t, "\"" + key + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>Candidate game exes under a dir that import a supported gfx API.</summary>
    public static List<(string exe, GfxApi api)> FindRenderExes(string dir)
    {
        var found = new List<(string, GfxApi)>();
        IEnumerable<string> exes;
        try { exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories); }
        catch { return found; }

        foreach (var exe in exes)
        {
            string low = Path.GetFileName(exe).ToLowerInvariant();
            if (low.Contains("prereq") || low.Contains("redist") || low.Contains("crashpad")
                || low.Contains("vcredist") || low.Contains("unitycrash") || low.Contains("setup")) continue;
            try
            {
                var (isX64, dlls) = PeAnalyzer.ReadImports(exe);
                if (!isX64) continue;
                var api = PeAnalyzer.DetectApi(dlls);
                if (PeAnalyzer.ProxyDllName(api) != null) found.Add((exe, api));
            }
            catch { }
        }
        // biggest exe first (usually the real game binary)
        return found.OrderByDescending(f => { try { return new FileInfo(f.Item1).Length; } catch { return 0L; } }).ToList();
    }
}
