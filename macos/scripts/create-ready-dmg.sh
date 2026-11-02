#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]] || exit 1
SOURCE_DIR="$ROOT/artifacts/$VERSION/arm64"
SOURCE_ZIP="$SOURCE_DIR/TelePilot-$VERSION-arm64.app.zip"
OUTPUT="$ROOT/artifacts/TelePilot-$VERSION-Apple-Silicon.dmg"
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo 'Build on Apple Silicon macOS.' >&2; exit 1; }
[[ ! -e "$OUTPUT" ]] || { echo 'Output already exists; preserve or rename it before rebuilding.' >&2; exit 1; }
(cd "$SOURCE_DIR" && shasum -a 256 -c SHA256SUMS)
TEMP_ROOT="$(mktemp -d /tmp/telepilot-ready-dmg.XXXXXX)"
MOUNT="$TEMP_ROOT/mounted"
MOUNTED=false
cleanup() {
  if [[ "$MOUNTED" == true ]]; then hdiutil detach -quiet "$MOUNT" || return; fi
  rm -rf -- "$TEMP_ROOT"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$TEMP_ROOT/stage" "$MOUNT"
ditto -x -k "$SOURCE_ZIP" "$TEMP_ROOT/stage"
APP="$TEMP_ROOT/stage/TelePilot.app"
[[ -x "$APP/Contents/MacOS/TelePilot" ]]
codesign --verify --deep --strict "$APP"
lipo "$APP/Contents/MacOS/TelePilot" -verify_arch arm64
ln -s /Applications "$TEMP_ROOT/stage/Applications"
cat > "$TEMP_ROOT/stage/Instalacja.txt" <<TEXT
TelePilot dla Apple Silicon
macOS 14 lub nowszy, procesor Apple M1 lub nowszy.

INSTALACJA
1. Przeciągnij TelePilot.app na skrót Applications (Programy) w tym obrazie.
2. Wysuń obraz dysku TelePilot.
3. Uruchom TelePilot z folderu Programy.

VLC i FFmpeg są już w aplikacji. Nie trzeba instalować .NET ani osobnych silników.
Własną playlistę dodasz przez Command+O (plik) lub Command+U (adres).
Kanał uruchamia Enter, Command+S zatrzymuje, Command+Q kończy program.

PODPIS
Pakiet ma bezpłatny lokalny podpis ad-hoc. Płatny Developer ID i notaryzację
Apple pominięto zgodnie z decyzją użytkownika.
Jeśli po pobraniu macOS zablokuje aplikację, można zatwierdzić konkretny
TelePilot w Ustawienia systemowe > Prywatność i ochrona > Otwórz mimo to.
Nie trzeba wyłączać Gatekeepera ani innych zabezpieczeń systemu.
Instrukcja Apple: https://support.apple.com/pl-pl/102445

Aplikacja nie zawiera playlist ani abonamentu IPTV.
Wersja: $VERSION, Apple Silicon (arm64).
TEXT
hdiutil create -volname TelePilot -srcfolder "$TEMP_ROOT/stage" -format UDZO "$OUTPUT" >/dev/null
hdiutil verify "$OUTPUT" >/dev/null
hdiutil attach -readonly -nobrowse -mountpoint "$MOUNT" "$OUTPUT" >/dev/null
MOUNTED=true
codesign --verify --deep --strict "$MOUNT/TelePilot.app"
lipo "$MOUNT/TelePilot.app/Contents/MacOS/TelePilot" -verify_arch arm64
[[ "$(readlink "$MOUNT/Applications")" == /Applications ]]
[[ -s "$MOUNT/Instalacja.txt" ]]
hdiutil detach -quiet "$MOUNT"
MOUNTED=false
(cd "$ROOT/artifacts" && shasum -a 256 "$(basename "$OUTPUT")" > "$(basename "$OUTPUT").sha256")
printf 'Ready DMG: %s\n' "$OUTPUT"
cat "$OUTPUT.sha256"
