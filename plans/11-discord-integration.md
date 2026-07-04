# 11 — Discord Integration (Voice-Settings Control via Local RPC)

Control Discord's voice processing from EchoDeck — the same local RPC surface Elgato Stream Deck uses. This is what makes the "one app" story complete for the primary use case: without it, the user must manually disable Discord's noise suppression / echo cancellation / AGC so they don't double-process EchoDeck's already-cleaned signal.

## Why
- EchoDeck's chain (AEC + denoise + dereverb) outputs a processed signal. Discord's own Krisp noise suppression + WebRTC AEC + AGC running **on top** causes over-suppression artifacts ("underwater"/robotic voice, ducking). The standard guidance for NVIDIA Broadcast users — turn Discord's processing off — applies identically here; EchoDeck should do it automatically.
- Discord can also be told **which input device to use** → EchoDeck can select its own virtual mic in Discord for the user. Removes the last manual setup step.
- Mute/deafen/PTT are natural targets for EchoDeck's hotkeys + profiles.

## What the RPC API can do (verified against Discord developer docs, 2026-07)
Commands `GET_VOICE_SETTINGS` / `SET_VOICE_SETTINGS` + event `VOICE_SETTINGS_UPDATE` cover:
| Field | Type | EchoDeck use |
|---|---|---|
| `noise_suppression`, `echo_cancellation`, `automatic_gain_control` | bool | auto-off while engine Running; restore on Stop |
| `input.device_id`, `input.volume` | string / 0–100 | point Discord at the EchoDeck virtual mic |
| `output.device_id`, `output.volume` | string / 0–200 | optional; profiles may set it |
| `mode` (PTT / VOICE_ACTIVITY, threshold, delay) | object | per-profile; hotkey PTT↔VAD flip |
| `mute`, `deaf` | bool | hotkeys + tray toggles |
| `qos`, `silence_warning` | bool | leave alone (expose nothing) |
All fields optional — send only what changes. `GET_VOICE_SETTINGS` returns `available_devices` (id + name) for the input/output objects.

**Device-ID mapping caveat:** Discord's `device_id` strings are its own (WebRTC enumeration), **not** `MMDevice.ID`. Match our virtual mic by **name** against `available_devices` (`VirtualMicManager.Active.CaptureEndpointName` ≈ device name), then send Discord's id back. Fuzzy-match tolerance needed (Discord may append/prefix).

## Transport & protocol
- **Named pipe** `\\?\pipe\discord-ipc-0` … `discord-ipc-9` — try in order until connect (browser Discord has **no** RPC; desktop client only; PTB/Canary share the same pipe range). WebSocket `localhost:6463–6472` exists as an alternative; pipe is primary on Windows.
- Framing: little-endian `int32 opcode` + `int32 length` + UTF-8 JSON payload.
- Opcodes: `0` handshake, `1` frame, `2` close, `3` ping, `4` pong.
- Handshake: send `{"v":1,"client_id":"<id>"}` → `READY` event arrives as a frame.
- Commands: `{"cmd":"SET_VOICE_SETTINGS","args":{…},"nonce":"<guid>"}`; responses matched by nonce. Events subscribed via `SUBSCRIBE` cmd (`VOICE_SETTINGS_UPDATE`).
- Implementation: a single `Discord/DiscordRpcClient.cs` in **EchoDeck.App** (engine stays audio-only). `NamedPipeClientStream` + `System.Text.Json`; ~300–400 lines, no new NuGet deps.

## Auth — the annoying part (one-time user setup)
Discord gates RPC to approved apps, **but** an unapproved app works when the connecting Discord account owns the app (or is on its tester list). The established OSS workaround (Touch Portal-style tools have shipped this for years): **each user creates their own free Discord application** and pastes its credentials into EchoDeck once.

Flow:
1. Wizard: link to `discord.com/developers/applications` → "New Application" → copy **Client ID** + **Client Secret** into EchoDeck. Also add `http://localhost` as an OAuth2 redirect URI (required for the token exchange to accept the code).
2. Connect pipe → handshake → `AUTHORIZE` cmd with scopes `rpc`, `rpc.voice.read`, `rpc.voice.write` → Discord pops an in-client approval dialog → returns an authorization `code`.
3. Exchange the code at `https://discord.com/api/oauth2/token` (plain `HttpClient`, `client_id` + `client_secret` + `grant_type=authorization_code`) → `access_token` + `refresh_token`.
4. `AUTHENTICATE` cmd with the access token → RPC session live.
5. Cache tokens in config, **DPAPI-encrypted** (`ProtectedData`, CurrentUser scope) — never plaintext. Refresh via `grant_type=refresh_token` on expiry; on `invalid_grant`, silently fall back to "needs re-auth" state and surface the wizard button.

