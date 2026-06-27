# 09 — Open-Source Repo Setup

Contract for how the EchoDeck repo is run as an OSS project. The files below were scaffolded at the repo root; this doc records the decisions behind them.

## Identity
- **Name:** EchoDeck (dropped the `nv-` prefix — NVIDIA is a trademark and the app is broader than AEC now).
- **License:** **MIT** (`LICENSE`). Most adoption-friendly; compatible with the bundled MIT/MS-PL driver and MIT NAudio.
- ⚠️ Name is not unique — other "EchoDeck" products exist (a soundboard web app, a sales-analytics tool). Fine for an OSS desktop tool under the maintainer's account, but not a registrable-unique brand. Revisit before any trademark ambitions.

## Files scaffolded (repo root)
| File | Purpose |
|---|---|
| `LICENSE` | MIT |
| `README.md` | Front door: pitch, features, requirements, how-it-works, build, profiles, links |
| `THIRD_PARTY_NOTICES.md` | NAudio (MIT), Virtual-Audio-Driver (MIT/MS-PL), **NVIDIA SDK not redistributed**, VB-Cable (fallback) |
| `CONTRIBUTING.md` | Dev env, build/run, layout, style, PR process |
| `CODE_OF_CONDUCT.md` | Contributor Covenant 2.1 |
| `SECURITY.md` | Private vuln reporting (matters — app installs a kernel driver) |
| `CHANGELOG.md` | Keep a Changelog + SemVer |
| `.gitignore` | .NET + **never commit NVIDIA DLLs/`.trtpkg`/driver `.sys`** |
| `.editorconfig` | C# style for consistent PRs |
| `Directory.Build.props` | Shared metadata (version, authors, MIT, nullable, repo URL) |
| `.github/workflows/ci.yml` | Build on `windows-latest` for PRs |
| `.github/workflows/release.yml` | On `v*` tag: publish → zip → draft GitHub Release |
| `.github/ISSUE_TEMPLATE/*` | Bug (forces GPU/OS/SDK/device details) + feature + config |
| `.github/PULL_REQUEST_TEMPLATE.md` | Summary, testing-with-hardware, checklist |
| `.github/dependabot.yml` | Weekly NuGet + actions updates |

## Placeholders to fix after creating the GitHub repo
- Replace **`OWNER`** with the GitHub username/org in: `README.md` badges/links, `Directory.Build.props` `RepositoryUrl`, `CHANGELOG.md`, `.github/ISSUE_TEMPLATE/config.yml`, `release.yml` is fine as-is.
- Confirm the **copyright holder** name in `LICENSE` / `Directory.Build.props` (currently "Kyle Culp").
- Add a **CI badge** to the README after the first push.

## Repo settings to apply on GitHub (manual)
- Description + topics: `audio`, `nvidia`, `noise-suppression`, `echo-cancellation`, `wasapi`, `virtual-audio`, `microphone`, `windows`, `dotnet`, `csharp`.
- Branch protection on `main`: require PR + passing CI (+ review once there are co-maintainers).
- Enable Discussions; enable private security advisories.
- Social preview image; a demo GIF in the README (huge for adoption).
- Seed a few `good first issue` / `help wanted` labels from the roadmap.

## Release process
1. Land changes via PR; update `CHANGELOG.md` (Unreleased).
2. Bump `<Version>` in `Directory.Build.props`; move Unreleased → a dated version section.
3. Tag `vX.Y.Z` and push → `release.yml` builds, packages `EchoDeck-vX.Y.Z-win-x64.zip`, opens a **draft** release with auto-notes.
4. Review artifacts, attach the installer (once one exists), publish.
- **SemVer:** pre-`1.0` while the API/UX churns; `1.0` when the GUI + virtual mic + profiles are stable.

## Project-specific gotchas (do NOT regress)
- ⚠️ **Never commit** `NVAudioEffects.dll`, CUDA DLLs, or `.trtpkg` models (proprietary + large) — users install the NVIDIA SDK themselves.
- ⚠️ **Code-signing:** an unsigned installer that drops a kernel driver will trip SmartScreen/AV. Document this; ideally sign the installer (EV cert). The *driver* signing is upstream's responsibility — ship their signed build (see [`04-virtual-mic.md`](04-virtual-mic.md)).
- Keep `EchoDeck.Engine` free of WinForms so it stays testable/portable.
- Bundled driver: preserve upstream `THIRD_PARTY_NOTICES` + MS-PL notices.

## Build matrix note
CI builds the current single console project today and will build `EchoDeck.sln` after the Phase 1 split — both are picked up by `dotnet build`/`dotnet restore` at the repo root with no workflow change. Tests get enabled in CI once `EchoDeck.Engine.Tests` exists.
