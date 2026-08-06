# System Tray & Taskbar Overlay

LocalTelemetry features a Win32 taskbar integration mechanism that embeds real-time hardware telemetry directly into the Windows taskbar.

## The Taskbar Overlay

The taskbar overlay is a borderless, transparent window docked directly into the Windows Shell taskbar window (`Shell_TrayWnd`).

![LocalTelemetry Taskbar Overlay](/images/overlay.png)
*Figure 1: Real-time telemetry overlay running inside the Windows taskbar.*

### Key Capabilities
- **Native Taskbar Integration**: Attaches to the Windows taskbar and auto-calculates its size from the configured metrics.
- **Automatic Multi-Line Rendering**: Metrics are laid out in a compact grid. With a single metric the widget renders one line; with two or more metrics it renders a 2-row layout automatically.
- **High DPI Awareness**: Scales crisply on 100%, 125%, 150%, 200%+ display scaling settings.

## System Tray Controls

Right-click the **LocalTelemetry icon** in your Windows System Tray (near the clock) to access quick controls:

![System Tray Context Menu](/images/tray-menu.png)
*Figure 2: System tray right-click context menu.*

- **Show Overlay / Hide Overlay**: Instantly toggle overlay visibility (synced with the Overlay page toggle).
- **Open Settings**: Opens the Svelte 5 settings panel.
- **Quit**: Completely closes LocalTelemetry.

## Overlay Settings

In **Settings -> Overlay**:
- **Show Overlay**: Enable/disable the widget on the taskbar.
- **Double-click action**: Choose what happens when you double-click the overlay (`None`, `Task Manager`, `Settings`).
- **Position in taskbar**: Place the widget at the `Left` or `Right` of the taskbar.
- **Offset from Edge**: Shift the widget horizontally from the chosen position.
- **Opacity**: Adjust overlay opacity (0-100%).
- **Scale**: Adjust overlay scale percentage.

## Multi-Monitor Support

The overlay attaches to the primary taskbar (`Shell_TrayWnd`). It does not mirror onto secondary taskbars.

## Windows Explorer Restart Handling

If Windows Explorer crashes or restarts (`explorer.exe`), LocalTelemetry automatically detects the `TaskbarCreated` Win32 message and re-attaches the overlay without requiring an application restart.

If the overlay does not reappear automatically, right-click the tray icon and select **Show Overlay**.
