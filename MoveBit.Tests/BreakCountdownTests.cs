using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public sealed class BreakCountdownTests
{
    [Fact]
    public void DisplaySeconds_rounds_up_until_break_has_really_finished()
    {
        Assert.Equal(60, BreakCountdown.DisplaySeconds(TimeSpan.FromSeconds(59.01)));
        Assert.Equal(1, BreakCountdown.DisplaySeconds(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(0, BreakCountdown.DisplaySeconds(TimeSpan.Zero));
    }

    [Fact]
    public void ProgressFraction_uses_the_same_remaining_time_as_the_text()
    {
        var total = TimeSpan.FromMinutes(5);
        var remaining = TimeSpan.FromMinutes(2.5);

        Assert.Equal(0.5, BreakCountdown.ProgressFraction(remaining, total), 10);
        Assert.Equal(150, BreakCountdown.DisplaySeconds(remaining));
    }

    [Fact]
    public void Remaining_clamps_elapsed_time_to_the_break_bounds()
    {
        var total = TimeSpan.FromMinutes(5);

        Assert.Equal(total, BreakCountdown.Remaining(total, TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromMinutes(4), BreakCountdown.Remaining(total, TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.Zero, BreakCountdown.Remaining(total, TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void ProgressFraction_clamps_to_zero_and_one()
    {
        var total = TimeSpan.FromMinutes(5);

        Assert.Equal(1, BreakCountdown.ProgressFraction(TimeSpan.FromMinutes(6), total));
        Assert.Equal(0, BreakCountdown.ProgressFraction(TimeSpan.Zero, total));
        Assert.Equal(0, BreakCountdown.ProgressFraction(TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }
}
