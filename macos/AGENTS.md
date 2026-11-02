# TelePilot native macOS

Separate Swift 6 / AppKit application. Apple Silicon (arm64) only; Intel is explicitly out of scope at the user's request. Windows behavior baseline: eleganckidenis/TelePilot commit b727566a4910c1e735340bd150f50a1569ec6b51 (public 1.1.1). Read README.md and docs/VERIFICATION.md before changes. Do not substitute the old internal 2.0.2 baseline for current public Windows functionality.

- Keep native keyboard/VoiceOver semantics and named controls. Preserve Play -> Stop -> another channel -> Play -> Tab regression, cyclic Tab, visible focus restoration, and nonfocusable video.
- Never print/read private playlists or url.txt as part of ordinary tests. Synthetic integration fixtures suffice. Do not publish personal settings, recordings, tokens or Keychain entries.
- Keep URLs and playlist bytes encrypted at rest, with keys/secrets in Keychain. No URL or FFmpeg stderr in diagnostics.
- Run scripts/test.sh; real system tests require explicit TELEPILOT_RUN_SYSTEM_INTEGRATION=1, touch only their own random task/secret, and clean up.
- Packaging requires pinned official VLC DMG SHA256/version/Developer ID/TeamID verification and architecture/dependency checks. Keep complete license/source notices. Never describe ad-hoc local artifacts as notarized releases.
- User decision (2026-10-30): deliver an Apple Silicon DMG with the free ad-hoc signature; skip paid Developer ID/notarization. Do not treat Intel testing or paid signing as blockers for this requested local package. Preserve strict ad-hoc signature validation and describe the artifact accurately.
- Actual VoiceOver listening and complete VLC contrib notices remain separate verification/distribution considerations. Record evidence accurately; automated AX inspection does not prove speech behavior.
