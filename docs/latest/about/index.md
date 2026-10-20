# :heart: Why I Built LocalTelemetry

I built **LocalTelemetry** because I wanted a lightweight, reliable hardware and network monitor that lives strictly on the Windows taskbar.

---

## :goal: The Initial Goal: Essentials on the Taskbar

When I started, I did not want floating widgets, desktop gadgets, or heavy overlays that obstruct the screen.

I only wanted to see my essential performance vitals in one glance:

- **CPU and GPU usage**
- **Hardware temperatures**
- **Live network download and upload speeds**

The Windows taskbar is always accessible. Placing these metrics directly on the taskbar lets me check my hardware at any moment without switching windows or interrupting my work or games.

---

## :shield-alert: The Antivirus False Alarm Problem

The biggest frustration with existing free monitoring tools was **constant antivirus false positives**.

Almost every time my system booted, Windows Defender flagged my monitoring utilities:

> *"Threat detected: Trojan / Vulnerable Driver Blocked"*

These alerts caused constant panic and doubt. Every reboot brought scary warnings about malware. I had to inspect logs, check file hashes, and verify that my computer was safe.

The root cause comes down to driver signing economics:

1. Reading low-level processor registers requires a kernel-mode driver.
2. Official Extended Validation (EV) code-signing certificates cost **$250 to $400 every year**. An independent open-source project cannot afford this annual fee.
3. Because of this barrier, older free tools bundled unmaintained drivers like `WinRing0`. Microsoft Defender flags these legacy drivers as vulnerable and treats tools loading them as threats.

---

## :sparkles: The Solution: PawnIO

I wanted a clean architecture that permanently eliminates false virus alarms.

That is why I chose **[PawnIO](https://github.com/namazso/PawnIO)**.

PawnIO is a modern, secure, and officially signed Windows kernel driver. Major open-source projects, including **LibreHardwareMonitor (LHM)**, use and trust PawnIO.

With PawnIO, LocalTelemetry gets direct kernel access for temperature and power monitoring with **zero Defender false alarms**, full Secure Boot support, and clean scans on Antivirus softwares.

---

## :rocket: How LocalTelemetry Evolved

Once the core taskbar monitor was solid, I kept improving it.

What started as a simple monitor for temperatures, usage, and network speeds grew into a comprehensive utility. I added unique capabilities you will not find in other taskbar monitors:

- **Real-Time Disk Activity**: Live read and write throughput directly beside CPU and GPU metrics.
- **Laptop Battery Metrics**: Live charge and discharge rates in Watts.
- **Gapless Network History**: Imports historical data from the Windows System Resource Usage Monitor (**SRUM**) database so your bandwidth history never has gaps.
- **Per-Metric Customization**: Custom colors, custom metric order, and authentic manufacturer brand palettes.
- **Focus-Safe Toast Alerts**: Alerts sent through an out-of-process helper over Named Pipes so notifications never minimize full-screen games or steal keyboard focus.

---

## :heart-pulse: Support LocalTelemetry

LocalTelemetry is 100% free and open source under the **GPL-3.0 license**. If LocalTelemetry improves your daily desktop experience, you can support the project:

- :star: **Star on GitHub**: Star [**LocalTelemetry on GitHub**](https://github.com/Nicconike/LocalTelemetry) to help more people discover the project.
- :heart: **Like on AlternativeTo**: Upvote and review [**LocalTelemetry on AlternativeTo**](https://alternativeto.net/software/localtelemetry/about/?utm_source=badge&utm_medium=referral) to help users find a lightweight alternative.
- :message-square: **Join the Discord**: Chat with the community and share your setup on [**Discord Server**](https://discord.gg/M2QevSgW3S).
- :bug: **Report Bugs & Suggest Features**: Open an issue on [**GitHub Issue Tracker**](https://github.com/Nicconike/LocalTelemetry/issues).
