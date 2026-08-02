<p align="center">
    <img src="docs/public/images/banner.jpg" alt="LocalTelemetry Banner" width="100%" />
</p>

[![CI](https://github.com/Nicconike/LocalTelemetry/actions/workflows/ci.yml/badge.svg)](https://github.com/Nicconike/LocalTelemetry/actions/workflows/ci.yml)
[![Release](https://github.com/Nicconike/LocalTelemetry/actions/workflows/release.yml/badge.svg)](https://github.com/Nicconike/LocalTelemetry/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6.svg)](https://github.com/Nicconike/LocalTelemetry)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Svelte 5](https://img.shields.io/badge/Frontend-Svelte%205-FF3E00.svg)](https://svelte.dev/)
[![codecov](https://codecov.io/gh/Nicconike/LocalTelemetry/graph/badge.svg?token=OQ25T5D97Q)](https://codecov.io/gh/Nicconike/LocalTelemetry)
![GitHub License](https://img.shields.io/github/license/nicconike/localtelemetry)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/13911/badge)](https://www.bestpractices.dev/projects/13911)
[![wakatime](https://wakatime.com/badge/user/018e538b-3f55-4e8e-95fa-6c3225418eed/project/4a017698-b6e1-4859-a4f2-b5379b9a36ef.svg)](https://wakatime.com/badge/user/018e538b-3f55-4e8e-95fa-6c3225418eed/project/4a017698-b6e1-4859-a4f2-b5379b9a36ef)

> [!NOTE] About
> **LocalTelemetry** is a lightweight, real-time Windows hardware monitoring utility that embeds your system metrics directly into your Windows taskbar.

---

## Key Features

- 📊 **Taskbar Embedded Overlay**: Displays real-time hardware metrics neatly inside your Windows taskbar.
- 🌡️ **CPU & GPU Telemetry**: Track temperatures, usage percentages, clock speeds and package power in real time.
- 💾 **RAM & Storage Monitoring**: Monitor active memory usage and live disk read/write throughput.
- 🌐 **Network & Internet Stats**: Live download/upload speeds with daily bandwidth tracking.
- 🔔 **Custom Threshold Alerts**: Flashing visual warnings and desktop toast notifications when temperatures or usage exceed your thresholds.
- 🎨 **Fully Customizable**: Personalize colors, fonts, metric ordering, placement and update intervals through a clean dark-mode settings panel.
- 🔒 **100% Private & Local**: Zero network tracking, no telemetry phoning home and no external API calls. Your hardware data stays entirely on your machine.

---

## System Requirements

| Requirement          | Minimum Specification                                   | Recommended Specification                        |
| :------------------- | :------------------------------------------------------ | :----------------------------------------------- |
| **Operating System** | **Windows 10** (64-bit, v1903+)                         | **Windows 11** (64-bit, v22H2+)                  |
| **Architecture**     | **x86-64 / AMD64** (Intel / AMD)                        | **x86-64 / AMD64**                               |
| **Permissions**      | **Administrator (UAC)** *(For hardware sensor drivers)* | **Administrator (UAC)**                          |
| **RAM**              | **2 GB**                                                | **4 GB or higher**                               |
| **Disk Space**       | **200 MB** free space                                   | **200 MB**                                       |
| **Runtime**          | **WebView2 Runtime**                                    | **WebView2 Runtime** *(Pre-installed on Win 11)* |

*Note: 32-bit (x86) Windows versions and ARM processors are currently not supported.*

---

## Download & Installation

### Option 1: Standard Installer (Recommended)

1. Download the latest `LocalTelemetrySetup.exe` from [**GitHub Releases**](https://github.com/Nicconike/LocalTelemetry/releases).
2. Run the installer and follow the quick setup wizard.
3. LocalTelemetry will launch automatically in your Windows system tray.

### Option 2: Portable ZIP

1. Download `LocalTelemetry-win-x64.zip` from [**GitHub Releases**](https://github.com/Nicconike/LocalTelemetry/releases).
2. Extract the folder to any location on your PC.
3. Run `LocalTelemetry.exe`.

---

## How to Use

1. **System Tray Control**: Right-click the **LocalTelemetry icon** in your Windows System Tray to toggle the taskbar overlay, access settings or exit.
2. **Configure Settings**: Double-click the tray icon (or select **Settings**) to adjust layout colors, toggle specific metrics, enable threshold alerts or set up Windows auto-start.

---

## Getting Help & Support

Need help, found a bug or have a feature suggestion?

- 📖 **Troubleshooting & FAQ**: Read our [**Support Guide**](.github/SUPPORT.md).
- 💬 **Community Discussions**: Ask questions or share ideas on [**GitHub Discussions**](https://github.com/Nicconike/LocalTelemetry/discussions).
- 🐛 **Report an Issue**: Open a bug report via our [**Issue Tracker**](https://github.com/Nicconike/LocalTelemetry/issues).
- 🛠️ **Developers & Contributors**: See [**CONTRIBUTING.md**](CONTRIBUTING.md) for build instructions and code guidelines.

---

## License

LocalTelemetry is free and open-source software licensed under the **GNU General Public License v3.0**. See [`LICENSE`](LICENSE) for details.
