using System;
using System.Collections.Generic;

namespace MoveBit.Services;

/// <summary>
/// Fun-but-not-cringe copy pools. Reminders that say the exact same thing every time
/// get ignored; a random line earns a smile and one extra second of attention.
/// </summary>
internal static class BreakCopy
{
    // Full-screen break hints (the big line under the countdown).
    internal static readonly string[] BreakHints =
    [
        "站起来，晃晃肩膀，看看窗外",
        "代码不会跑，但你可以",
        "水是免费的，厕所也是，快去",
        "让你的血液重新认识一下下半身",
        "站着想想刚才的 bug，说不定就想通了",
        "久坐一时爽，腰间盘火葬场",
        "去接杯水，顺便数一数走廊的绿植",
        "给眼睛一个对焦到无穷远的机会",
        "伸个懒腰，你值得发出一声叹气",
        "离开椅子五分钟，思路会自己追上来",
    ];

    // Water toast lines.
    internal static readonly string[] WaterLines =
    [
        "你的身体 70% 是水，别让剩下 30% 替它扛",
        "喝口水，膀胱和皮肤都会谢谢你",
        "接水路上记得伸个懒腰",
        "水杯空了？这是它给你的信号",
    ];

    // Sit toast lines (used when forced break is disabled).
    internal static readonly string[] SitLines =
    [
        "去趟厕所，或者接杯水，或者就站着发会儿呆",
        "椅子不会想你，但你的腰会",
        "站起来甩甩手，屏幕的事五分钟后再说",
    ];

    private static readonly Random Rng = new();

    public static string Pick(IReadOnlyList<string> pool) => pool[Rng.Next(pool.Count)];
}
