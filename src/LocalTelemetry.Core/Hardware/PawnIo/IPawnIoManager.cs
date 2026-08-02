using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware.PawnIo;

[SupportedOSPlatform("windows")]
public interface IPawnIoManager
{
    IPawnIoTransport? TryCreate();
    bool TryInstall();
    bool StartDriverService();
    byte[]? LoadResourceBytes(string resourceName);
    bool LoadModule(IPawnIoTransport device, byte[] blob);
}
