using System;
using System.IO;
using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir;

    public HistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "movebit-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best-effort
        }
    }

    [Fact]
    public void SaveDay_roundtrips_through_disk()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
        var store = new HistoryStore(_dir);
        store.Load();
        store.SaveDay(yesterday, new DayRecord(123, 2, 4, 6));

        var reloaded = new HistoryStore(_dir); // fresh instance reads from disk only
        reloaded.Load();

        var (_, record) = Assert.Single(reloaded.GetRecent(3), x => x.Date == yesterday);
        Assert.Equal(123, record.ActiveMinutes);
        Assert.Equal(2, record.SitBreaks);
        Assert.Equal(4, record.WaterReminders);
        Assert.Equal(6, record.MicroBreaks);
    }

    [Fact]
    public void GetRecent_fills_holes_with_zero_days_oldest_first()
    {
        var store = new HistoryStore(_dir);
        store.Load();

        var today = DateOnly.FromDateTime(DateTime.Now);
        store.SaveDay(today, new DayRecord(90, 1, 2, 3));

        var week = store.GetRecent(7);

        Assert.Equal(7, week.Count);
        Assert.Equal(today.AddDays(-6), week[0].Date);
        Assert.Equal(today, week[6].Date);
        Assert.Equal(90, week[6].Record.ActiveMinutes);
        Assert.Equal(0, week[0].Record.ActiveMinutes); // hole day
        Assert.All(week.GetRange(0, 6), day => Assert.Equal(0, day.Record.SitBreaks));
    }

    [Fact]
    public void SaveDay_overwrites_same_day()
    {
        var store = new HistoryStore(_dir);
        store.Load();
        var today = DateOnly.FromDateTime(DateTime.Now);

        store.SaveDay(today, new DayRecord(10, 0, 0, 0));
        store.SaveDay(today, new DayRecord(42, 1, 1, 1));

        var (date, record) = Assert.Single(store.GetRecent(1));
        Assert.Equal(today, date);
        Assert.Equal(42, record.ActiveMinutes);
    }
}
