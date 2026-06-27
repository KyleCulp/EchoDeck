# 00 — Overview, Goals & Architecture

## The problem
The user has true system-wide acoustic echo cancellation working, but the pipeline is a Rube Goldberg machine of three apps and two virtual-cable hops:

```
Shure MV7+  +  Modi 5 loopback
  → nv-aec-bridge.exe   (ChatGPT console app — NVIDIA AEC via Audio Effects SDK)
  → VB-CABLE            (virtual cable)
  → NVIDIA Broadcast    (Studio Voice enhancement)
  → apps                (Discord / Teams / games / browser)
```

Concrete pain points (from the user):
1. **Major delay.** The console app has real latency bombs (see [`02-audio-engine.md`](02-audio-engine.md)), and Broadcast's Studio Voice adds ~110 ms on top.
2. **No GUI.** Device names are hardcoded in `Program.cs`; changing a device means editing code and recompiling.
3. **Too many apps.** Wants Broadcast, VB-Cable, and SoundSwitch all folded into one minimal program.
4. **Wants to test audio** (record + compare input vs output), like Broadcast's "Test microphone effects".
5. **Switches audio devices often** and wants SoundSwitch-style control.

## Goals
- **One app.** A single WinForms tray app is the only thing the user installs, sees, and configures.
- **Effects in-process.** Run the NVIDIA Audio Effects SDK directly (AEC + denoise + dereverb, Studio Voice gated) — no Broadcast.
- **Own virtual mic.** Present a virtual microphone via the bundled signed Virtual-Audio-Driver — no VB-Cable (kept only as a fallback).
- **Low latency.** Target **~30–60 ms** end-to-end for the live chain (AEC + denoise), down from ~300–800 ms.
- **Device control.** In-app device pickers (saved, by ID) + global hotkeys to switch the Windows default device (SoundSwitch parity).
- **Audio testing.** Record a few seconds and play back raw vs processed for an A/B.
- **Minimal footprint.** Idle CPU/RAM near-zero; modest GPU with AEC+denoise.

## Non-goals (explicitly out of scope)
- **Studio Voice for live talking.** It's ~110 ms and GPU-heavy; gated off by default, intended for recording. Architect for it, don't optimize the live path around it.
- **Writing our own kernel audio driver.** A .NET app cannot be a virtual mic; we bundle an existing signed OSS driver instead.
- **Cross-platform.** Inherently Windows-only (WASAPI + NVIDIA SDK + virtual audio driver).
- **Mobile/remote/cloud.** Local desktop tool only.

## Success criteria
| Criterion | Target |
|---|---|
| Live latency (AEC + denoise) | ~30–60 ms measured round-trip |
| Apps to run | 1 (this app) + 1 driver underneath |
| Change a device | Dropdown in GUI, no recompile |
| AEC parity | Echo still removed vs current app |
| Effects | AEC / Noise / Echo / Studio Voice toggles all functional (Studio Voice once model present) |
| Test panel | Record + play input vs output |
| Hotkeys | Switch Windows default playback/recording device |
| Idle resource use | Minimal CPU/RAM in tray |

## Target architecture (high level)
Two projects in one solution (details in [`07-build-deploy.md`](07-build-deploy.md)):

```
EchoDeck.Engine   (class library, net8.0, framework-agnostic)
  ├─ Interop      → NVIDIA NvAFX P/Invoke + capability probe          (03)
  ├─ Capture      → WASAPI mic + far-end loopback, mono float frames  (02)
  ├─ Pipeline     → single proc thread, effect chain, NvAFX_Run       (02,03)
  ├─ Io           → ring buffers, output renderer, level meters, test (02)
  └─ AudioEngine  → public façade (start/stop, devices, effects, …)   (02)

EchoDeck.App      (WinForms, net8.0-windows, WinExe)
  ├─ MainForm           → toggles, sliders, dropdowns, meters, test    (06)
  ├─ TrayIcon           → NotifyIcon, minimize-to-tray, quick toggles  (06)
  ├─ HotkeyManager      → RegisterHotKey / WM_HOTKEY                    (05)
  ├─ PolicyConfig       → IPolicyConfig COM (set default device)       (05)
  ├─ VirtualMicProvider → install/route virtual mic (driver mgmt)      (04)
  └─ Startup            → run-at-login registry, single-instance       (06)
```

Data flow at runtime:
```
mic (WASAPI capture) ─┐
                      ├─► proc thread ─► effect chain ─► output renderer ─► virtual mic ─► apps
Modi 5 (WASAPI loop) ─┘   (NvAFX)        (AEC→dereverb→denoise→[SV])      (WASAPI render)
```

## Key constraints discovered during research (full detail in `01-environment.md`)
- The installed SDK (**1.6.1.2**) ships `aec`, `denoiser`, `dereverb`, `dereverb_denoiser`, `superres` at **48 kHz / 10 ms native** → the whole live chain is single-rate, **no resampling**. **No `studio_voice` model** is installed.
- Studio Voice must be downloaded from NVIDIA NGC and even in low-latency mode adds **~110 ms**.
- Presenting a mic requires a **kernel driver**; the chosen one (Virtual-Audio-Driver) now has a **signed** build and routes virtual-speaker → ring buffer → virtual-mic just like VB-Cable.
