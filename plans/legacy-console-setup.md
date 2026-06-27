# Legacy — Original Console App Runbook

This is the operational runbook for the **original ChatGPT-built `nv-aec-bridge` console app** (the working prototype EchoDeck evolves from). Kept for reference while the GUI app is built and because these exact commands are still how the current bridge is operated. Design context is in the other `plans/` docs; verified facts are in [`01-environment.md`](01-environment.md).

## Pipeline (current/legacy)
```
Shure MV7+ mic  +  Modi 5 speaker loopback
  → NVIDIA Audio Effects SDK AEC (aec_48k.trtpkg)
  → VB-CABLE (CABLE In 16ch)
  → apps select "CABLE Output" as their mic
```
This is local reference-based AEC (NVIDIA Maxine / Audio Effects SDK), **not** Broadcast noise suppression.

## Device routing
| Role | Device |
|---|---|
| Real speaker output | `Speakers (3- Modi 5)` |
| Real microphone | `Microphone (2- Shure MV7+)` |
| Bridge output target | `CABLE In 16ch` (VB-Audio Virtual Cable) |
| App microphone selection | `CABLE Output` |
| App speaker selection | `Speakers (3- Modi 5)` |

Do **not** select the Shure mic directly in apps, or it bypasses the bridge.

## `mmsys.cpl` audio format settings
Set **all** of these to **48000 Hz** with **"Allow applications to take exclusive control" unchecked**:
- Playback: `Speakers (3- Modi 5)`, `CABLE In 16ch / CABLE Input`
- Recording: `Microphone (2- Shure MV7+)`, `CABLE Output`

## Hardcoded device names (in `Program.cs`)
```csharp
string micName     = "Microphone (2- Shure MV7+)";
string speakerName = "Speakers (3- Modi 5)";
string cableName   = "CABLE In 16ch";
```
If Windows renames devices after an update, list active endpoints and update the strings:
```powershell
Get-PnpDevice -Class AudioEndpoint |
  Sort-Object FriendlyName |
  Select-Object Status, FriendlyName, InstanceId
```
> EchoDeck removes this brittleness by selecting devices by `MMDevice.ID` from GUI dropdowns — see [`05-device-switching.md`](05-device-switching.md).

## Build / publish
```powershell
cd C:\Users\me\Repos\nv-aec-bridge
dotnet publish -c Release -r win-x64 --self-contained false
# → bin\Release\net8.0\win-x64\publish\nv-aec-bridge.exe
```
`OutputType=WinExe` runs it hidden; switch to `Exe` temporarily for a console window when debugging.

## Startup task (Task Scheduler)
```powershell
$exe = "C:\Users\me\Repos\nv-aec-bridge\bin\Release\net8.0\win-x64\publish\nv-aec-bridge.exe"
$workdir = Split-Path $exe
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $workdir
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName "NVIDIA AEC Bridge" -Action $action -Trigger $trigger `
  -Settings $settings -Description "Local NVIDIA AEC bridge to VB-CABLE" -Force
```
Manage it:
```powershell
Start-ScheduledTask -TaskName "NVIDIA AEC Bridge"
Get-Process nv-aec-bridge -ErrorAction SilentlyContinue            # health check
Get-Process nv-aec-bridge -ErrorAction SilentlyContinue | Stop-Process
Unregister-ScheduledTask -TaskName "NVIDIA AEC Bridge" -Confirm:$false
```
> EchoDeck replaces this with an in-process registry `Run`-key + tray (see [`06-gui-tray.md`](06-gui-tray.md)).

## Debug run (with SDK on PATH)
```powershell
cd C:\Users\me\Repos\nv-aec-bridge
$env:Path = "C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK;$env:Path"
dotnet run -c Release
```
Expected output: device/format lines, `NVIDIA AEC loaded.`, `RUNNING.`

## If the mic goes silent
1. App mic is set to `CABLE Output`.
2. Bridge process is running.
3. Windows didn't rename the devices.
4. Modi, Shure, and VB-CABLE are still 48000 Hz.
5. Exclusive mode still unchecked.
6. Debug-run (above) to see errors.
