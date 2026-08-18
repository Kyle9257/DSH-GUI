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
    private readonly Label _costValue = new();
    private readonly Label _balanceValue = new();
    private readonly Panel _contextFill = new();
    private readonly System.Windows.Forms.Timer _usageTimer = new();
    private readonly Button _btnToggleUsage = null!;
    private DateTime _lastBalanceAt = DateTime.MinValue;

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

        // 卡片3：当前上下文
        var contextCard = Ui.Card(16, 234, 248, 104, CardBg);
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
        contextCard.Controls.Add(_contextValue);
        contextCard.Controls.Add(contextBar);
        contextCard.Controls.Add(_breakdownValue);

        // 卡片4：费用（估算）
        var costCard = Ui.Card(16, 348, 248, 74, CardBg);
        costCard.Controls.Add(Ui.SectionTitle("费用（估算）", 14, 10, 220));
        _costValue.Text = "—";
        _costValue.Font = new Font("Consolas", 18, FontStyle.Bold);
        _costValue.ForeColor = TextCost;
        _costValue.Location = new Point(14, 30);
        _costValue.Size = new Size(220, 30);
        _costValue.TextAlign = ContentAlignment.MiddleRight;
        costCard.Controls.Add(_costValue);

        // 卡片5：账户余额
        var balanceCard = Ui.Card(16, 432, 248, 74, CardBg);
        balanceCard.Controls.Add(Ui.SectionTitle("账户余额（DeepSeek）", 14, 10, 220));
        _balanceValue.Text = "—";
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
            Location = new Point(16, 516),
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
            }
            else
            {
                _contextValue.Text = "暂无数据";
                _contextFill.Width = 0;
            }
            _breakdownValue.Text = $"系统 {ModelUsage.FormatTokens(s.CurrentSystemTokens)} · 工具 {ModelUsage.FormatTokens(s.CurrentToolsTokens)} · 消息 {ModelUsage.FormatTokens(s.CurrentMessageTokens)}";

            _costValue.Text = $"¥{s.TotalCostCny:0.00}";
            RefreshBalanceIfStale();
        }
        catch
        {
            // 模型用量刷新失败不影响主界面
        }
    }

    /// <summary>余额每 60 秒最多查询一次（外部 API），失败显示占位。</summary>
    private async void RefreshBalanceIfStale()
    {
        if (DateTime.UtcNow - _lastBalanceAt < TimeSpan.FromSeconds(60)) return;
        _lastBalanceAt = DateTime.UtcNow;
        var balance = await ModelUsage.FetchBalanceAsync();
        _balanceValue.Text = balance ?? "—";
    }

    // ---------- 侧栏显隐与偏好持久化 ----------

    private void SetUsagePanelVisible(bool visible)
    {
        _usagePanel.Visible = visible;
        _btnToggleUsage.BackColor = visible ? Panel : Color.FromArgb(56, 56, 64);
        _btnToggleUsage.ForeColor = visible ? Accent : Dim;
        SaveUiState(visible);
    }

    private sealed class UiStateDoc
    {
        public bool UsagePanelVisible { get; set; } = true;
    }

    private static string UiStateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DshDesktop", "ui-state.json");

    private static bool LoadUiState()
    {
        try
        {
            if (File.Exists(UiStateFile))
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<UiStateDoc>(File.ReadAllText(UiStateFile));
                if (d != null) return d.UsagePanelVisible;
            }
        }
        catch
        {
        }
        return true;
    }

    private static void SaveUiState(bool visible)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UiStateFile)!);
            File.WriteAllText(UiStateFile,
                System.Text.Json.JsonSerializer.Serialize(new UiStateDoc { UsagePanelVisible = visible }));
        }
        catch
        {
        }
    }
}
