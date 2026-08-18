# DeepSeek Harness 启动器（桌面快捷启动 + 配套 UI）

> **主入口提示**：更完整的桌面端见 `D:\DSH\dsh-desktop\` —— **原生 exe 桌面窗口**（C#/.NET 8 + WebView2 内嵌 DSH agent 界面，参考 workbuddy/codex 形态），桌面 `DeepSeek Harness.lnk` 现指向该 exe。本目录为 PowerShell 轻量版（CLI 工具与备选仪表盘）。

为 DSH Web GUI（默认 http://127.0.0.1:3080）提供：
- **桌面快捷方式** `DeepSeek Harness.lnk`：双击即打开配套仪表盘，若 DSH 未运行则自动启动。
- **配套 UI**（WinForms 中文仪表盘）：状态灯 + 启动 / 打开界面 / 停止 / 查看日志 / 退出，2 秒自动刷新。

## 文件结构

| 文件 | 作用 |
|---|---|
| `lib.ps1` | **唯一业务逻辑**（状态检测 / 启动 / 停止 / 日志 / 路径解析），全部脚本复用 |
| `start-dsh.ps1` | CLI：启动（幂等），退出码 0/2/3/4 |
| `stop-dsh.ps1` | CLI：安全停止，退出码 0/5/6 |
| `status.ps1` | CLI：输出状态 JSON |
| `dashboard.ps1` | 配套 UI（WinForms），由快捷方式调用 |
| `smoke.ps1` | 自动冒烟（L1 语法 / L2 双态 / L3 隔离端口生命周期 / L4 快捷方式） |
| `install-shortcut.ps1` | 创建/更新桌面快捷方式（幂等） |
| `logs\` | 运行日志（dsh.log / dsh.out.log / dsh.err.log，>5MB 自动滚动） |
| `state\` | 启动器自启动实例记录（供停止时身份确认） |

## 使用

1. 安装快捷方式（一次性）：`powershell -NoProfile -ExecutionPolicy Bypass -File install-shortcut.ps1`
2. 双击桌面 `DeepSeek Harness.lnk` → 仪表盘打开，未运行时自动启动 DSH。
3. 点「打开界面」进入 DSH GUI；点「停止 DSH」可停止；关闭仪表盘**不会**停止 DSH。
4. 命令行方式：`start-dsh.ps1` / `stop-dsh.ps1` / `status.ps1`（可用 `-Port` 指定端口，默认 3080）。

## 验收单（人工，约 2 分钟）

- [ ] 双击桌面快捷方式 → 仪表盘窗口打开，无报错弹窗
- [ ] DSH 未运行时自动启动；状态灯 30 秒内变绿并显示「运行中 · http://127.0.0.1:3080」
- [ ] 「打开界面」→ 默认浏览器打开 DSH GUI
- [ ] 「停止 DSH」→ 状态灯变灰；刷新浏览器页面确认无法访问
- [ ] 再次双击快捷方式 → 仪表盘显示「运行中」，不会重复启动（幂等）
- [ ] 「查看日志」→ 打开 logs\dsh.log

## 自动冒烟

```
powershell -NoProfile -ExecutionPolicy Bypass -File smoke.ps1
```

L3 会真实启动一个隔离实例（端口 3180），要求 `%DSH_HOME%` 可写；受限环境用 `-SkipLive`。

## 回滚（完全卸载）

1. 删除桌面 `DeepSeek Harness.lnk`
2. 删除 `D:\DSH\dsh-launcher\` 整个目录

无注册表、无服务、无全局安装，删除即卸载。

## 故障排查

| 现象 | 原因与处理 |
|---|---|
| 仪表盘显示「端口被占用（非 DSH）」 | 3080 被其它程序占用；点「停止」会二次确认后强制停止该进程，或自行处理该程序 |
| 启动后一直「启动中…」超时 | 查看 `logs\dsh.err.log`；确认 node 已安装且 `DSH_HOME` 可写 |
| 停止被拒绝（代码 5） | 进程身份无法确认为 DSH（安全兜底）；确认无误后用 `stop-dsh.ps1 -Force` |
| 端口改了 | 用环境变量 `DSH_LAUNCHER_PORT` 覆盖默认端口（仪表盘与 CLI 都遵循） |

## 错误码约定

`0` 成功/已在运行 · `2` 端口被外来进程占用 · `3` 启动就绪超时 · `4` node/dsh 未找到 · `5` 停止被拒绝 · `6` 停止失败
