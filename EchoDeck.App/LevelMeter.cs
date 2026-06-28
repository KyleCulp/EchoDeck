namespace EchoDeck.App;

/// <summary>
/// Segmented horizontal VU meter in the NVIDIA Broadcast style: a row of small blocks
/// that light up green (amber near the top, red on clip) to the current level.
/// </summary>
public sealed class LevelMeter : Control
{
    private static readonly Color Bg = Color.FromArgb(32, 32, 34);
    private static readonly Color Off = Color.FromArgb(52, 52, 56);
    private static readonly Color Green = Color.FromArgb(118, 185, 0);
    private static readonly Color Amber = Color.FromArgb(232, 176, 60);
    private static readonly Color Red = Color.FromArgb(232, 72, 72);

    private const int SegW = 9;
    private const int Gap = 4;

    private float _level;
    private bool _clip;

    public LevelMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        Height = 18;
        BackColor = Bg;
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
        using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, ClientRectangle);

        int count = Math.Max(1, (Width + Gap) / (SegW + Gap));
        int lit = (int)Math.Round(_level * count);

        for (int i = 0; i < count; i++)
        {
            var rect = new Rectangle(i * (SegW + Gap), 1, SegW, Height - 2);
            Color c;
            if (i < lit)
                c = _clip ? Red : (i / (float)count) > 0.85f ? Amber : Green;
            else
                c = Off;
            using var b = new SolidBrush(c);
            g.FillRectangle(b, rect);
        }
    }
}
