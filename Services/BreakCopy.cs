using System;
using System.Collections.Generic;

namespace MoveBit.Services;

/// <summary>
/// Copy pools in the droplet's first-person voice. Reminders that say the exact same
/// thing every time get ignored; a persona with rotating lines earns a smile and one
/// extra second of attention.
/// </summary>
internal static class BreakCopy
{
    // Full-screen break hints (the big line under the countdown), spoken by the droplet.
    internal static readonly string[] BreakHints =
    [
        "我帮你盯着屏幕，你负责把水喝掉",
        "快去厕所，我替你把座位捂热……算了，你快去",
        "作为一颗专业水滴，我宣布现在是补水时间",
        "站起来转转脖子，我就在这等你回来",
        "窗外的光比屏幕的光温柔，去看一眼",
        "你的腰比你以为的更需要这次休息",
        "去接杯水吧，就当替我看看亲戚",
        "站着想想刚才的 bug，说不定它先想通了",
        "深呼吸三次，再决定要不要坐下",
        "屁股放假五分钟，效率不会跑的",
    ];

    // Water toast lines.
    internal static readonly string[] WaterLines =
    [
        "我是水滴，喝口水，咱们就算团聚了",
        "你的杯子说它好空虚",
        "水分充足的人，debug 都快三分（大概）",
        "干了这杯白开水，不为别的，为你的肾",
    ];

    // Sit toast lines (used when forced break is disabled).
    internal static readonly string[] SitLines =
    [
        "椅子不会想你，但你的腰会——水滴敬上",
        "起来晃两分钟，屏幕我帮你看着",
        "站起来，让血液重新认识一下下半身",
    ];

    // Micro break lines: short, one glance readable, no lock needed.
    internal static readonly string[] MicroLines =
    [
        "站 20 秒，看看远处 👀",
        "眼睛离开屏幕，找窗外最远的东西",
        "站起来，肩膀向后绕两圈",
        "看一眼 6 米外的任何东西，20 秒就够",
        "掌心捂眼十秒，让眼睛歇口气",
    ];

    // Milestone celebration counts (completed sit breaks per day).
    internal static readonly int[] CelebrateAt = [3, 5, 8];

    private static readonly Random Rng = new();

    public static string Pick(IReadOnlyList<string> pool) => pool[Rng.Next(pool.Count)];

    public static bool ShouldCelebrate(int completedToday) =>
        Array.IndexOf(CelebrateAt, completedToday) >= 0;
}
