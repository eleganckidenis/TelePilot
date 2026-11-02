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
exec swift test "${FLAGS[@]}" "$@"
