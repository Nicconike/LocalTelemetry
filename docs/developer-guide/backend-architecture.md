# Core Engine & App Backend (.NET 10)

This page provides an architectural breakdown of the C# backend powering **LocalTelemetry.Core** and **LocalTelemetry.App**.


## ⚙️ Core Class Library (`LocalTelemetry.Core`)

`LocalTelemetry.Core` is built as a pure C# .NET 10 class library with zero UI dependencies.

### Key Components

#### 1. `AppSettings.cs` (`Config/`)
- Manages application preferences saved in `%LOCALAPPDATA%\LocalTelemetry\settings.json`.
- Implements migration logic (`MigrateConfig`) to cleanly upgrade older settings schemas without breaking user defaults.
- Handles atomic file writes to prevent config corruption during unexpected system power loss.

#### 2. `HardwareMonitor.cs` (`Hardware/`)
- Central engine for polling system hardware sensors.
- Maintains timer loops executing periodic sensor checks based on user polling rate settings.
- Aggregates metrics from specialized hardware readers:
  - `AmdGpuMonitor.cs` (AMD ADL library interop)
  - `NvGpuMonitor.cs` (NVIDIA NVAPI library interop)
  - `IntelGpuMonitor.cs` (Intel Graphics API interop)
  - `DiskQuery.cs` (Performance Counter / IOCTL queries)
  - `WindowsNetworkUsageProvider.cs` & `EseNetworkUsageReader.cs` (Network interfaces & ESE databases)

#### 3. `SystemInfo.cs` (`Hardware/`)
- System info collector gathering CPU name, physical core counts, RAM capacity, GPU models, Windows OS build versions and motherboard identifiers.

#### 4. `Log.cs` (`Diagnostics/`)
- Lightweight structured logging utility. Writes logs to `lt_system.log` and daily metric logs `lt_DD-MM-YYYY.log`.


## 🖥️ Desktop Application (`LocalTelemetry.App`)

`LocalTelemetry.App` is a WPF application targeting .NET 10 Windows x64.

### Key Components

#### 1. `App.xaml.cs`
- Application entry point. Handles UAC elevation checks (`IsAdministrator()`), dependency injection setup and unhandled exception logging.
- Initializes system tray icon (`TrayIconManager`), loads configuration, starts `HardwareMonitor` and instantiates `TaskbarOverlay`.

#### 2. `AlertService.cs` (`Services/`)
- Evaluates live telemetry values against threshold rules set in `AppSettings.Alerts`.
- Triggers visual text flashing in `TaskbarOverlay`.
- Dispatches commands to `NotificationClient.cs` to communicate with `LocalTelemetry.Notifier.exe`.

#### 3. `SettingsShell.xaml.cs` (`Settings/`)
- WPF host window managing Microsoft WebView2 control (`Microsoft.Web.WebView2.Wpf`).
- Binds Svelte 5 frontend build assets (`wwwroot/dist/index.html`) using local virtual folder mapping.
- Implements bidirectional IPC messaging between C# and Svelte via `WebMessageReceived` handlers.
