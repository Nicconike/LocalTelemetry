using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware.PawnIo;

/// <summary>
/// Abstraction over the PawnIo kernel driver transport for sending commands
/// (MSR reads, SMBus transactions, PM table operations).
/// </summary>
[SupportedOSPlatform("windows")]
public interface IPawnIoTransport : IDisposable
{
    /// <summary>
    /// Executes a named function on the loaded PawnIo kernel module.
    /// </summary>
    /// <param name="name">The null-terminated function name (max 31 characters).</param>
    /// <param name="input">Optional input array of 64-bit values.</param>
    /// <param name="outLength">The expected number of 64-bit output values.</param>
    /// <returns>Array of 64-bit result values or empty on failure.</returns>
    ulong[] Execute(string name, ulong[]? input, int outLength);
}
