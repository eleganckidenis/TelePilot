#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-/tmp/telepilot-clang-cache}"
export SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-/tmp/telepilot-swift-cache}"
FLAGS=(--package-path "$ROOT" --cache-path /tmp/telepilot-spm-cache --config-path /tmp/telepilot-spm-config --security-path /tmp/telepilot-spm-security --disable-sandbox --build-system native)
FRAMEWORKS=/Library/Developer/CommandLineTools/Library/Developer/Frameworks
if [[ "$(xcode-select -p)" == /Library/Developer/CommandLineTools && -d "$FRAMEWORKS/Testing.framework" ]]; then
  FLAGS+=(-Xswiftc -F -Xswiftc "$FRAMEWORKS" -Xlinker -F -Xlinker "$FRAMEWORKS" -Xlinker -rpath -Xlinker "$FRAMEWORKS" -Xlinker -rpath -Xlinker /Library/Developer/CommandLineTools/Library/Developer/usr/lib)
fi
if [[ ${TELEPILOT_APPKIT_TEST_HOST:-0} == 1 ]]; then
  swift build "${FLAGS[@]}" --build-tests
  bin="$(swift build "${FLAGS[@]}" --show-bin-path)"
  bundle="$bin/TelePilotMacPackageTests.xctest/Contents/MacOS/TelePilotMacPackageTests"
  platform="$(xcode-select -p)/Platforms/MacOSX.platform/Developer"
  test -f "$bundle"
  app="$bin/TelePilotAppKitTestHost.app"
  mkdir -p "$app/Contents/MacOS"
  cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>pl.telepilot.appkit-test-host</string>
  <key>CFBundleName</key><string>TelePilot AppKit Test Host</string>
  <key>CFBundleExecutable</key><string>TelePilotAppKitTestHost</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>LSUIElement</key><false/>
</dict></plist>
PLIST
  xcrun swiftc -parse-as-library -F "$platform/Library/Frameworks" -L "$platform/usr/lib" \
    -Xlinker -rpath -Xlinker "$platform/Library/Frameworks" -Xlinker -rpath -Xlinker "$platform/usr/lib" \
    "$ROOT/scripts/AppKitTestHost.swift" -o "$app/Contents/MacOS/TelePilotAppKitTestHost"
  codesign --force --sign - "$app"
  export TELEPILOT_SWIFT_TEST_BUNDLE="$bundle"
  exec "$app/Contents/MacOS/TelePilotAppKitTestHost" --testing-library swift-testing "$@"
fi
exec swift test "${FLAGS[@]}" "$@"
