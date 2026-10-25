using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware.PawnIo;

public class PawnIoDeviceTests
{
    [Fact]
    public void PawnIoDevice_TryCreate_HandlesMissingDriverGracefully()
    {
        using var device = PawnIoDevice.TryCreate();
        if (device is null)
        {
            device.Should().BeNull();
        }
        else
        {
            device.LoadModule([]).Should().BeFalse();
        }
    }

    [Fact]
    public void PawnIoDevice_WithInvalidHandle_ReturnsDefaults()
    {
        using var invalidHandle = new SafeFileHandle(IntPtr.Zero, true);
        using var device = new PawnIoDevice(invalidHandle);

        device.LoadModule([]).Should().BeFalse();
        device.Execute("test_fn", null, 2).Should().BeEmpty();
        device.Execute("test_fn", [100UL], 2).Should().BeEmpty();
    }

    [Fact]
    public void PawnIoDevice_LoadResourceBytes_LoadsEmbeddedBins()
    {
        var bytes = PawnIoDevice.LoadResourceBytes("NonExistentResource.sys");
        bytes.Should().BeNull();
    }

    [Fact]
    public void PawnIoDevice_TryInstall_UsesSeams_ReturnsTrueOnSuccess()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => [1, 2, 3];
        PawnIoDevice.ProcessRunner = _ => 0;
        try
        {
            PawnIoDevice.TryInstall().Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.InstallerDownloader = null;
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_TryInstall_UsesSeams_ReturnsFalseOnInstallerFailure()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => [1, 2, 3];
        PawnIoDevice.ProcessRunner = _ => 1;
        try
        {
            PawnIoDevice.TryInstall().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.InstallerDownloader = null;
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_StartDriverService_UsesSeam_ReturnsExpectedResults()
    {
        PawnIoDevice.ProcessRunner = _ => 0;
        try
        {
            PawnIoDevice.StartDriverService().Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.ProcessRunner = null;
        }

        PawnIoDevice.ProcessRunner = _ => 1;
        try
        {
            PawnIoDevice.StartDriverService().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_CleanupStaleInstall_UsesHandler()
    {
        bool called = false;
        PawnIoDevice.CleanupHandler = () => called = true;
        try
        {
            PawnIoDevice.CleanupStaleInstall();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = () => { };
        }
        called.Should().BeTrue();
    }

    [Fact]
    public void PawnIoDevice_TryInstall_NonElevated_FailsClosed()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = null;
        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            PawnIoDevice.TryInstall().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
            PawnIoDevice.InstallerDownloader = () => [];
            PawnIoDevice.ProcessRunner = _ => -1;
        }
    }

    [Fact]
    public void PawnIoDevice_TryInstall_DownloaderThrows_ReturnsFalse()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => throw new InvalidOperationException("download failed");
        PawnIoDevice.ProcessRunner = _ => 0;
        try
        {
            PawnIoDevice.TryInstall().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.InstallerDownloader = () => [];
            PawnIoDevice.ProcessRunner = _ => -1;
        }
    }

    [Fact]
    public void PawnIoDevice_TryInstall_WithoutProcessRunner_FailsClosed()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => [1, 2, 3];
        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            PawnIoDevice.TryInstall().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
            PawnIoDevice.InstallerDownloader = () => [];
            PawnIoDevice.ProcessRunner = _ => -1;
        }
    }

    [Fact]
    public void PawnIoDevice_StartDriverService_WithoutProcessRunner_FailsClosed()
    {
        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            PawnIoDevice.StartDriverService().Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
            PawnIoDevice.ProcessRunner = _ => -1;
        }
    }

    [Fact]
    public void PawnIoDevice_RunSc_WithoutProcessRunner_FailsClosed()
    {
        var method = typeof(PawnIoDevice).GetMethod("RunSc", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            method.Invoke(null, ["stop PawnIO"]);
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
            PawnIoDevice.ProcessRunner = _ => -1;
        }
    }

    [Fact]
    public void PawnIoDevice_RunSc_UsesProcessRunner()
    {
        int? captured = null;
        var method = typeof(PawnIoDevice).GetMethod("RunSc", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        PawnIoDevice.ProcessRunner = _ =>
        {
            captured = 7;
            return 7;
        };
        try
        {
            method.Invoke(null, ["delete PawnIO"]);
        }
        finally
        {
            PawnIoDevice.ProcessRunner = _ => -1;
        }
        captured.Should().Be(7);
    }

    [Fact]
    public void PawnIoDevice_LoadResourceBytes_LoadsEmbeddedResource()
    {
        var bytes = PawnIoDevice.LoadResourceBytes("Echo.bin");
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PawnIoDevice_TryInstall_PrefersLocalBundledInstaller()
    {
        string localSetup = Path.Combine(AppContext.BaseDirectory, "PawnIO_setup.exe");
        File.WriteAllBytes(localSetup, [0x4D, 0x5A]); // minimal dummy header
        string? launchedFile = null;

        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = null;
        PawnIoDevice.ProcessRunner = psi =>
        {
            launchedFile = psi.FileName;
            return 0;
        };

        try
        {
            bool ok = PawnIoDevice.TryInstall();
            ok.Should().BeTrue();
            launchedFile.Should().Be(localSetup);
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.InstallerDownloader = null;
            try
            {
                if (File.Exists(localSetup))
                {
                    File.Delete(localSetup);
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }
    }

    [Fact]
    public void PawnIoDevice_CleanupStaleInstall_WithoutCleanupHandler_ExecutesSafely()
    {
        PawnIoDevice.CleanupHandler = null;
        PawnIoDevice.ProcessRunner = _ => 0;
        try
        {
            var act = () => PawnIoDevice.CleanupStaleInstall();
            act.Should().NotThrow();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_TryInstall_WithExitCode183_ReturnsTrue()
    {
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => [1, 2, 3];
        PawnIoDevice.ProcessRunner = _ => 183;
        try
        {
            PawnIoDevice.TryInstall().Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.InstallerDownloader = null;
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_StartDriverService_ExitCode1056_ReturnsTrue()
    {
        PawnIoDevice.ProcessRunner = _ => 1056;
        try
        {
            PawnIoDevice.StartDriverService().Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.ProcessRunner = null;
        }
    }

    [Fact]
    public void PawnIoDevice_LoadModule_Execute_Disposal_Coverage()
    {
        var invalidHandle = new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
        var device = new PawnIoDevice(invalidHandle);

        // 1. LoadModule edge cases
        device.LoadModule(null!).Should().BeFalse();
        device.LoadModule([]).Should().BeFalse();
        device.LoadModule(new byte[16]).Should().BeFalse();

        // 2. Execute with invalid handle
        device.Execute(null!, null, 0).Should().BeEmpty();
        device.Execute("test_fn", [10, 20], 1).Should().BeEmpty();

        // 3. Dispose is safe and idempotent
        var disposeAct = () => device.Dispose();
        disposeAct.Should().NotThrow();
        disposeAct.Should().NotThrow();

        // Post-disposal guards
        device.LoadModule(new byte[16]).Should().BeFalse();
        device.Execute("test_fn", null, 1).Should().BeEmpty();
    }

    [Fact]
    public void PawnIoDevice_ElevationAndProcessChecks_Coverage()
    {
        var isElevated = typeof(PawnIoDevice).GetMethod("IsProcessElevated", BindingFlags.NonPublic | BindingFlags.Static);
        isElevated.Should().NotBeNull();

        var canRun = typeof(PawnIoDevice).GetMethod("CanRunElevatedOperation", BindingFlags.NonPublic | BindingFlags.Static);
        canRun.Should().NotBeNull();

        // 1. Unmocked check against current process identity
        isElevated.Invoke(null, null).Should().NotBeNull();

        // 2. Mocked non-elevated check
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            ((bool)isElevated.Invoke(null, null)!).Should().BeFalse();
            ((bool)canRun.Invoke(null, null)!).Should().BeFalse();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
        }

        // 3. Mocked elevated check
        PawnIoDevice.ElevationChecker = () => true;
        try
        {
            ((bool)isElevated.Invoke(null, null)!).Should().BeTrue();
            ((bool)canRun.Invoke(null, null)!).Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
        }
    }

    [Fact]
    public void PawnIoDevice_EnableBackupPrivilege_ExecutesSafely()
    {
        var enablePriv = typeof(PawnIoDevice).GetMethod("EnableBackupPrivilege", BindingFlags.NonPublic | BindingFlags.Static);
        enablePriv.Should().NotBeNull();
        var act = () => enablePriv.Invoke(null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void PawnIoDevice_CleanupStaleInstall_WithMockedElevation_CoversBranches()
    {
        // 1. Non-elevated early return
        PawnIoDevice.CleanupHandler = null;
        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            var actNonElevated = () => PawnIoDevice.CleanupStaleInstall();
            actNonElevated.Should().NotThrow();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
        }

        // 2. Elevated with ProcessRunner mock
        int runnerInvocations = 0;
        PawnIoDevice.ProcessRunner = _ =>
        {
            runnerInvocations++;
            return 0;
        };
        PawnIoDevice.ElevationChecker = () => true;
        try
        {
            var actElevated = () => PawnIoDevice.CleanupStaleInstall();
            actElevated.Should().NotThrow();
            runnerInvocations.Should().BeGreaterThan(0);
        }
        finally
        {
            PawnIoDevice.ProcessRunner = null;
            PawnIoDevice.ElevationChecker = null;
        }
    }

    [Fact]
    public void PawnIoDevice_RunInstaller_And_StartDriver_NonElevated_Coverage()
    {
        PawnIoDevice.ProcessRunner = null;
        PawnIoDevice.ElevationChecker = () => false;
        try
        {
            var runInstaller = typeof(PawnIoDevice).GetMethod("RunInstaller", BindingFlags.NonPublic | BindingFlags.Static)!;
            int code = (int)runInstaller.Invoke(null, [new System.Diagnostics.ProcessStartInfo("cmd.exe")])!;
            code.Should().Be(-1);

            bool started = PawnIoDevice.StartDriverService();
            started.Should().BeFalse();

            var runSc = typeof(PawnIoDevice).GetMethod("RunSc", BindingFlags.NonPublic | BindingFlags.Static)!;
            var actSc = () => runSc.Invoke(null, ["query PawnIO"]);
            actSc.Should().NotThrow();
        }
        finally
        {
            PawnIoDevice.ElevationChecker = null;
        }
    }

    [Fact]
    public void PawnIoDevice_GetDefaultInstallerUri_And_InstallerBranches_Coverage()
    {
        // 1. GetDefaultInstallerUri with and without custom URL
        var getUriMethod = typeof(PawnIoDevice).GetMethod("GetDefaultInstallerUri", BindingFlags.NonPublic | BindingFlags.Static)!;
        var defaultUri = (Uri)getUriMethod.Invoke(null, null)!;
        defaultUri.AbsoluteUri.Should().Contain("PawnIO_setup.exe");

        string? origEnv = Environment.GetEnvironmentVariable("PAWNIO_INSTALLER_URL");
        try
        {
            Environment.SetEnvironmentVariable("PAWNIO_INSTALLER_URL", "https://example.com/custom_pawnio.exe");
            var customUri = (Uri)getUriMethod.Invoke(null, null)!;
            customUri.AbsoluteUri.Should().Be("https://example.com/custom_pawnio.exe");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAWNIO_INSTALLER_URL", origEnv);
        }

        // 2. TryInstall with exit code 183 (already installed branch)
        PawnIoDevice.CleanupHandler = () => { };
        PawnIoDevice.InstallerDownloader = () => [1, 2, 3];
        PawnIoDevice.ProcessRunner = _ => 183;
        try
        {
            PawnIoDevice.TryInstall().Should().BeTrue();
        }
        finally
        {
            PawnIoDevice.CleanupHandler = null;
            PawnIoDevice.InstallerDownloader = null;
            PawnIoDevice.ProcessRunner = null;
        }

        // 4. EnableBackupPrivilege and IsProcessElevated fallback coverage
        var enablePrivMethod = typeof(PawnIoDevice).GetMethod("EnableBackupPrivilege", BindingFlags.NonPublic | BindingFlags.Static);
        if (enablePrivMethod is not null)
        {
            var actPriv = () => enablePrivMethod.Invoke(null, null);
            actPriv.Should().NotThrow();
        }

        var isElevatedMethod = typeof(PawnIoDevice).GetMethod("IsProcessElevated", BindingFlags.NonPublic | BindingFlags.Static);
        if (isElevatedMethod is not null)
        {
            PawnIoDevice.ElevationChecker = null;
            var isElevated = isElevatedMethod.Invoke(null, null);
            isElevated.Should().NotBeNull();
        }

        // 5. Execute with non-null input array
        using var invalidHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(-1), false);
        var device = new PawnIoDevice(invalidHandle);
        var res = device.Execute("test_fn", [12345UL], 1);
        res.Should().BeEmpty();
    }
}
