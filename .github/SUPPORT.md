# LocalTelemetry Support & Troubleshooting Guide

Welcome to the **LocalTelemetry** support hub! This document provides complete guidance on getting help, troubleshooting common issues, understanding application permissions and submitting effective bug reports or feature requests.

---

## 1. Quick Navigation & Support Channels

| Topic / Needs                  | Recommended Channel              | Link                                                                                                                          |
| ------------------------------ | -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| 🐛 **Bug Reports**              | GitHub Issue Tracker (YAML Form) | [Report a Bug](https://github.com/Nicconike/LocalTelemetry/issues/new?template=bug_report.yml)                                |
| ✨ **Feature Requests**         | GitHub Issue Tracker (YAML Form) | [Request a Feature](https://github.com/Nicconike/LocalTelemetry/issues/new?template=feature_request.yml)                      |
| 💬 **Questions & Discussions**  | GitHub Discussions               | [Ask a Question](https://github.com/Nicconike/LocalTelemetry/discussions)                                                     |
| 🔒 **Security Vulnerabilities** | GitHub Security Advisories       | [Report Security Issue](https://github.com/Nicconike/LocalTelemetry/security/advisories/new) (See [SECURITY.md](SECURITY.md)) |
| 📖 **Developer & Build Docs**   | Build & Developer Instructions   | See [CONTRIBUTING.md](CONTRIBUTING.md)                                                                                        |

---

## 2. Frequently Asked Questions (FAQ)

### Privacy & Data Handling
* **Q: Is my telemetry data transmitted over the internet or logged externally?**
  * **A:** No. LocalTelemetry is **100% local-only and offline**. The application contains zero remote telemetry, tracking, analytics or external API calls. All hardware monitoring data remains strictly in system memory on your local machine.

---

### Hardware Sensors & Driver Elevation
* **Q: Why does LocalTelemetry prompt for Administrator (UAC) privileges?**
  * **A:** Admin privileges are required exclusively to load the kernel-level **PawnIo** hardware driver for direct Model-Specific Register (MSR) access on CPUs. The application executable runs as `asInvoker` by default and elevates programmatically via UAC only when required for low-level ring-0 sensor access.
* **Q: Why is my GPU usage or temperature showing 0% or N/A?**
  * **A:** LocalTelemetry includes both generic Windows WDDM counters and vendor-specific hardware monitors (NVIDIA NVML, AMD ADL, Intel IGCL). If vendor libraries are unavailable or uninitialized:
    1. Navigate to **Settings > Monitoring**.
    2. Check the **GPU Usage Source** dropdown to switch between WDDM and vendor-specific APIs.
    3. Ensure graphics drivers are updated.

---

### Overlay & Taskbar Integration
* **Q: The taskbar overlay is missing, misaligned or clipped. How do I fix it?**
  * **A:** The overlay utilizes a low-overhead GDI+ native window embedded directly into the Windows taskbar structure. If alignment issues occur:
    - Right-click the system tray icon and toggle **"Hide Taskbar Overlay"** / **"Show Taskbar Overlay"**.
    - Open **Settings > Overlay** to adjust position offsets or color tokens.
    - If using multiple displays, ensure the primary monitor settings match your target display.

---

### Application Lifecycle & Startup
* **Q: How do I configure LocalTelemetry to launch automatically at Windows startup?**
  * **A:** Toggle the auto-start option under **Settings > General**. LocalTelemetry manages startup via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry entries.
* **Q: How do I completely close or exit the application?**
  * **A:** Closing the Settings window minimizes the app to the Windows System Tray. To fully terminate the application, right-click the **LocalTelemetry tray icon** and select **"Exit"**.

---

## 3. Pre-Flight Troubleshooting Checklist

Before opening a bug report, please verify the following:

1. **System Requirements**:
   - Windows 10/11 (64-bit x64).
   - WebView2 Runtime installed (included in modern Windows 11 / Edge updates).
   - .NET 10 Runtime (included in self-contained installer builds).
2. **Search Existing Reports**:
   - Search open and closed [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues) to check if your issue has already been reported or resolved.
3. **Verify App Version**:
   - Check **Settings > About** to ensure you are running the latest release (e.g. `v1.0.0-beta.1`).

---

## 4. Submitting a High-Quality Bug Report

When opening a bug report, providing accurate environment details allows us to diagnose and resolve issues quickly:

* **Windows Build**: Exact OS version (e.g., *Windows 11 24H2 Build 26100*).
* **Hardware Specs**: CPU model, GPU model and installed RAM.
* **Reproduction Steps**: Step-by-step instructions to trigger the issue consistently.
* **Expected vs. Actual Behavior**: Describe clearly what happened vs what should have happened.
