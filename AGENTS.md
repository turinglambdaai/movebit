# AGENTS.md

指引给 AI agent（及开发者）：如何理解、构建、运行、改动 MoveBit。

## 这是什么

MoveBit 以 Rivet（github.com/turinglambdaai/rivet）构建：一份 Racket 领域核心，
通过 Rivet 类型化 RPC（RVT1 协议）驱动各平台第一方原生 UI 薄壳。
Avalonia/.NET 旧栈已彻底移除，仓库从 rivet 时代（0.1.0 起）起算。

| 平台宿主 | 技术栈 | 目录 | 状态 |
|---|---|---|---|
| macOS | SwiftUI 宿主 + 生成客户端 | `macos-host/` | 可用 |
| Windows | C++/WinRT 宿主 + 生成客户端 | `windows/` | 可用 |
| Linux | GTK4 宿主 + 生成客户端 | `linux/` | 可用 |
| agent CLI | — | — | 不适用 |

## 快速命令

```bash
# 前置：Racket CS 9.x，rivet 以 link 方式安装
# cd ../rivet && raco pkg install --auto --no-docs --name rivet --link file://$PWD

raco rivet doctor --json   # 工具链自检
raco rivet schema check rivet-schema.json --json # RPC 兼容性门禁
raco rivet build           # 生成三端客户端 + 编译后端 bundle + 宿主构建
raco rivet dev             # 开发循环
raco test racket/          # 领域核心测试
raco test racket/tests/backend-server-test.rkt # 真实 RVT1 wire test
raco rivet acceptance --json # 当前平台的打包产品验收
```

## 完成定义

- GUI 功能必须通过 `raco rivet acceptance --json`：领域契约、真实 RVT1、
  原生宿主、可分发包以及打包后的原生 UI 操作缺一不可。
- 默认验收使用隔离配置启动打包应用，并确认后端发布的 `Active today`
  状态进入原生可访问性树；只构建成功、只截图或只打开空窗口不算完成。
- 跨平台交互要同步 macOS、Windows、Linux，或明确记录真实平台边界。
- 不得用 `continue-on-error`、空测试、跳过 UI 或强杀进程作为成功证据。

## 契约（不要破坏）

- **数据路径与格式与旧版完全一致**（drop-in 迁移）：config.json / history.json（按日 DayRecord，保留 370 天），目录 macOS ~/Library/Application Support/movebit · Linux ~/.config/movebit · Windows %APPDATA%/movebit
- **i18n 单源**：`shared/i18n/{zh,en}.json`，平台副本必须逐字节一致；zh 为默认
- **RPC 面**：`app/backend.rkt` 的 define-rpc 是宿主唯一数据通道；改签名 = 各端宿主 + 生成客户端同步改
- **宿主只做渲染与交互**：业务一律走 RPC；定时器/调度/HTTP/存储都在 Racket 后端
- **Rivet 改动走上游**：缺能力先提 issue/PR 到 turinglambdaai/rivet

## 项目结构

```
├── rivet.rktd          Rivet 应用清单
├── app/backend.rkt     Rivet 后端入口（装配领域层）
├── racket/             Racket 领域核心 + tests/
├── shared/i18n/        zh.json / en.json 单源
├── macos-host/         SwiftUI 宿主
├── windows/            WinUI3 宿主
└── linux/              GTK4 宿主
```
