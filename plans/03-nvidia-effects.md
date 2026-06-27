# 03 — NVIDIA Effects (NvAFX Interop & Effect Chain)

Contract for `Interop/NvAfx.cs`, `Interop/NvAfxCapabilities.cs`, and the `Pipeline/*` effect-chain types. Environment facts (SDK 1.6.1.2, model list, DLL deps) are in [`01-environment.md`](01-environment.md).

## P/Invoke surface (`NVAudioEffects.dll`, Cdecl)
Keep the existing, proven imports from `Program.cs`:
```csharp
NvAFX_CreateEffect(string code, out IntPtr handle)
NvAFX_SetString(IntPtr h, string param, string value)
NvAFX_SetFloat (IntPtr h, string param, float value)
NvAFX_SetU32   (IntPtr h, string param, uint value)
NvAFX_Load(IntPtr h)
NvAFX_Run(IntPtr h, IntPtr[] input, IntPtr[] output, int numSamples, int numChannels)
NvAFX_DestroyEffect(IntPtr h)
```
Add for the new design:
```csharp
NvAFX_GetU32(IntPtr h, string param, out uint val)               // query real frame size / rate / channels
NvAFX_GetString(IntPtr h, string param, StringBuilder val, int maxLen)
NvAFX_Reset(IntPtr h)                                            // flush internal state between test runs / post-xrun
// NvAFX_GetEffectIndices(out int num, out IntPtr indices)       // optional capability probe; fall back to try-create
```
All return `int` status; nonzero = failure. Keep the `Check(status, where)` helper pattern.

### Known parameter names
```
"model_path"                  string  → …\models\<name>.trtpkg
"intensity_ratio"             float   → 0.0–1.0 (current AEC uses 1.0)
"enable_vad"                  u32     → 0/1 (current AEC sets 0)
"sample_rate"                 u32     → 8000 / 16000 / 48000
"num_input_samples_per_frame" u32     → query (480 @ 48k/10ms; 160 @ 16k)
"num_output_samples_per_frame" u32    → query
"num_input_channels"          u32     → AEC=2 (near,far); others=1
"num_output_channels"         u32
"num_streams"                 u32
```
Always **query** frame size/rate via `NvAFX_GetU32` after `Load` instead of assuming 480 — future-proofs 20 ms or 16 k models.

## DLL search path (must be set IN-PROCESS)
The CUDA deps (`cublas64_12`, `cublasLt64_12`, `cufft64_11`, `nvrtc64_120_0`, `nvinfer_10`, `libcrypto-3-x64`) live beside `NVAudioEffects.dll`. The current app relies on `start-bridge.bat` prepending the SDK dir to `PATH`. The new app must do this itself at startup so the GUI exe runs standalone:
- `AddDllDirectory(sdkDir)` + `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)`, or prepend the SDK dir to the process `PATH`. The current code already calls `SetDllDirectory(sdkDir)` in `NvAec` ctor — keep but broaden so dependent DLLs resolve too.

## Effect → model mapping
| GUI toggle (Broadcast name) | Effect code | Model file (live = 48 k) | In→Out | Installed? |
|---|---|---|---|---|
| AEC | `aec` | `aec_48k.trtpkg` | 2ch (near,far) → 1ch | ✅ |
| Noise removal | `denoiser` | `denoiser_48k.trtpkg` | 1→1 | ✅ |
| Room echo removal | `dereverb` | `dereverb_48k.trtpkg` | 1→1 | ✅ |
| Noise + echo (both on) | `dereverb_denoiser` | `dereverb_denoiser_48k.trtpkg` | 1→1 | ✅ |
| Studio voice | `studio_voice` | `studio_voice_48k.trtpkg` | 1→1 | ❌ **NGC download** |
| (Super-res — not used live) | `superres` | `superres_16kto48k.trtpkg` | 1→1, rate-changing | ✅ |

## Chain order & rules
Fixed logical order; only enabled stages run:
```
mic(48k mono) → [AEC near+far] → [Dereverb] → [Denoiser] → [StudioVoice] → out(48k)
                                  \____ collapse to dereverb_denoiser when BOTH on ____/
```
Rationale: AEC first (needs raw near + far ref); dereverb before denoise (kill room tail first); **collapse dereverb+denoise into the single `dereverb_denoiser_48k` handle** when both are requested (one model instead of two → quality + latency win); Studio Voice last (re-synthesis should see the cleaned signal).

Because every installed live model is **48 k / 10 ms / 480 samples**, the live chain needs **no resampling and no reblocking** — each stage is 480-in/480-out at 48 k. (Resampler/reblocker infrastructure only matters if a 16 k model or super-res is ever introduced; keep `FrameResampler`/`FrameReblocker` as latent infra per [`02-audio-engine.md`](02-audio-engine.md).)

