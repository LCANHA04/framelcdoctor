using System.IO;

namespace FrameLCDoctor.Launcher;

public enum GfxApi { Unknown, D3D11, D3D12, Dxgi, D3D9, OpenGl, Vulkan }

/// <summary>Reads a PE (x64) exe's import table to find which graphics API it uses,
/// so the launcher knows which proxy DLL to deploy. No external tools.</summary>
public static class PeAnalyzer
{
    public static (bool isX64, List<string> importedDlls) ReadImports(string exePath)
    {
        var dlls = new List<string>();
        byte[] b = File.ReadAllBytes(exePath);

        uint peOff = BitConverter.ToUInt32(b, 0x3C);
        if (BitConverter.ToUInt16(b, (int)peOff) != 0x4550) return (false, dlls);  // "PE\0\0"
        ushort machine = BitConverter.ToUInt16(b, (int)peOff + 4);
        bool isX64 = machine == 0x8664;
        ushort optSize = BitConverter.ToUInt16(b, (int)peOff + 20);
        ushort numSec = BitConverter.ToUInt16(b, (int)peOff + 6);
        int opt = (int)peOff + 24;
        bool pe32plus = BitConverter.ToUInt16(b, opt) == 0x20b;
        int dirBase = opt + (pe32plus ? 112 : 96);
        uint impRva = BitConverter.ToUInt32(b, dirBase + 1 * 8);
        if (impRva == 0) return (isX64, dlls);

        // section table for RVA->file offset
        int secOff = (int)peOff + 24 + optSize;
        var secs = new List<(uint va, uint vs, uint ptr, uint rs)>();
        for (int i = 0; i < numSec; i++)
        {
            int s = secOff + i * 40;
            secs.Add((BitConverter.ToUInt32(b, s + 12), BitConverter.ToUInt32(b, s + 8),
                      BitConverter.ToUInt32(b, s + 20), BitConverter.ToUInt32(b, s + 16)));
        }
        int RvaToOff(uint rva)
        {
            foreach (var (va, vs, ptr, rs) in secs)
                if (rva >= va && rva < va + Math.Max(vs, rs)) return (int)(ptr + (rva - va));
            return -1;
        }

        int io = RvaToOff(impRva);
        if (io < 0) return (isX64, dlls);
        while (io + 20 <= b.Length)
        {
            uint nameRva = BitConverter.ToUInt32(b, io + 12);
            if (nameRva == 0) break;
            int no = RvaToOff(nameRva);
            if (no < 0) break;
            int e = no; while (e < b.Length && b[e] != 0) e++;
            dlls.Add(System.Text.Encoding.ASCII.GetString(b, no, e - no));
            io += 20;
        }
        return (isX64, dlls);
    }

    public static GfxApi DetectApi(IEnumerable<string> dlls)
    {
        var lower = dlls.Select(d => d.ToLowerInvariant()).ToHashSet();
        // exe imports the API DLL directly => best proxy target
        if (lower.Contains("d3d11.dll")) return GfxApi.D3D11;
        if (lower.Contains("dxgi.dll")) return GfxApi.Dxgi;   // D3D12 / D3D11.4 path
        if (lower.Contains("d3d12.dll")) return GfxApi.D3D12;
        if (lower.Contains("d3d9.dll")) return GfxApi.D3D9;
        if (lower.Any(d => d.StartsWith("vulkan"))) return GfxApi.Vulkan;
        if (lower.Contains("opengl32.dll")) return GfxApi.OpenGl;
        return GfxApi.Unknown;
    }

    /// <summary>Which proxy DLL name we drop for a given API (only the supported ones).</summary>
    public static string? ProxyDllName(GfxApi api) => api switch
    {
        GfxApi.D3D11 => "d3d11.dll",
        GfxApi.Dxgi or GfxApi.D3D12 => "dxgi.dll",
        _ => null,   // D3D9 / OpenGL / Vulkan not supported yet
    };
}
