# :settings-2: Settings & Customization

LocalTelemetry features a modern settings interface built using **Svelte 5 Runes**, **TypeScript**, and **Vite** rendered inside a native Microsoft **WebView2** window.

## :mouse-pointer-click: Accessing the Settings Interface

- Double-click the **System Tray Icon**.
- Or right-click the tray icon and choose **Open Settings**.

## :notebook-tabs: Comprehensive Settings Tabs

The settings panel is organized into 10 dedicated tabs matching the exact navigation sequence in the application:

### :notebook-text: General

- **Start with Windows**: Automatically launches LocalTelemetry at Windows logon using a Windows Task Scheduler task (`LocalTelemetry Startup`) running with elevated rights, avoiding UAC prompts on reboot.
- **Show System Tray Icon**: Toggle whether the application icon is displayed in the Windows notification area. When disabled, LocalTelemetry operates exclusively through the taskbar overlay. This toggle is disabled if the overlay is hidden to prevent accidental interface lockout.
- **Polling Interval**: Set hardware refresh speed to `0.5 s`, `1 s`, `2 s`, or `5 s`.
- **Temperature Units**: Toggle **Show Temperatures in °F** to switch between Celsius (`°C`) and Fahrenheit (`°F`).

![General Settings Tab](/images/general-settings.png)
*General settings tab for configuring startup behavior, sensor polling rates, and temperature units.*

### :panel-bottom: Overlay

- **Show Overlay**: Master toggle to show or hide the taskbar overlay. This toggle is disabled if the system tray icon is hidden to keep at least one entry point active.
- **Position in taskbar**: Select `Left side` or `Right side` of the taskbar.
- **Offset from Edge**: Adjust horizontal pixel offset from the selected edge (`0px` to `200px`).
- **Opacity**: Adjust overlay opacity (`20%` to `100%`).
- **Scale**: Adjust overlay scaling multiplier (`80%` to `200%`).
- **Double-Click Action**: Configure what happens when double-clicking the overlay (`None`, `Open Task Manager`, or `Open Settings`).

![Overlay Settings Tab](/images/overlay-settings.png)
*Overlay settings tab for configuring taskbar placement, edge offset, opacity, scaling, and double-click behavior.*

### :activity: Monitoring

- **Active Trackers**: Toggle individual hardware polling components on or off (CPU, GPU, RAM, Network, Disk, and Battery on portable devices). Toggling off an unused component completely halts its background polling loop and driver queries to eliminate unnecessary CPU wakeups.
- **CPU Monitoring Mode**:
  - **Clock Mode**: Choose between `Average` (mean clock frequency across all active cores) or `Max Active Core` (highest clocked core frequency).
  - **Usage Source**: Choose between `Overall` (average system-wide load matching Task Manager) or `Max Thread` (highest loaded thread, ideal for spotting single-core bottlenecks in games).
- **GPU Usage Source**: Choose between `Driver (NVML/ADL)` (vendor driver engine load matching MSI Afterburner) or `WDDM (Task Manager)` (Windows display driver model engine statistics).
- **Metrics Logging**: Master toggle to log periodic sensor telemetry to daily log files (`lt_DD-MM-YYYY.log`).
- **Per-Category Log Modes**: Configure logging detail (`Off`, `Errors`, `Full`) independently for each hardware component.
- **System Logging**: Sets diagnostic logging verbosity written to `lt_system.log` (`Off`, `Error`, `Warning`, `Info`, `Debug`). Set to `Debug` only when troubleshooting hardware issues.

![Monitoring Settings Tab](/images/monitoring-settings.png)
*Monitoring settings tab for enabling individual hardware sensor categories, calculation modes, and metric logging.*

### :layout-grid: Layout

- **Metric Drag-and-Drop Ordering**: Drag metrics (or use the up/down arrows) to change the exact column sequence on the taskbar overlay.
- **Add / Remove Metrics**: Select which hardware metrics are displayed on the overlay (such as NVMe disk I/O, laptop battery status, package wattage, and core clocks) or remove unneeded sensors. Battery metrics are automatically hidden on desktop computers without a battery.
- **Reset Layout to Defaults**: Click the **Reset Layout to Defaults** button to instantly restore the standard 5-column default layout (CPU Usage & Temp, GPU Usage & Temp, GPU VRAM & Total Transferred, RAM Usage & Used, and Download & Upload speeds).

![Layout Settings Tab](/images/layout-settings.png)
*Layout configuration tab for reordering and selecting hardware metrics.*

![Customized Taskbar Overlay Result](/images/overlay.png)
*Resulting customized taskbar overlay displaying active NVMe disk throughput and battery vitals alongside core metrics.*

### :network: Network

- **Show Speed in Bits/s**: Toggle displaying network download and upload throughput in bits per second (`b/s`, `Kb/s`, `Mb/s`, `Gb/s`) instead of bytes per second (`B/s`, `KB/s`, `MB/s`, `GB/s`).
- **Network Interface**: Select which network adapter is used for throughput monitoring (`Auto (Best Match)` or a specific physical adapter like `Ethernet` / `Wi-Fi`).
- **Internet Traffic Logging**: Toggle automatic daily bandwidth tracking to record download and upload volumes into `internet_usage.jsonl`.

![Network Settings Tab](/images/network-settings.png)
*Network settings tab for selecting the preferred network adapter and enabling daily traffic logging.*

### :arrow-down-up: Traffic

