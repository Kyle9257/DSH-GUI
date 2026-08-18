namespace DshDesktop;

/// <summary>
/// 主窗口的「模型用量侧栏」部分（partial）：右侧圆角卡片侧栏的构建、
/// 数据刷新与显隐偏好持久化。静态色值与核心控件（_tip 等）来自主文件部分。
/// </summary>
public sealed partial class AppWindow
{
    // 右侧模型用量侧栏
    private readonly Panel _usagePanel = new();
    private readonly Label _sessionValue = new();
    private readonly Label _tokenTotal = new();
    private readonly Label _tokenDetail = new();
    private readonly Label _contextValue = new();
    private readonly Label _breakdownValue = new();
    private readonly Label _contextHint = new();
    private readonly Label _costValue = new();
    private readonly Label _balanceValue = new();
    private readonly Panel _contextFill = new();
    private readonly System.Windows.Forms.Timer _usageTimer = new();
    private readonly Button _btnToggleUsage = null!;
    private Button _btnToggleSensitive = null!; // 在 BuildUsagePanel（方法）中赋值，不能 readonly
    private DateTime _lastBalanceAt = DateTime.MinValue;
    private string? _lastBalance;   // 最近一次余额查询结果（掩码时仍缓存，点击 👁 立即显示）
    private bool _sensitiveVisible; // 费用/余额是否明文显示（默认 false：掩码 *****）

