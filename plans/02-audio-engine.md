# 02 — Audio Engine Contract

The engine is a **framework-agnostic class library** (`EchoDeck.Engine`, no WinForms reference) that the GUI sits on top of. This doc is the contract for it. Effects interop lives in [`03-nvidia-effects.md`](03-nvidia-effects.md); the virtual-mic output target in [`04-virtual-mic.md`](04-virtual-mic.md).

## Why a rewrite (the latency bombs in `Program.cs`)
| Source | Now | Fix | Target |
|---|---|---|---|
| `BufferedWaveProvider.BufferDuration` | **300 ms** | small bridge buffer only | 20–30 ms, `DiscardOnBufferOverflow=true` |
| Queue trim high-water (`micQ.Count > SampleRate/2`) | trim only at **500 ms** | trim every tick to a small target | ~40 ms backlog |
| Processing loop | `Thread.Sleep(1)` poll | event-driven | `AutoResetEvent` signalled at ≥480 samples |
| `WasapiOut(... true, 30)` | 30 ms | shared + event-sync, smaller | 20 ms |
| Sample transport | `ConcurrentQueue<float>` (1 node/sample → GC churn) | custom SPSC ring | single `float[]`, zero-alloc |
| Extra app + cable | Broadcast + 2nd cable hop | effects in-process | removed |

**The crucial behavioral fix:** trim the mic backlog to a small target *continuously*, instead of letting it grow to 500 ms before trimming. That alone removes most of the "major delay". **Phase 2 of the roadmap ships this and is the single highest-value change.**

### Live latency budget (AEC + denoise, no Studio Voice)
```
capture (WASAPI period)        ~10–20 ms
ring + backlog                 ~10–20 ms
NvAFX compute (GPU)            sub-ms to few ms
output bridge buffer           ~20 ms
WASAPI render                  ~20 ms
────────────────────────────────────────
TOTAL                          ~30–60 ms      (vs old ~300–800 ms)
+ Studio Voice (if enabled)    ~110 ms        (inherent floor; off by default)
```

## Public API surface (`AudioEngine`)
```csharp
public sealed class AudioEngine : IDisposable
{
    public AudioEngine(EngineConfig? initial = null);

    // lifecycle
    public EngineState State { get; }   // Stopped, Starting, Running, Reconfiguring, Faulted
    public void Start();  public void Stop();

    // device selection — ALWAYS by MMDevice.ID, never FriendlyName
    public IReadOnlyList<DeviceInfo> GetInputDevices();   // Capture, Active
    public IReadOnlyList<DeviceInfo> GetRenderDevices();  // Render, Active
    public string? InputDeviceId  { get; set; }           // mic (near-end)
    public string? FarEndDeviceId { get; set; }           // speaker to loopback (AEC ref)
    public string? OutputDeviceId { get; set; }           // virtual-mic render endpoint
    // changing a device while Running restarts only the affected capture/render node

    // effect chain (detail in 03)
    public IReadOnlyList<EffectType> AvailableEffects { get; }   // probed at ctor; gates GUI
    public EffectChainConfig Chain { get; }
    public void ApplyChain();                                     // atomic rebuild
    public void SetEffectEnabled(EffectType e, bool on);
    public void SetEffectIntensity(EffectType e, float ratio);

    // config persistence (05 for schema)
    public EngineConfig ExportConfig();  public void ImportConfig(EngineConfig c);
    public void SaveConfig();            public static EngineConfig? TryLoadConfig();

    // metering (VU)
    public event EventHandler<LevelEventArgs>? InputLevel;    // raw mic
    public event EventHandler<LevelEventArgs>? OutputLevel;   // post-chain
    public int LevelUpdateIntervalMs { get; set; } = 33;      // ~30 fps

    // before/after test (06 plays the result)
    public Task<TestCaptureResult> RunMicTestAsync(TimeSpan duration, CancellationToken ct = default);

    // status / diagnostics
    public event EventHandler<EngineStateEventArgs>? StateChanged;
    public event EventHandler<EngineErrorEventArgs>? ErrorOccurred;
    public LatencyReport GetLatencyReport();
}
```

