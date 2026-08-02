# Traffic & Network History

LocalTelemetry includes a network logging engine that tracks active network interfaces and logs internet data usage over time.


## Features

- **Live Speed Monitoring**: Real-time download and upload transfer rates updated continuously.
- **Daily Usage Aggregation**: Records total Megabytes (MB), Gigabytes (GB) and Terabytes (TB) uploaded and downloaded each day.
- **Historical Data Store**: Saves records into `internet_usage.jsonl` (JSON Lines format) for lightweight data persistence.
- **Windows ESE Log Integration**: Support for reading Windows ESE (Extensible Storage Engine) network usage databases (`SRUDB.dat`) to account for bandwidth used while LocalTelemetry was not running.


## Viewing Traffic History

In **Settings -> Traffic**:

![Traffic History Calendar & Bandwidth Tracking](/images/network-history.png)
*Figure: Daily traffic history breakdown calendar showing data usage per day.*

1. View daily breakdown calendar of network consumption over the month (color-coded by bandwidth tier).
2. Switch between **All**, **Ethernet** or **WiFi** network interfaces.
3. Export network usage logs as `.jsonl` or `.csv`.


## Privacy & Storage

Network data is logged **100% locally** to `%LOCALAPPDATA%\LocalTelemetry\internet_usage.jsonl`. No bandwidth data or visited IP addresses are ever stored or transmitted over the internet.
