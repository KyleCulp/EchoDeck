# 04 — Virtual Mic (Provider Seam + Driver)

How the app presents a microphone to other apps. **This is the #1 risk in the project** — validate the driver install path before ripping out VB-Cable.

## The hard constraint
A microphone that other apps can select is an **OS audio endpoint**, which requires a **kernel-mode audio driver**. A .NET app cannot *be* a virtual mic — it can only render audio *into* one that a driver provides. So "get rid of VB-Cable" really means "swap VB-Cable for a different driver we bundle/manage," not "remove the driver layer." The user accepted this and chose to go OSS from the start.

## Decision
- **Primary:** bundle and manage **Virtual-Audio-Driver** (github.com/VirtualDrivers/Virtual-Audio-Driver), using its **signed** build so Windows test-signing mode is *not* required.
- **Fallback:** keep a VB-Cable path behind an abstraction, so if the OSS driver won't install cleanly on this machine the user isn't stuck.

## How Virtual-Audio-Driver works (confirmed)
- MIT-licensed (original code) + MS-PL (Microsoft sample portions) → bundling/redistribution OK with attribution (`THIRD_PARTY_NOTICES.md`).
- Creates a **virtual speaker** (render) + **virtual mic** (capture). Audio written to the virtual speaker is routed to the virtual mic through an internal **ring buffer — exactly like a physical audio cable** (same model as VB-Cable). So our engine's render stage is unchanged: render processed audio to the virtual speaker endpoint; apps capture from the virtual mic.
- Supports Windows 10 (1903+) and 11.
- The VirtualDrivers org now ships a **signed Virtual Audio Driver** with install/uninstall/enable/disable tooling ("Virtual Driver Control"). Using the signed build avoids `bcdedit /set testsigning on`. **The older beta required test-signing — must confirm the signed build is what we ship.**

## The provider seam
```csharp
public interface IVirtualMicProvider {
    string DisplayName { get; }
    bool IsInstalled { get; }
    string? RenderEndpointId { get; }   // MMDevice ID the engine renders into
    string? CaptureEndpointName { get; }// what the user selects in other apps
    Task<bool> EnsureInstalledAsync(IProgress<string>? log = null);  // elevated
    Task EnableAsync();  Task DisableAsync();
    Task UninstallAsync();
}
```
Implementations:
- `VirtualAudioDriverProvider` (default) — bundles the signed driver package; install/enable/disable via the project's tooling or `pnputil` + `devgen`/`devcon`.
- `VbCableProvider` (fallback) — detects an existing VB-Cable install; `RenderEndpointId` = "CABLE In", `CaptureEndpointName` = "CABLE Output". No install automation (user installs VB-Cable manually if this path is chosen).

The engine's `OutputDeviceId` is set from the active provider's `RenderEndpointId`. The GUI shows `CaptureEndpointName` as "set this as your mic in Discord/Teams".

## Install automation (Virtual-Audio-Driver)
Order of preference:
1. **Use the project's own signed installer/CLI** if the release provides one (cleanest; handles devnode creation + signing).
2. Otherwise: stage the package with `pnputil /add-driver <path>\*.inf /install`, then create the software devnode (these virtual drivers are root-enumerated, so a plain `pnputil` install won't spawn the device) via `devgen /add /instanceid … <hwid>` or `devcon install <inf> <hwid>`.
3. All of the above require **admin elevation** — the app relaunches elevated (UAC) or ships a small elevated helper just for install/uninstall.

Detect-first: only install if `IsInstalled` is false. Never reinstall on every launch.

## Risks & validation gate
- **Signing:** confirm the bundled build is production-signed and installs **without** test-signing on this Win11 box. If it needs test-signing, that's a security/UX downgrade (watermark) → surface to the user as a go/no-go before proceeding.
- **Windows-update fragility:** kernel audio drivers can break on feature updates. The `VbCableProvider` fallback is the insurance.
- **Silent install of a root-enumerated devnode** is the fiddly part — budget time to get `pnputil`+`devgen`/`devcon` (or the project installer) working unattended + elevated.
- **Endpoint naming / multiple instances:** the driver may expose a single fixed-name pair; confirm naming so the GUI instructions are accurate.

## Roadmap placement
Implemented **last** (roadmap Phase 8), *after* the engine + effects + GUI work on the existing VB-Cable so the high-value latency/effects wins land with zero driver risk. Switching the default provider from VB-Cable → Virtual-Audio-Driver is then a contained change behind `IVirtualMicProvider`. See [`08-roadmap.md`](08-roadmap.md).
