using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FrameLCDoctor;

/// <summary>
/// Connects to the injected core's named pipe, reads streamed FrameSignals (JSON lines),
/// and (P1) sends OS metrics / commands back.
/// </summary>
public sealed class CorePipeClient
{
    private const string PipeName = "framelcdoctor";   // matches flcd::ipc::kPipeName

    public event Action<FrameSignals>? OnSignals;

    private volatile NamedPipeClientStream? _pipe;

    /// <summary>Send a JSON line to the core (e.g. OS metrics). No-op if disconnected.</summary>
    public async Task SendAsync(string json, CancellationToken ct)
    {
        var pipe = _pipe;
        if (pipe is null || !pipe.IsConnected) return;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
            await pipe.WriteAsync(bytes, ct);
        }
        catch { /* disconnected mid-write */ }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                Console.WriteLine("connecting to core...");
                await pipe.ConnectAsync(2000, ct);
                _pipe = pipe;
                Console.WriteLine("connected.");

                var buf = new byte[4096];
                var sb = new StringBuilder();
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    int n = await pipe.ReadAsync(buf, ct);
                    if (n == 0) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, n));

                    int nl;
                    while ((nl = IndexOfNewline(sb)) >= 0)
                    {
                        string line = sb.ToString(0, nl).Trim();
                        sb.Remove(0, nl + 1);
                        if (line.Length > 0) Dispatch(line);
                    }
                }
            }
            catch (TimeoutException) { /* core not up yet */ }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"pipe error: {ex.Message}"); }
            finally { _pipe = null; }

            await Task.Delay(1000, ct);
        }
    }

    private void Dispatch(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<FrameSignals>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (s is not null) OnSignals?.Invoke(s);
        }
        catch (JsonException) { /* ignore partial/garbled */ }
    }

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') return i;
        return -1;
    }
}

public sealed class FrameSignals
{
    public double DisplayFps { get; set; }
    public double PresentRate { get; set; }
    public int Ppf { get; set; }
    public double FrametimeMs { get; set; }
    public double FrametimeP99 { get; set; }
    public double Low1Fps { get; set; }
    public double Low01Fps { get; set; }
    public double[] Ft { get; set; } = Array.Empty<double>();
    public double GpuBusyPct { get; set; }
    public double CpuMainPct { get; set; }
    public double CpuTotalPct { get; set; }

    // Headroom Index (computed in the core).
    public string Bottleneck { get; set; } = "unknown";
    public double UtilizationIndex { get; set; }
    public double HeadroomIndex { get; set; }
    public bool MoreFpsLikely { get; set; }
    public string Verdict { get; set; } = "";
    public string Suggestion { get; set; } = "";
}
