using System.IO;

namespace FrameLCDoctor.Launcher;

/// <summary>Deploys / removes the FrameLCDoctor proxy into a game folder. Never clobbers
/// a foreign DLL: an install is only "ours" if our marker ini sits next to it.</summary>
public static class Installer
{
    private const string Marker = "framelcdoctor.ini";

    public static string ProxiesDir =>
        Path.Combine(AppContext.BaseDirectory, "proxies");

    public static bool IsInstalled(string gameDir) =>
        File.Exists(Path.Combine(gameDir, Marker));

    public static (bool ok, string message) Install(string exePath, GfxApi api)
    {
        string? proxyName = PeAnalyzer.ProxyDllName(api);
        if (proxyName is null)
            return (false, $"API {api} no soportada todavia (solo D3D11 / DXGI-D3D12).");

        string gameDir = Path.GetDirectoryName(exePath)!;
        string proxySrc = Path.Combine(ProxiesDir, proxyName);
        if (!File.Exists(proxySrc))
            return (false, $"falta el proxy '{proxyName}' en {ProxiesDir} (build de proxies).");

        string proxyDst = Path.Combine(gameDir, proxyName);
        string origDst = Path.Combine(gameDir, Path.GetFileNameWithoutExtension(proxyName) + "_orig.dll");

        // guard: don't overwrite a non-ours DLL
        if (File.Exists(proxyDst) && !IsInstalled(gameDir))
            return (false, $"ya existe un '{proxyName}' que no es nuestro (otro mod/real). Abortado.");

        try
        {
            string sysDll = Path.Combine(Environment.SystemDirectory, proxyName);
            File.Copy(sysDll, origDst, overwrite: true);
            File.Copy(proxySrc, proxyDst, overwrite: true);
            File.WriteAllText(Path.Combine(gameDir, Marker), "[core]\r\nLimitFps=0\r\n");
            return (true, $"Instalado en {gameDir}  ({proxyName} + {Path.GetFileName(origDst)}). Arranca el juego.");
        }
        catch (Exception ex) { return (false, $"error instalando: {ex.Message}"); }
    }

    public static (bool ok, string message) Uninstall(string gameDir, GfxApi api)
    {
        if (!IsInstalled(gameDir)) return (false, "no hay instalacion de FrameLCDoctor aca.");
        string? proxyName = PeAnalyzer.ProxyDllName(api) ?? "d3d11.dll";
        try
        {
            foreach (var f in new[] { proxyName, Path.GetFileNameWithoutExtension(proxyName) + "_orig.dll",
                                      Marker, "framelcdoctor.log" })
            {
                string p = Path.Combine(gameDir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            return (true, "Desinstalado. El juego vuelve a su DLL normal.");
        }
        catch (Exception ex) { return (false, $"error desinstalando: {ex.Message}"); }
    }
}
