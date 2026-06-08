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
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0x7A));
        TxtFps.Text = s.DisplayFps.ToString("F0", CultureInfo.InvariantCulture);
        TxtFrametime.Text = $"{s.FrametimeMs:F1} ms por frame";
        TxtLow1.Text  = s.Low1Fps  >= 1 ? s.Low1Fps.ToString("F0", CultureInfo.InvariantCulture)  : "--";
        TxtLow01.Text = s.Low01Fps >= 1 ? s.Low01Fps.ToString("F0", CultureInfo.InvariantCulture) : "--";
        _lastFt = s.Ft ?? Array.Empty<double>();
        DrawGraph();

        SetBar(BarGpu, TxtGpu, s.GpuBusyPct);
        SetBar(BarCpuPeak, TxtCpuPeak, s.CpuMainPct);
        SetBar(BarCpuTotal, TxtCpuTotal, s.CpuTotalPct);

        TxtBottleneck.Text = BottleneckLabel(s.Bottleneck);
        Badge.Background = new SolidColorBrush(BottleneckColor(s.Bottleneck));
        TxtHeadroom.Text = s.HeadroomIndex.ToString("F0", CultureInfo.InvariantCulture);
        TxtVerdict.Text = s.Verdict;
        TxtSuggestion.Text = s.Suggestion;
        MoreFpsPill.Visibility = s.MoreFpsLikely ? Visibility.Visible : Visibility.Collapsed;

        var adv = DxvkAdvisor.Advise(s.Bottleneck, _vendor);
        TxtDxvkVerdict.Text = adv.Verdict;
        TxtDxvk.Text = adv.Reason;
        var c = adv.Level switch
        {
            DxvkRec.Recommended   => Color.FromRgb(0x5C, 0xC8, 0x7A),
            DxvkRec.Unlikely      => Color.FromRgb(0xE0, 0xA5, 0x4F),
            DxvkRec.NotApplicable => Color.FromRgb(0x6E, 0x8E, 0xC0),
            _                     => Color.FromRgb(0x9A, 0x9A, 0xA6),
        };
        TxtDxvkVerdict.Foreground = new SolidColorBrush(c);
        DxvkDot.Background = new SolidColorBrush(c);
    }

    private double[] _lastFt = Array.Empty<double>();

    private void DrawGraph()
    {
        double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
        var ft = _lastFt;
        if (w <= 1 || h <= 1 || ft.Length < 2) { GraphLine.Points = new System.Windows.Media.PointCollection(); return; }
        double maxScale = Math.Max(ft.Max(), 33.3);   // floor: a 30fps spike (33ms) still fits; 60fps ~ mid
        var pts = new System.Windows.Media.PointCollection(ft.Length);
        for (int i = 0; i < ft.Length; i++)
        {
            double x = w * i / (ft.Length - 1);
            double y = h - Math.Min(ft[i] / maxScale, 1.0) * h;   // big frametime -> spike up
            pts.Add(new System.Windows.Point(x, y));
        }
        GraphLine.Points = pts;
        GraphMax.Text = $"max {maxScale:F0} ms";
    }

    private void Graph_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

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