### Supporting types
```csharp
public enum EffectType { Aec, Denoiser, Dereverb, DereverbDenoiser, StudioVoice, SuperRes }
public enum EngineState { Stopped, Starting, Running, Reconfiguring, Faulted }

public sealed record DeviceInfo(string Id, string FriendlyName, DataFlow Flow,
                                bool IsDefault, int MixSampleRate, int MixChannels);

public sealed class LevelEventArgs : EventArgs { public float Rms; public float Peak; public bool Clip; }

public sealed class EngineErrorEventArgs : EventArgs
{ public EngineErrorCode Code; public string Message = ""; public bool Fatal; public Exception? Inner; }

public enum EngineErrorCode {
    InputDeviceNotFound, FarEndDeviceNotFound, OutputDeviceNotFound, DeviceDisconnected,
    ModelFileMissing, EffectNotSupported, GpuInitFailed, CudaOutOfMemory,
    UnsupportedFormat, SampleRateMismatch, Underrun, Overrun, ChainRebuildFailed, Unknown }

public sealed record TestCaptureResult(byte[] RawWav, byte[] ProcessedWav, WaveFormat Format);
public sealed record LatencyReport(double CaptureMs, double RingMs, double ProcessingFrameMs,
    double StudioVoiceInherentMs, double RenderMs, double TotalMs);
```
`EffectChainConfig` / `EngineConfig` are defined in [`05-device-switching.md`](05-device-switching.md) (config schema).

## Threading model
Three owned threads plus WASAPI-internal threads. Hot path is **allocation-free after warmup**.
```
[Mic capture thread]   WasapiCapture.DataAvailable
    downmix→mono float → micRing (SPSC); update input LevelMeter; signal frameReady
[Far-end thread]       WasapiLoopbackCapture.DataAvailable   (only when AEC on)
    downmix→mono float → farRing; FarEndClock synthesizes silence on idle (§ alignment)
[Processing thread]    our Thread { IsBackground, Priority=AboveNormal, Name="afx-proc" }
    wait(frameReady); pull 480 near + 480 aligned far; run EffectChain (NvAFX_Run sequence);
    PCM16 → output renderer; update output LevelMeter; feed TestRecorder
[WASAPI render thread] WasapiOut event-sync internal → virtual mic
```
Rules:
- **All `NvAFX_*` calls for every handle happen on the single processing thread** → one CUDA context, no locks in `NvAFX_Run`, simple handle lifetime.
- Capture callbacks stay tiny (downmix + ring write + RMS) so WASAPI never overruns.
- No `Thread.Sleep` polling; `AutoResetEvent`/`SemaphoreSlim` wakes the proc thread (with a ~15 ms timeout so it can also do far-end silence top-up and stall detection).

## Buffering design (concrete numbers)
| Buffer | Capacity / target | Behavior |
|---|---|---|
| `micRing` | 80 ms capacity, **trim to ~40 ms each tick** | drop-oldest on overflow; steady-state ≈ 1 frame |
| `farRing` | 40–60 ms, target backlog ≈ 1–2 frames | silence top-up when loopback idle |
| output bridge (`BufferedWaveProvider`) | 20–30 ms | `DiscardOnBufferOverflow=true`; absorbs render-period jitter |
| `WasapiOut` | 20 ms, shared, event-sync | exclusive optional for power users only |

`RingBuffer` = single preallocated `float[]`, head/tail indices, `Volatile`/`Interlocked`; SPSC (one writer = capture thread, one reader = proc thread). `TryReadExact(480)` gates the proc thread.

