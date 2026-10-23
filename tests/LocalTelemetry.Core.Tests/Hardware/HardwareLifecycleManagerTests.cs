using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class HardwareLifecycleManagerTests
{
    [Fact]
    public void HardwareLifecycleManager_NotifyDisplayChanged_FiresDisplayOrDriverChangedEvent()
    {
        using var manager = new HardwareLifecycleManager();
        bool fired = false;
        manager.DisplayOrDriverChanged += () => fired = true;

        manager.NotifyDisplayChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void HardwareLifecycleManager_NotifyDevicePnPChanged_FiresDevicePnPChangedEvent()
    {
        using var manager = new HardwareLifecycleManager();
        bool fired = false;
        manager.DevicePnPChanged += () => fired = true;

        manager.NotifyDevicePnPChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void HardwareLifecycleManager_Instance_ReturnsNonNullSingleton()
    {
        var instance = HardwareLifecycleManager.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void HardwareLifecycleManager_Dispose_UnsubscribesSafely()
    {
        var manager = new HardwareLifecycleManager();
        var act = () => manager.Dispose();
        act.Should().NotThrow();

        // Calling Dispose twice hits the early return
        var act2 = () => manager.Dispose();
        act2.Should().NotThrow();
    }

    [Fact]
    public void HardwareLifecycleManager_OnPowerModeChanged_HandlesSuspendAndResume()
    {
        using var manager = new HardwareLifecycleManager();
        bool suspended = false;
        bool resumed = false;
        manager.PowerSuspended += () => suspended = true;
        manager.PowerResumed += () => resumed = true;

        var method = typeof(HardwareLifecycleManager).GetMethod("OnPowerModeChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        // 1. Suspend
        var suspendArgs = new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Suspend);
        method.Invoke(manager, [null, suspendArgs]);
        suspended.Should().BeTrue();

        // While suspended, notifications early-return
        bool displayFired = false;
        manager.DisplayOrDriverChanged += () => displayFired = true;
        manager.NotifyDisplayChanged();
        displayFired.Should().BeFalse();

        bool pnpFired = false;
        manager.DevicePnPChanged += () => pnpFired = true;
        manager.NotifyDevicePnPChanged();
        pnpFired.Should().BeFalse();

        // 2. Resume
        var resumeArgs = new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Resume);
        method.Invoke(manager, [null, resumeArgs]);
        resumed.Should().BeTrue();

        // 3. Other status change
        var statusArgs = new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.StatusChange);
        var act = () => method.Invoke(manager, [null, statusArgs]);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareLifecycleManager_OnSessionEnding_LogsKnownAndUnknownReasons()
    {
        using var manager = new HardwareLifecycleManager();
        var method = typeof(HardwareLifecycleManager).GetMethod("OnSessionEnding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var shutdownArgs = new Microsoft.Win32.SessionEndingEventArgs(Microsoft.Win32.SessionEndReasons.SystemShutdown);
        var actShutdown = () => method.Invoke(manager, [null, shutdownArgs]);
        actShutdown.Should().NotThrow();

        var logoffArgs = new Microsoft.Win32.SessionEndingEventArgs(Microsoft.Win32.SessionEndReasons.Logoff);
        var actLogoff = () => method.Invoke(manager, [null, logoffArgs]);
        actLogoff.Should().NotThrow();

        var otherArgs = new Microsoft.Win32.SessionEndingEventArgs((Microsoft.Win32.SessionEndReasons)999);
        var actOther = () => method.Invoke(manager, [null, otherArgs]);
        actOther.Should().NotThrow();
    }

    [Fact]
    public void HardwareLifecycleManager_OnSessionSwitch_LogsKnownAndUnknownReasons()
    {
        using var manager = new HardwareLifecycleManager();
        var method = typeof(HardwareLifecycleManager).GetMethod("OnSessionSwitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        foreach (var reason in new[]
        {
            Microsoft.Win32.SessionSwitchReason.SessionLock,
            Microsoft.Win32.SessionSwitchReason.SessionUnlock,
            Microsoft.Win32.SessionSwitchReason.SessionLogon,
            Microsoft.Win32.SessionSwitchReason.SessionLogoff,
            Microsoft.Win32.SessionSwitchReason.RemoteConnect,
            Microsoft.Win32.SessionSwitchReason.RemoteDisconnect,
            (Microsoft.Win32.SessionSwitchReason)999
        })
        {
            var args = new Microsoft.Win32.SessionSwitchEventArgs(reason);
            var act = () => method.Invoke(manager, [null, args]);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void HardwareLifecycleManager_NetworkEvents_TriggerTopologyChangedWithDebounce()
    {
        using var manager = new HardwareLifecycleManager();
        int eventCount = 0;
        manager.NetworkTopologyChanged += () => eventCount++;

        var onAddr = typeof(HardwareLifecycleManager).GetMethod("OnNetworkAddressChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var onAvail = typeof(HardwareLifecycleManager).GetMethod("OnNetworkAvailabilityChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        onAddr!.Invoke(manager, [null, EventArgs.Empty]);
        eventCount.Should().Be(1);

        // Immediate subsequent call is debounced
        var availArgs = (System.Net.NetworkInformation.NetworkAvailabilityEventArgs)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(System.Net.NetworkInformation.NetworkAvailabilityEventArgs));
        onAvail!.Invoke(manager, [null, availArgs]);
        eventCount.Should().Be(1);

        // Reset debounce timestamp to simulate elapsed time
        var field = typeof(HardwareLifecycleManager).GetField("_lastNetworkEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(manager, DateTime.MinValue);

        onAvail.Invoke(manager, [null, availArgs]);
        eventCount.Should().Be(2);
    }

    [Fact]
    public void HardwareLifecycleManager_NotifyDisplayChanged_DebouncesDuplicateMetrics()
    {
        using var manager = new HardwareLifecycleManager();
        int eventCount = 0;
        manager.DisplayOrDriverChanged += () => eventCount++;

        manager.NotifyDisplayChanged();
        eventCount.Should().Be(1);

        // Reset debounce time but leave metric dimensions identical -> returns early without firing
        var field = typeof(HardwareLifecycleManager).GetField("_lastDisplayEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(manager, DateTime.MinValue);

        manager.NotifyDisplayChanged();
        eventCount.Should().Be(1);

        // Modify lastVirtualWidth to simulate changed resolution -> fires event
        var widthField = typeof(HardwareLifecycleManager).GetField("_lastVirtualWidth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        widthField!.SetValue(manager, 1);
        field.SetValue(manager, DateTime.MinValue);

        manager.NotifyDisplayChanged();
        eventCount.Should().Be(2);
    }

    [Fact]
    public void HardwareLifecycleManager_OnDisplaySettingsChanged_DelegatesToNotifyDisplayChanged()
    {
        using var manager = new HardwareLifecycleManager();
        bool fired = false;
        manager.DisplayOrDriverChanged += () => fired = true;

        var method = typeof(HardwareLifecycleManager).GetMethod("OnDisplaySettingsChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(manager, [null, EventArgs.Empty]);

        fired.Should().BeTrue();
    }

    [Fact]
    public void HardwareLifecycleManager_ExceptionsInListeners_AreCaughtGracefully()
    {
        using var manager = new HardwareLifecycleManager();
        manager.DisplayOrDriverChanged += () => throw new InvalidOperationException("simulated listener error");
        manager.NetworkTopologyChanged += () => throw new InvalidOperationException("simulated listener error");
        manager.DevicePnPChanged += () => throw new InvalidOperationException("simulated listener error");
        manager.PowerSuspended += () => throw new InvalidOperationException("simulated listener error");

        var actDisplay = () => manager.NotifyDisplayChanged();
        actDisplay.Should().NotThrow();

        var triggerNet = typeof(HardwareLifecycleManager).GetMethod("TriggerNetworkChange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var actNet = () => triggerNet!.Invoke(manager, null);
        actNet.Should().NotThrow();

        var actPnp = () => manager.NotifyDevicePnPChanged();
        actPnp.Should().NotThrow();

        var onPower = typeof(HardwareLifecycleManager).GetMethod("OnPowerModeChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var actPower = () => onPower!.Invoke(manager, [null, new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Suspend)]);
        actPower.Should().NotThrow();
    }

    [Fact]
    public void HardwareLifecycleManager_SessionEvents_AllReasons_Coverage()
    {
        var onSessionSwitch = typeof(HardwareLifecycleManager).GetMethod("OnSessionSwitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        onSessionSwitch.Should().NotBeNull();

        Microsoft.Win32.SessionSwitchReason[] switchReasons =
        [
            Microsoft.Win32.SessionSwitchReason.SessionLock,
            Microsoft.Win32.SessionSwitchReason.SessionUnlock,
            Microsoft.Win32.SessionSwitchReason.SessionLogon,
            Microsoft.Win32.SessionSwitchReason.SessionLogoff,
            Microsoft.Win32.SessionSwitchReason.RemoteConnect,
            Microsoft.Win32.SessionSwitchReason.RemoteDisconnect,
            (Microsoft.Win32.SessionSwitchReason)999
        ];

        foreach (var reason in switchReasons)
        {
            var e = new Microsoft.Win32.SessionSwitchEventArgs(reason);
            var act = () => onSessionSwitch.Invoke(null, [null, e]);
            act.Should().NotThrow();
        }

        var onSessionEnding = typeof(HardwareLifecycleManager).GetMethod("OnSessionEnding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        onSessionEnding.Should().NotBeNull();

        Microsoft.Win32.SessionEndReasons[] endReasons =
        [
            Microsoft.Win32.SessionEndReasons.SystemShutdown,
            Microsoft.Win32.SessionEndReasons.Logoff,
            (Microsoft.Win32.SessionEndReasons)999
        ];

        foreach (var reason in endReasons)
        {
            var e = new Microsoft.Win32.SessionEndingEventArgs(reason);
            var act = () => onSessionEnding.Invoke(null, [null, e]);
            act.Should().NotThrow();
        }

        // Test exception catch blocks when EventArgs is null
        var actEndingNull = () => onSessionEnding.Invoke(null, [null, null]);
        actEndingNull.Should().NotThrow();

        var actSwitchNull = () => onSessionSwitch.Invoke(null, [null, null]);
        actSwitchNull.Should().NotThrow();
    }
}
