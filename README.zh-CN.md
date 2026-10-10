# MoveBit

> **Rivet 重建（本分支）：** 正在以 [Rivet](https://github.com/turinglambdaai/rivet) 重建——一份 Racket 领域核心通过类型化 RPC 驱动各平台第一方原生宿主（见 AGENTS.md）。下文描述的旧栈是已归档的 `main` 线，仅作行为与视觉参照。

一个托盘常驻的健康小卫士：监测你**真正处于电脑前工作的时间**，到点把你从椅子上请起来——久坐提醒可用带倒计时的休息屏**覆盖所有显示器**，喝水提醒保持轻量。基于 **Avalonia 11** / .NET 10 构建，支持 Windows、macOS 和 Linux。

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[English](README.md) · **中文**

## 为什么做

很多提醒工具的问题不是“不会提醒”，而是提醒太容易被随手关掉。MoveBit 把下面几件事当成产品契约：

- **尽量统计活跃工作时长，而不是盲目累计墙上时间。** Windows、macOS、Linux/X11 都有会话级空闲检测；Wayland 会优先使用 GNOME/Mutter IdleMonitor 或 freedesktop ScreenSaver 会话总线接口。桌面环境没有提供可靠接口时，MoveBit 会明确退化为自然时间，而不是猜一个假的空闲时间。
- **让长休息变成一个明确动作。** 久坐提醒可以覆盖所有已连接显示器，显示倒计时；“跳过”按钮默认延迟 20 秒出现。
- **真的休息过，就不要马上再催。** 离开电脑达到阈值后回来，三个提醒周期都会从零开始；完整完成一次强制休息也一样，而且休息本身不会被算进工作时长。
- **不只看今天，还要看到工作模式。** 主界面支持最近 7 天 / 30 天切换，并统计当前连续工作、今日/近 30 天最长连续工作、活跃日平均时长、最活跃的一天等指标。
- **用户可写目录里的版本应该能自己更新。** MoveBit 的更新 feed 使用 GitHub Releases 上的便携压缩包 + SHA-256 sidecar；C# 线会后台检查，校验 SHA-256、替换当前便携/按用户安装，并且只有你主动点击“更新并重启”才会动手。
- **每个桌面平台都同时提供原生安装器和便携压缩包。** Windows 有 MSI + 便携 ZIP，macOS 在 Apple silicon 和 Intel 上都有 DMG + 便携 ZIP，Linux 提供便携 tar.gz。

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
- 📦 **原生包 + 便携版**：Windows MSI、macOS DMG、Windows/macOS 便携 ZIP、Linux tar.gz

## 安装

从 [Releases](https://github.com/turinglambdaai/movebit/releases/latest) 下载对应平台的构建：

| 平台 | 便携压缩包（更新 feed） | 安装器 |
| --- | --- | --- |
| macOS Apple silicon | `movebit-<version>-macos-arm64.zip` | `movebit-<version>-macos-arm64.dmg` |
| macOS Intel | `movebit-<version>-macos-x64.zip` | `movebit-<version>-macos-x64.dmg` |
| Windows x64 | `movebit-<version>-windows-x64.zip` | `movebit-<version>-windows-x64.msi` |
| Windows ARM64 | 通过 Windows on ARM 的 x64 模拟运行 x64 构建 | — |
| Linux x64 | `movebit-<version>-linux-x64.tar.gz` | — |

所有资产遵循同一套小写命名：`movebit-<version>-<os>-<arch>.<ext>`（例如 `movebit-0.1.0-macos-arm64.dmg`）。每个资产都有对应的 `.sha256` 文件，每个发布还带 `SHA256SUMS` 汇总清单和作为更新 feed 的 Ed25519 签名 `update-manifest.json`。

平台说明：

- **Windows** — 日常使用建议运行 MSI 安装；便携 ZIP 解压到任意可写目录即可。ARM64 Windows 请使用 x64 资产：Windows on ARM 会通过 x64 模拟运行，官方不发布原生 ARM64 构建。
- **macOS** — 打开 DMG，把 **MoveBit.app** 拖进 Applications（或你自己的目录）；便携 ZIP 就是同一个应用包，只是没有磁盘镜像。构建为 ad-hoc 签名、未公证，Gatekeeper 首次运行可能要求明确允许（右键 → 打开，或 `xattr -cr /Applications/MoveBit.app`）。公证在拿到付费 Apple 开发者身份前暂缓。
- **Linux** — 解压 tar.gz 后运行其中的宿主；GTK 4 及其系统库是仅有的运行时依赖，其余全部自带。

> 发布二进制没有发布者代码签名。Windows SmartScreen 或 macOS Gatekeeper 首次启动时可能给出提示。付费签名/公证是目前唯一保留的分发 Roadmap 项。

## 在线更新

更新 feed = 当前平台的便携压缩包 + 对应 `.sha256` 文件，通过 GitHub 的 latest-release API 发现，按发布的资产命名规则匹配。任何替换发生之前都会先完成校验。

C# 线（≤ 1.4.x）具备完整的应用内更新流程：启用“自动检查更新”后，启动后稍作延迟检查一次、之后大约每 6 小时检查一次；下载当前平台的便携压缩包和 `.sha256`，本地重新计算 SHA-256，校验通过后解压到 staging；只有你明确点击“更新并重启”，MoveBit 才会退出、由独立 helper 替换应用文件（失败时回滚）并重新启动。配置与历史数据放在用户应用数据目录，应用目录之外的它们不会被子更新触碰；`/Applications` 这类系统目录对应用只读，这类安装请用新版 DMG 手动替换。

1.5+ 的 Rivet 原生宿主尚未接入应用内更新器；feed 和签名 `update-manifest.json` 已为这次迁移发布。在此之前，请通过运行新版 MSI/DMG 或替换便携/tar 包来升级。

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

- **Windows**：通过 `GetLastInputInfo` 获取会话级空闲时间；提供 MSI 安装版与便携 ZIP（ARM64 通过模拟运行 x64 构建）。
- **macOS**：通过 CoreGraphics 的 `CGEventSourceSecondsSinceLastEventType` 获取会话级空闲时间；Apple silicon 与 Intel 均提供 DMG 与便携 ZIP。
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
bash packaging/macos/build-native.sh 0.1.0

# 在 Debian/Ubuntu runner 上
bash packaging/linux/build-deb.sh 0.1.0
```

## 发布流程

1. 更新单源 `VERSION` 文件；`scripts/check-release-version.sh` 会校验 `VERSION == rivet.rktd == MoveBit.csproj <Version>`，发布 tag 也必须与之一致（例如 `0.1.0` ↔ `v0.1.0`）。
2. CI 全绿后再合并（Racket 测试 + Windows/macOS/Linux 宿主矩阵；CI 会验证 Windows 宿主编译）。
3. 推送版本 tag。
4. Release workflow 生成 macOS DMG + 便携 ZIP（arm64 与 x64）、Windows MSI + 便携 ZIP、Linux tar.gz、每个资产的 `.sha256` sidecar、`SHA256SUMS` 汇总清单和签名的 `update-manifest.json`；所有必要包都成功后才创建 GitHub Release，发布说明取自 CHANGELOG 对应段落。
5. 便携压缩包就是更新 feed：可更新的安装通过 GitHub latest-release API 发现新版本，按 `movebit-<version>-<os>-<arch>.<ext>` 命名规则匹配资产。

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
