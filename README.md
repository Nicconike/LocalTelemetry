<p align="center">
    <img src="docs/public/images/banner.jpg" alt="LocalTelemetry Banner" width="100%" />
</p>

<div align="center">
    <details>
        <summary align="center">
            <a href="https://github.com/Nicconike/LocalTelemetry/releases">
                <img src="https://img.shields.io/github/v/release/nicconike/LocalTelemetry?include_prereleases&sort=semver&logo=github&logoColor=white&label=Release&color=0DBCBF&labelColor=101318" alt="GitHub Release" />
            </a>
            <a href="https://github.com/Nicconike/LocalTelemetry/releases">
                <img src="https://img.shields.io/github/downloads/nicconike/LocalTelemetry/total?logo=github&logoColor=white&color=0DBCBF&labelColor=101318&label=Downloads" alt="GitHub Downloads" />
            </a>
            <a href="https://github.com/Nicconike/LocalTelemetry">
                <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0DBCBF?logo=windows11&logoColor=white&labelColor=101318" alt="Platform" />
            </a>
            <a href="https://github.com/Nicconike/LocalTelemetry/blob/main/LICENSE">
                <img src="https://img.shields.io/github/license/nicconike/localtelemetry?logo=github&logoColor=white&color=0DBCBF&labelColor=101318" alt="License" />
            </a>
            <a href="https://discord.gg/M2QevSgW3S">
                <img src="https://img.shields.io/badge/Join-Discord-0DBCBF?logo=discord&logoColor=5865F2&labelColor=101318" alt="Discord" />
            </a>
        </summary>
        <br />
        <p align="center">
            <a href="https://github.com/Nicconike/LocalTelemetry/actions/workflows/ci.yml">
                <img src="https://img.shields.io/github/actions/workflow/status/Nicconike/LocalTelemetry/ci.yml?logo=githubactions&logoColor=white&labelColor=101318&label=CI" alt="CI" />
            </a>
            <a href="https://github.com/Nicconike/LocalTelemetry/actions/workflows/release.yml">
                <img src="https://img.shields.io/github/actions/workflow/status/Nicconike/LocalTelemetry/release.yml?logo=githubactions&logoColor=white&labelColor=101318&label=Release" alt="Release" />
            </a>
            <a href="https://github.com/Nicconike/LocalTelemetry/actions/workflows/docs.yml">
                <img src="https://img.shields.io/github/actions/workflow/status/Nicconike/LocalTelemetry/docs.yml?logo=githubactions&logoColor=white&labelColor=101318&label=Deploy%20Docs" alt="Deploy Docs" />
            </a>
            <a href="https://sonarcloud.io/summary/new_code?id=LocalTelemetry">
                <img src="https://img.shields.io/sonar/quality_gate/LocalTelemetry?server=https%3A%2F%2Fsonarcloud.io&logo=sonarqubecloud&logoColor=F3702A&labelColor=101318&label=SonarCloud" alt="SonarCloud" />
            </a>
            <a href="https://codecov.io/gh/Nicconike/LocalTelemetry">
                <img src="https://img.shields.io/codecov/c/github/Nicconike/LocalTelemetry?token=OQ25T5D97Q&logo=codecov&logoColor=F01F7A&labelColor=101318&label=Codecov" alt="Codecov" />
            </a>
        </p>
        <p align="center">
            <a href="https://dotnet.microsoft.com/">
                <img src="https://img.shields.io/badge/.NET-10.0-0DBCBF?logo=dotnet&logoColor=white&labelColor=101318" alt=".NET 10" />
            </a>
            <a href="https://svelte.dev/">
                <img src="https://img.shields.io/badge/Frontend-Svelte%205-0DBCBF?logo=svelte&logoColor=FF3E00&labelColor=101318" alt="Svelte 5" />
            </a>
            <a href="https://www.bestpractices.dev/projects/13911">
                <img src="https://img.shields.io/cii/level/13911?labelColor=101318&label=OpenSSF%20Best%20Practices" alt="OpenSSF Best Practices" />
            </a>
            <a href="https://scorecard.dev/viewer/?uri=github.com/Nicconike/LocalTelemetry">
                <img src="https://img.shields.io/ossf-scorecard/github.com/Nicconike/LocalTelemetry?labelColor=101318&label=Scorecard" alt="Scorecard" />
            </a>
            <a href="https://alternativeto.net/software/localtelemetry/about/?utm_source=badge&utm_medium=referral" target="_blank">
                <img src="https://img.shields.io/badge/Like_on-AlternativeTo-0DBCBF?logo=alternativeto&logoColor=white&labelColor=101318" alt="AlternativeTo" />
            </a>
            <a href="https://wakatime.com/badge/user/018e538b-3f55-4e8e-95fa-6c3225418eed/project/4a017698-b6e1-4859-a4f2-b5379b9a36ef">
                <img src="https://wakatime.com/badge/user/018e538b-3f55-4e8e-95fa-6c3225418eed/project/4a017698-b6e1-4859-a4f2-b5379b9a36ef.svg?color=0DBCBF&label_color=101318" alt="wakatime" />
            </a>
        </p>
    </details>
