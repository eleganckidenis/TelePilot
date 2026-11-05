# TelePilot 2.0.1 — release verification

The linked GitHub Actions run in the release notes identifies the commit and the tested Windows installers. Both Windows workflows run on their target architecture. They test LibVLC playback, FFmpeg recording, the installed process and native DLL architecture, both installer languages, the language choice in Settings after a process restart, reinstall, cleanup of old VLC plugins, and uninstall. The separate FFmpeg download test checks extraction, the pinned checksum and rejection of an invalid checksum.

The native macOS suite exercises the Settings language selection, playlist management, focus restoration, playback with a synthetic stream, EPG, FFmpeg and scheduled recording. The Apple Silicon DMG is mounted read-only to verify its version, architecture, and the app’s ad-hoc signature. The bundled VLC on all platforms is 3.0.24. Its accompanying source archive includes the upstream source, verified contrib sources, build configuration, binary inventory and notices. The optional upstream Tremor target for processors without an FPU is not used by these packages.

The public release assets, checksums and GitHub-generated source archives are compared against the release metadata and tagged source. The update service is tested against the public packages for Windows x64, Windows ARM64 and macOS.

The tests use synthetic fixtures. Native accessibility checks do not substitute for a manual listening pass with NVDA or VoiceOver. The Windows installers are unsigned, and the macOS app is signed ad hoc without Apple notarization.
