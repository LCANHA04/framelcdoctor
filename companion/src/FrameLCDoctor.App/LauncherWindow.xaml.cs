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

    private static readonly Color Green = Color.FromRgb(0x5C, 0xC8, 0x7A);
    private static readonly Color Red   = Color.FromRgb(0xE0, 0x5A, 0x5A);
    private static readonly Color Gray  = Color.FromRgb(0x9A, 0x9A, 0xA6);

    private void Select(string exe, GfxApi api, string appId)
    {
        _exe = exe; _api = api; _appId = appId; _gameDir = Path.GetDirectoryName(exe) ?? "";
        TxtExe.Text = exe;

        bool supported = PeAnalyzer.ProxyDllName(api) != null;
        TxtApiVal.Text = api == GfxApi.Unknown ? "no reconocida"
                       : supported ? $"{api}  -  soportada"
                       : $"{api}  -  no soportada todavia";
        TxtApiVal.Foreground = new SolidColorBrush(supported ? Green : Red);

        var (det, reason) = AntiCheat.Scan(exe, PeAnalyzer.ReadImports(exe).importedDlls);
        _acDetected = det;
        TxtAcVal.Text = det ? $"DETECTADO  -  instalacion bloqueada ({reason})" : "no detectado  -  seguro";
        TxtAcVal.Foreground = new SolidColorBrush(det ? Red : Green);

        bool installed = Installer.IsInstalled(_gameDir);
        TxtInstVal.Text = installed ? "instalado en este juego" : "no instalado";
        TxtInstVal.Foreground = new SolidColorBrush(installed ? Green : Gray);

        BtnInstall.IsEnabled = supported && !det && !installed;
        BtnUninstall.IsEnabled = installed;
        BtnLaunch.IsEnabled = true;

        if (det)
            SetStatus("No se puede instalar aca",
                $"Este juego usa anti-cheat ({reason}). Inyectar un DLL puede banear tu cuenta de forma permanente, asi que FrameLCDoctor lo bloquea. Solo single-player sin anti-cheat.");
        else if (!supported)
            SetStatus("Juego no soportado todavia",
                $"La API {api} aun no esta soportada. Por ahora andan los juegos D3D11 y D3D12/DXGI.");
        else if (installed)
            SetStatus("Ya esta instalado",
                "Arranca el juego (boton Lanzar o por Steam) y abri el panel para ver el diagnostico. Para sacarlo, Desinstalar.");
        else
            SetStatus("Listo para instalar",
                "Tocando Instalar dejas el juego preparado. Despues arrancalo y abri el panel. Todo reversible.");
    }

    private void SetStatus(string title, string detail)
    {
        TxtStatus.Text = title;
        TxtStatusDetail.Text = detail;
    }

    private void ResetBadges()
    {
        TxtApiVal.Text = "--"; TxtApiVal.Foreground = new SolidColorBrush(Gray);
        TxtAcVal.Text = "--";  TxtAcVal.Foreground = new SolidColorBrush(Gray);
        TxtInstVal.Text = "--"; TxtInstVal.Foreground = new SolidColorBrush(Gray);
        SetButtons(false);
    }

    private void SetButtons(bool on) { BtnInstall.IsEnabled = on; BtnUninstall.IsEnabled = on; BtnLaunch.IsEnabled = on; }

    private void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_acDetected) { SetStatus("Bloqueado", "Anti-cheat detectado: no se instala (riesgo de ban)."); return; }
        var (ok, msg) = Installer.Install(_exe, _api);
        if (ok)
        {
            BtnInstall.IsEnabled = false; BtnUninstall.IsEnabled = true;
            TxtInstVal.Text = "instalado en este juego"; TxtInstVal.Foreground = new SolidColorBrush(Green);
            SetStatus("Instalado", "Ahora arranca el juego (Lanzar o por Steam) y abri el panel de FrameLCDoctor.");
        }
        else SetStatus("No se pudo instalar", msg);
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = Installer.Uninstall(_gameDir, _api);
        if (ok)
        {
            BtnUninstall.IsEnabled = false;
            BtnInstall.IsEnabled = PeAnalyzer.ProxyDllName(_api) != null && !_acDetected;
            TxtInstVal.Text = "no instalado"; TxtInstVal.Foreground = new SolidColorBrush(Gray);
            SetStatus("Desinstalado", "El juego vuelve a su DLL normal. No quedo nada de FrameLCDoctor.");
        }
        else SetStatus("No se pudo desinstalar", msg);
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string target = _appId.Length > 0 ? $"steam://rungameid/{_appId}" : _exe;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = target, UseShellExecute = true });
            SetStatus("Lanzando el juego...", "Cuando entre, abri el panel de FrameLCDoctor para ver el diagnostico en vivo.");
        }
        catch (Exception ex) { SetStatus("No pude lanzar el juego", ex.Message); }
    }
}
