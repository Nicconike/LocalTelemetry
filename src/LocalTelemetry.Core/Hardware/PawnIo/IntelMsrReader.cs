using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware.PawnIo;

/// <summary>
/// Reads Intel CPU temperature via the PawnIo driver using MSR
/// <c>IA32_TEMPERATURE_TARGET</c> (0x1A2) and <c>IA32_PACKAGE_THERM_STATUS</c> (0x1B1).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntelMsrReader : IDisposable
{
    private const ulong MSR_IA32_TEMPERATURE_TARGET = 0x1A2;
    private const ulong MSR_IA32_PACKAGE_THERM_STATUS = 0x1B1;

    private IPawnIoTransport? _device;
    private bool _disposed;

    /// <summary>Gets whether the PawnIo transport is available for Intel MSR reads.</summary>
    public bool IsAvailable => _device is not null;

    /// <summary>
    /// Initializes a new instance of <see cref="IntelMsrReader"/> with a PawnIo transport.
    /// </summary>
    /// <param name="device">The PawnIo transport to use for driver communication.</param>
    public IntelMsrReader(IPawnIoTransport device)
    {
        _device = device;
    }

    /// <summary>
    /// Reads the current Intel CPU package temperature via MSR.
    /// </summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float ReadTemperature()
    {
        if (_device is null) return 0f;

        var result = _device.Execute("ioctl_read_msr", [MSR_IA32_TEMPERATURE_TARGET], 1);
        if (result.Length < 1)
            return 0f;
        ulong tjTarget = result[0];
        int tjMax = (int)((tjTarget >> 16) & 0xFF);
        if (tjMax == 0)
            return 0f;

        result = _device.Execute("ioctl_read_msr", [MSR_IA32_PACKAGE_THERM_STATUS], 1);
        if (result.Length < 1)
            return 0f;
        ulong thermStatus = result[0];
        int digitalReadout = (int)((thermStatus >> 16) & 0xFF);

        return tjMax - digitalReadout;
    }

    /// <summary>Releases the PawnIo transport reference.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device = null;
    }
}
