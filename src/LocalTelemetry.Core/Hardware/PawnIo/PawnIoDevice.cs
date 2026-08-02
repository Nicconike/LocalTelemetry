using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace LocalTelemetry.Core.Hardware.PawnIo;

/// <summary>
/// Communicates with the PawnIo kernel driver for low-level hardware access
/// (MSR reads, SMBus/SPD, power management table).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PawnIoDevice : IPawnIoTransport
{
    private const uint IOCTL_PIO_LOAD_BINARY = 0xA1B22084;
    private const uint IOCTL_PIO_EXECUTE_FN = 0xA1B22104;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";

    private SafeFileHandle? _handle;
    private bool _disposed;

    private PawnIoDevice(SafeFileHandle handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Attempts to open the PawnIo device handle.
    /// </summary>
    /// <returns>A new <see cref="PawnIoDevice"/> if the device was opened or null on failure.</returns>
    public static PawnIoDevice? TryCreate()
    {
        EnableBackupPrivilege();

        var handle = NativeMethods.CreateFile(
            DevicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Error($"PawnIo driver not present (Win32 error {err})");
            handle.Dispose();
            return null;
        }

        return new PawnIoDevice(handle);
    }

    private static bool _privilegeEnabled;
    private static void EnableBackupPrivilege()
    {
        if (_privilegeEnabled) return;
        try
        {
            var hProc = NativeMethods.GetCurrentProcess();
            if (!NativeMethods.OpenProcessToken(hProc,
                    NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ADJUST_PRIVILEGES, out var token))
                return;
            using (token)
            {
                if (!NativeMethods.LookupPrivilegeValue(null, "SeBackupPrivilege", out var luid))
                    return;

                var tp = new NativeMethods.TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
                    }
                };
                int len = Marshal.SizeOf<NativeMethods.TOKEN_PRIVILEGES>();
                if (NativeMethods.AdjustTokenPrivileges(token, false, ref tp, (uint)len, IntPtr.Zero, IntPtr.Zero)
                    && Marshal.GetLastWin32Error() == 0)
                {
                    _privilegeEnabled = true;
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Enable privilege failed"); }
    }

    /// <summary>Loads a PawnIo binary module (SYS file) into the driver.</summary>
    /// <param name="blob">The raw bytes of the PawnIo kernel module.</param>
    /// <returns><see langword="true"/> if the module was loaded successfully.</returns>
    public bool LoadModule(byte[] blob)
    {
        if (_disposed || _handle is null || blob is null || blob.Length == 0)
            return false;

        bool ok = NativeMethods.DeviceIoControl(_handle, IOCTL_PIO_LOAD_BINARY,
            blob, (uint)blob.Length, null, 0, out _, IntPtr.Zero);
        if (!ok)
            Log.Error($"LoadModule failed: len={blob.Length} error={Marshal.GetLastWin32Error()}");

        return ok;
    }

    /// <summary>Executes a named function on the loaded PawnIo kernel module.</summary>
    /// <param name="name">The null-terminated function name (max 31 characters).</param>
    /// <param name="input">Optional input array of 64-bit values.</param>
    /// <param name="outLength">The expected number of 64-bit output values.</param>
    /// <returns>Array of 64-bit result values or empty on failure.</returns>
    public ulong[] Execute(string name, ulong[]? input, int outLength)
    {
        if (_disposed || _handle is null)
            return [];

        int inCount = input?.Length ?? 0;
        int inBufSize = 32 + inCount * 8;
        byte[] inBuffer = ArrayPool<byte>.Shared.Rent(inBufSize);
        byte[] nameBytes = Encoding.ASCII.GetBytes(name ?? "");
        int nameLen = Math.Min(nameBytes.Length, 31);
        Buffer.BlockCopy(nameBytes, 0, inBuffer, 0, nameLen);
        if (nameLen < 32) inBuffer[nameLen] = 0; // null-terminate for kernel driver

        if (input is not null)
            Buffer.BlockCopy(input, 0, inBuffer, 32, inCount * 8);

        int outBufSize = outLength * 8;
        byte[] outBuffer = ArrayPool<byte>.Shared.Rent(outBufSize);
        try
        {
            if (!NativeMethods.DeviceIoControl(_handle, IOCTL_PIO_EXECUTE_FN,
                    inBuffer, (uint)inBufSize,
                    outBuffer, (uint)outBufSize,
                    out _, IntPtr.Zero))
            {
                Log.Error($"Execute failed: name='{name}' error={Marshal.GetLastWin32Error()}");
                return [];
            }

            var result = new ulong[outLength];
            Buffer.BlockCopy(outBuffer, 0, result, 0, outBufSize);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inBuffer);
            ArrayPool<byte>.Shared.Return(outBuffer);
        }
    }

    /// <summary>Releases the PawnIo device handle.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle?.Dispose();
        _handle = null;
    }

    /// <summary>Loads a named embedded resource from the assembly manifest.</summary>
    /// <param name="resourceName">The resource file name (e.g. <c>pawnio_x64.bin</c>).</param>
    /// <returns>The raw resource bytes or null if not found.</returns>
    public static byte[]? LoadResourceBytes(string resourceName)
    {
        var asm = typeof(PawnIoDevice).Assembly;
        var fullName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase)
                              || n.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName is null)
        {
            Log.Error($"Resource '{resourceName}' not found");
            return null;
        }

        using var stream = asm.GetManifestResourceStream(fullName);
        if (stream is null) return null;

        byte[] blob = new byte[stream.Length];
        stream.ReadExactly(blob, 0, (int)stream.Length);
        return blob;
    }

    /// <summary>
    /// Downloads and runs the PawnIO setup installer with silent flags.
    /// Cleans up any stale installation first.
    /// </summary>
    /// <returns><see langword="true"/> if installation succeeded or already installed.</returns>
    public static bool TryInstall()
    {
        CleanupStaleInstall();

        Log.Info("Downloading installer...");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var setupBytes = client.GetByteArrayAsync(
                "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe")
                .GetAwaiter().GetResult();

            string tmpPath = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
            File.WriteAllBytes(tmpPath, setupBytes);

            var psi = new ProcessStartInfo(tmpPath, "-install -silent")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                try { File.Delete(tmpPath); } catch (Exception ex) { Log.Error(ex, "Delete temp file (early) failed"); }
                return false;
            }

            proc.WaitForExit(90000);
            try { File.Delete(tmpPath); } catch (Exception ex) { Log.Error(ex, "Delete temp file failed"); }

            Log.Info($"Installer exit code {proc.ExitCode}");

            if (proc.ExitCode == 0)
                return true;

            if (proc.ExitCode == 183)
            {
                Thread.Sleep(2000);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download failed");
            return false;
        }
    }

    /// <summary>
    /// Stops and deletes the PawnIO service, removes uninstall registry keys,
    /// and deletes the Program Files directory.
    /// </summary>
    public static void CleanupStaleInstall()
    {
        try { RunSc("stop PawnIO"); } catch (Exception ex) { Log.Error(ex, "Stop service cleanup failed"); }
        try { RunSc("delete PawnIO"); } catch (Exception ex) { Log.Error(ex, "Delete service cleanup failed"); }

        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false); } catch (Exception ex) { Log.Error(ex, "Remove HKLM uninstall key failed"); }
        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false); } catch (Exception ex) { Log.Error(ex, "Remove WOW uninstall key failed"); }

        try { if (Directory.Exists(@"C:\Program Files\PawnIO")) Directory.Delete(@"C:\Program Files\PawnIO", true); } catch (Exception ex) { Log.Error(ex, "Delete Program Files cleanup failed"); }
    }

    private static void RunSc(string args)
    {
        var psi = new ProcessStartInfo("sc", args)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
            proc.WaitForExit(5000);
    }

    /// <summary>Starts the PawnIO Windows driver service via <c>sc start</c>.</summary>
    /// <returns><see langword="true"/> if the service started or was already running.</returns>
    public static bool StartDriverService()
    {
        try
        {
            var psi = new ProcessStartInfo("sc", "start PawnIO")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.WaitForExit(10000);
                return proc.ExitCode == 0 || proc.ExitCode == 1056;
            }
        }
        catch (Exception ex) { Log.Error(ex, "RunSc failed"); }
        return false;
    }
}
