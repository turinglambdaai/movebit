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
            MicroBreakEnabled = false, // tests opt in explicitly; keeps legacy cases clean
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
        Assert.Empty(h.Fired);

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

        h.StepMinutes(40);

        h.Idle.Idle = TimeSpan.FromMinutes(10);
        h.StepMinutes(20);
        Assert.Empty(h.Fired);
        Assert.Equal(TimeSpan.FromMinutes(40), h.Scheduler.Stats.ActiveTime);

        h.Idle.Idle = TimeSpan.Zero;
        h.StepMinutes(40);
        Assert.Empty(h.Fired);

        h.StepMinutes(10);
        Assert.Equal(ReminderKind.Sit, Assert.Single(h.Fired).Kind);
        Assert.Equal(TimeSpan.FromMinutes(90), h.Scheduler.Stats.ActiveTime);
    }

    [Fact]
    public void Pause_blocks_reminders_and_counts_only_time_after_exact_expiry()
    {
        var h = new Harness();

        h.StepMinutes(20);
        h.Scheduler.PauseFor(TimeSpan.FromHours(1));

        h.StepMinutes(50);
        Assert.Empty(h.Fired);
        Assert.True(h.Scheduler.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(20), h.Scheduler.Stats.ActiveTime);

        h.StepMinutes(15); // pause expires 10 minutes into this interval: only final 5 minutes count
        Assert.False(h.Scheduler.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(25), h.Scheduler.Stats.ActiveTime);
    }

    [Fact]
    public void Snooze_re_fires_after_the_delay()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.StepMinutes(50);
        Assert.Single(h.Fired);

        h.Scheduler.Snooze(ReminderKind.Sit, minutes: 10);
        h.StepMinutes(9, stepMinutes: 3);
        Assert.Single(h.Fired);

        h.StepMinutes(2, stepMinutes: 1);
        Assert.Equal(2, h.Fired.Count);
    }

    [Fact]
    public void Day_rollover_resets_stats()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);
        h.Time.Advance(TimeSpan.FromHours(14));
        h.StepMinutes(55);

        Assert.True(h.Scheduler.Stats.ActiveTime > TimeSpan.Zero);

        h.StepMinutes(10);

        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.Stats.ActiveTime);
        Assert.Equal(0, h.Scheduler.Stats.SitReminders);
        Assert.Equal(0, h.Scheduler.Stats.WaterReminders);
    }

    [Fact]
    public void Huge_delta_is_clamped()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);

        h.Step(TimeSpan.FromHours(3));

        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.Stats.ActiveTime);
        Assert.Empty(h.Fired);
    }

    [Fact]
    public void Null_idle_provider_degrades_to_natural_time()
    {
        var h = new Harness(sitMinutes: 45, waterMinutes: 300);
        h.Idle.Idle = null;

        h.StepMinutes(50);

        Assert.Equal(ReminderKind.Sit, Assert.Single(h.Fired).Kind);
    }

    [Fact]
    public void Micro_break_fires_on_its_own_cycle_and_survives_long_breaks()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);
        h.Scheduler.Config.MicroBreakEnabled = true;
        h.Scheduler.Config.MicroBreakIntervalMinutes = 30;

        h.StepMinutes(35);

        var micro = Assert.Single(h.Fired);
        Assert.Equal(ReminderKind.Micro, micro.Kind);
        Assert.Equal(1, h.Scheduler.Stats.MicroBreaks);
    }

    [Fact]
    public void Micro_break_disabled_never_fires()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);
        h.Scheduler.Config.MicroBreakEnabled = false;

        h.StepMinutes(120);

        Assert.Empty(h.Fired);
        Assert.Equal(0, h.Scheduler.Stats.MicroBreaks);
    }

    [Fact]
    public void Away_return_resets_micro_cycle_too()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);
        h.Scheduler.Config.MicroBreakEnabled = true;
        h.Scheduler.Config.MicroBreakIntervalMinutes = 30;

        h.StepMinutes(20);
        h.Idle.Idle = TimeSpan.FromMinutes(10);
        h.StepMinutes(20);
        h.Idle.Idle = TimeSpan.Zero;
        h.StepMinutes(20);

        Assert.Empty(h.Fired);

        h.StepMinutes(11);
        Assert.Equal(ReminderKind.Micro, Assert.Single(h.Fired).Kind);
    }

    [Fact]
    public void Day_rollover_archives_the_closing_day_and_resets()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);
        h.Time.Advance(TimeSpan.FromHours(14));
        h.Scheduler.Tick();
        h.StepMinutes(55);

        DayStats? archived = null;
        h.Scheduler.DayCompleted += (_, stats) => archived = stats;

        h.StepMinutes(10);

        Assert.NotNull(archived);
        Assert.Equal(TimeSpan.FromMinutes(65), archived!.ActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(10), h.Scheduler.Stats.ActiveTime);
    }

    [Fact]
    public void Forced_break_elapsed_time_can_be_discarded_without_losing_cycle_progress()
    {
        var h = new Harness(sitMinutes: 60, waterMinutes: 300);

        h.StepMinutes(20);
        h.Time.Advance(TimeSpan.FromMinutes(30)); // simulated time under the break overlay
        h.Scheduler.DiscardElapsedSinceLastTick();
        h.StepMinutes(20);

        Assert.Equal(TimeSpan.FromMinutes(40), h.Scheduler.Stats.ActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(40), h.Scheduler.SitCycleElapsed);
        Assert.Empty(h.Fired);
    }

    [Fact]
    public void Completed_break_restarts_all_reminder_cycles()
    {
        var h = new Harness(sitMinutes: 300, waterMinutes: 300);
        h.Scheduler.Config.MicroBreakEnabled = true;
        h.Scheduler.Config.MicroBreakIntervalMinutes = 30;

        h.StepMinutes(25);
        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Scheduler.CompleteBreak();

        h.StepMinutes(29);
        Assert.Empty(h.Fired);

        h.StepMinutes(2, stepMinutes: 1);
        Assert.Equal(ReminderKind.Micro, Assert.Single(h.Fired).Kind);
        Assert.Equal(TimeSpan.FromMinutes(56), h.Scheduler.Stats.ActiveTime); // 25 + 29 + 2; break excluded
    }
}
