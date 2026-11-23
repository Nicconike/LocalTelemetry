using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Tracks daily network usage by accumulating per-tick byte deltas per interface
/// and persisting via <see cref="TrafficHistoryFile"/>.</summary>
public sealed class NetUsageLogger : IDisposable
{
    private readonly AppSettings _cfg;
    private readonly TrafficHistoryFile _historyFile;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (double down, double up)> _perInterface = [];
    private DateTime _lastDate;
    private DateTime _lastSnapTime;
    private DateTime _lastFlushTime = DateTime.UtcNow;

    public TrafficHistoryFile HistoryFile => _historyFile;

    public NetUsageLogger(AppSettings cfg, TrafficHistoryFile? existingFile = null)
    {
        _cfg = cfg;
        if (existingFile is not null)
        {
            _historyFile = existingFile;
        }
        else
        {
            _historyFile = new TrafficHistoryFile(AppSettings.NetUsagePath);
            _historyFile.Load();
        }
        _lastDate = DateTime.Now.Date;
        SyncWithHistory();
        TrafficHistoryStore.HistoryUpdated += SyncWithHistory;
    }

    public void SyncWithHistory()
    {
        lock (_lock)
        {
            var today = DateTime.Now.Date;
            var todayRecords = _historyFile.GetMonth(today.Year, today.Month)
                .Where(r => r.Day == today.Day);
            foreach (var r in todayRecords)
            {
                string iface = r.Interface ?? "";
                if (_perInterface.TryGetValue(iface, out var cur))
                {
                    // If the app was actively logging while the background import ran,
                    // add the live deltas recorded during that window onto the imported baseline.
                    _perInterface[iface] = (Math.Max(cur.down, r.DownBytes + cur.down), Math.Max(cur.up, r.UpBytes + cur.up));
                }
                else
                {
                    _perInterface[iface] = (r.DownBytes, r.UpBytes);
                }
            }
        }
    }

    public void Record(TelemetrySnapshot snap)
    {
        if (!_cfg.NetUsage.Enabled) return;
        if (double.IsNaN(snap.NetDownBps) || double.IsNaN(snap.NetUpBps)) return;
        if (snap.NetDownBps < 0 || snap.NetUpBps < 0) return;

        string activeNic = snap.NetInterfaceName;
        double actualDt = CalculateDeltaSeconds(snap.Timestamp);
        double deltaDown = snap.NetDownBps * actualDt;
        double deltaUp = snap.NetUpBps * actualDt;
        var today = DateTime.Now.Date;

        lock (_lock)
        {
            if (today != _lastDate)
            {
                HandleDayRollover(today);
            }

            if (!_perInterface.TryGetValue(activeNic, out var cur))
                cur = (0, 0);
            _perInterface[activeNic] = (cur.down + deltaDown, cur.up + deltaUp);

            var val = _perInterface[activeNic];
            _historyFile.SetDay(today.Year, today.Month, today.Day, (long)val.down, (long)val.up, activeNic, "LocalTelemetry");

            if ((DateTime.UtcNow - _lastFlushTime).TotalSeconds >= 60)
            {
                _lastFlushTime = DateTime.UtcNow;
                _historyFile.Save();
            }
        }
    }

    private double CalculateDeltaSeconds(DateTime timestamp)
    {
        double normalDt = _cfg.Monitoring.PollIntervalMs / 1000.0;
        if (_lastSnapTime == default)
        {
            _lastSnapTime = timestamp;
            return normalDt;
        }

        double dt = (timestamp - _lastSnapTime).TotalSeconds;
        _lastSnapTime = timestamp;

        // If clock jumped backwards or excessive pause occurred (e.g. system sleep/hibernation),
        // clamp dt to poll interval to prevent huge anomalous traffic spikes.
        if (dt <= 0 || dt > normalDt * 3.0)
        {
            return normalDt;
        }

        return dt;
    }

    private void HandleDayRollover(DateTime today)
    {
        foreach (var kvp in _perInterface)
        {
            long downBytes = (long)kvp.Value.down;
            long upBytes = (long)kvp.Value.up;
            if (downBytes > 0 || upBytes > 0)
                _historyFile.SetDay(_lastDate.Year, _lastDate.Month, _lastDate.Day, downBytes, upBytes, kvp.Key, "LocalTelemetry");
        }
        _historyFile.Save();
        Diagnostics.Log.Info($"Rolled over network usage history to new day ({today:dd-MM-yyyy})");
        _perInterface.Clear();
        _lastDate = today;

        SeedTodayRecords(today);
        TrafficHistoryStore.NotifyHistoryUpdated();
    }

    private void SeedTodayRecords(DateTime today)
    {
        var todayRecords = _historyFile.GetMonth(today.Year, today.Month).Where(r => r.Day == today.Day);
        foreach (var r in todayRecords)
        {
            if (!string.IsNullOrEmpty(r.Interface))
                _perInterface[r.Interface] = (r.DownBytes, r.UpBytes);
            else if (_perInterface.Count == 0)
                _perInterface[""] = (r.DownBytes, r.UpBytes);
        }
    }

    public void FlushFinal()
    {
        lock (_lock)
        {
            var today = DateTime.Now.Date;
            foreach (var kvp in _perInterface)
            {
                long downBytes = (long)kvp.Value.down;
                long upBytes = (long)kvp.Value.up;
                if (downBytes > 0 || upBytes > 0)
                    _historyFile.SetDay(today.Year, today.Month, today.Day, downBytes, upBytes, kvp.Key, "LocalTelemetry");
            }
            _historyFile.Save();
        }
    }

    public double GetTodayTotalGb()
    {
        lock (_lock)
        {
            double bytes = _perInterface.Values.Sum(v => v.down + v.up);
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }
    }

    public double GetMonthTotalGb()
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var monthRecords = _historyFile.GetMonth(now.Year, now.Month);
            long historicalBytes = monthRecords.Sum(r => r.DownBytes + r.UpBytes);
            return historicalBytes / (1024.0 * 1024.0 * 1024.0);
        }
    }

    public void Dispose()
    {
        TrafficHistoryStore.HistoryUpdated -= SyncWithHistory;
        FlushFinal();
    }
}
