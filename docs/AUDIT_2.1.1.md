# TelePilot 2.1.1 updater audit

Baseline: public v2.1.0, commit ef72b7021e616cd5e99436386d38826a8a1e9b50.
The reported starting version is the original public v2.0.2. Its four updater
implementation files are byte-identical to the v2.1.0 baseline.

## Confirmed failures

The old startup updater performs the entire installer download before presenting
any notice. A slow transfer therefore appears to do nothing. HTTP failures return
the same empty result as no newer version, other failures are swallowed, and a
process-wide one-shot flag prevents another check until restart. There is no
manual check, progress indication or retry action. Presentation also waits for an
active, enabled main window with no modal dialog.

Testing the metadata downloader with an older version number does not prove the
startup UI works. This audit explicitly includes unmodified public 2.0.2 binaries,
fresh synthetic profiles and genuine public update files. On macOS the original
binary downloaded the full 80,435,270-byte 2.1.0 DMG, then showed its native offer
when reactivated. From launch to the observed notice was 7 minutes 13 seconds,
including transfer and waiting for foreground presentation. OK opened the image
and the old process exited. This confirms a silent/delayed-notice failure, rather
than an incompatible version comparison or missing public installer.

On native Windows x64 and ARM64 runners, the original public 2.0.2 installers
were installed unchanged. Fresh profiles contained a real, checksum-verified
2.1.0 installer in the update cache. The actual application startup displayed its
accessible installer offer after 1.815 seconds (x64) and 2.542 seconds (ARM64).
The tests invoked Later and closed the old application. No replacement checker
or altered executable version was used. Evidence:
[original Windows startup run](https://github.com/eleganckidenis/TelePilot/actions/runs/43037177607).

## Updated behavior and verification

The updater retains automatic startup checking and downloading, and reports a
new version before waiting for the complete file. A nonmodal update window shows
progress and cancellation; manual checking is available in Help. Failures,
cancelled operations and no-update results are distinct. Repeated actions share
one operation; retry, close and shutdown must not let old completions change a
new operation. Transient metadata checks have bounded retries.

The existing stable-version, architecture, canonical URL, trusted HTTPS redirect,
size and SHA-256 checks remain mandatory. A cached file is verified, and opening
an installer requires re-verification. macOS quarantine and Windows Internet-zone
markers are preserved. macOS opens its image and defers application termination
after OK; cancelled recording-quit consent leaves the application usable.

Regression coverage includes checking versus downloading, early availability,
progress, manual/startup coalescing, cancellation and partial cleanup, retry,
late callbacks, cached/corrupt files, transient and permanent HTTP errors,
malformed metadata, foreground/modal handling and application shutdown. Final
native-platform counts, original Windows 2.0.2 startup evidence, packaged UI
results and public 2.0.2-to-2.1.1 download checks are recorded in the release notes.

An independent gpt-6-astra/high review identified prepublication issues in the
replacement UI: rate-limit retries, deferred/modal presentation, internal timeout
reporting, replacement-panel identity and manual checking during ready delivery.
Each was corrected and rechecked. Native macOS testing additionally found that an
ordinary utility panel could take keyboard focus on reactivation; automatic
panels now remain passive until explicit user interaction. The native tests
exercise focus, cancellation, retry, ready confirmation, Later and real keyboard
quit. A test-harness key-delivery problem after Later was separated from product
behavior using the native Quit action and fresh keyboard/HID runs; no speculative
shutdown workaround was added.

## Distribution and limits

The original 2.0.2 executable is immutable: a new release cannot change code
already installed on another computer. It can download a correctly published
new release, but retains its delayed notice and silent-error behavior until
upgraded. The README provides the official manual installation route.

Tests use isolated profiles and synthetic media, not private IPTV accounts or
the user's stored settings. Automated accessibility/keyboard checks do not prove
audible VoiceOver/NVDA behavior. macOS remains Apple Silicon, signed ad hoc
without Apple notarization; Windows x64/ARM64 installers remain unsigned.
Dependency versions and matching source archives are unchanged.
