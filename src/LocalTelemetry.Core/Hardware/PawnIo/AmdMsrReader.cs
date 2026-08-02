using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware.PawnIo;

/// <summary>
/// Reads AMD CPU temperature via the PawnIo driver by resolving and iterating
/// the power management table.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmdMsrReader : IDisposable
{
    private const int PM_TABLE_ENTRY_COUNT = 128;

    private IPawnIoTransport? _device;
    private bool _disposed;

    /// <summary>Gets whether the PawnIo transport is available for AMD MSR reads.</summary>
    public bool IsAvailable => _device is not null;

    /// <summary>
    /// Initializes a new instance of <see cref="AmdMsrReader"/> with a PawnIo transport.
    /// </summary>
    /// <param name="device">The PawnIo transport to use for driver communication.</param>
    public AmdMsrReader(IPawnIoTransport device)
    {
        _device = device;
    }

    /// <summary>
    /// Reads the highest AMD CPU temperature by walking the power management table.
    /// </summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float ReadTemperature()
    {
        if (_device is null) return 0f;

        var result = _device.Execute("ioctl_resolve_pm_table", null, 2);
        if (result.Length < 2)
            return 0f;
        uint pmVersion = (uint)(result[0] & 0xFFFFFFFF);
        ulong pmBase = result[1];

        if (_device.Execute("ioctl_update_pm_table", null, 0).Length == 0)
            return 0f;

        result = _device.Execute("ioctl_read_pm_table", null, PM_TABLE_ENTRY_COUNT);
        if (result.Length == 0)
            return 0f;

        int actualEntries = Math.Min(result.Length, PM_TABLE_ENTRY_COUNT);
        float bestTemp = 0f;

        for (int i = 0; i < actualEntries; i++)
        {
            ulong entry = result[i];
            uint lo = (uint)(entry & 0xFFFFFFFF);
            float t = TryDecodeTemperature(lo);
            if (t > bestTemp)
                bestTemp = t;

            uint hi = (uint)((entry >> 32) & 0xFFFFFFFF);
            if (hi == lo) continue;
            t = TryDecodeTemperature(hi);
            if (t > bestTemp)
                bestTemp = t;
        }

        return bestTemp;
    }

    private static float TryDecodeTemperature(uint raw)
    {
        if (raw == 0 || raw == 0xFFFFFFFF)
            return 0f;

        if (raw > 10000 && raw < 200000)
            return raw / 1000f;

        if (raw > 40 && raw < 500)
            return raw * 0.25f;

        return 0f;
    }

    /// <summary>Releases the PawnIo transport reference.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device = null;
    }
}