### Stage interface
```csharp
internal interface IAudioEffectStage : IDisposable {
    EffectType Type { get; }
    int InRate { get; }  int OutRate { get; }
    int InChannels { get; }              // AEC=2, others=1
    int FrameSamplesIn { get; }  int FrameSamplesOut { get; }
    int InherentLatencySamples { get; }  // StudioVoice ~110 ms; others ~0
    void ProcessFrame(ReadOnlySpan<float> primaryIn, ReadOnlySpan<float> secondaryIn, Span<float> output);
    void Reset();                        // NvAFX_Reset
}
```
`NvAfxStage` wraps one handle: `CreateEffect(code)` → `SetString("model_path", …)` → optional `SetFloat("intensity_ratio")` / `SetU32("sample_rate")` → `Load` → query real frame size/channels. AEC's `ProcessFrame` uses `secondaryIn` as the far-end; others ignore it.

### Reconfigure while running (atomic swap)
```csharp
public void ApplyChain() {
    var newStages = BuildStages(Chain);          // create+load all handles (marshalled onto proc thread)
    EnqueueToProcThread(() => {
        var old = _activeChain;
        _activeChain = newStages;                 // single volatile ref swap, read at top of each tick
        old?.DisposeAll();                        // destroy old handles AFTER swap, same thread
        ResetResamplersAndReblockers();
    });
}
```
- New handles built **before** the swap; audio keeps flowing through the old chain meanwhile.
- Build + dispose both happen on the **proc thread** (drain a `ConcurrentQueue<Action>` each tick) → single-threaded CUDA/handle lifetime.
- If a new stage's `Load` fails (missing model / GPU OOM) → **abort the swap, keep the old chain**, raise `ChainRebuildFailed` / `ModelFileMissing`. Never tear down a working chain on a failed reconfigure.
- Intensity-only change → live `SetFloat("intensity_ratio")` if allowed; else rebuild just that one handle.

## Capability probing (`NvAfxCapabilities.Probe()`)
At engine construction:
1. Set up the in-process DLL search dir (above).
2. For each `EffectType`, map enum → model filename (48 k variant) and check the `.trtpkg` exists under `…\models\`. Missing file → effect unavailable.
3. Optionally `CreateEffect(code)` + `DestroyEffect` to confirm the DLL build supports the code (nonzero = unsupported).
4. Cache the result as `AvailableEffects`. The GUI greys out unavailable toggles.

Model-path resolution order: (a) `EngineConfig` override, (b) `C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK`, (c) `NVAFX_SDK_DIR` / `NV_AUDIO_EFFECTS_SDK` env var. Resolve `models\<name>.trtpkg`; raise `ModelFileMissing` with the **exact path** if absent (the current code hardcodes `aec_48k.trtpkg` and throws an opaque load error otherwise).

Map nonzero `Load`/`Run` statuses → `GpuInitFailed` / `CudaOutOfMemory` / `EffectNotSupported`.

## Studio Voice — gated, recording-oriented
- **Not installed** on this machine (no `studio_voice` model). Treated as a **first-class but capability-gated** stage: code is written, but the toggle stays disabled until `studio_voice_48k.trtpkg` is present. When the user installs it, the stage lights up with **zero code change**.
- Two variants: **Low-Latency** (real-time, but ~110 ms inherent floor; "may not run in real time on lower-end GPUs") and **High-Quality** (offline, up to ~6 s latency).
- **Live path:** Low-Latency only, default **off**; surface its cost via `LatencyReport.StudioVoiceInherentMs`.
- **Recording/test path:** allow High-Quality in `RunMicTestAsync` (latency irrelevant offline) — this is the user's intended use ("I like it for recording videos").
- HQ allocates heavily → guard `Load`/`Run` with try/catch → `CudaOutOfMemory`, auto-fall-back to Low-Latency.
- Download: NVIDIA NGC, org `nvidia` / team `maxine`, via the SDK's download script + an NGC API key. See [`07-build-deploy.md`](07-build-deploy.md).

## Disposal / lifecycle
- Every `CreateEffect`/`DestroyEffect` on the proc thread. CUDA context is per-process inside the DLL; destroy-then-create across reconfig is fine.
- On `Stop()`: dispose all handles for a clean device change.
- Device change while running rebuilds only the affected capture/render node — **not** the GPU chain — unless the effect set itself changed.

## Open questions (resolve at impl time)
- Exact `intensity_ratio` support per effect (does denoiser/dereverb accept it, and the valid range?).
- Whether `studio_voice` mode (HQ vs LL) is selected by model file, a param, or a separate effect code in SDK 1.6.x vs newer.
- Whether `NvAFX_GetEffectIndices` exists in this DLL build (else use try-create probing).
- AEC far-end delay tolerance (how much misalignment before cancellation degrades) — informs `FarEndSource` backlog target.
