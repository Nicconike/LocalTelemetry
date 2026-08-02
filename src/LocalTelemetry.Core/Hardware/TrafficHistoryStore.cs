namespace LocalTelemetry.Core.Hardware;

/// <summary>Singleton accessor for the <see cref="TrafficHistoryFile"/> instance shared across the app.</summary>
public static class TrafficHistoryStore
{
    private static TrafficHistoryFile? _instance;

    /// <summary>Sets the singleton file instance (must be called once at startup).</summary>
    public static void Initialize(TrafficHistoryFile file)
    {
        _instance = file;
    }

    private static TrafficHistoryFile Instance =>
        _instance ?? throw new InvalidOperationException("TrafficHistoryStore not initialized. Call Initialize() first.");

    /// <summary>Returns all records for the specified month.</summary>
    public static List<DailyRecord> GetMonth(int year, int month)
        => Instance.GetMonth(year, month);

    /// <summary>Returns the accumulated download and upload bytes for today.</summary>
    public static (long downBytes, long upBytes) GetToday()
        => Instance.GetToday();

    /// <summary>Returns a sorted list of available month keys (<c>"yyyy-MM"</c>).</summary>
    public static List<string> GetAvailableMonths()
        => Instance.GetAvailableMonths();

    /// <summary>Returns all records from the store.</summary>
    public static List<DailyRecord> GetAll()
        => Instance.GetAllRecords();

    /// <summary>Adds or replaces a record for the given (date, interface).</summary>
    public static void SetDay(int year, int month, int day, long downBytes, long upBytes, string interfaceName, string source)
        => Instance.SetDay(year, month, day, downBytes, upBytes, interfaceName, source);

    /// <summary>Writes all in-memory changes to disk.</summary>
    public static void Save()
        => Instance.Save();
}
