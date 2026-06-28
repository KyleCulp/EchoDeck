using System.Runtime.InteropServices;
using EchoDeck.Engine;
using EchoDeck.Engine.Interop;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EchoDeck.App;

/// <summary>
/// Main window: preflight status, device pickers (with show/hide for disabled &amp;
/// disconnected), live effect toggles, segmented meters, latency readout, a
/// Broadcast-style record/compare test panel, start/stop, tray, persisted settings.
/// Resizable + scrollable so content never clips at any size/DPI; dark theme.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(32, 32, 34);
    private static readonly Color Panel = Color.FromArgb(45, 45, 48);
    private static readonly Color TextColor = Color.FromArgb(230, 230, 232);
    private static readonly Color SubText = Color.FromArgb(152, 152, 158);
    private static readonly Color Accent = Color.FromArgb(118, 185, 0);
    private static readonly Color Border = Color.FromArgb(64, 64, 68);
    private static readonly Color Warn = Color.FromArgb(224, 176, 64);
    private static readonly Color Error = Color.FromArgb(232, 96, 96);
    private static readonly Color Ok = Color.FromArgb(132, 190, 80);

    private readonly AudioEngine _engine = new();
    private EngineConfig _config = new();
    private bool _initializing = true;

    private List<DeviceInfo> _allInputs = new();
    private List<DeviceInfo> _allOutputs = new();

    private readonly ComboBox _mic = NewCombo();
    private readonly ComboBox _reference = NewCombo();
    private readonly ComboBox _output = NewCombo();
    private readonly Button _refresh = new() { Text = "↻  Refresh", AutoSize = true };
    private readonly ToggleSwitch _showDisabled = NewToggle("Show disabled", 10f);
    private readonly ToggleSwitch _showDisconnected = NewToggle("Show disconnected", 10f);

    private readonly ToggleSwitch _aec = NewToggle("Acoustic Echo Cancellation");
    private readonly ToggleSwitch _noise = NewToggle("Noise Removal");
    private readonly ToggleSwitch _echo = NewToggle("Room Echo Removal");

    private readonly LevelMeter _inLevel = new();
    private readonly LevelMeter _outLevel = new();
    private readonly Label _latency = new() { AutoSize = true, Text = "Latency: —", ForeColor = Color.FromArgb(152, 152, 158), Margin = new Padding(0, 10, 0, 0) };

    private readonly Button _record = new() { Text = "●  Record speech", AutoSize = true, Enabled = false };
    private readonly Button _playIn = new() { Text = "▶", AutoSize = false, Enabled = false };
    private readonly Button _playOut = new() { Text = "▶", AutoSize = false, Enabled = false };
    private readonly Button _save = new() { Text = "⤓  Save recorded samples", AutoSize = true, Enabled = false };
    private readonly WaveformView _inWave = new() { WaveColor = Color.FromArgb(175, 175, 180), FillBack = Color.FromArgb(26, 26, 28) };
    private readonly WaveformView _outWave = new() { WaveColor = Color.FromArgb(150, 212, 44), FillBack = Color.FromArgb(28, 44, 16) };
    private readonly System.Windows.Forms.Timer _recTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 33 };
    private int _recCountdown;
    private byte[]? _rawWav, _procWav;
    private IWavePlayer? _player;
    private WaveStream? _playReader;
    private WaveformView? _activeWave;

    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", Enabled = false, AutoSize = true };
    private readonly Label _status = new() { Text = "Stopped", AutoSize = true };
    private readonly Label _sdkLabel = new() { AutoSize = true };
    private readonly Label _cableLabel = new() { AutoSize = true };
    private readonly ToolTip _tips = new();
    private readonly NotifyIcon _tray;

    private TableLayoutPanel? _root;
    private bool _reallyExit;

    public MainForm()
    {
        Text = "EchoDeck";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 11f);

        int longest = TextRenderer.MeasureText("AEC reference — the speaker whose echo to cancel", Font).Width;
        MinimumSize = new Size(Math.Max(560, longest + 130), 480);
        int screenH = Screen.PrimaryScreen?.WorkingArea.Height ?? 1000;
        ClientSize = new Size(Math.Max(860, longest + 180), Math.Min(1000, screenH - 90));

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(28, 22, 28, 28),
            BackColor = Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // STATUS
        root.Controls.Add(SectionHeader("STATUS"));
        var banner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Bg, Margin = new Padding(0) };
        banner.Controls.Add(_sdkLabel);
        banner.Controls.Add(_cableLabel);
        root.Controls.Add(banner);

        // DEVICES
        root.Controls.Add(SectionHeader("DEVICES"));
        root.Controls.Add(FieldLabel("Microphone"));
        root.Controls.Add(FullWidth(_mic));
        root.Controls.Add(FieldLabel("AEC reference — the speaker whose echo to cancel"));
        root.Controls.Add(FullWidth(_reference));
        root.Controls.Add(FieldLabel("Output — the virtual mic / cable your apps will listen to"));
        root.Controls.Add(FullWidth(_output));

        StyleFlat(_refresh, Panel, SubText);
        _refresh.Padding = new Padding(14, 8, 14, 8);
        _showDisabled.ForeColor = SubText; _showDisabled.Margin = new Padding(18, 6, 0, 0);
        _showDisconnected.ForeColor = SubText; _showDisconnected.Margin = new Padding(10, 6, 0, 0);
        var devRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0), BackColor = Bg };
        devRow.Controls.Add(_refresh);
        devRow.Controls.Add(_showDisabled);
        devRow.Controls.Add(_showDisconnected);
        root.Controls.Add(devRow);

        // EFFECTS
        root.Controls.Add(SectionHeader("EFFECTS"));
        root.Controls.Add(_aec);
        root.Controls.Add(_noise);
        root.Controls.Add(_echo);

        // LEVELS
        root.Controls.Add(SectionHeader("LEVELS"));
        root.Controls.Add(MeterRow("Input", _inLevel));
        root.Controls.Add(MeterRow("Output", _outLevel));
        root.Controls.Add(_latency);

        // TEST
        root.Controls.Add(SectionHeader("TEST MICROPHONE EFFECTS"));
        root.Controls.Add(FieldLabel("Record a sample while running, then play Input vs Output to compare."));
        StyleFlat(_record, Panel, Error);
        _record.Padding = new Padding(18, 10, 18, 10);
        _record.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(_record);
        root.Controls.Add(BuildWaves());
        StyleFlat(_save, Bg, SubText);
        _save.FlatAppearance.BorderColor = Bg;
        _save.Margin = new Padding(0, 10, 0, 0);
        root.Controls.Add(_save);

        // CONTROLS
        root.Controls.Add(ButtonRow());
        _status.ForeColor = SubText;
        _status.Margin = new Padding(0, 16, 0, 0);
        root.Controls.Add(_status);

        _root = root;
        host.Controls.Add(root);
        Controls.Add(host);
        WireEvents();
        Resize += (_, _) => UpdateBannerWidths();
        UpdateBannerWidths();

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "EchoDeck", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show EchoDeck", null, (_, _) => ShowFromTray());
        menu.Items.Add("Start", null, (_, _) => StartEngine());
        menu.Items.Add("Stop", null, (_, _) => _engine.Stop());
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
    private static ComboBox NewCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Panel,
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

    private Label SectionHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SubText,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Margin = new Padding(0, 24, 0, 10)
    };

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

    private TableLayoutPanel MeterRow(string label, LevelMeter meter)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 34, Margin = new Padding(0, 8, 0, 8), BackColor = Bg };
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
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 10, 0, 0), BackColor = Bg };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));

        StylePlay(_playIn);
        StylePlay(_playOut);
        _inWave.Dock = DockStyle.Fill; _inWave.Margin = new Padding(0, 11, 0, 11);
        _outWave.Dock = DockStyle.Fill; _outWave.Margin = new Padding(0, 11, 0, 11);

        t.Controls.Add(WaveLabel("Input audio"), 0, 0);
        t.Controls.Add(_playIn, 1, 0);
        t.Controls.Add(_inWave, 2, 0);
        t.Controls.Add(WaveLabel("Output audio"), 0, 1);
        t.Controls.Add(_playOut, 1, 1);
        t.Controls.Add(_outWave, 2, 1);
        return t;
    }

    private static Label WaveLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = TextColor,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private void StylePlay(Button b)
    {
        StyleFlat(b, Panel, TextColor);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(0, 23, 8, 23);
        b.Font = new Font("Segoe UI", 11f);
    }

    private FlowLayoutPanel ButtonRow()
    {
        StyleFlat(_start, Accent, Color.Black);
        _start.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _start.Padding = new Padding(46, 12, 46, 12);
        StyleFlat(_stop, Panel, TextColor);
        _stop.Font = new Font("Segoe UI", 12f);
        _stop.Padding = new Padding(50, 12, 50, 12);
        _stop.Margin = new Padding(16, 0, 0, 0);
        return new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 22, 0, 0),
            BackColor = Bg,
            Controls = { _start, _stop }
        };
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
        int w = ClientSize.Width - 80;
        if (w < 120) return;
        _sdkLabel.MaximumSize = new Size(w, 0);
        _cableLabel.MaximumSize = new Size(w, 0);
    }

    // ── events ───────────────────────────────────────────────────────────────────
    private void WireEvents()
    {
        _refresh.Click += (_, _) => Reload();
        _start.Click += (_, _) => StartEngine();
        _stop.Click += (_, _) => _engine.Stop();

        _showDisabled.CheckedChanged += (_, _) => { _config.ShowDisabledDevices = _showDisabled.Checked; SaveConfig(); ApplyDeviceLists(); };
        _showDisconnected.CheckedChanged += (_, _) => { _config.ShowDisconnectedDevices = _showDisconnected.Checked; SaveConfig(); ApplyDeviceLists(); };

        _mic.SelectedIndexChanged += (_, _) => { _engine.InputDeviceId = SelId(_mic); SaveConfig(); };
        _reference.SelectedIndexChanged += (_, _) => { _engine.FarEndDeviceId = SelId(_reference); SaveConfig(); };
        _output.SelectedIndexChanged += (_, _) => { _engine.OutputDeviceId = SelId(_output); SaveConfig(); };

        _aec.CheckedChanged += (_, _) => { _engine.AecEnabled = _aec.Checked; SaveConfig(); };
        _noise.CheckedChanged += (_, _) => { _engine.NoiseRemovalEnabled = _noise.Checked; SaveConfig(); };
        _echo.CheckedChanged += (_, _) => { _engine.RoomEchoRemovalEnabled = _echo.Checked; SaveConfig(); };

        _record.Click += async (_, _) => await RecordTest();
        _playIn.Click += (_, _) => Play(_rawWav, "input", _inWave);
        _playOut.Click += (_, _) => Play(_procWav, "output", _outWave);
        _save.Click += (_, _) => SaveSamples();
        _recTimer.Tick += (_, _) =>
        {
            _recCountdown--;
            _record.Text = _recCountdown > 0 ? $"●  Recording… {_recCountdown}" : "●  Recording…";
            if (_recCountdown <= 0) _recTimer.Stop();
        };
        _playTimer.Tick += (_, _) =>
        {
            if (_player == null || _playReader == null || _activeWave == null) { _playTimer.Stop(); return; }
            float frac = _playReader.Length > 0 ? (float)((double)_playReader.Position / _playReader.Length) : 0f;
            _activeWave.SetPlayhead(frac);
        };

        _engine.Status += OnEngineStatus;
        _engine.InputLevel += (_, e) => SetMeter(_inLevel, e);
        _engine.OutputLevel += (_, e) => SetMeter(_outLevel, e);
        _engine.LatencyEstimateMs += (_, ms) => SetLatency(ms);
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

        await RefreshDevicesAsync(); // enumerate off the UI thread, then fill

        _initializing = false;
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
        Fill(_mic, VisibleDevices(_allInputs), _config.InputDeviceId, "Shure");
        Fill(_reference, VisibleDevices(_allOutputs), _config.FarEndDeviceId, "Modi");
        Fill(_output, VisibleDevices(_allOutputs), _config.OutputDeviceId, "CABLE In");
        _engine.InputDeviceId = SelId(_mic);
        _engine.FarEndDeviceId = SelId(_reference);
        _engine.OutputDeviceId = SelId(_output);
        UpdateCableStatus();
    }

    private List<DeviceInfo> VisibleDevices(List<DeviceInfo> all) => all.Where(d =>
        d.IsActive
        || (d.State == DeviceState.Disabled && _config.ShowDisabledDevices)
        || (d.State == DeviceState.Unplugged && _config.ShowDisconnectedDevices)).ToList();

    private void ShowSdkStatus(SdkStatus sdk)
    {
        if (!sdk.Installed)
        {
            _sdkLabel.ForeColor = Error;
            _sdkLabel.Text = "⚠  NVIDIA Audio Effects SDK not found — effects unavailable.\n     Install it from developer.nvidia.com/maxine, then click Refresh.";
        }
        else
        {
            var ready = new List<string>();
            if (sdk.Aec) ready.Add("AEC");
            if (sdk.Denoiser) ready.Add("Noise");
            if (sdk.Dereverb) ready.Add("Echo");
            _sdkLabel.ForeColor = Ok;
            _sdkLabel.Text = $"✓  NVIDIA AFX SDK {sdk.Version ?? "?"}   —   {string.Join(" · ", ready)} ready";
        }
    }

    private void UpdateCableStatus()
    {
        bool cable = _allOutputs.Any(d =>
            d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
            d.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
        _cableLabel.Visible = !cable;
        if (!cable)
        {
            _cableLabel.ForeColor = Warn;
            _cableLabel.Text = "⚠  No virtual audio cable detected — install VB-Cable (or the virtual mic driver) so apps can pick EchoDeck as their mic.";
        }
    }

    private void Gate(ToggleSwitch sw, bool available, string what)
    {
        sw.Enabled = available;
        sw.ForeColor = available ? TextColor : SubText;
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

    // ── engine + test ────────────────────────────────────────────────────────────
    private void StartEngine()
    {
        _engine.InputDeviceId = SelId(_mic);
        _engine.FarEndDeviceId = SelId(_reference);
        _engine.OutputDeviceId = SelId(_output);
        _engine.Start();
    }

    private async Task RecordTest()
    {
        if (_engine.State != EngineState.Running)
        {
            _status.Text = "Start EchoDeck first, then record.";
            _status.ForeColor = Warn;
            return;
        }

        _record.Enabled = false;
        _playIn.Enabled = _playOut.Enabled = _save.Enabled = false;
        _recCountdown = 5;
        _record.Text = "●  Recording… 5";
        _recTimer.Start();
        try
        {
            TestCaptureResult r = await _engine.RunMicTestAsync(TimeSpan.FromSeconds(5));
            _rawWav = r.RawWav;
            _procWav = r.ProcessedWav;
            _inWave.SetSamples(DecodeWav(_rawWav));
            _outWave.SetSamples(DecodeWav(_procWav));
            _playIn.Enabled = _playOut.Enabled = _save.Enabled = true;
            _status.Text = "Recorded — play Input vs Output to compare.";
            _status.ForeColor = SubText;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = Error;
        }
        finally
        {
            _recTimer.Stop();
            _record.Text = "●  Record speech";
            _record.Enabled = _engine.State == EngineState.Running;
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
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = Error;
        }
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
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = Error;
        }
    }

    private void SetLatency(double ms)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke((Action)(() => _latency.Text = $"Latency ≈ {ms:0} ms")); } catch { } return; }
        _latency.Text = $"Latency ≈ {ms:0} ms";
    }

    private void OnEngineStatus(object? sender, EngineStatusEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => OnEngineStatus(sender, e))); return; }

        _status.Text = e.Message;
        _status.ForeColor = e.IsError ? Error : SubText;

        bool busy = e.State is EngineState.Running or EngineState.Starting;
        _start.Enabled = !busy;
        _stop.Enabled = busy;
        _mic.Enabled = _reference.Enabled = _output.Enabled = _refresh.Enabled = !busy;
        _showDisabled.Enabled = _showDisconnected.Enabled = !busy;
        _record.Enabled = e.State == EngineState.Running;

        string tip = $"EchoDeck — {e.Message}";
        _tray.Text = tip.Length <= 63 ? tip : "EchoDeck";

        if (!busy)
        {
            _inLevel.SetLevel(0, false);
            _outLevel.SetLevel(0, false);
            _latency.Text = "Latency: —";
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
        // Open sized to the content (capped to the screen), then stay freely resizable.
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
