# MoveBit

一个托盘常驻的健康小卫士：监测你**真实的工作时长**，到点强制把你从椅子上请起来——久坐提醒用带倒计时的休息屏**接管所有显示器**，喝水提醒保持轻量弹窗。基于 **Avalonia 11** / .NET 10 构建，支持跨平台（Windows / macOS / Linux）。

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[English](README.md) · **中文**

## 为什么做

久坐正在悄悄搞坏你的身体。常见的提醒工具弹个小气泡，你手一滑关掉，人没站起来，循环继续。MoveBit 认真对待「强制」二字：

- **统计的是活跃工作时长**，不是挂机时间。人离开，时钟就停。
- **久坐提醒直接接管整个屏幕**——所有已连接的显示器都会被置顶遮罩盖住，带倒计时。「跳过」按钮延迟出现（默认 20 秒），让跳过成为一个主动决定，而不是条件反射。
- **你真的休息过了？它就不烦你。** 空闲超过离开阈值（默认 5 分钟）后回来，两个计时周期都从零开始。

## 功能

- 🪟 **托盘常驻**，启动不弹窗口；左键托盘图标打开设置与统计
- ⏱️ **工作时间监测**：会话级空闲检测（任何应用的键鼠输入都算）
- 🚨 **强制休息**：全屏多显示器遮罩 + 倒计时，Alt+F4 关不掉；跳过按钮延迟出现
- 💧 **喝水提醒**保持轻量右下角弹窗（喝口水不需要锁屏）
- 📊 **今日统计**：活跃时长、久坐/喝水提醒次数，托盘 tooltip 显示当前周期进度
- ⏸️ 托盘菜单**暂停 1 小时**（开会、共享屏幕时用）
- 🛠️ 全部可配置，设置持久化到 `config.json`

## 安装

从 [Releases](https://github.com/turinglambdaai/movebit/releases) 下载对应平台的自包含单文件构建，解压即用，无需安装 .NET 运行时。

| 平台 | 压缩包 |
| --- | --- | 
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

## 工作原理

两个独立周期，每 30 秒推进一次：

- **久坐周期**只在活跃时累计，到间隔（默认 45 分钟）触发——强制休息开启时为全屏遮罩（默认 5 分钟），否则弹窗。
- **喝水周期**按自己的间隔（默认 30 分钟）弹窗。

空闲 ≥ 离开阈值（默认 5 分钟）判定为「人离开了」：两个周期冻结，回来时从零开始——休息已经发生，不再补催。系统休眠等时钟跳变会被钳制，唤醒后不会被积压的提醒轰炸。

## 配置

配置文件在 `%APPDATA%\movebit\config.json`（Windows）或 `~/.config/movebit/config.json`（macOS/Linux），所有项均可在设置窗口修改：

| 配置项 | 默认值 | 范围 |
| --- | --- | --- |
| `SitReminderMinutes` | 45 | 10–240 |
| `WaterReminderMinutes` | 30 | 5–180 |
| `AwayResetMinutes` | 5 | 1–60 |
| `ForceBreakEnabled` | true | — |
| `BreakDurationMinutes` | 5 | 1–30 |
| `SkipAfterSeconds` | 20 | 0–120 |
| `SoundEnabled` | true | — |

## 平台说明

- **Windows**：全功能——`GetLastInputInfo` 空闲检测、`MessageBeep` 提示音。
- **macOS / Linux**：可用；空闲检测尚未实现，提醒按自然时间计（产物正常构建，但未在真实硬件上持续测试）。

强制休息是置顶窗口，不是键鼠钩子——这是有意为之。全局锁输入是危险动作（钩子崩溃会让机器没法用）；屏幕遮罩 + 延迟跳过足以把你请离椅子，又不承担那个风险。

## 从源码构建

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet run        # 源码运行
dotnet test       # 调度器单元测试
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish
```

需要 .NET 10 SDK。

## Roadmap

- [ ] macOS/Linux 空闲检测（CGEventSource / XScreenSaver）
- [ ] 每日/每周统计历史
- [ ] 开机自启
- [ ] 微休息模式（每 10 分钟 20 秒）与长休息并存

## 许可

[MIT](LICENSE)
