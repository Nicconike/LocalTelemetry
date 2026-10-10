# :wrench: Troubleshooting & FAQ

Answers to frequently asked questions, configuration solutions, and diagnostic steps for **LocalTelemetry**.

## :circle-question-mark: Frequently Asked Questions

### :shield: Why does LocalTelemetry require Administrator privileges?

LocalTelemetry reads hardware telemetry directly from processor registers (Intel MSRs, AMD Ryzen SMU) and storage controllers. Windows restricts access to kernel-level hardware registers to administrative accounts to protect system security and kernel integrity.

### :shield-alert: Can LocalTelemetry run without Administrator privileges?

No. LocalTelemetry requires administrative privileges to load and communicate with the `PawnIO` kernel driver. Kernel driver access is necessary to read CPU package temperatures, MSR clock frequencies, and RAPL package power. If the Windows UAC elevation prompt is declined on launch, LocalTelemetry will display an elevation notice and exit.

### :app-window: Why doesn't the taskbar overlay appear immediately after installation?

On a fresh installation, LocalTelemetry launches with the Settings window open and places an icon in your Windows System Tray, but the taskbar overlay is hidden by default:

1. In the Settings window, go to **Overlay** and toggle **Show Overlay** on.
2. Or right-click the **LocalTelemetry System Tray icon** and select **Show Overlay**.

### :gamepad-2: Does LocalTelemetry impact gaming or system performance?

No. LocalTelemetry is optimized for minimal background resource consumption:

- Hardware polling occurs on asynchronous worker threads using non-blocking Win32 and vendor APIs.
- When minimized to the taskbar and system tray with settings closed, memory usage remains around ~40 MB RAM.
- The Svelte 5 settings panel (Microsoft WebView2) is completely disposed when closed, consuming zero GPU or rendering resources while hidden.

### :monitor: Is LocalTelemetry compatible with Windows 11?

Yes. LocalTelemetry is fully compatible with both **Windows 10** (v2004+ / build 19041+) and **Windows 11** (including 22H2, 23H2, and later).

### :laptop-minimal-check: Why does CPU clock speed or GPU usage differ from Windows Task Manager?

- **CPU Frequency**: By default, LocalTelemetry calculates the effective clock rate across all active cores using hardware APERF/MPERF MSR deltas. In **Settings &rarr; Monitoring**, you can switch the **Clock Mode** between `Average` (mean clock across all cores) and `Max Active Core` (fastest active boosted core).
- **CPU Utilization**: By default, LocalTelemetry measures overall processor utilization across all threads via `GetSystemTimes`, matching Windows Task Manager. In **Settings &rarr; Monitoring**, you can switch the **Usage Source** to `Max Thread (Gaming)` to monitor the single highest loaded core/thread, which is invaluable for spotting single-core bottlenecks in games.
- **GPU Utilization**: By default, LocalTelemetry queries vendor drivers directly (`NVML` / `ADL`), which reports actual GPU kernel busy time (matching MSI Afterburner). Windows Task Manager instead reads WDDM 3D/compute engine percentage. In **Settings &rarr; Monitoring**, you can switch the **GPU Usage Source** between `Driver (NVML/ADL)` and `WDDM (Task Manager)` to match your preference.

### :laptop-minimal-check: Why does telemetry take ~3 seconds to update after waking from sleep?

LocalTelemetry includes an automatic hardware stabilization delay. When your computer resumes from sleep or hibernation, `HardwareLifecycleManager` waits 3 seconds before querying hardware sensors. This intentional delay allows PCIe buses, graphics drivers, and network adapters to reach full operational power states, preventing crashes and driver reset errors.

---

## :waypoints: Common Troubleshooting Steps

### :cpu: CPU Temperature, Frequency, or Power displays `--`

- **Check Elevation**: Ensure LocalTelemetry was launched with Administrator privileges. Kernel sensor drivers cannot be accessed in unprivileged mode.
- **Check Anti-Cheat / Security Isolation**: Certain aggressive anti-cheat engines or third-party hypervisors may restrict MSR register access.
- **AMD Ryzen / Intel Processors**: Ensure you are running a supported Intel Core/Ultra or AMD Ryzen CPU (x64 architecture).

