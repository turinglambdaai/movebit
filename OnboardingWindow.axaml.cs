using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using MoveBit.Models;

namespace MoveBit;

/// <summary>
/// Four-step first-run onboarding. The user picks a strictness pact with the droplet
/// rather than silently accepting defaults — for a tool that locks every screen,
/// consent gathered up front is the product, not a courtesy.
/// </summary>
public partial class OnboardingWindow : Window
{
    private const int LastPage = 3;

    private readonly ReminderConfig _config;
    private int _page;
    private bool _finished;
    private string _mode = "standard";

    /// Raised exactly once when the user finishes or skips; the config object already
    /// holds the chosen values, the caller persists it.
    public event Action? Finished;

    public OnboardingWindow(ReminderConfig config)
    {
        _config = config;
        InitializeComponent();

        AttachMode(ModeGentle, "gentle");
        AttachMode(ModeStandard, "standard");
        AttachMode(ModeStrict, "strict");

        AttachWater(Water30, 30);
        AttachWater(Water45, 45);
        AttachWater(Water60, 60);

        ApplyMode("standard");
        ApplyWater(30);

        SoundToggle.IsChecked = _config.SoundEnabled;
        SoundToggle.IsCheckedChanged += (_, _) => _config.SoundEnabled = SoundToggle.IsChecked == true;

        ShowPage(0);
    }

    private void AttachMode(Border card, string mode) => card.PointerPressed += (_, _) => ApplyMode(mode);

    private void AttachWater(Border card, int minutes) => card.PointerPressed += (_, _) => ApplyWater(minutes);

    private void ApplyMode(string mode)
    {
        _mode = mode;

        // Write through immediately: closing the window at any point must leave the
        // config consistent with what the user saw selected.
        switch (mode)
        {
            case "gentle":
                _config.SitReminderMinutes = 60;
                _config.ForceBreakEnabled = false;
                _config.BreakDurationMinutes = 3;
                break;
            case "strict":
                _config.SitReminderMinutes = 30;
                _config.ForceBreakEnabled = true;
                _config.BreakDurationMinutes = 5;
                break;
            default:
                _config.SitReminderMinutes = 45;
                _config.ForceBreakEnabled = true;
                _config.BreakDurationMinutes = 5;
                break;
        }

        SetSelected(ModeGentle, mode == "gentle");
        SetSelected(ModeStandard, mode == "standard");
        SetSelected(ModeStrict, mode == "strict");
    }

    private void ApplyWater(int minutes)
    {
        _config.WaterReminderMinutes = minutes;
        SetSelected(Water30, minutes == 30);
        SetSelected(Water45, minutes == 45);
        SetSelected(Water60, minutes == 60);
    }

    private static void SetSelected(Border card, bool selected)
    {
        if (selected)
        {
            card.Classes.Add("selected");
        }
        else
        {
            card.Classes.Remove("selected");
        }
    }

    private void ShowPage(int page)
    {
        _page = page;
        Page0.IsVisible = page == 0;
        Page1.IsVisible = page == 1;
        Page2.IsVisible = page == 2;
        Page3.IsVisible = page == 3;

        SetActive(Dot0, page == 0);
        SetActive(Dot1, page == 1);
        SetActive(Dot2, page == 2);
        SetActive(Dot3, page == 3);

        NextButton.Content = page == 0 ? "开始" : page == LastPage ? "开始使用" : "下一步";
        SkipButton.IsVisible = page != LastPage;

        if (page == LastPage)
        {
            SummaryText.Text = BuildSummary();
        }
    }

    private static void SetActive(Ellipse dot, bool active)
    {
        if (active)
        {
            dot.Classes.Add("active");
        }
        else
        {
            dot.Classes.Remove("active");
        }
    }

    private string BuildSummary()
    {
        var sit = _config.ForceBreakEnabled
            ? $"连续工作 {_config.SitReminderMinutes} 分钟 → 锁定全部屏幕，休息 {_config.BreakDurationMinutes} 分钟"
            : $"每 {_config.SitReminderMinutes} 分钟弹窗提醒起身";

        return $"{sit}\n每 {_config.WaterReminderMinutes} 分钟提醒喝水"
            + $"\n离开电脑超过 {_config.AwayResetMinutes} 分钟，计时自动重置"
            + $"\n提示音：{(_config.SoundEnabled ? "开" : "关")}（全屏休息永远静默）";
    }

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_page >= LastPage)
        {
            Finish();
            return;
        }

        ShowPage(_page + 1);
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        // Skip = plain defaults, as if the user never touched anything.
        ApplyMode("standard");
        ApplyWater(30);
        _config.SoundEnabled = true;
        Finish();
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke();
        Close();
    }
}
