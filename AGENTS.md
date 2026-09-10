# TelePilot repository instructions

## Start here

Read these files before changing the project:

1. `docs/MACOS_HANDOFF.md` when working on macOS.
2. `README.md` for user-visible behavior and build entry points.
3. `IMPLEMENTATION_PLAN.md` for architecture and quality gates.
4. `docs/ACCESSIBILITY_TEST_CHECKLIST.md` and `docs/RELEASE_CHECKLIST.md` before a release.
5. `CHANGELOG.md` for the current version history.

The current verified Windows baseline is TelePilot 2.0.1. Preserve its behavior and
tests while adding macOS support. Isolate platform-specific code behind OS/RID checks
only where it is actually needed.

## Accessibility is a release gate

TelePilot is keyboard-first. A change is not complete merely because it compiles.

- Keep every primary action reachable without a mouse.
- Keep Tab and Shift+Tab cyclic and restore focus after dialogs, menus, list refreshes,
  fullscreen transitions, and player/list view changes.
- Video surfaces must not become the keyboard focus target.
- Controls need stable accessible names, roles, values, and status announcements.
- On macOS, verify the real NSAccessibility tree with VoiceOver and Accessibility
  Inspector. Headless Avalonia tests do not replace this check.
- Re-run the regression: select a channel with Enter, stop it, select a different
  channel, press Enter, then Tab. The second player must remain responsive and focus
  must reach the player controls.
- Prefer standard macOS menu behavior and Command shortcuts while retaining sensible
  compatibility aliases where appropriate.

## Private fixtures and secrets

`djdenis.m3u` and `url.txt` are private local integration fixtures. They are ignored
by Git and must remain ignored.

- Never print or commit their contents, URL host, path, query, token, credentials, or
  derived secrets.
- Run the URL test only when `TELEPILOT_RUN_PRIVATE_INTEGRATION=1` was set explicitly.
- Never put Apple signing certificates, `.p12` files, passwords, App Store Connect API
  keys, or notary credentials in the repository or in a Codex prompt. Use Keychain or
  CI secrets.

## Build hygiene

- The SDK is pinned by `global.json` to .NET 10.0.302 with compatible patch roll-forward.
- Restore packages on the target operating system.
- Do not commit or transfer `.dotnet`, `.tools`, `artifacts`, `.artifacts`,
  `.test-output`, `bin`, `obj`, logs, settings, recordings, or runtime data.
- Obtain native LibVLC only from the pinned official VideoLAN DMG. Verify its
  published SHA-256, version, Developer ID signature, and TeamIdentifier before
  repackaging it. Keep ARM64 and x64 binaries separate and preserve all applicable
  license notices, including an inventory for bundled VLC/FFmpeg plugins.
- A loose `dotnet publish` folder or a Windows cross-publish is not a macOS release.
  The release artifacts are separate, tested `.app` bundles and DMGs for `osx-arm64`
  and `osx-x64`, signed with Developer ID, notarized, and stapled.

## Required verification

Before claiming a macOS release:

1. Run the full automated suite and the private smoke tests without exposing data.
2. Validate every Mach-O architecture with `file`/`lipo` and dependencies with
   `otool -L`.
3. Test playback, EPG, fullscreen, tracks, volume, source import/removal, managed
   playlist copies, and single-instance activation on macOS.
4. Complete the VoiceOver checklist on the target architecture.
5. Verify `codesign`, Gatekeeper, notarization, stapling, DMG integrity, licenses,
   and SHA-256 checksums.
