using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;

namespace DshDesktop;

/// <summary>
/// 主窗口：暗色无边框圆角桌面应用（参考 workbuddy/codex 形态）。
/// 结构：自定义标题栏（状态点+标题+圆角按钮） + WebView2（内嵌 DSH GUI） +
/// 右侧模型用量卡片侧栏（见 AppWindow.UsageSidebar.cs，partial）+ 底部状态条。
/// 快捷键：F5 重载、F11 最大化/还原、Alt+F4 关闭。
/// </summary>
public sealed partial class AppWindow : Form
{
    private static readonly Color Bg = Color.FromArgb(26, 26, 31);
    private static readonly Color Panel = Color.FromArgb(38, 38, 44);
    private static readonly Color CardBg = Color.FromArgb(44, 44, 52);
    private static readonly Color TextColor = Color.FromArgb(230, 230, 234);
    private static readonly Color Dim = Color.FromArgb(150, 150, 158);
    private static readonly Color Accent = Color.FromArgb(76, 154, 255);
    private static readonly Color Danger = Color.FromArgb(224, 92, 92);
    private static readonly Color BarBg = Color.FromArgb(30, 30, 36);
    private static readonly Color BarContext = Color.FromArgb(190, 140, 230);
    private static readonly Color BarWarn = Color.FromArgb(230, 150, 60);
    private static readonly Color BarDanger = Color.FromArgb(224, 92, 92);
    private static readonly Color TextCost = Color.FromArgb(222, 170, 86);
    private static readonly Color TextBalance = Color.FromArgb(76, 154, 255);

    private readonly DshService _svc;
    private readonly int _port;
    private readonly WebView2 _web = new();
    private readonly SplitContainer _split = null!;
    private readonly Label _statusDot = new();
    private readonly Label _statusText = new();
    private readonly Label _statusDetail = new();
    private readonly Button _btnStart = null!;
    private readonly Button _btnStop = null!;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolTip _tip = new();

    private DshState _state = null!;
    private bool _starting;
    private int _startWait;
    private bool _navigated;
    private bool _closeAsked;
    private bool _splitterSized;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    public AppWindow(DshService svc)
    {
        _svc = svc;
        _port = DshService.ResolvePort(0);

        Text = "DeepSeek Harness";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(960, 600);
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ---- 标题栏 ----
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Panel };
        var closeBtn = MakeTitleButton("✕", true, "关闭窗口");
        closeBtn.Click += (_, _) => Close();
        var maxBtn = MakeTitleButton("▢", false, "最大化 / 还原（F11）");
        maxBtn.Click += (_, _) => ToggleMaximize();
        var minBtn = MakeTitleButton("—", false, "最小化");
        minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        // 侧栏开关（位于窗口按钮组最左）
        _btnToggleUsage = MakeTitleButton("☰", false, "显示 / 隐藏模型用量侧栏");
        _btnToggleUsage.Click += (_, _) => SetUsagePanelVisible(!_usagePanel.Visible);

        _statusDot.Text = "●";
        _statusDot.Font = new Font("Microsoft YaHei UI", 14);
        _statusDot.ForeColor = Color.Gray;
        _statusDot.AutoSize = true;
        _statusDot.Location = new Point(16, 11);

