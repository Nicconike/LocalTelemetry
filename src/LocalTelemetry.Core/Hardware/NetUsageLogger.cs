using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Tracks daily network usage by accumulating per-tick byte deltas per interface
/// and persisting via <see cref="TrafficHistoryFile"/>.</summary>
public sealed class NetUsageLogger : IDisposable
{
    private readonly AppSettings _cfg;
    private readonly TrafficHistoryFile _historyFile;
    private readonly object _lock = new();
    private readonly Dictionary<string, (double down, double up)> _perInterface = [];
    private DateTime _lastDate = DateTime.Now.Date;
    private DateTime _lastSnapTime;
    private string _activeNic = string.Empty;

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

        var todayRecords = _historyFile.GetMonth(_lastDate.Year, _lastDate.Month)
            .Where(r => r.Day == _lastDate.Day);
        foreach (var r in todayRecords)
        {
            if (!string.IsNullOrEmpty(r.Interface))
                _perInterface[r.Interface] = (r.DownBytes, r.UpBytes);
            else if (_perInterface.Count == 0)
                _perInterface[""] = (r.DownBytes, r.UpBytes);
        }
    }

    public void Record(TelemetrySnapshot snap)
    {
        if (!_cfg.NetUsage.Enabled) return;

        _activeNic = snap.NetInterfaceName;

        double actualDt;
        if (_lastSnapTime == default)
            actualDt = _cfg.Monitoring.PollIntervalMs / 1000.0;
        else
            actualDt = (snap.Timestamp - _lastSnapTime).TotalSeconds;
        _lastSnapTime = snap.Timestamp;

        double deltaDown = snap.NetDownBps * actualDt;
        double deltaUp = snap.NetUpBps * actualDt;

        var now = DateTime.Now;
        var today = now.Date;

        lock (_lock)
        {
            if (today != _lastDate)
            {
                foreach (var kvp in _perInterface)
                {
                    long downBytes = (long)kvp.Value.down;
                    long upBytes = (long)kvp.Value.up;
                    if (downBytes > 0 || upBytes > 0)
                        _historyFile.SetDay(_lastDate.Year, _lastDate.Month, _lastDate.Day, downBytes, upBytes, kvp.Key, "LocalTelemetry");
                }
                _historyFile.Save();
                _perInterface.Clear();
                _lastDate = today;
            }

            if (!_perInterface.TryGetValue(_activeNic, out var cur))
                cur = (0, 0);
            _perInterface[_activeNic] = (cur.down + deltaDown, cur.up + deltaUp);

            var val = _perInterface[_activeNic];
            _historyFile.SetDay(today.Year, today.Month, today.Day, (long)val.down, (long)val.up, _activeNic, "LocalTelemetry");
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

    public void Dispose() => FlushFinal();
}
