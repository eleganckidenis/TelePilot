#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Buduje samodzielny TelePilot.app i opcjonalny DMG na macOS.

Użycie:
  ./scripts/build-macos-app.sh --runtime osx-arm64 --version 2.0.1 \
    --vlc-app /Applications/VLC.app [opcje]

Wymagane:
  --runtime RID            osx-arm64 albo osx-x64
  --version WERSJA         wersja numeryczna, np. 2.0.0
  --vlc-app ŚCIEŻKA        VLC.app 3.0.23 do lokalnego buildu albo:
  --vlc-dmg ŚCIEŻKA        oficjalny VLC 3.0.23 DMG do publicznego buildu

Opcje:
  --configuration NAZWA   Release (domyślnie) albo Debug
  --bundle-id ID           pl.telepilot.TelePilot (domyślnie)
  --minimum-macos WERSJA   14.0 (domyślnie)
  --sign-identity NAZWA    certyfikat Developer ID Application
  --notary-profile NAZWA   profil zapisany przez xcrun notarytool
  --skip-tests             pomija pełne testy automatyczne
  --no-restore             pomija dotnet restore
  --skip-dmg               pozostawia tylko TelePilot.app
  --force                  zastępuje wynik dla tej wersji i RID
  --help                    pokazuje tę pomoc

Bez --sign-identity aplikacja otrzymuje lokalny podpis ad-hoc i nie nadaje się
do publicznej dystrybucji. Publiczny podpis wymaga --vlc-dmg; skrypt weryfikuje
przypiętą oficjalną sumę i montuje obraz samodzielnie. --notary-profile wymaga
--sign-identity.
USAGE
}