    /// <summary>构建右侧模型用量侧栏（5 张圆角卡片）。</summary>
    private void BuildUsagePanel()
    {
        _usagePanel.Dock = DockStyle.Right;
        _usagePanel.Width = 280;
        _usagePanel.BackColor = Bg;
        _usagePanel.Padding = new Padding(12, 12, 12, 12);

        var title = new Label
        {
            Text = "模型用量",
            Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(14, 12),
            AutoSize = true,
        };
        _usagePanel.Controls.Add(title);

        // 费用/余额显隐切换（👁）：默认掩码 *****，点击显示明文（偏好持久化）
        _btnToggleSensitive = new Button
        {
            Text = "👁",
            Font = new Font("Segoe UI Emoji", 9),
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = Accent,
            Location = new Point(234, 9),
            Size = new Size(32, 26),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        _btnToggleSensitive.FlatAppearance.BorderSize = 0;
        _btnToggleSensitive.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 68);
        Ui.ApplyRound(_btnToggleSensitive, 6);
        Ui.Tip(_tip, _btnToggleSensitive, "费用与余额默认掩码显示（*****），点击 👁 显示 / 隐藏明文");
        _btnToggleSensitive.Click += (_, _) => ToggleSensitiveVisible();
        _usagePanel.Controls.Add(_btnToggleSensitive);

        // 卡片1：当前会话
        var sessionCard = Ui.Card(16, 46, 248, 76, CardBg);
        sessionCard.Controls.Add(Ui.SectionTitle("当前会话", 14, 10, 220));
        _sessionValue.Text = "—";
        _sessionValue.Font = new Font("Microsoft YaHei UI", 9f);
        _sessionValue.ForeColor = TextColor;
        _sessionValue.Location = new Point(14, 30);
        _sessionValue.Size = new Size(220, 34);
        sessionCard.Controls.Add(_sessionValue);

        // 卡片2：Token 用量
        var tokenCard = Ui.Card(16, 132, 248, 92, CardBg);
        tokenCard.Controls.Add(Ui.SectionTitle("Token 用量（累计）", 14, 10, 220));
        _tokenTotal.Text = "—";
        _tokenTotal.Font = new Font("Consolas", 18, FontStyle.Bold);
        _tokenTotal.ForeColor = Accent;
        _tokenTotal.Location = new Point(14, 28);
        _tokenTotal.Size = new Size(220, 26);
        _tokenTotal.TextAlign = ContentAlignment.MiddleRight;
        _tokenDetail.Text = "";
        _tokenDetail.Font = new Font("Consolas", 8.5f);
        _tokenDetail.ForeColor = Dim;
        _tokenDetail.Location = new Point(14, 58);
        _tokenDetail.Size = new Size(220, 26);
        tokenCard.Controls.Add(_tokenTotal);
        tokenCard.Controls.Add(_tokenDetail);

        // 卡片3：当前上下文（含「上下文过高」提示条，默认隐藏）
        var contextCard = Ui.Card(16, 234, 248, 134, CardBg);
        contextCard.Controls.Add(Ui.SectionTitle("当前上下文", 14, 10, 220));
        _contextValue.Text = "—";
        _contextValue.Font = new Font("Consolas", 12, FontStyle.Bold);
        _contextValue.ForeColor = TextColor;
        _contextValue.Location = new Point(14, 28);
        _contextValue.Size = new Size(220, 18);
        _contextValue.TextAlign = ContentAlignment.MiddleRight;
        var contextBar = new Panel
        {
            Location = new Point(14, 52),
            Size = new Size(220, 10),
            BackColor = BarBg,
        };
        Ui.ApplyRound(contextBar, 5);
        _contextFill.BackColor = BarContext;
        _contextFill.Location = new Point(0, 0);
        _contextFill.Size = new Size(0, 10);
        contextBar.Controls.Add(_contextFill);
        _breakdownValue.Text = "";
        _breakdownValue.Font = new Font("Consolas", 8.5f);
        _breakdownValue.ForeColor = Dim;
        _breakdownValue.Location = new Point(14, 70);
        _breakdownValue.Size = new Size(220, 26);
        // 上下文占用过高提示条（>=70% 橙 / >=90% 红，提醒压缩上下文节省 token）
        _contextHint.Text = "";
        _contextHint.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _contextHint.ForeColor = Color.White;
        _contextHint.BackColor = BarWarn;
        _contextHint.Location = new Point(14, 102);
        _contextHint.Size = new Size(220, 22);
        _contextHint.TextAlign = ContentAlignment.MiddleCenter;
        _contextHint.Visible = false;
        Ui.ApplyRound(_contextHint, 6);
        Ui.Tip(_tip, _contextHint, "上下文占用过高：每次请求都会携带完整上下文，压缩会话历史可显著节省 token 费用");
        contextCard.Controls.Add(_contextValue);
        contextCard.Controls.Add(contextBar);
        contextCard.Controls.Add(_breakdownValue);
        contextCard.Controls.Add(_contextHint);

        // 卡片4：费用（估算）
        var costCard = Ui.Card(16, 378, 248, 74, CardBg);
        costCard.Controls.Add(Ui.SectionTitle("费用（估算）", 14, 10, 220));
        _costValue.Text = "*****";
        _costValue.Font = new Font("Consolas", 18, FontStyle.Bold);
        _costValue.ForeColor = TextCost;
        _costValue.Location = new Point(14, 30);
        _costValue.Size = new Size(220, 30);
        _costValue.TextAlign = ContentAlignment.MiddleRight;
        costCard.Controls.Add(_costValue);

        // 卡片5：账户余额
        var balanceCard = Ui.Card(16, 462, 248, 74, CardBg);
        balanceCard.Controls.Add(Ui.SectionTitle("账户余额（DeepSeek）", 14, 10, 220));
        _balanceValue.Text = "*****";
        _balanceValue.Font = new Font("Consolas", 18, FontStyle.Bold);
        _balanceValue.ForeColor = TextBalance;
        _balanceValue.Location = new Point(14, 30);
        _balanceValue.Size = new Size(220, 30);
        _balanceValue.TextAlign = ContentAlignment.MiddleRight;
        balanceCard.Controls.Add(_balanceValue);

        _usagePanel.Controls.Add(sessionCard);
        _usagePanel.Controls.Add(tokenCard);
        _usagePanel.Controls.Add(contextCard);
        _usagePanel.Controls.Add(costCard);
        _usagePanel.Controls.Add(balanceCard);

        var sourceHint = new Label
        {
            Text = "数据来自 DSH 会话投影与 DeepSeek 账户",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(132, 132, 142),
            Location = new Point(16, 540),
            AutoSize = true,
        };
        _usagePanel.Controls.Add(sourceHint);

        Ui.Tip(_tip, _costValue, "费用按 DeepSeek 官方价估算（deepseek-chat）：输入 ¥2/M、缓存命中 ¥0.5/M、输出 ¥8/M，可用环境变量覆盖");
        Ui.Tip(_tip, _balanceValue, "DeepSeek 账户余额，60 秒刷新一次");
        Ui.Tip(_tip, _contextValue, "当前会话上下文占用：已用 token / 上下文窗口总长");
        Ui.Tip(_tip, _tokenTotal, "全会话累计 token（含缓存命中；缓存命中按 1/4 价计费）");
    }

