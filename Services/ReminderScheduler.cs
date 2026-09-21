using System;
using MoveBit.Models;

namespace MoveBit.Services;

public enum ReminderKind
{
    Sit,
    Water,
}

public sealed record ReminderEvent(ReminderKind Kind, int CountToday, TimeSpan ActiveTimeToday);

/// <summary>
/// The core state machine. Two independent reminder cycles advance on <see cref="Tick"/>:
///   - Sit cycle: accumulates ACTIVE time only. Fires "stand up" reminders.
///   - Water cycle: same accumulation (only meaningful while the user is at the desk).
/// When the user goes idle beyond the away threshold, both cycles freeze; when they
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
    private DateTimeOffset? _pausedUntil;

    public event EventHandler<ReminderEvent>? ReminderFired;

    /// Raised on the away -> active transition (user just sat back down).
    public event EventHandler? UserReturned;

    public ReminderConfig Config { get; }

    public DayStats Stats { get; }

    /// Active time accumulated in the current sit cycle (for UI progress display).
    public TimeSpan SitCycleElapsed => _sitAccum;

    public bool IsPaused => _pausedUntil is { } until && _time.GetLocalNow() < until;

    public DateTimeOffset? PausedUntil => IsPaused ? _pausedUntil : null;

    public ReminderScheduler(ReminderConfig config, TimeProvider time, IIdleProvider idle)
    {
        Config = config;
        _time = time;
        _idle = idle;
        Stats = new DayStats { Date = DateOnly.FromDateTime(_time.GetLocalNow().LocalDateTime) };
        _lastTick = _time.GetLocalNow();
    }

    private TimeSpan SitInterval => TimeSpan.FromMinutes(Config.SitReminderMinutes);

    private TimeSpan WaterInterval => TimeSpan.FromMinutes(Config.WaterReminderMinutes);

    private TimeSpan AwayThreshold => TimeSpan.FromMinutes(Config.AwayResetMinutes);

    /// <summary>Advance the state machine. Drive from a UI timer (e.g. every 30 s).</summary>
    public void Tick()
    {
        var now = _time.GetLocalNow();
        RollDayIfNeeded(now);

        if (IsPaused)
        {
            _lastTick = now;
            return;
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
            // Back from a real break: restart both cycles instead of dumping a stale reminder.
            _wasAway = false;
            _sitAccum = TimeSpan.Zero;
            _waterAccum = TimeSpan.Zero;
            UserReturned?.Invoke(this, EventArgs.Empty);
        }

        _sitAccum += delta;
        _waterAccum += delta;
        Stats.ActiveTime += delta;

        if (_sitAccum >= SitInterval)
        {
            _sitAccum = TimeSpan.Zero;
            Stats.SitReminders++;
            ReminderFired?.Invoke(this, new ReminderEvent(ReminderKind.Sit, Stats.SitReminders, Stats.ActiveTime));
        }

        if (_waterAccum >= WaterInterval)
        {
            _waterAccum = TimeSpan.Zero;
            Stats.WaterReminders++;
            ReminderFired?.Invoke(this, new ReminderEvent(ReminderKind.Water, Stats.WaterReminders, Stats.ActiveTime));
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

    /// <summary>"Remind me later": push the cycle so it fires again in <paramref name="minutes"/>.</summary>
    public void Snooze(ReminderKind kind, int minutes)
    {
        var target = TimeSpan.FromMinutes(minutes);
        if (kind == ReminderKind.Sit)
        {
            _sitAccum = SitInterval - target;
            if (_sitAccum < TimeSpan.Zero)
            {
                _sitAccum = TimeSpan.Zero;
            }
        }
        else
        {
            _waterAccum = WaterInterval - target;
            if (_waterAccum < TimeSpan.Zero)
            {
                _waterAccum = TimeSpan.Zero;
            }
        }
    }

    private void RollDayIfNeeded(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (Stats.Date != today)
        {
            Stats.Date = today;
            Stats.ActiveTime = TimeSpan.Zero;
            Stats.SitReminders = 0;
            Stats.WaterReminders = 0;
        }
    }
}
