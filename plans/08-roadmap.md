# 08 — Roadmap & Status

Phased implementation order. Each phase is independently shippable/testable. **Phase 2 alone fixes the "major delay"** and should be validated before going further. Update the checkboxes and the status line as you go so the next session knows where things stand.

## Current status
> **Phases 1–4 + most of Phase 6 implemented on branch `feat/engine-phase1-2` — `dotnet build -c Release` is green.** `EchoDeck.sln` = `EchoDeck.Engine` + `EchoDeck.App` + `EchoDeck.Tests` (xUnit; 24 passing unit tests over `RingBuffer` / `SampleConvert` / `ConfigStore`). App is a dark, DPI-safe, roomy WinForms tray UI. Working: device pickers showing **all device states** (active/disabled/unplugged), **AEC + Noise + Room Echo live toggles** (gated), **SDK + virtual-mic preflight**, **persisted settings** (`%APPDATA%\EchoDeck\config.json`), fat VU meters, and a **Broadcast-style test panel** (Record → play Input vs Output). **Pending: run on the rig to confirm parity / effects / latency.** The window is now **resizable + scrollable** with **pill toggles**, async device load, and a **latency readout**. The live chain rebuild is now **non-destructive** (build-new-then-swap; a failed reconfigure keeps the working chain), the far-end ring is **cleared when AEC toggles on** so cancellation starts from a clean reference, and the NvAFX hot path is **allocation-free** (no per-frame `IntPtr[]`). **Phase 8 in progress:** virtual-mic detection (Virtual-Audio-Driver + VB-Cable) + elevated install flow + UI are in; still needs the signed driver binary dropped into `drivers/` and an on-machine install test. Remaining: hotkeys + profiles (Phase 7), Studio Voice + intensity sliders, full `FarEndSource` clock + device-disconnect handling (Phase 5), packaging.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done

---

### Phase 1 — Scaffolding ✅
- [x] `git init` + `.gitignore` (done by maintainer; ignores `bin/`, `obj/`, `bridge.log`, `.claude/settings.local.json`)
- [x] Create solution; split into `EchoDeck.Engine` (net8.0 classlib) + `EchoDeck.App` (net8.0-windows WinExe)
- [x] Move `Program.cs` helpers into Engine (`Audio/SampleConvert`, `Interop/NvAfx`); old root console project removed
- [x] App builds; opens to a tray window with device pickers + Start/Stop *(confirm visually on the rig)*
- **Exit check:** `dotnet build -c Release` green ✓ — launch on the rig to confirm.
- Note: dedicated console harness skipped — the WinForms app exercises the engine directly. `--selftest` gives a headless smoke test, and `EchoDeck.Tests` (xUnit) covers the pure engine logic (ring buffer, sample conversion, config store) with no GPU/SDK dependency.

### Phase 2 — Latency fix (HIGHEST VALUE) — see [`02-audio-engine.md`](02-audio-engine.md)
- [x] Extract NvAFX interop into `Interop/NvAfx.cs`; in-process DLL search-dir setup (`NvAfx.EnsureSdkOnPath`, drops `start-bridge.bat`)
- [x] Replace `ConcurrentQueue<float>` with SPSC `RingBuffer`
- [x] Event-driven proc loop (`AutoResetEvent`) — removed `Thread.Sleep(1)` polling
- [x] Output bridge buffer 300 ms → 80 ms cap w/ low standing fill; `WasapiOut` 30 → 20 ms event-sync
- [x] Continuous mic/far backlog trim to ~30 ms (kills the 500 ms high-water)
- [ ] **AEC parity test vs the old app + measure latency — needs a run on the rig** ⬅ next verification
- **Exit check:** echo still cancelled; measured live latency ~30–60 ms (down from ~300–800 ms). **Validate on hardware.**

### Phase 3 — Devices + config ✅ — see [`05-device-switching.md`](05-device-switching.md)
- [x] `DeviceEnumerator` → `DeviceInfo`; select by `MMDevice.ID`
- [x] `EngineConfig` + `ConfigStore` (JSON in `%APPDATA%\EchoDeck`, temp-file + replace)
- [x] Removed hardcoded names; load/save device + effect selections (saved on change & on close)
- **Exit check:** change mic/reference/output without recompiling; survives restart ✓ (confirm on rig).

