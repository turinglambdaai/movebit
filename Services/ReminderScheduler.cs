using System;
using MoveBit.Models;

namespace MoveBit.Services;

public enum ReminderKind
{
    Sit,
    Water,
    Micro,
}

public sealed record ReminderEvent(ReminderKind Kind, int CountToday, TimeSpan ActiveTimeToday);

/// <summary>
/// The core state machine. Three independent reminder cycles advance on <see cref="Tick"/>:
///   - Sit cycle: accumulates ACTIVE time only. Fires "stand up" reminders.
///   - Water cycle: same accumulation (only meaningful while the user is at the desk).
///   - Micro cycle: lightweight short-break nudges when enabled.
/// When the user goes idle beyond the away threshold, all cycles freeze; when they
/// come back the cycles reset — the break already happened, no nagging after it.
/// </summary>
public sealed class ReminderScheduler
{
    private static readonly TimeSpan MaxTickDelta = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly IIdleProvider _idle;

    private DateTimeOffset _lastTick;
    private bool _wasAway;
    private TimeSpan _sitAccum;
    private TimeSpan _waterAccum;
    private TimeSpan _microAccum;
    private DateTimeOffset? _pausedUntil;

    public event EventHandler<ReminderEvent>? ReminderFired;

    /// Raised on the away -> active transition (user just sat back down).
    public event EventHandler? UserReturned;

    /// Raised with the closing day's stats right before the daily reset (archive hook).
    public event EventHandler<DayStats>? DayCompleted;

    private readonly ReminderConfig _config;

    public ReminderConfig Config => _config;

    private DayStats _stats;

    public DayStats Stats => _stats;

    /// Active time accumulated in the current sit cycle (for UI progress display).
    public TimeSpan SitCycleElapsed => _sitAccum;

    public bool IsPaused => _pausedUntil is { } until && _time.GetLocalNow() < until;

    public DateTimeOffset? PausedUntil => IsPaused ? _pausedUntil : null;

    public ReminderScheduler(ReminderConfig config, TimeProvider time, IIdleProvider idle)
    {
        _config = config;
        _time = time;
        _idle = idle;
        _stats = new DayStats { Date = DateOnly.FromDateTime(_time.GetLocalNow().LocalDateTime) };
        _lastTick = _time.GetLocalNow();
    }

    private TimeSpan SitInterval => TimeSpan.FromMinutes(Config.SitReminderMinutes);

    private TimeSpan WaterInterval => TimeSpan.FromMinutes(Config.WaterReminderMinutes);

    private TimeSpan MicroInterval => TimeSpan.FromMinutes(Config.MicroBreakIntervalMinutes);

    private TimeSpan AwayThreshold => TimeSpan.FromMinutes(Config.AwayResetMinutes);

    /// <summary>Advance the state machine. Drive from a UI timer (e.g. every 30 s).</summary>
    public void Tick()
    {
        var now = _time.GetLocalNow();
        RollDayIfNeeded(now);

        if (_pausedUntil is { } until)
        {
            if (now < until)
            {
                _lastTick = now;
                return;
            }

            // The pause may expire between timer ticks. Count only the portion after
            // the exact pause deadline rather than charging the whole tick as work.
            if (_lastTick < until)
            {
                _lastTick = until;
            }

            _pausedUntil = null;
        }

        var delta = now - _lastTick;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero; // clock stepped backwards
        }

        if (delta > MaxTickDelta)
        {
            delta = MaxTickDelta; // system sleep / suspended VM clamps to one max step
        }

        _lastTick = now;

        var idle = _idle.GetIdleTime();
        var away = idle is { } t && t >= AwayThreshold;
        if (away)
        {
            _wasAway = true;
            return; // user already stepped away — nothing accumulates, nothing fires
        }

        if (_wasAway)
        {
            // Back from a real break: restart every cycle instead of dumping a stale reminder.
            _wasAway = false;
            ResetCycles();
            UserReturned?.Invoke(this, EventArgs.Empty);
        }

        _sitAccum += delta;
        _waterAccum += delta;
        _microAccum += delta;
        _stats.ActiveTime += delta;

        if (_sitAccum >= SitInterval)
        {
            _sitAccum = TimeSpan.Zero;
            _stats.SitReminders++;
            ReminderFired?.Invoke(this, new ReminderEvent(ReminderKind.Sit, _stats.SitReminders, _stats.ActiveTime));
        }

        if (_waterAccum >= WaterInterval)
        {
            _waterAccum = TimeSpan.Zero;
            _stats.WaterReminders++;
            ReminderFired?.Invoke(this, new ReminderEvent(ReminderKind.Water, _stats.WaterReminders, _stats.ActiveTime));
        }

        if (Config.MicroBreakEnabled && _microAccum >= MicroInterval)
        {
            _microAccum = TimeSpan.Zero;
            _stats.MicroBreaks++;
            ReminderFired?.Invoke(this, new ReminderEvent(ReminderKind.Micro, _stats.MicroBreaks, _stats.ActiveTime));
        }
    }

    /// <summary>Silence reminders for the given duration ("pause 1 hour" tray action).</summary>
    public void PauseFor(TimeSpan duration)
    {
        var now = _time.GetLocalNow();
        _pausedUntil = now + duration;
        _lastTick = now;
    }

    public void Resume()
    {
        _pausedUntil = null;
        _lastTick = _time.GetLocalNow();
    }

    /// <summary>
    /// Discard wall-clock time since the previous scheduler tick without changing any
    /// cycle progress. Used while a forced break owns the screen: resting is not work.
    /// </summary>
    public void DiscardElapsedSinceLastTick()
    {
        _lastTick = _time.GetLocalNow();
    }

    /// <summary>
    /// Record a completed real break. All reminder cycles restart and time spent on the
    /// break is discarded, matching the same semantics as returning from an away period.
    /// </summary>
    public void CompleteBreak()
    {
        ResetCycles();
        _lastTick = _time.GetLocalNow();
    }

    /// <summary>"Remind me later": push the cycle so it fires again in <paramref name="minutes"/>.</summary>
    public void Snooze(ReminderKind kind, int minutes)
    {
        var target = TimeSpan.FromMinutes(minutes);

        if (kind == ReminderKind.Sit)
        {
            _sitAccum = MaxOfZero(SitInterval - target);
        }
        else if (kind == ReminderKind.Micro)
        {
            _microAccum = MaxOfZero(MicroInterval - target);
        }
        else
        {
            _waterAccum = MaxOfZero(WaterInterval - target);
        }
    }

    private void ResetCycles()
    {
        _sitAccum = TimeSpan.Zero;
        _waterAccum = TimeSpan.Zero;
        _microAccum = TimeSpan.Zero;
    }

    private static TimeSpan MaxOfZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private void RollDayIfNeeded(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (_stats.Date != today)
        {
            DayCompleted?.Invoke(this, _stats); // let the app archive the closing day first
            _stats = new DayStats { Date = today };
        }
    }
}