        var titleLabel = new Label
        {
            Text = "DeepSeek Harness",
            Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(44, 12),
        };
        var versionLabel = new Label
        {
            Text = "桌面版",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Dim,
            AutoSize = true,
            Location = new Point(168, 16),
        };
        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(52, 52, 60) };

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(_statusDot);
        titleBar.Controls.Add(versionLabel);
        titleBar.Controls.Add(_btnToggleUsage);
        titleBar.Controls.Add(minBtn);
        titleBar.Controls.Add(maxBtn);
        titleBar.Controls.Add(closeBtn);
        titleBar.Controls.Add(divider);

        titleBar.MouseDown += OnTitleMouseDown;
        titleLabel.MouseDown += OnTitleMouseDown;
        _statusDot.MouseDown += OnTitleMouseDown;
        titleBar.MouseDoubleClick += (_, _) => ToggleMaximize();

        // ---- 底部状态条 ----
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Panel };
        var statusDivider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(52, 52, 60) };

        _statusText.Text = "检测中…";
        _statusText.Font = new Font("Microsoft YaHei UI", 10f);
        _statusText.ForeColor = TextColor;
        _statusText.Dock = DockStyle.Fill;
        _statusText.TextAlign = ContentAlignment.MiddleLeft;
        _statusText.Padding = new Padding(14, 0, 0, 0);

        _statusDetail.Text = "";
        _statusDetail.Font = new Font("Microsoft YaHei UI", 8.5f);
        _statusDetail.ForeColor = Dim;
        _statusDetail.Dock = DockStyle.Right;
        _statusDetail.AutoSize = true;
        _statusDetail.TextAlign = ContentAlignment.MiddleLeft;
        _statusDetail.Padding = new Padding(0, 0, 10, 0);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 348,
            BackColor = Panel,
            Padding = new Padding(0, 5, 10, 5),
        };
        _btnStart = Ui.FlatButton("启动 DSH", 92, 30, Accent, Color.White, "启动 DSH（未运行时自动启动）");
        _btnStart.Click += (_, _) => AutoStart();
        _btnStop = Ui.FlatButton("停止 DSH", 92, 30, Color.FromArgb(70, 44, 46), TextColor, "安全停止 DSH（外来进程默认拒绝，需二次确认）");
        _btnStop.Click += (_, _) => OnStopClick();
        var btnReload = Ui.FlatButton("重载", 64, 30, Color.FromArgb(50, 50, 58), TextColor, "重新加载界面（F5）");
        btnReload.Click += (_, _) => Reload();
        var btnLogs = Ui.FlatButton("日志", 64, 30, Color.FromArgb(50, 50, 58), TextColor, "打开运行日志");
        btnLogs.Click += (_, _) => OpenLogs();
        btnFlow.Controls.Add(_btnStart);
        btnFlow.Controls.Add(_btnStop);
        btnFlow.Controls.Add(btnReload);
        btnFlow.Controls.Add(btnLogs);

        // Dock 布局：add 顺序 = 布局反序（statusDivider 最外）
        statusBar.Controls.Add(_statusText);
        statusBar.Controls.Add(_statusDetail);
        statusBar.Controls.Add(btnFlow);
        statusBar.Controls.Add(statusDivider);

        // ---- 右侧模型用量侧栏（构建在 AppWindow.UsageSidebar.cs） ----
        BuildUsagePanel();

        // ---- 主体：SplitContainer（Panel1=WebView2 对话区，Panel2=模型用量侧栏）----
        // SplitContainer 统一管理布局：窗口缩放/侧栏收起时绝不与 WebView2 重叠。
        // 重要：构造中【不能】设置 SplitterDistance / Panel1MinSize / Panel2MinSize——
        // 容器未布局（Width 未知）时设置 Panel2MinSize 会触发 SplitterDistance 校验并抛
        // InvalidOperationException（实测崩溃）。全部延后到 OnShown 按安全顺序设置。
        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Bg;
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6,
            BackColor = Bg,
        };
        _split.Panel1.Controls.Add(_web);
        _split.Panel2.BackColor = Bg;
        _split.Panel2.Controls.Add(_usagePanel);

        // Dock 布局：add 顺序 = 布局反序（与 statusBar 内部一致）。
        // 踩坑：若按 [titleBar, statusBar, _split] 正序添加，Fill 的 _split 会先处理占满
        // 整个窗体，titleBar 后处理盖在最上层 → 侧栏顶部（含 👁 按钮）被标题栏遮住。
        // 正确：先加 Fill 的 _split，再加 Bottom 的 statusBar，最后加 Top 的 titleBar。
        Controls.Add(_split);
        Controls.Add(statusBar);
        Controls.Add(titleBar);

        // ---- 定时刷新 ----
        _timer.Interval = 2000;
        _timer.Tick += (_, _) =>
        {
            if (_starting)
            {
                _startWait++;
                if (_startWait >= 45)
                {
                    _starting = false;
                    _statusText.Text = "启动超时（45 秒）";
                    _statusDetail.Text = "请查看日志";
                }
            }
            UpdateStatus();
        };

        _tip.InitialDelay = 600;
        // 应用上次的侧栏可见性与费用/余额掩码偏好（实现见 UsageSidebar 部分）
        var (usageVisible, sensitiveVisible) = LoadUiState();
        _sensitiveVisible = sensitiveVisible;
        SetUsagePanelVisible(usageVisible);
    }

    // ---------- UI 构建辅助 ----------

    private Button MakeTitleButton(string glyph, bool isClose, string tipText)
    {
        var b = new Button
        {
            Text = glyph,
            Width = 44,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = isClose ? Danger : Dim,
            Font = new Font("Segoe UI", 10),
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = isClose ? Color.FromArgb(196, 43, 28) : Color.FromArgb(60, 60, 68);
        b.MouseEnter += (_, _) => { if (!isClose) b.ForeColor = TextColor; };
        b.MouseLeave += (_, _) => { if (!isClose) b.ForeColor = Dim; };
        Ui.Tip(_tip, b, tipText);
        return b;
    }

    private void OnTitleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, 0x2 /*HTCAPTION*/, 0);
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    // ---------- 状态与导航 ----------

    private static Color DotColor(DshState s)
    {
        if (!s.Running) return Color.Gray;
        if (s.Foreign) return Color.OrangeRed;
        if (!s.HttpOk) return Color.Gold;
        return Color.LimeGreen;
    }

    private string StatusMain(DshState s)
    {
        if (!s.Running) return _starting ? $"启动中…（{_startWait} 秒）" : "未运行";
        if (s.Foreign) return $"端口被非 DSH 进程占用（PID {s.Pid}）";
        if (!s.HttpOk) return "运行中（服务尚未就绪）";
        return "运行中";
    }

    private string StatusDetail(DshState s)
    {
        if (!s.Running) return s.Url;
        if (s.Foreign) return s.Url;
        return $"{s.Url} · PID {s.Pid} · 本应用启动: {s.StartedByUs}";
    }

    private void ShowPage(string title, string subtitle, string color)
    {
        string html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
            "body{margin:0;height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;" +
            "background:#1a1a1f;color:#e6e6ea;font-family:'Microsoft YaHei UI',sans-serif;user-select:none;}" +
            ".dot{font-size:44px;color:" + color + ";}" +
            "h1{font-size:20px;margin:10px 0 6px;font-weight:600;}" +
            "p{color:#8f8f97;margin:0;font-size:13px;}" +
            "</style></head><body><div class=\"dot\">●</div>" +
            "<h1>" + System.Net.WebUtility.HtmlEncode(title) + "</h1>" +
            "<p>" + System.Net.WebUtility.HtmlEncode(subtitle) + "</p></body></html>";
        _web.NavigateToString(html);
    }

    private void UpdateStatus()
    {
        try
        {
            var s = _svc.GetState(_port);
            _state = s;
            if (_starting && s.Running) { _starting = false; _startWait = 0; }

            _statusDot.ForeColor = DotColor(s);
            _statusText.Text = StatusMain(s);
            _statusDetail.Text = StatusDetail(s);
            _btnStart.Enabled = !s.Running && !_starting;
            _btnStop.Enabled = s.Running;

            if (s.Running && s.HttpOk && !s.Foreign)
            {
                if (!_navigated)
                {
                    _navigated = true;
                    _web.Source = new Uri(s.Url);
                }
            }
            else if (!s.Running)
            {
                _navigated = false;
                if (_starting)
                    ShowPage("正在启动 DeepSeek Harness", $"启动中… {s.Url}", "#4C9AFF");
                else
                    ShowPage("DeepSeek Harness 未运行", $"点击下方「启动 DSH」开始 · {s.Url}", "#8a8a92");
            }
            else if (s.Foreign)
            {
                _navigated = false;
                ShowPage("端口被占用", $"端口 {s.Port} 被非 DSH 进程（PID {s.Pid}）占用，无法安全启动", "#e05c5c");
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = "状态刷新失败";
            _statusDetail.Text = ex.Message;
        }
    }

    private void AutoStart()
    {
        _starting = true;
        _startWait = 0;
        int rc = _svc.Start(_port);
        if (rc != ExitCodes.Ok)
        {
            var s = _svc.GetState(_port);
            _statusText.Text = s.Foreign ? "无法启动：端口被非 DSH 进程占用" : $"启动失败（代码 {rc}）";
            _statusDetail.Text = "详见日志";
            _starting = false;
        }
        UpdateStatus();
    }

    private void OnStopClick()
    {
        if (_state.Foreign)
        {
            var r = MessageBox.Show(this,
                $"端口 {_state.Port} 被非 DSH 进程（PID {_state.Pid}）占用。\n确认强制停止该进程？",
                "确认停止", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }
        int rc = _svc.Stop(_port, force: _state.Foreign);
        if (rc == ExitCodes.Refused) { _statusText.Text = "已拒绝停止"; _statusDetail.Text = "无法确认进程身份"; }
        else if (rc != ExitCodes.Ok) { _statusText.Text = $"停止失败（代码 {rc}）"; _statusDetail.Text = "详见日志"; }
        UpdateStatus();
    }

    private void Reload()
    {
        _navigated = false;
        var s = _svc.GetState(_port);
        if (s.Running && s.HttpOk && !s.Foreign)
        {
            _web.Source = new Uri(s.Url);
        }
        else
        {
            UpdateStatus();
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_svc.LogDir);
            var logFile = Path.Combine(_svc.LogDir, "dsh.log");
            Process.Start(new ProcessStartInfo(logFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _statusText.Text = "打开日志失败";
            _statusDetail.Text = ex.Message;
        }
    }

    // ---------- 生命周期与快捷键 ----------

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 窗口实际布局完成后设置 SplitContainer 的尺寸约束（侧栏固定 ~280px）。
        // 安全顺序（SplitContainer 的 MinSize setter 会校验当前 SplitterDistance）：
        //   1) 先设 SplitterDistance（MinSize 仍为 0，校验宽松）
        //   2) 再设 Panel1MinSize / Panel2MinSize
        //   3) 最后把 SplitterDistance 调到目标值
        if (!_splitterSized)
        {
            _splitterSized = true;
            try
            {
                // 侧栏若上次处于隐藏（Panel2Collapsed），先解除折叠完成约束设置，再恢复折叠
                bool collapsed = _split.Panel2Collapsed;
                _split.Panel2Collapsed = false;
                _split.SplitterDistance = 420;
                _split.Panel1MinSize = 420;
                _split.Panel2MinSize = 240;
                _split.SplitterDistance = Math.Max(420, _split.Width - 286);
                _split.Panel2Collapsed = collapsed;
            }
            catch
            {
                // 约束设置失败仅影响拖拽范围，不阻塞启动
            }
        }
        try
        {
            await _web.EnsureCoreWebView2Async(null);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        }
        catch (Exception ex)
        {
            _statusText.Text = "WebView2 初始化失败";
            _statusDetail.Text = ex.Message + "（请确认已安装 WebView2 Runtime）";
        }
        UpdateStatus();
        if (!_state.Running && !_state.Foreign) AutoStart();
        _timer.Start();
        UpdateModelUsage();
        _usageTimer.Interval = 5000;
        _usageTimer.Tick += (_, _) => UpdateModelUsage();
        _usageTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _timer.Stop();
        _usageTimer.Stop();
        if (_state != null && _state.StartedByUs && !_closeAsked)
        {
            _closeAsked = true;
            var r = MessageBox.Show(this,
                "DSH 由本应用启动。关闭窗口时是否同时停止 DSH？",
                "关闭确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { e.Cancel = true; _closeAsked = false; return; }
            if (r == DialogResult.Yes) _svc.Stop(_port, force: false);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // 最大化时去掉圆角（避免四角黑边），还原时恢复
        try
        {
            if (WindowState == FormWindowState.Maximized) Region = null;
            else if (WindowState == FormWindowState.Normal) Ui.ApplyRound(this, 12);
        }
        catch
        {
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F5) Reload();
        else if (e.KeyCode == Keys.F11) ToggleMaximize();
    }

    // ---------- 无边框窗口：鼠标边缘拖动缩放 ----------

    private const int WmNcHitTest = 0x84;
    private const int HtClient = 0x1;
    private const int HtLeft = 0xA, HtRight = 0xB, HtTop = 0xC, HtTopLeft = 0xD, HtTopRight = 0xE, HtBottom = 0xF, HtBottomLeft = 0x10, HtBottomRight = 0x11;
    private const int ResizeEdge = 8;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HtClient && WindowState == FormWindowState.Normal)
            {
                var pos = PointToClient(Cursor.Position);
                bool left = pos.X <= ResizeEdge;
                bool right = pos.X >= ClientSize.Width - ResizeEdge;
                bool top = pos.Y <= ResizeEdge;
                bool bottom = pos.Y >= ClientSize.Height - ResizeEdge;
                if (top && left) m.Result = (IntPtr)HtTopLeft;
                else if (top && right) m.Result = (IntPtr)HtTopRight;
                else if (bottom && left) m.Result = (IntPtr)HtBottomLeft;
                else if (bottom && right) m.Result = (IntPtr)HtBottomRight;
                else if (left) m.Result = (IntPtr)HtLeft;
                else if (right) m.Result = (IntPtr)HtRight;
                else if (top) m.Result = (IntPtr)HtTop;
                else if (bottom) m.Result = (IntPtr)HtBottom;
            }
            return;
        }
        base.WndProc(ref m);
    }
}