## Behavior in EchoDeck
- Master toggle **"Manage Discord"** (off by default). When on and connected:
  - Engine → Running: `GET_VOICE_SETTINGS`, remember prior `noise_suppression`/`echo_cancellation`/`automatic_gain_control`, then set all three **off**; optionally set `input.device_id` to the virtual mic ("Also switch Discord's mic" sub-toggle).
  - Engine → Stopped/Faulted: restore the remembered values (never blind-set `true` — the user may have had them off already).
  - Remembered pre-EchoDeck state persists in config so a crash doesn't strand Discord with everything off.
- GUI: a Discord group with live pill toggles (NS / EC / AGC / mute / deafen) reflecting `VOICE_SETTINGS_UPDATE` — state shown is Discord's truth, not our last write.
- Degrade gracefully: Discord not running / not configured / pipe closed → grey the group, reconnect with capped exponential backoff (5 s → 60 s). All failures are toasts, never dialogs; never block audio.

## Profile integration (extends [`10-profiles.md`](10-profiles.md))
```csharp
public sealed class DiscordProfileSettings {   // all nullable = "leave alone"
    public bool? NoiseSuppression { get; set; }
    public bool? EchoCancellation { get; set; }
    public bool? AutomaticGainControl { get; set; }
    public bool? SetInputToVirtualMic { get; set; }
    public string? VoiceMode { get; set; }     // "PUSH_TO_TALK" | "VOICE_ACTIVITY" | null
}
// AudioProfile gains:  public DiscordProfileSettings? Discord { get; set; }
```
`ProfileManager.Apply` step 3.5: if connected and `Discord != null`, send one `SET_VOICE_SETTINGS` with only the non-null fields. **Non-fatal**: a Discord failure toasts a warning but never rolls back the audio switch.

## Hotkeys (extends [`05-device-switching.md`](05-device-switching.md))
`HotkeyConfig` gains `ToggleDiscordMute`, `ToggleDiscordDeafen` → `SET_VOICE_SETTINGS {mute/deaf}`. Reuses the existing `HotkeyManager`; no new infrastructure.

## Risks
| Risk | Mitigation |
|---|---|
| Unofficial-ish API; Discord could restrict it | Stable ~8 years; Stream Deck/commercial hardware depend on it. Feature-flagged, fails soft — EchoDeck works fully without it |
| Setup friction (user creates own Discord app) | Wizard with numbered steps + deep links; established pattern users of streamer tools know |
| Client secret + tokens on disk | DPAPI-encrypt; document in SECURITY.md |
| Discord device_id ≠ MMDevice.ID | Name-match against `available_devices`; surface "couldn't find virtual mic in Discord" toast |
| Restore-on-crash leaves Discord processing off | Persist pre-EchoDeck snapshot in config; offer "Restore Discord defaults" button |

## Open questions
- Minimal scope set — is `rpc` + `rpc.voice.write` enough, or is `rpc.voice.read` required for `GET_VOICE_SETTINGS`/events? (Request all three initially.)
- Access-token lifetime over the pipe — does an RPC session outlive token expiry, or must we re-`AUTHENTICATE` mid-session?
- Exact `available_devices` name format for the virtual mic (verify once the driver is installed — ties into [`04-virtual-mic.md`](04-virtual-mic.md) endpoint-naming validation).
- Whether restoring settings on engine Stop should be immediate or debounced (rapid Start/Stop flapping shouldn't strobe Discord's DSP).

## Roadmap placement
**Phase 11**, after tray/hotkeys (Phase 7) exist to hang the toggles on; independent of the virtual-mic driver work (Phase 8) and Studio Voice (Phase 9). The `DiscordRpcClient` + wizard can be built and tested standalone any time — only the profile/hotkey hooks need Phase 7. See [`08-roadmap.md`](08-roadmap.md).