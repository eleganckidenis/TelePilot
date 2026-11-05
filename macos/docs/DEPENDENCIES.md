# TelePilot native macOS dependencies

This target uses Swift 6, Foundation, AppKit, CryptoKit, Security and system zlib on macOS 14 or newer. Its executable is native Mach-O. It does not bundle .NET, Avalonia, LibVLCSharp, or a browser runtime. The requested deliverable targets Apple Silicon (arm64) only. Intel is explicitly out of scope.

## Reproducible package inputs

Run from the native project directory after the current full test run succeeds:

```sh
scripts/package-app.sh --vlc-dmg /path/to/vlc-3.0.23-arm64.dmg
```

The default release is read from `VERSION`; output goes to `artifacts/<version>/arm64/`. It contains the installation DMG with an Applications link, a resource-preserving app ZIP, and `SHA256SUMS`. A verified loose `TelePilot.app` is also copied there when the volume permits it. If File Provider immediately restores prohibited Finder metadata, the invalid loose copy is removed and a verified local app is retained under a printed `/tmp/telepilot-native-app-VERSION-ARCH.XXXXXX/TelePilot.app` path; the DMG and ZIP remain the distributable artifacts. Existing output requires explicit `--force`. The selected Apple Silicon release requires its matching image and native host. Historical x86_64 build options and source pins are not tested or delivered.

The script runs the tests, builds the Swift release executable with an app-relative LibVLC runtime search path, generates the app icon from the original TelePilot PNG when no native ICNS exists, and builds/signs in a temporary local directory. Swift module/build caches are under `/tmp`. `--skip-tests` is available after a successful full test run of the same source. Bundle identifier: `pl.telepilot.native`. The plist uses the numeric release version for Apple's bundle version fields and retains the full version in `TelePilotDistributionVersion` and artifact names. `NSAllowsArbitraryLoads` supports user-supplied HTTP IPTV endpoints; it does not grant access to protected streams.

## VLC 3.0.23 / LibVLC

Only the official VideoLAN 3.0.23 disk images are accepted:

