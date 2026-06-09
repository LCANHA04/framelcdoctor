using System.Diagnostics;
using System.IO;

namespace FrameLCDoctor;

/// <summary>System-side fps wins that don't touch the game: power plan, game process
/// priority, background-app awareness. Especially impactful on low-end PCs/laptops.</summary>
public static class SystemOptimizer
{
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // ---- power plan ----
    public static string ActivePlanName()
    {
        try
        {
            string o = Run("powercfg", "/getactivescheme");      // "...GUID: <guid>  (Name)"
            int a = o.IndexOf('('), b = o.LastIndexOf(')');
            if (a >= 0 && b > a) return o[(a + 1)..b].Trim();
        }
        catch { }
        return "desconocido";
    }

    public static bool IsHighPerf() => IsHighName(ActivePlanName());

    public static bool IsHighName(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("alto rendimiento") || n.Contains("high performance")
            || n.Contains("maximo") || n.Contains("ultimate") || n.Contains("rendimiento maximo");
    }

    public static (bool ok, string msg) SetHighPerf()
    {
        try
        {
            Run("powercfg", "/setactive " + HighPerfGuid);
            return IsHighPerf()
                ? (true, "Plan de energia: Alto rendimiento activado.")
                : (true, "Comando enviado (revisa el plan; en algunas PCs esta oculto).");
        }
        catch (Exception ex) { return (false, "No se pudo cambiar el plan: " + ex.Message); }
    }

    // ---- game process priority ----
    public static (bool ok, string msg) BoostGame(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return (false, "No hay un juego conectado.");
        string baseName = Path.GetFileNameWithoutExtension(exeName);
        var procs = Process.GetProcessesByName(baseName);
        if (procs.Length == 0) return (false, "No encontre el proceso del juego en ejecucion.");
        int done = 0;
        foreach (var p in procs) { try { p.PriorityClass = ProcessPriorityClass.High; done++; } catch { } }
        return done > 0
            ? (true, $"Prioridad ALTA puesta a {baseName}. (vuelve a normal al cerrar el juego)")
            : (false, "No pude cambiar la prioridad (permisos).");
    }

    // ---- background memory hogs ----
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "System","Idle","Registry","Memory Compression","csrss","wininit","services","lsass","winlogon",
        "svchost","dwm","explorer","fontdrvhost","smss","taskhostw","sihost","ctfmon","RuntimeBroker",
        "SearchHost","StartMenuExperienceHost","ShellExperienceHost","FrameLCDoctor","dotnet",
    };

    // CPU usage steals fps; RAM that just sits there (idle helpers like steamwebhelper)
    // does not. Sample TotalProcessorTime between calls so only ACTIVE background apps show.
    private static Dictionary<int, (TimeSpan cpu, DateTime t)> _last = new();

    public static List<(string name, double cpuPct)> TopCpuHogs(string gameExe, int top = 5)
    {
        string game = string.IsNullOrEmpty(gameExe) ? "" : Path.GetFileNameWithoutExtension(gameExe);
        int cores = Math.Max(1, Environment.ProcessorCount);
        var now = DateTime.UtcNow;
        var cur = new Dictionary<int, (TimeSpan, DateTime)>();
        var acc = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                TimeSpan cpu = p.TotalProcessorTime;
                cur[p.Id] = (cpu, now);
                if (Skip.Contains(p.ProcessName) || p.ProcessName.Equals(game, StringComparison.OrdinalIgnoreCase)) continue;
                if (_last.TryGetValue(p.Id, out var prev))
                {
                    double ms = (now - prev.t).TotalMilliseconds;
                    if (ms > 0)
                    {
                        double pct = (cpu - prev.cpu).TotalMilliseconds / (ms * cores) * 100.0;  // % of total CPU
                        if (pct >= 1.5) acc[p.ProcessName] = acc.GetValueOrDefault(p.ProcessName) + pct;
                    }
                }
            }
            catch { }   // protected processes throw on TotalProcessorTime
        }
        _last = cur;
        return acc.OrderByDescending(kv => kv.Value).Take(top)
                  .Select(kv => (kv.Key, Math.Round(kv.Value, 0))).ToList();
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        return o;
    }
}
