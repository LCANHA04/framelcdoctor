// FrameLCDoctor companion - P0 console orchestrator.
//
// Responsibilities (growing): launch/locate the target, manage the proxy DLL,
// connect to the injected core over a named pipe, pull OS GPU%/CPU% via PDH,
// load game profiles, and surface the diagnosis + chosen remedies.

namespace FrameLCDoctor;

internal static class Program
{
    private const string Version = "0.0.1";

    private static int Main(string[] args)
    {
        Console.WriteLine($"FrameLCDoctor companion v{Version} (P0 scaffold)");
        Console.WriteLine("Nothing wired yet. Next: named-pipe client + PDH metrics + profile loader.");
        // TODO P0: var pipe = new CorePipeClient(); pipe.Connect();
        // TODO P1: var metrics = new SysMetrics(); // PDH GPU Engine + Processor
        // TODO P1: render diagnosis from core signals + metrics
        return 0;
    }
}
