using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MoveBit.Services;

namespace MoveBit;

/// Borderless bottom-right toast. Non-activating (never steals focus), auto-closes
/// after a few seconds unless the pointer is hovering over it.
public partial class NotificationWindow : Window
{
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(9);

    private DispatcherTimer? _autoClose;

    public event EventHandler? Acknowledged;

    public event EventHandler<ReminderKind>? Snoozed;

    public ReminderKind ReminderKind { get; init; }

    public string NotificationTitle { get; init; } = "";

    public string NotificationBody { get; init; } = "";

    public NotificationWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        TitleText.Text = NotificationTitle;
        BodyText.Text = NotificationBody;

        // Color-code by kind: orange = move, blue = water.
        AccentBar.Background = ReminderKind == ReminderKind.Water
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3E7EC2"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#EA580C"));

        PositionAtBottomRight();
        RestartAutoClose();

        PointerEntered += (_, _) => RestartAutoClose();
        PointerExited += (_, _) => RestartAutoClose();
    }

    private void PositionAtBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var wa = screen.WorkingArea;
        var scale = RenderScaling;
        Position = new PixelPoint(
            wa.X + wa.Width - (int)(Bounds.Width * scale) - 16,
            wa.Y + wa.Height - (int)(Bounds.Height * scale) - 16);
    }

    private void RestartAutoClose()
    {
        _autoClose?.Stop();
        _autoClose = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();
    }

    private void OnAcknowledge(object? sender, RoutedEventArgs e)
    {
        Acknowledged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnSnooze(object? sender, RoutedEventArgs e)
    {
        Snoozed?.Invoke(this, ReminderKind);
        Close();
    }
}
