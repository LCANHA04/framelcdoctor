using System.IO;
using System.Text.RegularExpressions;

namespace FrameLCDoctor;

/// <summary>Applies / restores a per-game graphics preset to the game's own config file
/// (flat "key = value" ini). Backs up the original so it's fully reversible.</summary>
public static class ConfigPreset
{
    private const string BackupSuffix = ".flcd-bak";

    public static string? ResolvePath(GameProfile p)
    {
        if (string.IsNullOrEmpty(p.ConfigPath)) return null;
        string root = p.ConfigBase.ToLowerInvariant() switch
        {
            "documents"   => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "userprofile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "appdata"     => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "localappdata"=> Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _             => "",
        };
        string rel = p.ConfigPath.Replace('/', '\\');
        string full = string.IsNullOrEmpty(root) ? rel : Path.Combine(root, rel);
        return full.Contains('*') ? ResolveGlob(full) : (File.Exists(full) ? full : null);
    }

    // Handles a single "*" directory segment (e.g. ...\Steam\*\drawing_settings.ini).
    private static string? ResolveGlob(string full)
    {
        var parts = full.Split('\\');
        int star = Array.FindIndex(parts, s => s.Contains('*'));
        if (star <= 0) return null;
        string baseDir = string.Join('\\', parts[..star]);
        string pattern = parts[star];
        string rest = string.Join('\\', parts[(star + 1)..]);
        if (!Directory.Exists(baseDir)) return null;
        foreach (var dir in Directory.GetDirectories(baseDir, pattern))
        {
            string cand = Path.Combine(dir, rest);
            if (File.Exists(cand)) return cand;
        }
        return null;
    }

    // Human-readable list of exactly what the preset changes (full transparency).
    public static List<string> DescribeChanges(GameProfile p)
        => p.PresetFps.Select(kv => $"{Friendly(kv.Key)} ({kv.Key})  ->  {FriendlyVal(kv.Key, kv.Value)}").ToList();

    private static string Friendly(string key) => key switch
    {
        "shadowQuality"            => "Sombras",
        "isReflectionEnable"       => "Reflejos",
        "isAmbientOcclusionEnable" => "Oclusion ambiental",
        "isAntiAliasingEnable"     => "Antialiasing (AA)",
        "isDynamicResolutionEnable"=> "Resolucion dinamica",
        "textureFilter"            => "Filtro de texturas",
        "lodType"                  => "Nivel de detalle (LOD)",
        // Unreal Engine scalability groups
        "sg.ViewDistanceQuality"   => "Distancia de dibujado",
        "sg.ShadowQuality"         => "Sombras",
        "sg.AntiAliasingQuality"   => "Antialiasing (AA)",
        "sg.PostProcessQuality"    => "Post-procesado",
        "sg.TextureQuality"        => "Texturas",
        "sg.EffectsQuality"        => "Efectos",
        "sg.FoliageQuality"        => "Vegetacion / follaje",
        "sg.ResolutionQuality"     => "Escala de resolucion",
        _                          => key,
    };

    private static string FriendlyVal(string key, string v)
    {
        if (key.StartsWith("is", StringComparison.OrdinalIgnoreCase) && key.Contains("Enable"))
            return v.Trim() == "0" ? "desactivado" : "activado";
        if (key.StartsWith("sg.", StringComparison.OrdinalIgnoreCase))
            return v.Trim() == "0" ? "minimo (0)" : v;
        if (key == "shadowQuality" && v.Trim() == "0") return "0 (minimo)";
        return v;
    }

    public static bool IsApplied(GameProfile p)
    {
        var path = ResolvePath(p);
        return path != null && File.Exists(path + BackupSuffix);
    }

    public static (bool ok, string msg) Apply(GameProfile p)
    {
        var path = ResolvePath(p);
        if (path == null) return (false, "No encontre el archivo de config del juego (¿lo abriste al menos una vez?).");
        try
        {
            string bak = path + BackupSuffix;
            if (!File.Exists(bak)) File.Copy(path, bak);   // first apply = backup original

            string[] lines = File.ReadAllLines(path);
            foreach (var kv in p.PresetFps)
            {
                // write "key=value" with no spaces: UE4's ini parser is strict about it,
                // and flat inis (NieR) read it fine too.
                var rx = new Regex($@"^\s*{Regex.Escape(kv.Key)}\s*=.*$", RegexOptions.IgnoreCase);
                bool found = false;
                for (int i = 0; i < lines.Length; i++)
                    if (rx.IsMatch(lines[i])) { lines[i] = $"{kv.Key}={kv.Value}"; found = true; break; }
                if (!found) lines = lines.Append($"{kv.Key}={kv.Value}").ToArray();
            }
            File.WriteAllLines(path, lines);
            return (true, $"Preset FPS aplicado ({p.PresetFps.Count} ajustes). Backup guardado. Arranca el juego.");
        }
        catch (Exception ex) { return (false, "Error aplicando: " + ex.Message); }
    }

    public static (bool ok, string msg) Restore(GameProfile p)
    {
        var path = ResolvePath(p);
        if (path == null) return (false, "No encontre el archivo de config.");
        string bak = path + BackupSuffix;
        if (!File.Exists(bak)) return (false, "No hay backup (no se aplico preset).");
        try
        {
            File.Copy(bak, path, overwrite: true);
            File.Delete(bak);
            return (true, "Config original restaurada.");
        }
        catch (Exception ex) { return (false, "Error restaurando: " + ex.Message); }
    }
}
