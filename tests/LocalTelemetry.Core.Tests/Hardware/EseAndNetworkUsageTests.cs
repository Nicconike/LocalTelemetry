using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class EseAndNetworkUsageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public EseAndNetworkUsageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EseAndNetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "history.jsonl");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WindowsNetworkUsageProvider_GetWindowsInstallDate_ReturnsValidDate()
    {
        DateTime installDate = WindowsNetworkUsageProvider.GetWindowsInstallDate();
        installDate.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportAndSaveHistoryAsync_ExecutesSafely()
    {
        var historyFile = new TrafficHistoryFile(_filePath);
        historyFile.Load();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // This will execute the actual code and test that it handles missing databases
        // or executes successfully if the database and esentutl exist, without crashing.
        await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile, cts.Token);
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageAsync_ExecutesSafely()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Call the static method directly
        var records = await EseNetworkUsageReader.ReadUsageAsync(
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, cts.Token);

        // Should return a list (possibly empty if no DB access/run as non-admin)
        records.Should().NotBeNull();
    }
}
