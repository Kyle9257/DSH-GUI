# DeepSeek Harness 桌面端（exe）

双击桌面 `DeepSeek Harness.lnk`（或 `D:\DSH\dsh-desktop\dist\DeepSeek Harness.exe`）→ 打开**原生桌面窗口**（不是浏览器），
窗口内直接是 DSH agent 界面（形态参考 workbuddy / codex 等桌面 agent 客户端）。

## 技术构成

- **C# (.NET 8) 原生 WinForms 窗口**：无边框**圆角**暗色现代 UI，自定义标题栏（状态点/标题/圆角按钮，可拖动、双击最大化）。
- **WebView2 内嵌引擎**：窗口主体加载 `http://127.0.0.1:3080`（DSH GUI），无需浏览器。
- **右侧「模型用量」侧栏**（5 秒刷新，**圆角卡片**布局）：当前会话、累计 Token（输入/输出/缓存命中率）、
  当前上下文占用（已用/窗口 + **占用分级变色进度条** + 系统/工具/消息构成）、费用估算、DeepSeek 账户余额（60 秒刷新）。
  **可一键显示/隐藏**：标题栏「▤」按钮切换，偏好持久化（隐藏时 WebView2 自动占满窗口）。
- **交互友好**：全部控件带 Tooltip 说明；快捷键 `F5` 重载、`F11` 最大化/还原、`Alt+F4` 关闭；状态条主/次信息分层。
- **GUI 消息尾部本轮用量**：每条 AI 输出后小字显示「本轮 输入 X · 输出 Y · ≈¥Z」（token 与费用，定价与侧栏一致；
  修改自 `@deepseek-ai/dsh-client-ui-conversation` 的客户端模块，**刷新 GUI 页面即生效**；若 npx 缓存重装需重新应用）。
- **文字加强**：侧栏/状态条/标题字号整体提升（+0.5~1pt），小节标题加粗，次要文字对比度提高。
- **内置 DSH 服务**：状态探测（TCP + HTTP 200）、幂等启动、三重身份安全停止、日志落盘、state 持久化。
- **单实例**：重复启动会激活已有窗口。

## 文件与目录

| 路径 | 说明 |
|---|---|
| `dist\DeepSeek Harness.exe` | 发布版可执行文件（点击即用；依赖本机 .NET 8 运行时 + WebView2 Runtime，均已安装） |
| `Program.cs` + `Cli.cs` | 入口（partial）：Main/单实例 + CLI 模式（--status/--start/--stop/--stats/--help） |
| `AppWindow.cs` + `AppWindow.UsageSidebar.cs` | 主窗口（partial）：标题栏/状态条/WebView2/状态机 + 模型用量侧栏（卡片/刷新/显隐） |
| `DshService.cs` / `ModelUsage.cs` / `Ui.cs` | 服务层 / 模型用量数据面 / UI 样式辅助（模块化，可复用） |
| `PROJECT.md` | **项目记忆索引**：结构/约定/数据源/踩坑记录/修改记录/自查清单（先读它再动手） |
| `ci.ps1` | **一键自查总入口**：构建 + verify-exe 回归 + --stats 冒烟（改完必跑） |
| `verify-exe.ps1` | exe 回归（9 项：启动就绪/白名单/HTTP/幂等/停止/无残留） |
| `patch-worker.ps1` | **目录选择器 bug 修复补丁**（见下方「已修复的问题」，npm 重装后重跑一次即可） |
| `gen-icon.ps1` / `app.ico` | 图标生成脚本 / 应用图标 |
| `%LOCALAPPDATA%\DshDesktop\logs` | 运行日志（dsh.log / dsh.out.log / dsh.err.log，>5MB 滚动） |
| `%LOCALAPPDATA%\DshDesktop\state` | 启动器自启动实例记录（停止时身份确认用） |

## 模型用量数据来源与假设

- **Token / 上下文 / 构成**：读取 DSH 服务端落盘的会话投影缓存
  `%USERPROFILE%\.dsh\storages\session_projcache.json`（token-meter 写入的
  `tokenUsage` / `contextPressure` / `contextBreakdown`，含**当前上下文窗口总长**与已用 token）。
  当前会话 = 最近写入（seq 最大）的会话；侧栏同时显示全会话累计 token。
