# Repository Structure

An overview of the folder structure and codebase organization of **LocalTelemetry**.

## Directory Overview

```
LocalTelemetry/
├── .github/                     # GitHub workflows, templates, funding & issue configs
│   ├── workflows/               # CI/CD actions (ci.yml, release.yml, codeql.yml)
│   ├── CONTRIBUTING.md          # Quick contribution guidelines
│   ├── SUPPORT.md               # Support & FAQ guide
│   └── SECURITY.md              # Vulnerability reporting policy
├── docs/                        # VitePress documentation site
│   ├── .vitepress/              # VitePress configuration (config.mts)
│   ├── user-guide/              # End-user documentation pages
│   ├── developer-guide/         # Developer architecture & contribution pages
│   └── package.json             # VitePress package manifest
├── src/                         # C# & Svelte source code
│   ├── LocalTelemetry.Core/     # Hardware polling, PawnIo, SystemInfo, Config models
│   │   ├── Config/              # AppSettings.cs & serialization options
│   │   ├── Diagnostics/         # Log.cs logging utility
│   │   ├── Hardware/            # Hardware sensors (CPU, NVAPI, ADL, Disk, Network)
│   │   └── Models/              # Data transfer objects & metric structures
│   ├── LocalTelemetry.App/      # WPF Windows desktop app & overlay
│   │   ├── Overlay/             # TaskbarOverlay.cs (Win32 P/Invoke taskbar window)
│   │   ├── Services/            # AlertService.cs & NotificationClient.cs
│   │   ├── Settings/            # SettingsShell.xaml & Svelte 5 WebView2 frontend
│   │   │   └── wwwroot/         # Svelte 5 (Vite + Bun + TS) settings panel
│   │   ├── Tray/                # TrayIconManager.cs
│   │   └── App.xaml.cs          # WPF Entry point & lifecycle management
│   └── LocalTelemetry.Notifier/ # Standalone toast notification helper executable
│       ├── Program.cs           # Notifier CLI parser & WinForms entry point
│       └── ToastForm.cs         # Non-focus stealing toast window
├── Directory.Build.props        # Global C# build properties (.NET 10, C# 13)
├── LocalTelemetry.sln           # Visual Studio solution file
├── setup.iss                    # Inno Setup installer script
└── README.md                    # GitHub repository main landing page
```