### Output mode decision: shared, not exclusive
The output target is a **virtual** device (Virtual-Audio-Driver / VB-Cable). Exclusive mode bypasses no physical DMA there, so it buys ~nothing while risking `AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED` and blocking other clients. Default **shared + event-sync**. Expose an `OutputExclusiveMode` flag (default off) for power users targeting a physical endpoint; if `IsFormatSupported` fails, raise `UnsupportedFormat` and auto-fall-back to shared.

## Hard problems & how the engine handles them
- **Far-end alignment & silence (the big one):** `WasapiLoopbackCapture` may deliver no/idle buffers when nothing plays, but AEC needs a continuous, time-aligned far reference. `FarEndSource` runs a monotonic clock; on each tick if `farRing < 480` it synthesizes zeros for the shortfall (current code already zero-fills far on underrun — keep, make deterministic). Apply a ~5 ms linear fade on silence↔signal transitions. Keep far backlog bounded (D ± slack): if it grows past 2·D, drop oldest; if it underflows, inject silence. The adaptive AEC tolerates small far-delay error, so bounded-backlog avoids explicit drift-resampling. **If AEC is off, don't start loopback at all.**
- **Variable WASAPI buffer vs fixed 480 frame:** accumulate into the ring; proc thread pulls exactly 480. No partial frame reaches AFX.
- **Reconfigure while running:** build new stages off the hot path, swap a single `Volatile` chain reference at the top of a tick, dispose old stages on the proc thread; if a new stage fails to load, keep the old chain and raise `ChainRebuildFailed`. (See 03.)
- **GC pauses:** preallocate every buffer at Start()/chain-build; reuse forever; `Span<float>`/`ReadOnlySpan<float>` over pooled arrays; no LINQ/boxing in the hot path; drop `ConcurrentQueue<float>`. Steady-state zero alloc ⇒ GC effectively never runs in the audio path.
- **Mic / far not 48 k:** auto-resample to 48 k via `WdlResamplingSampleProvider` (preferred, in NAudio.Core, low-latency) rather than throwing like the current code; or raise `SampleRateMismatch`. Prefer auto-resample so users avoid `mmsys.cpl` fiddling.
- **Device unplugged while running:** wire `IMMNotificationClient`; on removal raise `DeviceDisconnected`, pause, re-acquire by ID, fall back to FriendlyName, else surface and stay paused.

## Level metering & test capture
- `LevelMeter`: compute RMS/peak per processed block, raise `InputLevel`/`OutputLevel` throttled to `LevelUpdateIntervalMs`; include a `Clip` flag (keep the existing `Math.Clamp(-1,1)` in `FloatToPcm16` and surface clip for a GUI LED).
- `RunMicTestAsync`: on the same proc tick, open two `MemoryStream`-backed `WaveFileWriter`s (mono 48 k PCM16) — `raw` fed from the mic-ring tap (pre-effects), `processed` from chain output — so both WAVs start at test t0 and are length-aligned for A/B playback. Returns `TestCaptureResult`. High-Quality Studio Voice is allowed here (latency irrelevant offline).

## File breakdown (`EchoDeck.Engine`)
```
AudioEngine.cs              EngineConfig.cs / ConfigStore.cs (05)
DeviceEnumerator.cs / DeviceInfo.cs
Capture/WasapiInputSource.cs   Capture/FarEndSource.cs
Pipeline/ProcessingThread.cs   Pipeline/EffectChain.cs   Pipeline/IAudioEffectStage.cs
Pipeline/NvAfxStage.cs   Pipeline/StageDescriptor.cs   Pipeline/FrameReblocker.cs   Pipeline/FrameResampler.cs
Io/RingBuffer.cs   Io/OutputRenderer.cs   Io/LevelMeter.cs   Io/TestRecorder.cs
Interop/NvAfx.cs   Interop/NvAfxCapabilities.cs   (03)
EngineEvents.cs   EngineEnums.cs
```

## Reused from `Program.cs`
`EnqueueMono`, `ReadSample`, `Read24` (downmix + sample decode — handle 16/24/32-bit + float), `FloatToPcm16`, `FindDevice`. All correct; relocate into the engine.
