# Troubleshooting & FAQ

Answers to frequently asked questions and troubleshooting guides for common issues.


## FAQ

### Q: Why does LocalTelemetry require Administrator privileges?
LocalTelemetry reads hardware sensors directly from CPU registers (MSRs) and GPU interfaces. Operating systems restrict direct hardware register access to administrative users to maintain kernel security.

### Q: Does LocalTelemetry impact gaming performance?
No. LocalTelemetry is optimized for low-overhead background operation. Polling takes place on background worker threads using non-blocking Win32 calls. When minimized to the system tray with settings closed, background CPU and memory usage remain minimal. Opening the Svelte 5 settings panel spins up Microsoft WebView2 child processes (`msedgewebview2.exe`) to render the configuration UI.

### Q: Can I run LocalTelemetry on Windows 11?
Yes! LocalTelemetry is fully compatible with both Windows 10 (1903+) and Windows 11 (all versions including 22H2 or later).


## Troubleshooting Common Issues

### 1. Overlay is missing or stuck after restarting Windows Explorer
- Right-click the **LocalTelemetry System Tray Icon** and click **Restart Overlay**.
- If the overlay is still not visible, verify in **Settings -> Overlay** that metrics are enabled for Row 1 or Row 2.

### 2. GPU Temperature shows `0°C` or `N/A`
- **NVIDIA GPUs**: Ensure standard NVIDIA Display Drivers (v450+) are installed. If running inside a virtual machine without GPU passthrough, NVAPI is disabled.
- **AMD GPUs**: Verify ADL library availability.
- **Intel GPUs**: Verify Intel Graphics driver is up to date.

### 3. Settings window displays a blank white screen
- Ensure **WebView2 Runtime** is installed on your Windows 10 PC (Windows 11 includes WebView2 by default). Download WebView2 Evergreen Standalone Installer from Microsoft.

### 4. Settings corruption / App crashes on launch
If settings become corrupt:
1. Navigate to `%LOCALAPPDATA%\LocalTelemetry` (or application directory in portable mode).
2. Delete or rename `settings.json`.
3. Relaunch `LocalTelemetry.exe` to generate default settings.


## Log File Locations

When reporting bugs on [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues), attach the relevant log files:

- **System Log**: `%LOCALAPPDATA%\LocalTelemetry\lt_system.log`
- **Metrics Log**: `%LOCALAPPDATA%\LocalTelemetry\lt_DD-MM-YYYY.log`
