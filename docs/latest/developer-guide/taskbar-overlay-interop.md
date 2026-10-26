# :app-window: Win32 Taskbar Hooking & Overlay Interop

LocalTelemetry embeds a high-performance GDI+ render surface (`NativeWindow`) directly into the native Windows taskbar process (`Shell_TrayWnd`).

## :layers: Win32 Taskbar Hierarchy & State Machine

The taskbar overlay management is handled inside `TaskbarOverlay.cs` and `TaskbarEmbedder.cs`.

```mermaid
%%{init: {'theme': 'dark', 'themeVariables': {'fontSize': '13px'}, 'flowchart': {'nodeSpacing': 35, 'rankSpacing': 40, 'subGraphTitleMargin': {'top': 12, 'bottom': 20}, 'curve': 'linear'}}}%%
flowchart TD
    %% 1. DESKTOP & TASKBAR ROOT
    Desktop["<b>Windows Desktop</b><br/>HWND 0"] --> Tray["<b>Shell_TrayWnd</b><br/>Native Windows Taskbar HWND"]

    %% 2. OS SHELL ARCHITECTURES (SIDE BY SIDE)
    subgraph Win10Arch["Windows 10 Classic Shell"]
        direction TB
        ReBar["<b>ReBarWindow32</b><br/>Taskbar Control Container"]
        TaskSw["<b>MSTaskSwWClass</b><br/>Running Apps TaskBand"]
        ReBar --> TaskSw
    end

    subgraph Win11Arch["Windows 11 Modern Shell"]
        direction TB
        Win11Bridge["<b>DesktopWindowContentBridge</b><br/>XAML Islands Taskbar Canvas"]
    end

    Tray --> ReBar
    Tray --> Win11Bridge

    %% 3. EMBEDDING STRATEGY
    subgraph EmbeddingStrategy["TaskbarEmbedder Strategy Resolution"]
        direction TB
        ShrinkBand["<b>ShrinkTaskBand Interop</b><br/>Reserves Pixel Space on Win10"]
        ChildMode["<b>Tier 1: Child Mode (SetParent HWND)</b><br/>WS_CHILD • WS_EX_TOOLWINDOW • WS_EX_LAYERED"]
        FallbackMode["<b>Tier 2: Layered Fallback Window</b><br/>WS_EX_TOPMOST • WS_EX_NOACTIVATE • WS_EX_LAYERED"]

        ShrinkBand --> ChildMode
        ChildMode -.->|"SetParent Fails"| FallbackMode
    end

    TaskSw --> ShrinkBand
    Win11Bridge --> ChildMode

    %% 4. TASKBAR OVERLAY SURFACE & RUNTIME
    subgraph OverlayComponent["LocalTelemetry Taskbar Overlay Surface"]
        direction TB
        GdiSurface["<b>GDI+ Render Surface (NativeWindow)</b><br/>Double-Buffered Memory DC Paint"]
        Watchdog["<b>Heartbeat Watchdog (5s & 200ms)</b><br/>Tracks Taskbar HWND Validity"]
        MouseHook["<b>Low-Level Mouse Hook</b><br/>WH_MOUSE_LL Click & DblClick"]

        GdiSurface --- Watchdog
        GdiSurface --- MouseHook
    end

    ChildMode --> GdiSurface
    FallbackMode --> GdiSurface

    %% 5. DEPLOYMENT STATE MACHINE
    subgraph DeploymentStateMachine["OverlayDeploymentState Lifecycle"]
        direction LR
        StateInit["<b>Uninitialized</b>"]
        StateActive["<b>Active</b>"]
        StateRecov["<b>Recovering</b>"]
        StateFailed["<b>Failed</b>"]

        StateInit -->|"Embed OK"| StateActive
        StateInit -->|"Embed Fail"| StateFailed
        StateActive -->|"Explorer Restart"| StateRecov
        StateRecov -->|"Re-embed OK"| StateActive
        StateRecov -->|"Re-embed Fail"| StateFailed
    end

    Watchdog ==>|"Drives Recovery"| StateRecov

    class Desktop,Tray,ReBar,TaskSw,Win11Bridge shellNode
    class ShrinkBand,Watchdog,MouseHook toolNode
    class ChildMode,StateActive childNode
    class FallbackMode,StateFailed fallbackNode
    class GdiSurface,StateRecov surfaceNode
    class StateInit initNode

    classDef shellNode fill:#0b1329,stroke:#38bdf8,stroke-width:1.5px,color:#e0f2fe
    classDef toolNode fill:#18181b,stroke:#a1a1aa,stroke-width:1.5px,color:#f4f4f5
    classDef childNode fill:#064e3b,stroke:#34d399,stroke-width:2px,color:#ffffff
    classDef fallbackNode fill:#7c2d12,stroke:#fb923c,stroke-width:2px,color:#ffffff
    classDef surfaceNode fill:#1e3a8a,stroke:#60a5fa,stroke-width:2px,color:#ffffff
    classDef initNode fill:#374151,stroke:#9ca3af,stroke-width:1.5px,color:#f3f4f6
```

