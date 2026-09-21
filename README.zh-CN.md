# MoveBit

一个托盘常驻的健康小卫士：监测你**真正处于电脑前工作的时间**，到点把你从椅子上请起来——久坐提醒可用带倒计时的休息屏**覆盖所有显示器**，喝水提醒保持轻量。基于 **Avalonia 11** / .NET 10 构建，支持 Windows、macOS 和 Linux。

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[English](README.md) · **中文**

## 为什么做

很多提醒工具的问题不是“不会提醒”，而是提醒太容易被随手关掉。MoveBit 把下面几件事当成产品契约：

- **尽量统计活跃工作时长，而不是盲目累计墙上时间。** Windows、macOS 和 Linux/X11 都有会话级空闲检测；Linux/Wayland 目前因为缺少适合本应用的统一全局空闲接口，会退化为自然时间提醒。
- **让长休息变成一个明确动作。** 久坐提醒可以覆盖所有已连接显示器，显示倒计时；“跳过”按钮默认延迟 20 秒出现。
- **真的休息过，就不要马上再催。** 离开电脑达到阈值后回来，三个提醒周期都会从零开始；完整完成一次强制休息也一样，而且休息本身不会被算进工作时长。
- **已经安装的版本应该能自己跟上新版本。** MoveBit 会后台检查 GitHub Releases；发现新版本后明确提示，只有用户主动点击“更新并重启”才会下载、校验、替换并重新启动。

## v1 产品保证

MoveBit v1 把这些行为固定下来：

- 第二次启动不会再创建第二套调度器，而是唤起已经运行的实例；
- 强制休息时间不会污染“今日活跃时间”；
- 同一个 Tick 同时到期的喝水/微休息，不会盖到强制休息界面上；
- 完成长休息后，久坐、喝水、微休息三个周期一起重新开始；
- 配置与历史数据采用“临时文件写入 + 替换”的持久化方式；
- 历史数据最多保留 370 天；
- 发布依赖固定版本，Git tag 必须和项目版本一致；
- 每个发布压缩包都会同时生成 SHA-256 校验文件；
- 在线更新必须先用 Release 同名 `.sha256` 校验下载包，通过后才允许进入替换阶段；
- 更新先解压到 staging，程序退出后由独立 helper 替换应用文件；替换失败时会恢复已经覆盖的旧文件。

## 功能

- 🪟 **托盘常驻**：启动后不会强行弹设置窗口
- ⏱️ **活跃工作监测**：Windows `GetLastInputInfo`、macOS CoreGraphics、Linux/X11 XScreenSaver
- 🚨 **强制休息**：多显示器全屏遮罩 + 倒计时；Alt+F4 无法直接退出；跳过按钮延迟出现
- 👀 **微休息**：短时屏幕中央提示，不锁屏、不出声
- 💧 **喝水提醒**：独立周期的轻量弹窗
- 📊 **今日 + 历史统计**：实时活跃时长与提醒次数、最近 7 天图表，后台最多保存 370 天
- 🌗 **深浅色主题**跟随系统
- 🔁 **登录自启**：Windows 注册表 / macOS LaunchAgent / Linux XDG autostart
- ⏸️ 托盘菜单**暂停 1 小时**，暂停结束时间按真实到期点恢复计算
- 🧙 **首次运行引导**：先选择温和 / 标准 / 严格，再开始使用强制休息
- 🔒 **单实例激活**：重复启动只会唤起已有实例
- ⬆️ **在线更新**：设置页和托盘都能检查/安装新版本；Windows x64、macOS arm64、Linux x64 共用现有 GitHub Release ZIP + SHA-256 发布链路

## 安装

