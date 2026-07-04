using System.Drawing.Drawing2D;

namespace EchoDeck.App;

/// <summary>Shared rounded-rectangle path helper for the custom-painted controls.</summary>
internal static class RoundRect
{
    public static GraphicsPath Path(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>
/// A panel with a rounded fill and hairline border — used to group content into inset
/// "cards" (e.g. the status banner). Set <see cref="BackColor"/> to the parent's colour so
/// the rounded corners blend in.
/// </summary>
public class RoundedPanel : Panel
{
    public int Radius { get; set; } = 10;
    public Color FillColor { get; set; } = Color.FromArgb(21, 24, 29);
    public Color BorderColor { get; set; } = Color.FromArgb(43, 48, 58);
    public int BorderThickness { get; set; } = 1;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundRect.Path(r, Radius);
        using (var b = new SolidBrush(FillColor)) g.FillPath(b, path);
        if (BorderThickness > 0)
            using (var p = new Pen(BorderColor, BorderThickness)) g.DrawPath(p, path);
    }
}

/// <summary>
/// A small rounded status "chip" — optional leading dot + a line of text — used for the
/// live-status pill and the latency readout beside the master toggle.
/// </summary>
public sealed class Chip : Control
{
    public int Radius { get; set; } = 13;
    public Color Fill { get; set; } = Color.FromArgb(26, 31, 22);
    public Color BorderC { get; set; } = Color.FromArgb(45, 62, 20);
    public Color DotColor { get; set; } = Color.FromArgb(134, 194, 50);
    public bool ShowDot { get; set; } = true;

    private const int DotSize = 8;

    public Chip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        ForeColor = Color.FromArgb(134, 194, 50);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size ts = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font);
        int dot = ShowDot ? DotSize + 8 : 0;
        return new Size(12 + dot + ts.Width + 12, ts.Height + 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = RoundRect.Path(r, Radius))
        {
            using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
            using (var p = new Pen(BorderC)) g.DrawPath(p, path);
        }

        int x = 12, cy = Height / 2;
        if (ShowDot)
        {
            using var db = new SolidBrush(DotColor);
            g.FillEllipse(db, x, cy - DotSize / 2, DotSize, DotSize);
            x += DotSize + 8;
        }
        Size tsz = TextRenderer.MeasureText(Text, Font);
        TextRenderer.DrawText(g, Text, Font, new Point(x, cy - tsz.Height / 2), ForeColor, TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// A dark, flat drop-down. A stock <see cref="ComboBox"/> always paints a light native
/// button; this over-paints the closed control (dark face, hairline border, custom chevron)
/// and owner-draws the list items so the whole thing matches the dark theme. Corners stay
/// square — a native combo can't be rounded.
/// </summary>
public sealed class FlatCombo : ComboBox
{
    private const int WM_PAINT = 0x000F;

    public Color FaceColor { get; set; } = Color.FromArgb(21, 24, 29);
    public Color BorderColor { get; set; } = Color.FromArgb(43, 48, 58);
    public Color ArrowColor { get; set; } = Color.FromArgb(139, 146, 156);
    public Color ItemText { get; set; } = Color.FromArgb(231, 233, 236);
    public Color SelColor { get; set; } = Color.FromArgb(45, 62, 20);

    public FlatCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 28;
        BackColor = FaceColor;
        ForeColor = ItemText;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        Graphics g = e.Graphics;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? SelColor : FaceColor)) g.FillRectangle(b, e.Bounds);
        if (e.Index >= 0)
        {
            string t = GetItemText(Items[e.Index]);
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(g, t, Font, r, ItemText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT) return;

        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        using (var b = new SolidBrush(FaceColor)) g.FillRectangle(b, r);
        using (var p = new Pen(BorderColor)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);

        string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : Text;
        var tr = new Rectangle(11, 0, Math.Max(0, r.Width - 42), r.Height);
        TextRenderer.DrawText(g, text, Font, tr, Enabled ? ItemText : Color.FromArgb(120, 124, 130),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        int ax = r.Width - 24, ay = r.Height / 2;
        using var pen = new Pen(ArrowColor, 1.6f);
        g.DrawLines(pen, new[] { new Point(ax, ay - 3), new Point(ax + 5, ay + 3), new Point(ax + 10, ay - 3) });
    }
}

/// <summary>
/// An uppercase section heading with a trailing hairline rule that fills the remaining
/// width — replaces the plain section labels for clearer grouping.
/// </summary>
public sealed class SectionLabel : Control
{
    public Color LineColor { get; set; } = Color.FromArgb(35, 39, 47);

    public SectionLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        ForeColor = Color.FromArgb(91, 98, 108);
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        Height = 24;
        Dock = DockStyle.Top;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        string t = (Text ?? "").ToUpperInvariant();
        Size ts = TextRenderer.MeasureText(t, Font);
        int cy = Height / 2;
        TextRenderer.DrawText(g, t, Font, new Point(0, cy - ts.Height / 2), ForeColor, TextFormatFlags.NoPrefix);

        int lx = ts.Width + 12;
        if (lx < Width - 2)
            using (var pen = new Pen(LineColor))
                g.DrawLine(pen, lx, cy, Width - 2, cy);
    }
}
