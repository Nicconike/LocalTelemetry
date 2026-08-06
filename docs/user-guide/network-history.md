# Traffic & Network History

LocalTelemetry includes a network logging engine that tracks active network interfaces and records data usage over time.

## Features

- **Live Speed Monitoring**: Real-time download and upload transfer rates.
- **Daily Usage Aggregation**: Records total upload/download per day for each active network interface.
- **Historical Data Store**: Persists daily records as JSON Lines in `internet_usage.jsonl`.
- **Windows SRUM Log Integration**: Reads network usage from the Windows ESE database (`SRUDB.dat`) to account for bandwidth used while LocalTelemetry was not running.

## Viewing Traffic History

In **Settings -> Traffic**:

![Traffic History Calendar & Bandwidth Tracking](/images/traffic-settings.png)
*Figure: Daily traffic history breakdown calendar showing data usage per day.*

1. View a daily breakdown calendar of network consumption over the month (color-coded by bandwidth tier).
2. Click a day to inspect the per-interface records logged for that date.
3. Filter the calendar by a specific network interface using the filter bar (e.g. the active Wi-Fi or Ethernet adapter name).

## Importing Records

- **Import**: A `.dat` import option is available to restore daily usage records from an external plain-text `.dat` file (values in KB).

## Privacy & Storage

Network data is logged **100% locally** to `internet_usage.jsonl` under the app data directory (`%LOCALAPPDATA%\LocalTelemetry` in installed mode, or next to the executable in portable mode). No bandwidth data or visited addresses are ever transmitted over the internet.
