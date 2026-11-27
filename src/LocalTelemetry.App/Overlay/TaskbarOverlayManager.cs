using System.Runtime.Versioning;
using Microsoft.Win32;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.App.Overlay;

/// <summary>
/// Coordinates multiple <see cref="TaskbarOverlay"/> instances across one or more display monitors.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarOverlayManager : IDisposable
{
    private const string PrimaryDisplayId = "primary";
    private readonly AppSettings _cfg;
    private readonly Dictionary<string, TaskbarOverlay> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncLock = new();
    private bool _disposed;
    private bool _isVisible;

    // Test seams
    internal Func<AppSettings, DisplayInfo, TaskbarOverlay>? OverlayFactory { get; set; }
    internal static bool SkipDisplaySettingsChangedHookForTesting { get; set; }

    public TaskbarOverlay PrimaryOverlay
    {
        get
        {
            lock (_syncLock)
            {
                if (!_overlays.TryGetValue(PrimaryDisplayId, out var overlay))
                {
                    var primaryDisplay = DisplayInfo.EnumerateDisplays().FirstOrDefault(d => d.IsPrimary)
                        ?? new DisplayInfo { Id = PrimaryDisplayId, IsPrimary = true };
                    overlay = OverlayFactory is not null ? OverlayFactory(_cfg, primaryDisplay) : new TaskbarOverlay(_cfg, primaryDisplay);
                    WireOverlay(overlay, true);
                    _overlays[PrimaryDisplayId] = overlay;
                }
                return overlay;
            }
        }
    }

    /// <summary>Gets all currently active overlay instances across all displays.</summary>
    public IReadOnlyList<TaskbarOverlay> GetActiveOverlays()
    {
        lock (_syncLock)
        {
            return [.. _overlays.Values];
        }
    }

    public bool IsTaskbarSpaceConstrained => PrimaryOverlay?.IsTaskbarSpaceConstrained ?? false;

    public int AvailableSlotWidth => PrimaryOverlay?.AvailableSlotWidth ?? 0;

    public event Action<bool, int>? TaskbarSpaceConstraintChanged;

    public Action<Point>? ShowContextMenuAction { get; set; }
    public Func<bool>? IsContextMenuOpen { get; set; }
    internal Func<NativeMethods.Point, bool>? IsPointInsideContextMenu { get; set; }
    public Func<Rectangle>? GetContextMenuBounds { get; set; }
    public Action? CloseContextMenuAction { get; set; }
    public Action? OnOpenSettingsRequested { get; set; }
    public Action? OnToggleOverlayRequested { get; set; }
    public Action? OnExitRequested { get; set; }
    public event Action<string>? OnDoubleClick;

    public TaskbarOverlayManager(AppSettings cfg)
    {
        _cfg = cfg;
        if (!SkipDisplaySettingsChangedHookForTesting)
        {
            try
            {
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to register SystemEvents.DisplaySettingsChanged handler");
            }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        Log.Info("Display settings change detected; synchronizing overlay monitors");
        try
        {
            SyncOverlays("DisplaySettingsChanged notification");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error synchronizing overlays following display change");
        }
    }

    /// <inheritdoc/>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool wasVisible = _isVisible;
        _isVisible = true;
        SyncOverlays(wasVisible ? "Display settings updated" : "Show request");
    }

    /// <inheritdoc/>
    public void Hide()
    {
        _isVisible = false;
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                try
                {
                    overlay.Hide();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error hiding overlay during manager Hide");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void RefreshOverlays()
    {
        if (_disposed) return;
        SyncOverlays("Refresh request");
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                try
                {
                    overlay.RefreshOverlay();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error refreshing overlay during manager RefreshOverlays");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void UpdateSnapshot(TelemetrySnapshot snap)
    {
        if (_disposed || !_isVisible || !_cfg.Overlay.Visible) return;
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                try
                {
                    overlay.UpdateSnapshot(snap);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error updating snapshot on overlay");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void RecoverTaskbars(string reason)
    {
        if (_disposed || !_cfg.Overlay.Visible) return;
        SyncOverlays($"Recovery: {reason}");
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                try
                {
                    overlay.RecoverTaskbar(reason);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error recovering overlay during manager RecoverTaskbars");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void StopMouseHook()
    {
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                try
                {
                    overlay.StopMouseHook();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error stopping mouse hook on overlay");
                }
            }
        }
    }

    /// <summary>
    /// Synchronizes active overlay instances with currently connected displays and settings.
    /// </summary>
    /// <param name="reason">Trigger reason for the synchronization.</param>
    public void SyncOverlays(string reason)
    {
        if (_disposed) return;

        var displays = DisplayInfo.EnumerateDisplays();
        var desiredDisplayIds = DetermineDesiredDisplayIds(displays);

        lock (_syncLock)
        {
            RemoveObsoleteOverlays(desiredDisplayIds, reason);

            if (_isVisible && _cfg.Overlay.Visible)
            {
                EnsureDesiredOverlays(displays, desiredDisplayIds);
            }
        }
    }

    private void RemoveObsoleteOverlays(HashSet<string> desiredDisplayIds, string reason)
    {
        var idsToRemove = _overlays.Keys.Where(k => !desiredDisplayIds.Contains(k)).ToList();
        foreach (var id in idsToRemove)
        {
            if (_overlays.Remove(id, out var removedOverlay))
            {
                string displayLabel = removedOverlay.TargetDisplay?.FriendlyName ?? id;
                Log.Info($"Removing overlay for display '{id}' ({displayLabel}) - reason: {reason}");
                UnwireOverlay(removedOverlay);
                removedOverlay.Hide();
                removedOverlay.Dispose();
            }
        }
    }

    private void EnsureDesiredOverlays(List<DisplayInfo> displays, HashSet<string> desiredDisplayIds)
    {
        foreach (var display in displays.Where(d => desiredDisplayIds.Contains(d.Id)))
        {
            if (_overlays.TryGetValue(display.Id, out var existingOverlay))
            {
                UpdateExistingOverlay(existingOverlay, display);
            }
            else if (display.HasTaskbar)
            {
                AttachNewOverlay(display);
            }
            else
            {
                Log.Debug($"Display '{display.Id}' ({display.FriendlyName}) has no taskbar window; skipping overlay creation");
            }
        }
    }

    private void AttachNewOverlay(DisplayInfo display)
    {
        Log.Info($"Creating overlay for display '{display.Id}' ({display.FriendlyName})");
        var overlay = OverlayFactory is not null
            ? OverlayFactory(_cfg, display)
            : new TaskbarOverlay(_cfg, display);

        WireOverlay(overlay, display.IsPrimary);
        _overlays[display.Id] = overlay;
        overlay.Show();
    }

    private static void UpdateExistingOverlay(TaskbarOverlay overlay, DisplayInfo display)
    {
        if (overlay.TargetDisplay is not null)
        {
            overlay.TargetDisplay.TaskbarHwnd = display.TaskbarHwnd;
        }

        if (!overlay.Visible)
        {
            overlay.Show();
        }
    }

    private HashSet<string> DetermineDesiredDisplayIds(List<DisplayInfo> displays)
    {
        string mode = _cfg.Overlay.DisplayMode?.ToLowerInvariant() ?? PrimaryDisplayId;
        return mode switch
        {
            "all" => new HashSet<string>(displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase),
            "secondary" => ResolveSecondaryDisplayIds(displays),
            "custom" => new HashSet<string>(_cfg.Overlay.EnabledDisplays ?? [PrimaryDisplayId], StringComparer.OrdinalIgnoreCase),
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PrimaryDisplayId }
        };
    }

    private static HashSet<string> ResolveSecondaryDisplayIds(List<DisplayInfo> displays)
    {
        var secondaries = displays.Where(d => !d.IsPrimary).Select(d => d.Id).ToList();
        if (secondaries.Count > 0)
        {
            return new HashSet<string>(secondaries, StringComparer.OrdinalIgnoreCase);
        }

        Log.Info("No secondary displays detected; falling back to primary display overlay");
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PrimaryDisplayId };
    }

    private void WireOverlay(TaskbarOverlay overlay, bool isPrimary)
    {
        overlay.ShowContextMenuAction = pt => ShowContextMenuAction?.Invoke(pt);
        overlay.IsContextMenuOpen = () => IsContextMenuOpen?.Invoke() ?? false;
        overlay.IsPointInsideContextMenu = pt => IsPointInsideContextMenu?.Invoke(pt) ?? false;
        overlay.GetContextMenuBounds = () => GetContextMenuBounds?.Invoke() ?? Rectangle.Empty;
        overlay.CloseContextMenuAction = () => CloseContextMenuAction?.Invoke();
        overlay.OnOpenSettingsRequested = () => OnOpenSettingsRequested?.Invoke();
        overlay.OnToggleOverlayRequested = () => OnToggleOverlayRequested?.Invoke();
        overlay.OnExitRequested = () => OnExitRequested?.Invoke();
        overlay.OnDoubleClick += action => OnDoubleClick?.Invoke(action);

        if (isPrimary)
        {
            overlay.TaskbarSpaceConstraintChanged += OnPrimaryConstraintChanged;
        }
    }

    private void UnwireOverlay(TaskbarOverlay overlay)
    {
        overlay.TaskbarSpaceConstraintChanged -= OnPrimaryConstraintChanged;
    }

    private void OnPrimaryConstraintChanged(bool constrained, int slotWidth)
    {
        TaskbarSpaceConstraintChanged?.Invoke(constrained, slotWidth);
    }

    /// <summary>Pauses background timers on all managed overlay windows.</summary>
    public void PauseAllTimers()
    {
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.PauseTimers();
            }
        }
    }

    /// <summary>Resumes background timers on all managed overlay windows.</summary>
    public void ResumeAllTimers()
    {
        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.ResumeTimers();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!SkipDisplaySettingsChangedHookForTesting)
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error unhooking DisplaySettingsChanged");
            }
        }

        lock (_syncLock)
        {
            foreach (var overlay in _overlays.Values)
            {
                UnwireOverlay(overlay);
                overlay.Dispose();
            }
            _overlays.Clear();
        }
    }
}
