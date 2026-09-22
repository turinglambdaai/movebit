# MoveBit

一个托盘常驻的健康小卫士：监测你**真正处于电脑前工作的时间**，到点把你从椅子上请起来——久坐提醒可用带倒计时的休息屏**覆盖所有显示器**，喝水提醒保持轻量。基于 **Avalonia 11** / .NET 10 构建，支持 Windows、macOS 和 Linux。

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[English](README.md) · **中文**

## 为什么做

很多提醒工具的问题不是“不会提醒”，而是提醒太容易被随手关掉。MoveBit 把下面几件事当成产品契约：

- **尽量统计活跃工作时长，而不是盲目累计墙上时间。** Windows、macOS、Linux/X11 都有会话级空闲检测；Wayland 会优先使用 GNOME/Mutter IdleMonitor 或 freedesktop ScreenSaver 会话总线接口。桌面环境没有提供可靠接口时，MoveBit 会明确退化为自然时间，而不是猜一个假的空闲时间。
- **让长休息变成一个明确动作。** 久坐提醒可以覆盖所有已连接显示器，显示倒计时；“跳过”按钮默认延迟 20 秒出现。
- **真的休息过，就不要马上再催。** 离开电脑达到阈值后回来，三个提醒周期都会从零开始；完整完成一次强制休息也一样，而且休息本身不会被算进工作时长。
- **不只看今天，还要看到工作模式。** 主界面支持最近 7 天 / 30 天切换，并统计当前连续工作、今日/近 30 天最长连续工作、活跃日平均时长、最活跃的一天等指标。
- **用户可写目录里的版本应该能自己更新。** MoveBit 会后台检查 GitHub Releases；只有用户主动点击“更新并重启”才会下载 ZIP、校验 SHA-256、替换并重新启动。
- **需要原生安装体验时，也应该像正常桌面软件。** Windows 有 Setup，macOS 有 `.app` / DMG，Debian/Ubuntu 有 `.deb`，同时三个平台继续保留 Portable ZIP。

## v1 产品保证

MoveBit v1 把这些行为固定下来：

- 第二次启动不会再创建第二套调度器，而是唤起已经运行的实例；
- 强制休息时间不会污染“今日活跃时间”；
- 同一个 Tick 同时到期的喝水/微休息，不会盖到强制休息界面上；
- 完成长休息后，久坐、喝水、微休息周期和连续工作会话一起重新开始；
- 配置与历史数据采用“临时文件写入 + 替换”的持久化方式；
- 历史数据最多保留 370 天，新增洞察字段后仍兼容旧版 `history.json`；
- Git tag 必须和项目版本一致；
- 发布压缩包、安装器和原生包都生成 SHA-256 校验文件；
- 在线更新必须先校验下载包的 SHA-256；
- 更新先解压到 staging，程序退出后由独立 helper 替换应用文件；替换失败时恢复已覆盖的旧文件。

## 功能

- 🪟 **托盘常驻**：启动后不会强行弹设置窗口
- ⏱️ **活跃工作监测**：Windows `GetLastInputInfo`、macOS CoreGraphics、Linux/X11 XScreenSaver，以及可用时的 Wayland 会话总线后端
- 🚨 **强制休息**：多显示器全屏遮罩 + 倒计时；Alt+F4 无法直接退出；跳过按钮延迟出现
- 👀 **微休息**：短时屏幕中央提示，不锁屏、不出声
- 💧 **喝水提醒**：独立周期的轻量弹窗
- 📊 **7 天 / 30 天历史**：可以切换最近一周和最近一个月，后台保留最多 370 天数据
- 🧠 **工作模式洞察**：当前连续工作、今日最长连续、近 30 天最长连续、活跃日平均时长、最活跃日和久坐提示
- 🌗 **深浅色主题**跟随系统
- 🔁 **登录自启**：Windows 注册表 / macOS LaunchAgent / Linux XDG autostart
- ⏸️ 托盘菜单**暂停 1 小时**，暂停结束时间按真实到期点恢复计算
- 🧙 **首次运行引导**：先选择温和 / 标准 / 严格，再开始使用强制休息
- 🔒 **单实例激活**：重复启动只会唤起已有实例
- ⬆️ **校验后的在线更新**：用户可写安装目录支持后台检查 + 手动“更新并重启”
- 📦 **原生包 + 便携版**：Windows Setup、macOS `.app`/DMG、Debian `.deb`，三个平台继续提供 Portable ZIP

