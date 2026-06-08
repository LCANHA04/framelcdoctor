// FrameLCDoctor companion - P0 console orchestrator.
//
// Connects to the injected core over a named pipe and prints the live frame signals.
// Next (P1): pull OS GPU%/CPU% via PDH and send them to the core, then render the
// diagnosis + Headroom Index.

namespace FrameLCDoctor;

internal static class Program
{
    private const string Version = "0.0.1";

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"FrameLCDoctor companion v{Version} (P0)");
        Console.WriteLine("Reading core signals. Ctrl+C to quit.\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var client = new CorePipeClient();
        client.OnSignals += s =>
            Console.WriteLine(
                $"display {s.DisplayFps,6:F1} fps | present {s.PresentRate,6:F1}/s (ppf {s.Ppf}) | " +
                $"frametime {s.FrametimeMs,5:F2}ms (p99 {s.FrametimeP99,5:F2}) | " +
                $"GPU {Pct(s.GpuBusyPct)} CPU {Pct(s.CpuMainPct)}");

        try { await client.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        Console.WriteLine("bye.");
        return 0;
    }

    private static string Pct(double v) => v < 0 ? "  n/a" : $"{v,4:F0}%";
}
