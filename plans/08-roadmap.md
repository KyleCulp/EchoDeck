# 08 — Roadmap & Status

Phased implementation order. Each phase is independently shippable/testable. **Phase 2 alone fixes the "major delay"** and should be validated before going further. Update the checkboxes and the status line as you go so the next session knows where things stand.

## Current status
> **Phase 0 of build = not started. Design contract complete (docs 00–07).** The repo still contains only the original console app (`Program.cs`). Next action: Phase 1 scaffolding.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done

---

### Phase 1 — Scaffolding
- [ ] `git init` + `.gitignore` (`bin/`, `obj/`, `bridge.log`, `.claude/settings.local.json`)
- [ ] Create solution; split into `EchoDeck.Engine` (net8.0 classlib) + `EchoDeck.App` (net8.0-windows WinExe) — see [`07-build-deploy.md`](07-build-deploy.md)
- [ ] Move `Program.cs` helpers into Engine; keep a tiny console harness for engine testing
- [ ] App opens to an empty tray window; builds clean
- **Exit check:** `dotnet build -c Release` green; app launches.

### Phase 2 — Latency fix (HIGHEST VALUE) — see [`02-audio-engine.md`](02-audio-engine.md)
- [ ] Extract NvAFX interop into `Interop/NvAfx.cs`; in-process DLL search-dir setup (drop reliance on `start-bridge.bat`)
- [ ] Replace `ConcurrentQueue<float>` with SPSC `RingBuffer`
- [ ] Event-driven proc loop (`AutoResetEvent`) — remove `Thread.Sleep(1)`
- [ ] Output bridge buffer 300 ms → 20–30 ms; `WasapiOut` 30 → 20 ms event-sync
- [ ] Continuous mic-backlog trim to ~40 ms (kill the 500 ms high-water)
- [ ] AEC-only parity test vs current app; **measure latency**
- **Exit check:** echo still cancelled; measured live latency ~30–60 ms (down from ~300–800 ms). **Ship/validate here.**

### Phase 3 — Devices + config — see [`05-device-switching.md`](05-device-switching.md)
- [ ] `DeviceEnumerator` → `DeviceInfo`; select by `MMDevice.ID`
- [ ] `EngineConfig` + `ConfigStore` (JSON in `%APPDATA%`, atomic write)
- [ ] Remove hardcoded device names; load/save selections
- **Exit check:** change mic/reference/output without recompiling; survives restart.

### Phase 4 — Effect chain — see [`03-nvidia-effects.md`](03-nvidia-effects.md)
- [ ] `IAudioEffectStage` / `NvAfxStage` / `EffectChain`
- [ ] AEC + Denoiser + Dereverb + DereverbDenoiser (collapse rule when both on)
- [ ] Runtime toggle + intensity; atomic chain rebuild (keep-old-on-failure)
- [ ] `NvAfxCapabilities.Probe()` → `AvailableEffects`; Studio Voice stage present but gated
- **Exit check:** toggle each effect live; audible denoise/dereverb; no dropouts on toggle.

### Phase 5 — Engine robustness — see [`02-audio-engine.md`](02-audio-engine.md)
- [ ] `FarEndSource` clocked silence + bounded backlog + transition fade
- [ ] `LevelMeter` (RMS/peak/clip) events
- [ ] `RunMicTestAsync` (dual raw+processed capture)
- [ ] `LatencyReport`; `ErrorOccurred` mapping; `IMMNotificationClient` device-change handling
- **Exit check:** AEC stable when speaker idle; meters move; test capture returns aligned WAVs; unplug handled.

### Phase 6 — GUI — see [`06-gui-tray.md`](06-gui-tray.md)
- [ ] `MainForm`: device dropdowns, effect toggles + sliders, VU meters + clip LED, latency label, Start/Stop
- [ ] Test panel: Record → play Input vs Output
- [ ] Event marshalling to UI thread; meter throttling
- **Exit check:** full control of the engine from the window; A/B playback works.

### Phase 7 — Tray + hotkeys + default-device switching — see [`05`](05-device-switching.md) / [`06`](06-gui-tray.md)
- [ ] `NotifyIcon` tray, minimize-to-tray, quick toggles, device submenus
- [ ] Single-instance mutex; run-at-login registry toggle (retire Task Scheduler + `start-bridge.bat`)
- [ ] `HotkeyManager` (`RegisterHotKey`/`WM_HOTKEY`)
- [ ] `PolicyConfig` (`IPolicyConfig.SetDefaultEndpoint`) → hotkeys/tray switch Windows default device
- **Exit check:** hotkey switches the system default device (visible in Sound settings); app autostarts to tray.

### Phase 8 — Virtual mic swap (#1 RISK) — see [`04-virtual-mic.md`](04-virtual-mic.md)
- [ ] `IVirtualMicProvider` seam; `VbCableProvider` (fallback) + `VirtualAudioDriverProvider` (default)
- [ ] **Validate the signed Virtual-Audio-Driver installs WITHOUT test-signing on this Win11 box** ← go/no-go gate
- [ ] Elevated install/enable automation (project tooling or `pnputil`+`devgen`/`devcon`)
- [ ] Route engine output to the virtual mic; verify in apps
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

---

## Risk register (carry forward)
| Risk | Phase | Mitigation |
|---|---|---|
| Virtual-Audio-Driver needs test-signing / won't silently install | 8 | Use signed build; `VbCableProvider` fallback; go/no-go gate before removing VB-Cable |
| `IPolicyConfig` vtable differs on this Win11 build | 7 | Ship Vista+Win10 variants like SoundSwitch; test early |
| AEC far-end misalignment when speaker idle | 5 | Clocked silence + bounded backlog (already designed) |
| Studio Voice GPU cost / NGC access | 9 | Gated, off by default, recording-only |
| Kernel driver breaks on Windows update | 8/ongoing | Provider seam + VB-Cable fallback |

## Validation (end-to-end, when feature-complete)
1. Build/run both projects; engine console harness prints device/format lines.
2. AEC parity: echo not present in the virtual mic.
3. Latency: round-trip click test ~30–60 ms (live readout corroborates).
4. Test panel: Record → Input vs Output A/B; denoise/dereverb audible.
5. Virtual mic: selectable in Windows Sound + Discord/Teams; carries processed audio.
6. Hotkeys: switch default playback/recording device.
7. Resources: idle CPU/RAM minimal; GPU modest with AEC+denoise.
8. Autostart: reboot → app to tray, virtual mic live, no manual steps.
