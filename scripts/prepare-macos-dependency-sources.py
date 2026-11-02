#!/usr/bin/env python3
"""Archive verified corresponding sources and build materials for bundled macOS FFmpeg."""
import argparse
import hashlib
import json
from pathlib import Path
from urllib.parse import urlparse
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--archive-dir', type=Path, required=True)
parser.add_argument('--manifest', type=Path, required=True)
parser.add_argument('--license-dir', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
metadata = json.loads(args.manifest.read_text())
version = (root / 'macos/VERSION').read_text().strip()
entries = []
for component in metadata['sources'] + metadata.get('buildToolSources', []):
    name = Path(urlparse(component['url']).path).name
    archive = args.archive_dir / name
    actual = hashlib.sha256(archive.read_bytes()).hexdigest()
    if actual != component['sha256']:
        raise SystemExit(f'Source checksum mismatch: {name}')
    entries.append((archive, 'sources/' + name))
for path in args.license_dir.rglob('*'):
    if path.is_file():
        entries.append((path, 'licenses/' + path.relative_to(args.license_dir).as_posix()))
entries += [(root / 'macos/scripts/build-ffmpeg.sh', 'build-ffmpeg.sh'),
            (root / 'macos/docs/FFMPEG_BUILD.md', 'FFMPEG_BUILD.md'),
            (args.manifest, 'source-manifest.json')]
args.output.parent.mkdir(parents=True, exist_ok=True)
if args.output.exists():
    raise SystemExit('Output exists; preserve it before rebuilding')
with zipfile.ZipFile(args.output, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
    for path, name in entries:
        archive.write(path, f'TelePilot-{version}-macos-third-party-sources/{name}')
digest = hashlib.sha256(args.output.read_bytes()).hexdigest()
args.output.with_name(args.output.name + '.sha256').write_text(f'{digest}  {args.output.name}\n')
print(f'Verified {len(metadata["sources"]) + len(metadata.get("buildToolSources", []))} source archives; {args.output.name}; SHA-256 {digest}')