## 安装

### Windows x64 —— 推荐安装版

从 [Releases](https://github.com/turinglambdaai/movebit/releases) 下载 **`MoveBit-Setup-windows-x64.exe`**。安装器默认推荐：

`%LOCALAPPDATA%\Programs\MoveBit`

这个位置**不需要管理员权限**，也最适合应用内更新；安装时仍然可以自由选择其他当前用户有写权限的位置。安装器会创建开始菜单快捷方式、标准卸载入口，并提供可选桌面快捷方式。

### macOS arm64 —— `.app` / DMG

下载 **`MoveBit-macos-arm64.dmg`**，打开后把 **MoveBit.app** 拖入 Applications（或者你自己的目录）。如果不想用 DMG，也提供 `MoveBit-macos-arm64.app.zip`。

目前 macOS 原生包**没有签名和公证**，所以 Gatekeeper 首次运行时可能需要用户明确允许。这一项需要 Apple 付费开发者身份，因此按当前策略暂不做。

如果把 MoveBit.app 放在 `/Applications` 这类应用自身不可写的位置，后续请用新版 DMG 替换应用；如果特别希望使用 MoveBit 的应用内替换更新，可以继续使用放在用户可写目录中的 Portable ZIP。

### Debian / Ubuntu x64 —— `.deb`

下载 **`MoveBit-linux-x64.deb`**，可以直接用系统包管理器安装，例如：

```bash
sudo apt install ./MoveBit-linux-x64.deb
```

`.deb` 会把程序安装到 `/opt/movebit`，同时创建 `/usr/bin/movebit`、桌面菜单入口和应用图标。因为 `/opt` 由包管理器管理，升级 `.deb` 安装版时应直接安装新版 `.deb`，MoveBit 不会尝试以普通用户权限覆盖系统包管理目录。

### 便携版

三个平台仍然提供 Portable ZIP：

| 平台 | 便携压缩包 |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

发布构建已经包含所需 .NET 运行时，不需要另外安装。每个正式发布包都有对应的 `.sha256` 文件。

> 当前 Release 二进制还没有做代码签名 / macOS 公证，因此 Windows SmartScreen 或 macOS Gatekeeper 首次启动时可能给出提示。付费签名/公证是目前唯一保留的分发 Roadmap 项。

## 在线更新

MoveBit 1.0.1 起默认会在启动后稍作延迟检查一次最新 GitHub Release，之后大约每 6 小时检查一次。**自动检查不等于静默安装**：任何版本替换都需要用户明确点击“更新并重启”。

对于程序目录可写的安装版 / 便携版：

1. 设置页与托盘菜单显示“更新并重启”。
2. 下载当前平台对应的 Portable ZIP 和 `.sha256` 文件。
3. 本地重新计算 SHA-256；不一致立即停止。
4. 校验通过后解压到 staging。
5. MoveBit 保存配置和当天统计，然后退出。
6. 独立更新 helper 替换应用文件并重新启动。
7. 替换阶段失败时恢复已经覆盖的旧文件。

MoveBit 的配置与历史数据放在用户应用数据目录，不会因为替换应用文件而丢失。DMG 拖进 `/Applications` 或 `.deb` 装进 `/opt` 后，应用目录通常由系统 / 包管理器管理，这类安装方式请通过新版 DMG / `.deb` 升级。

## 工作原理

三个独立周期由 30 秒调度 Tick 推进：

- **久坐周期**只累计活跃工作时间。达到配置间隔（默认 45 分钟）后，如果启用了强制休息就进入全屏休息，否则弹窗提醒。
- **喝水周期**独立运行，默认 30 分钟。
- **微休息周期**独立运行，默认每 30 分钟提示 20 秒，不会替代长休息周期。

空闲时间达到离开阈值（默认 5 分钟）后，所有周期停止累计；回来时三个周期从零重新开始。系统休眠等超大时间跳变会被钳制，不会在唤醒后一次性倾倒积压提醒。

强制休息被明确视为“休息时间”：期间不会累计活跃时长，也不会推进其他提醒周期。完整完成强制休息、真实离开、暂停或者跨天时，“当前连续工作”会重新开始，但当天最长连续工作峰值会保留下来。

普通提醒中选择“稍后”，或者跳过强制休息后，同类提醒会按照可配置的延后时间再次出现（默认累计 10 分钟活跃时间）。

每日统计每隔几分钟写入 `history.json`，跨天自动归档，最多保留最近 370 天。v1.1.0 开始还会保存每日最长连续工作时长，并兼容旧版没有该字段的历史文件。

## 配置

Windows 默认使用 `%APPDATA%\movebit\config.json`；macOS/Linux 使用对应的平台应用数据目录。所有面向用户的配置都可以在设置窗口修改：

| 配置项 | 默认值 | 范围 |
| --- | --- | --- |
| `SitReminderMinutes` | 45 | 10–240 |
| `WaterReminderMinutes` | 30 | 5–180 |
| `AwayResetMinutes` | 5 | 1–60 |
| `ForceBreakEnabled` | true | — |
| `BreakDurationMinutes` | 5 | 1–30 |
| `SkipAfterSeconds` | 20 | 0–120 |
| `SnoozeMinutes` | 10 | 5–60 |
| `MicroBreakEnabled` | true | — |
| `MicroBreakIntervalMinutes` | 30 | 10–60 |
| `MicroBreakDurationSeconds` | 20 | 10–60 |
| `SoundEnabled` | true | — |
| `AutoCheckUpdates` | true | — |

数字时间项都支持直接键盘输入并立即保存。较长间隔使用实用步进，短时长保留更细粒度调整。

## 平台说明

- **Windows**：通过 `GetLastInputInfo` 获取会话级空闲时间；推荐当前用户安装版，同时保留 Portable ZIP。
- **macOS**：通过 CoreGraphics 的 `CGEventSourceSecondsSinceLastEventType` 获取会话级空闲时间；提供原生 `.app` / DMG 与 arm64 Portable ZIP。
- **Linux/X11**：通过 XScreenSaver 扩展的 `XScreenSaverQueryInfo` 获取空闲时间。
- **Linux/Wayland**：优先查询 GNOME/Mutter `org.gnome.Mutter.IdleMonitor`，再尝试 freedesktop ScreenSaver 会话总线 idle API；如果桌面环境两者都不提供，则返回“未知空闲时间”，MoveBit 安全退化为自然时间提醒，不伪造输入状态。

强制休息使用的是置顶遮罩窗口，而不是全局键鼠钩子。这是有意为之：全局锁输入一旦程序异常，可能导致机器难以操作。

## 从源码构建

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet restore MoveBit.slnx
dotnet build MoveBit.slnx -c Release --no-restore
dotnet test MoveBit.Tests/MoveBit.Tests.csproj -c Release --no-build
```

需要 .NET 10 SDK。原生打包脚本放在 `packaging/`：

```bash
# 在 macOS runner 上
bash packaging/macos/build-native.sh 1.1.0

# 在 Debian/Ubuntu runner 上
bash packaging/linux/build-deb.sh 1.1.0
```

## 发布流程

1. `MoveBit.csproj` 中的 `<Version>` 必须和发布 tag 完全对应，例如 `1.1.0` ↔ `v1.1.0`。
2. 只有 Windows / macOS / Linux 构建和测试全绿后再合并；CI 还会额外验证 Windows Setup、挂载并检查 DMG、真实安装并卸载 `.deb`。
3. 推送版本 tag。
4. Release workflow 生成三个 Portable ZIP、Windows Setup、macOS `.app.zip` + DMG、Linux `.deb` 和所有 SHA-256 sidecar；所有必要包都成功后才创建一次 GitHub Release。
5. 用户可写目录中的 MoveBit 1.0.1+ 可以继续通过 latest-release API 使用对应 Portable ZIP 完成应用内更新。

## Roadmap

不需要付费凭据的项目已经完成：

- [x] Linux/Wayland 主流原生空闲检测，并在桌面不提供可靠 API 时安全回退
- [x] macOS 原生 `.app` / DMG 分发，以及 Debian/Ubuntu `.deb` 分发
- [x] 最近 7 天 / 30 天历史视图
- [x] 工作模式洞察（连续工作、最长会话、近期平均、最活跃日等）

暂缓的付费分发项：

- [ ] Windows 代码签名，以及 macOS 签名 / 公证

## 许可

[MIT](LICENSE)
