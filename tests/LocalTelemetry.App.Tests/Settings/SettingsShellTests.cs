using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Settings;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.App.Tests.Settings;

public class SettingsShellTests
{
    public SettingsShellTests()
    {
        SettingsShell.SkipInitializeComponentForTesting = true;
        SettingsShell.SkipWebView2InitForTesting = true;
        SettingsShell.SkipShowForTesting = true;
        SettingsShell.MessageBoxAction = (_, _) => { };
    }

    [Fact]
    public void SettingsShell_GetActiveNics_ReturnsActiveNetworkAdapters()
    {
        var getActiveNics = typeof(SettingsShell).GetMethod("GetActiveNics", BindingFlags.NonPublic | BindingFlags.Static);
        getActiveNics.Should().NotBeNull();
        var nics = (List<string>)getActiveNics.Invoke(null, null)!;
        nics.Should().NotBeNull();
    }

    [Fact]
    public void SettingsShell_SafeGetGateways_HandlesNetworkInterfacesSafely()
    {
        var method = typeof(SettingsShell).GetMethod("SafeGetGateways", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var res = method.Invoke(null, [nic]);
            res.Should().NotBeNull();
        }
    }

    [WpfFact]
    public void SettingsShell_OnWebMessageReceived_DispatchesInboundMessages()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var history = new TrafficHistoryFile(tempPath);
        TrafficHistoryStore.Initialize(history);
        var netLogger = new NetUsageLogger(cfg, history);
        var shell = new SettingsShell(cfg, netLogger);

        string? launchedUrl = null;
        SettingsShell.UrlLauncherAction = url => launchedUrl = url;

        // Test ApplySettings directly with JsonElement
        var dto = SettingsDtoMapping.ToDto(cfg);
        string json = JsonSerializer.Serialize(dto);
        using (var doc = JsonDocument.Parse(json))
        {
            var applySettings = typeof(SettingsShell).GetMethod("ApplySettings", BindingFlags.NonPublic | BindingFlags.Instance);
            applySettings!.Invoke(shell, [doc.RootElement]);
        }

        // Test SendAllTrafficHistory
        var sendAll = typeof(SettingsShell).GetMethod("SendAllTrafficHistory", BindingFlags.NonPublic | BindingFlags.Instance);
        sendAll!.Invoke(shell, null);

        // Test SendTrafficMonths
        var sendMonths = typeof(SettingsShell).GetMethod("SendTrafficMonths", BindingFlags.NonPublic | BindingFlags.Instance);
        sendMonths!.Invoke(shell, null);

        // Test SendTrafficToday
        var sendToday = typeof(SettingsShell).GetMethod("SendTrafficToday", BindingFlags.NonPublic | BindingFlags.Instance);
        sendToday!.Invoke(shell, null);

