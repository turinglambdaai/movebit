using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveBit;

public partial class MainWindow : Window
{
    private const double DefaultWidth = 520;
    private const double DefaultHeight = 900;
    private const double MinimumWidth = 460;
    private const double PreferredMinimumHeight = 560;
    private const double AbsoluteMinimumHeight = 320;
    private const double MaximumComfortableWidth = 720;
    private const double ScreenMargin = 24;

    public MainWindow()
    {
        InitializeComponent();

        // Keep MoveBit compact by default, but let users make better use of larger displays.
        // Never cap the native window itself: Windows can otherwise maximize the title bar
        // while Avalonia's client area remains at a smaller MaxWidth/MaxHeight.
        CanResize = true;
        Width = DefaultWidth;
        Height = DefaultHeight;
        MinWidth = MinimumWidth;
        MinHeight = AbsoluteMinimumHeight;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;

        // Re-evaluate normal-window sizing when the window moves between displays or DPI changes.
        PositionChanged += (_, _) => FitToCurrentScreen();
        ScalingChanged += (_, _) => FitToCurrentScreen();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == WindowStateProperty && WindowState == WindowState.Normal)
            {
                FitToCurrentScreen();
            }
        };
    }

    /// True while the app is running: closing hides to tray. Set false only on real shutdown.
    public bool HideOnClose { get; set; } = true;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToCurrentScreen();
    }

    private void FitToCurrentScreen()
    {
        // A maximized/full-screen window belongs to the window manager. Only normal windows
        // get our compact-product sizing so chrome and client area always share the same bounds.
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            return;
        }

        var scaling = Math.Max(screen.Scaling, 0.1);
        var workingArea = screen.WorkingArea;

        // WorkingArea is reported in physical pixels while Window dimensions use DIPs.
        // Leave breathing room around the native window chrome and taskbar/dock.
        var availableWidth = Math.Max(MinimumWidth, workingArea.Width / scaling - ScreenMargin * 2);
        var availableHeight = Math.Max(AbsoluteMinimumHeight, workingArea.Height / scaling - ScreenMargin * 2);
        var comfortableWidth = Math.Min(MaximumComfortableWidth, availableWidth);

        // Preserve the roomy normal minimum where possible, but allow a shorter viewport on
        // high-DPI/small displays so the ScrollViewer can keep every setting reachable.
        MinHeight = Math.Min(PreferredMinimumHeight, availableHeight);

        if (Width > comfortableWidth)
        {
            Width = comfortableWidth;
        }

        if (Height > availableHeight)
        {
            Height = availableHeight;
        }
    }

    private void OnHistoryWeekClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.ShowHistoryRange(7);
        }
    }

    private void OnHistoryMonthClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.ShowHistoryRange(30);
        }
    }

    private async void OnUpdateClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            await app.CheckOrInstallUpdateFromUiAsync();
        }
    }
}
