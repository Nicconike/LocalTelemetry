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

    internal static Func<string, SafeFileHandle>? CreateFileOverride { get; set; }
    internal static Func<SafeFileHandle, uint, byte[], byte[], bool>? DeviceIoControlOverride { get; set; }
    internal static bool PrivilegeEnabled { get; set; }
    internal static bool ThrowInEnableBackupPrivilegeForTesting { get; set; }
    internal static Func<HttpClient>? HttpClientFactory { get; set; }
    internal static Func<ProcessStartInfo, Process?>? ProcessLauncher { get; set; }
    internal static bool ThrowInStartDriverServiceForTesting { get; set; }

    internal PawnIoDevice(SafeFileHandle handle)
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

        var handle = CreateFileOverride != null
            ? CreateFileOverride(DevicePath)
            : NativeMethods.CreateFile(
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
            Log.Debug($"PawnIo driver not present (Win32 error {err})");
            handle.Dispose();
            return null;
        }

        return new PawnIoDevice(handle);
    }

    private static void EnableBackupPrivilege()
    {
        if (PrivilegeEnabled) return;
        try
        {
            if (ThrowInEnableBackupPrivilegeForTesting)
                throw new InvalidOperationException("Simulated privilege failure");
            var hProc = NativeMethods.GetCurrentProcess();
            if (!NativeMethods.OpenProcessToken(hProc,
                    NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ADJUST_PRIVILEGES, out var token))
                return;
            using (token)
            {
                if (!NativeMethods.LookupPrivilegeValue(null, "SeBackupPrivilege", out var luid))
                    return;

                var tp = new NativeMethods.TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Privileges = new NativeMethods.LuidAndAttributes
                    {
                        Luid = luid,
                        Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
                    }
                };
                int len = Marshal.SizeOf<NativeMethods.TokenPrivileges>();
                if (NativeMethods.AdjustTokenPrivileges(token, false, ref tp, (uint)len, IntPtr.Zero, IntPtr.Zero)
                    && Marshal.GetLastWin32Error() == 0)
                {
                    PrivilegeEnabled = true;
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

        bool ok = DeviceIoControlOverride != null
            ? DeviceIoControlOverride(_handle, IOCTL_PIO_LOAD_BINARY, blob, [])
            : NativeMethods.DeviceIoControl(_handle, IOCTL_PIO_LOAD_BINARY,
                blob, (uint)blob.Length, null, 0, out _, IntPtr.Zero);
        if (!ok)
            Log.Error($"Failed to load PawnIO kernel module ({blob.Length} bytes, Win32 error {Marshal.GetLastWin32Error()})");

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
        if (nameLen < 32) inBuffer[nameLen] = 0;

        if (input is not null)
            Buffer.BlockCopy(input, 0, inBuffer, 32, inCount * 8);

        int outBufSize = outLength * 8;
        byte[] outBuffer = ArrayPool<byte>.Shared.Rent(outBufSize);
        try
        {
            bool ioctlSuccess = DeviceIoControlOverride != null
                ? DeviceIoControlOverride(_handle, IOCTL_PIO_EXECUTE_FN, inBuffer, outBuffer)
                : NativeMethods.DeviceIoControl(_handle, IOCTL_PIO_EXECUTE_FN,
                    inBuffer, (uint)inBufSize,
                    outBuffer, (uint)outBufSize,
                    out _, IntPtr.Zero);

            if (!ioctlSuccess)
            {
                int err = Marshal.GetLastWin32Error();
                if (err is 6 or 1167) // ERROR_INVALID_HANDLE or ERROR_DEVICE_NOT_CONNECTED
                {
                    Log.Warn($"PawnIo driver disconnected (Win32 error {err})");
                    _disposed = true;
                    _handle.Dispose();
                }
                else
                {
                    Log.Error($"Failed to execute IOCTL command '{name}' (Win32 error {err})");
                }
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
    /// <summary>
    /// Test seam: replaces <see cref="Process.Start"/> for all elevated launches
    /// (installer, <c>sc.exe</c>). Returns the process exit code; <see langword="null"/>
    /// means the process could not be started.
    /// </summary>
    internal static Func<ProcessStartInfo, int?>? ProcessRunner { get; set; }

    /// <summary>Test seam: replaces the HTTPS download of the PawnIO setup executable.</summary>
    internal static Func<byte[]>? InstallerDownloader { get; set; }

    /// <summary>
    /// Test seam: replaces the destructive stale-install cleanup
    /// (<c>sc.exe stop/delete</c>, HKLM uninstall keys, Program Files removal).
    /// </summary>
    internal static Action? CleanupHandler { get; set; }

    /// <summary>Test seam: replaces the process elevation check.</summary>
    internal static Func<bool>? ElevationChecker { get; set; }

    private static Uri GetDefaultInstallerUri()
    {
        string? customUrl = Environment.GetEnvironmentVariable("PAWNIO_INSTALLER_URL");
        if (!string.IsNullOrWhiteSpace(customUrl) && Uri.TryCreate(customUrl, UriKind.Absolute, out var customUri))
            return customUri;

        return new UriBuilder(Uri.UriSchemeHttps, "github.com")
        {
            Path = "namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"
        }.Uri;
    }

    private static byte[] DownloadInstallerBytes()
    {
        if (InstallerDownloader is not null)
        {
            return InstallerDownloader();
        }

        using var client = HttpClientFactory is not null ? HttpClientFactory() : new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        return client.GetByteArrayAsync(GetDefaultInstallerUri()).GetAwaiter().GetResult();
    }

    private static (string Path, bool IsTemporary) ResolveInstallerPath()
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, "PawnIO_setup.exe");
        if (InstallerDownloader is null && File.Exists(localPath))
        {
            Log.Info("Using bundled PawnIO installer");
            return (localPath, false);
        }

        Log.Info("Downloading PawnIO installer");
        byte[] setupBytes = DownloadInstallerBytes();
        string tmpPath = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
        File.WriteAllBytes(tmpPath, setupBytes);
        return (tmpPath, true);
    }

    /// <summary>
    /// Downloads and runs the PawnIO setup installer with silent flags.
    /// Cleans up any stale installation first.
    /// </summary>
    /// <returns><see langword="true"/> if installation succeeded or already installed.</returns>
    public static bool TryInstall()
    {
        if (ProcessRunner is null && !CanRunElevatedOperation())
            return false;

        CleanupStaleInstall();

        try
        {
            var (installerPath, isTemporaryFile) = ResolveInstallerPath();

            var psi = new ProcessStartInfo(installerPath, "-install -silent")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };

            int exitCode = RunInstaller(psi);
            if (isTemporaryFile)
            {
                try { File.Delete(installerPath); } catch (Exception ex) { Log.Error(ex, "Failed to delete temporary installer file"); }
            }

            DeletePawnIoTempFiles();

            Log.Info($"PawnIO installer completed with exit code {exitCode}");

            if (exitCode == 0)
                return true;

            if (exitCode == 183)
            {
                Thread.Sleep(2000);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install PawnIO driver");
            return false;
        }
    }

    private static bool IsProcessElevated()
    {
        if (ElevationChecker is not null)
            return ElevationChecker();

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Guards elevated execution. Elevated operations are skipped when
    /// the current process is not already elevated, so a UAC prompt is never triggered.
    /// </summary>
    private static bool CanRunElevatedOperation()
    {
        if (!IsProcessElevated())
        {
            Log.Warn("Process is not elevated; skipping PawnIO operation to prevent UAC prompt");
            return false;
        }

        return true;
    }

    private static int RunInstaller(ProcessStartInfo psi)
    {
        if (ProcessRunner is not null)
            return ProcessRunner(psi) ?? -1;

        if (!CanRunElevatedOperation())
            return -1;

        using var proc = ProcessLauncher is not null ? ProcessLauncher(psi) : Process.Start(psi);
        if (proc is null) return -1;
        proc.WaitForExit(90000);
        return proc.ExitCode;
    }

    /// <summary>
    /// Stops and deletes the PawnIO service, removes uninstall registry keys,
    /// and deletes the Program Files directory.
    /// </summary>
    public static void CleanupStaleInstall()
    {
        if (CleanupHandler is not null)
        {
            CleanupHandler();
            return;
        }

        if (ProcessRunner is null && !CanRunElevatedOperation())
            return;

        try { RunSc("stop PawnIO"); } catch (Exception ex) { Log.Error(ex, "Stop service cleanup failed"); }
        try { RunSc("delete PawnIO"); } catch (Exception ex) { Log.Error(ex, "Delete service cleanup failed"); }

        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false); } catch (Exception ex) { Log.Error(ex, "Remove HKLM uninstall key failed"); }
        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false); } catch (Exception ex) { Log.Error(ex, "Remove WOW uninstall key failed"); }

        string programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
        try { if (Directory.Exists(programFilesPath)) Directory.Delete(programFilesPath, true); } catch (Exception ex) { Log.Error(ex, "Delete Program Files cleanup failed"); }

        DeletePawnIoTempFiles();
    }

    internal static void DeletePawnIoTempFiles()
    {
        var tempDirs = new List<string> { Path.GetTempPath() };
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (!tempDirs.Contains(winTemp, StringComparer.OrdinalIgnoreCase))
            tempDirs.Add(winTemp);

        foreach (string dir in tempDirs)
        {
            if (Directory.Exists(dir))
                DeletePawnIoEntriesInDirectory(dir);
        }
    }

    private static void DeletePawnIoEntriesInDirectory(string dir)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "PawnIO_*"))
            {
                try { File.Delete(file); } catch (Exception ex) { Log.Debug(ex, $"Failed to delete temp file: {file}"); }
            }

            foreach (string subDir in Directory.EnumerateDirectories(dir, "PawnIO_*"))
            {
                try { Directory.Delete(subDir, true); } catch (Exception ex) { Log.Debug(ex, $"Failed to delete temp directory: {subDir}"); }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, $"Failed to enumerate temp files in: {dir}");
        }
    }

    private static void RunSc(string args)
    {
        string scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        var psi = new ProcessStartInfo(scPath, args)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
        };

        if (ProcessRunner is not null)
        {
            ProcessRunner(psi);
            return;
        }

        if (!CanRunElevatedOperation())
            return;

        using var proc = ProcessLauncher is not null ? ProcessLauncher(psi) : Process.Start(psi);
        if (proc is not null)
            proc.WaitForExit(5000);
    }

    /// <summary>Starts the PawnIO Windows driver service via <c>sc start</c>.</summary>
    /// <returns><see langword="true"/> if the service started or was already running.</returns>
    public static bool StartDriverService()
    {
        try
        {
            if (ThrowInStartDriverServiceForTesting)
                throw new InvalidOperationException("Simulated sc error");

            string scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
            var psi = new ProcessStartInfo(scPath, "start PawnIO")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };

            if (ProcessRunner is not null)
            {
                int exitCode = ProcessRunner(psi) ?? -1;
                return exitCode == 0 || exitCode == 1056;
            }

            if (!CanRunElevatedOperation())
                return false;

            using var proc = ProcessLauncher is not null ? ProcessLauncher(psi) : Process.Start(psi);
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
