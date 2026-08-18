using System.Drawing.Drawing2D;

namespace DshDesktop;

/// <summary>UI 样式辅助：圆角 Region、扁平圆角按钮、圆角卡片、Tooltip。</summary>
internal static class Ui
{
    /// <summary>生成圆角矩形 Region（GraphicsPath，无 GDI 句柄泄漏）。</summary>
    public static Region RoundRect(int width, int height, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Math.Max(0, width - d), 0, d, d, 270, 90);
        path.AddArc(Math.Max(0, width - d), Math.Max(0, height - d), d, d, 0, 90);
        path.AddArc(0, Math.Max(0, height - d), d, d, 90, 90);
        path.CloseFigure();
        var region = new Region(path);
        path.Dispose();
        return region;
    }

    /// <summary>把控件替换为圆角 Region（失败静默，保持原矩形）。</summary>
    public static void ApplyRound(Control control, int radius)
    {
        try
        {
            var r = RoundRect(control.Width, control.Height, radius);
            control.Region?.Dispose();
            control.Region = r;
        }
        catch
        {
        }
    }

    /// <summary>扁平圆角按钮（hover 提亮、圆角 8）。</summary>
    public static Button FlatButton(string text, int w, int h, Color back, Color fore, string? tooltip = null)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = new Font("Microsoft YaHei UI", 9),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.18f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Light(back, 0.35f);
        ApplyRound(b, 8);
        if (tooltip != null) b.Tag = tooltip;
        return b;
    }

    /// <summary>圆角卡片容器（圆角 12，默认卡片底色）。</summary>
    public static Panel Card(int x, int y, int w, int h, Color back)
    {
        var p = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = back,
        };
        ApplyRound(p, 12);
        return p;
    }

    /// <summary>统一的小节标题 label（加强：字号 8.5、加粗、对比度更高）。</summary>
    public static Label SectionTitle(string text, int x, int y, int w)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(158, 158, 168),
            Location = new Point(x, y),
            Size = new Size(w, 17),
        };
    }

    /// <summary>为控件挂 Tooltip 说明。</summary>
    public static void Tip(ToolTip tip, Control control, string text)
    {
        tip.SetToolTip(control, text);
    }
}
