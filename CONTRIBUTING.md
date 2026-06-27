# Contributing to EchoDeck

Thanks for wanting to help! EchoDeck is a Windows audio tool that wraps the NVIDIA Audio Effects SDK with a tray GUI, a virtual mic, and device/profile switching.

## TL;DR
1. Read [`plans/README.md`](plans/README.md) — the design contract. It explains every component.
2. Pick something from the [roadmap](plans/08-roadmap.md) or a "good first issue".
3. Fork → branch → build → PR. Keep PRs focused.

## Dev environment
- Windows 10 (1903+) / 11, 64-bit
- **NVIDIA RTX GPU** + the [NVIDIA Audio Effects SDK](https://developer.nvidia.com/maxine) installed at `C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK` (not redistributed; you install it)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Optional for the virtual-mic work: the signed [Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver)

You can build without an RTX GPU/SDK (the NVIDIA calls are runtime P/Invoke), but you can't run the effects pipeline.

## Build & run
```powershell
dotnet restore
dotnet build -c Release
# once the app project exists (roadmap Phase 1):
dotnet run -c Release --project EchoDeck.App
```

## Project layout (target)
```
EchoDeck.Engine/   audio engine, NVIDIA interop  (framework-agnostic)
EchoDeck.App/      WinForms tray GUI, hotkeys, driver mgmt
plans/             design contract (read before coding a component)
```
See [`plans/07-build-deploy.md`](plans/07-build-deploy.md) for the full structure.

## Coding guidelines
- Style is enforced by [`.editorconfig`](.editorconfig); run `dotnet format` before pushing.
- Keep the **audio hot path allocation-free** (see [`plans/02-audio-engine.md`](plans/02-audio-engine.md)) — no LINQ/boxing/allocations per frame.
- The engine must not reference WinForms; UI-specific code lives in `EchoDeck.App`.
- Match the existing comment density and naming of nearby code.
- Add/update the relevant `plans/` doc when you change a component's contract.

## Commits & PRs
- Conventional Commits encouraged: `feat:`, `fix:`, `docs:`, `refactor:`, `perf:`, `chore:`.
- One logical change per PR; describe **what** and **why**, and how you tested (audio changes need real-device testing — note your GPU + Windows build).
- CI (build) must pass. Be ready to iterate on review.

## Reporting bugs / ideas
Use the issue templates. For anything security-related (this app can install a kernel driver), follow [SECURITY.md](SECURITY.md) instead of a public issue.

## Code of Conduct
This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). Be respectful.
