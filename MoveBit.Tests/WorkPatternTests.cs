using System;
using System.IO;
using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public sealed class WorkPatternTests
{
    [Fact]
    public void Away_break_resets_current_session_but_preserves_daily_peak()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300, awayMinutes: 5);

        h.StepMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(30), h.Scheduler.CurrentSessionActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(30), h.Scheduler.Stats.LongestSession);

        h.Idle.Idle = TimeSpan.FromMinutes(10);
        h.StepMinutes(5);
        Assert.Equal(TimeSpan.Zero, h.Scheduler.CurrentSessionActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(30), h.Scheduler.Stats.LongestSession);

        h.Idle.Idle = TimeSpan.Zero;
        h.StepMinutes(20);
        Assert.Equal(TimeSpan.FromMinutes(20), h.Scheduler.CurrentSessionActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(30), h.Scheduler.Stats.LongestSession);
    }

    [Fact]
    public void Completed_forced_break_starts_a_new_continuous_session()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);

        h.StepMinutes(25);
        h.Scheduler.CompleteBreak();
        Assert.Equal(TimeSpan.Zero, h.Scheduler.CurrentSessionActiveTime);

        h.StepMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.CurrentSessionActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(25), h.Scheduler.Stats.LongestSession);
    }

    [Fact]
    public void Old_history_json_without_insight_field_still_loads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "movebit-history-insight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            File.WriteAllText(
                Path.Combine(dir, "history.json"),
                $"{{\"{today:yyyy-MM-dd}\":{{\"ActiveMinutes\":120,\"SitBreaks\":2,\"WaterReminders\":3,\"MicroBreaks\":4}}}}");

            var store = new HistoryStore(dir);
            store.Load();

            var record = Assert.Single(store.GetRecent(1)).Record;
            Assert.Equal(120, record.ActiveMinutes);
            Assert.Equal(0, record.LongestSessionMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Live_longest_session_survives_existing_four_field_flush()
    {
        var dir = Path.Combine(Path.GetTempPath(), "movebit-history-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var store = new HistoryStore(dir);
            store.Load();
            store.ObserveLongestSession(today, 47);
            store.SaveDay(today, new DayRecord(90, 1, 2, 3));

            var reloaded = new HistoryStore(dir);
            reloaded.Load();
            Assert.Equal(47, Assert.Single(reloaded.GetRecent(1)).Record.LongestSessionMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