### Phase 4 — Effect chain ✅ (mostly) — see [`03-nvidia-effects.md`](03-nvidia-effects.md)
- [x] Generic `NvAfxEffect` + chain (`AudioEngine.RebuildChain`) — AEC → dereverb/denoiser
- [x] AEC + Denoiser + Dereverb + DereverbDenoiser, with the **collapse rule** when both echo+noise on
- [x] Runtime live toggle (chain rebuilt on the proc thread; best-effort error handling)
- [x] `NvAfxCapabilities.Probe()` → `SdkStatus`; UI gates toggles by installed models; Studio Voice probed (toggle UI deferred)
- [x] True keep-old-on-failure atomic swap — `RebuildChain` builds new handles first, swaps only on full success, disposes old after; failed load keeps the working chain
- [ ] Intensity sliders *(deferred polish)*
- **Exit check:** toggle each effect live; audible denoise/dereverb; no dropouts on toggle *(verify on rig)*.

### Phase 4.5 — Preflight (added) ✅
- [x] Startup SDK probe (DLL/models/version) + **virtual-mic detection**, surfaced as a status banner; effect toggles greyed out when their model is missing.

### Phase 5 — Engine robustness — see [`02-audio-engine.md`](02-audio-engine.md)
- [~] `FarEndSource` clocked silence + bounded backlog + transition fade — *partial:* zero-fill on far underrun + far-ring cleared on AEC-enable are in; the monotonic clock, `D ± slack` bounding, and transition fade are still TODO
- [x] `LevelMeter` (RMS/peak/clip) events — RMS/peak/clip raised via `InputLevel`/`OutputLevel`
- [x] `RunMicTestAsync` (dual raw+processed capture) — t0-aligned raw + processed WAVs
- [ ] `LatencyReport`; `ErrorOccurred` mapping (`EngineErrorCode`); `IMMNotificationClient` device-change handling ⬅ **next** (device-disconnect is the biggest remaining robustness gap)
- **Exit check:** AEC stable when speaker idle; meters move; test capture returns aligned WAVs; unplug handled.

### Phase 6 — GUI ✅ (mostly) — see [`06-gui-tray.md`](06-gui-tray.md)
- [x] `MainForm`: device dropdowns (all device states), effect toggles, VU meters (clip→red), Start/Stop, dark theme, roomy DPI-safe layout
- [x] Test panel: Record 5s → play Input vs Output (`AudioEngine.RunMicTestAsync` + `WaveOutEvent`)
- [x] Event marshalling to UI thread; meter throttling; preflight STATUS banner
- [x] Live latency readout; **resizable + scrollable** window (no clipping at any size/DPI); **pill toggle switches** (dark-visible); show/hide disabled & disconnected devices (async, no UI freeze)
- [ ] Intensity sliders + Studio Voice toggle *(deferred)*
- **Exit check:** full control of the engine from the window; A/B playback works *(verify on rig)*.

### Phase 7 — Tray + hotkeys + default-device switching — see [`05`](05-device-switching.md) / [`06`](06-gui-tray.md)
- [ ] `NotifyIcon` tray, minimize-to-tray, quick toggles, device submenus
- [ ] Single-instance mutex; run-at-login registry toggle (retire Task Scheduler + `start-bridge.bat`)
- [ ] `HotkeyManager` (`RegisterHotKey`/`WM_HOTKEY`)
- [ ] `PolicyConfig` (`IPolicyConfig.SetDefaultEndpoint`) → hotkeys/tray switch Windows default device
- **Exit check:** hotkey switches the system default device (visible in Sound settings); app autostarts to tray.

