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
if [[ ${TELEPILOT_DEBUG_TEST_EXIT:-0} == 1 ]]; then
  swift build "${FLAGS[@]}" --build-tests
  bin="$(swift build "${FLAGS[@]}" --show-bin-path)"
  bundle="$bin/TelePilotMacPackageTests.xctest/Contents/MacOS/TelePilotMacPackageTests"
  toolchain="$(dirname "$(xcrun -f swiftc)")/.."
  platform="$(xcode-select -p)/Platforms/MacOSX.platform/Developer"
  test -f "$bundle"
  exec xcrun lldb --batch \
    -o 'settings set target.disable-aslr false' \
    -o "settings set target.env-vars DYLD_FRAMEWORK_PATH=$platform/Library/Frameworks DYLD_LIBRARY_PATH=$platform/usr/lib" \
    -o 'breakpoint set --name exit --name _exit --name abort' \
    -o 'breakpoint set --name "-[NSApplication terminate:]"' \
    -o run -o 'thread backtrace' \
    -- "$toolchain/libexec/swift/pm/swiftpm-testing-helper" \
      --test-bundle-path "$bundle" --testing-library swift-testing \
      --filter managerOpensForFirstRunAndImportsSwitchesRemovesAndReturnsFocus
fi
exec swift test "${FLAGS[@]}" "$@"