### :gpu: GPU Temperature or Usage displays `--` or `0%`

- **NVIDIA GeForce**: Ensure the official NVIDIA Game Ready or Studio Driver is installed (`nvml.dll` in `C:\Windows\System32`). Virtual machines without direct GPU passthrough do not support NVML.
- **AMD Radeon**: Ensure the official AMD Adrenalin software and driver (`atiadlxx.dll`) are installed.
- **Intel Arc / Discrete Graphics**: Ensure the latest Intel Graphics drivers (`ControlLib.dll`) are installed.
- **Laptops with Hybrid Graphics (Optimus / SmartAccess)**: When idle on battery or desktop, discrete GPUs enter a deep sleep power state (`0 W` / `0%`) until a 3D application or game is launched.

### :network: Network speed or traffic history is not recording activity

- **Check Active Trackers**: Open **Settings &rarr; Monitoring** and ensure the **Network** toggle under **Active Trackers** is switched on. If toggled off, the background network polling loop is halted entirely.
- **Select Active Network Interface**: Open **Settings &rarr; Network** and ensure the preferred adapter is set to `Auto (Best Match)` or your active connection (Ethernet or Wi-Fi) rather than an inactive virtual adapter (such as VPN, WSL, or VirtualBox virtual network cards).
- **Verify Traffic Logging Toggle**: In **Settings &rarr; Network**, confirm that **Enable Daily Traffic Logging** is checked if you want bandwidth to be recorded into `internet_usage.jsonl` and visible on the Traffic calendar.

### :bell: Toast notifications are not appearing for threshold alerts

LocalTelemetry uses an out-of-process notifier (`LocalTelemetry.Notifier.exe`) that creates custom, non-activating TopMost windows via Named Pipes (`LocalTelemetryNotifier`) rather than Windows Action Center UWP toasts. If toasts do not appear:

- **Check Notifier Process**: Open Windows Task Manager and confirm that `LocalTelemetry.Notifier.exe` is running alongside `LocalTelemetry.exe`.
- **Check Alert Settings**: In **Settings &rarr; Alerts**, verify that **Show Tray Notification** is checked.
- **10-Second Startup Grace Period**: LocalTelemetry suppresses all alerts for the first 10 seconds following application startup to avoid false alarms while hardware sensors calibrate.
- **Hysteresis & Alert Cooldown**: Once an alert triggers, it respects an alert cooldown timer (default: 60 seconds) and must drop below a hysteresis threshold buffer (e.g. 6°C, 8%, 512 MB) before re-arming. If **Only fire once per session (ignores cooldown)** is enabled, each metric will alert only once until the application is restarted.
- **Inspect Notifier Log**: Open `%LOCALAPPDATA%\LocalTelemetry\lt_notifier.log` (or `lt_notifier.log` in portable mode) to check for named pipe connection failures or toast creation errors.

### :battery-warning: Battery metrics are missing from Layout or display `--`

- **Desktop Systems**: On desktop PCs without a physical battery, LocalTelemetry automatically detects the chassis type and AC power status, dynamically hiding battery metrics (`BAT %`, `Charge Rate`) from **Settings &rarr; Layout** and the overlay.
- **Laptops**: If battery metrics display `--` on a laptop, verify that **Battery** is enabled under **Settings &rarr; Monitoring &rarr; Active Trackers**. Note that low battery alerts only evaluate when running on battery power (discharging).

### :move: Overlay placement, clipping, or auto-hide issues

- **Taskbar Embedding vs. Floating Fallback**: LocalTelemetry first attempts to embed directly into the Windows taskband as a child window (Approach A). If taskband embedding is blocked or fails, it automatically switches to a floating layered window positioned over the taskbar (Approach B).
- **Adjust Edge Offset**: If taskbar icons overlap the overlay, go to **Settings &rarr; Overlay** and adjust **Offset from Edge** (`0px` to `200px`) or switch **Position in taskbar** between `Left side` and `Right side`.
- **Explorer Restart Watchdog**: If the Windows taskbar crashes or restarts, an integrated background watchdog checks the taskbar handle every 5 seconds and automatically re-embeds the overlay. If it does not reappear immediately, right-click the system tray icon and choose **Show Overlay**.

