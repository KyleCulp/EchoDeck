# Bundled virtual-mic driver (drop-in)

EchoDeck needs a virtual **microphone** so other apps (Discord, OBS, browsers…) can
capture its processed output. A .NET app can't *be* a microphone — Windows requires a
kernel audio driver — so EchoDeck manages an external one.

## How EchoDeck uses this folder

At runtime EchoDeck looks for a driver package at:

```
drivers/VirtualAudioDriver/
```

If that folder exists and contains an installer, the **"Install virtual mic driver…"**
button in the app runs it **elevated** (UAC), in this preference order:

1. `*.bat` / `*.cmd`  (the project's install script)
2. `*.exe`            (a setup executable)
3. `*.inf`            (falls back to `pnputil /add-driver … /install`)

If the folder is empty/absent, the button instead opens the
[Virtual-Audio-Driver releases page](https://github.com/VirtualDrivers/Virtual-Audio-Driver/releases)
so you can grab the signed build and run its installer yourself.

EchoDeck then **auto-detects** the installed endpoints (no manual cable picking):
- renders its output to the driver's *virtual speaker*,
- and tells you to select the driver's *virtual mic* in your apps.

VB-Cable is also auto-detected as a fallback provider (nothing to bundle — install it
from vb-audio.com if you prefer it).

## What to put here

Download the **signed** [Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver)
release and place its contents in `drivers/VirtualAudioDriver/` (the `.inf`, `.sys`,
`.cat`, and the install script/exe).

> ⚠️ **Binaries are not committed to this repo.** `.gitignore` excludes `*.sys` and
> `*.cat`; releases fetch/stage them at packaging time. Do not commit the kernel driver
> binaries here.

## Signing / safety notes

- Use the project's **production-signed** build so Windows installs it without
  test-signing mode. If only a test-signed build is available, the user must enable
  test signing (`bcdedit /set testsigning on`) — surface that before proceeding.
- License: MIT + MS-PL (Microsoft WDM audio sample portions). Preserve the upstream
  `THIRD_PARTY_NOTICES` when bundling. See the repo root [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).
- Kernel audio drivers can break on Windows feature updates — the `IVirtualMicProvider`
  abstraction keeps VB-Cable available as a fallback.

See [`../plans/04-virtual-mic.md`](../plans/04-virtual-mic.md) for the full design.
