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
    public double GpuBusyPct { get; set; }
    public double CpuMainPct { get; set; }
}
