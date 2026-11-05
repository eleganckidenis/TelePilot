# TelePilot 2.0.0 — release verification

The published release and its linked GitHub Actions run are the record for the exact Windows installer and source archive. The Windows workflow tests the app on Windows, builds the actual installer, checks the system language and both explicit installer languages, tests uninstall, and verifies the pinned FFmpeg download and rejection of an incorrect checksum. When the runner provides a desktop, it also checks the native UI Automation tree during first launch and after reinstall.

On Apple Silicon, the native suite passed 75 tests using synthetic playlists and media. It covered the playlist manager, language and preferences, focus cycling and restoration, VLC playback and HLS, EPG, recording with FFmpeg, Keychain and launchd. The distribution image contains only arm64 native code; its bundled app passes strict ad-hoc signature checks. The final image is mounted read-only to check the application version, signature, architecture, and both installation guides.

Both update clients were exercised with the live GitHub release endpoint and real release assets. Tests also cover stable version selection, wrong platform or URL, invalid digest or size, corrupt cached files, and interrupted downloads. Neither updater automatically runs an installer. The release audit compares the source archive to the release commit and checks every downloaded release asset and its SHA-256 against the public latest-release metadata. See `scripts/verify-release-assets.py`.

Automated accessibility checks do not substitute for listening to NVDA or VoiceOver. Private IPTV playlists and external provider accounts were not used. The Windows installer is unsigned and the Apple Silicon package is signed ad hoc without Apple notarization.