    /// <summary>刷新模型用量（同步读投影缓存 + 节流异步查余额）。</summary>
    private void UpdateModelUsage()
    {
        try
        {
            var s = ModelUsage.Snapshot();
            _sessionValue.Text = s.CurrentSessionTitle ?? "（空白会话）";
            if (s.CurrentWorkspace != null) _sessionValue.Text += $" · {s.CurrentWorkspace}";
            if (s.SessionCount > 1) _sessionValue.Text += $"\n共 {s.SessionCount} 个会话";

            long total = s.TotalUncachedInput + s.TotalCacheRead + s.TotalCacheWrite + s.TotalOutput;
            double cacheHit = (s.TotalUncachedInput + s.TotalCacheRead + s.TotalCacheWrite) == 0
                ? 0
                : 100.0 * s.TotalCacheRead / (s.TotalUncachedInput + s.TotalCacheRead + s.TotalCacheWrite);
            _tokenTotal.Text = ModelUsage.FormatTokens(total);
            _tokenDetail.Text = $"输入 {ModelUsage.FormatTokens(s.TotalUncachedInput + s.TotalCacheWrite)}\n输出 {ModelUsage.FormatTokens(s.TotalOutput)} · 缓存命中 {cacheHit:0}%";

            if (s.CurrentContextWindow > 0)
            {
                double percent = Math.Clamp(100.0 * s.CurrentPressureTokens / s.CurrentContextWindow, 0, 100);
                _contextValue.Text = $"{ModelUsage.FormatTokens(s.CurrentPressureTokens)} / {ModelUsage.FormatTokens(s.CurrentContextWindow)}";
                _contextFill.Width = Math.Max(0, (int)(220 * percent / 100.0));
                // 占用分级变色：<70% 紫 / 70-90% 橙 / >90% 红
                _contextFill.BackColor = percent >= 90 ? BarDanger : percent >= 70 ? BarWarn : BarContext;
                // 上下文过高提示：>=90% 红（建议立即压缩）/ >=70% 橙（建议压缩节省 token），否则隐藏
                if (percent >= 90)
                {
                    _contextHint.Text = $"⚠ 上下文过高（{percent:0}%），建议立即压缩";
                    _contextHint.BackColor = BarDanger;
                    _contextHint.Visible = true;
                }
                else if (percent >= 70)
                {
                    _contextHint.Text = $"⚠ 上下文偏高（{percent:0}%），可压缩节省 Token";
                    _contextHint.BackColor = BarWarn;
                    _contextHint.Visible = true;
                }
                else
                {
                    _contextHint.Visible = false;
                }
            }
            else
            {
                _contextValue.Text = "暂无数据";
                _contextFill.Width = 0;
                _contextHint.Visible = false;
            }
            _breakdownValue.Text = $"系统 {ModelUsage.FormatTokens(s.CurrentSystemTokens)} · 工具 {ModelUsage.FormatTokens(s.CurrentToolsTokens)} · 消息 {ModelUsage.FormatTokens(s.CurrentMessageTokens)}";

            // 费用/余额：默认掩码 *****，点击 👁 后显示明文
            _costValue.Text = _sensitiveVisible ? $"¥{s.TotalCostCny:0.00}" : "*****";
            RefreshBalanceIfStale();
        }
        catch
        {
            // 模型用量刷新失败不影响主界面
        }
    }

    /// <summary>余额每 60 秒最多查询一次（外部 API），失败显示占位；掩码时仍缓存结果。</summary>
    private async void RefreshBalanceIfStale()
    {
        if (DateTime.UtcNow - _lastBalanceAt < TimeSpan.FromSeconds(60)) return;
        _lastBalanceAt = DateTime.UtcNow;
        _lastBalance = await ModelUsage.FetchBalanceAsync();
        _balanceValue.Text = _sensitiveVisible ? (_lastBalance ?? "—") : "*****";
    }

    /// <summary>👁 切换费用/余额明文显示（默认掩码），立即重绘并持久化偏好。</summary>
    private void ToggleSensitiveVisible()
    {
        _sensitiveVisible = !_sensitiveVisible;
        UpdateModelUsage();
        SaveUiState(_usagePanel.Visible, _sensitiveVisible);
    }

    // ---------- 侧栏显隐与偏好持久化 ----------

    private void SetUsagePanelVisible(bool visible)
    {
        _usagePanel.Visible = visible;
        _split.Panel2Collapsed = !visible; // 整列折叠，不残留空白条
        _btnToggleUsage.BackColor = visible ? Panel : Color.FromArgb(56, 56, 64);
        _btnToggleUsage.ForeColor = visible ? Accent : Dim;
        SaveUiState(visible, _sensitiveVisible);
    }

    private sealed class UiStateDoc
    {
        public bool UsagePanelVisible { get; set; } = true;
        public bool SensitiveVisible { get; set; }
    }

    private static string UiStateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DshDesktop", "ui-state.json");

    private static (bool UsagePanelVisible, bool SensitiveVisible) LoadUiState()
    {
        try
        {
            if (File.Exists(UiStateFile))
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<UiStateDoc>(File.ReadAllText(UiStateFile));
                if (d != null) return (d.UsagePanelVisible, d.SensitiveVisible);
            }
        }
        catch
        {
        }
        return (true, false);
    }

    private static void SaveUiState(bool usagePanelVisible, bool sensitiveVisible)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UiStateFile)!);
            File.WriteAllText(UiStateFile,
                System.Text.Json.JsonSerializer.Serialize(new UiStateDoc
                {
                    UsagePanelVisible = usagePanelVisible,
                    SensitiveVisible = sensitiveVisible,
                }));
        }
        catch
        {
        }
    }
}
