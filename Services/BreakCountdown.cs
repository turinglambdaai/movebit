using System;

namespace MoveBit.Services;

/// <summary>
/// Pure countdown math shared by the forced-break clock and every overlay.
/// Keeping display seconds and ring progress derived from the same remaining value
/// prevents the text and visual progress from drifting apart.
/// </summary>
public static class BreakCountdown
{
    public static TimeSpan Remaining(TimeSpan total, TimeSpan elapsed)
    {
        if (total <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var remaining = total - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining > total ? total : remaining;
    }

    public static int DisplaySeconds(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(remaining.TotalSeconds);
    }

    public static double ProgressFraction(TimeSpan remaining, TimeSpan total)
    {
        if (total <= TimeSpan.Zero || remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp(remaining.TotalSeconds / total.TotalSeconds, 0, 1);
    }
}
