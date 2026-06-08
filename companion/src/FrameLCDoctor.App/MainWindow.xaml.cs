using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FrameLCDoctor;

public partial class MainWindow : Window
{
    private readonly CorePipeClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private GpuVendor _vendor = GpuVendor.Unknown;

    public MainWindow()
    {
        InitializeComponent();
        _client.OnSignals += s => Dispatcher.Invoke(() => UpdateUi(s));
        Loaded += (_, _) => { DetectGpu(); Start(); };
        Closed += (_, _) => _cts.Cancel();
    }

    private void DetectGpu()
    {
        var (vendor, name) = GpuInfo.Detect();
        _vendor = vendor;
        TxtGpu2.Text = name.Length > 0 ? name : "GPU desconocida";
    }

    private void Start()
    {
        _ = _client.RunAsync(_cts.Token);
        _ = MetricsLoop(_cts.Token);
    }

    private async Task MetricsLoop(CancellationToken ct)
    {
        using var metrics = new SysMetrics();
        bool ok = metrics.Open();
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { break; }
            if (!ok) continue;
            var (gpu, cpuPeak, cpuTotal) = metrics.Sample();
            if (gpu < 0 && cpuPeak < 0 && cpuTotal < 0) continue;
            string json = FormattableString.Invariant(
                $"{{\"gpu\":{gpu:F1},\"cpuPeak\":{cpuPeak:F1},\"cpuTotal\":{cpuTotal:F1}}}");
            await _client.SendAsync(json, ct);
        }
    }

    private void UpdateUi(FrameSignals s)
    {
        TxtStatus.Text = "conectado";
        TxtFps.Text = s.DisplayFps.ToString("F0", CultureInfo.InvariantCulture);
        TxtFrametime.Text = $"{s.FrametimeMs:F1} ms  (p99 {s.FrametimeP99:F1})";

        SetBar(BarGpu, TxtGpu, s.GpuBusyPct);
        SetBar(BarCpuPeak, TxtCpuPeak, s.CpuMainPct);
        SetBar(BarCpuTotal, TxtCpuTotal, s.CpuTotalPct);

        TxtBottleneck.Text = BottleneckLabel(s.Bottleneck);
        Badge.Background = new SolidColorBrush(BottleneckColor(s.Bottleneck));
        TxtHeadroom.Text = s.HeadroomIndex.ToString("F0", CultureInfo.InvariantCulture);
        TxtVerdict.Text = s.Verdict;
        TxtSuggestion.Text = s.Suggestion;
        TxtMoreFps.Text = s.MoreFpsLikely ? "+ fps posible" : "";
        TxtMoreFps.Foreground = new SolidColorBrush(s.MoreFpsLikely ? Color.FromRgb(0x5C, 0xD6, 0x7A) : Colors.Gray);

        TxtDxvk.Text = DxvkAdvisor.Advise(s.Bottleneck, _vendor);
    }

    private void BtnGames_Click(object sender, RoutedEventArgs e)
    {
        var w = new LauncherWindow { Owner = this };
        w.Show();
    }

    private void BtnDxvk_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/doitsujin/dxvk/releases",
                UseShellExecute = true
            });
        }
        catch { /* no browser */ }
    }

    private static void SetBar(System.Windows.Controls.ProgressBar bar, System.Windows.Controls.TextBlock txt, double v)
    {
        if (v < 0) { bar.Value = 0; txt.Text = "n/a"; return; }
        bar.Value = v;
        txt.Text = $"{v:F0}%";
        bar.Foreground = new SolidColorBrush(v >= 90 ? Color.FromRgb(0xE5, 0x5A, 0x5A)
                                          : v >= 70 ? Color.FromRgb(0xE0, 0xA5, 0x4F)
                                                    : Color.FromRgb(0x4F, 0xA3, 0xFF));
    }

    private static string BottleneckLabel(string b) => b switch
    {
        "gpu"        => "GPU-bound",
        "cpu-single" => "CPU (1 core)",
        "cpu-multi"  => "CPU (varios)",
        "cap"        => "Cap / liviano",
        "balanced"   => "Balanceado",
        _            => "--",
    };

    private static Color BottleneckColor(string b) => b switch
    {
        "gpu"        => Color.FromRgb(0xC9, 0x7A, 0x2B),
        "cpu-single" => Color.FromRgb(0xC0, 0x3A, 0x3A),
        "cpu-multi"  => Color.FromRgb(0xC0, 0x3A, 0x3A),
        "cap"        => Color.FromRgb(0x3A, 0x6E, 0xC0),
        "balanced"   => Color.FromRgb(0x3A, 0x9E, 0x5A),
        _            => Color.FromRgb(0x55, 0x55, 0x55),
    };

    private void SliderFps_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int fps = (int)Math.Round(e.NewValue);
        if (TxtFpsVal != null) TxtFpsVal.Text = fps == 0 ? "sin limite" : fps.ToString();
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        int fps = (int)Math.Round(SliderFps.Value);
        int ppf = int.TryParse(TxtPpf.Text, out var p) && p >= 1 ? p : 1;
        string json = FormattableString.Invariant($"{{\"cmd\":\"limiter\",\"fps\":{fps},\"ppf\":{ppf}}}");
        await _client.SendAsync(json, _cts.Token);
        TxtStatus.Text = fps == 0 ? "limite: sin tope (aplicado)" : $"limite: {fps} fps (aplicado)";
    }
}
