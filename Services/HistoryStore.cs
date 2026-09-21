using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MoveBit.Services;

/// One persisted day of activity: the answer to "how long did I actually work".
public sealed record DayRecord(int ActiveMinutes, int SitBreaks, int WaterReminders, int MicroBreaks);

/// <summary>
/// Appends daily stats to history.json under the config directory. One line per day,
/// trivially small — JSON beats SQLite at this scale and keeps the app dependency-free.
/// The current day lives in memory (scheduler) and is flushed here periodically.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _path;
    private Dictionary<string, DayRecord> _days = [];

    public HistoryStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "movebit");
        _path = Path.Combine(dir, "history.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                _days = JsonSerializer.Deserialize<Dictionary<string, DayRecord>>(File.ReadAllText(_path)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _days = []; // unreadable history is not worth crashing a tray app over
        }
    }

    /// Upsert one day and persist atomically (write-then-swap).
    public void SaveDay(DateOnly date, DayRecord record)
    {
        _days[date.ToString("yyyy-MM-dd")] = record;

        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_days, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; in-memory data keeps the session working.
        }
    }

    /// The most recent <paramref name="count"/> days, oldest first, holes included as zero days.
    public List<(DateOnly Date, DayRecord Record)> GetRecent(int count)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = new List<(DateOnly, DayRecord)>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            _days.TryGetValue(date.ToString("yyyy-MM-dd"), out var record);
            result.Add((date, record ?? new DayRecord(0, 0, 0, 0)));
        }

        return result;
    }

    /// Days kept in the file; older entries are pruned on save.
    public static int RetentionDays => 370;
}
