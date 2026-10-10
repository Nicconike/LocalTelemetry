using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Win32;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Defines the contract for monitoring hardware lifecycle, driver updates,
/// power transitions, and device connectivity changes.
/// </summary>
public interface IHardwareLifecycleManager : IDisposable
{
    /// <summary>Fired when the display adapter configuration changes or graphics drivers restart.</summary>
    event Action? DisplayOrDriverChanged;

    /// <summary>Fired when the system is entering a suspended/sleep power state.</summary>
    event Action? PowerSuspended;

    /// <summary>Fired when the system resumes from a suspended/sleep power state.</summary>
    event Action? PowerResumed;

    /// <summary>Fired when network adapter availability or IP topology changes.</summary>
    event Action? NetworkTopologyChanged;

    /// <summary>Fired when hardware devices (USB, storage, PnP) arrive or are removed.</summary>
    event Action? DevicePnPChanged;

    /// <summary>Notifies the manager of an external display configuration change (e.g. from WM_DISPLAYCHANGE).</summary>
    void NotifyDisplayChanged();

    /// <summary>Notifies the manager of an external device change (e.g. from WM_DEVICECHANGE).</summary>
    void NotifyDevicePnPChanged();
}

/// <summary>
/// Monitors Windows system events, display changes, power transitions, and network availability
/// to notify hardware monitors of driver resets, sleeps/resumes, and device topology changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareLifecycleManager : IHardwareLifecycleManager
{
    private static readonly Lazy<HardwareLifecycleManager> _instance = new(() => new HardwareLifecycleManager());

    /// <summary>Gets the shared default instance of <see cref="HardwareLifecycleManager"/>.</summary>
    public static HardwareLifecycleManager Instance => _instance.Value;

    public event Action? DisplayOrDriverChanged;
    public event Action? PowerSuspended;
    public event Action? PowerResumed;
    public event Action? NetworkTopologyChanged;
    public event Action? DevicePnPChanged;

    private readonly object _lock = new();
    private bool _disposed;
    private bool _isSuspended;
    private DateTime _lastDisplayEvent = DateTime.MinValue;
    private DateTime _lastNetworkEvent = DateTime.MinValue;
    private DateTime _lastDeviceEvent = DateTime.MinValue;
    private int _lastVirtualWidth;
    private int _lastVirtualHeight;
    private int _lastMonitorCount;
    private const int DebounceMs = 3000;
    private const int NetworkDebounceMs = 2000;

    /// <summary>
    /// Initializes a new instance of <see cref="HardwareLifecycleManager"/> and subscribes to OS events.
    /// </summary>
    public HardwareLifecycleManager()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe to SystemEvents");
        }

        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe to NetworkChange");
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        NotifyDisplayChanged();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _isSuspended = true;
                Log.Info("System entering suspend/sleep; hardware telemetry paused");
                PowerSuspended?.Invoke();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _isSuspended = false;
                Log.Info("System resumed from sleep; re-initializing hardware monitors");
                PowerResumed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle power mode change");
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        try
        {
            string reason = e.Reason switch
            {
                SessionEndReasons.SystemShutdown => "Windows is shutting down or restarting",
                SessionEndReasons.Logoff => "User is logging off",
                _ => e.Reason.ToString()
            };
            Log.Info($"Windows session ending: {reason}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareLifecycleManager] Error logging session ending: {ex}");
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        try
        {
            string reason = e.Reason switch
            {
                SessionSwitchReason.SessionLock => "Session locked",
                SessionSwitchReason.SessionUnlock => "Session unlocked",
                SessionSwitchReason.SessionLogon => "User logged on",
                SessionSwitchReason.SessionLogoff => "User logged off",
                SessionSwitchReason.RemoteConnect => "Remote desktop connected",
                SessionSwitchReason.RemoteDisconnect => "Remote desktop disconnected",
                _ => e.Reason.ToString()
            };
            Log.Info($"Windows session event ({reason})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareLifecycleManager] Error logging session switch: {ex}");
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        TriggerNetworkChange();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        TriggerNetworkChange();
    }

    private void TriggerNetworkChange()
    {
        if (_isSuspended) return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNetworkEvent).TotalMilliseconds < NetworkDebounceMs) return;
            _lastNetworkEvent = now;
        }

        try
        {
            Log.Debug("Network topology or availability changed; notifying network monitor");
            NetworkTopologyChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle network change event");
        }
    }

    /// <inheritdoc />
    public void NotifyDisplayChanged()
    {
        if (_isSuspended) return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDisplayEvent).TotalMilliseconds < DebounceMs) return;
            _lastDisplayEvent = now;

            // Filter out transient events (e.g. dynamic refresh rate or Optimus mux toggles)
            // that do not alter the virtual desktop bounds or monitor count.
            int curW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int curH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            int curMon = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);

            if (_lastVirtualWidth > 0 && curW == _lastVirtualWidth && curH == _lastVirtualHeight && curMon == _lastMonitorCount)
            {
                return;
            }

            _lastVirtualWidth = curW;
            _lastVirtualHeight = curH;
            _lastMonitorCount = curMon;
        }

        try
        {
            Log.Debug("Display resolution or monitor count changed; notifying display listeners");
            DisplayOrDriverChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle display change event");
        }
    }

    /// <inheritdoc />
    public void NotifyDevicePnPChanged()
    {
        if (_isSuspended) return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDeviceEvent).TotalMilliseconds < DebounceMs) return;
            _lastDeviceEvent = now;
        }

        try
        {
            Log.Debug("Hardware device PnP arrival or removal detected; notifying hardware monitors");
            DevicePnPChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle device PnP change event");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to unhook SystemEvents handlers during disposal");
        }

        try
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to unhook NetworkChange handlers during disposal");
        }
    }
}
