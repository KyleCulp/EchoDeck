# 05 — Device Switching, Config & Hotkeys

Covers the "like SoundSwitch" features plus the engine's config schema. Two distinct things:
1. **In-app routing devices** — which mic/reference/output the engine uses (saved to config).
2. **Windows default-device switching** — global hotkeys + tray to change the system default playback/recording device (SoundSwitch parity).

## Device enumeration
- Use NAudio `MMDeviceEnumerator.EnumerateAudioEndpoints(DataFlow, DeviceState.Active)`.
- Map to `DeviceInfo(Id, FriendlyName, Flow, IsDefault, MixSampleRate, MixChannels)`.
- **Persist by `MMDevice.ID`** (the stable `{0.0.0.00000000}.{guid}` endpoint string), never FriendlyName — names change on driver updates (the whole reason the current hardcoded-name approach is brittle). Cache FriendlyName for display + as a fuzzy fallback if the ID disappears after a driver reinstall (then re-resolve and rewrite config).
- Replaces the current `FindDevice(...)` substring match in `Program.cs`.

## Config schema (`%APPDATA%\EchoDeck\config.json`)
`System.Text.Json`, indented, atomic write (temp file + `File.Move` over) to survive crashes. `Environment.GetFolderPath(SpecialFolder.ApplicationData)`.
```csharp
public sealed class EngineConfig {
    public string SchemaVersion { get; set; } = "1";
    public string? InputDeviceId  { get; set; }   // mic
    public string? FarEndDeviceId { get; set; }   // speaker to loopback (AEC ref)
    public string? OutputDeviceId { get; set; }   // virtual-mic render endpoint
    public EffectChainConfig Chain { get; set; } = new();
    public int  TargetOutputLatencyMs   { get; set; } = 20;
    public bool OutputExclusiveMode     { get; set; } = false;  // default shared
    public int  RingCapacityMs          { get; set; } = 80;
    public int  LatencyTrimThresholdMs  { get; set; } = 40;
    public HotkeyConfig Hotkeys { get; set; } = new();
}

public sealed class EffectChainConfig {           // fixed logical order; only enabled run
    public bool Aec { get; set; }                  // requires FarEndDeviceId
    public bool Denoiser { get; set; }
    public bool Dereverb { get; set; }
    public bool DereverbDenoiser { get; set; }     // auto-used when Denoiser && Dereverb
    public bool StudioVoice { get; set; }          // gated by AvailableEffects; ~110 ms
    public float AecIntensity { get; set; } = 1f;
    public float DenoiserIntensity { get; set; } = 1f;
    public float DereverbIntensity { get; set; } = 1f;
    public StudioVoiceMode StudioVoiceMode { get; set; } = StudioVoiceMode.LowLatency;
}
public enum StudioVoiceMode { LowLatency, HighQuality }

public sealed class HotkeyConfig {
    public string? CyclePlaybackDevice { get; set; }   // e.g. "Ctrl+Alt+F11"
    public string? CycleRecordingDevice { get; set; }  // e.g. "Ctrl+Alt+F7"
    public string? ToggleMuteMic { get; set; }
    public List<string> PlaybackCycleListDeviceIds { get; set; } = new();   // optional curated cycle set
    public List<string> RecordingCycleListDeviceIds { get; set; } = new();
}
```
Defaults mirror SoundSwitch (Ctrl+Alt+F11 playback, Ctrl+Alt+F7 recording).

## Setting the Windows default device — `IPolicyConfig`
NAudio can enumerate and read default devices but **cannot set** the default. Use the **undocumented COM interface `IPolicyConfig`** (CLSID `CPolicyConfigClient`) — the same mechanism SoundSwitch, AudioSwitcher, etc. use. Add `App/PolicyConfig.cs` with the COM interop:
```csharp
// CLSID_CPolicyConfigClient = {870af99c-171d-4f9e-af0d-e63df40c2bc9}
// IID_IPolicyConfig         = {f8679f50-850a-41cf-9c72-430f290290c8}
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig {
    // ... (HRESULT methods; the one we need:)
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    // GetMixFormat / SetEndpointVisibility / etc. — declare full vtable in order, even if unused
}
enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
```
Call `SetDefaultEndpoint(deviceId, eConsole)` (and usually `eMultimedia` + `eCommunications`) to fully switch. **Vtable order matters** — declare every method in the interface in the correct order even if unused, or the layout breaks. (Alternative: the `AudioSwitcher.AudioApi.CoreAudio` NuGet wraps this, but a hand-rolled interop keeps deps minimal per the "minimal resource" goal.)

## Global hotkeys
- Win32 `RegisterHotKey(hwnd, id, modifiers, vk)` + handle **`WM_HOTKEY`** in the WinForms message pump (override `WndProc` on a hidden message window or the tray's form). Unregister on exit.
- Map hotkey → action: cycle to next playback/recording device (from the curated `*CycleListDeviceIds`, or all active endpoints if empty) and call `IPolicyConfig.SetDefaultEndpoint`. Show a brief toast/balloon of the new device (SoundSwitch-style).
- Parse hotkey strings ("Ctrl+Alt+F11") → modifier flags + virtual-key code.

## Tray integration
The tray context menu (see [`06-gui-tray.md`](06-gui-tray.md)) also lists playback/recording devices for click-to-set-default, reusing the same `IPolicyConfig` path.

## Relationship to engine routing
Two different concepts, don't conflate:
- **Engine routing devices** (`InputDeviceId`/`FarEndDeviceId`/`OutputDeviceId`) = what the bridge captures/renders. Changing these reconfigures the engine.
- **Windows default device** (via hotkeys/tray) = system-wide default for *all* apps. Independent of the bridge; this is the pure SoundSwitch feature.

## Open questions
- Confirm the exact `IPolicyConfig` vtable for current Windows 11 (there are `IPolicyConfig` / `IPolicyConfigVista` variants; SoundSwitch ships both).
- Whether to also expose "mic mute" with an on-screen banner (SoundSwitch has it) — nice-to-have, low priority.
- Conflict handling if a chosen hotkey is already registered by another app (`RegisterHotKey` fails → surface to user).