fail() {
  printf 'Błąd: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Brak wymaganego narzędzia: $1"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPOSITORY_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

RUNTIME=""
VERSION=""
VLC_APP=""
VLC_VERSION="3.0.23"
VLC_DMG=""
EXPECTED_VLC_TEAM_ID="75GAHG3SZQ"
CONFIGURATION="Release"
BUNDLE_IDENTIFIER="pl.telepilot.TelePilot"
MINIMUM_MACOS="14.0"
SIGN_IDENTITY=""
NOTARY_PROFILE=""
SKIP_TESTS=false
NO_RESTORE=false
SKIP_DMG=false
FORCE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime)
      [[ $# -ge 2 ]] || fail "--runtime wymaga wartości"
      RUNTIME="$2"
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || fail "--version wymaga wartości"
      VERSION="$2"
      shift 2
      ;;
    --vlc-app)
      [[ $# -ge 2 ]] || fail "--vlc-app wymaga wartości"
      VLC_APP="$2"
      shift 2
      ;;
    --configuration)
      [[ $# -ge 2 ]] || fail "--configuration wymaga wartości"
      CONFIGURATION="$2"
      shift 2
      ;;
    --vlc-dmg)
      [[ $# -ge 2 ]] || fail "--vlc-dmg wymaga wartości"
      VLC_DMG="$2"
      shift 2
      ;;
    --bundle-id)
      [[ $# -ge 2 ]] || fail "--bundle-id wymaga wartości"
      BUNDLE_IDENTIFIER="$2"
      shift 2
      ;;
    --minimum-macos)
      [[ $# -ge 2 ]] || fail "--minimum-macos wymaga wartości"
      MINIMUM_MACOS="$2"
      shift 2
      ;;
    --sign-identity)
      [[ $# -ge 2 ]] || fail "--sign-identity wymaga wartości"
      SIGN_IDENTITY="$2"
      shift 2
      ;;
    --notary-profile)
      [[ $# -ge 2 ]] || fail "--notary-profile wymaga wartości"
      NOTARY_PROFILE="$2"
      shift 2
      ;;
    --skip-tests)
      SKIP_TESTS=true
      shift
      ;;
    --no-restore)
      NO_RESTORE=true
      shift
      ;;
    --skip-dmg)
      SKIP_DMG=true
      shift
      ;;
    --force)
      FORCE=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Nieznany argument: $1"
      ;;
  esac
done

[[ "$(uname -s)" == "Darwin" ]] || fail "Ten skrypt należy uruchomić na macOS."
[[ "$RUNTIME" == "osx-arm64" || "$RUNTIME" == "osx-x64" ]] \
  || fail "--runtime musi mieć wartość osx-arm64 albo osx-x64."
[[ "$VERSION" =~ ^[0-9]+([.][0-9]+){2}$ ]] || fail "Wersja musi mieć trzy człony, np. 2.0.0."
[[ "$CONFIGURATION" == "Release" || "$CONFIGURATION" == "Debug" ]] \
  || fail "--configuration musi mieć wartość Release albo Debug."
[[ "$BUNDLE_IDENTIFIER" =~ ^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$ ]] \
  || fail "Bundle ID musi mieć poprawny format reverse-DNS."
[[ "$MINIMUM_MACOS" =~ ^[0-9]+([.][0-9]+){1,2}$ ]] \
  || fail "Nieprawidłowa minimalna wersja macOS."
minimum_macos_major="${MINIMUM_MACOS%%.*}"
(( minimum_macos_major >= 14 )) || fail "TelePilot z .NET 10 wymaga macOS 14.0 lub nowszego."
[[ -n "$VLC_APP" || -n "$VLC_DMG" ]] \
  || fail "Podaj --vlc-app dla buildu lokalnego albo --vlc-dmg dla publicznego."
[[ -z "$NOTARY_PROFILE" || -n "$SIGN_IDENTITY" ]] \
  || fail "--notary-profile wymaga --sign-identity."
if [[ -n "$SIGN_IDENTITY" ]]; then
  [[ "$SIGN_IDENTITY" == "Developer ID Application:"* ]] \
    || fail "Publiczny podpis musi używać certyfikatu Developer ID Application."
  [[ -n "$VLC_DMG" ]] || fail "Publiczny build wymaga przypiętego --vlc-dmg."
fi

for command_name in codesign ditto file hdiutil iconutil lipo plutil sed shasum sips spctl xattr; do
  require_command "$command_name"
done

TEMP_BASE="${TMPDIR:-/tmp}"
TEMP_BASE="${TEMP_BASE%/}"
VLC_MOUNT_ROOT=""
TEMP_ROOT=""
cleanup() {
  if [[ -n "$VLC_MOUNT_ROOT" ]]; then
    case "$VLC_MOUNT_ROOT" in
      "$TEMP_BASE"/telepilot-vlc.*)
        hdiutil detach -quiet "$VLC_MOUNT_ROOT" >/dev/null 2>&1 || true
        rmdir "$VLC_MOUNT_ROOT" 2>/dev/null || true
        ;;
    esac
  fi
  if [[ -n "$TEMP_ROOT" ]]; then
    case "$TEMP_ROOT" in
      "$TEMP_BASE"/telepilot-macos.*) rm -rf -- "$TEMP_ROOT" ;;
    esac
  fi
}
trap cleanup EXIT

# Official files: https://download.videolan.org/pub/videolan/vlc/3.0.23/macosx/
EXPECTED_VLC_SHA256="fc6fac08d87f538517d44aca0c5e7a244b67c8c4cb589bf478363a7315fd5e0d"
if [[ "$RUNTIME" == "osx-x64" ]]; then
  EXPECTED_VLC_SHA256="ec01530ce69d849dd057fba8876e68ac39bf279dc28de4e9c04e4aec11fc98db"
fi
if [[ -n "$VLC_DMG" ]]; then
  [[ -f "$VLC_DMG" ]] || fail "Nie znaleziono obrazu źródłowego VLC: $VLC_DMG"
  actual_vlc_hash_line="$(shasum -a 256 "$VLC_DMG")"
  actual_vlc_sha256="${actual_vlc_hash_line%% *}"
  [[ "$actual_vlc_sha256" == "$EXPECTED_VLC_SHA256" ]] \
    || fail "Obraz VLC nie ma oficjalnej sumy SHA-256 dla $RUNTIME."
  VLC_MOUNT_ROOT="$(mktemp -d "$TEMP_BASE/telepilot-vlc.XXXXXX")"
  hdiutil attach -readonly -nobrowse -mountpoint "$VLC_MOUNT_ROOT" "$VLC_DMG" >/dev/null
  VLC_APP="$VLC_MOUNT_ROOT/VLC.app"
fi

[[ -f "$VLC_APP/Contents/Info.plist" ]] || fail "VLC.app nie zawiera Info.plist."
[[ -d "$VLC_APP/Contents/MacOS/lib" ]] || fail "VLC.app nie zawiera katalogu lib."
[[ -d "$VLC_APP/Contents/MacOS/plugins" ]] || fail "VLC.app nie zawiera katalogu plugins."
find "$VLC_APP/Contents/MacOS/plugins" -type f -name '*.dylib' -print -quit | grep -q . \
  || fail "VLC.app nie zawiera natywnych pluginów."
actual_vlc_version="$(plutil -extract CFBundleShortVersionString raw -o - "$VLC_APP/Contents/Info.plist")"
[[ "$actual_vlc_version" == "$VLC_VERSION" ]] \
  || fail "VLC.app ma wersję $actual_vlc_version, oczekiwano $VLC_VERSION."
codesign --verify --deep --strict --verbose=2 "$VLC_APP"
vlc_signature="$(codesign -dv --verbose=4 "$VLC_APP" 2>&1)"
grep -q '^Authority=Developer ID Application:' <<<"$vlc_signature" \
  || fail "VLC.app nie ma podpisu Developer ID Application."
grep -Fqx "TeamIdentifier=$EXPECTED_VLC_TEAM_ID" <<<"$vlc_signature" \
  || fail "VLC.app nie ma oficjalnego TeamIdentifier VideoLAN."
spctl --assess --type execute --verbose=2 "$VLC_APP"

if [[ -n "$SIGN_IDENTITY" ]]; then
  require_command security
  security find-identity -v -p codesigning \
    | grep -F "\"$SIGN_IDENTITY\"" >/dev/null \
    || fail "Nie znaleziono wskazanego certyfikatu w Keychain."
fi

DOTNET="$REPOSITORY_ROOT/.dotnet/dotnet"
if [[ ! -x "$DOTNET" ]]; then
  require_command dotnet
  DOTNET="$(command -v dotnet)"
fi
cd "$REPOSITORY_ROOT"

EXPECTED_ARCH="arm64"
ARCH_LABEL="macos-arm64"
if [[ "$RUNTIME" == "osx-x64" ]]; then
  EXPECTED_ARCH="x86_64"
  ARCH_LABEL="macos-x64"
fi

PROJECT="$REPOSITORY_ROOT/src/TelePilot.App/TelePilot.App.csproj"
SOLUTION="$REPOSITORY_ROOT/TelePilot.sln"
INFO_TEMPLATE="$REPOSITORY_ROOT/installer/macos/Info.plist.template"
ENTITLEMENTS="$REPOSITORY_ROOT/installer/macos/TelePilot.entitlements"
ICON_SOURCE="$REPOSITORY_ROOT/src/TelePilot.App/Assets/TelePilot.png"
RELEASE_ROOT="$REPOSITORY_ROOT/artifacts/release/$VERSION/$RUNTIME"
PUBLISH_DIR="$RELEASE_ROOT/publish"
APP="$RELEASE_ROOT/TelePilot.app"
CONTENTS="$APP/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
LIBVLC_ROOT="$CONTENTS/Frameworks/libvlc"
DMG="$RELEASE_ROOT/TelePilot-$VERSION-$ARCH_LABEL.dmg"

case "$RELEASE_ROOT" in
  "$REPOSITORY_ROOT"/artifacts/release/*) ;;
  *) fail "Niebezpieczna ścieżka wyniku: $RELEASE_ROOT" ;;
esac

if [[ -e "$RELEASE_ROOT" ]]; then
  if [[ "$FORCE" == true ]]; then
    rm -rf -- "$RELEASE_ROOT"
  else
    fail "Wynik już istnieje. Użyj --force po sprawdzeniu ścieżki."
  fi
fi

mkdir -p "$RELEASE_ROOT"
TEMP_ROOT="$(mktemp -d "$TEMP_BASE/telepilot-macos.XXXXXX")"

if [[ "$NO_RESTORE" == false ]]; then
  "$DOTNET" restore "$SOLUTION"
fi

if [[ "$SKIP_TESTS" == false ]]; then
  test_args=(test "$SOLUTION" --configuration "$CONFIGURATION" \
    "-p:UsedAvaloniaProducts=")
  if [[ "$NO_RESTORE" == true ]]; then
    test_args+=(--no-restore)
  fi
  "$DOTNET" "${test_args[@]}"
fi

publish_args=(publish "$PROJECT" --configuration "$CONFIGURATION" \
  --framework net10.0 --runtime "$RUNTIME" --self-contained true \
  -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false \
  -p:DebugSymbols=false -p:DebugType=None "-p:Version=$VERSION" \
  "-p:UsedAvaloniaProducts=" --output "$PUBLISH_DIR")
if [[ "$NO_RESTORE" == true ]]; then
  publish_args+=(--no-restore)
fi
"$DOTNET" "${publish_args[@]}"

[[ -x "$PUBLISH_DIR/TelePilot" ]] || fail "Publikacja nie zawiera wykonywalnego TelePilot."
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$LIBVLC_ROOT"
ditto "$PUBLISH_DIR" "$MACOS_DIR"
chmod +x "$MACOS_DIR/TelePilot"

VLC_SOURCE_ROOT="$VLC_APP/Contents/MacOS"
for required_library in libvlc.dylib libvlccore.dylib; do
  library_path="$VLC_SOURCE_ROOT/lib/$required_library"
  [[ -e "$library_path" ]] || fail "VLC.app nie zawiera $required_library."
  lipo -verify_arch "$EXPECTED_ARCH" "$library_path" \
    || fail "$required_library nie zawiera architektury $EXPECTED_ARCH."
done

ditto "$VLC_SOURCE_ROOT/lib" "$LIBVLC_ROOT/lib"
ditto "$VLC_SOURCE_ROOT/plugins" "$LIBVLC_ROOT/plugins"
find "$LIBVLC_ROOT/plugins" -type f -name '*.dylib' -print -quit | grep -q . \
  || fail "Skopiowany pakiet LibVLC nie zawiera pluginów."
if [[ -d "$VLC_SOURCE_ROOT/share" ]]; then
  ditto "$VLC_SOURCE_ROOT/share" "$LIBVLC_ROOT/share"
fi

ICONSET="$TEMP_ROOT/TelePilot.iconset"
mkdir -p "$ICONSET"
sips -z 16 16 "$ICON_SOURCE" --out "$ICONSET/icon_16x16.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET/icon_32x32.png" >/dev/null
sips -z 64 64 "$ICON_SOURCE" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$ICON_SOURCE" --out "$ICONSET/icon_128x128.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET/icon_256x256.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$RESOURCES_DIR/TelePilot.icns"

sed \
  -e "s/__VERSION__/$VERSION/g" \
  -e "s/__BUNDLE_IDENTIFIER__/$BUNDLE_IDENTIFIER/g" \
  -e "s/__MINIMUM_SYSTEM_VERSION__/$MINIMUM_MACOS/g" \
  "$INFO_TEMPLATE" > "$CONTENTS/Info.plist"
plutil -lint "$CONTENTS/Info.plist"
plutil -lint "$ENTITLEMENTS"

mkdir -p "$RESOURCES_DIR/licenses"
ditto "$REPOSITORY_ROOT/docs/THIRD_PARTY_LICENSES.md" \
  "$RESOURCES_DIR/licenses/THIRD_PARTY_LICENSES.md"
ditto "$REPOSITORY_ROOT/docs/licenses/LGPL-2.1-or-later.txt" \
  "$RESOURCES_DIR/licenses/LGPL-2.1-or-later.txt"
xattr -cr "$APP"

mach_o_count=0
while IFS= read -r -d '' native_file; do
  if file -b "$native_file" | grep -q 'Mach-O'; then
    mach_o_count=$((mach_o_count + 1))
    lipo -verify_arch "$EXPECTED_ARCH" "$native_file" \
      || fail "Plik nie zawiera architektury $EXPECTED_ARCH: $native_file"
  fi
done < <(find "$CONTENTS" -type f -print0)
[[ $mach_o_count -gt 0 ]] || fail "Pakiet nie zawiera plików Mach-O."

MAIN_EXECUTABLE="$MACOS_DIR/TelePilot"
while IFS= read -r -d '' native_file; do
  [[ "$native_file" == "$MAIN_EXECUTABLE" ]] && continue
  if file -b "$native_file" | grep -q 'Mach-O'; then
    if [[ -n "$SIGN_IDENTITY" ]]; then
      codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$native_file"
    else
      codesign --force --sign - "$native_file"
    fi
  fi
done < <(find "$CONTENTS" -type f -print0)

rm -f -- "$LIBVLC_ROOT/plugins/plugins.dat"
VLC_CACHE_GEN="$VLC_SOURCE_ROOT/vlc-cache-gen"
if [[ ! -x "$VLC_CACHE_GEN" && -x "$(dirname "$VLC_APP")/vlc-cache-gen" ]]; then
  VLC_CACHE_GEN="$(dirname "$VLC_APP")/vlc-cache-gen"
fi
cache_generated=false
if [[ -x "$VLC_CACHE_GEN" ]] \
  && "$VLC_CACHE_GEN" --help >/dev/null 2>&1 \
  && "$VLC_CACHE_GEN" "$LIBVLC_ROOT/plugins"; then
  [[ -f "$LIBVLC_ROOT/plugins/plugins.dat" ]] && cache_generated=true
fi
if [[ "$cache_generated" == false ]]; then
  rm -f -- "$LIBVLC_ROOT/plugins/plugins.dat"
  printf 'Uwaga: brak zgodnego vlc-cache-gen; LibVLC przeskanuje pluginy przy uruchomieniu.\n' >&2
fi

if [[ -n "$SIGN_IDENTITY" ]]; then
  codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" \
    --sign "$SIGN_IDENTITY" "$APP"
else
  codesign --force --entitlements "$ENTITLEMENTS" --sign - "$APP"
fi
codesign --verify --deep --strict --verbose=4 "$APP"

if [[ -n "$NOTARY_PROFILE" ]]; then
  require_command xcrun
  require_command spctl
  APP_ZIP="$RELEASE_ROOT/TelePilot-$VERSION-$ARCH_LABEL.app.zip"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$APP_ZIP"
  xcrun notarytool submit "$APP_ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$APP"
  xcrun stapler validate "$APP"
  codesign --verify --deep --strict --verbose=4 "$APP"
  spctl --assess --type execute --verbose=4 "$APP"
  rm -f -- "$APP_ZIP"
fi

if [[ "$SKIP_DMG" == false ]]; then
  DMG_STAGE="$TEMP_ROOT/dmg-stage"
  mkdir -p "$DMG_STAGE"
  ditto "$APP" "$DMG_STAGE/TelePilot.app"
  ln -s /Applications "$DMG_STAGE/Applications"
  hdiutil create -volname "TelePilot" -srcfolder "$DMG_STAGE" \
    -ov -format UDZO "$DMG"

  if [[ -n "$SIGN_IDENTITY" ]]; then
    codesign --force --timestamp --sign "$SIGN_IDENTITY" \
      --identifier "$BUNDLE_IDENTIFIER.dmg" "$DMG"
  fi

  if [[ -n "$NOTARY_PROFILE" ]]; then
    xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$DMG"
    xcrun stapler validate "$DMG"
    spctl --assess --type open --context context:primary-signature --verbose=4 "$DMG"
  fi

  hdiutil verify "$DMG"
  (
    cd "$RELEASE_ROOT"
    shasum -a 256 "$(basename "$DMG")" > "$(basename "$DMG").sha256"
  )
fi

printf '\nTelePilot macOS build zakończony.\n'
printf 'Aplikacja: %s\n' "$APP"
if [[ "$SKIP_DMG" == false ]]; then
  printf 'DMG:        %s\n' "$DMG"
fi
printf 'Runtime:    %s\n' "$RUNTIME"
printf 'Mach-O:     %s sprawdzonych plików\n' "$mach_o_count"
if [[ -z "$SIGN_IDENTITY" ]]; then
  printf 'Podpis:     ad-hoc; wynik tylko do testów lokalnych\n'
elif [[ -z "$NOTARY_PROFILE" ]]; then
  printf 'Podpis:     Developer ID; wynik nie został notaryzowany\n'
else
  printf 'Podpis:     Developer ID i notaryzacja zakończone\n'
fi
