# 10 — Audio Profiles

The feature that makes EchoDeck worth using over a one-off script: **named, switchable bundles of a full audio setup** — like SoundSwitch profiles, but including the effect chain. Builds directly on [`05-device-switching.md`](05-device-switching.md) (devices + `IPolicyConfig` + hotkeys) and [`02`](02-audio-engine.md)/[`03`](03-nvidia-effects.md) (engine + effects).

## What a profile is
A profile captures everything needed to switch context in one click/hotkey:
- **EchoDeck routing:** mic device, AEC reference (speaker to loopback), output (virtual mic) — all by `MMDevice.ID`.
- **Effect chain:** which effects are on + their intensities + Studio Voice mode.
- **Optional Windows defaults:** whether selecting this profile should also set the Windows default playback/recording device (via `IPolicyConfig`) — so e.g. switching to "Speakers" also makes Windows route system audio there.
- **Optional Discord settings:** per-profile Discord voice config (processing on/off, PTT vs VAD, follow the virtual mic) via local RPC — see [`11-discord-integration.md`](11-discord-integration.md).

### Example profiles (the user's real setups)
| Profile | Output (Windows default?) | Mic | AEC ref | Effects |
|---|---|---|---|---|
| Gaming | Headphones ✓ | Headset mic | Headphones | AEC + Noise |
| Desk | Headphones ✓ | Desktop mic | Headphones | AEC + Noise + Echo |
| Speakers | Speakers ✓ | Desktop mic | Speakers | AEC (high intensity) + Noise |
| Recording | Headphones | Shure / IEM mic | — (AEC off) | Noise + Studio Voice (HQ) |

## Data model (extends `EngineConfig` — see [`05`](05-device-switching.md))
```csharp
public sealed class AudioProfile {
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); // stable
    public string Name { get; set; } = "";
    public string? InputDeviceId { get; set; }
    public string? FarEndDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }     // usually the virtual mic; can override
    public EffectChainConfig Chain { get; set; } = new();
    public bool SetWindowsDefaultPlayback { get; set; }
    public string? WindowsDefaultPlaybackId { get; set; }   // null = same as FarEndDeviceId
    public bool SetWindowsDefaultRecording { get; set; }
    public string? WindowsDefaultRecordingId { get; set; }
    public string? Hotkey { get; set; }             // e.g. "Ctrl+Alt+1"
    public string? AppTrigger { get; set; }         // optional: process/window name to auto-activate (nice-to-have)
    public DiscordProfileSettings? Discord { get; set; }   // null = don't touch Discord (schema in 11)
}

// add to EngineConfig:
//   public List<AudioProfile> Profiles { get; set; } = new();
//   public string? ActiveProfileId { get; set; }
```
Persisted in the same `%APPDATA%\EchoDeck\config.json`.

## Applying a profile (atomic)
`ProfileManager.Apply(profile)`:
1. Set engine `InputDeviceId` / `FarEndDeviceId` / `OutputDeviceId` (restart only the changed capture/render nodes).
2. Set `Chain` and call `ApplyChain()` (atomic rebuild — keep-old-on-failure, see [`03`](03-nvidia-effects.md)).
3. If `SetWindowsDefault*`, call `IPolicyConfig.SetDefaultEndpoint(...)` for the chosen device(s).
4. If `Discord != null` and the RPC client is connected, send one `SET_VOICE_SETTINGS` with the non-null fields ([`11`](11-discord-integration.md)) — **non-fatal**: failure toasts a warning, never rolls back the audio switch.
5. Set `ActiveProfileId`; raise `ProfileChanged`; toast the new profile name; persist.
Failure in steps 1–3 → roll back to the previous profile and raise `ErrorOccurred`.

## Switching UX
- **GUI:** a profile dropdown/segmented control at the top of `MainForm`; an editor to create/rename/duplicate/delete profiles and capture the current setup as a new profile ("Save current as profile…").
- **Tray:** profiles listed in the context menu for one-click switching; active one checked.
- **Hotkeys:** per-profile direct hotkey (`Ctrl+Alt+1…`) and/or a "cycle profiles" hotkey, via the existing `HotkeyManager` ([`05`](05-device-switching.md)).
- **(Nice-to-have) app triggers:** auto-activate a profile when a given app/window is focused (SoundSwitch has this) — foreground-window watcher → match `AppTrigger`. Defer past v1.

## Relationship to plain device switching
- A **profile** changes EchoDeck's whole setup (routing + effects + optionally Windows default).
- A bare **device hotkey** ([`05`](05-device-switching.md)) only flips the Windows default device, independent of EchoDeck.
Keep both: profiles for "my whole setup", device hotkeys for quick one-offs.

## Roadmap placement
Lands after the GUI + device switching are working — slot into **Phase 7** (or a Phase 7.5) once `IPolicyConfig` + hotkeys + config persistence exist. Add to [`08-roadmap.md`](08-roadmap.md) when scheduling.

## Open questions
- Should the virtual-mic output ever differ per profile, or always be the EchoDeck virtual mic? (Default: always the virtual mic; allow override for power users.)
- Conflict UX when a profile references a now-absent device (offer to re-pick / fall back).
- How profiles interact with "start hidden at login" — restore `ActiveProfileId` on startup.