</div>

<p align="center">
    <a href="https://nicconike.github.io/LocalTelemetry/"><strong>Documentation & Guides »</strong></a>
    ·
    <a href="https://nicconike.github.io/LocalTelemetry/user-guide/quickstart">Quickstart</a>
    ·
    <a href="https://nicconike.github.io/LocalTelemetry/user-guide/metrics-and-sensors">Supported Sensors</a>
    ·
    <a href="https://nicconike.github.io/LocalTelemetry/user-guide/customization">Customization</a>
</p>

---

## Why LocalTelemetry?

Typical vendor software suites consume hundreds of megabytes of RAM across multiple background services. Windows 11 also removed support for legacy taskbar deskbands.

LocalTelemetry provides hardware monitoring in the taskbar: lightweight, native, and completely offline.

### Feature Comparison

| Capability | LocalTelemetry | Typical Vendor Suites |
| :--- | :---: | :---: |
| **Windows 11 Taskbar Integration** | Embedded next to clock (no floating windows) | Floating overlay or tray tooltip only |
| **Background Memory Footprint** | **~15–20 MB RAM** (~40–60 MB when Settings window is active) | 400–100 MB RAM across helper processes |
| **Low-Level Hardware Access** | Direct Intel MSR & AMD SMN registers via PawnIO | Proprietary services or high-latency queries |
| **Layout Customization** | 8 curated presets with column swapping & drag-and-drop ordering | Fixed vendor window layouts |
| **Network Traffic Accounting** | Daily upload & download history with calendar heatmap | None |
| **Privacy & Connectivity** | 100% offline with zero telemetry or tracking | Mandatory accounts & cloud telemetry |
| **Game-Safe Alerts** | Non-focus-stealing toasts with 10°C thermal hysteresis | Minimizes or interrupts full-screen games |

---

## Key Features

- **Native Taskbar Overlay**: Embeds live metrics next to your clock. Automatically supports both centered and left-aligned Windows 11 taskbars.
- **Overlay Layout Presets**: Switch instantly between 8 curated presets (Default, Gaming, Compact, Micro, Thermals, Workstation, Network, Full Telemetry) or build custom arrangements with interactive column swapping, live preview, and drag-and-drop ordering.
- **Deep Kernel Telemetry**: Direct CPU clocks, wattage, and thermals via Intel MSR and AMD SMN registers with ACPI fallback. Full support for NVIDIA (NVML), AMD (ADL), and Intel (IGCL) GPUs, including shared memory and package thermal fallbacks for integrated graphics.
- **Real-Time Network Speed & History**: Track live upload and download speeds with local daily history logging, JSONL backup imports, and Windows network usage backfilling.
- **Adaptive OEM Brand Themes**: Detects installed hardware vendors (Intel, AMD, NVIDIA, Samsung, Corsair) and applies authentic manufacturer color palettes.
- **100% Offline & Private**: Zero telemetry collection, zero trackers, and zero cloud calls. All metrics stay strictly on your local PC.
- **Smart Game-Safe Alerts**: Anti-spike sustained breach filtering, 10°C thermal hysteresis, taskbar warning highlights, and non-intrusive focus-safe desktop toasts.

---

## Visual Overview

### 1. Application Walkthrough

A complete look at LocalTelemetry in action-real-time CPU, GPU, RAM, disk, and network telemetry with seamless system tray and taskbar integration:

<p align="center">
    <img src="docs/public/videos/LocalTelemetry.gif" alt="LocalTelemetry Application Walkthrough" style="border-radius: 8px; max-width: 85%;" />
</p>

### 2. Live Taskbar Overlay

Embeds directly into the Windows taskbar next to your system clock, matching system fonts and scaling automatically across multiple display DPIs:

<p align="center">
    <img src="docs/public/videos/default-overlay.gif" alt="LocalTelemetry Live Taskbar Overlay" style="border-radius: 8px;" />
</p>

### 3. Settings & Customization

Configure layout presets, 2D column pairing, polling intervals, and custom brand color schemes (Intel, AMD, NVIDIA, Corsair, Samsung) effortlessly in the Settings window:

<p align="center">
    <img src="docs/public/videos/customization.gif" alt="LocalTelemetry Settings & Customization Walkthrough" style="border-radius: 8px; max-width: 90%;" />
</p>

### 4. Traffic History Calendar

