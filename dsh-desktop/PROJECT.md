# DSH 桌面端项目记忆（PROJECT.md）

> 本文件是项目的**记忆索引**：结构、约定、已验证事实、踩坑记录、修改记录。
> 修改代码前先读本文件 + 目标文件，避免重复全量扫描与重复踩坑。

## 一、项目结构（文件职责）

### D:\DSH\dsh-desktop（主项目，C#/.NET 8 WinForms）
| 文件 | 职责 | 说明 |
|---|---|---|
| `Program.cs` | 入口（partial）：Main、单实例、AttachConsole | CLI 逻辑在 Cli.cs |
| `Cli.cs` | CLI 模式（partial）：--status/--start/--stop/--stats/--help | 供脚本自动化 |
| `AppWindow.cs` | 主窗口（partial）：标题栏/状态条/WebView2/状态机/生命周期/快捷键 | 约 500 行 |
| `AppWindow.UsageSidebar.cs` | 模型用量侧栏（partial）：卡片构建/刷新/显隐持久化 | 主窗口拆分文件 |
| `DshService.cs` | DSH 服务层：状态探测/幂等启动/三重安全停止/日志/state | 唯一业务服务 |
| `ModelUsage.cs` | 模型用量数据面：projcache 解析/费用估算/余额查询/定价常量 | 数据源见第三节 |
| `Ui.cs` | UI 样式辅助：圆角 Region/圆角按钮/卡片/Tooltip | 静态工具类 |
| `DshDesktop.csproj` | 项目定义（net8.0-windows + WebView2 包 + app.ico） | — |
| `gen-icon.ps1` | 应用图标生成（多尺寸 ICO） | 已生成 app.ico |
| `verify-exe.ps1` | exe 一键回归（9 项，隔离端口 3180） | 自查第一层 |
| `patch-worker.ps1` | DSH 目录选择器 worker bug 补丁（幂等） | npm 重装后重跑 |
| `ci.ps1` | **一键自查总入口**（构建+回归+冒烟） | 改完必跑 |
| `dist\DeepSeek Harness.exe` | 发布版（框架依赖 .NET 8 + WebView2 Runtime） | 桌面快捷方式指向此 |
| `dist\*.old` | 热替换遗留的旧版本文件（运行中进程占用，勿删） | — |

### D:\DSH\dsh-launcher（PowerShell 轻量版，备选工具集）
| 文件 | 职责 |
|---|---|
| `lib.ps1` | 唯一业务逻辑（状态/启动/停止/日志/路径解析），全部脚本复用 |
| `start-dsh.ps1` / `stop-dsh.ps1` / `status.ps1` | CLI 薄封装 |
| `dashboard.ps1` | WinForms 仪表盘（备选 UI） |
| `smoke.ps1` | 冒烟测试（L1-L4） |
| `install-shortcut.ps1` | 桌面快捷方式安装 |

> 主入口是 exe；ps1 版为 CLI 工具/备选。两套实现语义一致（幂等、白名单、fail-safe）。

## 二、关键路径与常量