- **费用**：按 DeepSeek 官方定价估算（deepseek-chat，CNY/1M tokens）：输入未命中 ¥2、
  缓存命中 ¥0.5、输出 ¥8；可用环境变量覆盖 `DSH_DESKTOP_PRICE_INPUT` /
  `DSH_DESKTOP_PRICE_CACHE_READ` / `DSH_DESKTOP_PRICE_OUTPUT`。**换模型时价格需自行核对**。
- **余额**：`GET https://api.deepseek.com/user/balance`，API key 读取自
  `%USERPROFILE%\.dsh\.credentials.yaml` 的 `DEEPSEEK_API_KEY`；失败或未配置显示「—」。

## 使用

- 双击快捷方式：窗口打开，DSH 未运行则自动启动；窗口内即 agent 界面。
- 标题栏：左侧状态点 + 标题；右侧「▤」侧栏开关 + 最小化 / 最大化 / 关闭按钮。
- 底部状态条：`启动 DSH` / `停止 DSH` / `重载` / `日志`；左侧主状态 + 右侧详情（URL/PID）。
- 快捷键：`F5` 重载界面 · `F11` 最大化/还原 · `Alt+F4` 关闭。
- 鼠标悬停任意按钮/数据卡片有说明提示。
- 关闭窗口：若 DSH 由本应用启动，会询问是否同时停止；否则不停止。

## 命令行（脚本/自动化）

```
DeepSeek Harness.exe --status            # 输出状态 JSON [--port N]
DeepSeek Harness.exe --stats             # 输出模型用量 JSON（token/上下文/费用/余额）
DeepSeek Harness.exe --start [--wait]    # 启动（幂等）
DeepSeek Harness.exe --stop [--force]    # 安全停止
DeepSeek Harness.exe --help
```

端口默认 3080；环境变量 `DSH_LAUNCHER_PORT` 可覆盖。

> 注意：CLI 输出在管道/重定向环境下不可见时，属 Windows GUI 子系统程序的控制台附加限制，不影响功能；
> 自动化脚本请用 `Start-Process -PassThru` 轮询 `HasExited` 读取退出码（不要用 `Start-Process -Wait`，
> 该方式与 GUI 程序派生子进程的句柄交互会导致 PowerShell 挂起——已实测）。

## 已修复的问题（DSH GUI 侧）

**「添加工作区」取消后再次点击无反应** —— 根因在
`@deepseek-ai/dsh-host-directory-picker-native` 的 `worker.cjs`：其 `post()` 在**每次**发送消息
（含对话框即将显示的 `showing` 通知）后都调用 `process.disconnect()`，关闭了 IPC 通道，
导致用户取消对话框后 worker 的 `done(null)` 消息无法送达 → 服务端 promise 永不 settle →
前端 `armed` 状态永远卡死 → 再次点击「添加工作区」直接无响应。
修复：**仅终态消息（done/error）发送后才允许断开通道**（worker 每次点击时重新 spawn，修复即时生效，无需重启 DSH）。

- 该修复直接改在 npx 缓存的 `worker.cjs` 中；若 npm 重装/清理缓存导致回归，
  运行 `patch-worker.ps1` 一键重新应用（幂等）。

## 一键回归

```
powershell -NoProfile -ExecutionPolicy Bypass -File D:\DSH\dsh-desktop\verify-exe.ps1
```

在隔离端口 3180 上真实启动/停止一次 dsh web，输出 9 项 PASS/FAIL（要求 `%DSH_HOME%` 可写）。

## 重新编译 / 回滚

- 编译：`dotnet publish D:\DSH\dsh-desktop\DshDesktop.csproj -c Release -o dist`
- 回滚：删除桌面快捷方式 + 删除 `D:\DSH\dsh-desktop\`；无注册表、无服务、无全局安装。

## 与 ps1 启动器（dsh-launcher）的关系

- `dsh-launcher\`（PowerShell 版仪表盘 + CLI）是轻量备选与工具集；
- 本 exe 是**主入口**（自包含，不依赖 ps1 脚本，行为语义与 ps1 版一致：幂等、白名单、fail-safe 拒停）。
