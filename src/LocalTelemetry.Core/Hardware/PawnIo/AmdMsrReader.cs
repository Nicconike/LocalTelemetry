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
    private const uint SMN_THM_TCON_CUR_TMP = 0x00059800;
    private static readonly ulong[] SmnCurTmpInput = [SMN_THM_TCON_CUR_TMP];

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
    /// Reads AMD CPU temperature via SMN register on Zen architectures, falling back
    /// to walking the power management table on legacy Ryzen platforms.
    /// </summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float ReadTemperature()
    {
        if (_device is null) return 0f;

        // Primary: SMN register 0x00059800 (THM_TCON_CUR_TMP) across Zen 1-5
        float smnTemp = ReadTemperatureSmn();
        if (smnTemp > 0) return smnTemp;

        // Fallback: SMU PM table walking
        return ReadTemperaturePmTable();
    }

    private float ReadTemperatureSmn()
    {
        if (_device is null) return 0f;

        var result = _device.Execute("ioctl_read_smn", SmnCurTmpInput, 1);
        if (result.Length < 1 || result[0] == 0)
            return 0f;

        uint raw = (uint)(result[0] & 0xFFFFFFFF);
        uint curTemp = (raw >> 21) & 0x7FF;
        float temp = curTemp * 0.125f;

        if (temp is >= 10f and <= 125f)
            return temp;

        return 0f;
    }

    private float ReadTemperaturePmTable()
    {
        if (_device is null) return 0f;

        var result = _device.Execute("ioctl_resolve_pm_table", null, 2);
        if (result.Length < 2)
            return 0f;

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
