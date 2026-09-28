using System;
using System.Collections.Generic;

namespace MoveBit.Services;

/// <summary>
/// Copy pools in the droplet's first-person voice. Reminders that say the exact same
/// thing every time get ignored; a persona with rotating lines earns a smile and one
/// extra second of attention. The highest-frequency pools are the largest, and after
/// 22:00 the droplet drops the jokes and keeps it soft.
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

    // Water toast lines. Shown every ~30 minutes, so this pool is the largest.
    internal static readonly string[] WaterLines =
    [
        "我是水滴，喝口水，咱们就算团聚了",
        "你的杯子说它好空虚",
        "水分充足的人，debug 都快三分（大概）",
        "干了这杯白开水，不为别的，为你的肾",
        "看完这行字去喝一口，就一口",
        "咖啡因是借的水，迟早要还的",
        "嗓子有点干了吧？我掐指一算的",
        "水喝够了，思路才不容易打结",
    ];

    // Sit toast lines (used when forced break is disabled).
    internal static readonly string[] SitLines =
    [
        "椅子不会想你，但你的腰会——水滴敬上",
        "起来晃两分钟，屏幕我帮你看着",
        "站起来，让血液重新认识一下下半身",
        "屏幕不会跑，腿会锈——去动动",
        "你坐着的姿势，我看着都替你腰疼",
        "去接杯水顺便散两步，一举两得（我批准了）",
    ];

    // Micro break lines: short, one glance readable, no lock needed.
    internal static readonly string[] MicroLines =
    [
        "站 20 秒，看看远处 👀",
        "眼睛离开屏幕，找窗外最远的东西",
        "站起来，肩膀向后绕两圈",
        "看一眼 6 米外的任何东西，20 秒就够",
        "掌心捂眼十秒，让眼睛歇口气",
        "脖子写个「米」字，写慢一点",
        "原地踏步十秒，假装自己在赶路",
        "蹲下再站起五次，比咖啡因管用",
        "去走廊走个来回，顺便看看谁在摸鱼",
    ];

    // After 22:00 the same jokes get tired; the droplet switches to a quieter voice.
    internal static readonly string[] LateNightWaterLines =
    [
        "都这个点了，水还是要喝的",
        "深夜赶工也要润嗓子，我轻点说",
        "夜再深，杯子也别空着",
    ];

    internal static readonly string[] LateNightMicroLines =
    [
        "夜深了，站 20 秒就好，我小声说",
        "眼睛也要下夜班，看会儿远处吧",
        "轻一点，站起来 20 秒，别吵醒开着的标签页",
    ];

    // English pools carry the same droplet persona: warm, a little cheeky, never preachy.
    internal static readonly string[] EnBreakHints =
    [
        "I'll watch the screen — you go drink that water",
        "Bathroom break. I'd keep your seat warm… fine, just go",
        "As a certified water droplet, I declare this hydration time",
        "Stand up and roll your neck — I'll be right here waiting",
        "The light outside is gentler than the screen. Go have a look",
        "Your back needs this more than you think it does",
        "Go get some water — say hi to my relatives",
        "Think about that bug standing up. Maybe it cracks first",
        "Three deep breaths before you decide to sit back down",
        "Your hips are on break for five minutes. The work isn't going anywhere",
    ];

    internal static readonly string[] EnWaterLines =
    [
        "I'm Droplet — take a sip and we're reunited",
        "Your glass says it's feeling a little empty",
        "Hydrated people debug 30% faster. (Allegedly.)",
        "Bottoms up — your kidneys will thank you",
        "Finish this line, then take one sip. Just one",
        "Coffee is borrowed water. Time to pay it back",
        "Throat a little dry? I had a feeling",
        "Water first. The tangled thoughts come apart easier after",
    ];

    internal static readonly string[] EnLateNightWaterLines =
    [
        "It's late — water still counts",
        "Working late? I'll keep it down: one sip",
        "However deep the night, don't leave the glass empty",
    ];

    internal static readonly string[] EnSitLines =
    [
        "The chair won't miss you, but your back will — Droplet",
        "Sway for two minutes; I'll mind the screen",
        "Stand up and let your blood rediscover your legs",
        "The screen isn't going anywhere. Your legs are rusting — move",
        "The way you're sitting hurts just from watching",
        "Refill your water and take a stroll — two birds, one stone (I approve)",
    ];

    internal static readonly string[] EnMicroLines =
    [
        "Stand for 20 seconds. Look far away 👀",
        "Eyes off the screen — find the farthest thing you can see",
        "Stand up and roll those shoulders back, twice",
        "Anything six meters away, for 20 seconds. That's it",
        "Palms over your eyes for ten seconds. Let them breathe",
        "Spell your name with your nose. Slowly",
        "March in place for ten seconds",
        "Five squats — better than caffeine",
        "Walk to the end of the hall. See who else is slacking",
    ];

    internal static readonly string[] EnLateNightMicroLines =
    [
        "It's late. Twenty seconds on your feet — I'll whisper",
        "Your eyes want off duty too. Gaze far for a moment",
        "Quietly now: stand for 20 seconds. Don't wake the open tabs",
    ];

    // Milestone celebration counts (completed sit breaks per day).
    internal static readonly int[] CelebrateAt = [3, 5, 8];

    private static readonly Random Rng = new();

    public static string Pick(IReadOnlyList<string> pool) => pool[Rng.Next(pool.Count)];

    private static bool IsLateNight => DateTime.Now.Hour is >= 22 or < 6;

    public static string PickHint() => Pick(L10n.IsEnglish ? EnBreakHints : BreakHints);

    public static string PickSit() => Pick(L10n.IsEnglish ? EnSitLines : SitLines);

    public static string PickWater() => L10n.IsEnglish
        ? Pick(IsLateNight ? EnLateNightWaterLines : EnWaterLines)
        : Pick(IsLateNight ? LateNightWaterLines : WaterLines);

    public static string PickMicro() => L10n.IsEnglish
        ? Pick(IsLateNight ? EnLateNightMicroLines : EnMicroLines)
        : Pick(IsLateNight ? LateNightMicroLines : MicroLines);

    public static bool ShouldCelebrate(int completedToday) =>
        Array.IndexOf(CelebrateAt, completedToday) >= 0;
}
