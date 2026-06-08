using System.IO;
using System.Windows;
using System.Windows.Media;
using FrameLCDoctor.Launcher;

namespace FrameLCDoctor;

public partial class LauncherWindow : Window
{
    private string _exe = "", _gameDir = "", _appId = "";
    private GfxApi _api = GfxApi.Unknown;
    private bool _acDetected;

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadGames();
    }

    private void LoadGames()
    {
        TxtStatus.Text = "buscando juegos de Steam...";
        Task.Run(() => SteamLibrary.InstalledGames()).ContinueWith(t =>
        {
            ListGames.ItemsSource = t.Result;
            TxtStatus.Text = t.Result.Count > 0
                ? $"{t.Result.Count} juegos. Elegi uno (o Examinar)."
                : "no encontre juegos de Steam. Usa Examinar.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadGames();

    private void ListGames_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ListGames.SelectedItem is not SteamGame g) return;
        TxtName.Text = g.Name;
        TxtExe.Text = "buscando el exe de render...";
        SetButtons(false);
        string dir = g.InstallDir, appId = g.AppId;
        Task.Run(() => SteamLibrary.FindRenderExes(dir)).ContinueWith(t =>
        {
            if (t.Result.Count == 0) { TxtExe.Text = "no encontre un exe D3D11/DXGI en este juego."; ResetBadges(); return; }
            var (exe, api) = t.Result[0];
            Select(exe, api, appId);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Ejecutable (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;
        var (isX64, dlls) = PeAnalyzer.ReadImports(dlg.FileName);
        var api = PeAnalyzer.DetectApi(dlls);
        TxtName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        Select(dlg.FileName, isX64 ? api : GfxApi.Unknown, "");
    }

    private void Select(string exe, GfxApi api, string appId)
    {
        _exe = exe; _api = api; _appId = appId; _gameDir = Path.GetDirectoryName(exe) ?? "";
        TxtExe.Text = exe;
        TxtApi.Text = "API: " + api;

        var (det, reason) = AntiCheat.Scan(exe, PeAnalyzer.ReadImports(exe).importedDlls);
        _acDetected = det;
        TxtAc.Text = det ? "ANTI-CHEAT detectado" : "sin anti-cheat";
        BadgeAc.Background = new SolidColorBrush(det ? Color.FromRgb(0xC0, 0x3A, 0x3A) : Color.FromRgb(0x3A, 0x9E, 0x5A));

        bool supported = PeAnalyzer.ProxyDllName(api) != null;
        bool installed = Installer.IsInstalled(_gameDir);
        TxtInstalled.Text = installed ? "FrameLCDoctor YA instalado aca." : "";

        BtnInstall.IsEnabled = supported && !det && !installed;
        BtnUninstall.IsEnabled = installed;
        BtnLaunch.IsEnabled = true;

        if (det) TxtStatus.Text = $"BLOQUEADO: {reason}. Inyectar aca puede banear tu cuenta. No se permite.";
        else if (!supported) TxtStatus.Text = $"API {api} no soportada todavia (solo D3D11 / DXGI-D3D12).";
        else TxtStatus.Text = installed ? "Listo. Arranca el juego y abri el panel." : "Limpio. Pode instalar.";
    }

    private void ResetBadges()
    {
        TxtApi.Text = "API: --"; TxtAc.Text = "anti-cheat: --";
        BadgeAc.Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        SetButtons(false);
    }

    private void SetButtons(bool on) { BtnInstall.IsEnabled = on; BtnUninstall.IsEnabled = on; BtnLaunch.IsEnabled = on; }

    private void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_acDetected) { TxtStatus.Text = "No: anti-cheat detectado."; return; }
        var (ok, msg) = Installer.Install(_exe, _api);
        TxtStatus.Text = msg;
        if (ok) { BtnInstall.IsEnabled = false; BtnUninstall.IsEnabled = true; TxtInstalled.Text = "FrameLCDoctor instalado."; }
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = Installer.Uninstall(_gameDir, _api);
        TxtStatus.Text = msg;
        if (ok) { BtnUninstall.IsEnabled = false; BtnInstall.IsEnabled = PeAnalyzer.ProxyDllName(_api) != null && !_acDetected; TxtInstalled.Text = ""; }
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string target = _appId.Length > 0 ? $"steam://rungameid/{_appId}" : _exe;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = target, UseShellExecute = true });
            TxtStatus.Text = "lanzando... abri el panel para ver el diagnostico.";
        }
        catch (Exception ex) { TxtStatus.Text = "no pude lanzar: " + ex.Message; }
    }
}