| Native architecture | Official image | SHA-256 |
| --- | --- | --- |
| arm64 | [vlc-3.0.23-arm64.dmg](https://download.videolan.org/pub/videolan/vlc/3.0.23/macosx/vlc-3.0.23-arm64.dmg) | `fc6fac08d87f538517d44aca0c5e7a244b67c8c4cb589bf478363a7315fd5e0d` |
| x86_64 | [vlc-3.0.23-intel64.dmg](https://download.videolan.org/pub/videolan/vlc/3.0.23/macosx/vlc-3.0.23-intel64.dmg) | `ec01530ce69d849dd057fba8876e68ac39bf279dc28de4e9c04e4aec11fc98db` |

The hashes match the upstream TelePilot macOS packaging pins at commit `b727566a4910c1e735340bd150f50a1569ec6b51`. Packaging verifies the image hash before mounting read-only, checks VLC's version, verifies the entire source app's strict code signature and requires `Developer ID Application` with VideoLAN team identifier `75GAHG3SZQ`. A trap detaches the script's own mount when it exits. An arbitrary installed VLC app is not an accepted substitute.

The official `lib`, `plugins` and `share` directories are copied to `Contents/Frameworks/libvlc/`. Source plugin-cache paths are removed. The obsolete `libosx_notifications_plugin.dylib` is deliberately excluded: it requires Growl.framework solely for VLC GUI notifications, while TelePilot uses AppKit/VoiceOver announcements. This exclusion is recorded in the plugin inventory; the dependency gate remains strict for every included module. TelePilot's C bridge loads LibVLC dynamically; LGPL library replacement remains possible for local builds (a changed signed bundle must be re-signed). Before signing, every Mach-O must contain the release architecture. The script parses `otool` load commands, resolves loader/executable/rpath references, and rejects dependencies escaping the app except Apple's `/usr/lib/` and `/System/` libraries. Thus Homebrew/MacPorts paths are not implicitly redistributed dependencies.

The package contains a sorted plugin inventory and the exact official DMG checksum in `Contents/Resources/licenses/VLC_PLUGIN_INVENTORY.txt`. LibVLC is LGPL-2.1-or-later; VLC's plugin set includes GPL components and libraries with additional license terms. The official DMG contains no standalone complete third-party contrib notice set. The included upstream COPYING/COPYING.LIB, AUTHORS and THANKS preserve VLC's general license/credit documents; they do not establish the completeness of every statically incorporated contrib notice. Supply the matching full notice set with `--vlc-license-dir DIR` before distributing a release that requires those notices. The script preserves that directory under `licenses/VLC-contrib`.

VLC source and build materials: [VideoLAN 3.0.23 release directory](https://download.videolan.org/pub/videolan/vlc/3.0.23/), [VLC 3.0.23 source tag](https://github.com/videolan/vlc/tree/3.0.23), [contrib build recipes](https://github.com/videolan/vlc/tree/3.0.23/contrib/src). Retain matching source/build materials when redistributing binaries under their applicable terms.

## Optional FFmpeg recorder

Without `--ffmpeg`, no FFmpeg executable is included. An existing compatible FFmpeg can be selected in application settings. To bundle a verified build, provide all three inputs:

```sh
scripts/package-app.sh --vlc-dmg /path/to/vlc-3.0.23-arm64.dmg \
  --ffmpeg /path/to/ffmpeg \
  --ffmpeg-manifest /path/to/source-manifest.json \
  --ffmpeg-license-dir /path/to/complete-license-notices
```

The binary is copied to `Contents/Resources/tools/ffmpeg`. The JSON manifest must identify the architecture, binarySHA256, name, version, license, configureArguments, and source URLs with SHA-256 hashes. Its architecture and binary checksum are verified. The input manifest, entire license directory, input executable SHA-256 before bundle signing, `ffmpeg -version` and `ffmpeg -buildconf` are retained under `Contents/Resources/licenses/ffmpeg/`. The packager's Mach-O closure checks also apply to FFmpeg: a binary depending on an unbundled Homebrew library is rejected. FFmpeg is a separate subprocess; its actual build configuration determines LGPL/GPL licensing. Keep the corresponding source, patches and build recipe with its manifest. License text for an unrelated build is not a replacement for matching notices.

## Project license and retained source documents

Public packages also supply `--vlc-license-dir` with the complete retained
VLC3.0.23 contrib notices archive from the main repository (including optional
source components). The corresponding VLC/contrib source bundle is available in
[Release v1.0.0](https://github.com/eleganckidenis/TelePilot/releases/tag/v1.0.0).
The native FFmpeg source archives, manifest, licenses and build recipe accompany
the GitHub Release as `TelePilot-<version>-macos-third-party-sources.zip`. This is separate from
the older Windows FFmpeg download, which is supplied directly by its publisher.

`Resources/licenses/TelePilot-LICENSE.txt` (GPL-3.0) and `TelePilot-NOTICE.txt` were fetched from [eleganckidenis/TelePilot](https://github.com/eleganckidenis/TelePilot/tree/b727566a4910c1e735340bd150f50a1569ec6b51). The retained LGPL text comes from the same commit. `VLC-COPYING.txt`, `VLC-COPYING.LIB.txt`, `VLC-AUTHORS.txt` and `VLC-THANKS.txt` come from VideoLAN's `3.0.23` source tag. These files and this document are copied into every native package.

## Signing and notarization

The default signature is ad-hoc for local testing. For Developer ID signing, provide `--sign-identity 'Developer ID Application: …'`; for notarization also provide `--notary-profile PROFILE` referring to existing Keychain credentials. A notary profile without an identity is rejected. VLC resource files under Frameworks receive generic nested-code signatures retained in archive resource metadata. Mach-O components are signed inside out, then the enclosing app is signed and checked with `codesign --verify --deep --strict`. Notarization submits and staples the app before final archives, then submits/staples the signed DMG. The final ZIP is extracted and its app signature verified again; File Provider FinderInfo metadata is removed from the workspace copy while preserving code-signature attributes; the copied app (with a verified local fallback outside File Provider) and DMG are also verified. An ad-hoc build is not a notarized public release.

User delivery decision, 2026-10-30: the Apple Silicon DMG uses the validated free ad-hoc signature. Paid Developer ID and notarization are intentionally omitted; optional signing switches remain available for future use.
