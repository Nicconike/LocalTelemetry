# System Tray & Taskbar Overlay

LocalTelemetry features a unique Win32 taskbar integration mechanism that embeds real-time hardware telemetry directly into the Windows taskbar.


## The Taskbar Overlay

The taskbar overlay is a borderless, transparent WPF window docked directly into the Windows Shell taskbar window (`Shell_TrayWnd`).

![LocalTelemetry Taskbar Overlay](/images/overlay.png)
*Figure 1: Real-time multi-line telemetry overlay running inside the Windows taskbar.*

### Key Capabilities
- **Native Taskbar Integration**: Moves automatically alongside Windows taskbar elements and auto-calculates available space.
- **Multi-Line Rendering**: Supports 1-row or 2-row metric layouts depending on taskbar height.
- **High DPI Awareness**: Scales crisply on 100%, 125%, 150%, 200%+ display scaling settings.
- **Click-Through Support**: Optional click-through mode so taskbar clicks pass through seamlessly.


## System Tray Controls

Right-click the **LocalTelemetry icon** in your Windows System Tray (near the clock) to access quick controls:

![System Tray Context Menu](/images/tray-menu.png)
*Figure 2: System tray right-click context menu.*

- **Show Taskbar Overlay / Hide Taskbar Overlay**: Instantly toggle overlay visibility.
- **Open Settings**: Opens the Svelte 5 settings panel.
- **Quit**: Completely closes LocalTelemetry.


## Overlay Positioning & Alignment

In **Settings -> Overlay**, you can adjust:
- **Horizontal Offset**: Shift the overlay left or right within the taskbar.
- **Vertical Alignment**: Center or fine-tune vertical alignment.
- **Row Count**: Display 1 compact line or 2 stacked lines of metrics.
- **Metric Ordering**: Customize which metrics appear first (e.g. `CPU Temp -> GPU Temp -> RAM -> Net`).


## Multi-Monitor Support

When using multiple monitors:
- By default, LocalTelemetry docks into the primary monitor's taskbar.
- You can configure multi-monitor mirroring in **Settings -> General** to display the overlay on secondary taskbars if enabled in Windows settings.


## Windows Explorer Restart Handling

If Windows Explorer crashes or restarts (`explorer.exe`), LocalTelemetry automatically detects the `TaskbarCreated` Win32 message and re-attaches the overlay without requiring an application restart.

If the overlay does not reappear automatically, right-click the tray icon and select **Show Taskbar Overlay**.