### Phase 8 — Virtual mic swap (#1 RISK) — see [`04-virtual-mic.md`](04-virtual-mic.md)
- [x] `VirtualMicManager` + `VirtualMicInfo` — detects Virtual-Audio-Driver **and** VB-Cable from the device snapshot; `Active` picks the best
- [x] Elevated install plumbing (`InstallVirtualAudioDriverAsync` runs the bundled `.bat`/`.exe`/`.inf` via `runas`) + `drivers/` drop-in convention
- [x] UI: virtual-mic status line + "Install virtual mic driver…" button (installs bundled, else opens the releases page); output endpoint auto-picks the active provider
- [ ] **Drop in the signed driver package + validate it installs WITHOUT test-signing on this Win11 box** ← go/no-go gate (needs the real binary + on-machine test)
- [ ] Once validated, route/verify in apps and make VB-Cable optional
- **Exit check:** apps capture processed audio from the virtual mic; VB-Cable no longer required (fallback still selectable).

### Phase 9 — Studio Voice — see [`03`](03-nvidia-effects.md) / [`07`](07-build-deploy.md)
- [ ] In-app "Get Studio Voice" helper (NGC steps); capability probe enables the toggle when model present
- [ ] Low-Latency for live (off by default); allow High-Quality in the recording/test path
- **Exit check:** with the model installed, Studio Voice toggles on; latency readout reflects ~110 ms.

### Phase 10 — Polish & packaging — see [`07-build-deploy.md`](07-build-deploy.md)
- [ ] Settings persistence finalised; icons; optional dark theme
- [ ] Installer (Inno/WiX) bundling app + driver + run-at-login; `THIRD_PARTY_NOTICES`
- [ ] Update root `README.md` for the one-app setup; remove stale Task Scheduler/VB-Cable instructions
- **Exit check:** clean install on a fresh profile yields a working virtual mic with effects, one app, autostart.

### Phase 11 — Discord integration — see [`11-discord-integration.md`](11-discord-integration.md)
*(only depends on Phase 7's hotkey/tray infra — can start any time after it; independent of Phases 8–10)*
- [ ] `Discord/DiscordRpcClient.cs` — named-pipe transport, handshake, nonce-matched commands, `VOICE_SETTINGS_UPDATE` subscription, reconnect backoff
- [ ] One-time setup wizard (user's own Discord app: client ID/secret → `AUTHORIZE` → token exchange → `AUTHENTICATE`); tokens DPAPI-encrypted
- [ ] "Manage Discord" mode: engine Running → NS/EC/AGC off (+ optional input=virtual mic), engine Stopped → restore remembered state (persisted, crash-safe)
- [ ] GUI Discord group (live NS/EC/AGC/mute/deafen pills, greyed when unavailable)
- [ ] Profile hook (`DiscordProfileSettings`, non-fatal apply) + mute/deafen hotkeys
- **Exit check:** starting the engine flips Discord's processing off automatically (visible in Discord settings); stopping restores it; profile switch applies its Discord block; all soft-fails when Discord is closed.

---

## Risk register (carry forward)
| Risk | Phase | Mitigation |
|---|---|---|
| Virtual-Audio-Driver needs test-signing / won't silently install | 8 | Use signed build; `VbCableProvider` fallback; go/no-go gate before removing VB-Cable |
| `IPolicyConfig` vtable differs on this Win11 build | 7 | Ship Vista+Win10 variants like SoundSwitch; test early |
| AEC far-end misalignment when speaker idle | 5 | Clocked silence + bounded backlog (already designed) |
| Studio Voice GPU cost / NGC access | 9 | Gated, off by default, recording-only |
| Kernel driver breaks on Windows update | 8/ongoing | Provider seam + VB-Cable fallback |
| Discord RPC restricted/changed by Discord | 11 | Feature-flagged, soft-fail; app fully functional without it; stable ~8 yrs (Stream Deck depends on it) |

## Validation (end-to-end, when feature-complete)
1. Build/run both projects; engine console harness prints device/format lines.
2. AEC parity: echo not present in the virtual mic.
3. Latency: round-trip click test ~30–60 ms (live readout corroborates).
4. Test panel: Record → Input vs Output A/B; denoise/dereverb audible.
5. Virtual mic: selectable in Windows Sound + Discord/Teams; carries processed audio.
6. Hotkeys: switch default playback/recording device.
7. Resources: idle CPU/RAM minimal; GPU modest with AEC+denoise.
8. Autostart: reboot → app to tray, virtual mic live, no manual steps.
