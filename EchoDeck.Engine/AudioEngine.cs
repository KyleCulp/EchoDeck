using EchoDeck.Engine.Audio;
using EchoDeck.Engine.Interop;
using EchoDeck.Engine.Io;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EchoDeck.Engine;

/// <summary>
/// Real-time audio engine: captures the mic (and, for AEC, the speaker loopback as a
/// far-end reference), runs an NVIDIA effect chain, and renders to the output device
/// (virtual mic / cable).
///
/// Chain order (only enabled stages run): AEC → [Room echo removal] / [Noise removal].
/// When both noise + echo are on, the single <c>dereverb_denoiser</c> model is used.
/// Effects can be toggled live while running (rebuilt on the processing thread).
///
/// Low-latency design: event-driven processing thread, bounded ring buffers trimmed to a
/// small target each tick, small event-sync WASAPI output.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int FrameSamples = 480; // 10 ms @ 48 kHz

    private static readonly int MaxBacklogSamples = SampleRate * 60 / 1000;
    private static readonly int TrimTargetSamples = SampleRate * 30 / 1000;

    public string SdkDir { get; set; } = NvAfxCapabilities.DefaultSdkDir;

    public string? InputDeviceId { get; set; }
    public string? FarEndDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public int LevelUpdateIntervalMs { get; set; } = 33;

    private bool _aecEnabled = true, _noise, _echo;
    public bool AecEnabled { get => _aecEnabled; set { _aecEnabled = value; RequestRebuild(); } }
    public bool NoiseRemovalEnabled { get => _noise; set { _noise = value; RequestRebuild(); } }
    public bool RoomEchoRemovalEnabled { get => _echo; set { _echo = value; RequestRebuild(); } }

    public EngineState State { get; private set; } = EngineState.Stopped;

    public event EventHandler<LevelEventArgs>? InputLevel;
    public event EventHandler<LevelEventArgs>? OutputLevel;
    public event EventHandler<EngineStatusEventArgs>? Status;
    /// <summary>Rough end-to-end latency estimate (ms), raised ~2×/sec while running.</summary>
    public event EventHandler<double>? LatencyEstimateMs;

    /// <summary>Probe the installed SDK (DLL/models present, version, available effects).</summary>
    public SdkStatus ProbeSdk() => NvAfxCapabilities.Probe(SdkDir);

    private readonly MMDeviceEnumerator _devices = new();
    private readonly RingBuffer _micRing = new(SampleRate);
    private readonly RingBuffer _farRing = new(SampleRate);
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly ManualResetEventSlim _ready = new(false);

    private WasapiCapture? _micCap;
    private WasapiLoopbackCapture? _farCap;
    private WasapiOut? _out;
    private BufferedWaveProvider? _outBuffer;
    private NvAfxEffect? _aec;
    private NvAfxEffect? _post;
    private Thread? _proc;
    private volatile bool _running;
    private volatile bool _rebuildRequested;
    private Exception? _startError;
    private volatile TestSession? _test;

    private float[] _micScratch = new float[FrameSamples * 8];
    private float[] _farScratch = new float[FrameSamples * 8];
    private int _levelFrameCounter;
    private int _latencyCounter;

    public void Start()
    {
        if (_running) return;
        SetState(EngineState.Starting, "Starting…");
        try
        {
            MMDevice micDev = Resolve(InputDeviceId, "microphone");
            MMDevice outDev = Resolve(OutputDeviceId, "output");
            MMDevice? farDev = string.IsNullOrWhiteSpace(FarEndDeviceId) ? null : Resolve(FarEndDeviceId, "AEC reference");

            _micCap = new WasapiCapture(micDev);
            RequireFormat(_micCap.WaveFormat, "Microphone");
            _micCap.DataAvailable += OnMicData;

            if (farDev != null) // run the reference capture whenever a reference is chosen, so AEC can toggle live
            {
                _farCap = new WasapiLoopbackCapture(farDev);
                RequireFormat(_farCap.WaveFormat, "AEC reference");
                _farCap.DataAvailable += OnFarData;
            }

            _outBuffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromMilliseconds(80),
                DiscardOnBufferOverflow = true
            };
            _out = new WasapiOut(outDev, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
            _out.Init(_outBuffer);

            _micRing.Clear();
            _farRing.Clear();
            _running = true;
            _startError = null;
            _ready.Reset();

            _proc = new Thread(ProcLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "echodeck-proc" };
            _proc.Start();

            // Wait for the chain to build on the proc thread (model load can throw).
            if (!_ready.Wait(8000)) throw new TimeoutException("Engine initialization timed out.");
            if (_startError != null) throw _startError;

            _out.Play();
            _micCap.StartRecording();
            _farCap?.StartRecording();

            SetState(EngineState.Running, RunningSummary());
        }
        catch (Exception ex)
        {
            SetState(EngineState.Faulted, ex.Message, isError: true);
            StopInternal(reportStopped: false);
        }
    }

    public void Stop() => StopInternal(reportStopped: true);

    private void StopInternal(bool reportStopped)
    {
        bool wasRunning = _running;
        _running = false;
        _frameReady.Set();

        try { _micCap?.StopRecording(); } catch { }
        try { _farCap?.StopRecording(); } catch { }
        _proc?.Join(800); // proc disposes the NvAFX effects on its own thread before exiting
        TestSession? pending = _test; _test = null; pending?.Finish(); // finalize any in-flight test
        try { _out?.Stop(); } catch { }

        if (_micCap != null) { _micCap.DataAvailable -= OnMicData; _micCap.Dispose(); _micCap = null; }
        if (_farCap != null) { _farCap.DataAvailable -= OnFarData; _farCap.Dispose(); _farCap = null; }
        _out?.Dispose(); _out = null;
        _outBuffer = null;
        _proc = null;

        if (reportStopped && wasRunning && State != EngineState.Faulted)
            SetState(EngineState.Stopped, "Stopped");
    }

    // ── capture callbacks ────────────────────────────────────────────────────────
    private void OnMicData(object? sender, WaveInEventArgs e)
    {
        var wf = _micCap!.WaveFormat;
        EnsureScratch(ref _micScratch, e.BytesRecorded, wf);
        int frames = SampleConvert.DownmixToMono(wf, e.Buffer, e.BytesRecorded, _micScratch);
        _micRing.Write(_micScratch.AsSpan(0, frames));
        _frameReady.Set();
    }

    private void OnFarData(object? sender, WaveInEventArgs e)
    {
        var wf = _farCap!.WaveFormat;
        EnsureScratch(ref _farScratch, e.BytesRecorded, wf);
        int frames = SampleConvert.DownmixToMono(wf, e.Buffer, e.BytesRecorded, _farScratch);
        _farRing.Write(_farScratch.AsSpan(0, frames));
    }

    private static void EnsureScratch(ref float[] scratch, int bytes, WaveFormat wf)
    {
        int frames = bytes / wf.BlockAlign;
        if (scratch.Length < frames) scratch = new float[frames];
    }

    // ── processing thread ────────────────────────────────────────────────────────
    private void ProcLoop()
    {
        try { RebuildChain(); }
        catch (Exception ex) { _startError = ex; _ready.Set(); return; }
        _ready.Set();

        var near = new float[FrameSamples];
        var far = new float[FrameSamples];
        var s1 = new float[FrameSamples];
        var s2 = new float[FrameSamples];
        var pcm = new byte[FrameSamples * 2];
        var pcmNear = new byte[FrameSamples * 2];

        while (_running)
        {
            if (_rebuildRequested)
            {
                _rebuildRequested = false;
                try { RebuildChain(); SetState(EngineState.Running, RunningSummary()); }
                catch (Exception ex) { SetState(EngineState.Running, $"Effect error: {ex.Message}", isError: true); }
            }

            if (_micRing.Count < FrameSamples) { _frameReady.WaitOne(15); continue; }

            _micRing.Read(near);
            float[] src = near;

            if (_aec != null)
            {
                int got = _farRing.Read(far);
                for (int i = got; i < FrameSamples; i++) far[i] = 0f;
                _aec.Run(near, far, s1);
                src = s1;
            }
            if (_post != null)
            {
                _post.Run(src, null, s2);
                src = s2;
            }

            int n = SampleConvert.FloatToPcm16(src, pcm);
            _outBuffer?.AddSamples(pcm, 0, n);

            TestSession? test = _test;
            if (test != null)
            {
                if (!test.StopRequested && test.Remaining > 0)
                {
                    int rn = SampleConvert.FloatToPcm16(near, pcmNear);
                    test.Raw.Write(pcmNear, 0, rn);
                    test.Proc.Write(pcm, 0, n);
                    test.Remaining -= FrameSamples;
                    if (test.Remaining <= 0) { _test = null; test.Finish(); }
                }
                else // stop requested (or capped) — finalize on this thread
                {
                    _test = null;
                    test.Finish();
                }
            }

            UpdateLevels(near, src);

            if (++_latencyCounter >= 50)
            {
                _latencyCounter = 0;
                double ms = _micRing.Count / 48.0 + (_outBuffer?.BufferedDuration.TotalMilliseconds ?? 0) + 20.0;
                LatencyEstimateMs?.Invoke(this, ms);
            }

            if (_micRing.Count > MaxBacklogSamples) _micRing.DiscardOldest(_micRing.Count - TrimTargetSamples);
            if (_farRing.Count > MaxBacklogSamples) _farRing.DiscardOldest(_farRing.Count - TrimTargetSamples);
        }

        // dispose effects on the same thread that created them
        _aec?.Dispose(); _aec = null;
        _post?.Dispose(); _post = null;
    }

    /// <summary>(Re)build the NvAFX chain from the current config. Runs on the proc thread.</summary>
    private void RebuildChain()
    {
        _aec?.Dispose(); _aec = null;
        _post?.Dispose(); _post = null;

        if (_aecEnabled && _farCap != null)
            _aec = new NvAfxEffect(SdkDir, "aec", "aec_48k.trtpkg", 2);

        if (_noise && _echo)
            _post = new NvAfxEffect(SdkDir, "dereverb_denoiser", "dereverb_denoiser_48k.trtpkg", 1);
        else if (_echo)
            _post = new NvAfxEffect(SdkDir, "dereverb", "dereverb_48k.trtpkg", 1);
        else if (_noise)
            _post = new NvAfxEffect(SdkDir, "denoiser", "denoiser_48k.trtpkg", 1);
    }

    private void RequestRebuild()
    {
        if (!_running) return;
        _rebuildRequested = true;
        _frameReady.Set();
    }

    private string RunningSummary()
    {
        var on = new List<string>();
        if (_aecEnabled && _farCap != null) on.Add("AEC");
        if (_noise) on.Add("Noise");
        if (_echo) on.Add("Echo");
        if (_aecEnabled && _farCap == null) return "Running — no reference device, AEC off";
        return on.Count == 0 ? "Running — passthrough" : "Running — " + string.Join(" + ", on);
    }

    private void UpdateLevels(float[] input, float[] output)
    {
        int every = Math.Max(1, LevelUpdateIntervalMs / 10);
        if (++_levelFrameCounter < every) return;
        _levelFrameCounter = 0;
        InputLevel?.Invoke(this, Measure(input));
        OutputLevel?.Invoke(this, Measure(output));
    }

    private static LevelEventArgs Measure(float[] block)
    {
        float peak = 0f;
        double sumSq = 0;
        foreach (float s in block)
        {
            float a = Math.Abs(s);
            if (a > peak) peak = a;
            sumSq += s * (double)s;
        }
        return new LevelEventArgs { Rms = (float)Math.Sqrt(sumSq / block.Length), Peak = peak, Clip = peak >= 0.999f };
    }

    private MMDevice Resolve(string? id, string label)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException($"No {label} device selected.");
        try { return _devices.GetDevice(id); }
        catch (Exception ex) { throw new InvalidOperationException($"Could not open the {label} device: {ex.Message}", ex); }
    }

    private static void RequireFormat(WaveFormat wf, string label)
    {
        if (wf.SampleRate != SampleRate)
            throw new InvalidOperationException(
                $"{label} is {wf.SampleRate} Hz. EchoDeck currently requires 48000 Hz — set it in Windows Sound " +
                "settings → Device properties → Advanced. (Auto-resampling comes later.)");
    }

    private void SetState(EngineState state, string message, bool isError = false)
    {
        State = state;
        Status?.Invoke(this, new EngineStatusEventArgs { State = state, Message = message, IsError = isError });
    }

    /// <summary>
    /// Record <paramref name="duration"/> of the live signal — raw mic and processed
    /// output — and return both as in-memory WAVs for A/B playback (like Broadcast's
    /// "Record speech"). Requires the engine to be running.
    /// </summary>
    /// <summary>True while a mic test is capturing.</summary>
    public bool IsTesting => _test != null;

    /// <summary>Begin an open-ended mic test (raw + processed), auto-capped at maxDuration.</summary>
    public bool StartMicTest(TimeSpan maxDuration)
    {
        if (State != EngineState.Running || _test != null) return false;
        var fmt = new WaveFormat(SampleRate, 16, 1);
        var rawMs = new MemoryStream();
        var procMs = new MemoryStream();
        _test = new TestSession
        {
            RawMs = rawMs,
            ProcMs = procMs,
            Raw = new WaveFileWriter(rawMs, fmt),
            Proc = new WaveFileWriter(procMs, fmt),
            Remaining = Math.Max(FrameSamples, (int)(maxDuration.TotalSeconds * SampleRate))
        };
        return true;
    }

    /// <summary>Stop the current mic test and return the captured raw + processed WAVs.</summary>
    public TestCaptureResult? StopMicTest()
    {
        TestSession? s = _test;
        if (s == null) return null;
        s.StopRequested = true;
        _frameReady.Set();
        s.Done.Wait(2000);
        return new TestCaptureResult(s.RawMs.ToArray(), s.ProcMs.ToArray(), new WaveFormat(SampleRate, 16, 1));
    }

    /// <summary>Fixed-duration mic test (used by the self-test).</summary>
    public async Task<TestCaptureResult> RunMicTestAsync(TimeSpan duration, CancellationToken ct = default)
    {
        if (!StartMicTest(duration))
            throw new InvalidOperationException("Start EchoDeck before recording a test.");
        TestSession s = _test!;
        await Task.Run(() => s.Done.Wait(ct), ct);
        return new TestCaptureResult(s.RawMs.ToArray(), s.ProcMs.ToArray(), new WaveFormat(SampleRate, 16, 1));
    }

    private sealed class TestSession
    {
        public required MemoryStream RawMs { get; init; }
        public required MemoryStream ProcMs { get; init; }
        public required WaveFileWriter Raw { get; init; }
        public required WaveFileWriter Proc { get; init; }
        public int Remaining;
        public volatile bool StopRequested;
        public readonly ManualResetEventSlim Done = new(false);

        public void Finish()
        {
            try { Raw.Dispose(); } catch { }
            try { Proc.Dispose(); } catch { }
            Done.Set();
        }
    }

    public void Dispose()
    {
        StopInternal(reportStopped: false);
        _devices.Dispose();
        _frameReady.Dispose();
        _ready.Dispose();
    }
}
