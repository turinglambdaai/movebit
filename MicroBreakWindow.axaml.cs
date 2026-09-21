using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MoveBit;

/// <summary>
/// Screen-center micro-break nudge. Auto-dismisses after the configured duration;
/// click / Esc closes it early. Never locks, never steals focus, never blocks —
/// the evidence-friendly layer between long forced breaks.
/// </summary>
public partial class MicroBreakWindow : Window
{
    private readonly TimeSpan _duration;
    private DispatcherTimer? _timer;

    public MicroBreakWindow(string title, TimeSpan duration)
    {
        _duration = duration;
        InitializeComponent();
        TitleText.Text = title;

        Card.PointerPressed += (_, _) => Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Park it near the upper third of the primary screen — in the sight line,
        // so it actually pulls eyes off the code.
        if (Screens.Primary is { } screen)
        {
            var wa = screen.WorkingArea;
            var scale = RenderScaling;
            Position = new PixelPoint(
                wa.X + (wa.Width - (int)(Width * scale)) / 2,
                wa.Y + (int)(wa.Height * 0.28));
        }

        var remaining = _duration;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.25) };
        _timer.Tick += (_, _) =>
        {
            remaining -= TimeSpan.FromSeconds(0.25);
            ShrinkingBar.Value = Math.Clamp(remaining.TotalSeconds / _duration.TotalSeconds * 100, 0, 100);
            if (remaining <= TimeSpan.Zero)
            {
                _timer.Stop();
                Close();
            }
        };
        _timer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
