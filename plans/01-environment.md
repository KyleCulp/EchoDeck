# 01 — Environment (Verified Ground Truth)

Everything here was **confirmed by reading the machine** on 2026-06-27 (SDK folder, `version.h`, `Program.cs`, `bridge.log`). Treat as authoritative; do not re-research. Re-verify only if the machine changes (driver update, device rename, SDK upgrade).

## OS / toolchain
| Item | Value |
|---|---|
| OS | Windows 11 Pro (10.0.26200) |
| Runtime | .NET 8 (`net8.0`); app currently `OutputType=WinExe` |
| Audio lib | NAudio **2.2.1** (NuGet). Consider bumping to **2.3.0** for steadier loopback-silence behavior |
| Repo | `C:\Users\me\Repos\nv-aec-bridge` (not a git repo yet) |
| GPU | NVIDIA RTX (required for the SDK; Studio Voice ran in Broadcast, so Tensor-core RTX present). Exact model not captured — confirm at impl time if it matters for Studio Voice perf |

## NVIDIA Audio Effects SDK (AFX)
| Item | Value |
|---|---|
| Version | **1.6.1.2** (`NVIDIA_AUDIOFX_SDK_VERSION_STRING "1.6.1.2"`) |
| Install path | `C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK` |
| Main DLL | `NVAudioEffects.dll` (P/Invoke target; calling convention **Cdecl**) |
| Models dir | `…\NVIDIA Audio Effects SDK\models\` |
| License | `NVIDIA-Software-License-Agreement-2024.06.05.pdf` + Product-Specific-Terms (2024) — personal use is fine; redistribution has terms if ever shared |

### CUDA / runtime DLLs that sit beside `NVAudioEffects.dll`
These must be on the DLL search path at load time (the current app does this via `start-bridge.bat` setting `PATH`; the new app must do it **in-process** — see [`03-nvidia-effects.md`](03-nvidia-effects.md)):
```
cublas64_12.dll   cublasLt64_12.dll   cufft64_11.dll
nvrtc64_120_0.dll   nvinfer_10.dll   libcrypto-3-x64.dll
```

### Installed models (the effects we can actually run)
All native **48 kHz / 10 ms / 480-sample** (and 16 kHz variants). Confirmed from `models\readme.txt`:
```
aec_16k.trtpkg              aec_48k.trtpkg
denoiser_16k.trtpkg         denoiser_48k.trtpkg
dereverb_16k.trtpkg         dereverb_48k.trtpkg
dereverb_denoiser_16k.trtpkg  dereverb_denoiser_48k.trtpkg
superres_8kto16k.trtpkg     superres_16kto48k.trtpkg
```
- **NO `studio_voice` model present.** Studio Voice in the user's current setup comes from the **NVIDIA Broadcast app**, which bundles its own models — NOT from this SDK. To use it in our app, download `studio_voice_48k.trtpkg` from NVIDIA NGC (free account; org `nvidia`, team `maxine`). See [`03-nvidia-effects.md`](03-nvidia-effects.md) and [`07-build-deploy.md`](07-build-deploy.md).
- The **live chain (AEC + dereverb + denoise) is entirely 48 k** → no resampling between stages. Big simplification.

## Audio devices (current hardcoded names in `Program.cs`)
| Role | FriendlyName | Notes |
|---|---|---|
| Mic (near-end) | `Microphone (2- Shure MV7+)` | capture; 48000 Hz, 1 ch, 32-bit IeeeFloat |
| Speaker / AEC far-end ref | `Speakers (3- Modi 5)` | render; loopback-captured; 48000 Hz, 2 ch, 32-bit IeeeFloat |
| Bridge output (current) | `CABLE In 16ch` (VB-Audio Virtual Cable) | render target the app writes to |
| App mic selection (current) | `CABLE Output` | what other apps pick as their mic |

> These names are brittle (Windows renames on driver updates). The new app **must select by `MMDevice.ID`** and persist that, with FriendlyName only as a display/fallback. See [`05-device-switching.md`](05-device-switching.md).

`mmsys.cpl` requirements from the original README (still relevant): all four endpoints set to **48000 Hz** with **"Allow applications to take exclusive control" unchecked**.

## Frame / format constants (from `Program.cs`)
```
SampleRate   = 48000
FrameSamples = 480          // 10 ms @ 48 k  (matches AFX 10 ms model framesize)
Output fmt   = 48000 Hz, 16-bit, mono PCM
```

## Current NvAFX usage (the working baseline to preserve)
From `Program.cs` `NvAec` class — proven to load and run:
```
NvAFX_CreateEffect("aec", out handle)
NvAFX_SetString(handle, "model_path", "…\models\aec_48k.trtpkg")
NvAFX_SetFloat (handle, "intensity_ratio", 1.0f)
NvAFX_SetU32   (handle, "enable_vad", 0)
NvAFX_Load(handle)
NvAFX_Run(handle, input=[near,far], output=[clean], numSamples=480, numChannels=2)
NvAFX_DestroyEffect(handle)
```
Observed `bridge.log` success output confirms: mic `48000 Hz, 1 ch, 32 bit, IeeeFloat`, far `48000 Hz, 2 ch, 32 bit, IeeeFloat`, "NVIDIA AEC loaded.", "RUNNING."

## Current autostart (to be replaced)
- Task Scheduler task **"NVIDIA AEC Bridge"** (At Log On) runs `start-bridge.bat`.
- `start-bridge.bat`: waits 8 s, `cd` to publish dir, prepends SDK to `PATH`, runs `nv-aec-bridge.exe`, appends to `bridge.log`.
- Published exe: `…\bin\Release\net8.0\win-x64\publish\nv-aec-bridge.exe`.
- New app replaces this with a registry `Run` key + in-process DLL path setup + tray.

## External dependencies referenced by this project
| Dep | Role | Doc |
|---|---|---|
| NVIDIA Audio Effects SDK 1.6.1.2 | effects engine | 03 |
| VB-Audio Virtual Cable | current virtual mic (being replaced/fallback) | 04 |
| Virtual-Audio-Driver (github.com/VirtualDrivers) | target virtual mic (MIT, signed build) | 04 |
| NVIDIA Broadcast | current Studio Voice source (being dropped) | 03 |
| SoundSwitch (github.com/Belphemur/SoundSwitch) | reference for device-switch UX (not a dependency) | 05 |
