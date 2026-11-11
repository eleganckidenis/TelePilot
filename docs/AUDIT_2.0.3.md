# TelePilot 2.0.3 audit

Baseline: public v2.0.2, commit 99fde6ea0e4fa09c2dd0c925e66840a7eef6aab3.
Scope: native macOS Apple Silicon and Windows x64/ARM64; synthetic playlists and
local HTTP streams, without private playlists or the user's profile.

## Confirmed failures and regressions

- Windows XMLTV reads lost the cancellation token once XmlReader started consuming
  the stream. Stalled XML/gzip transfers kept the EPG service gate locked.
  `CancelStalledReadReleasesGateForNextRefresh` and the parser cancellation tests
  reproduce the failure and verify that the next refresh succeeds.
- Closing the Windows guide cancelled channel loading but left guide refreshes
  running. `Dispose_CancelsPendingGuideRefresh` verifies cancellation at disposal.
- macOS HLS variant addresses used the original master URL after an HTTP redirect.
  `redirectedHLSProbeResolvesVariantsAgainstFinalURL` checks the redirected path.
- macOS remote M3U import, cache and refresh used the original URL for relative
  channel and guide addresses. `redirectedPlaylistRetainsFinalBaseAcrossCacheAndRefresh`
  verifies import, reopening the encrypted copy, and refreshing via the original
  source URL. The new resolved URL stays inside the encrypted payload; older
  payloads remain readable.
- macOS recording rejected RTMPS although the player and bundled FFmpeg support
  it. The recording regression verifies the accepted input arguments.
- Repeated macOS quit requests started overlapping asynchronous shutdowns. The
  termination regressions reproduce the extra preparation/negative reply, check
  a single shutdown, and verify cancellation followed by retry.

Each new regression was observed failing before its corresponding fix. The
focused Windows EPG/guide suite passes all 40 cases. NuGet's advisory check reports
no vulnerable direct or transitive packages in the eight .NET projects.

Native Windows tests and actual install/reinstall/uninstall checks for both
architectures are recorded by the release workflow. The release description links
to the exact run and records the final results. macOS release checks use the native
Swift suite, bundled LibVLC/FFmpeg, synthetic network playback, temporary test keys
and launchd jobs, and the packaged application.

## Distribution and verification limits

VLC 3.0.24, LibVLCSharp and the FFmpeg dependency versions are unchanged. The
matching VLC source archive remains the public v2.0.1 asset with SHA-256
800818898a94ce8ebaa5bfbbbe1246251ca19ef990e735b60ae26d1ab0e8df93.
Source links, license notices, architecture/dependency checks, app signatures,
installer checksums, and public release downloads are retained in the release
process. The Mac package is signed ad hoc, without Apple notarization. Windows
installers remain unsigned.

Automated keyboard/accessibility-tree checks do not prove audible VoiceOver,
NVDA or Narrator behavior. No full screen-reader listening session or test of a
private IPTV provider is claimed. The audit found and repaired the documented
failures; it does not establish that every possible application behavior is free
of defects.
