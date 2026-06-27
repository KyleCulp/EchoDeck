# 06 — GUI & Tray (`EchoDeck.App`)

WinForms (`net8.0-windows`, `WinExe`, `UseWindowsForms=true`). Chosen for **minimal resource use** (no GPU compositor like WPF) and easy tray integration; SoundSwitch is WinForms too. The app is a thin shell over `AudioEngine` ([`02-audio-engine.md`](02-audio-engine.md)).

## Layout (mirrors NVIDIA Broadcast's Audio tab)
```
┌─ EchoDeck ───────────────────────────────────┐
│ Devices                                       │
│   Mic        [ Microphone (Shure MV7+)   ▼]   │
│   Reference  [ Speakers (Modi 5)         ▼]   │   ← far-end for AEC
│   Virtual mic[ Virtual Mic Driver        ▼]   │   ← output target
│                                               │
│ Effects                                       │
│   ◉ AEC               [ON ]  intensity [====] │
│   ◉ Noise removal     [off]  intensity [====] │
│   ◉ Room echo removal [off]  intensity [====] │
│   ◉ Studio voice      [off]  (recording only) │   ← greyed if model absent
│                                               │
│ Levels                                        │
│   in  ▮▮▮▮▮▮▯▯▯  ⬤clip   out ▮▮▮▮▯▯▯▯       │
│                                               │
│ Test microphone effects                       │
│   [ ● Record 5s ]   [▶ Input]   [▶ Output]    │
│                                               │
│ Latency: ~42 ms   |   [ Start ] [ Stop ]      │
└───────────────────────────────────────────────┘
```

## Controls → engine bindings
| Control | Bound to |
|---|---|
| 3 device combo boxes | `GetInputDevices()`/`GetRenderDevices()`; set `InputDeviceId`/`FarEndDeviceId`/`OutputDeviceId` |
| Effect toggles | `SetEffectEnabled(EffectType, bool)`; disabled if not in `AvailableEffects` |
| Intensity sliders | `SetEffectIntensity(EffectType, float 0–1)` |
| `in`/`out` VU meters + clip LED | `InputLevel`/`OutputLevel` events (`Rms`/`Peak`/`Clip`) |
| Record / Input / Output | `RunMicTestAsync(5s)` → play `RawWav` / `ProcessedWav` via `WaveOutEvent` |
| Latency label | `GetLatencyReport().TotalMs`; shows Studio Voice's ~110 ms when on |
| Start / Stop | `Start()` / `Stop()` |
| Status / error strip | `StateChanged` / `ErrorOccurred` (device missing, model missing, GPU error) |

UI thread marshalling: engine events arrive on worker threads → use `Control.BeginInvoke` (or a `SynchronizationContext` captured at startup). Throttle meter repaints to `LevelUpdateIntervalMs` (~30 fps).

## Test panel (Broadcast parity)
"Record" calls `RunMicTestAsync(5s)`; on completion enable "Input" (plays `RawWav`) and "Output" (plays `ProcessedWav`) so the user A/Bs raw vs processed — same idea as Broadcast's "Record speech / Input audio / Output audio". Allow High-Quality Studio Voice here (offline). Optionally a "Save samples" button (writes the two WAVs).

## Tray (`NotifyIcon`)
- Always-present tray icon; **minimize-to-tray** (intercept form close/minimize → hide).
- Context menu: master Start/Stop, quick effect toggles, **playback/recording device submenus** (click-to-set-default via `IPolicyConfig`, see [`05-device-switching.md`](05-device-switching.md)), Open window, Quit.
- Balloon/toast on hotkey device switch and on errors.
- Icon state reflects running/stopped/faulted.

## Startup & lifecycle (`App/Startup.cs`)
- **Single instance** (named `Mutex`); second launch surfaces the existing window.
- **Run-at-login:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value pointing at the exe (a checkbox in the UI toggles it). **Replaces** the Task Scheduler task "NVIDIA AEC Bridge" + `start-bridge.bat`.
- In-process DLL search-dir setup for the NVIDIA SDK at startup (folds in `start-bridge.bat`'s PATH shim — see [`03-nvidia-effects.md`](03-nvidia-effects.md)).
- Optionally **start hidden** (to tray) when launched at login; show window on user launch.
- Persist window/effect/device state via `EngineConfig.SaveConfig()` on change and on graceful exit.

## Resource posture (the "minimal" goal)
- Idle: just the message pump + tray + (optionally) a paused engine → near-zero CPU, small RAM.
- Running: capture/render threads + one proc thread; GPU light with AEC+denoise, heavier only with Studio Voice.
- Avoid timers that wake constantly; drive meters off engine events, not a UI poll loop.

## Files (`EchoDeck.App`)
```
Program.cs (entry, single-instance, DLL path setup, hidden-start)
MainForm.cs / MainForm.Designer.cs
TrayIcon.cs
HotkeyManager.cs        (05)
PolicyConfig.cs         (05)
VirtualMicProvider*.cs  (04)
Startup.cs              (run-at-login, mutex)
appsettings/icon assets
```

## Open questions
- Visual style: plain WinForms vs a light dark-theme (manual colors). Default to functional/plain first; theme later.
- Whether to show the live latency number always or behind an "advanced" expander.
