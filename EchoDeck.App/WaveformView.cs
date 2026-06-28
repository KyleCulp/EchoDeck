namespace EchoDeck.App;

/// <summary>
/// Renders a recorded mono signal as a waveform (peak-per-pixel), with an optional
/// playback cursor. Used for the Broadcast-style Input/Output comparison rows.
/// </summary>
public sealed class WaveformView : Control
{
    private float[]? _samples;
    private float[] _peaks = Array.Empty<float>();
    private float _playhead = -1f; // 0..1, or <0 to hide

    public Color WaveColor { get; set; } = Color.FromArgb(170, 170, 175);
    public Color FillBack { get; set; } = Color.FromArgb(26, 26, 28);

    public WaveformView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        Height = 56;
    }

    public void SetSamples(float[]? samples)
    {
        _samples = samples;
        _playhead = -1f;
        Recompute();
        Invalidate();
    }

    public void SetPlayhead(float fraction)
    {
        _playhead = fraction;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Recompute();
        Invalidate();
    }

    private void Recompute()
    {
        int w = Math.Max(1, Width);
        if (_samples is null || _samples.Length == 0) { _peaks = Array.Empty<float>(); return; }

        var peaks = new float[w];
        int n = _samples.Length;
        for (int x = 0; x < w; x++)
        {
            int start = (int)((long)x * n / w);
            int end = (int)((long)(x + 1) * n / w);
            if (end <= start) end = start + 1;
            if (end > n) end = n;
            float p = 0f;
            for (int i = start; i < end; i++)
            {
                float a = Math.Abs(_samples[i]);
                if (a > p) p = a;
            }
            peaks[x] = p;
        }
        _peaks = peaks;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var bg = new SolidBrush(FillBack)) g.FillRectangle(bg, ClientRectangle);

        int mid = Height / 2;
        int amp = Height / 2 - 3;

        using (var baseline = new Pen(Color.FromArgb(60, WaveColor)))
            g.DrawLine(baseline, 0, mid, Width, mid);

        using (var pen = new Pen(WaveColor))
        {
            for (int x = 0; x < _peaks.Length && x < Width; x++)
            {
                int h = (int)(_peaks[x] * amp);
                if (h > 0) g.DrawLine(pen, x, mid - h, x, mid + h);
            }
        }

        if (_playhead >= 0f)
        {
            int px = (int)(_playhead * Width);
            using var ph = new Pen(Color.FromArgb(220, 255, 255, 255));
            g.DrawLine(ph, px, 0, px, Height);
        }
    }
}
