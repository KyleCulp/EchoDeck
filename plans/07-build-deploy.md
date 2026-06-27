# 07 — Build, Solution Structure & Deploy

## Solution layout
Convert the single-project repo into a 2-project solution:
```
EchoDeck.sln
  EchoDeck.Engine/   net8.0          class library, AllowUnsafeBlocks, NAudio   (no WinForms)
  EchoDeck.App/      net8.0-windows  WinExe, UseWindowsForms, refs Engine + NAudio
```
Rationale: keeps the engine framework-agnostic and unit-testable; the App holds all WinForms/COM/driver code. The current single `nv-aec-bridge.csproj` is split accordingly.

### `EchoDeck.Engine.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>   <!-- fixed* pointers for NvAFX_Run -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.3.0" />  <!-- bump from 2.2.1 for loopback-silence behavior -->
  </ItemGroup>
</Project>
```

### `EchoDeck.App.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationManifest>app.manifest</ApplicationManifest>   <!-- declare requireAdministrator only for the install helper, not the main app -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\EchoDeck.Engine\EchoDeck.Engine.csproj" />
    <PackageReference Include="NAudio" Version="2.3.0" />
  </ItemGroup>
</Project>
```
> Current project is `net8.0` `WinExe` with NAudio 2.2.1 (the original `WinExe` note: it runs hidden — that behavior is now handled explicitly via start-to-tray).

## Build / run
```powershell
# from repo root
dotnet build -c Release
dotnet run   -c Release --project EchoDeck.App        # dev run
dotnet publish EchoDeck.App -c Release -r win-x64 --self-contained false
```
`.claude/settings.local.json` already allows `dotnet:*` so these won't prompt.

The NVIDIA SDK is **not** copied into the build — it's a machine-installed dependency at `C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK`. The app sets that dir on the DLL search path **in-process** at startup (replaces `start-bridge.bat`'s `PATH` shim). Do **not** vendor `NVAudioEffects.dll` / CUDA DLLs into the repo (size + license).

## Studio Voice model download (NGC) — optional, for the gated effect
1. Create a free NVIDIA NGC account; generate an **NGC API key**.
2. Use the AFX SDK's download script (org `nvidia`, team `maxine`) to fetch `studio_voice_48k.trtpkg` (and/or 16k).
3. Drop it in `…\NVIDIA Audio Effects SDK\models\` (or a path set in `EngineConfig`).
4. The app's capability probe picks it up automatically → the Studio Voice toggle un-greys.
Provide an in-app "Get Studio Voice" helper that links to the steps (don't bundle the model — license + size).

## Virtual-Audio-Driver bundling (see [`04-virtual-mic.md`](04-virtual-mic.md))
- Ship the **signed** driver package alongside the app (MIT/MS-PL → include `THIRD_PARTY_NOTICES`).
- Install/uninstall via the project's signed tooling or `pnputil`+`devgen`/`devcon`, **elevated**. Keep the main app non-elevated; elevate only the install helper (separate manifest or `runas` relaunch).

## Packaging (later)
- Simplest: a folder/zip of the publish output + the driver package + a first-run setup that installs the driver and offers run-at-login.
- Nicer: an installer (Inno Setup — what SoundSwitch uses, or WiX/MSIX). MSIX complicates driver install (sandbox) → prefer Inno/WiX for a tool that installs a kernel driver.
- Not a git repo yet — consider `git init` + a `.gitignore` for `bin/`, `obj/`, `bridge.log`, and `.claude/settings.local.json`.

## Migration off the old setup
| Old | New |
|---|---|
| `start-bridge.bat` (PATH shim + run + log) | in-process DLL path + start-to-tray |
| Task Scheduler "NVIDIA AEC Bridge" | `HKCU\…\Run` registry value (UI toggle) |
| Hardcoded device names in `Program.cs` | `EngineConfig` device IDs (GUI dropdowns) |
| `bridge.log` redirect | optional in-app log/diagnostics pane |
| Console `Exe`/`WinExe` flip for debugging | `dotnet run` on the Engine with a tiny console harness if needed |

## Verification commands
See [`08-roadmap.md`](08-roadmap.md) for the full end-to-end test plan; minimal smoke test:
```powershell
dotnet build -c Release
dotnet run -c Release --project EchoDeck.App
# → app opens to tray; pick devices; toggle AEC; confirm virtual mic carries processed audio in Sound settings
```
