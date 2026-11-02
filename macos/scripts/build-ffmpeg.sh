#!/bin/bash
# Native, isolated FFmpeg build. Requires macOS Command Line Tools and Python 3.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: build-ffmpeg.sh [options]
  --output DIR       Build root (default: /tmp/telepilot-ffmpeg-repro-ARCH)
  --arch ARCH        Native host architecture: arm64 or x86_64
  --jobs N           Parallel make jobs (default: host CPU count, capped at 8)
  --archive-dir DIR  Read previously downloaded pinned archives from DIR
  --offline          Require cached archives; never download
  --preflight        Verify and safely extract all sources; do not compile
  --force            Replace an output previously created by this script
  --help             Show this help

Products: DIR/install/bin/ffmpeg and DIR/install/share/telepilot-ffmpeg/
No sudo, Homebrew installation, system writes, or cross compilation.
EOF
}
fail() { echo "build-ffmpeg: $*" >&2; exit 1; }
host_arch="$(uname -m)"
build_arch="$host_arch"
output=""
archive_dir=""
jobs="$(getconf NPROCESSORS_ONLN 2>/dev/null || echo 4)"
[[ "$jobs" -le 8 ]] || jobs=8
offline=0; preflight=0; force=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --output|--arch|--jobs|--archive-dir)
      [[ $# -ge 2 && -n "$2" ]] || fail "Missing value for $1"
      case "$1" in
        --output) output="$2" ;; --arch) build_arch="$2" ;;
        --jobs) jobs="$2" ;; --archive-dir) archive_dir="$2" ;;
      esac
      shift 2 ;;
    --offline) offline=1; shift ;; --preflight) preflight=1; shift ;;
    --force) force=1; shift ;; *) fail "Unknown option: $1" ;;
  esac