## :settings: Taskbar Embedding Strategies

Implemented in [`TaskbarEmbedder.cs`](https://github.com/Nicconike/LocalTelemetry/blob/master/src/LocalTelemetry.App/Win32/TaskbarEmbedder.cs), LocalTelemetry provides a 2-tier embedding engine to guarantee compatibility across all Windows 10 and 11 builds:

### :shield-alert: Child-Window Reparenting (`IsChildMode`)

- **Find Taskbar HWND**: `FindWindow("Shell_TrayWnd", null)` locates the primary taskbar window handle.
- **Find ReBar / TaskBand**: `FindWindowEx(trayHwnd, IntPtr.Zero, "ReBarWindow32", null)` retrieves the control container and taskband (`MSTaskSwWClass`).
- **Reparent Window (`SetParent`)**: Calls Win32 `SetParent(overlayHwnd, parentHwnd)` to reparent the overlay window as a native child control of the taskbar.
- **Shrink TaskBand (`ShrinkTaskBand`)**: Dynamically reduces taskband width to reserve dedicated space for the overlay metrics on the taskbar.
- **Apply Window Styles (`SetWindowLong`)**: Sets `WS_CHILD`, `WS_EX_TOOLWINDOW` (hides from Alt+Tab), `WS_EX_NOACTIVATE`, and `WS_EX_LAYERED`.

### :panel-bottom: Layered Floating Window Fallback (`IsFallback`)

- Activated if reparenting fails due to third-party shell software or security policies.
- Restores top-level window styles (`WS_POPUP`, `WS_EX_TOPMOST`) and positions a transparent layered floating window precisely over the taskbar bounding rectangle.

---

## :compass: Taskbar Alignment & Coordinate Mathematics

LocalTelemetry dynamically adapts overlay placement based on Windows 11 taskbar alignment:

### 1. Taskbar Alignment Detection (`IsTaskbarCentered`)

Inspects user registry configuration:

```csharp
using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
int val = Convert.ToInt32(key?.GetValue("TaskbarAl") ?? 1);
return val == 1; // 1 = Centered, 0 = Left-aligned
```

### 2. Centered Taskbar Positioning

On centered taskbars, the app icons occupy the center band:

- **Left Placement (`left`)**: Anchors relative to the Windows Widgets button edge (`_leftWidgetRightEdge + PlacementOffset`).
- **Right Placement (`right`)**: Anchors directly before the system notification area (`_trayLeftEdge - Width - PlacementOffset`).

### 3. Left-Aligned Taskbar Positioning

On left-aligned taskbars, app icons start from the Start button and expand toward the right:

- **Application Band Boundary Detection (`GetAppListRightEdge` & `GetAppListLeftEdge`)**:
  - Inspects UIAutomation elements for `Taskbar.TaskListButtonPanel` or native `MSTaskListWClass`.
  - Determines the exact left and right boundaries of running and pinned applications.
- **Taskbar Space Congestion Detection (`IsTaskbarSpaceConstrained`)**:
  - Calculates the available horizontal gap (`AvailableSlotWidth`) between running app icons and the system tray or widgets.
  - Sets `IsTaskbarSpaceConstrained` to `true` when the measured slot is narrower than the overlay width (`overlayWidth > availableWidth`).
  - Supplies status to the frontend settings UI via `TaskbarSlotStatusProvider`.
- **Position Next to Apps (`beside_apps` or `left`)**:
  - Positions the overlay immediately following the app list:

    $$\text{X} = \text{clientAppRight} + \text{PlacementOffset}$$

- **Position Before System Tray (`right`)**:
  - Positions the overlay immediately before the system notification area:

    $$\text{X} = \text{clientRightBoundary} - \text{Width} - \text{PlacementOffset}$$

- **Boundary Clamping**:
  - Clamps computed coordinates via `Math.Clamp(x, minX, maxX)` to keep the overlay within bounds and prevent visual overlap.

---

## :cloud-backup: Deployment Lifecycle State Machine

`TaskbarEmbedder.cs` tracks overlay attachment health using `OverlayDeploymentState`:

```csharp
public OverlayDeploymentState State { get; private set; } = OverlayDeploymentState.Uninitialized;
```

1. **`Uninitialized`**: Overlay is hidden or has not yet completed initial taskbar discovery.
2. **`Active`**: Taskbar handle verified and overlay successfully positioned (either as child or layered fallback).
3. **`Failed`**: Taskbar handle not found or `SetParent` call failed.
4. **`Recovering`**: Watchdog detected that `explorer.exe` restarted; attempting re-embedding cycle.

---

## :palette: GDI+ Double-Buffered Rendering Engine

Implemented in [`TaskbarOverlay.cs`](https://github.com/Nicconike/LocalTelemetry/blob/master/src/LocalTelemetry.App/Overlay/TaskbarOverlay.cs), `TaskbarOverlay` inherits from `System.Windows.Forms.NativeWindow`:

- Renders hardware metrics into an in-memory double-buffered bitmap using GDI+ (`System.Drawing.Graphics`).
- Caches font families and solid brushes to eliminate memory allocations on high-frequency polling ticks.
- Updates the layered window surface using `UpdateLayeredWindow` / `SetLayeredWindowAttributes`.
- When an alert triggers, applies a visual warning tint across the buffer (`rgba(255, 80, 0, 0.24)`).

---

## :monitor: High-DPI Awareness & Scaling

Queries `GetDpiForWindow` to dynamically scale font rendering, text margins, and line heights for 4K displays and custom DPI scaling settings (100%, 125%, 150%, 175%, 200%+).

---

## :refresh-cw: Windows Explorer Restart Recovery

When `explorer.exe` crashes or restarts, all child window handles of `Shell_TrayWnd` are destroyed by the operating system:

1. **5-Second Heartbeat Watchdog (`_watchTimer`)**:
   A background timer polls `TaskbarEmbedder.FindExplorerTrayWnd()` every 5 seconds. If the taskbar window handle changes or is recreated, it immediately triggers `OnTaskbarRecreated()` on the UI thread:

```csharp
private void WatchdogTick()
{
    IntPtr currentTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
    if (currentTray != _lastKnownTray && currentTray != IntPtr.Zero)
    {
        Log.Info("Explorer restart detected; re-embedding taskbar overlay");
        _dispatcher.Invoke(ReembedOverlay);
    }
}
```

1. **State Transition to `Recovering`**:
   During re-embedding, `State` transitions to `OverlayDeploymentState.Recovering`. Once positioning succeeds, it returns to `Active`.
