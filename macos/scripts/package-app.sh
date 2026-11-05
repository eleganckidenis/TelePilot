#!/bin/bash
set -euo pipefail

usage() {
cat <<'USAGE'
Build native AppKit TelePilot.app, DMG and ZIP on the matching macOS architecture.
Usage: scripts/package-app.sh --vlc-dmg FILE [options]
  --arch arm64|x86_64       Defaults to uname -m; cross releases are rejected.
  --version VERSION        Defaults to the VERSION file.
  --ffmpeg FILE            Optional verified native FFmpeg executable.
  --ffmpeg-manifest FILE   Required with --ffmpeg: JSON provenance/build/checksum manifest.
  --ffmpeg-license-dir DIR Required with --ffmpeg: complete matching license/notice set.
  --vlc-license-dir DIR    Additional complete VLC contrib license/notice documents.
  --sign-identity NAME     Developer ID Application identity; otherwise ad-hoc.
  --notary-profile NAME    Requires --sign-identity; notarizes/staples app and DMG.
  --skip-tests             Use only after a successful current full test run.
  --force                  Replace this version/architecture's existing output.
  --help
Output: artifacts/VERSION/ARCH/TelePilot.app and matching DMG, ZIP, SHA256SUMS.
Only the official, SHA-256-pinned VLC 3.0.23 image is accepted.
USAGE
}
fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARCH="$(uname -m)"; VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"; VLC_DMG=""; FFMPEG=""; FFMPEG_MANIFEST=""; FFMPEG_LICENSE_DIR=""; VLC_LICENSE_DIR=""
SIGN_IDENTITY=""; NOTARY_PROFILE=""; SKIP_TESTS=false; FORCE=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --skip-tests) SKIP_TESTS=true; shift ;;
    --force) FORCE=true; shift ;;
    --arch|--version|--vlc-dmg|--ffmpeg|--ffmpeg-manifest|--ffmpeg-license-dir|--vlc-license-dir|--sign-identity|--notary-profile)
      [[ $# -ge 2 && -n "$2" ]] || fail "$1 requires a value"
      case "$1" in
        --arch) ARCH="$2" ;; --version) VERSION="$2" ;; --vlc-dmg) VLC_DMG="$2" ;;
        --ffmpeg) FFMPEG="$2" ;; --ffmpeg-manifest) FFMPEG_MANIFEST="$2" ;; --ffmpeg-license-dir) FFMPEG_LICENSE_DIR="$2" ;;
        --vlc-license-dir) VLC_LICENSE_DIR="$2" ;; --sign-identity) SIGN_IDENTITY="$2" ;; --notary-profile) NOTARY_PROFILE="$2" ;;
      esac; shift 2 ;;
    *) fail "Unknown option: $1" ;;
  esac
done
[[ "$(uname -s)" == Darwin ]] || fail "macOS is required"
[[ "$ARCH" == arm64 || "$ARCH" == x86_64 ]] || fail "Unsupported architecture"
[[ "$ARCH" == "$(uname -m)" ]] || fail "Build and test on a native $ARCH Mac; cross release refused"
[[ "$(sysctl -n sysctl.proc_translated 2>/dev/null || true)" != 1 ]] || fail "Run from a native terminal, outside Rosetta"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]] || fail "Invalid version"
[[ -f "$VLC_DMG" ]] || fail "--vlc-dmg must name the official VLC 3.0.23 image"
[[ -z "$NOTARY_PROFILE" || -n "$SIGN_IDENTITY" ]] || fail "Notarization requires a Developer ID identity"
[[ -z "$SIGN_IDENTITY" || "$SIGN_IDENTITY" == 'Developer ID Application:'* ]] || fail "Expected Developer ID Application identity"
if [[ -n "$FFMPEG" ]]; then
  [[ -x "$FFMPEG" && -s "$FFMPEG_MANIFEST" && -d "$FFMPEG_LICENSE_DIR" ]] || fail "FFmpeg requires executable, manifest and license directory"
  find "$FFMPEG_LICENSE_DIR" -type f -print -quit | grep -q . || fail "FFmpeg license directory is empty"
else
  [[ -z "$FFMPEG_MANIFEST" && -z "$FFMPEG_LICENSE_DIR" ]] || fail "FFmpeg metadata was given without --ffmpeg"
fi
[[ -z "$VLC_LICENSE_DIR" || -d "$VLC_LICENSE_DIR" ]] || fail "VLC license directory not found"
for tool in swift xcrun codesign ditto hdiutil iconutil lipo plutil shasum sips file otool python3 xattr; do
  command -v "$tool" >/dev/null || fail "Required tool missing: $tool"