done
[[ "$(uname -s)" == Darwin ]] || fail "Build on macOS."
[[ "$build_arch" == arm64 || "$build_arch" == x86_64 ]] || fail "Unsupported architecture."
[[ "$build_arch" == "$host_arch" ]] || fail "Run on a native $build_arch Mac; cross compilation is unsupported."
[[ "$(/usr/sbin/sysctl -in sysctl.proc_translated 2>/dev/null || true)" != 1 ]] || fail "Run outside Rosetta."
[[ "$jobs" =~ ^[1-9][0-9]*$ && "$jobs" -le 64 ]] || fail "--jobs must be between 1 and 64."
for tool in python3 curl make xcrun tar shasum otool lipo; do command -v "$tool" >/dev/null || fail "Missing prerequisite: $tool"; done
cc="$(xcrun --find clang)"
cxx="$(xcrun --find clang++)"
output="${output:-/tmp/telepilot-ffmpeg-repro-$build_arch}"
output="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$output")"
# Autoconf/FFmpeg makefiles do not consistently support special characters in prefixes.
[[ "$output" != *[!a-zA-Z0-9_./-]* ]] || fail "Use a build path without spaces or shell punctuation."
case "$output" in
  /|/tmp|/private/tmp|/Users|/Volumes|/usr|/usr/*|/bin|/bin/*|/sbin|/sbin/*|/System|/System/*|/Library|/Library/*|/Applications|/Applications/*|/opt|/opt/*)
    fail "Choose a dedicated build directory, not a system directory." ;;
esac
if [[ -e "$output" ]]; then
  [[ "$force" == 1 ]] || fail "Output exists; choose another directory or use --force."
  [[ -f "$output/.telepilot-ffmpeg-build" && ! -L "$output/.telepilot-ffmpeg-build" ]] || fail "--force only replaces output created by this script."
  rm -rf -- "$output"
fi
mkdir -p "$output/build" "$output/archives" "$output/logs" "$output/install"
touch "$output/.telepilot-ffmpeg-build"
work="$output/build"
prefix="$output/install"
export MACOSX_DEPLOYMENT_TARGET=14.0
export SDKROOT="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"

# Pin metadata is also the source of the generated redistribution manifest.
python3 - "$output" "$archive_dir" "$offline" <<'PY'
import hashlib, json, os, pathlib, posixpath, shutil, subprocess, sys, tarfile
root = pathlib.Path(sys.argv[1]); cache = pathlib.Path(sys.argv[2]) if sys.argv[2] else None
offline = sys.argv[3] == '1'
rows = [
 ('FFmpeg', '9.0.2', 'ffmpeg-9.0.2.tar.xz', 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz', '8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e', 'f/ffmpeg', 'LGPL-2.1-or-later'),
 ('LAME', '4.0', 'lame-4.0.tar.gz', 'https://downloads.sourceforge.net/project/lame/lame/4.0/lame-4.0.tar.gz', '3df5124d5ad3a98312ffd7ba6a9b36230e4f8a3e66d3ce0f425e336c32d216eb', 'l/lame', 'LGPL-2.0-or-later'),
 ('Opus', '1.6.1', 'opus-1.6.1.tar.gz', 'https://ftp.osuosl.org/pub/xiph/releases/opus/opus-1.6.1.tar.gz', '6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1', 'o/opus', 'BSD-3-Clause'),
 ('pkgconf', '3.0.10', 'pkgconf-3.0.10.tar.xz', 'https://distfiles.ariadne.space/pkgconf/pkgconf-3.0.10.tar.xz', '9371b2f6372da574d4c6dc96fe9b1463037bcf6afcb6671c1555afde7b17e2de', 'p/pkgconf', 'ISC'),
]
pins = []
for name, version, filename, url, sha, formula, license_name in rows:
    archive = root / 'archives' / filename
    cached = cache / filename if cache else None
    if cached and cached.is_file():
        shutil.copyfile(cached, archive)
    elif offline:
        raise SystemExit('Missing offline archive: ' + filename)
    else:
        subprocess.run(['curl', '--proto', '=https', '--proto-redir', '=https', '--tlsv1.2', '-fL', '--retry', '2', '--connect-timeout', '20', url, '-o', str(archive)], check=True)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != sha:
        raise SystemExit('SHA-256 mismatch; refusing to extract: ' + filename)
    source_dir = filename.removesuffix('.tar.xz').removesuffix('.tar.gz')
    # Check all members and link destinations before extraction, including older Python versions.
    with tarfile.open(archive) as source:
        for member in source:
            normalized = posixpath.normpath(member.name)
            if member.name.startswith('/') or normalized.split('/')[0] != source_dir or '..' in member.name.split('/'):
                raise SystemExit('Unsafe archive member: ' + filename)
            if member.issym() or member.islnk():
                base = posixpath.dirname(normalized) if member.issym() else ''
                target = posixpath.normpath(posixpath.join(base, member.linkname))
                if member.linkname.startswith('/') or target.split('/')[0] != source_dir:
                    raise SystemExit('Unsafe archive link: ' + filename)
            elif not (member.isfile() or member.isdir()):
                raise SystemExit('Unsupported archive member: ' + filename)
    subprocess.run(['/usr/bin/tar', '-xf', str(archive), '-C', str(root / 'build')], check=True)
    if not (root / 'build' / source_dir / 'configure').is_file():
        raise SystemExit('Missing configure script: ' + filename)
    pins.append(dict(name=name, version=version, archive=filename, sourceDirectory=source_dir, url=url, sha256=sha,
                     formulaURL='https://raw.githubusercontent.com/Homebrew/homebrew-core/master/Formula/' + formula + '.rb', license=license_name))
    print('Verified and extracted ' + filename, flush=True)
(root / 'build' / 'pins.json').write_text(json.dumps(pins, indent=2) + '\n')
PY
if [[ "$preflight" == 1 ]]; then echo "Preflight passed: $work"; exit 0; fi

base_cflags="-arch $build_arch -O2 -mmacosx-version-min=14.0"
build_component() {
  local name="$1" source="$2" flags="$3"
  shift 3
  python3 - "$work/$name-build.json" "$name" "$cc" "$flags" "$@" <<'PY'
import json, pathlib, sys
pathlib.Path(sys.argv[1]).write_text(json.dumps(dict(name=sys.argv[2], arguments=sys.argv[5:], environment=dict(CC=sys.argv[3], CFLAGS=sys.argv[4], ac_cv_prog_cc_c23='no')), indent=2))
PY
  echo "Building $name (logs: $output/logs)"
  (
    cd "$work/$source"
    env CC="$cc" CFLAGS="$flags" ac_cv_prog_cc_c23=no ./configure "$@" > "$output/logs/$name-configure.log" 2>&1
    make -j "$jobs" > "$output/logs/$name-build.log" 2>&1
    make install > "$output/logs/$name-install.log" 2>&1
  )
}
build_component pkgconf pkgconf-3.0.10 "$base_cflags" --prefix="$prefix" --disable-shared --enable-static --disable-dependency-tracking
ln -s pkgconf "$prefix/bin/pkg-config"
build_component LAME lame-4.0 "$base_cflags -Wno-implicit-function-declaration" --prefix="$prefix" --disable-shared --enable-static --disable-frontend --disable-decoder --disable-nasm --disable-dependency-tracking
build_component Opus opus-1.6.1 "$base_cflags" --prefix="$prefix" --disable-shared --enable-static --disable-doc --disable-extra-programs --disable-dependency-tracking

ffmpeg_args=(--prefix="$prefix" --arch="$build_arch" --target-os=darwin --cc="$cc" --cxx="$cxx"
  --disable-doc --disable-debug --disable-ffplay --disable-ffprobe --disable-shared --enable-static
  --disable-autodetect --enable-pthreads --enable-zlib --enable-securetransport --enable-libmp3lame --enable-libopus
  --pkg-config="$prefix/bin/pkg-config" --pkg-config-flags=--static
  --extra-cflags="-I$prefix/include $base_cflags" --extra-ldflags="-L$prefix/lib -arch $build_arch -mmacosx-version-min=14.0")
if [[ "$build_arch" == x86_64 ]] && ! command -v nasm >/dev/null; then
  echo "nasm unavailable; disabling x86 assembly (portable C implementation remains enabled)."
  ffmpeg_args+=(--disable-x86asm)
fi
echo "Building FFmpeg with default internal codecs/formats (logs: $output/logs)"
python3 - "$work/ffmpeg-arguments.json" "${ffmpeg_args[@]}" <<'PY'
import json, pathlib, sys
pathlib.Path(sys.argv[1]).write_text(json.dumps(sys.argv[2:], indent=2))
PY
(
  cd "$work/ffmpeg-9.0.2"
  ./configure "${ffmpeg_args[@]}" > "$output/logs/FFmpeg-configure.log" 2>&1
  make -j "$jobs" > "$output/logs/FFmpeg-build.log" 2>&1
  make install > "$output/logs/FFmpeg-install.log" 2>&1
)
python3 - "$output" "$build_arch" "$cc" <<'PY'
import datetime, hashlib, json, pathlib, shutil, subprocess, sys
root = pathlib.Path(sys.argv[1]); arch = sys.argv[2]; prefix = root / 'install'; work = root / 'build'
binary = prefix / 'bin/ffmpeg'; share = prefix / 'share/telepilot-ffmpeg'; licenses = share / 'licenses'
licenses.mkdir(parents=True, exist_ok=True); (share / 'sources').mkdir(exist_ok=True)
actual_arch = subprocess.check_output(['lipo', '-archs', str(binary)], text=True).strip()
if actual_arch != arch: raise SystemExit('Unexpected binary architecture: ' + actual_arch)
dependencies = [line.strip().split(' (')[0] for line in subprocess.check_output(['otool', '-L', str(binary)], text=True).splitlines()[1:]]
if any(not p.startswith(('/usr/lib/', '/System/Library/')) for p in dependencies):
    raise SystemExit('Unexpected external dynamic dependency')
pins = json.loads((work / 'pins.json').read_text())
for item in pins:
    shutil.copyfile(root / 'archives' / item['archive'], share / 'sources' / item['archive'])
    item['localSourceArchive'] = str(share / 'sources' / item['archive'])
    source = work / item['sourceDirectory']
    component_licenses = licenses / item['name']; component_licenses.mkdir(exist_ok=True)
    for pattern in ['COPYING*', 'LICENSE*', 'AUTHORS*']:
        for path in source.glob(pattern):
            if path.is_file(): shutil.copyfile(path, component_licenses / path.name)
    del item['archive']; del item['sourceDirectory']
manifest = dict(name='FFmpeg', version='9.0.2', architecture=arch, minimumMacOS='14.0',
    binarySHA256=hashlib.sha256(binary.read_bytes()).hexdigest(), license='LGPL-2.1-or-later',
    configureArguments=json.loads((work / 'ffmpeg-arguments.json').read_text()),
    compiler=subprocess.check_output([sys.argv[3], '--version'], text=True).strip(),
    builtAtUTC=datetime.datetime.now(datetime.timezone.utc).isoformat(), sources=pins[:3], buildToolSources=pins[3:],
    systemDependencies=dependencies,
    dependencyBuilds=[json.loads((work / (name + '-build.json')).read_text()) for name in ['LAME', 'Opus', 'pkgconf']],
    notes=['Default internal FFmpeg features; static LAME/Opus; no GPL/nonfree options or auto-detected external libraries.',
           'Source archives included in sources/. Build-only pkgconf is not a runtime dependency.',
           'Reproducible process and pinned inputs; byte-identical output requires the same compiler/SDK/build path.'])
(share / 'source-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
(licenses / 'NOTICE.txt').write_text('FFmpeg 9.0.2: LGPL-2.1-or-later. LAME 4.0: LGPL-2.0-or-later. Opus 1.6.1: BSD-3-Clause and accompanying patent terms. pkgconf 3.0.10: ISC (build tool only).\nSee component licenses and source-manifest.json. Exact source archives are included in ../sources/. No GPL/nonfree FFmpeg options enabled.\n')
print('Binary: ' + str(binary)); print('Manifest: ' + str(share / 'source-manifest.json'))
print('Licenses: ' + str(licenses)); print('SHA-256: ' + manifest['binarySHA256'])
PY