### :file-exclamation-point: "Start with Windows" does not launch after moving folders (Portable Mode)

- LocalTelemetry registers a Windows Task Scheduler task named `LocalTelemetry Startup` configured with highest privileges to launch elevated at logon without UAC prompts.
- If you move or rename the folder containing `LocalTelemetry.exe`, Task Scheduler will attempt to launch the old, non-existent file path.
- **Fix**: Open **Settings &rarr; General**, toggle **Start with Windows** off and then back on to register the updated file location.

### :shield-alert: Driver installation fails or requests restart (Exit Code 3010)

- If you reinstall LocalTelemetry immediately after uninstalling or running `Uninstall.bat`, Windows may report exit code `3010`.
- This occurs because the Windows kernel holds unreleased driver memory handles until a system restart. If this happens, restart your computer and run the installer again.

### :monitor: Settings window opens to a blank screen

- **Missing WebView2**: Windows 11 includes the **Microsoft WebView2 Runtime** by default. On Windows 10, if WebView2 is missing, download and install the free **Microsoft Edge WebView2 Evergreen Runtime** from Microsoft.
- **Corrupted User Data Cache**: If WebView2 fails to render despite being installed, clear its cache directory:
  - **Installer Mode**: Delete `%LOCALAPPDATA%\LocalTelemetry\webview2_data\`.
  - **Portable Mode**: Delete `webview2_data\` inside your application directory.
  *(This clears browser cache without wiping your application configuration in `settings.json`.)*

### :cog: Resetting corrupted settings to default

If your settings file becomes damaged:

1. Exit LocalTelemetry (right-click the tray icon and choose **Quit**).
2. Open Windows File Explorer and navigate to `%LOCALAPPDATA%\LocalTelemetry` (or the application folder if using portable mode).
3. Delete `settings.json`.
4. Relaunch `LocalTelemetry.exe`. The application will regenerate a fresh default configuration.

---

## :logs: Log File Locations

When reporting an issue on [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues), attach the relevant log files. You can quickly open your logs directory by clicking the arrow **→** button next to **Logs Folder** in **Settings -> About**:

- :file-cog: **System Diagnostics Log**: `%LOCALAPPDATA%\LocalTelemetry\logs\lt_system.log` (automatically rotates at 5 MB, up to 3 archive files: `.1.log`, `.2.log`, `.3.log`)
- :file-clock: **Daily Metrics Log**: `%LOCALAPPDATA%\LocalTelemetry\logs\lt_DD-MM-YYYY.log` (gated by the **Metrics Logging** toggle in Settings)
- :bell: **Notifier Process Log**: `%LOCALAPPDATA%\LocalTelemetry\lt_notifier.log` (records toast alert lifecycle and named pipe events)
- :folder-cog: **Data & Logs Directory**:
  - **Installer Mode**: `%LOCALAPPDATA%\LocalTelemetry\` (with logs in `%LOCALAPPDATA%\LocalTelemetry\logs\`)
  - **Portable Mode**: The application folder where `LocalTelemetry.exe` is located (with logs in `logs\`)

---

## :heart-handshake: Getting Community Help

Need assistance, found a bug, or have a question?

- :server: **Discord Server**: Join the [Discord Community](https://discord.gg/M2QevSgW3S) to chat live with the developer and get troubleshooting help.
- :message-square-code: **GitHub Discussions**: Ask questions or share custom setups on [GitHub Discussions](https://github.com/Nicconike/LocalTelemetry/discussions).
- :bug: **Report a Bug**: Open a bug report with your log files on [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues).

> [!TIP]
> For advanced developer diagnostics, IPC packet tracing, and source debugging workflows, see the [**Developer Backend Architecture Guide**](../developer-guide/backend-architecture.md) and [**Hardware Drivers Architecture**](../developer-guide/hardware-drivers.md).
