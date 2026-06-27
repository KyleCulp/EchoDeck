<!-- Thanks for contributing to EchoDeck! Keep PRs focused on one logical change. -->

## Summary
<!-- What does this PR do and why? -->

Closes #

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / cleanup
- [ ] Docs / `plans/` contract update
- [ ] Build / CI / packaging

## How was it tested?
<!-- Audio changes need real-device testing. Note your hardware. -->
- GPU / driver:
- Windows build:
- Devices / effects exercised:

## Checklist
- [ ] `dotnet build -c Release` passes
- [ ] Ran `dotnet format` (style matches `.editorconfig`)
- [ ] Audio hot path stays allocation-free (if touched) — see `plans/02-audio-engine.md`
- [ ] Updated the relevant `plans/` doc if a component's contract changed
- [ ] Updated `CHANGELOG.md` (Unreleased) if user-facing
