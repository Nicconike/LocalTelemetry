# :heart: Why LocalTelemetry Exists

**LocalTelemetry** was created out of personal necessity. Other monitoring tools only offered the bare essentials, lacked modern taskbar integration, and suffered from recurring driver issues.

LocalTelemetry was built to do everything properly: a **comprehensive, zero-bloat hardware and network monitor** that lives natively on the Windows taskbar.

---

## :goal: What Other Tools Offered vs. What Was Missing

For years, other taskbar monitoring utilities typically only offered the basics:

- **CPU and GPU usage percentages**
- **Basic core temperatures**
- **Simple network upload and download speeds**

When **Windows 11** removed support for legacy taskbar deskbands, other tools broke down. Some required third-party shell injectors, while others relied on floating desktop gadgets that obstructed games, code editors, and full-screen work. The taskbar is the one place on Windows that is **always accessible without getting in the way**.

---

## :shield-alert: The Daily Login False-Positive Problem

By late 2025, an even more frustrating issue appeared. **Every single day, right at Windows login**, my antivirus popped up with a security warning:

> *"Threat detected: Malware / Trojan in Temp folder"*

Starting up the computer every morning only to be greeted by a virus alert in the **`%TEMP%` directory** caused constant annoyance and doubt. After investigating the logs and inspecting the flagged files, the cause became clear: the monitoring tool running at startup was **repeatedly dropping driver files into the temporary directory every time I logged in**, triggering security heuristics.

In **April 2026**, tired of daily login false positives, I decided to do everything myself and build a clean application from scratch.

---

## :sparkles: Learning the Kernel Landscape & Choosing PawnIO

Building LocalTelemetry from the ground up was an eye-opening journey into low-level Windows systems programming:

1. **Kernel Access Is Unavoidable**: Reading low-level processor registers (**MSR / SMN**) and direct hardware vitals strictly requires a kernel-mode driver.
2. **The Certificate Barrier**: Microsoft requires kernel drivers to be signed with an Extended Validation (EV) code-signing certificate, costing **$250 to $400 every year**. Because free independent utilities cannot afford this recurring fee, other tools cut corners by dropping outdated or unmaintained driver binaries into temp folders.
3. **The Clean Solution**: While researching sustainable architectures, I discovered **[PawnIO](https://github.com/namazso/PawnIO)** - a modern, secure, and officially signed kernel driver trusted by established projects like **LibreHardwareMonitor**.

PawnIO installs properly as a verified system driver. It gives LocalTelemetry direct kernel access with **zero temporary file drops at login**, **zero antivirus false positives**, and **full Secure Boot and Core Isolation (HVCI) compliance**.

---

## :rocket: A Complete Monitoring Suite

Rather than settling for the bare-bones stats of other tools, LocalTelemetry was engineered to implement a complete set of practical capabilities:

- **Actual Wattage, Not Just Percentages**: Direct package power draw in **Watts** for CPU and GPU, measured straight from hardware registers.
- **Real-Time Disk Activity**: Live read and write throughput directly beside CPU and GPU metrics.
- **Laptop Battery Health**: Live charge and discharge rates in **Watts**, battery wear levels, and cycle counts.
- **Gapless Network History**: Automatically imports historical records from the Windows System Resource Usage Monitor (**SRUM**) database so bandwidth history never has gaps, saved locally in lightweight **JSON Lines**.
- **Flexible Placement & Appearance**: Dock on either the **left or right side** of the taskbar, adapting to **centered and left-aligned taskbars**, with custom OEM brand palettes (**Intel, AMD, NVIDIA, Corsair, Samsung**, etc) and drag-and-drop metric reordering.
- **Game-Safe Alerts**: Warning notifications run through an isolated background helper over **Named Pipes**, ensuring alerts **never minimize full-screen games** or steal keyboard focus.
- **100% Offline Privacy**: Zero telemetry, zero analytics and zero cloud calls. All data stays strictly on your machine.

---

## :heart-pulse: Support the Project

LocalTelemetry is 100% free and open source under the **GPL-3.0 license**. If LocalTelemetry improves your daily desktop experience:

- :star: **Star on GitHub**: Star [**LocalTelemetry on GitHub**](https://github.com/Nicconike/LocalTelemetry) to help more people discover it.
- :heart: **Like on AlternativeTo**: Upvote and review [**LocalTelemetry on AlternativeTo**](https://alternativeto.net/software/localtelemetry/about/?utm_source=badge&utm_medium=referral).
- :message-square: **Join the Community**: Share ideas and chat with us on the [**Discord Server**](https://discord.gg/M2QevSgW3S).
- :bug: **Suggest Sensors & Features**: Open an issue on our [**GitHub Issue Tracker**](https://github.com/Nicconike/LocalTelemetry/issues).