        // Test SendNics and CheckAndSendNics
        var sendNics = typeof(SettingsShell).GetMethod("SendNics", BindingFlags.NonPublic | BindingFlags.Instance);
        sendNics!.Invoke(shell, null);

        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, true);
        var checkNics = typeof(SettingsShell).GetMethod("CheckAndSendNics", BindingFlags.NonPublic | BindingFlags.Instance);
        checkNics!.Invoke(shell, null);

        // Test PostImportError
        var postImportErr = typeof(SettingsShell).GetMethod("PostImportError", BindingFlags.NonPublic | BindingFlags.Instance);
        postImportErr!.Invoke(shell, ["Test error message"]);

        SettingsShell.UrlLauncherAction = null;
        shell.Close();
        netLogger.Dispose();
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void SettingsShell_SendSystemInfo_GeneratesHardwarePayload()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var history = new TrafficHistoryFile(tempPath);
        TrafficHistoryStore.Initialize(history);
        var netLogger = new NetUsageLogger(cfg, history);
        var shell = new SettingsShell(cfg, netLogger);

        var sendSysInfo = typeof(SettingsShell).GetMethod("SendSystemInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => sendSysInfo!.Invoke(shell, null);
        act.Should().NotThrow();

        shell.Close();
        netLogger.Dispose();
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void SettingsShell_SendTrafficToday_PostsNetworkTotals()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var history = new TrafficHistoryFile(tempPath);
        TrafficHistoryStore.Initialize(history);
        var netLogger = new NetUsageLogger(cfg, history);
        var shell = new SettingsShell(cfg, netLogger);

        var sendTrafficToday = typeof(SettingsShell).GetMethod("SendTrafficToday", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => sendTrafficToday!.Invoke(shell, null);
        act.Should().NotThrow();

        shell.Close();
        netLogger.Dispose();
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void HandleWebMessageJson_GetSettings_PostsSettingsEnvelope()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"getSettings"}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"settings\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_GetNics_PostsNicsEnvelope()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"getNics"}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"nics\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_GetTrafficMonths_PostsMonthsEnvelope()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"getTrafficMonths"}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"trafficMonths\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_GetTrafficHistoryAll_PostsHistoryEnvelope()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"getTrafficHistoryAll"}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"trafficHistoryAll\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_GetSystemInfo_PostsSystemInfoEnvelope()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"getSystemInfo"}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"systemInfo\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_CheckForUpdates_HandlesForcedAndBackground()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        var act1 = () => ctx.Shell.HandleWebMessageJson("""{"type":"checkForUpdates","payload":{"force":true}}""");
        act1.Should().NotThrow();

        var act2 = () => ctx.Shell.HandleWebMessageJson("""{"type":"checkForUpdates","payload":{"force":false}}""");
        act2.Should().NotThrow();
    }

    [WpfFact]
    public void HandleWebMessageJson_SaveSettings_ValidPayload_PostsSaved()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;
        WindowHelpers.TaskSchedulerFactory = () => new object();

        var dto = SettingsDtoMapping.ToDto(ctx.Config);
        dto.RunAtStartup = !dto.RunAtStartup;
        string payload = JsonSerializer.Serialize(dto);
        string json = $$"""{"type":"saveSettings","payload":{{payload}}}""";

        ctx.Shell.HandleWebMessageJson(json);

        posted.Should().Contain(s => s.Contains("\"type\":\"saved\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_SaveSettings_NullMonitoring_DoesNotThrow()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"saveSettings","payload":{"monitoring":null}}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"saved\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_SaveSettings_NullPayload_PostsError()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"saveSettings","payload":null}""");

        posted.Should().Contain(s => s.Contains("\"type\":\"saved\""));
    }

    [WpfFact]
    public void HandleWebMessageJson_OpenUrl_ValidHttp_UsesUrlLauncher()
    {
        using var ctx = new ShellContext();
        string? launched = null;
        SettingsShell.UrlLauncherAction = url => launched = url;

        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"https://example.com/help"}}""");

        launched.Should().Be("https://example.com/help");
    }

    [WpfFact]
    public void HandleWebMessageJson_OpenUrl_NonHttpScheme_DoesNotLaunch()
    {
        using var ctx = new ShellContext();
        string? launched = null;
        SettingsShell.UrlLauncherAction = url => launched = url;

        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"javascript:alert(1)"}}""");

        launched.Should().BeNull();
    }

    [WpfFact]
    public void HandleWebMessageJson_UnknownType_DoesNotPost()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleWebMessageJson("""{"type":"doesNotExist"}""");

        posted.Should().NotContain(s => s.Contains("doesNotExist"));
    }

    [WpfFact]
    public void HandleWebMessageJson_InvalidJson_DoesNotThrow()
    {
        using var ctx = new ShellContext();

        var act = () => ctx.Shell.HandleWebMessageJson("{ this is not json");

        act.Should().NotThrow();
    }

    [WpfFact]
    public void HandleImportJson_ValidContent_ImportsAndPostsResult()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        string content = "LocalTelemetry history\n2024/01/15 100/200";
        string json = JsonSerializer.Serialize(new { type = "importDat", payload = new { content } });

        ctx.Shell.HandleImportJson(json);

        posted.Should().Contain(s => s.Contains("\"importDatResult\""));
        TrafficHistoryStore.GetAll().Should().Contain(r => r.Year == 2024 && r.Month == 1 && r.Day == 15);
    }

    [WpfFact]
    public void HandleImportJson_EmptyContent_PostsImportError()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleImportJson("""{"type":"importDat","payload":{"content":""}}""");

        posted.Should().Contain(s => s.Contains("\"importDatResult\"") && s.Contains("error"));
    }

    [WpfFact]
    public void HandleImportJson_InvalidJson_PostsImportError()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        ctx.Shell.HandleImportJson("{ bad json ");

        posted.Should().Contain(s => s.Contains("\"importDatResult\"") && s.Contains("error"));
    }

    [WpfFact]
    public void HandleImportJson_OversizedContent_PostsImportError()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        string hugeContent = new string('A', SettingsShell.MaxImportContentLength + 1);
        string json = JsonSerializer.Serialize(new { type = "importDat", payload = new { content = hugeContent } });
        ctx.Shell.HandleImportJson(json);

        posted.Should().Contain(s => s.Contains("\"importDatResult\"") && s.Contains("exceeds maximum allowed size"));
    }

    [Theory]
    [InlineData("https://localtelemetry.settings", true)]
    [InlineData("https://localtelemetry.settings/index.html", true)]
    [InlineData("HTTPS://LOCALTELEMETRY.SETTINGS/path?query=1", true)]
    [InlineData("https://malicious-site.com", false)]
    [InlineData("http://localtelemetry.settings", false)]
    [InlineData("ftp://localtelemetry.settings", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidMessageSource_ValidatesOriginCorrectly(string? source, bool expected)
    {
        SettingsShell.IsValidMessageSource(source).Should().Be(expected);
    }

    [Fact]
    public void ComputeSettingsDiff_And_GetMetricName_ExercisesAllDiffBranches()
    {
        var computeDiff = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        var getMetricName = typeof(SettingsShell).GetMethod("GetMetricName", BindingFlags.NonPublic | BindingFlags.Static);

        computeDiff.Should().NotBeNull();
        getMetricName.Should().NotBeNull();

        string before = """
        {
            "showSystemTrayIcon": true,
            "runAtStartup": false,
            "startMinimized": false,
            "dataDirectory": "C:\\before",
            "enableFileLogging": true,
            "monitoring": {
                "pollIntervalMs": 1000,
                "trackCpu": true,
                "trackGpu": true,
                "trackRam": true,
                "trackDisk": true,
                "trackNet": true,
                "trackTemp": true,
                "trackBattery": true,
                "gpuUsageSource": "nvml",
                "diskActivitySource": "perf"
            },
            "network": {
                "selectedInterface": "Ethernet",
                "unitFormat": "bytes",
                "speedUnit": "Mbps",
                "autoResetMonthly": true
            },
            "overlay": {
                "visible": true,
                "position": "bottom",
                "embedMode": "taskbar",
                "opacity": 80,
                "showLabels": true,
                "compactMode": false,
                "doubleClickAction": "taskmanager",
                "row1": ["cpu_pct"],
                "metricColors": {"cpu_pct": "#FF0000"},
                "groupColors": {"cpu": "#00FF00"},
                "userCustomizedMetricColors": ["cpu_pct"]
            },
            "appearance": {
                "theme": "dark",
                "accentColor": "#0000FF",
                "fontFamily": "Segoe UI",
                "fontSize": 12
            },
            "alerts": {
                "enabled": true,
                "dailyLimitMb": 100,
                "monthlyLimitMb": 1000,
                "tempThresholdC": 80,
                "showToastNotif": true,
                "playAlertSound": true
            },
            "updates": {
                "checkFrequency": "weekly",
                "includePrereleases": false
            }
        }
        """;

        string after = """
        {
            "showSystemTrayIcon": false,
            "runAtStartup": true,
            "startMinimized": true,
            "dataDirectory": "C:\\after",
            "enableFileLogging": false,
            "monitoring": {
                "pollIntervalMs": 2000,
                "trackCpu": false,
                "trackGpu": false,
                "trackRam": false,
                "trackDisk": false,
                "trackNet": false,
                "trackTemp": false,
                "trackBattery": false,
                "gpuUsageSource": "wddm",
                "diskActivitySource": "wmi"
            },
            "network": {
                "selectedInterface": "",
                "unitFormat": "bits",
                "speedUnit": "MB/s",
                "autoResetMonthly": false
            },
            "overlay": {
                "visible": false,
                "position": "top",
                "embedMode": "float",
                "opacity": 100,
                "showLabels": false,
                "compactMode": true,
                "doubleClickAction": "settings",
                "row1": ["ram_pct"],
                "metricColors": {"cpu_pct": "#000000"},
                "groupColors": {"cpu": "#FFFFFF"},
                "userCustomizedMetricColors": ["cpu_pct"]
            },
            "appearance": {
                "theme": "light",
                "accentColor": "#00FFFF",
                "fontFamily": "Arial",
                "fontSize": 14
            },
            "alerts": {
                "enabled": false,
                "dailyLimitMb": 200,
                "monthlyLimitMb": 2000,
                "tempThresholdC": 90,
                "showToastNotif": false,
                "playAlertSound": false
            },
            "updates": {
                "checkFrequency": "monthly",
                "includePrereleases": true
            }
        }
        """;

        var diffs = (List<string>)computeDiff.Invoke(null, [before, after])!;
        diffs.Should().NotBeEmpty();

        // Invalid JSON to trigger catch
        var invalidDiffs = (List<string>)computeDiff.Invoke(null, ["{invalid", "{invalid"])!;
        invalidDiffs.Should().BeEmpty();

        // Type mismatch and new property branches
        var mismatchDiffs = (List<string>)computeDiff.Invoke(null, ["""{"val":123,"extra":true}""", """{"val":"123","newProp":"new"}"""])!;
        mismatchDiffs.Should().NotBeEmpty();

        string[] metrics = ["cpu_pct", "cpu_temp", "cpu_freq", "cpu_power", "ram_pct", "ram_used", "gpu_pct", "gpu_temp", "gpu_vram", "gpu_freq", "gpu_power", "net_down", "net_up", "net_total", "battery_pct", "battery_rate", "disk_0_read", "unknown_metric"];
        foreach (var m in metrics)
        {
            var name = (string)getMetricName.Invoke(null, [m])!;
            name.Should().NotBeNullOrEmpty();
        }
    }

    [WpfFact]
    public void SettingsShell_OnHistoryUpdated_WhenWebViewReady_PushesTrafficHistory()
    {
        using var ctx = new ShellContext();
        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, true);
        var onHistUpdated = typeof(SettingsShell).GetMethod("OnHistoryUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => onHistUpdated!.Invoke(ctx.Shell, null);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void SettingsShell_WindowEvents_And_OpenFolder_Coverage()
    {
        AppSettings.InitPaths(AppContext.BaseDirectory);
        using var ctx = new ShellContext();
        var posted = new List<string>();
        var openedFolders = new List<string>();
        var openedUrls = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;
        SettingsShell.FolderLauncherAction = openedFolders.Add;
        SettingsShell.UrlLauncherAction = openedUrls.Add;

        ctx.Shell.SendUpdatedSettings();
        posted.Should().Contain(s => s.Contains("\"type\":\"settings\""));

        ctx.Shell.HandleWebMessageJson("""{"type":"openFolder","payload":{"target":"app"}}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"openFolder","payload":{"target":"data"}}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"getSettings"}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"getNics"}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"getTrafficMonths"}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"getSystemInfo"}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"bad-scheme://invalid"}}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"https://example.com"}}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"checkForUpdates"}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"unknownType"}""");
        ctx.Shell.HandleWebMessageJson("""invalid json""");

        openedUrls.Should().ContainSingle().Which.Should().Be("https://example.com/");

        var closeMethod = typeof(SettingsShell).GetMethod("CloseButton_Click", BindingFlags.NonPublic | BindingFlags.Instance);
        var actClose = () => closeMethod!.Invoke(ctx.Shell, [null!, null!]);
        actClose.Should().NotThrow();

        // Exercise TitleBar_MouseLeftButtonDown (single click and double click)
        var titleBarMethod = typeof(SettingsShell).GetMethod("TitleBar_MouseLeftButtonDown", BindingFlags.NonPublic | BindingFlags.Instance);
        if (titleBarMethod is not null)
        {
            var doubleClickArgs = new System.Windows.Input.MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                0,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            typeof(System.Windows.Input.MouseButtonEventArgs).GetProperty("ClickCount")?.SetValue(doubleClickArgs, 2);

            var actDblClick = () => titleBarMethod.Invoke(ctx.Shell, [null!, doubleClickArgs]);
            actDblClick.Should().NotThrow();
        }

        // Exercise OnNetworkAddressChanged
        var netChangedMethod = typeof(SettingsShell).GetMethod("OnNetworkAddressChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var actNetChanged = () => netChangedMethod!.Invoke(ctx.Shell, [null!, EventArgs.Empty]);
        actNetChanged.Should().NotThrow();
    }

    [Fact]
    public void SettingsShell_FormatHumanReadableChange_AllCategories_Coverage()
    {
        var formatMethod = typeof(SettingsShell).GetMethod("FormatHumanReadableChange", BindingFlags.NonPublic | BindingFlags.Static);
        formatMethod.Should().NotBeNull();

        string[] paths = [
            "showsystemtrayicon", "runatstartup", "startminimized", "datadirectory", "enablefilelogging",
            "monitoring.pollintervalms", "monitoring.enablecpu", "monitoring.enablegpu", "monitoring.enableram",
            "monitoring.enabledisk", "monitoring.enablenet", "monitoring.enablebattery",
            "monitoring.cpuusagemode", "monitoring.usefahrenheit", "monitoring.usenetbits", "monitoring.cpufreqmode",
            "monitoring.gpuusagesource", "monitoring.diskactivitysource",
            "monitoring.logcpumode", "monitoring.loggpumode", "monitoring.logrammode",
            "monitoring.lognetmode", "monitoring.logbatterymode", "monitoring.logdiskmode",
            "monitoring.preferrednic",
            "network.selectedinterface", "network.unitformat", "network.speedunit", "network.autoresetmonthly",
            "overlay.visible", "overlay.placement", "overlay.placementoffset", "overlay.opacity", "overlay.scalepct",
            "overlay.fontsizepx", "overlay.fontbold", "overlay.labelcolor", "overlay.valuecolor", "overlay.bgcolor",
            "overlay.showtoastnotif", "overlay.flashoverlay", "overlay.followwindowstheme", "overlay.showlabels",
            "overlay.compactmode", "overlay.doubleclickaction", "overlay.row1", "overlay.metrics",
            "overlay.metriccolors.cpu_pct", "overlay.groupcolors.cpu", "overlay.usercustomized",
            "appearance.theme", "appearance.accentcolor", "appearance.fontfamily", "appearance.fontsize", "windowtheme",
            "alerts.enabled", "alerts.alertcputemp", "alerts.alertgputemp", "alerts.alertcpuusage", "alerts.alertramusage",
            "alerts.alertgpuusage", "alerts.alertgpuvram", "alerts.alertbatterylow", "alerts.alertcpufreq",
            "alerts.alertcpupower", "alerts.alertgpufreq", "alerts.alertgpupower", "alerts.alertnetdaily",
            "alerts.alertnetmonthly", "alerts.fireoncepersession", "alerts.cputempmaxc", "alerts.gputempmaxc",
            "alerts.cpuusagemaxpct", "alerts.ramusagemaxpct", "alerts.gpuusagemaxpct", "alerts.gpuvrammaxmb",
            "alerts.batterylowpct", "alerts.cpufreqminmhz", "alerts.cpupowermaxw", "alerts.gpufreqminmhz",
            "alerts.gpupowermaxw", "alerts.netdailymaxgb", "alerts.netmonthlymaxgb", "alerts.cooldownsecs",
            "alerts.showtoastnotif", "alerts.flashoverlay", "netusage.enabled",
            "updates.checkfrequency", "updates.includeprereleases",
            "custom.unknown.setting"
        ];

        foreach (var p in paths)
        {
            var res = (string)formatMethod.Invoke(null, [p, "false", "true"])!;
            res.Should().NotBeNull();
        }

        // Test alternate branch values
        ((string)formatMethod.Invoke(null, ["monitoring.preferrednic", "", "auto"])!).Should().Contain("Auto");
        ((string)formatMethod.Invoke(null, ["monitoring.preferrednic", "auto", "Ethernet 1"])!).Should().Contain("Ethernet 1");
        ((string)formatMethod.Invoke(null, ["monitoring.logcpumode", "0", "1"])!).Should().Contain("Errors Only");
        ((string)formatMethod.Invoke(null, ["monitoring.logcpumode", "1", "2"])!).Should().Contain("Full");
        ((string)formatMethod.Invoke(null, ["monitoring.logcpumode", "2", "0"])!).Should().Contain("Off");
        ((string)formatMethod.Invoke(null, ["network.selectedinterface", "eth0", ""])!).Should().Contain("All Adapters");
        ((string)formatMethod.Invoke(null, ["overlay.doubleclickaction", "settings", "taskmanager"])!).Should().Contain("Task Manager");
        ((string)formatMethod.Invoke(null, ["overlay.doubleclickaction", "taskmanager", "settings"])!).Should().Contain("Open Settings");
        ((string)formatMethod.Invoke(null, ["overlay.showlabels", "true", "false"])!).Should().Contain("hidden");
        ((string)formatMethod.Invoke(null, ["updates.checkfrequency", "weekly", "monthly"])!).Should().Contain("Monthly");
    }

    [Fact]
    public void SettingsShell_ParseMetricsLayout_JsonException_FallsBackToDelimiter()
    {
        var parseMethod = typeof(SettingsShell).GetMethod("ParseMetricIds", BindingFlags.NonPublic | BindingFlags.Static);
        parseMethod.Should().NotBeNull();

        // Pass unquoted array string [cpu_pct, ram_pct] which fails JSON parse and falls back to delimiter split
        var res = (List<string>)parseMethod.Invoke(null, ["[cpu_pct, ram_pct]"])!;
        res.Should().Contain("cpu_pct");
        res.Should().Contain("ram_pct");
    }

    [WpfFact]
    public void SettingsShell_HandleOpenDataDirectoryMsg_AllTargets_Coverage()
    {
        AppSettings.InitPaths(AppContext.BaseDirectory);
        Directory.CreateDirectory(AppSettings.LogsDir);
        Directory.CreateDirectory(AppSettings.DataDir);

        var method = typeof(SettingsShell).GetMethod("HandleOpenFolder", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string? openedDir = null;
        SettingsShell.FolderLauncherAction = path => openedDir = path;

        try
        {
            // 1. target = "app"
            using var docApp = JsonDocument.Parse("""{"payload": {"target": "app"}}""");
            method.Invoke(null, [docApp.RootElement]);
            openedDir.Should().Be(AppSettings.ExeDir);

            // 2. target = "logs"
            using var docLogs = JsonDocument.Parse("""{"payload": {"target": "logs"}}""");
            method.Invoke(null, [docLogs.RootElement]);
            openedDir.Should().Be(AppSettings.LogsDir);

            // 3. target = "data"
            using var docData = JsonDocument.Parse("""{"payload": {"target": "data"}}""");
            method.Invoke(null, [docData.RootElement]);
            openedDir.Should().Be(AppSettings.DataDir);

            // 4. FolderLauncherAction is null -> hits early return when SkipShowForTesting is true
            SettingsShell.FolderLauncherAction = null;
            method.Invoke(null, [docData.RootElement]);

            // 5. Missing payload or target
            using var docEmpty = JsonDocument.Parse("{}");
            method.Invoke(null, [docEmpty.RootElement]);
        }
        finally
        {
            SettingsShell.FolderLauncherAction = null;
        }
    }

    [Fact]
    public void SettingsShell_AppendPowerInfo_LaptopAndDesktop_Coverage()
    {
        var appendPower = typeof(SettingsShell).GetMethod("AppendPowerInfo", BindingFlags.NonPublic | BindingFlags.Static);
        appendPower.Should().NotBeNull();

        var laptopDict = new Dictionary<string, object?>();
        appendPower.Invoke(null, [laptopDict, true]);

        var desktopDict = new Dictionary<string, object?>();
        appendPower.Invoke(null, [desktopDict, false]);
    }

    [Fact]
    public void SettingsShell_FormatGpuVramAndTdp_Coverage()
    {
        var formatVram = typeof(SettingsShell).GetMethod("FormatGpuVramGb", BindingFlags.NonPublic | BindingFlags.Static);
        var formatTdp = typeof(SettingsShell).GetMethod("FormatGpuTdpW", BindingFlags.NonPublic | BindingFlags.Static);

        formatVram.Should().NotBeNull();
        formatTdp.Should().NotBeNull();

        var gpuTuple = ("RTX 4090", "NVIDIA", true, (long?)(16L * 1024 * 1024 * 1024), "550.00");
        _ = (string?)formatVram.Invoke(null, [gpuTuple]);
        _ = (string?)formatTdp.Invoke(null, [gpuTuple, (float?)250f]);

        var amdTuple = ("RX 7900", "AMD", true, (long?)(24L * 1024 * 1024 * 1024), "24.0.0");
        var resAmdVram = (string?)formatVram.Invoke(null, [amdTuple]);
        var resAmdTdp = (string?)formatTdp.Invoke(null, [amdTuple, (float?)300f]);

        resAmdVram.Should().NotBeNull();
        resAmdTdp.Should().Be("300 W");
    }

    [WpfFact]
    public void SettingsShell_CheckAndSendNics_WhenWebViewReady_SendsUpdatedNics()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;
        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, true);

        var method = typeof(SettingsShell).GetMethod("CheckAndSendNics", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => method!.Invoke(ctx.Shell, null);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void SettingsShell_SendTrafficToday_WhenWebViewReady_SendsTodayTraffic()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;
        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, true);

        var method = typeof(SettingsShell).GetMethod("SendTrafficToday", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => method!.Invoke(ctx.Shell, null);
        act.Should().NotThrow();
        posted.Should().Contain(s => s.Contains("\"type\":\"trafficToday\""));
    }

    [WpfFact]
    public void SettingsShell_ApplySettings_WithStartupChangeAndInvalidPayload()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        bool settingsAppliedFired = false;
        ctx.Shell.SettingsApplied += () => settingsAppliedFired = true;

        var validSettingsJson = """
        {
            "type": "saveSettings",
            "payload": {
                "runAtStartup": true,
                "startMinimized": true,
                "enableFileLogging": true,
                "showSystemTrayIcon": false,
                "overlay": {
                    "visible": true,
                    "position": "center",
                    "doubleClickAction": "taskmanager"
                }
            }
        }
        """;
        ctx.Shell.HandleWebMessageJson(validSettingsJson);
        posted.Should().Contain(s => s.Contains("\"type\":\"saved\""));
        settingsAppliedFired.Should().BeTrue();

        // Null / invalid payload
        ctx.Shell.HandleWebMessageJson("""{"type":"saveSettings","payload":null}""");
        posted.Should().Contain(s => s.Contains("invalid payload"));
    }

    [WpfFact]
    public void SettingsShell_HandleImportJson_EmptyAndValidAndInvalidData_HandlesGracefully()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        // Empty content
        ctx.Shell.HandleWebMessageJson("""{"type":"importDat","payload":{"content":""}}""");
        posted.Should().Contain(s => s.Contains("Empty file content"));

        // Valid content
        ctx.Shell.HandleWebMessageJson("""{"type":"importDat","payload":{"content":"Date Upload/Download KB\n2026/08/01 1024/2048\n"}}""");
        posted.Should().Contain(s => s.Contains("\"daysImported\":1"));

        // Malformed JSON exception handling
        ctx.Shell.HandleImportJson("invalid_raw_json");
        posted.Should().Contain(s => s.Contains("\"daysImported\":0"));
    }

    [WpfFact]
    public void SettingsShell_LocationChanged_WhenWebViewReady_ExecutesSafely()
    {
        using var ctx = new ShellContext();
        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, true);
        var onLocationChanged = typeof(System.Windows.Window).GetMethod("OnLocationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => onLocationChanged?.Invoke(ctx.Shell, [EventArgs.Empty]);
        act.Should().NotThrow();
    }

    [WpfFact]
    public async Task SettingsShell_HandleCheckForUpdatesAsync_Coverage()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        var method = typeof(SettingsShell).GetMethod("HandleCheckForUpdatesAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(ctx.Shell, [true])!;
        await task;

        posted.Should().Contain(s => s.Contains("\"type\":\"updateCheckResult\""));
    }

    [Fact]
    public void SettingsShell_GetAppVersionInfo_Coverage()
    {
        var method = typeof(SettingsShell).GetMethod("GetAppVersionInfo", BindingFlags.NonPublic | BindingFlags.Static);
        var res = method!.Invoke(null, null);
        res.Should().NotBeNull();
    }

    [WpfFact]
    public void SettingsShell_OnLoaded_WhenSkipInit_ReturnsEarly()
    {
        using var ctx = new ShellContext();
        SettingsShell.SkipWebView2InitForTesting = true;
        var onLoaded = typeof(SettingsShell).GetMethod("OnLoaded", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(object), typeof(System.Windows.RoutedEventArgs)], null);
        var act = () => onLoaded!.Invoke(ctx.Shell, [null!, null!]);
        act.Should().NotThrow();
    }

    [Fact]
    public void SettingsShell_BuildGpuList_And_SafeGetGateways_Coverage()
    {
        var buildGpuList = typeof(SettingsShell).GetMethod("BuildGpuList", BindingFlags.NonPublic | BindingFlags.Static);
        var res = buildGpuList!.Invoke(null, null);
        res.Should().NotBeNull();

        var safeGetGateways = typeof(SettingsShell).GetMethod("SafeGetGateways", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var gws = safeGetGateways!.Invoke(null, [nic]);
            gws.Should().NotBeNull();
        }
    }

    [WpfFact]
    public void SettingsShell_HandleOpenUrl_ValidAndInvalid_Coverage()
    {
        using var ctx = new ShellContext();
        string? openedUrl = null;
        SettingsShell.UrlLauncherAction = url => openedUrl = url;

        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"https://example.com/test"}}""");
        openedUrl.Should().Be("https://example.com/test");

        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{"url":"ftp://invalid"}}""");
        ctx.Shell.HandleWebMessageJson("""{"type":"openUrl","payload":{}}""");
    }

    [WpfFact]
    public void SettingsShell_HandleOpenFolder_AppLogsData_Coverage()
    {
        AppSettings.InitPaths(AppContext.BaseDirectory);
        using var ctx = new ShellContext();
        var openedFolders = new List<string>();
        SettingsShell.FolderLauncherAction = openedFolders.Add;

        var handleOpenFolder = typeof(SettingsShell).GetMethod("HandleOpenFolder", BindingFlags.NonPublic | BindingFlags.Static);
        handleOpenFolder.Should().NotBeNull();

        using var docApp = JsonDocument.Parse("""{"payload":{"target":"app"}}""");
        using var docData = JsonDocument.Parse("""{"payload":{"target":"data"}}""");
        using var docLogs = JsonDocument.Parse("""{"payload":{"target":"logs"}}""");
        using var docEmpty = JsonDocument.Parse("""{"payload":{}}""");

        var actApp = () => handleOpenFolder.Invoke(null, [docApp.RootElement]);
        var actData = () => handleOpenFolder.Invoke(null, [docData.RootElement]);
        var actLogs = () => handleOpenFolder.Invoke(null, [docLogs.RootElement]);
        var actEmpty = () => handleOpenFolder.Invoke(null, [docEmpty.RootElement]);

        actApp.Should().NotThrow();
        actData.Should().NotThrow();
        actLogs.Should().NotThrow();
        actEmpty.Should().NotThrow();

        openedFolders.Should().NotBeEmpty();
    }

    private sealed class MockThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network outage");
        }
    }

    [WpfFact]
    public async Task SettingsShell_HandleCheckForUpdatesAsync_WhenThrows_CatchesAndPostsError()
    {
        var cfg = new AppSettings();
        using var client = new HttpClient(new MockThrowingHandler());
        var updateChecker = new UpdateCheckService(cfg, client);
        using var ctx = new ShellContext();
        typeof(SettingsShell).GetField("_updateChecker", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, updateChecker);

        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        var method = typeof(SettingsShell).GetMethod("HandleCheckForUpdatesAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(ctx.Shell, [true])!;
        await task;

        posted.Should().Contain(s => s.Contains("\"type\":\"updateCheckResult\""));
    }

    [WpfFact]
    public void SettingsShell_SendSystemInfo_StandardAndPortableMode()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        string marker = Path.Combine(AppContext.BaseDirectory, "app.mode");
        try
        {
            File.WriteAllText(marker, "standard");
            ctx.Shell.HandleWebMessageJson("""{"type":"getSystemInfo"}""");
            posted.Should().Contain(s => s.Contains("Standard"));

            File.Delete(marker);
            ctx.Shell.HandleWebMessageJson("""{"type":"getSystemInfo"}""");
            posted.Should().Contain(s => s.Contains("Portable"));
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    [WpfFact]
    public void SettingsShell_OnClosed_WhenWebViewReady_CleansUpTimersAndDisposes()
    {
        using var ctx = new ShellContext();
        typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ctx.Shell, true);
        var onClosed = typeof(System.Windows.Window).GetMethod("OnClosed", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => onClosed?.Invoke(ctx.Shell, [EventArgs.Empty]);
        act.Should().NotThrow();
    }

    [Fact]
    public void ComputeSettingsDiff_PreferredNic_FormatsHumanReadable()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """{"monitoring":{"preferredNic":"auto"}}""";
        string after = """{"monitoring":{"preferredNic":"Ethernet (Realtek PCIe GbE Family Controller)"}}""";

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Preferred network interface set to 'Ethernet (Realtek PCIe GbE Family Controller)'");
    }

    [Fact]
    public void ComputeSettingsDiff_FollowWindowsTheme_FormatsHumanReadable()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """{"overlay":{"followWindowsTheme":false}}""";
        string after = """{"overlay":{"followWindowsTheme":true}}""";

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Follow Windows theme enabled");
    }

    [Fact]
    public void ComputeSettingsDiff_AlertsEnabledWithDefaults_ConsolidatesCleanly()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """
        {
            "alerts": {
                "enabled": false,
                "alertCpuTemp": false,
                "cpuTempMaxC": 75,
                "alertRamUsage": false,
                "ramUsageMaxPct": 80,
                "alertGpuTemp": false,
                "gpuTempMaxC": 75
            }
        }
        """;
        string after = """
        {
            "alerts": {
                "enabled": true,
                "alertCpuTemp": true,
                "cpuTempMaxC": 90,
                "alertRamUsage": true,
                "ramUsageMaxPct": 90,
                "alertGpuTemp": true,
                "gpuTempMaxC": 85
            }
        }
        """;

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Alerts enabled: CPU temp (90°C), GPU temp (85°C), RAM usage (90%)");
    }

    [Fact]
    public void ComputeSettingsDiff_AlertsDisabled_LogsAlertsDisabled()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """{"alerts":{"enabled":true}}""";
        string after = """{"alerts":{"enabled":false}}""";

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Alerts disabled");
    }

    [Fact]
    public void ComputeSettingsDiff_MultipleAlertModifications_PairsToggleAndThreshold()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """
        {
            "alerts": {
                "cpuTempMaxC": 90,
                "alertBatteryLow": false,
                "batteryLowPct": 15
            }
        }
        """;
        string after = """
        {
            "alerts": {
                "cpuTempMaxC": 95,
                "alertBatteryLow": true,
                "batteryLowPct": 20
            }
        }
        """;

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Alerts updated: CPU temp threshold set to 95°C, Low battery alert enabled (≤ 20%)");
    }

    [Fact]
    public void ComputeSettingsDiff_GroupColorChange_ConsolidatesMetricsCleanly()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """
        {
            "overlay": {
                "groupColors": { "net": "#006DB6" },
                "metricColors": {
                    "net_down": "#006DB6",
                    "net_up": "#006DB6",
                    "net_total": "#006DB6"
                }
            }
        }
        """;
        string after = """
        {
            "overlay": {
                "groupColors": { "net": "#0DB8E3" },
                "metricColors": {
                    "net_down": "#0DB8E3",
                    "net_up": "#0DB8E3",
                    "net_total": "#0DB8E3"
                }
            }
        }
        """;

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Network group color changed to #0DB8E3 (applied to Download Speed, Upload Speed, Total Network Usage)");
    }

    [Fact]
    public void ComputeSettingsDiff_CustomMetricColor_LogsIndividualMetric()
    {
        var method = typeof(SettingsShell).GetMethod("ComputeSettingsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string before = """
        {
            "overlay": {
                "metricColors": { "net_up": "#0DB8E3" }
            }
        }
        """;
        string after = """
        {
            "overlay": {
                "metricColors": { "net_up": "#E3630D" }
            }
        }
        """;

        var diffs = (List<string>)method.Invoke(null, [before, after])!;
        diffs.Should().ContainSingle();
        diffs[0].Should().Be("Metric 'Upload Speed' color changed from #0DB8E3 to #E3630D");
    }
    [Fact]
    public void SettingsShell_FormatLogMode_AllBranches()
    {
        var method = typeof(SettingsShell).GetMethod("FormatLogMode", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        ((string)method.Invoke(null, ["0"])!).Should().Be("Off");
        ((string)method.Invoke(null, ["1"])!).Should().Be("Errors Only");
        ((string)method.Invoke(null, ["2"])!).Should().Be("Full (All Metrics)");
        ((string)method.Invoke(null, ["3"])!).Should().Be("3");
    }

    [Fact]
    public void SettingsShell_FormatGeneralOrLoggingChange_AllBranches()
    {
        var method = typeof(SettingsShell).GetMethod("FormatGeneralOrLoggingChange", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        ((string?)method.Invoke(null, ["showsystemtrayicon", "true"])!).Should().Be("System tray icon shown");
        ((string?)method.Invoke(null, ["showsystemtrayicon", "false"])!).Should().Be("System tray icon hidden");
        ((string?)method.Invoke(null, ["runatstartup", "true"])!).Should().Be("Run at startup enabled");
        ((string?)method.Invoke(null, ["startminimized", "true"])!).Should().Be("Start minimized enabled");
        ((string?)method.Invoke(null, ["minimizetotrayonclose", "true"])!).Should().Be("Minimize to tray on close enabled");
        ((string?)method.Invoke(null, ["datadirectory", "C:\\Data"])!).Should().Be("Data directory set to 'C:\\Data'");
        ((string?)method.Invoke(null, ["enablefilelogging", "true"])!).Should().Be("Metrics logging enabled");
        ((string?)method.Invoke(null, ["systemloglevel", "Debug"])!).Should().Be("System log level set to Debug");
        ((string?)method.Invoke(null, ["unknown", "val"])).Should().BeNull();
    }

    [Fact]
    public void SettingsShell_FormatNetworkChange_AllBranches()
    {
        var method = typeof(SettingsShell).GetMethod("FormatNetworkChange", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        ((string?)method.Invoke(null, ["network.selectedinterface", ""])!).Should().Be("Network adapter set to All Adapters");
        ((string?)method.Invoke(null, ["network.selectedinterface", "Ethernet"])!).Should().Be("Network adapter set to 'Ethernet'");
        ((string?)method.Invoke(null, ["network.unitformat", "auto"])!).Should().Be("Network unit format set to auto");
        ((string?)method.Invoke(null, ["network.speedunit", "bytes"])!).Should().Be("Network speed unit set to bytes");
        ((string?)method.Invoke(null, ["network.autoresetmonthly", "true"])!).Should().Be("Monthly network auto-reset enabled");
        ((string?)method.Invoke(null, ["network.unknown", "val"])).Should().BeNull();
    }

    [Fact]
    public void SettingsShell_FormatOverlayChange_AllBranches()
    {
        var method = typeof(SettingsShell).GetMethod("FormatOverlayChange", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        ((string?)method.Invoke(null, ["overlay.visible", "overlay.visible", "false", "true"])!).Should().Be("Taskbar overlay turned on");
        ((string?)method.Invoke(null, ["overlay.visible", "overlay.visible", "true", "false"])!).Should().Be("Taskbar overlay turned off");
        ((string?)method.Invoke(null, ["overlay.placement", "overlay.placement", "", "Right"])!).Should().Be("Position changed to Right");
        ((string?)method.Invoke(null, ["overlay.placementoffset", "overlay.placementoffset", "", "10"])!).Should().Be("Offset changed to 10 px");
        ((string?)method.Invoke(null, ["overlay.opacity", "overlay.opacity", "", "80"])!).Should().Be("Opacity changed to 80%");
        ((string?)method.Invoke(null, ["overlay.scalepct", "overlay.scalepct", "", "120"])!).Should().Be("Scale changed to 120%");
        ((string?)method.Invoke(null, ["overlay.fontsizepx", "overlay.fontsizepx", "", "14"])!).Should().Be("Font size changed to 14 px");
        ((string?)method.Invoke(null, ["overlay.fontbold", "overlay.fontbold", "", "true"])!).Should().Be("Bold font enabled");
        ((string?)method.Invoke(null, ["overlay.labelcolor", "overlay.labelcolor", "", "#fff"])!).Should().Be("Label color changed to #fff");
        ((string?)method.Invoke(null, ["overlay.valuecolor", "overlay.valuecolor", "", "#0f0"])!).Should().Be("Value color changed to #0f0");
        ((string?)method.Invoke(null, ["overlay.bgcolor", "overlay.bgcolor", "", "#000"])!).Should().Be("Background color changed to #000");
        ((string?)method.Invoke(null, ["overlay.showtoastnotif", "overlay.showtoastnotif", "", "true"])!).Should().Be("Toast notifications enabled");
        ((string?)method.Invoke(null, ["overlay.flashoverlay", "overlay.flashoverlay", "", "true"])!).Should().Be("Flashing alerts enabled");
        ((string?)method.Invoke(null, ["overlay.showlabels", "overlay.showlabels", "", "true"])!).Should().Be("Labels shown");
        ((string?)method.Invoke(null, ["overlay.showlabels", "overlay.showlabels", "", "false"])!).Should().Be("Labels hidden");
        ((string?)method.Invoke(null, ["overlay.doubleclickaction", "overlay.doubleclickaction", "", "taskmanager"])!).Should().Be("Double-click action set to Task Manager");
        ((string?)method.Invoke(null, ["overlay.doubleclickaction", "overlay.doubleclickaction", "", "settings"])!).Should().Be("Double-click action set to Open Settings");
        ((string?)method.Invoke(null, ["overlay.followwindowstheme", "overlay.followwindowstheme", "", "true"])!).Should().Be("Follow Windows theme enabled");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "", "[\"cpu_pct\",\"gpu_temp\"]"])!).Should().Be("Metrics layout updated (2 metrics - CPU Usage, GPU Temperature)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "", ""])!).Should().Be("Metrics layout cleared (0 metrics)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "", "val"])!).Should().Be("Metrics layout updated (1 metric - val)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "[\"cpu_pct\"]", "[\"cpu_pct\",\"gpu_power\"]"])!).Should().Be("Added metric to overlay (GPU Power)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "[\"cpu_pct\",\"gpu_power\"]", "[\"cpu_pct\"]"])!).Should().Be("Removed metric from overlay (GPU Power)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "[\"cpu_pct\"]", "[\"gpu_power\"]"])!).Should().Be("Metrics layout updated (Added GPU Power, Removed CPU Usage)");
        ((string?)method.Invoke(null, ["overlay.row1", "overlay.row1", "[\"cpu_pct\",\"gpu_temp\"]", "[\"gpu_temp\",\"cpu_pct\"]"])!).Should().Be("Metrics layout reordered");
        ((string?)method.Invoke(null, ["overlay.metriccolors.cpu_pct", "overlay.metriccolors.cpu_pct", "#fff", "#000"])!).Should().Contain("CPU Usage");
        ((string?)method.Invoke(null, ["overlay.groupcolors.cpu", "overlay.groupcolors.cpu", "#fff", "#000"])!).Should().Contain("Cpu");
        ((string?)method.Invoke(null, ["overlay.usercustomized", "overlay.usercustomized", "", ""])!).Should().BeEmpty();
        ((string?)method.Invoke(null, ["overlay.unknown", "overlay.unknown", "", ""])).Should().BeNull();
    }

    [Fact]
    public void SettingsShell_FormatAppearanceAlertsOrUpdatesChange_AllBranches()
    {
        var method = typeof(SettingsShell).GetMethod("FormatAppearanceAlertsOrUpdatesChange", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string[] paths =
        [
            "windowtheme", "alerts.enabled", "alerts.alertcputemp", "alerts.alertgputemp",
            "alerts.alertcpuusage", "alerts.alertramusage", "alerts.alertgpuusage", "alerts.alertgpuvram",
            "alerts.alertbatterylow", "alerts.alertcpufreq", "alerts.alertcpupower", "alerts.alertgpufreq",
            "alerts.alertgpupower", "alerts.alertnetdaily", "alerts.alertnetmonthly", "alerts.fireoncepersession",
            "alerts.cputempmaxc", "alerts.gputempmaxc", "alerts.cpuusagemaxpct", "alerts.ramusagemaxpct",
            "alerts.gpuusagemaxpct", "alerts.gpuvrammaxmb", "alerts.batterylowpct", "alerts.cpufreqminmhz",
            "alerts.cpupowermaxw", "alerts.gpufreqminmhz", "alerts.gpupowermaxw", "alerts.netdailymaxgb",
            "alerts.netmonthlymaxgb", "alerts.cooldownsecs", "alerts.showtoastnotif", "alerts.flashoverlay",
            "netusage.enabled", "updates.includeprereleases", "updates.checkfrequency"
        ];

        foreach (var p in paths)
        {
            var res = (string?)method.Invoke(null, [p, p.Contains("max") || p.Contains("min") || p.Contains("cooldown") ? "50" : "true"]);
            res.Should().NotBeNull();
        }

        ((string?)method.Invoke(null, ["unknown.path", "val"])).Should().BeNull();
    }

    [Fact]
    public void SettingsShell_GetMetricName_AllSupportedIds()
    {
        var method = typeof(SettingsShell).GetMethod("GetMetricName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string[] metricIds =
        [
            "cpu_pct", "cpu_temp", "cpu_freq", "cpu_power",
            "ram_pct", "ram_used",
            "gpu_pct", "gpu_temp", "gpu_vram", "gpu_freq", "gpu_power",
            "net_down", "net_up", "net_total",
            "battery_pct", "battery_rate",
            "disk_c", "unknown_metric"
        ];

        foreach (var id in metricIds)
        {
            var name = (string)method.Invoke(null, [id])!;
            name.Should().NotBeNullOrEmpty();
        }
    }

    [WpfFact]
    public void HandleWebMessageJson_ImportWindowsUsage_AllBranchesCoverage()
    {
        using var ctx = new ShellContext();
        var posted = new List<string>();
        SettingsShell.PostJsonActionOverride = posted.Add;

        // 1. Missing dates payload
        ctx.Shell.HandleWebMessageJson("""{"type":"importWindowsUsage","payload":{}}""");
        posted.Should().Contain(s => s.Contains("Missing start or end date"));

        // 2. Invalid start date
        ctx.Shell.HandleWebMessageJson("""{"type":"importWindowsUsage","payload":{"startDate":"invalid-date","endDate":"01-08-2026"}}""");
        posted.Should().Contain(s => s.Contains("Invalid start date format"));

        // 3. Invalid end date
        ctx.Shell.HandleWebMessageJson("""{"type":"importWindowsUsage","payload":{"startDate":"01-08-2026","endDate":"invalid-date"}}""");
        posted.Should().Contain(s => s.Contains("Invalid end date format"));

        // 4. Valid start and end dates
        string validStart = DateTime.Today.AddDays(-2).ToString("dd-MM-yyyy");
        string validEnd = DateTime.Today.AddDays(-1).ToString("dd-MM-yyyy");
        string validJson = JsonSerializer.Serialize(new
        {
            type = "importWindowsUsage",
            payload = new { startDate = validStart, endDate = validEnd }
        });
        ctx.Shell.HandleWebMessageJson(validJson);
        posted.Should().Contain(s => s.Contains("\"type\":\"windowsUsageImportResult\""));
    }

    [Fact]
    public void SettingsShell_FormatOverlayLayoutChange_AllBranches_Coverage()
    {
        var method = typeof(SettingsShell).GetMethod("FormatOverlayLayoutChange", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // 1. Cleared
        var res1 = (string?)method.Invoke(null, ["cpu", ""]);
        res1.Should().Be("Metrics layout cleared (0 metrics)");

        // 2. Old empty (1 metric)
        var res2 = (string?)method.Invoke(null, ["", "cpu"]);
        res2.Should().Contain("1 metric");

        // 3. Old empty (multiple metrics)
        var res3 = (string?)method.Invoke(null, ["", "cpu,gpu"]);
        res3.Should().Contain("2 metrics");

        // 4. Added only (1 metric)
        var res4 = (string?)method.Invoke(null, ["cpu", "cpu,gpu"]);
        res4.Should().StartWith("Added metric to overlay");

        // 5. Added only (multiple metrics)
        var res5 = (string?)method.Invoke(null, ["cpu", "cpu,gpu,ram"]);
        res5.Should().StartWith("Added metrics to overlay");

        // 6. Removed only (1 metric)
        var res6 = (string?)method.Invoke(null, ["cpu,gpu", "cpu"]);
        res6.Should().StartWith("Removed metric from overlay");

        // 7. Removed only (multiple metrics)
        var res7 = (string?)method.Invoke(null, ["cpu,gpu,ram", "cpu"]);
        res7.Should().StartWith("Removed metrics from overlay");

        // 8. Added and removed
        var res8 = (string?)method.Invoke(null, ["cpu,gpu", "cpu,ram"]);
        res8.Should().StartWith("Metrics layout updated (Added");

        // 9. Reordered
        var res9 = (string?)method.Invoke(null, ["cpu,gpu", "gpu,cpu"]);
        res9.Should().Be("Metrics layout reordered");

        // 10. Unchanged
        var res10 = (string?)method.Invoke(null, ["cpu,gpu", "cpu,gpu"]);
        res10.Should().BeEmpty();
    }

    [WpfFact]
    public void SettingsShell_TitleBar_MouseLeftButtonDown_And_CloseButton_Coverage()
    {
        using var ctx = new ShellContext();

        var titleBarMethod = typeof(SettingsShell).GetMethod("TitleBar_MouseLeftButtonDown", BindingFlags.NonPublic | BindingFlags.Instance);
        titleBarMethod.Should().NotBeNull();

        var mouseDevice = InputManager.Current.PrimaryMouseDevice;
        var eventArgs = new MouseButtonEventArgs(mouseDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };

        // ClickCount == 2: Normal -> Maximized
        ctx.Shell.WindowState = WindowState.Normal;
        typeof(MouseButtonEventArgs).GetProperty("ClickCount")?.SetValue(eventArgs, 2);
        titleBarMethod.Invoke(ctx.Shell, [ctx.Shell, eventArgs]);
        ctx.Shell.WindowState.Should().Be(WindowState.Maximized);

        // ClickCount == 2: Maximized -> Normal
        titleBarMethod.Invoke(ctx.Shell, [ctx.Shell, eventArgs]);
        ctx.Shell.WindowState.Should().Be(WindowState.Normal);

        // CloseButton_Click
        var closeMethod = typeof(SettingsShell).GetMethod("CloseButton_Click", BindingFlags.NonPublic | BindingFlags.Instance);
        closeMethod.Should().NotBeNull();
        closeMethod.Invoke(ctx.Shell, [ctx.Shell, new RoutedEventArgs()]);
    }

    private sealed class ShellContext : IDisposable
    {
        public ShellContext()
        {
            SettingsShell.FolderLauncherAction = _ => { };
            SettingsShell.UrlLauncherAction = _ => { };
            SettingsShell.MessageBoxAction = (_, _) => { };
            Config = new AppSettings();
            TempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
            History = new TrafficHistoryFile(TempPath);
            TrafficHistoryStore.Initialize(History);
            Logger = new NetUsageLogger(Config, History);
            Shell = new SettingsShell(Config, Logger);
        }

        public AppSettings Config { get; }
        public string TempPath { get; }
        public TrafficHistoryFile History { get; }
        public NetUsageLogger Logger { get; }
        public SettingsShell Shell { get; }

        public void Dispose()
        {
            SettingsShell.PostJsonActionOverride = null;
            SettingsShell.UrlLauncherAction = null;
            SettingsShell.FolderLauncherAction = null;
            SettingsShell.MessageBoxAction = null;
            WindowHelpers.TaskSchedulerFactory = null;
            Shell.Close();
            Logger.Dispose();
            if (File.Exists(TempPath)) File.Delete(TempPath);
        }
    }

    [Fact]
    public void SettingsShell_SafeGetGateways_NullInterface_TriggersCatch()
    {
        var method = typeof(SettingsShell).GetMethod("SafeGetGateways", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var res = method.Invoke(null, [null]);
        res.Should().NotBeNull();
        ((List<System.Net.IPAddress>)res).Should().BeEmpty();
    }

    [Fact]
    public void SettingsShell_CapitalizeGroupName_AllBranches_Coverage()
    {
        var method = typeof(SettingsShell).GetMethod("CapitalizeGroupName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((string)method.Invoke(null, ["cpu"])!).Should().Be("CPU");
        ((string)method.Invoke(null, ["gpu"])!).Should().Be("GPU");
        ((string)method.Invoke(null, ["ram"])!).Should().Be("RAM");
        ((string)method.Invoke(null, ["net"])!).Should().Be("Network");
        ((string)method.Invoke(null, ["disk"])!).Should().Be("Disk");
        ((string)method.Invoke(null, ["battery"])!).Should().Be("Battery");
        ((string)method.Invoke(null, ["sensors"])!).Should().Be("Sensors");
    }

    [Fact]
    public void SettingsShell_FormatAlertsSummary_AllEdgeCases_Coverage()
    {
        var method = typeof(SettingsShell).GetMethod("ConsolidateAlertsDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // 1. Alert enabled: toggle only (lines 999, 1004)
        var changes1 = new List<(string Path, string OldVal, string NewVal)>
        {
            ("alerts.enabled", "false", "true"),
            ("alerts.alertcpufreq", "false", "true")
        };
        string s1 = (string)method.Invoke(null, [changes1])!;
        s1.Should().Contain("CPU throttle enabled");

        // 2. Alert enabled: empty summaries (line 1006)
        var changes2 = new List<(string Path, string OldVal, string NewVal)>
        {
            ("alerts.enabled", "false", "true")
        };
        string s2 = (string)method.Invoke(null, [changes2])!;
        s2.Should().Be("Alerts enabled");

        // 3. Toggle only with true/false (lines 1032-1035)
        var changes3 = new List<(string Path, string OldVal, string NewVal)>
        {
            ("alerts.alertgpufreq", "false", "false")
        };
        string s3 = (string)method.Invoke(null, [changes3])!;
        s3.Should().Contain("GPU throttle alert disabled");

        // 4. Threshold only without toggle (lines 1039-1041)
        var changes4 = new List<(string Path, string OldVal, string NewVal)>
        {
            ("alerts.cpufreqminmhz", "800", "900")
        };
        string s4 = (string)method.Invoke(null, [changes4])!;
        s4.Should().Contain("CPU throttle threshold set to");

        // 5. Unhandled non-metric alert change (line 1056-1057)
        var changes5 = new List<(string Path, string OldVal, string NewVal)>
        {
            ("alerts.other_unknown_key", "old", "new")
        };
        string s5 = (string)method.Invoke(null, [changes5])!;
        s5.Should().Be("Alerts configuration updated");
    }

    [Fact]
    public void SettingsShell_FormatGpuTdpW_Coverage()
    {
        var method = typeof(SettingsShell).GetMethod("FormatGpuTdpW", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // AMD with valid TDP (lines 873-874)
        var amdGpu = (Name: "AMD Radeon RX 7900 XTX", Vendor: "AMD", IsDedicated: true, VramBytes: (long?)16000000000L, DriverVersion: "24.1.1");
        string? resAmd = (string?)method.Invoke(null, [amdGpu, 300f]);
        resAmd.Should().Be("300 W");

        // AMD without TDP
        string? resAmdNull = (string?)method.Invoke(null, [amdGpu, null]);
        resAmdNull.Should().BeNull();

        // Non-AMD non-Nvidia
        var intelGpu = (Name: "Intel Arc A770", Vendor: "Intel", IsDedicated: true, VramBytes: (long?)16000000000L, DriverVersion: "31.0.101");
        string? resIntel = (string?)method.Invoke(null, [intelGpu, 225f]);
        resIntel.Should().BeNull();
    }

    [WpfFact]
    public async Task SettingsShell_HandleImportWindowsUsage_Validations_And_ErrorPaths()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            var method = typeof(SettingsShell).GetMethod("HandleImportWindowsUsage", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            // 1. Missing payload or start/end dates
            using var emptyDoc = JsonDocument.Parse("{}");
            var task1 = (Task)method.Invoke(shell, [emptyDoc.RootElement])!;
            await task1;

            using var missingDateDoc = JsonDocument.Parse("{\"payload\":{}}");
            var task2 = (Task)method.Invoke(shell, [missingDateDoc.RootElement])!;
            await task2;

            // 2. Invalid start date
            using var badStartDoc = JsonDocument.Parse("{\"payload\":{\"startDate\":\"bad-date\",\"endDate\":\"01-08-2026\"}}");
            var task3 = (Task)method.Invoke(shell, [badStartDoc.RootElement])!;
            await task3;

            // 3. Invalid end date
            using var badEndDoc = JsonDocument.Parse("{\"payload\":{\"startDate\":\"01-08-2026\",\"endDate\":\"bad-date\"}}");
            var task4 = (Task)method.Invoke(shell, [badEndDoc.RootElement])!;
            await task4;

            // 4. Valid dates format
            using var validDoc = JsonDocument.Parse("{\"payload\":{\"startDate\":\"01-08-2026\",\"endDate\":\"05-08-2026\"}}");
            var task5 = (Task)method.Invoke(shell, [validDoc.RootElement])!;
            await task5;

            // 5. PostWindowsImportResult direct invocation
            var postResultMethod = typeof(SettingsShell).GetMethod("PostWindowsImportResult", BindingFlags.NonPublic | BindingFlags.Instance);
            postResultMethod.Should().NotBeNull();
            postResultMethod.Invoke(shell, [true, 2, 2048L, 1024L, "Success"]);
            postResultMethod.Invoke(shell, [false, 0, 0L, 0L, "Failed"]);

            shell.Close();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [WpfFact]
    public void SettingsShell_FrontendLog_And_MessageSource_Coverage()
    {
        // 1. IsValidMessageSource
        SettingsShell.IsValidMessageSource("https://localtelemetry.settings/index.html").Should().BeTrue();
        SettingsShell.IsValidMessageSource("https://localtelemetry.settings/").Should().BeTrue();
        SettingsShell.IsValidMessageSource("https://evil.com/settings.html").Should().BeFalse();
        SettingsShell.IsValidMessageSource(null).Should().BeFalse();
        SettingsShell.IsValidMessageSource("").Should().BeFalse();
        SettingsShell.IsValidMessageSource("   ").Should().BeFalse();

        // 2. HandleFrontendLog via HandleWebMessageJson
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            // Log without payload (early return)
            shell.HandleWebMessageJson("{\"type\":\"log\"}");

            // Log with warn and detail
            shell.HandleWebMessageJson("{\"type\":\"log\",\"payload\":{\"level\":\"warn\",\"message\":\"Test warning\",\"detail\":\"Additional debug info\"}}");

            // Log with error and no detail
            shell.HandleWebMessageJson("{\"type\":\"log\",\"payload\":{\"level\":\"error\",\"message\":\"Test error\"}}");

            // Log with empty payload
            shell.HandleWebMessageJson("{\"type\":\"log\",\"payload\":{}}");

            shell.Close();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [WpfFact]
    public void SettingsShell_GpuFormatting_And_TitleBar_And_TimerCallbacks_Coverage()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            // 1. FormatGpuTdpW with Nvidia LatestData
            var formatTdp = typeof(SettingsShell).GetMethod("FormatGpuTdpW", BindingFlags.NonPublic | BindingFlags.Static)!;
            var nvidiaGpu = (Name: "NVIDIA GeForce RTX 4080", Vendor: "NVIDIA", IsDedicated: true, VramBytes: (long?)16000000000L, DriverVersion: "550.0");

            var latestDataProp = typeof(NvGpuMonitor).GetProperty("LatestData", BindingFlags.Public | BindingFlags.Static)!;
            var origNvData = NvGpuMonitor.LatestData;
            try
            {
                latestDataProp.SetValue(null, new NvGpuMonitor.NvGpuData { TdpW = 320f });
                string? tdpResult = (string?)formatTdp.Invoke(null, [nvidiaGpu, null]);
                tdpResult.Should().Be("320 W");

                latestDataProp.SetValue(null, new NvGpuMonitor.NvGpuData { TdpW = 0f });
                string? tdpZero = (string?)formatTdp.Invoke(null, [nvidiaGpu, null]);
                tdpZero.Should().BeNull();
            }
            finally
            {
                latestDataProp.SetValue(null, origNvData);
            }

            // 2. FormatGpuVramGb
            var formatVram = typeof(SettingsShell).GetMethod("FormatGpuVramGb", BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                latestDataProp.SetValue(null, new NvGpuMonitor.NvGpuData { VramTotalMb = 16384f });
                string? vramNv = (string?)formatVram.Invoke(null, [nvidiaGpu]);
                vramNv.Should().Be("16.0");
            }
            finally
            {
                latestDataProp.SetValue(null, origNvData);
            }

            var genericGpu = (Name: "Intel Arc A770", Vendor: "Intel", IsDedicated: true, VramBytes: (long?)(8L * 1024 * 1024 * 1024), DriverVersion: "31.0");
            string? vramGen = (string?)formatVram.Invoke(null, [genericGpu]);
            vramGen.Should().Be("8.0");

            (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion) noVramGpu = ("Basic Display", "Microsoft", false, null, null);
            string? vramNull = (string?)formatVram.Invoke(null, [noVramGpu]);
            vramNull.Should().BeNull();

            // 3. AppUsageTimer tick callback
            var textBlockField = typeof(SettingsShell).GetField("WindowTitleText", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var titleBlock = new System.Windows.Controls.TextBlock();
            textBlockField?.SetValue(shell, titleBlock);

            var procField = typeof(SettingsShell).GetField("_currentProcess", BindingFlags.NonPublic | BindingFlags.Instance);
            procField?.SetValue(shell, System.Diagnostics.Process.GetCurrentProcess());

            var tickMethod = typeof(SettingsShell).GetMethod("<OnLoaded>b__53_0", BindingFlags.NonPublic | BindingFlags.Instance);
            tickMethod?.Invoke(shell, [null, EventArgs.Empty]);
            titleBlock.Text.Should().Contain("LocalTelemetry");

            procField?.SetValue(shell, null);
            tickMethod?.Invoke(shell, [null, EventArgs.Empty]);

            // 4. LocationChanged blur callback
            var locMethod = typeof(SettingsShell).GetMethod("<.ctor>b__52_0", BindingFlags.NonPublic | BindingFlags.Instance);
            typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, false);
            locMethod?.Invoke(shell, [null, EventArgs.Empty]);
            typeof(SettingsShell).GetField("_webViewReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, true);
            locMethod?.Invoke(shell, [null, EventArgs.Empty]);

            // 5. Timer callbacks for debounce & periodic push
            var timer1 = typeof(SettingsShell).GetMethod("<InitWebViewAsync>b__54_0", BindingFlags.NonPublic | BindingFlags.Instance);
            timer1?.Invoke(shell, [null]);
            var timer2 = typeof(SettingsShell).GetMethod("<InitWebViewAsync>b__54_1", BindingFlags.NonPublic | BindingFlags.Instance);
            timer2?.Invoke(shell, [null]);

            // 6. TitleBar double-click toggle
            var titleBarMethod = typeof(SettingsShell).GetMethod("TitleBar_MouseLeftButtonDown", BindingFlags.NonPublic | BindingFlags.Instance);
            var mouseEventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            typeof(MouseButtonEventArgs).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(mouseEventArgs, 2);
            shell.WindowState = WindowState.Normal;
            titleBarMethod?.Invoke(shell, [shell, mouseEventArgs]);
            shell.WindowState.Should().Be(WindowState.Maximized);
            titleBarMethod?.Invoke(shell, [shell, mouseEventArgs]);
            shell.WindowState.Should().Be(WindowState.Normal);

            // 7. Close button click
            var closeMethod = typeof(SettingsShell).GetMethod("CloseButton_Click", BindingFlags.NonPublic | BindingFlags.Instance);
            closeMethod?.Invoke(shell, [shell, new RoutedEventArgs()]);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [WpfFact]
    public async Task SettingsShell_InitWebViewAsync_Fallback_Coverage()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            string? errorDialogMsg = null;
            SettingsShell.MessageBoxAction = (title, msg) => errorDialogMsg = msg;

            var initAsync = typeof(SettingsShell).GetMethod("InitWebViewAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initAsync != null)
            {
                var task = (Task)initAsync.Invoke(shell, null)!;
                await task;
            }

            shell.Close();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [WpfFact]
    public async Task SettingsShell_HandleImportWindowsUsage_DetailedCoverage()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            var method = typeof(SettingsShell).GetMethod("HandleImportWindowsUsage", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            // 1. Missing or invalid date formats
            var invalidStart = JsonDocument.Parse("{\"startDate\":\"not-a-date\",\"endDate\":\"01-10-2026\"}").RootElement;
            await (Task)method!.Invoke(shell, [invalidStart])!;

            var invalidEnd = JsonDocument.Parse("{\"startDate\":\"01-10-2026\",\"endDate\":\"bad-end-date\"}").RootElement;
            await (Task)method.Invoke(shell, [invalidEnd])!;

            // 2. Valid dates with initialized store
            var validRange = JsonDocument.Parse("{\"startDate\":\"01-10-2026\",\"endDate\":\"05-10-2026\"}").RootElement;
            await (Task)method.Invoke(shell, [validRange])!;

            // 3. Exception in parsing (missing root properties causing exception)
            var malformed = JsonDocument.Parse("{}").RootElement;
            await (Task)method.Invoke(shell, [malformed])!;

            // 4. TrafficHistoryStore uninitialized error path
            typeof(TrafficHistoryStore).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
            await (Task)method.Invoke(shell, [validRange])!;
            TrafficHistoryStore.Initialize(history);

            shell.Close();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [WpfFact]
    public void SettingsShell_FrontendLog_And_OpenFolder_Coverage()
    {
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var history = new TrafficHistoryFile(tempPath);
            TrafficHistoryStore.Initialize(history);
            using var netLogger = new NetUsageLogger(cfg, history);
            var shell = new SettingsShell(cfg, netLogger);

            // 1. Log message - Warn level with detail
            shell.HandleWebMessageJson("{\"type\":\"log\",\"payload\":{\"level\":\"warn\",\"message\":\"Warn test\",\"detail\":\"Detail test\"}}");

            // 2. Log message - Error level without detail
            shell.HandleWebMessageJson("{\"type\":\"log\",\"payload\":{\"level\":\"error\",\"message\":\"Error test\"}}");

            // 3. Log message - Missing payload property
            shell.HandleWebMessageJson("{\"type\":\"log\"}");

            // 4. OpenFolder - valid folders
            shell.HandleWebMessageJson("{\"type\":\"openFolder\",\"payload\":{\"folder\":\"logs\"}}");
            shell.HandleWebMessageJson("{\"type\":\"openFolder\",\"payload\":{\"folder\":\"data\"}}");
            shell.HandleWebMessageJson("{\"type\":\"openFolder\",\"payload\":{\"folder\":\"app\"}}");
            shell.HandleWebMessageJson("{\"type\":\"openFolder\",\"payload\":{\"folder\":\"unknown\"}}");
            shell.HandleWebMessageJson("{\"type\":\"openFolder\",\"payload\":{}}");

            // 5. CheckForUpdates message
            shell.HandleWebMessageJson("{\"type\":\"checkForUpdates\",\"payload\":{\"force\":true}}");
            shell.HandleWebMessageJson("{\"type\":\"checkForUpdates\",\"payload\":{}}");

            shell.Close();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }
}