从 [Releases](https://github.com/turinglambdaai/movebit/releases) 下载对应平台压缩包；需要时可用同名 `.sha256` 文件校验完整性。解压即可运行，不需要单独安装 .NET 运行时。

| 平台 | 压缩包 |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

> 当前 GitHub Release 二进制尚未做代码签名/公证，因此 Windows SmartScreen 或 macOS Gatekeeper 首次启动时可能给出提示。签名与原生安装包属于后续分发体验优化，不影响程序本身运行。

## 在线更新

MoveBit 1.0.1 起默认会在启动后稍作延迟检查一次最新 GitHub Release，之后大约每 6 小时检查一次。这个行为可以在设置页关闭。**自动检查不等于静默安装**：任何版本替换都需要用户明确点击“更新并重启”。

发现新版本后的流程：

1. 设置页与托盘菜单显示“更新并重启”。
2. 下载当前平台对应的 Release ZIP 和 `.sha256` 文件。
3. 本地重新计算 SHA-256；不一致立即停止，旧版本保持不变。
4. 校验通过后解压到临时 staging 目录。
5. MoveBit 先保存配置和当天统计，然后退出。
6. 独立更新 helper 等待主进程完全退出，再替换应用文件并重新启动 MoveBit。
7. 如果替换阶段失败，helper 会把已经覆盖的旧文件恢复回来。

MoveBit 的配置与历史数据放在系统用户应用数据目录，而不是应用程序目录，因此程序更新不会覆盖用户数据。

当前自动应用更新与正式 Release 的架构保持一致：**Windows x64、macOS arm64、Linux x64**。如果你把便携版放在无写权限目录中，MoveBit 会拒绝替换并保留当前版本，不会半更新。

## 工作原理

三个独立周期由 30 秒调度 Tick 推进：

- **久坐周期**只累计活跃工作时间。达到配置间隔（默认 45 分钟）后，如果启用了强制休息就进入全屏休息，否则弹窗提醒。
- **喝水周期**独立运行，默认 30 分钟。
- **微休息周期**独立运行，默认每 30 分钟提示 20 秒，不会替代长休息周期。

空闲时间达到离开阈值（默认 5 分钟）后，所有周期停止累计；回来时三个周期从零重新开始。系统休眠等超大时间跳变会被钳制，不会在唤醒后一次性倾倒积压提醒。

强制休息被明确视为“休息时间”：期间不会累计活跃时长，也不会推进其他提醒周期。倒计时完整结束后三个周期重新计时。如果同一个 Tick 恰好同时触发多个提醒，强制久坐休息优先，喝水和微休息 UI 不会叠在它上面。

每日统计每隔几分钟写入 `history.json`，跨天自动归档，并裁剪为最近 370 天。

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
| `MicroBreakEnabled` | true | — |
| `MicroBreakIntervalMinutes` | 30 | 10–60 |
| `MicroBreakDurationSeconds` | 20 | 10–60 |
| `SoundEnabled` | true | — |
| `AutoCheckUpdates` | true | — |

## 平台说明

- **Windows**：通过 `GetLastInputInfo` 获取会话级空闲时间；提示音使用 `MessageBeep`；在线更新使用 `MoveBit-windows-x64.zip`。
- **macOS**：通过 CoreGraphics 的 `CGEventSourceSecondsSinceLastEventType` 获取会话级空闲时间；在线更新使用 arm64 Release 包。
- **Linux/X11**：通过 XScreenSaver 扩展的 `XScreenSaverQueryInfo` 获取空闲时间；Linux x64 支持在线更新。
- **Linux/Wayland**：程序可正常使用，但空闲检测目前会退化为自然时间提醒；原生 Wayland 空闲支持仍在 Roadmap 中。

强制休息使用的是置顶遮罩窗口，而不是全局键鼠钩子。这是有意为之：全局锁输入一旦程序异常，可能导致机器难以操作；MoveBit 的目标是让“跳过”变得有意识，而不是接管输入设备。

## 从源码构建

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet restore MoveBit.slnx
dotnet build MoveBit.slnx -c Release --no-restore
dotnet test MoveBit.Tests/MoveBit.Tests.csproj -c Release --no-build
```

需要 .NET 10 SDK。

本地发布 Windows x64：

```bash
dotnet publish MoveBit.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
```

## 发布流程

1. `MoveBit.csproj` 中的 `<Version>` 必须和发布 tag 完全对应，例如 `1.0.1` ↔ `v1.0.1`。
2. 只有 Windows / macOS / Linux 三平台 CI 全绿后再合并。
3. 推送版本 tag。
4. Release workflow 会先跑测试，再生成三个支持平台的压缩包和 SHA-256 文件，全部成功后只创建一次 GitHub Release。
5. MoveBit 1.0.1+ 会通过 GitHub latest-release API 发现这个新版本，并在应用内使用对应平台的已校验 Release 包完成更新。

## Roadmap

- [ ] Linux/Wayland 原生空闲检测
- [ ] 代码签名、公证和原生安装包
- [ ] 最近 7 天之外的周/月历史视图
- [ ] 工作模式洞察（最长连续久坐、最长单次会话等）

## 许可

[MIT](LICENSE)
