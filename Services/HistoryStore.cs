using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MoveBit.Services;

/// One persisted day of activity: the answer to "how long did I actually work".
/// The parameterless constructor keeps older history.json files forward-compatible
/// when new insight fields are added.
public sealed record DayRecord
{
    public int ActiveMinutes { get; init; }

    public int SitBreaks { get; init; }

    public int WaterReminders { get; init; }

    public int MicroBreaks { get; init; }

    public int LongestSessionMinutes { get; init; }

    public DayRecord()
    {
    }

    public DayRecord(
        int activeMinutes,
        int sitBreaks,
        int waterReminders,
        int microBreaks,
        int longestSessionMinutes = 0)
    {
        ActiveMinutes = activeMinutes;
        SitBreaks = sitBreaks;
        WaterReminders = waterReminders;
        MicroBreaks = microBreaks;
        LongestSessionMinutes = longestSessionMinutes;
    }
}

/// <summary>
/// Persists daily stats to history.json under the config directory. The data set is
/// intentionally tiny and human-readable, so JSON is a better fit than a database.
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

    /// Keep the live session peak in memory. The app's existing periodic SaveDay call
    /// persists it, so adding insights does not introduce a second write timer.
    public void ObserveLongestSession(DateOnly date, int minutes)
    {
        if (minutes <= 0)
        {
            return;
        }

        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _days.TryGetValue(key, out var existing);
        existing ??= new DayRecord();
        if (minutes > existing.LongestSessionMinutes)
        {
            _days[key] = existing with { LongestSessionMinutes = minutes };
        }
    }

    /// Upsert one day, prune expired entries, and persist atomically (write-then-swap).
    public void SaveDay(DateOnly date, DayRecord record)
    {
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_days.TryGetValue(key, out var existing) && existing.LongestSessionMinutes > record.LongestSessionMinutes)
        {
            record = record with { LongestSessionMinutes = existing.LongestSessionMinutes };
        }

        _days[key] = record;
        PruneExpired(date);

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
        if (count <= 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = new List<(DateOnly, DayRecord)>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            _days.TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var record);
            result.Add((date, record ?? new DayRecord()));
        }

        return result;
    }

    /// Days kept in the file. Pruning happens on every save.
    public static int RetentionDays => 370;

    private void PruneExpired(DateOnly savedDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var anchor = savedDate > today ? savedDate : today;
        var cutoff = anchor.AddDays(-(RetentionDays - 1));

        foreach (var key in _days.Keys.ToArray())
        {
            if (DateOnly.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                && parsed < cutoff)
            {
                _days.Remove(key);
            }
        }
    }
}
