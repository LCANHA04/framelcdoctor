using System.Runtime.InteropServices;

namespace FrameLCDoctor;

/// <summary>
/// OS performance metrics via PDH (P/Invoke, no NuGet). English counter names so it
/// works regardless of Windows display language.
///   GPU  = max over "\GPU Engine(*)\Utilization Percentage"
///   CPU  = "\Processor(_Total)\% Processor Time"  + max over "\Processor(*)\..."
/// </summary>
public sealed class SysMetrics : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA  = 0x800007D2;

    private IntPtr _query;
    private IntPtr _gpu;       // wildcard array
    private IntPtr _cpuTotal;  // single
    private IntPtr _cpuCores;  // wildcard array

    public bool Open()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return false;
        PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpu);
        PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpuTotal);
        PdhAddEnglishCounter(_query, @"\Processor(*)\% Processor Time", IntPtr.Zero, out _cpuCores);
        PdhCollectQueryData(_query);   // first sample (% counters need two)
        return true;
    }

    public (double gpu, double cpuPeak, double cpuTotal) Sample()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
            return (-1, -1, -1);

        double gpu = MaxOfArray(_gpu);
        double cpuTotal = SingleValue(_cpuTotal);
        double cpuPeak = MaxOfArray(_cpuCores, excludeTotal: true);
        return (gpu, cpuPeak, cpuTotal);
    }

    private static double SingleValue(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return -1;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, IntPtr.Zero, out var v) != 0) return -1;
        return v.doubleValue;
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W (x64): LPWSTR szName(0..7) ; CStatus(8) pad ; double(16..23)
    private static double MaxOfArray(IntPtr counter, bool excludeTotal = false)
    {
        if (counter == IntPtr.Zero) return -1;
        uint size = 0, count = 0;
        uint rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || size == 0) return -1;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return -1;
            const int itemSize = 24;
            double max = -1;
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buf + i * itemSize;
                if (excludeTotal)
                {
                    IntPtr namePtr = Marshal.ReadIntPtr(item);
                    string? name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr) : null;
                    if (name is not null && name.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
                }
                double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                if (val > max) max = val;
            }
            return max;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    // ---- PDH P/Invoke ----
    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
        [FieldOffset(8)] public long  largeValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr ItemBuffer);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr hQuery);
}
