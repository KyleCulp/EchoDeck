# Security Policy

EchoDeck runs with access to your audio devices and can **install a kernel-mode virtual audio driver**, so we take security reports seriously.

## Supported versions
EchoDeck is pre-`v0.1`; security fixes target the latest `main` and the most recent release.

| Version | Supported |
|---|---|
| `main` / latest release | ✅ |
| older pre-releases | ❌ |

## Reporting a vulnerability
**Please do not open a public issue for security problems.**

- Preferred: open a private [GitHub Security Advisory](../../security/advisories/new) on this repo.
- Or email the maintainer: **me@kyleculp.com** (use a clear subject like "EchoDeck security").

Include: a description, repro steps, affected version/OS/GPU, and impact. We'll acknowledge within a few days and keep you updated on the fix.

## Scope to keep in mind
- **Driver install/elevation:** EchoDeck installs/manages a virtual audio driver with admin rights. Issues in the install path, elevation, or bundled driver binaries are in scope.
- **Default-device switching:** uses the undocumented `IPolicyConfig` COM interface.
- **Native interop:** P/Invoke into the NVIDIA `NVAudioEffects.dll` and Win32 audio APIs.

## Out of scope
- Vulnerabilities in the NVIDIA Audio Effects SDK itself (report to NVIDIA) or in the upstream Virtual-Audio-Driver project (report there), unless EchoDeck uses them unsafely.
