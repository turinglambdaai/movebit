using System;
using System.Collections.Generic;
using MoveBit.Models;
using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

/// Drives ReminderScheduler with a fake clock and fake idle source.
internal sealed class Harness
{
    public sealed class FakeTime : TimeProvider
    {
        // Start at "today 09:00 in the machine's local timezone", expressed in UTC, so
        // GetLocalNow (non-overridable, uses the real system timezone) reads 09:00 too.
        // Works identically on local (+8) and UTC CI runners.
        private DateTimeOffset _now =
            TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified));

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan t) => _now += t;
    }

    public sealed class FakeIdle : IIdleProvider
    {
        public TimeSpan? Idle { get; set; } = TimeSpan.Zero;

        public TimeSpan? GetIdleTime() => Idle;
    }

    public FakeTime Time { get; } = new();

    public FakeIdle Idle { get; } = new();

    public ReminderScheduler Scheduler { get; }

    public List<ReminderEvent> Fired { get; } = [];

    public Harness(int sitMinutes = 45, int waterMinutes = 30, int awayMinutes = 5)
    {
        var config = new ReminderConfig
        {
            SitReminderMinutes = sitMinutes,
            WaterReminderMinutes = waterMinutes,
            AwayResetMinutes = awayMinutes,
        };
        Scheduler = new ReminderScheduler(config, Time, Idle);
        Scheduler.ReminderFired += (_, e) => Fired.Add(e);
    }

    /// <summary>Advance the clock then tick, in one step.</summary>
    public void Step(TimeSpan t)
    {
        Time.Advance(t);
        Scheduler.Tick();
    }

    /// <summary>
    /// Advance the clock in small ticks, like the real 30-second UI timer would
    /// (single big steps get clamped by design, mirroring system-sleep handling).
    /// </summary>
    public void StepMinutes(double totalMinutes, double stepMinutes = 5)
    {
        var remaining = TimeSpan.FromMinutes(totalMinutes);
        var step = TimeSpan.FromMinutes(stepMinutes);
        while (remaining > TimeSpan.Zero)
        {
            var now = remaining < step ? remaining : step;
            Step(now);
            remaining -= now;
        }
    }
}

public class SchedulerTests
{
    [Fact]
    public void Sit_reminder_fires_after_active_time_accumulates()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.StepMinutes(40);
        Assert.Empty(h.Fired); // 40 min < 45 min

        h.StepMinutes(10);

        var sit = Assert.Single(h.Fired);
        Assert.Equal(ReminderKind.Sit, sit.Kind);
        Assert.Equal(1, h.Scheduler.Stats.SitReminders);
        Assert.Equal(TimeSpan.FromMinutes(50), h.Scheduler.Stats.ActiveTime);
    }

    [Fact]
    public void Water_reminder_fires_on_its_own_cycle()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 30);

        h.StepMinutes(35);

        var water = Assert.Single(h.Fired);
        Assert.Equal(ReminderKind.Water, water.Kind);
        Assert.Equal(1, h.Scheduler.Stats.WaterReminders);
    }

    [Fact]
    public void Away_time_does_not_accumulate_and_return_resets_cycles()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.StepMinutes(40); // 40 min active, no fire yet

        h.Idle.Idle = TimeSpan.FromMinutes(10); // user left
        h.StepMinutes(20); // away: nothing accumulates, nothing fires
        Assert.Empty(h.Fired);
        Assert.Equal(TimeSpan.FromMinutes(40), h.Scheduler.Stats.ActiveTime);

        h.Idle.Idle = TimeSpan.Zero; // user is back: cycles restart
        h.StepMinutes(40);
        Assert.Empty(h.Fired); // no stale reminder dumped after the break

        h.StepMinutes(10); // 50 min since return
        Assert.Equal(ReminderKind.Sit, Assert.Single(h.Fired).Kind);
        Assert.Equal(TimeSpan.FromMinutes(90), h.Scheduler.Stats.ActiveTime); // 40 + 40 + 10
    }

    [Fact]
    public void Pause_blocks_reminders_and_accumulation()
    {
        var h = new Harness();

        h.StepMinutes(20);
        h.Scheduler.PauseFor(TimeSpan.FromHours(1));

        h.StepMinutes(50); // still inside the pause window
        Assert.Empty(h.Fired);
        Assert.True(h.Scheduler.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(20), h.Scheduler.Stats.ActiveTime);

        h.StepMinutes(15); // pause expires 10 min into this stretch: only 5 min counts
        Assert.False(h.Scheduler.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(30), h.Scheduler.Stats.ActiveTime);
    }

    [Fact]
    public void Snooze_re_fires_after_the_delay()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.StepMinutes(50); // sit fired once
        Assert.Single(h.Fired);

        h.Scheduler.Snooze(ReminderKind.Sit, minutes: 10);
        h.StepMinutes(9, stepMinutes: 3);
        Assert.Single(h.Fired); // not yet

        h.StepMinutes(2, stepMinutes: 1); // past the snooze window
        Assert.Equal(2, h.Fired.Count);
    }

    [Fact]
    public void Day_rollover_resets_stats()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);
        h.Time.Advance(TimeSpan.FromHours(14)); // start was 09:00, now 23:00
        h.StepMinutes(55); // 23:55, stats accumulated

        Assert.True(h.Scheduler.Stats.ActiveTime > TimeSpan.Zero);

        h.StepMinutes(10); // crossed midnight

        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.Stats.ActiveTime);
        Assert.Equal(0, h.Scheduler.Stats.SitReminders);
        Assert.Equal(0, h.Scheduler.Stats.WaterReminders);
    }

    [Fact]
    public void Huge_delta_is_clamped()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.Step(TimeSpan.FromHours(3)); // e.g. system slept; one big tick gets clamped

        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.Stats.ActiveTime);
        Assert.Empty(h.Fired); // 10 < 45, no reminder dumped after wake
    }

    [Fact]
    public void Null_idle_provider_degrades_to_natural_time()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);
        h.Idle.Idle = null; // platform without idle detection

        h.StepMinutes(50);

        Assert.Equal(ReminderKind.Sit, Assert.Single(h.Fired).Kind);
    }
}
