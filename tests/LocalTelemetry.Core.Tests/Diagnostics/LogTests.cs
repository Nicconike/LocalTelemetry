using FluentAssertions;
using LocalTelemetry.Core.Diagnostics;
using Xunit;

namespace LocalTelemetry.Core.Tests.Diagnostics;

public class LogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sysLogPath;
    private readonly string _metricLogPath;

    public LogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sysLogPath = Path.Combine(_tempDir, "sys.log");
        _metricLogPath = Path.Combine(_tempDir, "metric.log");

        Log.Init(_sysLogPath, _metricLogPath, enableMetrics: true);
        Log.SystemLevel = Log.Level.Info;
        Log.MetricsLevel = Log.Level.Info;
    }

    public void Dispose()
    {
        Log.Shutdown();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SystemLog_WritesInfoWarnError()
    {
        Log.Info("Info message test");
        Log.Warn("Warn message test");
        Log.Warn(new InvalidOperationException("Warn exception"), "Warn ex message");
        Log.Error("Error message test");
        Log.Error(new InvalidOperationException("Error exception"), "Error ex message");

        Log.Shutdown();

        File.Exists(_sysLogPath).Should().BeTrue();
        string content = File.ReadAllText(_sysLogPath);

        content.Should().Contain("Info message test");
        content.Should().Contain("Warn message test");
        content.Should().Contain("Warn ex message");
        content.Should().Contain("Error message test");
        content.Should().Contain("Error ex message");
        content.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void SystemLog_LevelFiltering_HonorsSystemLevel()
    {
        Log.SystemLevel = Log.Level.Error;

        Log.Info("Should be skipped");
        Log.Warn("Should be skipped");
        Log.Error("Should be logged");

        Log.Shutdown();

        string content = File.ReadAllText(_sysLogPath);
        content.Should().NotContain("Should be skipped");
        content.Should().Contain("Should be logged");
    }

    [Fact]
    public void MetricLog_WritesMetricMessages()
    {
        Log.InfoMetric("Metric info test");
        Log.ErrorMetric("Metric error test");
        Log.ErrorMetric(new ArgumentException("Bad arg"), "Metric error ex");

        Log.Shutdown();

        File.Exists(_metricLogPath).Should().BeTrue();
        string content = File.ReadAllText(_metricLogPath);

        content.Should().Contain("Metric info test");
        content.Should().Contain("Metric error test");
        content.Should().Contain("Metric error ex");
    }

    [Fact]
    public void EnableMetrics_TogglesMetricsLog()
    {
        Log.EnableMetrics(false);
        Log.InfoMetric("Metric when disabled");

        Log.EnableMetrics(true);
        Log.InfoMetric("Metric when re-enabled");

        Log.Shutdown();

        if (File.Exists(_metricLogPath))
        {
            string content = File.ReadAllText(_metricLogPath);
            content.Should().NotContain("Metric when disabled");
            content.Should().Contain("Metric when re-enabled");
        }
    }
}
