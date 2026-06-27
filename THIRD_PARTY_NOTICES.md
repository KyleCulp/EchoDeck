# Third-Party Notices

EchoDeck depends on, and in some cases redistributes, the following third-party
components. Their licenses are reproduced/linked below. EchoDeck itself is
licensed under the [MIT License](LICENSE).

---

## Used at build/runtime (NOT redistributed by EchoDeck)

### NVIDIA Maxine Audio Effects SDK (AFX)
- **Role:** provides the AEC / denoiser / dereverb / Studio Voice effects, called via P/Invoke into `NVAudioEffects.dll`.
- **License:** proprietary — NVIDIA Software License Agreement + Product-Specific Terms.
- **Redistribution:** **EchoDeck does NOT bundle or redistribute the SDK, its DLLs, or its `.trtpkg` models.** Users must download and install the SDK from NVIDIA themselves and accept NVIDIA's license. EchoDeck only calls the installed library at runtime.
- https://developer.nvidia.com/maxine

### NAudio
- **Role:** WASAPI capture/render, device enumeration, resampling, WAV I/O.
- **License:** MIT.
- https://github.com/naudio/NAudio

---

## Bundled / installed with EchoDeck

### Virtual-Audio-Driver (VirtualDrivers)
- **Role:** provides the virtual microphone endpoint EchoDeck renders into (replaces VB-Cable).
- **License:** MIT (original code) + Microsoft Public License (MS-PL) for the Microsoft WDM audio sample portions.
- **Redistribution:** the signed driver package may be bundled per its MIT/MS-PL terms; this notice and the upstream `THIRD_PARTY_NOTICES` are preserved.
- https://github.com/VirtualDrivers/Virtual-Audio-Driver

### VB-CABLE (optional fallback only)
- **Role:** alternative virtual-mic provider if the user prefers it.
- **License:** VB-Audio donationware EULA. **Not bundled** — users install it themselves if they choose this fallback.
- https://vb-audio.com/Cable/

---

## Acknowledgements / inspiration (no code used)
- **SoundSwitch** — inspiration for the device-switching + hotkey UX. https://github.com/Belphemur/SoundSwitch
- **NVIDIA Broadcast** — the app whose Audio-tab UX EchoDeck reimplements in-process.

> If you add a dependency, record it here in the same format as part of your PR.
