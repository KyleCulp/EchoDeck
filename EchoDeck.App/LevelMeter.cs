using System.Drawing.Drawing2D;

namespace EchoDeck.App;

/// <summary>
/// Horizontal VU meter drawn as a rounded inset track with a gradient fill to the current
/// level (green for the processed output, grey for the raw input; red on clip). Faint ticks
/// mark the scale.
/// </summary>
public sealed class LevelMeter : Control
{
    public Color FillStart { get; set; } = Color.FromArgb(95, 143, 36);
    public Color FillEnd { get; set; } = Color.FromArgb(150, 212, 44);
    public Color TrackColor { get; set; } = Color.FromArgb(34, 38, 46);
    public Color BorderColor { get; set; } = Color.FromArgb(43, 48, 58);
    private static readonly Color ClipColor = Color.FromArgb(232, 72, 72);

    private float _level;
    private bool _clip;

    public LevelMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        Height = 16;
        BackColor = Color.FromArgb(32, 32, 34);
    }

    public void SetLevel(float level, bool clip)
    {
        level = Math.Clamp(level, 0f, 1f);
        if (Math.Abs(level - _level) < 0.01f && clip == _clip) return;
        _level = level;
        _clip = clip;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Math.Max(3, Height / 3);
        using var path = RoundRect.Path(r, radius);

        using (var track = new SolidBrush(TrackColor)) g.FillPath(track, path);

        // fill to level (clipped to the rounded track)
        var savedClip = g.Clip;
        g.SetClip(path, CombineMode.Replace);
        int fillW = (int)Math.Round(_level * (r.Width - 2));
        if (fillW > 2)
        {
            var fillRect = new Rectangle(r.X + 1, r.Y + 1, fillW, r.Height - 1);
            if (_clip)
            {
                using var b = new SolidBrush(ClipColor);
                g.FillRectangle(b, fillRect);
            }
            else
            {
                using var b = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, r.Height), FillStart, FillEnd, LinearGradientMode.Horizontal);
                g.FillRectangle(b, fillRect);
            }
        }
        g.Clip = savedClip;

        using (var pen = new Pen(BorderColor)) g.DrawPath(pen, path);
    }
}
