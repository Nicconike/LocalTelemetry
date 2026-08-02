using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Wraps Intel Power Gadget (EnergyLib64.dll) for accurate CPU temperature
/// and frequency readings on Intel processors.
/// Returns NaN if the DLL is unavailable or no Intel CPU detected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntelPowerGadget : IDisposable
{
    private bool _initialized;
    private int _numNodes;
    private bool _disposed;

    /// <summary>Gets whether the Intel Power Gadget library was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    public IntelPowerGadget()
    {
        // Probe the DLL before any P/Invoke - avoids DllNotFoundException entirely
        // when Intel Power Gadget SDK is not installed (most systems).
        if (!NativeLibrary.TryLoad("EnergyLib64.dll", out var lib))
            return;
        NativeLibrary.Free(lib);

        int ret = IntelEnergyLib_Initialize();
        if (ret != 0) return;

        ret = IntelEnergyLib_GetNumNodes(ref _numNodes);
        if (ret != 0 || _numNodes <= 0) return;

        _initialized = true;
        IsAvailable = true;
    }

    /// <summary>Current IA frequency in MHz or NaN.</summary>
    public double GetFrequency()
    {
        if (!_initialized || _numNodes <= 0) return double.NaN;
        double freq = 0;
        int ret = IntelEnergyLib_GetIAFrequency(0, ref freq);
        return ret == 0 ? freq : double.NaN;
    }

    /// <summary>Releases the Intel Power Gadget library by calling <c>IntelEnergyLib_Shutdown</c>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized)
            IntelEnergyLib_Shutdown();
    }

    // P/Invoke into EnergyLib64.dll (Intel Power Gadget)

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_Initialize();

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_Shutdown();

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_GetNumNodes(ref int nNodes);

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_GetIAFrequency(int node, ref double frequency);
}
