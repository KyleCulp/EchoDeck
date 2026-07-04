using System.Diagnostics;
using System.Runtime.InteropServices;
using EchoDeck.App.VirtualMic;
using EchoDeck.Engine;
using EchoDeck.Engine.Interop;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EchoDeck.App;

/// <summary>
/// Main window — NVIDIA-Broadcast-style: EchoDeck processes your mic continuously while
/// open (master "Processing" toggle up top, on by default), with live effect toggles,
/// meters, and a record/compare test panel (Record is a start/stop toggle; Input/Output
/// each have a Play button). Two-column, dark, resizable layout.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(24, 27, 32);
    private static readonly Color Panel = Color.FromArgb(30, 34, 41);
    private static readonly Color Inset = Color.FromArgb(21, 24, 29);
    private static readonly Color TextColor = Color.FromArgb(231, 233, 236);
    private static readonly Color SubText = Color.FromArgb(139, 146, 156);
    private static readonly Color Faint = Color.FromArgb(91, 98, 108);
    private static readonly Color Accent = Color.FromArgb(134, 194, 50);
    private static readonly Color Border = Color.FromArgb(43, 48, 58);
    private static readonly Color Warn = Color.FromArgb(224, 176, 64);
    private static readonly Color Error = Color.FromArgb(232, 96, 96);
    private static readonly Color Ok = Color.FromArgb(132, 190, 80);

    private readonly AudioEngine _engine = new();
    private EngineConfig _config = new();
    private bool _initializing = true;

    private List<DeviceInfo> _allInputs = new();
    private List<DeviceInfo> _allOutputs = new();

    private readonly ToggleSwitch _power = NewToggle("Processing your mic", 13f);

    private readonly ComboBox _mic = NewCombo();
    private readonly ComboBox _reference = NewCombo();
    private readonly ComboBox _output = NewCombo();
    private readonly Button _refresh = new() { Text = "↻  Refresh", AutoSize = true };
    private readonly ToggleSwitch _showDisabled = NewToggle("Show disabled", 10f);
    private readonly ToggleSwitch _showDisconnected = NewToggle("Show disconnected", 10f);

    private readonly ComboBox _profileCombo = NewCombo();
    private readonly Button _profileSave = new() { Text = "＋  Save as…", AutoSize = true };
    private readonly Button _profileUpdate = new() { Text = "Update", AutoSize = true, Enabled = false };
    private readonly Button _profileDelete = new() { Text = "Delete", AutoSize = true, Enabled = false };
    private bool _suppressProfile;
    private ToolStripMenuItem? _trayProfiles;

    private readonly ToggleSwitch _aec = NewToggle("");
    private readonly ToggleSwitch _noise = NewToggle("");
    private readonly ToggleSwitch _echo = NewToggle("");
    private readonly Dictionary<ToggleSwitch, (Label name, Label desc)> _fxLabels = new();

    private readonly LevelMeter _inLevel = new();
    private readonly LevelMeter _outLevel = new();
    private readonly Chip _statusChip = new() { ShowDot = true };
    private readonly Chip _latChip = new() { ShowDot = false };

    private readonly Button _record = new() { Text = "●  Record speech", AutoSize = true, Enabled = false };
    private readonly Button _playIn = new() { Text = "▶  Play", AutoSize = false, Enabled = false };
    private readonly Button _playOut = new() { Text = "▶  Play", AutoSize = false, Enabled = false };
    private readonly Button _save = new() { Text = "⤓  Save recorded samples", AutoSize = true, Enabled = false };
    private readonly WaveformView _inWave = new() { WaveColor = Color.FromArgb(175, 175, 180), FillBack = Color.FromArgb(26, 26, 28) };
    private readonly WaveformView _outWave = new() { WaveColor = Color.FromArgb(150, 212, 44), FillBack = Color.FromArgb(28, 44, 16) };
    private readonly System.Windows.Forms.Timer _recTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 33 };
    private readonly System.Windows.Forms.Timer _deviceApplyTimer = new() { Interval = 400 };
    private bool _recording;
    private int _recElapsed;
    private byte[]? _rawWav, _procWav;
    private IWavePlayer? _player;
    private WaveStream? _playReader;
    private WaveformView? _activeWave;

    private readonly Label _status = new() { Text = "Stopped", AutoSize = true };
    private readonly BannerLine _sdkLine = new(Inset, TextColor, Faint);
    private readonly BannerLine _cableLine = new(Inset, TextColor, Faint);
    private readonly Button _vmicButton = new() { AutoSize = true, Visible = false, Text = "Install virtual mic driver…" };
    private VirtualMicManager _vmic = new(new List<DeviceInfo>(), new List<DeviceInfo>());
    private readonly ToolTip _tips = new();
    private readonly Icon _appIcon = MakeAppIcon();
    private readonly NotifyIcon _tray;

    /// <summary>One status-banner line: a coloured icon, a bold key term, and muted trailing text.</summary>
    private sealed class BannerLine
    {
        public readonly FlowLayoutPanel Row;
        public readonly Label Icon, Bold, Muted;

        public BannerLine(Color inset, Color boldColor, Color mutedColor)
        {
            Icon = new Label { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0, 1, 8, 0), BackColor = inset };
            Bold = new Label { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = boldColor, Margin = new Padding(0, 1, 6, 0), BackColor = inset };
            Muted = new Label { AutoSize = true, Font = new Font("Segoe UI", 10.5f), ForeColor = mutedColor, Margin = new Padding(0, 2, 0, 0), BackColor = inset };
            Row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = inset, Margin = new Padding(0, 2, 0, 2) };
            Row.Controls.Add(Icon);
            Row.Controls.Add(Bold);
            Row.Controls.Add(Muted);
        }

        public void Set(string icon, Color iconColor, string bold, string muted)
        {
            Icon.Text = icon; Icon.ForeColor = iconColor;
            Bold.Text = bold;
            Muted.Text = muted;
        }
    }

    private TableLayoutPanel? _root;
    private bool _suppressPower;
    private bool _suppressDeviceApply;
    private bool _autoStarted;
    private bool _reallyExit;

    public MainForm()
    {
        Text = "EchoDeck";
        Icon = _appIcon;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 11f);

        int longest = TextRenderer.MeasureText("AEC reference — the speaker whose echo to cancel", Font).Width;
        MinimumSize = new Size(Math.Max(900, longest * 2 + 140), 460);
        int screenH = Screen.PrimaryScreen?.WorkingArea.Height ?? 1000;
        ClientSize = new Size(Math.Max(1080, longest * 2 + 180), Math.Min(1000, screenH - 90));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(28, 20, 28, 28),
            BackColor = Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // master row: power toggle (auto-on) + live status pill + latency chip
        ConfigureChips();
        _power.Margin = new Padding(0, 2, 0, 2);
        _power.Anchor = AnchorStyles.Left;
        var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Bg, Margin = new Padding(0), Anchor = AnchorStyles.Right };
        _statusChip.Margin = new Padding(0, 0, 8, 0);
        _latChip.Margin = new Padding(0);
        chips.Controls.Add(_statusChip);
        chips.Controls.Add(_latChip);

        var masterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, BackColor = Bg, Margin = new Padding(0, 0, 0, 4) };
        masterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        masterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        masterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        masterRow.Controls.Add(_power, 0, 0);
        masterRow.Controls.Add(new Label { Text = "", AutoSize = false, Height = 1, BackColor = Bg, Margin = new Padding(0) }, 1, 0);
        masterRow.Controls.Add(chips, 2, 0);
        root.Controls.Add(masterRow);

        // PROFILES bar (named device + effect presets)
        root.Controls.Add(SectionHeader("PROFILES"));
        StyleFlat(_profileSave, Panel, TextColor); _profileSave.Padding = new Padding(12, 8, 12, 8);
        StyleFlat(_profileUpdate, Panel, SubText); _profileUpdate.Padding = new Padding(12, 8, 12, 8); _profileUpdate.Margin = new Padding(8, 0, 0, 0);
        StyleFlat(_profileDelete, Panel, SubText); _profileDelete.Padding = new Padding(12, 8, 12, 8); _profileDelete.Margin = new Padding(8, 0, 0, 0);
        _profileCombo.Dock = DockStyle.Fill; _profileCombo.Margin = new Padding(0, 0, 8, 0);
        var profRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1, BackColor = Bg, Margin = new Padding(0, 2, 0, 0) };
        profRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        profRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profRow.Controls.Add(_profileCombo, 0, 0);
        profRow.Controls.Add(_profileSave, 1, 0);
        profRow.Controls.Add(_profileUpdate, 2, 0);
        profRow.Controls.Add(_profileDelete, 3, 0);
        root.Controls.Add(profRow);

        // STATUS card (full width, inset)
        StyleFlat(_vmicButton, Panel, TextColor);
        _vmicButton.Padding = new Padding(12, 6, 12, 6);
        _vmicButton.Margin = new Padding(0, 8, 0, 2);
        var bannerInner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Inset, Margin = new Padding(0), Dock = DockStyle.Top };
        bannerInner.Controls.Add(_sdkLine.Row);
        bannerInner.Controls.Add(_cableLine.Row);
        bannerInner.Controls.Add(_vmicButton);
        var banner = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Bg, FillColor = Inset, BorderColor = Border, Radius = 10, Padding = new Padding(15, 13, 15, 13), Margin = new Padding(0, 12, 0, 2) };
        banner.Controls.Add(bannerInner);
        root.Controls.Add(banner);

        // two columns
        var left = NewColumn();
        left.Controls.Add(SectionHeader("DEVICES"));
        left.Controls.Add(FieldLabel("Microphone"));
        left.Controls.Add(FullWidth(_mic));
        left.Controls.Add(FieldLabel("AEC reference — the speaker whose echo to cancel"));
        left.Controls.Add(FullWidth(_reference));
        left.Controls.Add(FieldLabel("Output — the virtual mic / cable your apps will listen to"));
        left.Controls.Add(FullWidth(_output));
        StyleFlat(_refresh, Panel, SubText);
        _refresh.Padding = new Padding(14, 8, 14, 8);
        _showDisabled.ForeColor = SubText; _showDisabled.Margin = new Padding(14, 6, 0, 0);
        _showDisconnected.ForeColor = SubText; _showDisconnected.Margin = new Padding(10, 6, 0, 0);
        var devRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0), BackColor = Bg };
        devRow.Controls.Add(_refresh);
        devRow.Controls.Add(_showDisabled);
        devRow.Controls.Add(_showDisconnected);
        left.Controls.Add(devRow);
        left.Controls.Add(SectionHeader("EFFECTS"));
        left.Controls.Add(EffectRow(_aec, "Acoustic Echo Cancellation", "Removes speaker bleed using the reference above"));
        left.Controls.Add(Separator());
        left.Controls.Add(EffectRow(_noise, "Noise Removal", "Suppresses steady background noise"));
        left.Controls.Add(Separator());
        left.Controls.Add(EffectRow(_echo, "Room Echo Removal", "Strips reverb and room tail"));

        _inLevel.BackColor = Bg; _inLevel.FillStart = Color.FromArgb(74, 85, 96); _inLevel.FillEnd = Color.FromArgb(127, 139, 152); _inLevel.TrackColor = Color.FromArgb(34, 38, 46); _inLevel.BorderColor = Border;
        _outLevel.BackColor = Bg; _outLevel.FillStart = Color.FromArgb(95, 143, 36); _outLevel.FillEnd = Color.FromArgb(150, 212, 44); _outLevel.TrackColor = Color.FromArgb(34, 38, 46); _outLevel.BorderColor = Border;

        var right = NewColumn();
        right.Controls.Add(SectionHeader("LEVELS"));
        right.Controls.Add(MeterRow("Input", _inLevel));
        right.Controls.Add(MeterRow("Output", _outLevel));
        right.Controls.Add(SectionHeader("TEST MICROPHONE EFFECTS"));
        right.Controls.Add(FieldLabel("Record a sample, then play Input vs Output to hear the difference."));
        right.Controls.Add(BuildWaves());

        // Action row under the waveforms: Record (left) + Save-as-icon (inline, tooltip on hover).
        StyleFlat(_record, Panel, Error);
        _record.Padding = new Padding(20, 10, 20, 10);
        _record.Font = new Font("Segoe UI", 11.5f);
        _record.Margin = new Padding(0);
        StyleFlat(_save, Panel, SubText);
        _save.AutoSize = false;
        _save.Size = new Size(44, 44);
        _save.Text = "⤓";
        _save.Font = new Font("Segoe UI", 15f);
        _save.TextAlign = ContentAlignment.MiddleCenter;
        _save.Margin = new Padding(12, 0, 0, 0);
        _tips.SetToolTip(_save, "Save recorded samples");
        var testActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Bg, Margin = new Padding(0, 12, 0, 0) };
        testActions.Controls.Add(_record);
        testActions.Controls.Add(_save);
        right.Controls.Add(testActions);

        var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0, 4, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Margin = new Padding(0, 0, 20, 0);
        right.Margin = new Padding(20, 0, 0, 0);
        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);
        root.Controls.Add(body);

        _status.ForeColor = SubText;
        _status.Margin = new Padding(0, 16, 0, 0);
        root.Controls.Add(_status);

        _root = root;
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg };
        host.Controls.Add(root);
        Controls.Add(host);
        WireEvents();
        Resize += (_, _) => UpdateBannerWidths();
        UpdateBannerWidths();

        _tray = new NotifyIcon { Icon = _appIcon, Text = "EchoDeck", Visible = true };
        _trayProfiles = new ToolStripMenuItem("Profiles");
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show EchoDeck", null, (_, _) => ShowFromTray());
        menu.Items.Add(_trayProfiles);
        menu.Items.Add("Pause / resume", null, (_, _) => _power.Checked = !_power.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _reallyExit = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _config = ConfigStore.Load();
        if (!string.IsNullOrWhiteSpace(_config.SdkDir)) _engine.SdkDir = _config.SdkDir!;
        _showDisabled.Checked = _config.ShowDisabledDevices;
        _showDisconnected.Checked = _config.ShowDisconnectedDevices;
        Reload();
    }

    // ── control factories ────────────────────────────────────────────────────────
    private static ComboBox NewCombo() => new FlatCombo
    {
        FaceColor = Inset,
        BorderColor = Border,
        ArrowColor = SubText,
        ItemText = TextColor,
        SelColor = Color.FromArgb(45, 62, 20),
        BackColor = Inset,
        ForeColor = TextColor
    };

    private static ToggleSwitch NewToggle(string text, float fontSize = 11.5f) => new()
    {
        Text = text,
        ForeColor = TextColor,
        BackColor = Bg,
        Font = new Font("Segoe UI", fontSize),
        Margin = new Padding(0, 7, 0, 7)
    };

    private Control SectionHeader(string text) => new SectionLabel
    {
        Text = text,
        ForeColor = Faint,
        LineColor = Border,
        BackColor = Bg,
        Margin = new Padding(0, 22, 0, 10)
    };

    private void ConfigureChips()
    {
        _statusChip.BackColor = Bg;
        _statusChip.Fill = Color.FromArgb(26, 31, 22);
        _statusChip.BorderC = Color.FromArgb(45, 62, 20);
        _statusChip.DotColor = Accent;
        _statusChip.ForeColor = Accent;
        _statusChip.Text = "Paused";

        _latChip.BackColor = Bg;
        _latChip.Fill = Inset;
        _latChip.BorderC = Border;
        _latChip.ForeColor = SubText;
        _latChip.Font = new Font("Segoe UI", 9.75f);
        _latChip.Text = "Latency —";
    }

    private Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SubText,
        Margin = new Padding(0, 10, 0, 4)
    };

    private static ComboBox FullWidth(ComboBox cb)
    {
        cb.Dock = DockStyle.Fill;
        cb.Margin = new Padding(0, 0, 0, 8);
        return cb;
    }

    private static TableLayoutPanel NewColumn()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Bg, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return t;
    }

    private Control EffectRow(ToggleSwitch sw, string name, string desc)
    {
        sw.Text = "";
        sw.Anchor = AnchorStyles.Right;
        sw.Margin = new Padding(10, 2, 0, 0);

        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Bg, Margin = new Padding(0), Anchor = AnchorStyles.Left };
        var n = new Label { Text = name, AutoSize = true, ForeColor = TextColor, Font = new Font("Segoe UI", 11.5f), Margin = new Padding(0, 0, 0, 1) };
        var d = new Label { Text = desc, AutoSize = true, ForeColor = Faint, Font = new Font("Segoe UI", 8.75f), Margin = new Padding(0) };
        text.Controls.Add(n);
        text.Controls.Add(d);
        _fxLabels[sw] = (n, d);

        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0), Padding = new Padding(0, 11, 2, 11) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(text, 0, 0);
        row.Controls.Add(sw, 1, 0);
        return row;
    }

    private Panel Separator() => new() { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0) };

    /// <summary>A green rounded-square app icon with a small waveform, drawn at runtime.</summary>
    private static Icon MakeAppIcon()
    {
        const int s = 64;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var rect = new Rectangle(6, 6, s - 12, s - 12);
            using (var path = RoundRect.Path(rect, 14))
            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb(150, 212, 44), Color.FromArgb(95, 143, 36), 60f))
                g.FillPath(b, path);

            using var bar = new SolidBrush(Color.FromArgb(24, 27, 32));
            int[] hs = { 12, 22, 32, 24, 14 };
            const int bw = 5, gap = 4, cy = s / 2;
            int bx = (s - (hs.Length * bw + (hs.Length - 1) * gap)) / 2;
            for (int i = 0; i < hs.Length; i++)
                g.FillRectangle(bar, bx + i * (bw + gap), cy - hs[i] / 2, bw, hs[i]);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private TableLayoutPanel MeterRow(string label, LevelMeter meter)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 34, Margin = new Padding(0, 8, 0, 8), BackColor = Bg };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = TextColor, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 18, 0) };
        meter.Dock = DockStyle.Fill;
        meter.Margin = new Padding(0, 7, 0, 7);
        t.Controls.Add(lbl, 0, 0);
        t.Controls.Add(meter, 1, 0);
        return t;
    }

    private TableLayoutPanel BuildWaves()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 164, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 14, 0, 0), BackColor = Bg };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // label — sizes to "Output", never truncates
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));   // play button
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // waveform fills the rest
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        StylePlay(_playIn);
        StylePlay(_playOut);
        _inWave.Dock = DockStyle.Fill; _inWave.Margin = new Padding(0, 10, 0, 10);
        _outWave.Dock = DockStyle.Fill; _outWave.Margin = new Padding(0, 10, 0, 10);

        t.Controls.Add(WaveLabel("Input"), 0, 0);
        t.Controls.Add(_playIn, 1, 0);
        t.Controls.Add(_inWave, 2, 0);
        t.Controls.Add(WaveLabel("Output"), 0, 1);
        t.Controls.Add(_playOut, 1, 1);
        t.Controls.Add(_outWave, 2, 1);
        return t;
    }

    private static Label WaveLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextColor,
        Anchor = AnchorStyles.Left, // vertically centred in the row, no wrap
        Margin = new Padding(0, 0, 8, 0)
    };

    private void StylePlay(Button b)
    {
        StyleFlat(b, Panel, TextColor);
        b.AutoSize = false;
        b.Size = new Size(40, 40);
        b.Anchor = AnchorStyles.None; // centre in the cell
        b.Margin = new Padding(0, 0, 12, 0);
        b.Font = new Font("Segoe UI", 10f);
        b.Text = "▶";
        b.TextAlign = ContentAlignment.MiddleCenter;
        _tips.SetToolTip(b, "Play");
    }

    private static void StyleFlat(Button b, Color back, Color fore)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = back;
        b.ForeColor = fore;
        b.FlatAppearance.BorderColor = Border;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(0);
    }

    private void UpdateBannerWidths()
    {
        int w = ClientSize.Width - 120;
        if (w < 160) return;
        _sdkLine.Muted.MaximumSize = new Size(w, 0);
        _cableLine.Muted.MaximumSize = new Size(w, 0);
    }

    // ── events ───────────────────────────────────────────────────────────────────
    private void WireEvents()
    {
        _power.CheckedChanged += (_, _) =>
        {
            if (_suppressPower) return;
            if (_power.Checked) StartEngine();
            else _engine.Stop();
        };

        _refresh.Click += (_, _) => Reload();
        _vmicButton.Click += (_, _) => OnInstallVirtualMic();

        _showDisabled.CheckedChanged += (_, _) => { _config.ShowDisabledDevices = _showDisabled.Checked; SaveConfig(); ApplyDeviceLists(); };
        _showDisconnected.CheckedChanged += (_, _) => { _config.ShowDisconnectedDevices = _showDisconnected.Checked; SaveConfig(); ApplyDeviceLists(); };

        _mic.SelectedIndexChanged += (_, _) => { _engine.InputDeviceId = SelId(_mic); SaveConfig(); ScheduleDeviceApply(); };
        _reference.SelectedIndexChanged += (_, _) => { _engine.FarEndDeviceId = SelId(_reference); SaveConfig(); ScheduleDeviceApply(); };
        _output.SelectedIndexChanged += (_, _) => { _engine.OutputDeviceId = SelId(_output); SaveConfig(); ScheduleDeviceApply(); };

        _aec.CheckedChanged += (_, _) => { _engine.AecEnabled = _aec.Checked; SaveConfig(); };
        _noise.CheckedChanged += (_, _) => { _engine.NoiseRemovalEnabled = _noise.Checked; SaveConfig(); };
        _echo.CheckedChanged += (_, _) => { _engine.RoomEchoRemovalEnabled = _echo.Checked; SaveConfig(); };

        _profileCombo.SelectedIndexChanged += (_, _) => OnProfileSelected();
        _profileSave.Click += (_, _) => SaveAsProfile();
        _profileUpdate.Click += (_, _) => UpdateProfile();
        _profileDelete.Click += (_, _) => DeleteProfile();

        _record.Click += (_, _) => ToggleRecord();
        _playIn.Click += (_, _) => Play(_rawWav, "input", _inWave);
        _playOut.Click += (_, _) => Play(_procWav, "output", _outWave);
        _save.Click += (_, _) => SaveSamples();
        _recTimer.Tick += (_, _) =>
        {
            _recElapsed++;
            _record.Text = $"■  Stop  {_recElapsed}s";
            if (_recElapsed >= 60) EndRecord(); // safety cap
        };
        _playTimer.Tick += (_, _) =>
        {
            if (_player == null || _playReader == null || _activeWave == null) { _playTimer.Stop(); return; }
            float frac = _playReader.Length > 0 ? (float)((double)_playReader.Position / _playReader.Length) : 0f;
            _activeWave.SetPlayhead(frac);
        };

        _deviceApplyTimer.Tick += (_, _) => { _deviceApplyTimer.Stop(); ApplyDeviceChangeLive(); };

        _engine.Status += OnEngineStatus;
        _engine.InputLevel += (_, e) => SetMeter(_inLevel, e);
        _engine.OutputLevel += (_, e) => SetMeter(_outLevel, e);
        _engine.LatencyEstimateMs += (_, ms) => SetLatency(ms);
    }

    /// <summary>
    /// A device dropdown changed. When processing is live, debounce briefly (dropdowns can
    /// fire during list refills) then reconfigure the running engine onto the new device.
    /// </summary>
    private void ScheduleDeviceApply()
    {
        if (_initializing || _suppressDeviceApply) return;
        if (_engine.State is EngineState.Running or EngineState.Starting)
        {
            _deviceApplyTimer.Stop();
            _deviceApplyTimer.Start();
        }
    }

    private void ApplyDeviceChangeLive()
    {
        if (_engine.State is not (EngineState.Running or EngineState.Starting)) return;
        // Restart onto the new device via the tested start path. The NVIDIA models are cached
        // after the first load, so this is a sub-second glitch rather than a full re-init.
        _engine.Stop();
        StartEngine();
    }

    private static string? SelId(ComboBox cb) => (cb.SelectedItem as DeviceInfo)?.Id;

    // ── preflight + devices + config ─────────────────────────────────────────────
    private async void Reload()
    {
        _initializing = true;

        SdkStatus sdk = _engine.ProbeSdk();
        ShowSdkStatus(sdk);
        Gate(_aec, sdk.Aec, "the AEC model");
        Gate(_noise, sdk.Denoiser, "the denoiser model");
        Gate(_echo, sdk.Dereverb, "the dereverb model");

        _aec.Checked = _config.Aec && sdk.Aec;
        _noise.Checked = _config.NoiseRemoval && sdk.Denoiser;
        _echo.Checked = _config.RoomEchoRemoval && sdk.Dereverb;
        _engine.AecEnabled = _aec.Checked;
        _engine.NoiseRemovalEnabled = _noise.Checked;
        _engine.RoomEchoRemovalEnabled = _echo.Checked;

        await RefreshDevicesAsync();

        _initializing = false;
        LoadProfilesCombo();

        // Auto-start processing once, on first load (Broadcast-style "always on").
        if (!_autoStarted)
        {
            _autoStarted = true;
            if (SelId(_mic) != null && SelId(_output) != null)
                _power.Checked = true; // fires StartEngine
        }
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            (List<DeviceInfo> ins, List<DeviceInfo> outs) =
                await Task.Run(() => (DeviceEnumerator.Inputs(), DeviceEnumerator.Outputs()));
            _allInputs = ins;
            _allOutputs = outs;
        }
        catch { _allInputs = new(); _allOutputs = new(); }
        ApplyDeviceLists();
    }

    private void ApplyDeviceLists()
    {
        _suppressDeviceApply = true; // programmatic selection changes must not trigger a live restart
        try
        {
            _vmic = new VirtualMicManager(_allInputs, _allOutputs);
            Fill(_mic, VisibleDevices(_allInputs), _config.InputDeviceId, "Shure");
            Fill(_reference, VisibleDevices(_allOutputs), _config.FarEndDeviceId, "Modi");
            Fill(_output, VisibleDevices(_allOutputs), _config.OutputDeviceId ?? _vmic.Active?.RenderEndpointId, "CABLE In");
            _engine.InputDeviceId = SelId(_mic);
            _engine.FarEndDeviceId = SelId(_reference);
            _engine.OutputDeviceId = SelId(_output);
        }
        finally { _suppressDeviceApply = false; }
        UpdateVirtualMicStatus();
    }

    private List<DeviceInfo> VisibleDevices(List<DeviceInfo> all) => all.Where(d =>
        d.IsActive
        || (d.State == DeviceState.Disabled && _config.ShowDisabledDevices)
        || (d.State == DeviceState.Unplugged && _config.ShowDisconnectedDevices)).ToList();

    private void ShowSdkStatus(SdkStatus sdk)
    {
        if (!sdk.Installed)
        {
            _sdkLine.Set("⚠", Warn, "NVIDIA Audio Effects SDK not found",
                "— effects unavailable; install it from developer.nvidia.com/maxine, then Refresh.");
        }
        else
        {
            var ready = new List<string>();
            if (sdk.Aec) ready.Add("AEC");
            if (sdk.Denoiser) ready.Add("Noise");
            if (sdk.Dereverb) ready.Add("Echo");
            _sdkLine.Set("✓", Ok, $"NVIDIA AFX SDK {sdk.Version ?? "?"}", $"—   {string.Join(" · ", ready)} ready");
        }
    }

    private void UpdateVirtualMicStatus()
    {
        VirtualMicInfo? active = _vmic.Active;
        if (active != null)
        {
            _cableLine.Set("✓", Ok, $"Virtual mic: {active.Name}", $"— set “{active.CaptureEndpointName}” as your mic in other apps.");
            _vmicButton.Visible = false;
        }
        else
        {
            _cableLine.Set("⚠", Warn, "No virtual mic installed", "— other apps have nothing to capture EchoDeck's output from.");
            _vmicButton.Visible = true;
        }
    }

    private async void OnInstallVirtualMic()
    {
        _vmicButton.Enabled = false;
        try
        {
            if (!VirtualMicManager.BundledDriverPresent())
            {
                OpenUrl(VirtualMicManager.VadReleases);
                _status.Text = "Opened the Virtual Audio Driver downloads — install it, then click Refresh.";
                _status.ForeColor = SubText;
                return;
            }
            _status.Text = "Installing the virtual mic driver — accept the UAC prompt…";
            _status.ForeColor = SubText;
            string? result = await VirtualMicManager.InstallVirtualAudioDriverAsync();
            if (result == null) { _status.Text = "Virtual mic driver installed."; _status.ForeColor = Ok; Reload(); }
            else if (result is "no-package" or "no-installer") { OpenUrl(VirtualMicManager.VadReleases); _status.Text = "No bundled installer found — opened the download page."; _status.ForeColor = SubText; }
            else { _status.Text = result; _status.ForeColor = Error; }
        }
        finally { _vmicButton.Enabled = true; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    private void Gate(ToggleSwitch sw, bool available, string what)
    {
        sw.Enabled = available;
        if (_fxLabels.TryGetValue(sw, out var lab))
        {
            lab.name.ForeColor = available ? TextColor : SubText;
            lab.desc.ForeColor = available ? Faint : Color.FromArgb(74, 78, 84);
        }
        _tips.SetToolTip(sw, available ? string.Empty : $"Unavailable — {what} isn't installed in the SDK.");
        if (!available) sw.Checked = false;
    }

    private static void Fill(ComboBox cb, List<DeviceInfo> items, string? preferredId, string fallbackContains)
    {
        cb.BeginUpdate();
        cb.Items.Clear();
        foreach (DeviceInfo d in items) cb.Items.Add(d);
        cb.EndUpdate();

        if (preferredId != null)
            for (int i = 0; i < cb.Items.Count; i++)
                if (((DeviceInfo)cb.Items[i]!).Id == preferredId) { cb.SelectedIndex = i; return; }

        for (int i = 0; i < cb.Items.Count; i++)
        {
            var d = (DeviceInfo)cb.Items[i]!;
            if (d.IsActive && d.FriendlyName.Contains(fallbackContains, StringComparison.OrdinalIgnoreCase)) { cb.SelectedIndex = i; return; }
        }
        for (int i = 0; i < cb.Items.Count; i++)
            if (((DeviceInfo)cb.Items[i]!).IsActive) { cb.SelectedIndex = i; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private void SaveConfig()
    {
        if (_initializing) return;
        _config.InputDeviceId = SelId(_mic);
        _config.FarEndDeviceId = SelId(_reference);
        _config.OutputDeviceId = SelId(_output);
        _config.Aec = _aec.Checked;
        _config.NoiseRemoval = _noise.Checked;
        _config.RoomEchoRemoval = _echo.Checked;
        _config.ShowDisabledDevices = _showDisabled.Checked;
        _config.ShowDisconnectedDevices = _showDisconnected.Checked;
        ConfigStore.Save(_config);
    }

    // ── profiles ─────────────────────────────────────────────────────────────────
    private void LoadProfilesCombo()
    {
        _suppressProfile = true;
        _profileCombo.Items.Clear();
        _profileCombo.Items.Add("(No profile)");
        foreach (AudioProfile p in _config.Profiles) _profileCombo.Items.Add(p);

        int sel = 0;
        if (_config.ActiveProfileId != null)
            for (int i = 1; i < _profileCombo.Items.Count; i++)
                if (((AudioProfile)_profileCombo.Items[i]!).Id == _config.ActiveProfileId) { sel = i; break; }
        _profileCombo.SelectedIndex = sel;
        _suppressProfile = false;

        UpdateProfileButtons();
        RebuildTrayProfiles();
    }

    private void UpdateProfileButtons()
    {
        bool sel = _profileCombo.SelectedItem is AudioProfile;
        _profileUpdate.Enabled = sel;
        _profileDelete.Enabled = sel;
    }

    private void OnProfileSelected()
    {
        UpdateProfileButtons();
        if (_suppressProfile) return;
        if (_profileCombo.SelectedItem is AudioProfile p) ApplyProfile(p);
        else { _config.ActiveProfileId = null; SaveConfig(); RebuildTrayProfiles(); }
    }

    /// <summary>Apply a preset: set devices + effects (suppressing per-change churn), then one live restart.</summary>
    private void ApplyProfile(AudioProfile p)
    {
        _suppressDeviceApply = true;
        bool wasInit = _initializing;
        _initializing = true;
        try
        {
            SelectById(_mic, p.InputDeviceId);
            SelectById(_reference, p.FarEndDeviceId);
            SelectById(_output, p.OutputDeviceId);
            if (_aec.Enabled) _aec.Checked = p.Aec;
            if (_noise.Enabled) _noise.Checked = p.NoiseRemoval;
            if (_echo.Enabled) _echo.Checked = p.RoomEchoRemoval;
            _engine.InputDeviceId = SelId(_mic);
            _engine.FarEndDeviceId = SelId(_reference);
            _engine.OutputDeviceId = SelId(_output);
            _engine.AecEnabled = _aec.Checked;
            _engine.NoiseRemovalEnabled = _noise.Checked;
            _engine.RoomEchoRemovalEnabled = _echo.Checked;
        }
        finally
        {
            _initializing = wasInit;
            _suppressDeviceApply = false;
        }

        _config.ActiveProfileId = p.Id;
        SaveConfig();
        RebuildTrayProfiles();
        if (_engine.State is EngineState.Running or EngineState.Starting) ApplyDeviceChangeLive();
        _status.Text = $"Profile: {p.Name}";
        _status.ForeColor = SubText;
    }

    private static void SelectById(ComboBox cb, string? id)
    {
        if (id == null) return;
        for (int i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is DeviceInfo d && d.Id == id) { cb.SelectedIndex = i; return; }
    }

    private AudioProfile CaptureCurrent(string name, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Name = name,
        InputDeviceId = SelId(_mic),
        FarEndDeviceId = SelId(_reference),
        OutputDeviceId = SelId(_output),
        Aec = _aec.Checked,
        NoiseRemoval = _noise.Checked,
        RoomEchoRemoval = _echo.Checked
    };

    private void SaveAsProfile()
    {
        string? name = AskName("");
        if (name == null) return;
        AudioProfile p = CaptureCurrent(name);
        _config.Profiles.Add(p);
        _config.ActiveProfileId = p.Id;
        SaveConfig();
        LoadProfilesCombo();
        _status.Text = $"Saved profile: {p.Name}";
        _status.ForeColor = SubText;
    }

    private void UpdateProfile()
    {
        if (_profileCombo.SelectedItem is not AudioProfile p) return;
        AudioProfile c = CaptureCurrent(p.Name, p.Id);
        p.InputDeviceId = c.InputDeviceId;
        p.FarEndDeviceId = c.FarEndDeviceId;
        p.OutputDeviceId = c.OutputDeviceId;
        p.Aec = c.Aec;
        p.NoiseRemoval = c.NoiseRemoval;
        p.RoomEchoRemoval = c.RoomEchoRemoval;
        SaveConfig();
        _status.Text = $"Updated profile: {p.Name}";
        _status.ForeColor = SubText;
    }

    private void DeleteProfile()
    {
        if (_profileCombo.SelectedItem is not AudioProfile p) return;
        _config.Profiles.Remove(p);
        if (_config.ActiveProfileId == p.Id) _config.ActiveProfileId = null;
        SaveConfig();
        LoadProfilesCombo();
        _status.Text = $"Deleted profile: {p.Name}";
        _status.ForeColor = SubText;
    }

    private void RebuildTrayProfiles()
    {
        if (_trayProfiles == null) return;
        _trayProfiles.DropDownItems.Clear();
        if (_config.Profiles.Count == 0)
        {
            _trayProfiles.DropDownItems.Add(new ToolStripMenuItem("(none saved)") { Enabled = false });
            return;
        }
        foreach (AudioProfile p in _config.Profiles)
        {
            AudioProfile captured = p;
            var item = new ToolStripMenuItem(p.Name) { Checked = p.Id == _config.ActiveProfileId };
            item.Click += (_, _) => SelectProfileInCombo(captured);
            _trayProfiles.DropDownItems.Add(item);
        }
    }

    private void SelectProfileInCombo(AudioProfile p)
    {
        for (int i = 1; i < _profileCombo.Items.Count; i++)
            if (((AudioProfile)_profileCombo.Items[i]!).Id == p.Id) { _profileCombo.SelectedIndex = i; return; }
    }

    /// <summary>Small themed modal for naming a profile. Returns the trimmed name, or null if cancelled/empty.</summary>
    private string? AskName(string current)
    {
        using var f = new Form
        {
            Text = "Profile name",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 128),
            BackColor = Bg,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 10f)
        };
        var tb = new TextBox { Text = current, Left = 16, Top = 24, Width = 328, BackColor = Inset, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 188, Top = 78, Width = 74, Height = 30 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 270, Top = 78, Width = 74, Height = 30 };
        StyleFlat(ok, Accent, Color.Black);
        StyleFlat(cancel, Panel, TextColor);
        f.Controls.Add(new Label { Text = "Name this profile:", AutoSize = true, Left = 16, Top = 4, ForeColor = SubText });
        f.Controls.Add(tb);
        f.Controls.Add(ok);
        f.Controls.Add(cancel);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK && tb.Text.Trim().Length > 0 ? tb.Text.Trim() : null;
    }

    // ── engine + test ────────────────────────────────────────────────────────────
    private void StartEngine()
    {
        _engine.InputDeviceId = SelId(_mic);
        _engine.FarEndDeviceId = SelId(_reference);
        _engine.OutputDeviceId = SelId(_output);
        _engine.Start();
    }

    private void ToggleRecord()
    {
        if (!_recording) BeginRecord();
        else EndRecord();
    }

    private void BeginRecord()
    {
        if (_engine.State != EngineState.Running)
        {
            _status.Text = "Turn on processing first, then record.";
            _status.ForeColor = Warn;
            return;
        }
        if (!_engine.StartMicTest(TimeSpan.FromSeconds(60))) return;
        StopPlayer();
        _inWave.SetSamples(null);
        _outWave.SetSamples(null);
        _playIn.Enabled = _playOut.Enabled = _save.Enabled = false;
        _recording = true;
        _recElapsed = 0;
        _record.Text = "■  Stop  0s";
        _recTimer.Start();
        _status.Text = "Recording — speak now, then click Stop.";
        _status.ForeColor = SubText;
    }

    private void EndRecord()
    {
        if (!_recording) return;
        _recTimer.Stop();
        _recording = false;
        _record.Text = "●  Record speech";

        TestCaptureResult? r = _engine.StopMicTest();
        if (r != null && r.RawWav.Length > 44)
        {
            _rawWav = r.RawWav;
            _procWav = r.ProcessedWav;
            _inWave.SetSamples(DecodeWav(_rawWav));
            _outWave.SetSamples(DecodeWav(_procWav));
            _playIn.Enabled = _playOut.Enabled = _save.Enabled = true;
            _status.Text = "Recorded — play Input vs Output to compare.";
            _status.ForeColor = SubText;
        }
    }

    private static float[] DecodeWav(byte[] wav)
    {
        using var r = new WaveFileReader(new MemoryStream(wav));
        var samples = new List<float>(1 << 16);
        float[]? frame;
        while ((frame = r.ReadNextSampleFrame()) != null)
            samples.Add(frame.Length > 0 ? frame[0] : 0f);
        return samples.ToArray();
    }

    private void Play(byte[]? wav, string which, WaveformView wave)
    {
        if (wav == null) return;
        StopPlayer();
        try
        {
            _playReader = new WaveFileReader(new MemoryStream(wav));
            _player = new WaveOutEvent();
            _player.PlaybackStopped += (_, _) => OnPlaybackStopped();
            _player.Init(_playReader);
            _activeWave = wave;
            _player.Play();
            _playTimer.Start();
            _status.Text = $"Playing {which}…";
            _status.ForeColor = SubText;
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.ForeColor = Error; }
    }

    private void OnPlaybackStopped()
    {
        if (InvokeRequired) { BeginInvoke((Action)OnPlaybackStopped); return; }
        _playTimer.Stop();
        _activeWave?.SetPlayhead(-1f);
    }

    private void StopPlayer()
    {
        _playTimer.Stop();
        try { _player?.Stop(); } catch { }
        _player?.Dispose(); _player = null;
        _playReader?.Dispose(); _playReader = null;
        _activeWave?.SetPlayhead(-1f);
    }

    private void SaveSamples()
    {
        if (_rawWav == null && _procWav == null) return;
        using var dlg = new SaveFileDialog { Title = "Save recorded samples", Filter = "WAV files (*.wav)|*.wav", FileName = "echodeck_sample.wav" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string dir = Path.GetDirectoryName(dlg.FileName)!;
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (_rawWav != null) File.WriteAllBytes(Path.Combine(dir, name + "_input.wav"), _rawWav);
            if (_procWav != null) File.WriteAllBytes(Path.Combine(dir, name + "_output.wav"), _procWav);
            _status.Text = $"Saved {name}_input.wav and {name}_output.wav.";
            _status.ForeColor = SubText;
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.ForeColor = Error; }
    }

    private void SetLatency(double ms)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke((Action)(() => _latChip.Text = $"Latency ≈ {ms:0} ms")); } catch { } return; }
        _latChip.Text = $"Latency ≈ {ms:0} ms";
    }

    private void UpdateStatusPill(EngineState state)
    {
        switch (state)
        {
            case EngineState.Running:
                var on = new List<string>();
                if (_aec.Checked) on.Add("AEC");
                if (_noise.Checked) on.Add("Noise");
                if (_echo.Checked) on.Add("Echo");
                SetChip(_statusChip, on.Count > 0 ? "Live · " + string.Join(" · ", on) : "Live · passthrough",
                        Accent, Color.FromArgb(26, 31, 22), Color.FromArgb(45, 62, 20), dot: true);
                break;
            case EngineState.Starting:
                SetChip(_statusChip, "Starting…", Warn, Color.FromArgb(38, 32, 18), Color.FromArgb(74, 58, 26), dot: true);
                break;
            case EngineState.Faulted:
                SetChip(_statusChip, "Error", Error, Color.FromArgb(40, 24, 24), Color.FromArgb(74, 36, 36), dot: true);
                break;
            default:
                SetChip(_statusChip, "Paused", SubText, Inset, Border, dot: false);
                break;
        }
    }

    private static void SetChip(Chip c, string text, Color fg, Color fill, Color border, bool dot)
    {
        c.ForeColor = fg;
        c.Fill = fill;
        c.BorderC = border;
        c.DotColor = fg;
        c.ShowDot = dot;
        c.Text = text;
        if (c.AutoSize) c.Size = c.GetPreferredSize(Size.Empty);
        c.Invalidate();
    }

    private void SetPowerVisual(bool on)
    {
        _suppressPower = true;
        _power.Checked = on;
        _suppressPower = false;
    }

    private void OnEngineStatus(object? sender, EngineStatusEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => OnEngineStatus(sender, e))); return; }

        _status.Text = e.Message;
        _status.ForeColor = e.IsError ? Error : SubText;

        bool busy = e.State is EngineState.Running or EngineState.Starting;
        bool running = e.State == EngineState.Running;
        SetPowerVisual(busy);
        UpdateStatusPill(e.State);

        // Device pickers stay live while processing: changing one reconfigures the engine
        // (see ScheduleDeviceApply), so the always-on app can still switch mics/output.

        if (!running && _recording) EndRecord();
        _record.Enabled = running;

        string tip = $"EchoDeck — {e.Message}";
        _tray.Text = tip.Length <= 63 ? tip : "EchoDeck";

        if (!running)
        {
            _inLevel.SetLevel(0, false);
            _outLevel.SetLevel(0, false);
            _latChip.Text = "Latency —";
        }
    }

    private void SetMeter(LevelMeter meter, LevelEventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke((Action)(() => SetMeter(meter, e))); } catch { } return; }
        meter.SetLevel(Math.Min(1f, e.Peak * 1.4f), e.Clip);
    }

    // ── window chrome / tray ─────────────────────────────────────────────────────
    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_root == null) return;
        int contentH = _root.GetPreferredSize(new Size(ClientSize.Width, 0)).Height + 8;
        Rectangle area = Screen.FromControl(this)?.WorkingArea ?? new Rectangle(0, 0, 1280, 1000);
        int targetH = Math.Min(contentH, area.Height - 70);
        ClientSize = new Size(ClientSize.Width, Math.Max(MinimumSize.Height, targetH));
        CenterToScreen();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int on = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win10 2004+/Win11), 19 on older builds
            if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(1000, "EchoDeck", "Still running in the tray. Right-click → Exit to quit.", ToolTipIcon.Info);
            return;
        }

        SaveConfig();
        StopPlayer();
        _engine.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }
}
