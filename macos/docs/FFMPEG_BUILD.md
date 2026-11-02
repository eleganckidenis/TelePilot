# Rebuilding the bundled FFmpeg

Run on a native Apple Silicon or Intel Mac with Command Line Tools and Python 3:

```sh
./scripts/build-ffmpeg.sh --output /tmp/telepilot-ffmpeg-rebuild
```

The script installs only inside that directory. It builds local pkgconf, static LAME
and Opus, then FFmpeg with its default internal codecs, formats, filters and network
protocols. SecureTransport supplies TLS and macOS supplies zlib. It targets macOS 14,
disables external-library autodetection, and enables no GPL/nonfree components.
FLV/RTMP and native Vorbis support remain available. Intel builds without `nasm`
use `--disable-x86asm`; no toolchain or system packages are installed.

The default output is `/tmp/telepilot-ffmpeg-repro-arm64` or
`/tmp/telepilot-ffmpeg-repro-x86_64`. Existing output is preserved unless `--force`
is supplied; even then the script replaces only directories bearing its own marker.
Build paths must contain only letters, digits, `_`, `-`, `.`, and `/` because upstream
makefiles do not reliably support spaces. Use `--jobs N` to limit concurrency.

## Pinned inputs and provenance

The script pins release URLs and SHA-256 values verified against the official
[FFmpeg Homebrew formula](https://github.com/Homebrew/homebrew-core/blob/master/Formula/f/ffmpeg.rb),
[LAME formula](https://github.com/Homebrew/homebrew-core/blob/master/Formula/l/lame.rb),
[Opus formula](https://github.com/Homebrew/homebrew-core/blob/master/Formula/o/opus.rb),
and [pkgconf formula](https://github.com/Homebrew/homebrew-core/blob/master/Formula/p/pkgconf.rb)
on 2026-10-30. Later formula changes do not change these pinned inputs.

| Source | Version | SHA-256 |
| --- | --- | --- |
| FFmpeg | 9.0.2 | `8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e` |
| LAME | 4.0 | `3df5124d5ad3a98312ffd7ba6a9b36230e4f8a3e66d3ce0f425e336c32d216eb` |
| Opus | 1.6.1 | `6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1` |
| pkgconf | 3.0.10 | `9371b2f6372da574d4c6dc96fe9b1463037bcf6afcb6671c1555afde7b17e2de` |

Every archive is checked before extraction. Archive paths and links are checked for
directory traversal. For an offline extraction check using existing source tarballs:

```sh
./scripts/build-ffmpeg.sh --output /tmp/telepilot-ffmpeg-preflight \
  --archive-dir /path/to/tarballs --offline --preflight
```

## Outputs and verification

`install/bin/ffmpeg` is the native executable. `install/share/telepilot-ffmpeg/`
contains `source-manifest.json`, component `licenses/`, and the exact `sources/`
archives. The manifest records architecture, binary checksum, source provenance,
compiler, configure arguments, dependency build settings and dynamic dependencies.
Logs remain under `logs/`. The script checks that the binary has the requested
native architecture and uses only Apple system dynamic libraries/frameworks.
Compiler, SDK and build-directory differences can change the binary checksum.

Run native tests, including synthetic recording, all five audio formats, graceful
M4A finalization, and a temporary Keychain/launchd round trip:

```sh
PATH=/tmp/telepilot-ffmpeg-rebuild/install/bin:$PATH \
  TELEPILOT_RUN_SYSTEM_INTEGRATION=1 ./scripts/test.sh --filter RecordingTests
```

These integration tests use synthetic data and remove their temporary entries.
They do not use private playlists. Inspect additional input support with
`ffmpeg -demuxers`, `ffmpeg -decoders`, and `ffmpeg -protocols`.

For packaging, pass `install/bin/ffmpeg` to `--ffmpeg`,
`install/share/telepilot-ffmpeg/source-manifest.json` to `--ffmpeg-manifest`,
and its `licenses` directory to `--ffmpeg-license-dir`. Retain and distribute the
corresponding source archives and build instructions with the binary's notices.