| 项 | 值 |
|---|---|
| DSH GUI | `http://127.0.0.1:3080`（`dsh web` 启动，默认端口 3080，可用 `DSH_LAUNCHER_PORT` 覆盖） |
| DSH_HOME | `C:\Users\18943\.dsh` |
| 会话投影缓存 | `%USERPROFILE%\.dsh\storages\session_projcache.json`（tokenUsage/contextPressure/contextBreakdown/sessionStats） |
| 工作区 | `%USERPROFILE%\.dsh\storages\workspace.json` |
| API key | `%USERPROFILE%\.dsh\.credentials.yaml` → `DEEPSEEK_API_KEY` |
| DeepSeek 余额 API | `GET https://api.deepseek.com/user/balance`（Bearer key） |
| exe 数据目录 | `%LOCALAPPDATA%\DshDesktop\`（logs/、state\launcher-state.json、ui-state.json） |
| 应用日志 | `%LOCALAPPDATA%\DshDesktop\logs\dsh.log`（>5MB 滚动） |
| dsh bin.js | `%LOCALAPPDATA%\npm-cache\_npx\<hash>\node_modules\@deepseek-ai\dsh\lib\bin.js`（ResolveDshBin 多级回退） |
| node | `C:\Program Files\nodejs\node.exe` |

**错误码约定**（ps1 与 exe 一致）：`0` 成功/已在运行 · `2` 端口被外来进程占用 · `3` 启动就绪超时 · `4` node/dsh 未找到 · `5` 停止被拒绝 · `6` 停止失败

**定价常量**（ModelUsage.cs 与 GUI client.js 必须一致；可用 `DSH_DESKTOP_PRICE_INPUT / _CACHE_READ / _OUTPUT` 覆盖）：
输入未命中 ¥2/M、缓存命中 ¥0.5/M、输出 ¥8/M（CNY，deepseek-chat 假设）

**「当前会话」判定**：projcache 中 seq 最大（最近写入）的会话。

## 三、外部修改点（npx 缓存内，npm 重装会丢失）

| 文件 | 修改内容 | 生效方式 |
|---|---|---|
| `@deepseek-ai\dsh-host-directory-picker-native\lib\worker.cjs` | 修复 post() 的 IPC disconnect bug（仅终态消息才断开） | 每次点击重新 spawn，即时生效；回归用 `patch-worker.ps1` 重新应用 |
| `@deepseek-ai\dsh-client-ui-conversation\lib\client.js` | 消息尾部「本轮 输入/输出/≈¥」小字（usage 透传 + 定价常量 + zh/en 字典） | 刷新 GUI 页面生效 |

## 四、已验证事实（环境）

- node v25.9.0、npm 11.12.1、.NET SDK 9.0.308、.NET 8/9 运行时已装、WebView2 Runtime 已装（151.x）
- PowerShell 5.1（powershell.exe）与 7（pwsh）并存；.ps1 必须 **UTF-8 带 BOM**（5.1 按 ANSI 解析否则中文乱码破坏字符串）
- 本机 GUI 的 3080 实例由 harness 启动（命令行含 `bin.js web`）；**不要杀它**（会断当前会话）
- 用户桌面 exe 实例时常在运行（跑旧版）；热替换用「rename 旧文件为 .old + 复制新版」，运行中进程不受影响，重启后生效
- DeepSeek 账户余额约 ¥12-13（持续消耗中）

## 五、踩坑记录（务必先读，避免重复踩坑）

1. **PowerShell 5.1 解析 .ps1 按 ANSI**：中文必须 UTF-8 BOM，否则乱码吞字符串结束引号 → 解析失败。
2. **`$PID` 是只读自动变量**：C#/ps1 里不能用作变量名（会静默失败或报错）。
3. **`$args` 是 PowerShell 保留变量**：不能用作函数参数名。
4. **`Start-Process -Wait` 与 GUI 程序派生进程**：句柄交互导致 PowerShell 挂起。CLI 验证用 `&` 或 `Start-Process -PassThru` + 轮询 `HasExited`。
5. **cmd /c 管道 + GUI exe 拉起的 node**：node 继承管道句柄 → 读取方等不到 EOF 挂起；用 `>nul 2>nul` 或看门狗轮询。
6. **DPI 缩放 + 手动坐标 + Region 的标题栏按钮**：`Anchor+Location` 被 AutoScaleMode.Dpi 二次缩放、Region 不随缩放 → 按钮"消失"。标题按钮必须用 **Dock 布局、不用 Region**。
7. **C# 逐字字符串 `@"..."` 里的 `"` 必须写 `""`**：正则含引号时易犯。
8. **Get-NetTCPConnection 在部分沙箱环境不可用**：端口探测用 TcpClient 连接（Test-PortListening），PID 用 netstat 解析，均带 fallback。
9. **netstat 匹配端口会误报 TIME_WAIT**：检查监听必须 `-State Listen` 或匹配 `LISTENING`。
10. **worker.cjs 的 disconnect bug**：见第三节，`showing` 后 disconnect 会吞掉 `done` 消息。
11. **dist 目录里的 `DeepSeek Harness.exe.WebView2\` 是运行中实例的 WebView2 数据**：grep/扫描时排除，勿删。
12. **GUI 客户端模块运行时加载**：改 `client.js` 刷新页面即生效，无需重启 dsh web。
13. **ConvertFrom-Json（PS 5.1）对深层嵌套有限制**：用 -Depth 或直接读文件。

## 六、修改记录

| 日期 | 修改 |
|---|---|
| 2026-08-17 | 建立 dsh-launcher（ps1 启动器/仪表盘/冒烟）；修 worker.cjs disconnect bug |
| 2026-08-17 | 建立 dsh-desktop：C# 原生 exe + WebView2 内嵌 GUI；CLI 模式；verify-exe 回归 |
| 2026-08-17 | 模型用量侧栏（token/上下文/费用/余额）；--stats；余额 API |
| 2026-08-17 | UI 样式优化（圆角/卡片/Tooltip/快捷键/文字加强）；GUI 消息尾部本轮用量 |
| 2026-08-18 | 侧栏可隐藏（▤ 按钮 + 持久化）；修复标题按钮 DPI 消失 |
| 2026-08-18 | 项目规范化：partial 拆分（UsageSidebar/Cli）、PROJECT.md 记忆、ci.ps1 自查 |
| 2026-08-18 | GUI 左侧底部「🌐 社区」入口（sidebar.footer.action）；exe 布局改 SplitContainer（侧栏收起不遮挡）；git 建立并推送 GitHub |
| 2026-08-18 | 右侧侧栏上下文卡片新增「上下文过高」提示条（≥70% 橙 / ≥90% 红，提醒压缩上下文节省 token）；git 提交规范改为一律中文 |
| 2026-08-18 | 费用/余额默认掩码（*****）+ 👁 切换明文（偏好持久化）；侧栏隐藏改整列折叠（Panel2Collapsed，不残留空白） |
| 2026-08-18 | 👁 按钮改自绘图标（踩坑：WinForms GDI 无法渲染彩色 emoji，👁 显示为空白；用 Paint 画眼睛，掩码=空心瞳孔/明文=实心瞳孔） |

## 七、自查清单（每次修改后执行）

1. `dotnet build` → 0 错误
2. `ci.ps1`（构建 + verify-exe 9/9 + --stats 冒烟）→ 全绿
3. 涉及 GUI 前端（client.js/worker.cjs）：`node --check` + 刷新页面人工确认
4. 涉及 UI：重启 exe 目检（用户侧）
5. 修改完成后 `git add -A && git commit`（自查全绿才提交），需要时 `git push`

## 八、git 使用

- 仓库根：`D:\DSH`（分支 `main`）；远程：`origin = https://github.com/Kyle9257/DSH-GUI`
- 已提交：初始提交 f2a1dde（24 文件）；`.gitignore` 排除 bin/obj/dist/state/logs/WebView2 数据/ui-state.json 等
- 提交规范：自查（ci.ps1）全绿 → `git add -A` → `git commit -m "说明（中文）"` → `git push`
- **提交语言规则（强制）：每次 git 提交，`-m` 修改项说明一律用中文写**（如「新增上下文过高提示条」「修复侧栏重叠」），不得用英文或中英混杂
- 凭据：git 凭据管理器已存 GitHub 凭据，push 静默认证
