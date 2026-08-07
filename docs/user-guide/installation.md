# Installation & Requirements

This page details the minimum system requirements and step-by-step installation options for **LocalTelemetry**.


## System Requirements

| Requirement          | Minimum Specification                                | Recommended Specification                        |
| :------------------- | :--------------------------------------------------- | :----------------------------------------------- |
| **Operating System** | **Windows 10** (64-bit, v2004+ / build 19041)        | **Windows 11** (64-bit, v22H2+)                  |
| **Architecture**     | **x86-64 / AMD64** (Intel or AMD)                    | **x86-64 / AMD64**                               |
| **Permissions**      | **Administrator (UAC)** *(for PawnIo driver access)* | **Administrator (UAC)**                          |
| **RAM**              | **2 GB**                                             | **4 GB or higher**                               |
| **Disk Space**       | **200 MB** free space                                | **200 MB**                                       |
| **Runtime**          | **WebView2 Runtime**                                 | **WebView2 Runtime** *(Pre-installed on Win 11)* |

> [!WARNING]
> **Unsupported Architectures**: 32-bit (x86) Windows editions and ARM processors (e.g. Snapdragon X Elite) are currently **not supported**.


## Installation Methods

### Method 1: Standard Inno Setup Installer (Recommended)

1. Download `LocalTelemetrySetup.exe` from [GitHub Releases](https://github.com/Nicconike/LocalTelemetry/releases).
2. Double-click the executable to launch the Setup Wizard.
3. Choose your preferred installation directory (Default: `C:\Program Files\LocalTelemetry`).
4. Select whether to create a desktop shortcut or enable **Start with Windows**.
5. Click **Install**. LocalTelemetry will launch automatically in your Windows System Tray once finished.

### Method 2: Portable ZIP

1. Download `LocalTelemetry-win-x64.zip` from [GitHub Releases](https://github.com/Nicconike/LocalTelemetry/releases).
2. Extract the archive contents to any folder of your choice (e.g. `E:\Software\LocalTelemetry`).
3. Run `LocalTelemetry.exe`.

> [!NOTE]
> When running in portable mode, configuration files and network logs will be saved in the application directory rather than `%LOCALAPPDATA%\LocalTelemetry`.


## First-Time Launch & UAC Notice

### What to expect on the very first launch

On the first launch after a fresh install, LocalTelemetry:

1. **Asks for Administrator rights once** — a single Windows UAC prompt appears while LocalTelemetry elevates itself.
2. **Installs the PawnIo hardware driver silently in the background** — while running elevated, LocalTelemetry downloads and installs the PawnIo kernel driver without any further UAC prompts (child processes launched from an already-elevated process inherit its elevation).
3. **May take a few seconds to appear** — the system tray icon and taskbar overlay dock once the driver is installed and the monitoring services are ready.

On every subsequent launch the driver is already present, so the app starts instantly without a UAC prompt.

### Why does LocalTelemetry need Administrator rights?
To read hardware sensors (such as CPU package temperatures, ring voltage and GPU power draw), LocalTelemetry interacts with low-level kernel drivers (such as `PawnIo` / `WinRing0`). Standard user privileges cannot read these hardware registers directly.

> [!NOTE]
> If the background driver installation fails (for example, when offline), LocalTelemetry still runs, but CPU temperature/MSR sensors will be unavailable. Details are written to the system log at `%LOCALAPPDATA%\LocalTelemetry\lt_system.log`.


## Updating LocalTelemetry

To update to a newer version:
- Run the latest `LocalTelemetrySetup.exe` over your existing installation. Your settings (`settings.json`) will be preserved automatically.
- Or extract the newer portable ZIP contents over your previous folder.