Inspect daily upload and download totals per network adapter with JSONL backup imports and historical Windows network usage backfilling:

<p align="center">
    <img src="docs/public/images/traffic-settings.png" alt="Traffic History Calendar" style="border-radius: 8px; max-width: 85%;" />
</p>

---

## Installation Options

### Option 1: Setup Wizard Installer (Recommended)

1. Download `LocalTelemetrySetup.exe` from [**GitHub Releases**](https://github.com/Nicconike/LocalTelemetry/releases).
2. Run the installer. Setup automatically verifies the .NET 10 Desktop Runtime and installs the required PawnIO kernel sensor driver.
3. LocalTelemetry starts immediately and runs quietly in your taskbar.

### Option 2: Scoop Package Manager

```powershell
scoop bucket add localtelemetry https://github.com/Nicconike/LocalTelemetry
scoop install localtelemetry
```

### Option 3: Portable ZIP

1. Ensure the required runtimes are installed:
   - [**Microsoft .NET 10 Desktop Runtime (x64)**](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe)
   - [**Microsoft Edge WebView2 Runtime (x64)**](https://go.microsoft.com/fwlink/p/?LinkId=2124703) *(pre-installed on Windows 11)*
2. Download `LocalTelemetry-win-x64.zip` from [**GitHub Releases**](https://github.com/Nicconike/LocalTelemetry/releases).
3. Extract and run `LocalTelemetry.exe` as Administrator.

---

## Verifying Downloads & Checksums

Every GitHub Release includes cryptographic SHA-256 hashes (`checksums.txt`), Sigstore Cosign signature bundles (`.sigstore.json`), and SLSA build provenance attestations.

### 1. SHA-256 Checksum Verification

Download `checksums.txt` and your binary into the same folder. Compute the file hash in PowerShell:

```powershell
Get-FileHash .\LocalTelemetrySetup.exe -Algorithm SHA256
```

Or verify all release binaries automatically against `checksums.txt`:

```powershell
Get-Content .\checksums.txt | ForEach-Object {
    $expectedHash, $filename = -split $_
    if (Test-Path $filename) {
        $actualHash = (Get-FileHash $filename -Algorithm SHA256).Hash
        [PSCustomObject]@{ File = $filename; Valid = ($actualHash -eq $expectedHash) }
    }
}
```

### 2. Sigstore Cosign Keyless Signatures

Verify release binaries using keyless OpenID Connect (OIDC) signatures:

```powershell
cosign verify-blob --bundle .\LocalTelemetrySetup.exe.sigstore.json `
    --certificate-identity-regexp "https://github.com/Nicconike/LocalTelemetry/.*" `
    --certificate-oidc-issuer "https://token.actions.githubusercontent.com" `
    .\LocalTelemetrySetup.exe
```

### 3. SLSA Build Provenance

Verify supply chain build attestations using the GitHub CLI:

```powershell
gh attestation verify .\LocalTelemetrySetup.exe --repo Nicconike/LocalTelemetry
gh attestation verify .\LocalTelemetry-win-x64.zip --repo Nicconike/LocalTelemetry
```

---

## System Requirements

| Requirement | Specification |
| :--- | :--- |
| **Operating System** | **Windows 10** (64-bit, v2004+ / build 19041) or **Windows 11** (64-bit, v22H2+) |
| **Architecture** | **x86-64 / AMD64** (Intel / AMD) |
| **Permissions** | **Administrator** *(Required for PawnIO MSR kernel driver)* |
| **System RAM** | **2 GB RAM** minimum (**4 GB RAM or higher** recommended) |
| **App Footprint** | **~15–30 MB RAM** (background taskbar monitoring) / **~40–60 MB RAM** (when Settings window is active) |
| **Runtimes** | **.NET 10 Desktop Runtime** & **WebView2** |

---

## Support & Community

If LocalTelemetry makes your desktop experience better, here is how you can support the project:

- **Star the Repository**: Star [**LocalTelemetry on GitHub**](https://github.com/Nicconike/LocalTelemetry) to help other users discover it.
- **Join the Discord Community**: Chat with the developer and get help on [**Discord**](https://discord.gg/M2QevSgW3S).
- **Suggest Features**: Share ideas or feature suggestions on [**GitHub Discussions**](https://github.com/Nicconike/LocalTelemetry/discussions).
- **Report Issues**: Found a bug or driver error? Open an issue on [**GitHub Issues**](https://github.com/Nicconike/LocalTelemetry/issues).
- **Upvote on AlternativeTo**: Recommend [**LocalTelemetry on AlternativeTo**](https://alternativeto.net/software/localtelemetry/about/?utm_source=badge&utm_medium=referral).

---

## License

This project is licensed under the terms described in the [LICENSE](LICENSE) file.
