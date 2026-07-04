# EchoDeck — Design Contract

This folder is the **design contract** for **EchoDeck** — evolving the original `nv-aec-bridge` ChatGPT-built console app into **one lightweight Windows tray app** that replaces NVIDIA Broadcast, VB-Cable, and SoundSwitch in the user's audio pipeline.

It exists so that **any new Claude session (or the user) can resume work without re-deriving the research**. Each doc is a self-contained contract for one component: its purpose, the locked decisions, the key API/schema, and open questions.

> Status: **design documented, implementation not started.** The current repo still contains only the original console app (`Program.cs`). See [`08-roadmap.md`](08-roadmap.md) for what to build and in what order.

## How to resume (read this first)
1. Read [`00-overview.md`](00-overview.md) for the goal and architecture.
2. Read [`01-environment.md`](01-environment.md) for the verified ground truth (SDK version, models, device names, paths). **Do not re-research these — they were confirmed by reading the machine.**
3. Read the component doc(s) for whatever you're implementing.
4. Drive the work from [`08-roadmap.md`](08-roadmap.md) and tick its checkboxes as you go.

## The vision (one paragraph)
Collapse a 3-app, 2-cable pipeline into a single WinForms tray app that: runs the NVIDIA Audio Effects (AEC + denoise + dereverb, with Studio Voice gated) **in-process**, presents its **own virtual microphone** via the signed open-source Virtual-Audio-Driver, adds **SoundSwitch-style device pickers + global hotkeys**, includes a **Broadcast-style record/compare test panel**, and **cuts live latency from ~300–800 ms to ~30–60 ms**.

## Current → target pipeline
```
NOW (3 apps + 2 driver hops):
  Shure MV7+  +  Modi 5 loopback
    → nv-aec-bridge.exe (console: NVIDIA AEC only)
    → VB-CABLE
    → NVIDIA Broadcast (Studio Voice)
    → Discord / Teams / games / browser

TARGET (1 app + 1 driver):
  Shure MV7+  +  Modi 5 loopback
    → EchoDeck app  (AEC + denoise + dereverb [+ Studio Voice gated], in-process)
    → Virtual-Audio-Driver virtual mic
    → Discord / Teams / games / browser
```

## Locked decisions
| Topic | Decision | Why |
|---|---|---|
| UI framework | **WinForms** (`net8.0-windows`) | Minimal resource use; SoundSwitch is WinForms too |
| Effects | **All as live toggles**: AEC, Noise Removal, Room Echo Removal, Studio Voice | Mirrors Broadcast; user picks quality/latency live |
| Studio Voice | **Architected but gated** | Model not installed; ~110 ms latency; for recording, not live |
| Virtual mic | **OSS Virtual-Audio-Driver (signed) from the start**; VB-Cable kept only as a fallback seam | User wants off VB-Cable; a .NET app can't be a driver itself |
| Device switching | **In-app pickers + global hotkeys** (set Windows default) | Replaces SoundSwitch |
| Autostart | App-managed (registry Run key + tray) | Replaces Task Scheduler + `start-bridge.bat` |
| Discord control | **Local RPC** (`SET_VOICE_SETTINGS`) to auto-disable Discord's NS/EC/AGC and select the virtual mic; user brings their own Discord app ID (one-time wizard) | Prevents double-processing artifacts; last manual setup step removed |
| License | **MIT** — SoundSwitch (GPLv2) is a *behavioral* reference only, no code copied | Keep the repo permissive; the interop it would provide is small and already specced in [`05`](05-device-switching.md) |

## Doc index
| Doc | Component |
|---|---|
| [`00-overview.md`](00-overview.md) | Problem, goals/non-goals, success criteria, architecture |
| [`01-environment.md`](01-environment.md) | Verified ground truth: SDK, models, devices, paths, formats |
| [`02-audio-engine.md`](02-audio-engine.md) | Low-latency engine: API, threading, ring buffers, latency budget |
| [`03-nvidia-effects.md`](03-nvidia-effects.md) | NvAFX interop, effect→model mapping, chain, Studio Voice gating |
| [`04-virtual-mic.md`](04-virtual-mic.md) | Virtual-mic provider seam, Virtual-Audio-Driver, VB-Cable fallback |
| [`05-device-switching.md`](05-device-switching.md) | Device enum, config schema, IPolicyConfig, global hotkeys |
| [`06-gui-tray.md`](06-gui-tray.md) | WinForms UI, test panel, VU meters, tray, run-at-login |
| [`07-build-deploy.md`](07-build-deploy.md) | Solution structure, csproj, NGC model download, packaging |
| [`08-roadmap.md`](08-roadmap.md) | Phased implementation + status checklist |
| [`09-oss-repo.md`](09-oss-repo.md) | Open-source repo setup, MIT license, CI/release, governance |
| [`10-profiles.md`](10-profiles.md) | Audio profiles — switchable device + effect bundles |
| [`11-discord-integration.md`](11-discord-integration.md) | Discord voice-settings control via local RPC (Stream Deck-style) |
| [`legacy-console-setup.md`](legacy-console-setup.md) | Runbook for the original `nv-aec-bridge` console prototype |

## Source material in the repo (reuse, don't rewrite)
- [`../Program.cs`](../Program.cs) — current working pipeline. Salvage: `EnqueueMono`, `ReadSample`, `Read24`, `FloatToPcm16`, `FindDevice`, and the `NvAFX_*` P/Invoke block.
- [`legacy-console-setup.md`](legacy-console-setup.md) — the original console-app runbook (device routing, mmsys settings, Task Scheduler). The root [`../README.md`](../README.md) is now the EchoDeck project front door.
- [`../start-bridge.bat`](../start-bridge.bat) — current launch shim (PATH to SDK + logging); to be replaced by in-process startup.
