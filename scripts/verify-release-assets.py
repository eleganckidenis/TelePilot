#!/usr/bin/env python3
"""Check the exact TelePilot release files (and, if provided, GitHub REST metadata)."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
from urllib.parse import urlparse
import zipfile

REPOSITORY = "eleganckidenis/TelePilot"
ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise SystemExit(message)


def sha256(stream):
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
    return digest.hexdigest()


def file_digest(path):
    with path.open("rb") as stream:
        return sha256(stream)


def source_files(archive):
    names = set()
    for entry in archive.infolist():
        name = entry.filename
        path = PurePosixPath(name)
        require(not path.is_absolute() and ".." not in path.parts and "\\" not in name,
                f"Unsafe archive path: {name}")
        if not entry.is_dir():
            require(name not in names, f"Duplicate archive path: {name}")
            names.add(name)
    return names


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("directory", type=Path)
    parser.add_argument("--commit", default="HEAD", help="Commit used to build both binaries")
    parser.add_argument("--github-json", type=Path, help="Release object from GitHub REST API")
    parser.add_argument("--published", action="store_true", help="Require a public release in GitHub metadata")
    args = parser.parse_args()
    version = args.version
    require(bool(re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", version)),
            "Expected a canonical version such as 2.0.0")
    tag = f"v{version}"
    stem = f"TelePilot-{version}"
    names = [stem + suffix for suffix in (
        "-win-x64-setup.exe", "-Apple-Silicon.dmg", "-source.zip", "-macos-third-party-sources.zip"
    )]
    expected = {name + suffix for name in names for suffix in ("", ".sha256")}
    require({p.name for p in args.directory.iterdir()} == expected, "Unexpected or missing release files")
    digests = {name: file_digest(args.directory / name) for name in sorted(expected)}
    for name in names:
        checksum_path = args.directory / (name + ".sha256")
        require(checksum_path.stat().st_size <= 256, f"Invalid checksum length: {name}")
        require(checksum_path.read_text(encoding="ascii").split() == [digests[name], name],
                f"Incorrect checksum or name: {name}")

    with (args.directory / names[0]).open("rb") as installer:
        require(installer.read(2) == b"MZ", "Windows installer is not a PE executable")
    require(all((args.directory / name).stat().st_size <= 512 * 1024 * 1024 for name in names[:2]),
            "Binary asset exceeds the updater's size limit")

    reference = subprocess.check_output(
        ["git", "-C", str(ROOT), "-c", "core.autocrlf=false", "-c", "core.eol=lf",
         "archive", "--format=zip", f"--prefix={stem}/", args.commit]
    )
    with zipfile.ZipFile(io.BytesIO(reference)) as tracked, zipfile.ZipFile(args.directory / names[2]) as shipped:
        tracked_names = source_files(tracked)
        require(tracked_names == source_files(shipped), "Source ZIP does not contain the exact committed tree")
        for name in sorted(tracked_names):
            with tracked.open(name) as expected_stream, shipped.open(name) as actual_stream:
                require(sha256(expected_stream) == sha256(actual_stream), f"Source ZIP differs at {name}")

        require(shipped.read(f"{stem}/macos/VERSION").decode().strip() == version, "Wrong macOS source version")
        windows_project = shipped.read(f"{stem}/src/TelePilot.App/TelePilot.App.csproj").decode()
        require(f"<Version>{version}</Version>" in windows_project, "Wrong Windows source version")

    prefix = f"{stem}-macos-third-party-sources/"
    with zipfile.ZipFile(args.directory / names[3]) as sources:
        paths = source_files(sources)
        require(all(name.startswith(prefix) for name in paths), "Wrong dependency archive prefix")
        require({prefix + "build-ffmpeg.sh", prefix + "FFMPEG_BUILD.md", prefix + "source-manifest.json"} <= paths,
                "Missing FFmpeg build recipe")
        require(any(name.startswith(prefix + "licenses/") for name in paths), "Missing FFmpeg license notices")
        manifest = json.loads(sources.read(prefix + "source-manifest.json"))
        require(manifest["architecture"] == "arm64" and bool(manifest["sources"]), "Wrong FFmpeg provenance")
        dependencies = set()
        for component in manifest["sources"] + manifest.get("buildToolSources", []):
            filename = PurePosixPath(urlparse(component["url"]).path).name
            name = prefix + "sources/" + filename
            dependencies.add(name)
            with sources.open(name) as stream:
                require(sha256(stream) == component["sha256"].lower(), f"Incorrect FFmpeg source: {filename}")
        require({name for name in paths if name.startswith(prefix + "sources/")} == dependencies,
                "Unlisted FFmpeg source archives")
        require(sources.testzip() is None, "Corrupt FFmpeg source archive")

    if args.github_json is not None:
        release = json.loads(args.github_json.read_text(encoding="utf-8"))
        require(release["tag_name"] == tag and release["prerelease"] is False, "Wrong GitHub release")
        if args.published:
            require(release["draft"] is False and bool(release["published_at"]), "Release is still a draft")
        assets = release["assets"]
        require(len(assets) == len(expected) and {asset["name"] for asset in assets} == expected,
                "Unexpected GitHub asset list")
        for asset in assets:
            name = asset["name"]
            require(asset["state"] == "uploaded" and asset["size"] == (args.directory / name).stat().st_size
                    and asset["digest"] == "sha256:" + digests[name], f"GitHub metadata differs: {name}")
            if args.published:
                url = f"https://github.com/{REPOSITORY}/releases/download/{tag}/{name}"
                require(asset["browser_download_url"] == url, f"Incorrect download URL: {name}")
    else:
        require(not args.published, "--published requires --github-json")

    for name in names:
        print(f"{digests[name]}  {name}")


if __name__ == "__main__":
    main()