done
EXPECTED_SHA=fc6fac08d87f538517d44aca0c5e7a244b67c8c4cb589bf478363a7315fd5e0d
[[ "$ARCH" == arm64 ]] || EXPECTED_SHA=ec01530ce69d849dd057fba8876e68ac39bf279dc28de4e9c04e4aec11fc98db
ACTUAL_SHA="$(shasum -a 256 "$VLC_DMG")"; ACTUAL_SHA="${ACTUAL_SHA%% *}"
[[ "$ACTUAL_SHA" == "$EXPECTED_SHA" ]] || fail "VLC image SHA-256 mismatch for $ARCH"
OUT="$ROOT/artifacts/$VERSION/$ARCH"
if [[ -e "$OUT" ]]; then
  [[ "$FORCE" == true ]] || fail "Output exists: $OUT (use --force after review)"
  rm -rf -- "$OUT"
fi
TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/telepilot-native-package.XXXXXX")"
MOUNT="$TEMP_ROOT/vlc-volume"; MOUNTED=false
cleanup() {
  if [[ "$MOUNTED" == true ]]; then hdiutil detach -quiet "$MOUNT" || printf 'Warning: detach manually: %s\n' "$MOUNT" >&2; fi
  if [[ "$MOUNTED" == false || ! -d "$MOUNT/VLC.app" ]]; then rm -rf -- "$TEMP_ROOT"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$MOUNT"
# Mount exactly our verified image; never reuse another user's mounted VLC.
hdiutil attach -readonly -nobrowse -mountpoint "$MOUNT" "$VLC_DMG" >/dev/null
MOUNTED=true
VLC_APP="$MOUNT/VLC.app"; VLC_ROOT="$VLC_APP/Contents/MacOS"
[[ "$(plutil -extract CFBundleShortVersionString raw -o - "$VLC_APP/Contents/Info.plist")" == 3.0.23 ]] || fail "Unexpected VLC version"
codesign --verify --deep --strict "$VLC_APP"
VLC_SIGNATURE="$(codesign -dv --verbose=4 "$VLC_APP" 2>&1)"
grep -q '^Authority=Developer ID Application:' <<< "$VLC_SIGNATURE" || fail "VLC lacks Developer ID signature"
grep -Fqx 'TeamIdentifier=75GAHG3SZQ' <<< "$VLC_SIGNATURE" || fail "VLC signer is not VideoLAN"
for lib in libvlc.dylib libvlccore.dylib; do [[ -f "$VLC_ROOT/lib/$lib" ]] || fail "Missing VLC library $lib"; done
[[ -d "$VLC_ROOT/plugins" ]] || fail "VLC plugins missing"

export CLANG_MODULE_CACHE_PATH=/tmp/telepilot-clang-cache
export SWIFTPM_MODULECACHE_OVERRIDE=/tmp/telepilot-swift-cache
BUILD_ROOT="/tmp/telepilot-native-build-$ARCH"
SWIFT_FLAGS=(--package-path "$ROOT" --scratch-path "$BUILD_ROOT" --cache-path /tmp/telepilot-spm-cache --config-path /tmp/telepilot-spm-config --security-path /tmp/telepilot-spm-security --disable-sandbox --build-system native --triple "$ARCH-apple-macosx14.0")
FRAMEWORKS=/Library/Developer/CommandLineTools/Library/Developer/Frameworks
if [[ "$SKIP_TESTS" == false ]]; then
  "$ROOT/scripts/test.sh" --scratch-path "$BUILD_ROOT"
fi
swift build "${SWIFT_FLAGS[@]}" -c release --product TelePilot -Xlinker -rpath -Xlinker '@executable_path/../Frameworks/libvlc/lib'
BIN_DIR="$(swift build "${SWIFT_FLAGS[@]}" -c release --show-bin-path)"
[[ -x "$BIN_DIR/TelePilot" ]] || fail "Native TelePilot executable missing"
APP="$TEMP_ROOT/TelePilot.app"; CONTENTS="$APP/Contents"; RESOURCES="$CONTENTS/Resources"; LIBVLC="$CONTENTS/Frameworks/libvlc"
mkdir -p "$CONTENTS/MacOS" "$RESOURCES/licenses" "$LIBVLC"
ditto "$BIN_DIR/TelePilot" "$CONTENTS/MacOS/TelePilot"
ditto "$VLC_ROOT/lib" "$LIBVLC/lib"
ditto "$VLC_ROOT/plugins" "$LIBVLC/plugins"
[[ ! -d "$VLC_ROOT/share" ]] || ditto "$VLC_ROOT/share" "$LIBVLC/share"
# The provider cache stores source-bundle locations; LibVLC scans the copied plugins.
rm -f "$LIBVLC/plugins/plugins.dat"
# This obsolete VLC notification module alone needs Growl.framework. TelePilot
# uses AppKit/VoiceOver announcements and does not expose VLC GUI notifications.
rm -f "$LIBVLC/plugins/libosx_notifications_plugin.dylib"
ditto "$ROOT/Resources/licenses" "$RESOURCES/licenses"
ditto "$ROOT/docs/DEPENDENCIES.md" "$RESOURCES/licenses/DEPENDENCIES.md"
[[ -z "$VLC_LICENSE_DIR" ]] || ditto "$VLC_LICENSE_DIR" "$RESOURCES/licenses/VLC-contrib"
if [[ -n "$FFMPEG" ]]; then
  mkdir -p "$RESOURCES/tools" "$RESOURCES/licenses/ffmpeg"
  ditto "$FFMPEG" "$RESOURCES/tools/ffmpeg"; chmod +x "$RESOURCES/tools/ffmpeg"
  ditto "$FFMPEG_LICENSE_DIR" "$RESOURCES/licenses/ffmpeg"
  python3 - "$FFMPEG" "$FFMPEG_MANIFEST" "$ARCH" <<'PYMANIFEST'
import hashlib, json, pathlib, re, sys
binary, manifest, arch = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
metadata = json.loads(manifest.read_text())
if metadata.get('architecture') != arch: raise SystemExit('FFmpeg manifest architecture mismatch')
minimum = tuple(int(part) for part in str(metadata.get('minimumMacOS', '')).split('.'))
if not minimum or (minimum + (0, 0))[:2] > (14, 0):
    raise SystemExit('FFmpeg requires macOS newer than package minimum 14.0')
actual = hashlib.sha256(binary.read_bytes()).hexdigest()
if metadata.get('binarySHA256') != actual: raise SystemExit('FFmpeg binary does not match manifest SHA-256')
for key in ('name', 'version', 'license', 'configureArguments', 'sources'):
    if not metadata.get(key): raise SystemExit(f'FFmpeg manifest missing {key}')
for source in metadata['sources']:
    if not source.get('url') or not re.fullmatch('[0-9a-fA-F]{64}', source.get('sha256', '')):
        raise SystemExit('FFmpeg manifest source must include URL and SHA-256')
PYMANIFEST
  ditto "$FFMPEG_MANIFEST" "$RESOURCES/licenses/ffmpeg/source-manifest.json"
  [[ ! -f "$ROOT/docs/FFMPEG_BUILD.md" ]] || ditto "$ROOT/docs/FFMPEG_BUILD.md" "$RESOURCES/licenses/ffmpeg/FFMPEG_BUILD.md"
  "$FFMPEG" -version > "$RESOURCES/licenses/ffmpeg/VERSION.txt"
  "$FFMPEG" -buildconf > "$RESOURCES/licenses/ffmpeg/BUILD-CONFIG.txt" 2>&1
  shasum -a 256 "$FFMPEG" > "$RESOURCES/licenses/ffmpeg/INPUT-SHA256.txt"
fi
if [[ -f "$ROOT/Resources/TelePilot.icns" ]]; then
  ditto "$ROOT/Resources/TelePilot.icns" "$RESOURCES/TelePilot.icns"
else
  ICON_SOURCE="$ROOT/Resources/TelePilot.png"
  [[ -f "$ICON_SOURCE" ]] || fail "TelePilot icon source missing"
  ICONSET="$TEMP_ROOT/TelePilot.iconset"; mkdir -p "$ICONSET"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$ICON_SOURCE" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    double=$((size*2)); sips -z "$double" "$double" "$ICON_SOURCE" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$RESOURCES/TelePilot.icns"
fi
NUMERIC_VERSION="${VERSION%%-*}"
cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>pl.telepilot.native</string>
<key>CFBundleDevelopmentRegion</key><string>en</string>
<key>CFBundleLocalizations</key><array><string>en</string><string>pl</string></array>
<key>CFBundleExecutable</key><string>TelePilot</string>
<key>CFBundleName</key><string>TelePilot</string>
<key>CFBundleDisplayName</key><string>TelePilot</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$NUMERIC_VERSION</string>
<key>CFBundleVersion</key><string>$NUMERIC_VERSION</string>
<key>TelePilotDistributionVersion</key><string>$VERSION</string>
<key>CFBundleIconFile</key><string>TelePilot.icns</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>LSApplicationCategoryType</key><string>public.app-category.entertainment</string>
<key>LSUIElement</key><false/>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
<key>NSAppTransportSecurity</key><dict><key>NSAllowsArbitraryLoads</key><true/></dict>
</dict></plist>
PLIST
plutil -lint "$CONTENTS/Info.plist"
{
  printf 'Excluded: libosx_notifications_plugin.dylib (obsolete Growl notifications; TelePilot uses AppKit announcements)\n\n'
  printf 'TelePilot %s; VLC 3.0.23; architecture %s\nOfficial DMG SHA256: %s\n\n' "$VERSION" "$ARCH" "$EXPECTED_SHA"
  find "$LIBVLC/plugins" -type f -name '*.dylib' -exec basename {} \; | LC_ALL=C sort
} > "$RESOURCES/licenses/VLC_PLUGIN_INVENTORY.txt"
find "$LIBVLC/plugins" -type f -name '*.dylib' -print -quit | grep -q . || fail "No bundled VLC plugins"
# Verify real load commands, excluding LC_ID_DYLIB install names. Resolve @rpath
# against each loader and the executable, rejecting dependencies outside the app.
python3 - "$APP" "$ARCH" <<'PY'
import os, pathlib, re, subprocess, sys
app = pathlib.Path(sys.argv[1]).resolve(); arch = sys.argv[2]
main = app / 'Contents/MacOS/TelePilot'; executable_dir = main.parent

def run(*args):
    return subprocess.check_output(args, text=True, encoding='utf-8', errors='replace', stderr=subprocess.STDOUT)

def commands(path):
    result = []; rpaths = []
    for block in run('otool', '-arch', arch, '-l', str(path)).split('Load command ')[1:]:
        kind = re.search(r'^\s*cmd (\S+)', block, re.M)
        if not kind: continue
        if kind[1] == 'LC_RPATH':
            match = re.search(r'^\s*path (.+) \(offset ', block, re.M)
            if match: rpaths.append(match[1])
        elif kind[1] in {'LC_LOAD_DYLIB', 'LC_LOAD_WEAK_DYLIB', 'LC_REEXPORT_DYLIB', 'LC_LOAD_UPWARD_DYLIB'}:
            match = re.search(r'^\s*name (.+) \(offset ', block, re.M)
            if match: result.append(match[1])
    return result, rpaths

def substitute(value, loader):
    if value.startswith('@loader_path/'): return loader.parent / value[len('@loader_path/'):]
    if value.startswith('@executable_path/'): return executable_dir / value[len('@executable_path/'):]
    if value.startswith('/'): return pathlib.Path(value)
    return None

def system(path): return str(path).startswith(('/usr/lib/', '/System/'))
def packaged(path): return path.is_relative_to(app) and path.exists()
_, main_rpaths = commands(main)
count = 0
for directory, dirs, names in os.walk(app):
    for name in names:
        path = pathlib.Path(directory) / name
        if path.is_symlink():
            if not path.resolve().is_relative_to(app): raise SystemExit(f'Escaping symlink: {path}')
            continue
        if 'Mach-O' not in run('file', '-b', str(path)): continue
        count += 1
        run('lipo', str(path), '-verify_arch', arch)
        dependencies, local_rpaths = commands(path)
        rpaths = [(value, path) for value in local_rpaths] + [(value, main) for value in main_rpaths]
        for dependency in dependencies:
            if system(dependency): continue
            candidates = []
            if dependency.startswith('@rpath/'):
                for value, loader in rpaths:
                    base = substitute(value, loader)
                    if base is not None: candidates.append(base / dependency[len('@rpath/'):])
            else:
                value = substitute(dependency, path)
                if value is not None: candidates.append(value)
            if not any(packaged(candidate.resolve()) or system(candidate.resolve()) for candidate in candidates):
                raise SystemExit(f'Unresolved or external dependency: {path.relative_to(app)} -> {dependency}')
if count == 0: raise SystemExit('No Mach-O files')
print(f'Architecture and dependency closure verified: {count} Mach-O files ({arch})')
PY
# Resources are complete before signing. Re-sign bundled native components from
# deepest to shallowest; never sign the enclosing bundle with --deep.
xattr -cr "$APP"
SIGN_ARGS=(--force --sign -)
if [[ -n "$SIGN_IDENTITY" ]]; then SIGN_ARGS=(--force --timestamp --options runtime --sign "$SIGN_IDENTITY"); fi
# codesign treats all files below Frameworks as nested code, including VLC's
# Blu-ray Java resources. Preserve them and give non-Mach-O files generic
# signatures (stored in xattrs, retained by ditto resource-preserving archives).
RESOURCE_SIGN_ARGS=(--force --sign -)
if [[ -n "$SIGN_IDENTITY" ]]; then RESOURCE_SIGN_ARGS=(--force --timestamp --sign "$SIGN_IDENTITY"); fi
while IFS= read -r -d '' resource_file; do
  if ! file -b "$resource_file" | grep -q 'Mach-O'; then codesign "${RESOURCE_SIGN_ARGS[@]}" "$resource_file"; fi
done < <(find "$CONTENTS/Frameworks" -depth -type f -print0)
while IFS= read -r -d '' native_file; do
  [[ "$native_file" != "$CONTENTS/MacOS/TelePilot" ]] || continue
  if file -b "$native_file" | grep -q 'Mach-O'; then codesign "${SIGN_ARGS[@]}" "$native_file"; fi
done < <(find "$CONTENTS" -depth -type f -print0)
codesign "${SIGN_ARGS[@]}" "$CONTENTS/MacOS/TelePilot"
codesign "${SIGN_ARGS[@]}" "$APP"
codesign --verify --deep --strict "$APP"
if [[ -n "$NOTARY_PROFILE" ]]; then
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$TEMP_ROOT/notary.zip"
  xcrun notarytool submit "$TEMP_ROOT/notary.zip" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$APP"; xcrun stapler validate "$APP"
  codesign --verify --deep --strict "$APP"
  spctl --assess --type execute "$APP"
fi
mkdir -p "$OUT"
STAGE="$TEMP_ROOT/dmg-stage"; mkdir -p "$STAGE"
ditto "$APP" "$STAGE/TelePilot.app"; ln -s /Applications "$STAGE/Applications"
DMG="$OUT/TelePilot-$VERSION-$ARCH.dmg"; ZIP="$OUT/TelePilot-$VERSION-$ARCH.app.zip"
hdiutil create -volname TelePilot -srcfolder "$STAGE" -format UDZO "$DMG" >/dev/null
if [[ -n "$SIGN_IDENTITY" ]]; then codesign --force --timestamp --sign "$SIGN_IDENTITY" --identifier pl.telepilot.native.dmg "$DMG"; fi
if [[ -n "$NOTARY_PROFILE" ]]; then
  xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"; xcrun stapler validate "$DMG"
fi
hdiutil verify "$DMG" >/dev/null
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
mkdir -p "$TEMP_ROOT/zip-check"; ditto -x -k "$ZIP" "$TEMP_ROOT/zip-check"
codesign --verify --deep --strict "$TEMP_ROOT/zip-check/TelePilot.app"
ditto "$APP" "$OUT/TelePilot.app"
# File Provider may attach FinderInfo asynchronously after the copy. Wait for
# that initial metadata update, then remove only prohibited Finder metadata;
# preserve com.apple.cs.* generic signatures on VLC resources.
sleep 1
xattr -dr com.apple.FinderInfo "$OUT/TelePilot.app" >/dev/null 2>&1 || true
DELIVERED_APP="$OUT/TelePilot.app"
if ! codesign --verify --deep --strict "$DELIVERED_APP"; then
  # Some File Provider volumes immediately restore FinderInfo. Keep a verified
  # local app outside that volume; distribute the already verified DMG/ZIP.
  LOCAL_APP_ROOT="$(mktemp -d "/tmp/telepilot-native-app-$VERSION-$ARCH.XXXXXX")"
  ditto "$APP" "$LOCAL_APP_ROOT/TelePilot.app"
  codesign --verify --deep --strict "$LOCAL_APP_ROOT/TelePilot.app"
  rm -rf -- "$DELIVERED_APP"
  DELIVERED_APP="$LOCAL_APP_ROOT/TelePilot.app"
  printf 'File Provider changed loose bundle metadata; verified local app: %s\n' "$DELIVERED_APP" >&2
fi
(
  cd "$OUT"
  shasum -a 256 "$(basename "$DMG")" "$(basename "$ZIP")" > SHA256SUMS
)
printf '\nNative package: %s\nVerified app: %s\nArchitecture: %s\nSignature: %s\n' "$OUT" "$DELIVERED_APP" "$ARCH" "${SIGN_IDENTITY:-ad-hoc (local testing)}"
