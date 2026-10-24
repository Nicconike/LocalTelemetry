using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Wraps Intel Power Gadget (EnergyLib64.dll) for accurate CPU temperature
/// and frequency readings on Intel processors.
/// Returns NaN if the DLL is unavailable or no Intel CPU detected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntelPowerGadget : IDisposable
{
    private readonly IIntelPowerGadgetNative _native;
    private readonly bool _initialized;
    private readonly int _numNodes;
    private bool _disposed;

    /// <summary>Gets whether the Intel Power Gadget library was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    public IntelPowerGadget() : this(null) { }

    internal IntelPowerGadget(IIntelPowerGadgetNative? native)
    {
        _native = native ?? IntelPowerGadgetNativePInvoke.Instance;

        if (!_native.TryLoadLibrary())
            return;

        int ret = _native.Initialize();
        if (ret != 0) return;

        ret = _native.GetNumNodes(out _numNodes);
        if (ret != 0 || _numNodes <= 0) return;

        _initialized = true;
        IsAvailable = true;
    }

    /// <summary>Current IA frequency in MHz or NaN.</summary>
    public double GetFrequency()
    {
        if (!_initialized || _numNodes <= 0) return double.NaN;
        int ret = _native.GetIAFrequency(0, out double freq);
        return ret == 0 ? freq : double.NaN;
    }

    /// <summary>Releases the Intel Power Gadget library by calling <c>IntelEnergyLib_Shutdown</c>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized)
            _native.Shutdown();
    }
}

internal interface IIntelPowerGadgetNative
{
    bool TryLoadLibrary();
    int Initialize();
    int Shutdown();
    int GetNumNodes(out int nNodes);
    int GetIAFrequency(int node, out double frequency);
}

internal sealed class IntelPowerGadgetNativePInvoke : IIntelPowerGadgetNative
{
    public static readonly IntelPowerGadgetNativePInvoke Instance = new();

    internal Func<bool> TryLoadLibraryFunc { get; set; } = DefaultTryLoad;
    internal Func<int> InitializeFunc { get; set; } = IntelEnergyLib_Initialize;
    internal Func<int> ShutdownFunc { get; set; } = IntelEnergyLib_Shutdown;
    internal delegate int GetNumNodesDelegate(ref int nNodes);
    internal GetNumNodesDelegate GetNumNodesFunc { get; set; } = IntelEnergyLib_GetNumNodes;
    internal delegate int GetIAFrequencyDelegate(int node, ref double frequency);
    internal GetIAFrequencyDelegate GetIAFrequencyFunc { get; set; } = IntelEnergyLib_GetIAFrequency;

    internal void ResetHooks()
    {
        TryLoadLibraryFunc = DefaultTryLoad;
        InitializeFunc = IntelEnergyLib_Initialize;
        ShutdownFunc = IntelEnergyLib_Shutdown;
        GetNumNodesFunc = IntelEnergyLib_GetNumNodes;
        GetIAFrequencyFunc = IntelEnergyLib_GetIAFrequency;
    }

    private static bool DefaultTryLoad()
    {
        if (!NativeLibrary.TryLoad("EnergyLib64.dll", out var lib))
            return false;
        NativeLibrary.Free(lib);
        return true;
    }

    public bool TryLoadLibrary()
    {
        try { return TryLoadLibraryFunc(); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to verify native Intel Energy library availability");
            return false;
        }
    }

    public int Initialize()
    {
        try { return InitializeFunc(); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to initialize Intel Energy library");
            return -1;
        }
    }

    public int Shutdown()
    {
        try { return ShutdownFunc(); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to shutdown Intel Energy library");
            return -1;
        }
    }

    public int GetNumNodes(out int nNodes)
    {
        nNodes = 0;
        try { return GetNumNodesFunc(ref nNodes); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get Intel Energy library node count");
            return -1;
        }
    }

    public int GetIAFrequency(int node, out double frequency)
    {
        frequency = 0;
        try { return GetIAFrequencyFunc(node, ref frequency); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read Intel CPU frequency via Energy library");
            return -1;
        }
    }

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_Initialize();

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_Shutdown();

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_GetNumNodes(ref int nNodes);

    [DllImport("EnergyLib64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IntelEnergyLib_GetIAFrequency(int node, ref double frequency);
}