- **Usage Calendar**: View daily network download and upload totals on an interactive monthly calendar heatmap with 6 usage tiers.
- **Filter by Adapter**: View bandwidth totals for **All**, **Ethernet**, or **Wi-Fi**.
- **Day Detail Breakdown**: Click on any calendar day to inspect per-adapter upload and download volumes.
- **Windows Network Usage Import**: Backfill historical usage from Windows SRUM using date presets (`Last 7 Days`, `Last 14 Days`, `Last 30 Days`) or custom `From` / `To` date pickers.
- **Traffic Database (.dat format) Import**: Import external plaintext `.dat` backup logs into the database.
- **Persistent Storage**: All historical records are saved locally in `internet_usage.jsonl`.

![Traffic History Tab](/images/traffic-settings.png)
*Traffic history calendar tab for tracking daily upload and download bandwidth.*

### :triangle-alert: Alerts

- **Hardware Threshold Limits**: Configure warning triggers for CPU (Usage %, Temp, Frequency throttling, Package Power), RAM (Usage %), GPU (Usage %, VRAM, Temp, Frequency throttling, Board Power), Network (Daily and Monthly bandwidth quotas), and Battery (Low charge %).
- **Network Bandwidth Quotas**: Set warnings for **Daily Internet Usage** (`5 GB` to `500 GB`) and **Monthly Internet Usage** (`100 GB` to `5000 GB`).
- **Notification Channels**: Enable desktop toast popups (**Show Tray Notification**) and in-place visual flashing on the taskbar overlay (**Flash Overlay Briefly**).
- **Cooldown & Session Rules**: Configure cooldown delay between alerts (`5 s` to `120 s`, default: `60 s`) or enable **Only fire once per session (ignores cooldown)**.

![Alerts Settings Tab](/images/alerts-settings.png)
*Alerts settings tab for defining temperature and resource threshold limits.*

### :computer: System

- **Hardware Specs Overview**: Displays a comprehensive diagnostic breakdown of detected system hardware organized into distinct component cards:
  - **System**: Device Name (computer name), OS (Windows display version string), and System Type (`Desktop` or `Laptop`).
  - **Motherboard**: System model name, Motherboard manufacturer and model, hardware version/revision, serial number, BIOS version, firmware boot mode (`UEFI` or `Legacy`), and power supply information (PSU model and wattage rating on desktop PCs).
  - **Processor (CPU)**: Processor model string, CPU vendor brand, physical core count, logical thread count, base clock frequency (GHz), maximum turbo boost frequency (GHz), socket designation (e.g. `Socket LGA 1700`, `Socket AM5`), and thermal design power (TDP in Watts).
  - **Memory (RAM)**: Total installed RAM (with usable capacity in GB), memory manufacturer, clock speed (e.g. `5600 MT/s`), memory generation type (e.g. `DDR5`, `DDR4`), number of populated module slots, and individual capacity and speed breakdown per module (`Module 1`, `Module 2`, etc.).
  - **Graphics (GPU)**: Detected graphics cards labeled as `Dedicated` or `Integrated`, GPU model name, dedicated VRAM capacity (GB), installed driver version, and board TDP power limit (W).
  - **Storage**: Discovered physical drives indexed sequentially (`Disk 1`, `Disk 2`, etc.) with a blue `Boot` badge for the OS drive, drive model name, storage capacity, and connection bus type (`[NVMe]`, `[SATA]`, `[SCSI]`, or `[DISK]`).
  - **Network**: Discovered active network adapters (`Adapter` name for each physical or virtual connection).
  - **Battery** *(Laptops only)*: Detected battery manufacturer, device name / model identifier, factory designed capacity, and current full charge capacity.

![System Information Tab](/images/system-settings.png)
*System information tab summarizing detected hardware specifications and driver versions.*

### :swatch-book: Appearance

- **Window Themes**: Select from built-in curated dark and light theme presets for the configuration interface.
- **Metric Color Customization**: Set individual hex colors for each hardware metric (CPU, GPU, RAM, Disks, Network, Battery) using color pickers or hex inputs.
- **Hardware Group Colors**: Apply unified color overrides across entire component sensor groups.
- **Follow Windows Theme**: Synchronize overlay transparency key color and contrast with the active Windows theme.
- **Reset All to Defaults**: Reset metric colors back to the default vendor-detected color scheme.

![Appearance Settings Tab](/images/appearance-settings.png)
*Appearance tab for customizing window themes, per-metric colors, and component group palettes.*

### :info: About

- **Software Updates**: Check for newer releases via the GitHub Releases API on demand (**Check for Updates** button), configure update check frequency (**Weekly**, **Monthly**, or **Manual**), and toggle **Include pre-release & RC builds**. If an update is detected, a direct **Download** button links straight to the release.
- **Application Metadata**: Displays the active version (`vX.Y.Z`), deployment mode (`Standard` vs `Portable`), build date, target runtime (.NET 10), and platform (Windows x64).
- **Application Directories**: Single-click "→" buttons open the application folders directly in Windows File Explorer:
  - **Standard Mode**: Quick access to the Installation Folder (`C:\Program Files\LocalTelemetry`), Settings & Data Folder (`%LOCALAPPDATA%\LocalTelemetry`), and Logs Folder (`%LOCALAPPDATA%\LocalTelemetry\logs`).
  - **Portable Mode**: Direct access to the Portable Application Folder and the dedicated `logs\` folder.
- **Resources & Links**: Quick shortcuts to report bugs on GitHub Issues, browse official releases, view online documentation, and read the GNU GPLv3 license.
- **Third-Party Software & Licenses**: Lists open-source runtime libraries and components (.NET Runtime, Svelte 5, Vite, Microsoft WebView2, PawnIo Driver, MinVer).

![About Settings Tab](/images/about-settings.png)
*About tab for viewing application version details, update checks, application directories, and open-source licenses.*

::: tip Technical Implementation
To inspect the Svelte 5 component architecture, JSON message contracts, and the bidirectional WebView2 host bridge, see the [Frontend & WebView2 Architecture](/developer-guide/frontend-webview) developer guide.
:::
