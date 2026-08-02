using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware.PawnIo;

[SupportedOSPlatform("windows")]
public sealed class PawnIoManagerWrapper : IPawnIoManager
{
    public IPawnIoTransport? TryCreate() => PawnIoDevice.TryCreate();
    public bool TryInstall() => PawnIoDevice.TryInstall();
    public bool StartDriverService() => PawnIoDevice.StartDriverService();
    public byte[]? LoadResourceBytes(string resourceName) => PawnIoDevice.LoadResourceBytes(resourceName);

    public bool LoadModule(IPawnIoTransport device, byte[] blob)
    {
        if (device is PawnIoDevice pawnIoDevice)
        {
            return pawnIoDevice.LoadModule(blob);
        }
        return false;
    }
}
