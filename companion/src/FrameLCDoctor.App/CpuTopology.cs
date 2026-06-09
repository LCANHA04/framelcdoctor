using System.Runtime.InteropServices;

namespace FrameLCDoctor;

/// <summary>CPU core topology via the OS. Used to pin a game to the cores that help it:
/// the performance cores on hybrid Intel, or a single CCD (cores sharing L3) on multi-CCD
/// AMD (cuts cross-CCD latency). On a plain monolithic CPU there's nothing to pin.</summary>
public static class CpuTopology
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int RelationshipType, IntPtr Buffer, ref uint ReturnedLength);

    private static List<(int relationship, byte b8, byte b9, ulong mask)> Enumerate(int rel)
    {
        var res = new List<(int, byte, byte, ulong)>();
        uint len = 0;
        GetLogicalProcessorInformationEx(rel, IntPtr.Zero, ref len);
        if (len == 0) return res;
        IntPtr buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(rel, buf, ref len)) return res;
            int off = 0;
            while (off < len)
            {
                IntPtr p = buf + off;
                int relationship = Marshal.ReadInt32(p, 0);
                int size = Marshal.ReadInt32(p, 4);
                byte b8 = Marshal.ReadByte(p, 8);   // ProcessorCore: Flags ; Cache: Level
                byte b9 = Marshal.ReadByte(p, 9);   // ProcessorCore: EfficiencyClass
                // GROUP_AFFINITY.Mask: ProcessorCore at +32, Cache at +40
                ulong mask = (ulong)Marshal.ReadInt64(p, relationship == RelationCache ? 40 : 32);
                res.Add((relationship, b8, b9, mask));
                if (size <= 0) break;
                off += size;
            }
        }
        catch { }
        finally { Marshal.FreeHGlobal(buf); }
        return res;
    }

    public static (ulong mask, bool hybrid, int pCores, int totalCores) PerformanceCores()
    {
        var cores = Enumerate(RelationProcessorCore);
        if (cores.Count == 0) return (~0ul, false, 0, 0);
        byte maxEff = cores.Max(c => c.b9);
        bool hybrid = cores.Any(c => c.b9 != maxEff);
        ulong pMask = 0; int pCores = 0;
        foreach (var c in cores) if (c.b9 == maxEff) { pMask |= c.mask; pCores++; }
        return (pMask, hybrid, pCores, cores.Count);
    }

    // L3 cache groups = CCDs on AMD (and the whole CPU on monolithic dies).
    public static List<ulong> L3Groups()
        => Enumerate(RelationCache).Where(c => c.b8 == 3).Select(c => c.mask).Distinct().ToList();

    /// <summary>The affinity mask that helps a game, with a label. applicable=false when
    /// there's nothing meaningful to pin (monolithic, non-hybrid, single CCD).</summary>
    public static (ulong mask, string label, bool applicable) BestGameAffinity()
    {
        var (pMask, hybrid, pCores, _) = PerformanceCores();
        if (hybrid && pMask != 0) return (pMask, $"{pCores} P-cores", true);

        var l3 = L3Groups();
        if (l3.Count > 1)
        {
            ulong best = l3.OrderByDescending(BitCount).First();   // the CCD with most cores
            return (best, $"1 CCD ({BitCount(best)} hilos, mismo L3)", true);
        }
        return (~0ul, "todos los cores", false);
    }

    public static int BitCount(ulong v) { int n = 0; while (v != 0) { n += (int)(v & 1); v >>= 1; } return n; }
}
