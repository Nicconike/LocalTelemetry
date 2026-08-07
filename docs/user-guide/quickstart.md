# Quickstart Guide

Get up and running with **LocalTelemetry** in less than 2 minutes.


## 1. Launching LocalTelemetry

After installation, double-click the **LocalTelemetry** desktop shortcut or run `LocalTelemetry.exe`.

- You will see the **LocalTelemetry icon** appear in your Windows System Tray (near the clock).
- The taskbar overlay will dock itself into your taskbar, showing live hardware telemetry metrics.

> [!NOTE]
> On the very first launch after a fresh install, LocalTelemetry asks for Administrator rights once (single UAC prompt) and then installs the PawnIo hardware driver silently in the background. This can take a few seconds - the tray icon and overlay appear once the driver and monitoring services are ready. Later launches start instantly.


## 2. System Tray Controls

Right-click the **LocalTelemetry System Tray icon** to open the context menu:

- **Show Overlay / Hide Overlay**: Instantly show or hide the taskbar overlay.
- **Open Settings**: Opens the Svelte 5 WebView2 settings panel.
- **Quit**: Completely closes LocalTelemetry.


## 3. Opening the Settings Panel

Double-click the **Tray Icon** or select **Settings** from the right-click menu.

The Settings window allows you to:
1. **Choose Active Metrics**: Toggle CPU, GPU, RAM, Disk and Network displays.
2. **Reorder Layout**: Drag and drop lines/items to order metrics to your liking.
3. **Customize Appearance**: Choose one of the built-in themes and per-metric or per-group colors.
4. **Set Threshold Alerts**: Configure alert temperatures (e.g. 90°C CPU warning).
5. **Autostart**: Toggle "Start with Windows".


## 4. Next Steps

Check out the detailed guides:
- [System Tray & Taskbar Overlay Guide](./tray-and-overlay.md)
- [Telemetry Metrics & Sensors](./metrics-and-sensors.md)
- [Threshold Alerts & Toast Notifications](./alerts-and-notifications.md)
