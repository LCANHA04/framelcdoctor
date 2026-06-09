using System.IO;
using System.Windows;
using System.Windows.Media;
using FrameLCDoctor.Launcher;

namespace FrameLCDoctor;

public partial class LauncherWindow : Window
{
    private string _exe = "", _gameDir = "", _appId = "", _manualTarget = "";
    private int _mcPid;
    private GfxApi _api = GfxApi.Unknown;
    private bool _acDetected;
    private GameProfile? _profile;
    private string? _presetPath;
    private Dictionary<string, string>? _preset;
    private string _presetSource = "";

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadGames();
    }

    private void LoadGames()
    {
        TxtStatus.Text = "buscando juegos...";
        Task.Run(() =>
        {
            var games = SteamLibrary.InstalledGames();
            // auto-detect a running Minecraft (it isn't on Steam) and put it on top.
            var (ed, exe, pid) = Minecraft.FindRunning();
            if (ed != Minecraft.Edition.None)
                games.Insert(0, new SteamGame
                {
                    Name = Minecraft.Friendly(ed) + "  ·  en ejecucion",
                    IsMinecraft = true, McEdition = ed, ExePath = exe, McPid = pid,
                });
            return games;
        }).ContinueWith(t =>
        {
            ListGames.ItemsSource = t.Result;
            bool mc = t.Result.Count > 0 && t.Result[0].IsMinecraft;
            TxtStatus.Text = mc
                ? "Minecraft detectado arriba. Elegilo y toca 'Abrir panel'."
                : t.Result.Count > 0
                    ? $"{t.Result.Count} juegos. Elegi uno (o Examinar). Si abris Minecraft, toca Refrescar."
                    : "no encontre juegos. Abri Minecraft y toca Refrescar, o usa Examinar.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadGames();

    private void ListGames_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ListGames.SelectedItem is not SteamGame g) return;
        if (g.IsMinecraft)   // injected non-Steam entry -> external-tools path
        {
            TxtName.Text = Minecraft.Friendly(g.McEdition);
            _mcPid = g.McPid;
            Select(g.ExePath, g.McEdition == Minecraft.Edition.Bedrock ? GfxApi.D3D11 : GfxApi.OpenGl, "", g.McEdition);
            return;
        }
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

    private void BtnMinecraft_Click(object sender, RoutedEventArgs e)
    {
        ListGames.SelectedItem = null;
        var (ed, exe, pid) = Minecraft.FindRunning();
        if (ed == Minecraft.Edition.None)
        {
            TxtName.Text = "Minecraft";
            ResetBadges();
            SetStatus("Abri Minecraft primero",
                "No encontre Minecraft corriendo. Abrilo (Java o Bedrock), entra a un mundo, y volve a tocar 'Buscar Minecraft'. Tambien podes apuntar al javaw.exe con 'Examinar'.");
            return;
        }
        TxtName.Text = Minecraft.Friendly(ed);
        _mcPid = pid;
        Select(exe, ed == Minecraft.Edition.Bedrock ? GfxApi.D3D11 : GfxApi.OpenGl, "", ed);
    }

    private static readonly Color Green = Color.FromRgb(0x5C, 0xC8, 0x7A);
    private static readonly Color Red   = Color.FromRgb(0xE0, 0x5A, 0x5A);
    private static readonly Color Gray  = Color.FromRgb(0x9A, 0x9A, 0xA6);

    private void Select(string exe, GfxApi api, string appId, Minecraft.Edition mcForce = Minecraft.Edition.None)
    {
        _exe = exe; _api = api; _appId = appId; _gameDir = Path.GetDirectoryName(exe) ?? "";
        _manualTarget = "";
        TxtExe.Text = exe;

        var mc = mcForce != Minecraft.Edition.None ? mcForce : Minecraft.Classify(exe);
        bool isMc = mc != Minecraft.Edition.None;

        bool supported = PeAnalyzer.ProxyDllName(api) != null;
        if (isMc)
        {
            // Minecraft can't be injected (Java=OpenGL; Bedrock=UWP) -> external tools only.
            supported = false;
            TxtApiVal.Text = Minecraft.Friendly(mc);
            TxtApiVal.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x4F));
        }
        else
        {
            TxtApiVal.Text = api == GfxApi.Unknown ? "no reconocida"
                           : supported ? $"{api}  -  soportada"
                           : $"{api}  -  no soportada todavia";
            TxtApiVal.Foreground = new SolidColorBrush(supported ? Green : Red);
        }

        var (det, reason) = AntiCheat.Scan(exe, PeAnalyzer.ReadImports(exe).importedDlls);
        _acDetected = det;
        TxtAcVal.Text = det ? $"DETECTADO  -  instalacion bloqueada ({reason})" : "no detectado  -  seguro";
        TxtAcVal.Foreground = new SolidColorBrush(det ? Red : Green);

        bool installed = Installer.IsInstalled(_gameDir);
        TxtInstVal.Text = installed ? "instalado en este juego" : "no instalado";
        TxtInstVal.Foreground = new SolidColorBrush(installed ? Green : Gray);

        // we can't inject (unsupported API / Minecraft), but the external tools still work via
        // the panel's manual target - as long as it isn't an anti-cheat game.
        bool externalOnly = !supported && !det;
        if (externalOnly) _manualTarget = exe;

        if (!isMc) _mcPid = 0;
        BtnInstall.IsEnabled = supported && !det && !installed;
        BtnUninstall.IsEnabled = installed;
        BtnLaunch.IsEnabled = !isMc;   // launching javaw.exe directly won't start MC; user opens it themselves
        // diagnosis via injection: MC Java (OpenGL) only, and only if it's running (we have a pid).
        // Bedrock is UWP -> CreateRemoteThread is blocked, so no inject there.
        bool canInject = isMc && mc == Minecraft.Edition.Java && _mcPid > 0 && Injector.Available;
        BtnDiag.IsEnabled = canInject;

        if (det)
            SetStatus("No se puede instalar aca",
                $"Este juego usa anti-cheat ({reason}). Inyectar un DLL puede banear tu cuenta de forma permanente, asi que FrameLCDoctor lo bloquea. Solo single-player sin anti-cheat.");
        else if (canInject)
            SetStatus("Minecraft Java: diagnostico disponible (inyeccion)",
                "Toca 'Activar diagnostico' para inyectar el core y ver fps/cuello EN VIVO dentro de MC (hookea gdi32!SwapBuffers de OpenGL). Despues 'Abrir panel'. Solo single-player. "
                + "Tambien tenes driver optimizer + frame-gen. MC Java suele ser CPU-bound.");
        else if (externalOnly)
            SetStatus("Diagnostico en vivo no disponible - pero los tools externos si",
                (isMc ? "Bedrock es UWP y no se puede inyectar, asi que el panel no muestra fps/cuello. "
                      : $"La API {api} no se puede inyectar todavia, asi que no hay diagnostico en vivo. ")
                + "PERO podes usar el DRIVER OPTIMIZER, el UPSCALING y el FRAME-GEN: toca 'Abrir panel'.");
        else if (installed)
            SetStatus("Ya esta instalado",
                "Arranca el juego (boton Lanzar o por Steam) y abri el panel para ver el diagnostico. Para sacarlo, Desinstalar.");
        else
            SetStatus("Listo para instalar",
                "Tocando Instalar dejas el juego preparado. Despues arrancalo y abri el panel. Todo reversible.");

        // preset source: a hand-authored profile preset wins; otherwise auto-detect by engine
        _profile = GameProfile.Load(Path.GetFileName(exe));
        if (_profile is { HasPreset: true } hp)
        {
            _presetPath = ConfigPreset.ResolvePath(hp);
            _preset = hp.PresetFps;
            _presetSource = $"perfil de {hp.Name}";
        }
        else
        {
            string bn = BottleneckMemory.Get(Path.GetFileName(exe));
            if (AutoPresetDetector.Detect(exe, bn) is { } auto)
            {
                _presetPath = auto.Path;
                _preset = auto.Preset;
                _presetSource = bn.Length > 0
                    ? $"automatico ({auto.EngineName}, segun tu cuello medido: {FriendlyBn(bn)})"
                    : $"automatico ({auto.EngineName}, max fps -- jugalo una vez con el panel abierto para tailorearlo a tu cuello)";
            }
            else { _presetPath = null; _preset = null; _presetSource = ""; }
        }

        string engine = isMc ? "Minecraft"
                       : _profile?.Engine is { Length: > 0 } e ? e : EngineDetector.Detect(exe);
        TxtEngineVal.Text = engine.Length > 0 ? engine : "desconocido";

        UpdatePresetUi();
    }

    private static string FriendlyBn(string b) => b switch
    {
        "cpu-single" => "CPU (1 core)",
        "cpu-multi"  => "CPU (varios cores)",
        "gpu"        => "GPU",
        "cap"        => "tope de fps",
        "balanced"   => "balanceado",
        _            => b,
    };

    private void UpdatePresetUi()
    {
        if (_preset is { Count: > 0 })
        {
            PresetPanel.Visibility = Visibility.Visible;
            bool applied = ConfigPreset.IsApplied(_presetPath);
            TxtPresetDesc.Text = $"Preset {_presetSource}: baja settings pesados en el config del juego para ganar fps. "
                + "Antes de tocar nada hace un backup, asi lo podes restaurar siempre.";
            TxtPresetChanges.Text = string.Join("\n", ConfigPreset.DescribeChanges(_preset).Select(s => "•  " + s));
            TxtPresetFile.Text = _presetPath != null
                ? "Archivo que se edita:  " + _presetPath + (applied ? "   (preset YA aplicado)" : "")
                : "Archivo no encontrado. Abri el juego una vez para que lo cree y reintenta.";
            BtnPresetRestore.IsEnabled = applied;
            BtnPreset.IsEnabled = _presetPath != null && !applied;
        }
        else PresetPanel.Visibility = Visibility.Collapsed;
    }

    private void BtnPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null || _presetPath == null) return;
        string changes = string.Join("\n", ConfigPreset.DescribeChanges(_preset).Select(s => "  •  " + s));
        var confirm = MessageBox.Show(
            $"FrameLCDoctor va a MODIFICAR el archivo de configuracion de tu juego:\n\n{_presetPath}\n\n" +
            $"Cambios que hace:\n{changes}\n\n" +
            $"Se guarda una copia de seguridad ({System.IO.Path.GetFileName(_presetPath)}.flcd-bak) para restaurar cuando quieras.\n" +
            "Aplicalo con el juego CERRADO.\n\n¿Aplicar el preset?",
            "Aplicar preset FPS", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var (ok, msg) = ConfigPreset.Apply(_presetPath, _preset);
        SetStatus(ok ? "Preset FPS aplicado" : "No se pudo", msg);
        UpdatePresetUi();
    }

    private void BtnPresetRestore_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = ConfigPreset.Restore(_presetPath);
        SetStatus(ok ? "Config restaurada" : "No se pudo", msg);
        UpdatePresetUi();
    }

    private void SetStatus(string title, string detail)
    {
        TxtStatus.Text = title;
        TxtStatusDetail.Text = detail;
    }

    private void ResetBadges()
    {
        TxtApiVal.Text = "--"; TxtApiVal.Foreground = new SolidColorBrush(Gray);
        TxtEngineVal.Text = "--";
        TxtAcVal.Text = "--";  TxtAcVal.Foreground = new SolidColorBrush(Gray);
        TxtInstVal.Text = "--"; TxtInstVal.Foreground = new SolidColorBrush(Gray);
        SetButtons(false);
        _profile = null; _preset = null; _presetPath = null; _manualTarget = ""; _mcPid = 0;
        PresetPanel.Visibility = Visibility.Collapsed;
    }

    private void SetButtons(bool on) { BtnInstall.IsEnabled = on; BtnUninstall.IsEnabled = on; BtnLaunch.IsEnabled = on; BtnDiag.IsEnabled = on; }

    private void BtnDiag_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = Injector.Inject(_mcPid);
        SetStatus(ok ? "Diagnostico activado" : "No se pudo activar el diagnostico", msg);
        if (ok)
        {
            // real telemetry will arrive over the pipe; no need for the manual fallback.
            _manualTarget = "";
            if (_panel is { IsLoaded: true }) _panel.Activate(); else { _panel = new MainWindow(); _panel.Closed += (_, _) => _panel = null; _panel.Show(); }
        }
    }

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
            SetStatus("Lanzando el juego...", "Cuando entre, toca 'Abrir panel' para ver el diagnostico en vivo.");
        }
        catch (Exception ex) { SetStatus("No pude lanzar el juego", ex.Message); }
    }

    private MainWindow? _panel;

    private void BtnPanel_Click(object sender, RoutedEventArgs e)
    {
        // reuse the panel if it's already open, otherwise spawn one (clear the ref when closed).
        if (_panel is { IsLoaded: true }) { _panel.Activate(); return; }
        _panel = new MainWindow();
        // for games we can't inject (Minecraft / unsupported API), hand the panel a manual
        // target so the external tools light up without waiting for injection signals.
        if (_manualTarget.Length > 0) _panel.SetManualTarget(_manualTarget);
        _panel.Closed += (_, _) => _panel = null;
        _panel.Show();
    }
}
