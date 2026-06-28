using System.Drawing.Drawing2D;

namespace EchoDeck.App;

/// <summary>
/// A pill-style on/off switch (green when on) with a label — clearly visible on a dark
/// theme, unlike the tiny system <see cref="CheckBox"/> glyph. Drop-in for the effect
/// and device-visibility toggles.
/// </summary>
public sealed class ToggleSwitch : Control
{
    private const int SwitchW = 48;
    private const int SwitchH = 26;

    private bool _checked;

    public event EventHandler? CheckedChanged;

    public Color OnColor { get; set; } = Color.FromArgb(118, 185, 0);
    public Color OffColor { get; set; } = Color.FromArgb(78, 78, 84);

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Cursor = Cursors.Hand;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !_checked;
        base.OnClick(e);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); RelayoutIfAuto(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); RelayoutIfAuto(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    private void RelayoutIfAuto()
    {
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size ts = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font);
        return new Size(SwitchW + 12 + ts.Width + 2, Math.Max(SwitchH, ts.Height) + 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        int cy = Height / 2;
        var pill = new Rectangle(0, cy - SwitchH / 2, SwitchW, SwitchH);
        Color pillColor = !Enabled ? Color.FromArgb(58, 58, 62) : _checked ? OnColor : OffColor;
        using (var b = new SolidBrush(pillColor)) FillPill(g, pill, b);

        int knob = SwitchH - 6;
        int kx = _checked ? pill.Right - knob - 3 : pill.X + 3;
        using (var kb = new SolidBrush(Enabled ? Color.White : Color.FromArgb(150, 150, 154)))
            g.FillEllipse(kb, kx, cy - knob / 2, knob, knob);

        Color textColor = Enabled ? ForeColor : Color.FromArgb(120, 120, 124);
        Size tsz = TextRenderer.MeasureText(Text, Font);
        TextRenderer.DrawText(g, Text, Font, new Point(SwitchW + 12, cy - tsz.Height / 2), textColor, TextFormatFlags.NoPrefix);
    }

    private static void FillPill(Graphics g, Rectangle r, Brush b)
    {
        using var path = new GraphicsPath();
        int d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        g.FillPath(b, path);
    }
}
