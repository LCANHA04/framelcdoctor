// FrameLCDoctor companion - P1 console.
//
// Connects to the injected core, feeds it OS GPU%/CPU% (PDH), and prints the live
// signals plus the Headroom Index verdict computed in the core.

namespace FrameLCDoctor;

internal static class Program
{
    private const string Version = "0.1.0";

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"FrameLCDoctor companion v{Version} (P1)");
        Console.WriteLine("Feeding OS metrics + reading core diagnosis. Ctrl+C to quit.\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var client = new CorePipeClient();
        string lastBottleneck = "";
        client.OnSignals += s =>
        {
            Console.WriteLine(
                $"display {s.DisplayFps,5:F0} fps | frametime {s.FrametimeMs,5:F1}ms | " +
                $"GPU {Pct(s.GpuBusyPct)} CPUpk {Pct(s.CpuMainPct)} CPUtot {Pct(s.CpuTotalPct)} | " +
                $"[{s.Bottleneck}] util {s.UtilizationIndex,3:F0} headroom {s.HeadroomIndex,3:F0}");
            if (s.Bottleneck != lastBottleneck && s.Verdict.Length > 0)
            {
                lastBottleneck = s.Bottleneck;
                Console.WriteLine($"  >> {s.Verdict}");
                Console.WriteLine($"     {s.Suggestion}");
                Console.WriteLine($"     mas fps posible: {(s.MoreFpsLikely ? "SI" : "no facilmente")}");
            }
        };

        var pipeTask = client.RunAsync(cts.Token);
        var metricsTask = MetricsLoop(client, cts.Token);

        try { await Task.WhenAll(pipeTask, metricsTask); }
        catch (OperationCanceledException) { }

        Console.WriteLine("bye.");
        return 0;
    }

    private static async Task MetricsLoop(CorePipeClient client, CancellationToken ct)
    {
        using var metrics = new SysMetrics();
        if (!metrics.Open()) { Console.WriteLine("WARN: PDH metrics unavailable."); return; }

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { break; }
            var (gpu, cpuPeak, cpuTotal) = metrics.Sample();
            if (gpu < 0 && cpuPeak < 0 && cpuTotal < 0) continue;
            // Invariant culture: the core parses with '.' decimals (atof / C locale).
            string json = FormattableString.Invariant(
                $"{{\"gpu\":{gpu:F1},\"cpuPeak\":{cpuPeak:F1},\"cpuTotal\":{cpuTotal:F1}}}");
            await client.SendAsync(json, ct);
        }
    }

    private static string Pct(double v) => v < 0 ? " n/a" : $"{v,4:F0}%";
}
