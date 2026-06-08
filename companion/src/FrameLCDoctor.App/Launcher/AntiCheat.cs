using System.IO;

namespace FrameLCDoctor.Launcher;

/// <summary>Conservative anti-cheat detector. Injecting into an anti-cheat-protected
/// (online) game can get the account banned, so the launcher hard-blocks on a hit.</summary>
public static class AntiCheat
{
    private static readonly string[] DirNeedles =
        { "easyanticheat", "battleye", "beservice", "anticheat", "punkbuster", "xigncode", "vanguard", "denuvo" };

    private static readonly string[] FileNeedles =
        { "easyanticheat", "battleye", "beservice", "start_protected_game", "anticheat", "punkbuster", "vgc", "xigncode" };

    public static (bool detected, string reason) Scan(string exePath, IEnumerable<string> importedDlls)
    {
        // 1) imports of the exe itself
        foreach (var d in importedDlls)
        {
            string dl = d.ToLowerInvariant();
            if (dl.Contains("easyanticheat") || dl.Contains("battleye") || dl.Contains("anticheat"))
                return (true, $"el exe importa {d} (anti-cheat)");
        }

        // 2) files/folders near the game (depth-limited)
        try
        {
            string? dir = Path.GetDirectoryName(exePath);
            for (int up = 0; up < 3 && dir != null; up++, dir = Path.GetDirectoryName(dir))
            {
                var hit = ScanDir(dir, 2);
                if (hit != null) return (true, hit);
            }
        }
        catch { /* ignore IO */ }

        return (false, "");
    }

    private static string? ScanDir(string dir, int depth)
    {
        try
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(sub).ToLowerInvariant();
                if (DirNeedles.Any(n => name.Contains(n))) return $"carpeta '{Path.GetFileName(sub)}' (anti-cheat)";
                if (depth > 0) { var h = ScanDir(sub, depth - 1); if (h != null) return h; }
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (FileNeedles.Any(n => name.Contains(n))) return $"archivo '{Path.GetFileName(f)}' (anti-cheat)";
            }
        }
        catch { /* access denied etc. */ }
        return null;
    }
}
