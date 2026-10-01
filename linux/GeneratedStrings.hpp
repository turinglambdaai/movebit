// Generated from shared/i18n/{zh,en}.json — the single i18n source.
// Do not edit; run: node scripts/gen-cpp-strings.mjs
#ifndef MOVEBIT_GENERATED_STRINGS_H
#define MOVEBIT_GENERATED_STRINGS_H

#include <cstdint>
#include <map>
#include <random>
#include <string>
#include <vector>

namespace l10n {

// The active language ("zh" or "en"); hosts set it from the resolved config.
inline std::string g_language = "zh";

inline const std::map<std::string, std::string>& zh_table() {
  static const std::map<std::string, std::string> t = {
    {"TodayTitle", "今日活跃"},
    {"TodaySitCycle", "本轮久坐进度"},
    {"TodaySit", "久坐提醒"},
    {"TodayWater", "喝水提醒"},
    {"TodayMicro", "微休息"},
    {"HistToggle7", "7 天"},
    {"HistToggle30", "30 天"},
    {"HistRange7", "最近 7 天"},
    {"HistRange30", "最近 30 天"},
    {"HistTotalLong", "合计 {0} 小时 {1} 分钟"},
    {"HistTotalShort", "合计 {0} 分钟"},
    {"HistAvg", "日均 {0}"},
    {"HistToday", "今天"},
    {"HistTooltip", "{0:yyyy-MM-dd} · 活跃 {1} 分钟 · 久坐提醒 {2} 次 · 最长连续 {3} 分钟"},
    {"InsightTitle", "工作模式洞察"},
    {"InsightCurrent", "当前连续工作"},
    {"InsightTodayLongest", "今日最长连续"},
    {"InsightRecentLongest", "近 30 天最长连续"},
    {"InsightRecentAvg", "近 30 天活跃日均"},
    {"InsightBusiest", "最活跃日"},
    {"InsightNone", "暂无"},
    {"InsightNoData", "暂无足够数据"},
    {"InsightLong2", "最长连续工作达到 {0}，建议更早离开座位。"},
    {"InsightLong1", "最长连续工作 {0}，已达到一次久坐提醒周期。"},
    {"InsightLong0", "最长连续工作 {0}，目前低于久坐提醒周期。"},
    {"SetTitle", "提醒设置"},
    {"SetIntro", "可直接点击数字输入；按 Enter 或点到别处后立即生效。上下按钮已按常用步长调整。"},
    {"SetSitInterval", "久坐提醒间隔（分钟）"},
    {"SetWaterInterval", "喝水提醒间隔（分钟）"},
    {"SetAwayReset", "离开多久算休息（分钟）"},
    {"SetForce", "强制休息"},
    {"SetBreakDuration", "强制休息时长（分钟）"},
    {"SetSkipDelay", "「跳过」按钮出现延迟（秒）"},
    {"SetSnooze", "稍后再次提醒（分钟）"},
    {"SetMicroToggle", "微休息：每 X 分钟站 Y 秒"},
    {"SetMicroParams", "微休息间隔 / 时长（分·秒）"},
    {"SetSound", "提示音"},
    {"SetAutostart", "开机自启"},
    {"SetLanguage", "语言"},
    {"SetStatusDefault", "可直接输入数字 · 修改后自动保存"},
    {"SetStatusSaved", "✓ 已保存 · {0:HH:mm:ss}"},
    {"SetStatusApplied", "✓ 已应用 · {0:HH:mm:ss}"},
    {"SetStatusSaveFailed", "⚠ 已在本次运行中生效，但写入配置文件失败"},
    {"LangAuto", "跟随系统"},
    {"AboutAutoUpdate", "自动检查更新"},
    {"AboutAutoUpdateHint", "启动后检查，此后约每 6 小时检查一次；不会静默安装。"},
    {"UpdCurrentVersion", "当前版本 v{0}"},
    {"UpdChecking", "正在检查 GitHub Releases…"},
    {"UpdCheckingShort", "检查中…"},
    {"UpdFound", "发现新版本 {0}。已验证发布资产存在，点击后下载、校验并重启。"},
    {"UpdLatest", "已是最新版本 · v{0}"},
    {"UpdCurrentUpToDate", "当前版本 v{0} · 已是最新"},
    {"UpdUnsupported", "当前 CPU / 系统组合暂无官方在线更新包。"},
    {"UpdCheckFailed", "检查更新失败，请稍后重试。"},
    {"UpdCheckCancelled", "检查更新已取消。"},
    {"UpdPreparing", "准备更新到 {0}…"},
    {"UpdDownloadingPct", "正在下载 {0}… {1}%"},
    {"UpdDownloading", "正在下载 {0}…"},
    {"UpdVerifying", "正在校验 SHA-256…"},
    {"UpdStaging", "校验通过，正在准备替换文件…"},
    {"UpdRestartPrep", "准备重启到新版本…"},
    {"UpdGeneric", "正在更新…"},
    {"UpdRestarting", "更新已校验并准备完成，MoveBit 正在重启…"},
    {"UpdNotWritable", "应用目录不可写，无法自动更新。"},
    {"UpdFailed", "更新失败，当前版本未被替换。"},
    {"UpdCancelled", "更新已取消，当前版本保持不变。"},
    {"UpdActionUpdate", "更新并重启"},
    {"UpdActionRetry", "重试"},
    {"UpdActionUpdating", "更新中…"},
    {"MainConfigPath", "配置文件：{0}"},
    {"SyncTitle", "同步"},
    {"TrayOpen", "设置与统计"},
    {"TrayPause", "暂停提醒 1 小时"},
    {"TrayResume", "恢复提醒"},
    {"TrayCheckUpdate", "检查更新"},
    {"TrayUpdateTo", "更新并重启 {0}"},
    {"TrayExit", "退出 MoveBit"},
    {"TrayRunning", "MoveBit · {0}今日活跃 {1} · 久坐 {2}/{3} 分钟"},
    {"TrayPaused", "MoveBit · {0}已暂停至 {1:HH:mm}"},
    {"TrayUpdatePrefix", "有更新 {0} · "},
    {"NotifWindowTitle", "MoveBit 提醒"},
    {"NotifSitTitle", "该起身动一动了 🚶"},
    {"NotifSitBody", "已连续工作 {0}，今天第 {1} 次提醒。"},
    {"NotifWaterTitle", "喝口水吧 💧"},
    {"NotifWaterCount", "今天第 {0} 次提醒。"},
    {"NotifGotIt", "知道了"},
    {"NotifSnooze", "稍后 {0} 分钟"},
    {"NotifCheerTitle", "水滴为你鼓掌 💧"},
    {"NotifCheerBody", "今天第 {0} 次久坐休息，你的身体谢谢你。水滴帮你记着：今天已专注 {1}。"},
    {"NotifSitFallback", "已连续工作过久，请离开椅子休息。"},
    {"TplPausedUntil", "已暂停至 {0:HH:mm}"},
    {"TplRunning", "运行中"},
    {"TplSitCycleMin", "{0} / {1} 分钟"},
    {"TplOn", "开"},
    {"TplOff", "关"},
    {"AppHourMin", "{0} 小时 {1} 分钟"},
    {"AppMinutes", "{0} 分钟"},
    {"ObTitle", "欢迎使用 MoveBit"},
    {"ObHello", "你好，我是小水滴 💧"},
    {"ObIntro", "我会在托盘里安静待命，盯着你到底工作了多久——\n然后在你需要的时候，把你从椅子上请起来。"},
    {"ObSteps", "只需三步，让我们认识一下。"},
    {"ObStrictTitle", "我该多严格？"},
    {"ObStrictQ", "连续工作多久后，我可以打断你？"},
    {"ObModeGentle", "温和"},
    {"ObModeGentleHint", "我自己知道分寸"},
    {"ObModeGentleDesc", "每 60 分钟弹窗提醒，不锁屏"},
    {"ObModeStandard", "标准"},
    {"ObBadgeRecommended", "推荐"},
    {"ObModeStandardDesc", "每 45 分钟锁定全部屏幕，休息 5 分钟"},
    {"ObModeStrict", "严格"},
    {"ObBadgeEvidence", "贴合研究证据"},
    {"ObModeStrictDesc", "每 30 分钟锁定全部屏幕，休息 5 分钟"},
    {"ObStrictFootnote", "「跳过」按钮会延迟 20 秒出现；离开电脑 5 分钟以上，计时自动重置。"},
    {"ObWaterTitle", "多久提醒你喝水？"},
    {"ObMin30", "30 分钟"},
    {"ObMin45", "45 分钟"},
    {"ObMin60", "60 分钟"},
    {"ObSoundTitle", "提醒提示音"},
    {"ObSoundHint", "强制休息永远静默；仅弹窗提示音"},
    {"ObPactTitle", "就这么说定了 🤝"},
    {"ObPactOutro", "现在，回去工作吧。我盯着。"},
    {"ObSkip", "跳过，用默认设置"},
    {"ObNext", "下一步"},
    {"ObStart", "开始"},
    {"ObLaunch", "开始使用"},
    {"ObSummaryForced", "连续工作 {0} 分钟 → 锁定全部屏幕，休息 {1} 分钟"},
    {"ObSummaryToast", "每 {0} 分钟弹窗提醒起身"},
    {"ObSummaryWater", "每 {0} 分钟提醒喝水"},
    {"ObSummaryAway", "离开电脑超过 {0} 分钟，计时自动重置"},
    {"ObSummarySound", "提示音：{0}（强制休息永远静默）"},
    {"ObOn", "开"},
    {"ObOff", "关"},
    {"ObSettled", "就位 💧"},
    {"ObSettledBody", "我会安静待在托盘，到点见。"},
    {"BrkWindowTitle", "休息一下"},
    {"BrkTitle", "休息时间 ☕"},
    {"BrkDone", "休息完成"},
    {"BrkUntilUnlock", "分钟后解锁"},
    {"BrkDefaultHint", "站起来，晃晃肩膀"},
    {"BrkAutoUnlock", "倒计时结束后自动解锁"},
    {"BrkSkip", "跳过，{0} 分钟后再提醒"},
    {"BrkGoodbye", "回去工作吧，我随叫随到"},
    {"MicroWindowTitle", "微休息"},
    {"MicroDefaultTitle", "站 20 秒，看看远处"},
    {"MicroHint", "微休息不打断久坐计时 · 点一下或按 Esc 收起"},
    {"CopyBreakHintPool0", "我帮你盯着屏幕，你负责把水喝掉"},
    {"CopyBreakHintPool1", "快去厕所，我替你把座位捂热……算了，你快去"},
    {"CopyBreakHintPool2", "作为一颗专业水滴，我宣布现在是补水时间"},
    {"CopyBreakHintPool3", "站起来转转脖子，我就在这等你回来"},
    {"CopyBreakHintPool4", "窗外的光比屏幕的光温柔，去看一眼"},
    {"CopyBreakHintPool5", "你的腰比你以为的更需要这次休息"},
    {"CopyBreakHintPool6", "去接杯水吧，就当替我看看亲戚"},
    {"CopyBreakHintPool7", "站着想想刚才的 bug，说不定它先想通了"},
    {"CopyBreakHintPool8", "深呼吸三次，再决定要不要坐下"},
    {"CopyBreakHintPool9", "屁股放假五分钟，效率不会跑的"},
    {"CopySitToastPool0", "椅子不会想你，但你的腰会——水滴敬上"},
    {"CopySitToastPool1", "起来晃两分钟，屏幕我帮你看着"},
    {"CopySitToastPool2", "站起来，让血液重新认识一下下半身"},
    {"CopySitToastPool3", "屏幕不会跑，腿会锈——去动动"},
    {"CopySitToastPool4", "你坐着的姿势，我看着都替你腰疼"},
    {"CopySitToastPool5", "去接杯水顺便散两步，一举两得（我批准了）"},
    {"CopyWaterPool0", "我是水滴，喝口水，咱们就算团聚了"},
    {"CopyWaterPool1", "你的杯子说它好空虚"},
    {"CopyWaterPool2", "水分充足的人，debug 都快三分（大概）"},
    {"CopyWaterPool3", "干了这杯白开水，不为别的，为你的肾"},
    {"CopyWaterPool4", "看完这行字去喝一口，就一口"},
    {"CopyWaterPool5", "咖啡因是借的水，迟早要还的"},
    {"CopyWaterPool6", "嗓子有点干了吧？我掐指一算的"},
    {"CopyWaterPool7", "水喝够了，思路才不容易打结"},
    {"CopyMicroPool0", "站 20 秒，看看远处 👀"},
    {"CopyMicroPool1", "眼睛离开屏幕，找窗外最远的东西"},
    {"CopyMicroPool2", "站起来，肩膀向后绕两圈"},
    {"CopyMicroPool3", "看一眼 6 米外的任何东西，20 秒就够"},
    {"CopyMicroPool4", "掌心捂眼十秒，让眼睛歇口气"},
    {"CopyMicroPool5", "脖子写个「米」字，写慢一点"},
    {"CopyMicroPool6", "原地踏步十秒，假装自己在赶路"},
    {"CopyMicroPool7", "蹲下再站起五次，比咖啡因管用"},
    {"CopyMicroPool8", "去走廊走个来回，顺便看看谁在摸鱼"},
    {"CopyLateNightWaterPool0", "都这个点了，水还是要喝的"},
    {"CopyLateNightWaterPool1", "深夜赶工也要润嗓子，我轻点说"},
    {"CopyLateNightWaterPool2", "夜再深，杯子也别空着"},
    {"CopyLateNightMicroPool0", "夜深了，站 20 秒就好，我小声说"},
    {"CopyLateNightMicroPool1", "眼睛也要下夜班，看会儿远处吧"},
    {"CopyLateNightMicroPool2", "轻一点，站起来 20 秒，别吵醒开着的标签页"},
    {"CopyLateNightWindow", "22:00-06:00"},
  };
  return t;
}

inline const std::map<std::string, std::string>& en_table() {
  static const std::map<std::string, std::string> t = {
    {"TodayTitle", "Active today"},
    {"TodaySitCycle", "Sit cycle"},
    {"TodaySit", "Sit reminders"},
    {"TodayWater", "Water"},
    {"TodayMicro", "Micro breaks"},
    {"HistToggle7", "7 days"},
    {"HistToggle30", "30 days"},
    {"HistRange7", "Last 7 days"},
    {"HistRange30", "Last 30 days"},
    {"HistTotalLong", "Total {0} hr {1} min"},
    {"HistTotalShort", "Total {0} min"},
    {"HistAvg", "Daily avg {0}"},
    {"HistToday", "Today"},
    {"HistTooltip", "{0:yyyy-MM-dd} · {1} active min · {2} sit reminders · longest streak {3} min"},
    {"InsightTitle", "Work pattern"},
    {"InsightCurrent", "Current session"},
    {"InsightTodayLongest", "Longest today"},
    {"InsightRecentLongest", "30-day longest"},
    {"InsightRecentAvg", "30-day daily avg"},
    {"InsightBusiest", "Busiest day"},
    {"InsightNone", "None yet"},
    {"InsightNoData", "Not enough data yet"},
    {"InsightLong2", "Longest session hit {0} — consider standing up sooner."},
    {"InsightLong1", "Longest session {0} — a full sit-reminder cycle."},
    {"InsightLong0", "Longest session {0} — below one sit-reminder cycle."},
    {"SetTitle", "Reminders"},
    {"SetIntro", "Click a number to type in it; changes apply on Enter or focus loss. Steppers use sensible steps."},
    {"SetSitInterval", "Sit reminder interval (min)"},
    {"SetWaterInterval", "Water reminder (min)"},
    {"SetAwayReset", "Away counts as rest (min)"},
    {"SetForce", "Forced breaks"},
    {"SetBreakDuration", "Forced break length (min)"},
    {"SetSkipDelay", "\"Skip\" button delay (sec)"},
    {"SetSnooze", "Snooze length (min)"},
    {"SetMicroToggle", "Micro break: every X min / Y sec"},
    {"SetMicroParams", "Micro interval / length (min · sec)"},
    {"SetSound", "Sound"},
    {"SetAutostart", "Start at login"},
    {"SetLanguage", "Language"},
    {"SetStatusDefault", "Type directly · changes save automatically"},
    {"SetStatusSaved", "✓ Saved · {0:HH:mm:ss}"},
    {"SetStatusApplied", "✓ Applied · {0:HH:mm:ss}"},
    {"SetStatusSaveFailed", "⚠ Active this session, but writing the config file failed"},
    {"LangAuto", "Auto"},
    {"AboutAutoUpdate", "Check updates automatically"},
    {"AboutAutoUpdateHint", "Checks at startup, then every ~6 hours; never installs silently."},
    {"UpdCurrentVersion", "Current version v{0}"},
    {"UpdChecking", "Checking GitHub Releases…"},
    {"UpdCheckingShort", "Checking…"},
    {"UpdFound", "New version {0} found. Release assets verified — click to download, verify, and restart."},
    {"UpdLatest", "Up to date · v{0}"},
    {"UpdCurrentUpToDate", "Current version v{0} · up to date"},
    {"UpdUnsupported", "No official update package for this CPU / OS combination yet."},
    {"UpdCheckFailed", "Update check failed — try again later."},
    {"UpdCheckCancelled", "Update check cancelled."},
    {"UpdPreparing", "Preparing update to {0}…"},
    {"UpdDownloadingPct", "Downloading {0}… {1}%"},
    {"UpdDownloading", "Downloading {0}…"},
    {"UpdVerifying", "Verifying SHA-256…"},
    {"UpdStaging", "Verified — preparing to swap in the new files…"},
    {"UpdRestartPrep", "Preparing restart to the new version…"},
    {"UpdGeneric", "Updating…"},
    {"UpdRestarting", "Update verified and staged — MoveBit is restarting…"},
    {"UpdNotWritable", "App folder isn't writable; can't auto-update."},
    {"UpdFailed", "Update failed — current version untouched."},
    {"UpdCancelled", "Update cancelled — current version unchanged."},
    {"UpdActionUpdate", "Update & restart"},
    {"UpdActionRetry", "Retry"},
    {"UpdActionUpdating", "Updating…"},
    {"MainConfigPath", "Config file: {0}"},
    {"SyncTitle", "Sync"},
    {"TrayOpen", "Settings & stats"},
    {"TrayPause", "Pause reminders for 1 hour"},
    {"TrayResume", "Resume reminders"},
    {"TrayCheckUpdate", "Check for updates"},
    {"TrayUpdateTo", "Update & restart {0}"},
    {"TrayExit", "Quit MoveBit"},
    {"TrayRunning", "MoveBit · {0}active today {1} · sit {2}/{3} min"},
    {"TrayPaused", "MoveBit · {0}paused until {1:HH:mm}"},
    {"TrayUpdatePrefix", "update {0} · "},
    {"NotifWindowTitle", "MoveBit reminder"},
    {"NotifSitTitle", "Time to move 🚶"},
    {"NotifSitBody", "{0} of continuous work — reminder #{1} today."},
    {"NotifWaterTitle", "Sip some water 💧"},
    {"NotifWaterCount", "Reminder #{0} today."},
    {"NotifGotIt", "Got it"},
    {"NotifSnooze", "Later, {0} min"},
    {"NotifCheerTitle", "Droplet applauds 💧"},
    {"NotifCheerBody", "Break #{0} done — your body says thanks. Droplet's tally: {1} of focus today."},
    {"NotifSitFallback", "You've been working continuously for too long — please step away and rest."},
    {"TplPausedUntil", "Paused until {0:HH:mm}"},
    {"TplRunning", "Running"},
    {"TplSitCycleMin", "{0} / {1} min"},
    {"TplOn", "On"},
    {"TplOff", "Off"},
    {"AppHourMin", "{0} hr {1} min"},
    {"AppMinutes", "{0} min"},
    {"ObTitle", "Welcome to MoveBit"},
    {"ObHello", "Hi, I'm Droplet 💧"},
    {"ObIntro", "I live quietly in your tray, watching how long you actually work —\nthen, when the time comes, I'll kindly get you out of the chair."},
    {"ObSteps", "Three quick steps and we're acquainted."},
    {"ObStrictTitle", "How strict should I be?"},
    {"ObStrictQ", "After how much continuous work should I interrupt you?"},
    {"ObModeGentle", "Gentle"},
    {"ObModeGentleHint", "I know my limits"},
    {"ObModeGentleDesc", "Toast every 60 minutes, never locks the screen"},
    {"ObModeStandard", "Standard"},
    {"ObBadgeRecommended", "Recommended"},
    {"ObModeStandardDesc", "Lock all screens every 45 minutes for a 5-minute break"},
    {"ObModeStrict", "Strict"},
    {"ObBadgeEvidence", "Backed by research"},
    {"ObModeStrictDesc", "Lock all screens every 30 minutes for a 5-minute break"},
    {"ObStrictFootnote", "The \"skip\" button appears after 20 seconds; being away 5+ minutes resets the timer."},
    {"ObWaterTitle", "How often should I nudge you to drink?"},
    {"ObMin30", "30 min"},
    {"ObMin45", "45 min"},
    {"ObMin60", "60 min"},
    {"ObSoundTitle", "Reminder sound"},
    {"ObSoundHint", "Forced breaks are always silent; only toasts chime"},
    {"ObPactTitle", "It's a deal 🤝"},
    {"ObPactOutro", "Now, back to work. I'm watching."},
    {"ObSkip", "Skip, use defaults"},
    {"ObNext", "Next"},
    {"ObStart", "Start"},
    {"ObLaunch", "Let's go"},
    {"ObSummaryForced", "{0} min of continuous work → lock all screens for a {1}-minute break"},
    {"ObSummaryToast", "Toast nudge to stand up every {0} minutes"},
    {"ObSummaryWater", "Water reminder every {0} minutes"},
    {"ObSummaryAway", "Away from the desk over {0} minutes resets the timer"},
    {"ObSummarySound", "Sound: {0} (forced breaks are always silent)"},
    {"ObOn", "On"},
    {"ObOff", "Off"},
    {"ObSettled", "All set 💧"},
    {"ObSettledBody", "I'll wait quietly in the tray. See you at the next break."},
    {"BrkWindowTitle", "Take a break"},
    {"BrkTitle", "Break time ☕"},
    {"BrkDone", "Break complete"},
    {"BrkUntilUnlock", "until unlock"},
    {"BrkDefaultHint", "Stand up, shake out your shoulders"},
    {"BrkAutoUnlock", "Unlocks automatically when the countdown ends"},
    {"BrkSkip", "Skip — remind me in {0} minutes"},
    {"BrkGoodbye", "Back to work — I'm on call"},
    {"MicroWindowTitle", "Micro break"},
    {"MicroDefaultTitle", "Stand 20 seconds, look far away"},
    {"MicroHint", "Micro breaks don't touch the sit timer · click or press Esc to dismiss"},
    {"CopyBreakHintPool0", "I'll watch the screen — you go drink that water"},
    {"CopyBreakHintPool1", "Bathroom break. I'd keep your seat warm… fine, just go"},
    {"CopyBreakHintPool2", "As a certified water droplet, I declare this hydration time"},
    {"CopyBreakHintPool3", "Stand up and roll your neck — I'll be right here waiting"},
    {"CopyBreakHintPool4", "The light outside is gentler than the screen. Go have a look"},
    {"CopyBreakHintPool5", "Your back needs this more than you think it does"},
    {"CopyBreakHintPool6", "Go get some water — say hi to my relatives"},
    {"CopyBreakHintPool7", "Think about that bug standing up. Maybe it cracks first"},
    {"CopyBreakHintPool8", "Three deep breaths before you decide to sit back down"},
    {"CopyBreakHintPool9", "Your hips are on break for five minutes. The work isn't going anywhere"},
    {"CopySitToastPool0", "The chair won't miss you, but your back will — Droplet"},
    {"CopySitToastPool1", "Sway for two minutes; I'll mind the screen"},
    {"CopySitToastPool2", "Stand up and let your blood rediscover your legs"},
    {"CopySitToastPool3", "The screen isn't going anywhere. Your legs are rusting — move"},
    {"CopySitToastPool4", "The way you're sitting hurts just from watching"},
    {"CopySitToastPool5", "Refill your water and take a stroll — two birds, one stone (I approve)"},
    {"CopyWaterPool0", "I'm Droplet — take a sip and we're reunited"},
    {"CopyWaterPool1", "Your glass says it's feeling a little empty"},
    {"CopyWaterPool2", "Hydrated people debug 30% faster. (Allegedly.)"},
    {"CopyWaterPool3", "Bottoms up — your kidneys will thank you"},
    {"CopyWaterPool4", "Finish this line, then take one sip. Just one"},
    {"CopyWaterPool5", "Coffee is borrowed water. Time to pay it back"},
    {"CopyWaterPool6", "Throat a little dry? I had a feeling"},
    {"CopyWaterPool7", "Water first. The tangled thoughts come apart easier after"},
    {"CopyMicroPool0", "Stand for 20 seconds. Look far away 👀"},
    {"CopyMicroPool1", "Eyes off the screen — find the farthest thing you can see"},
    {"CopyMicroPool2", "Stand up and roll those shoulders back, twice"},
    {"CopyMicroPool3", "Anything six meters away, for 20 seconds. That's it"},
    {"CopyMicroPool4", "Palms over your eyes for ten seconds. Let them breathe"},
    {"CopyMicroPool5", "Spell your name with your nose. Slowly"},
    {"CopyMicroPool6", "March in place for ten seconds"},
    {"CopyMicroPool7", "Five squats — better than caffeine"},
    {"CopyMicroPool8", "Walk to the end of the hall. See who else is slacking"},
    {"CopyLateNightWaterPool0", "It's late — water still counts"},
    {"CopyLateNightWaterPool1", "Working late? I'll keep it down: one sip"},
    {"CopyLateNightWaterPool2", "However deep the night, don't leave the glass empty"},
    {"CopyLateNightMicroPool0", "It's late. Twenty seconds on your feet — I'll whisper"},
    {"CopyLateNightMicroPool1", "Your eyes want off duty too. Gaze far for a moment"},
    {"CopyLateNightMicroPool2", "Quietly now: stand for 20 seconds. Don't wake the open tabs"},
    {"CopyLateNightWindow", "22:00-06:00"},
  };
  return t;
}

// A format argument: plain text, or an epoch-ms timestamp so the
// "{0:yyyy-MM-dd}" / "{0:HH:mm}" patterns from the JSON can be resolved.
struct Arg {
  std::string text;
  std::int64_t epoch_ms = 0;
  bool is_time = false;
};

inline Arg A(std::string s) { Arg a; a.text = std::move(s); return a; }
inline Arg A(const char* s) { Arg a; a.text = s; return a; }
inline Arg A(std::int64_t n) { Arg a; a.text = std::to_string(n); return a; }
inline Arg T(std::int64_t epoch_ms) { Arg a; a.epoch_ms = epoch_ms; a.is_time = true; return a; }

inline std::string replace_first(std::string s, const std::string& from,
                                 const std::string& to) {
  auto pos = s.find(from);
  if (pos != std::string::npos) s.replace(pos, from.size(), to);
  return s;
}

inline std::string format_epoch(std::int64_t ms, const char* pattern) {
  std::time_t seconds = static_cast<std::time_t>(ms / 1000);
  std::tm local{};
#if defined(_WIN32)
  localtime_s(&local, &seconds);
#else
  localtime_r(&seconds, &local);
#endif
  char buffer[64];
  std::strftime(buffer, sizeof(buffer), pattern, &local);
  return buffer;
}

// Look up a key in the active language, falling back to zh then the key
// itself; substitutes "{i}" and "{i:pattern}" placeholders from args.
inline std::string t(const std::string& key, const std::vector<Arg>& args = {}) {
  const auto& table_ref = g_language == "en" ? en_table() : zh_table();
  auto it = table_ref.find(key);
  if (it == table_ref.end()) {
    it = zh_table().find(key);
    if (it == zh_table().end()) return key;
  }
  std::string s = it->second;
  for (std::size_t i = 0; i < args.size(); ++i) {
    const Arg& arg = args[i];
    if (arg.is_time) {
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":yyyy-MM-dd}",
                        format_epoch(arg.epoch_ms, "%Y-%m-%d"));
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":HH:mm}",
                        format_epoch(arg.epoch_ms, "%H:%M"));
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":HH:mm:ss}",
                        format_epoch(arg.epoch_ms, "%H:%M:%S"));
    }
    s = replace_first(std::move(s), "{" + std::to_string(i) + "}",
                      arg.is_time ? format_epoch(arg.epoch_ms, "%Y-%m-%d")
                                  : arg.text);
  }
  return s;
}

// Pick a random line from a copy pool emitted as indexed keys
// ("Prefix0" ... "Prefix{count-1}").
inline std::string pick(const std::string& prefix, int count) {
  if (count <= 0) return prefix;
  static std::mt19937 generator{std::random_device{}()};
  std::uniform_int_distribution<int> dist(0, count - 1);
  return t(prefix + std::to_string(dist(generator)));
}

}  // namespace l10n

#endif  // MOVEBIT_GENERATED_STRINGS_H
