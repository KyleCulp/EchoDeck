# 🎧 EchoDeck

**One small Windows app to route your audio, switch device profiles, and run NVIDIA-powered mic effects — replacing NVIDIA Broadcast, VB-Cable, and a device switcher all at once.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue.svg)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](#building-from-source)

> ⚠️ **Status: early development (pre-`v0.1`).** The design is fully specced in [`plans/`](plans/) and a working NVIDIA-AEC console prototype exists, but the GUI app described below is being built. Want to help? See [Contributing](#contributing) and the [roadmap](plans/08-roadmap.md). <!-- Replace OWNER in badge/links with your GitHub username after pushing. -->

---

## What it does

EchoDeck collapses a messy multi-app audio chain into a single tray app:

```
        BEFORE                                  AFTER
  mic + speaker loopback                   mic + speaker loopback
    → AEC console app                        → EchoDeck
    → VB-CABLE                                   (effects in-process,
    → NVIDIA Broadcast                            own virtual mic,
    → your apps                                   device + profile switching)
                                             → your apps
```

- 🎙️ **NVIDIA mic effects, in-process** — Acoustic Echo Cancellation, Noise Removal, Room Echo Removal (and Studio Voice, gated) via the NVIDIA Audio Effects SDK. No NVIDIA Broadcast needed.
- 🔌 **Its own virtual microphone** — built on the open-source [Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver), so apps just pick "EchoDeck" as their mic. No VB-Cable.
- 🔀 **Device + profile switching** — SoundSwitch-style: pick devices in-app, switch the Windows default with global hotkeys, and save **profiles** (e.g. *headphones + headset mic*, *speakers + desktop mic*, *IEMs + Shure mic*).
- 🎚️ **Live effect toggles + a test panel** — flip effects on/off and record a sample to A/B raw vs processed, just like Broadcast.
- ⚡ **Low latency** — a rewritten audio path targets **~30–60 ms** for the live chain (vs the old setup's ~300–800 ms).
- 🪶 **Light** — WinForms tray app; minimal CPU/RAM at idle.

## Requirements

- Windows 10 (1903+) or Windows 11, 64-bit
- An **NVIDIA RTX GPU** (required by the Audio Effects SDK)
- [NVIDIA Audio Effects SDK](https://developer.nvidia.com/maxine) installed (EchoDeck does **not** redistribute it — see [Third-Party Notices](THIRD_PARTY_NOTICES.md))
- [.NET 8 Runtime](https://dotnet.microsoft.com/download) (Desktop)
- Studio Voice is **optional** and requires a separate model download from NVIDIA NGC (see [docs](plans/07-build-deploy.md))

## How it works

```
mic (WASAPI capture) ─┐
                      ├─► effect chain (NVIDIA AFX) ─► virtual mic ─► your apps
speaker loopback ─────┘   AEC → echo-removal → denoise → [Studio Voice]
   (AEC far-end reference)
```
The speaker loopback gives the AEC a reference of what's playing, so it can subtract speaker bleed from your mic. Full architecture in [`plans/00-overview.md`](plans/00-overview.md).

## Installing

Pre-built releases will be published on the [Releases](../../releases) page once `v0.1` lands. For now, build from source.

## Building from source

```powershell
git clone https://github.com/OWNER/echodeck.git
cd echodeck
dotnet build -c Release
dotnet run -c Release --project EchoDeck.App   # once the app project exists (see roadmap)
```
> The repo currently contains the original NVIDIA-AEC console prototype; the `EchoDeck.Engine` / `EchoDeck.App` split is roadmap Phase 1. See [`plans/07-build-deploy.md`](plans/07-build-deploy.md).

## Profiles

A **profile** bundles a full audio setup under one name/hotkey: output device, mic device, AEC reference, and which effects are on. Example profiles:

| Profile | Output | Mic | Effects |
|---|---|---|---|
| Gaming | Headphones | Headset mic | AEC + Noise |
| Desk | Headphones | Desktop mic | AEC + Noise + Echo |
| Speakers | Speakers | Desktop mic | AEC (heavy) + Noise |
| Recording | Headphones | Shure / IEM mic | Noise + Studio Voice |

Switch from the window, the tray, or a hotkey. See [`plans/10-profiles.md`](plans/10-profiles.md).

## Documentation

The full design lives in [`plans/`](plans/) — start at [`plans/README.md`](plans/README.md). It's written as a "contract" so contributors (and the maintainer) can pick up any piece without re-deriving it.

## Contributing

PRs welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the [roadmap](plans/08-roadmap.md). Good first issues will be labelled once the issue tracker is seeded. Be kind — see the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE) © 2026 Kyle Culp. Third-party components and the NVIDIA SDK have their own terms — see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Acknowledgements

[NAudio](https://github.com/naudio/NAudio) · [Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver) · [NVIDIA Maxine](https://developer.nvidia.com/maxine) · [SoundSwitch](https://github.com/Belphemur/SoundSwitch) (UX inspiration).
