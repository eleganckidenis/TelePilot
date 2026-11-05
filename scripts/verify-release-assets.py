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
SHARED_VLC_SOURCES = "TelePilot-2.0.1-vlc-third-party-sources.zip"
SHARED_VLC_SHA256 = "800818898a94ce8ebaa5bfbbbe1246251ca19ef990e735b60ae26d1ab0e8df93"


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
    parser.add_argument("--commit", default="HEAD", help="Commit used for the release")
    parser.add_argument("--windows-rid", action="append", choices=("win-x64", "win-arm64"),
                        help="Windows package to verify; defaults to both architectures")
    parser.add_argument("--github-json", type=Path, help="Release object from GitHub REST API")
    parser.add_argument("--shared-vlc-sources", type=Path, help="Local copy of the 2.0.1 VLC source asset reused in 2.0.2")
    parser.add_argument("--published", action="store_true", help="Require a public release in GitHub metadata")
    args = parser.parse_args()
    version = args.version
    require(bool(re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", version)),
            "Expected a canonical version such as 2.0.0")
    tag = f"v{version}"
    stem = f"TelePilot-{version}"
    windows_rids = tuple(dict.fromkeys(args.windows_rid or ("win-x64", "win-arm64")))
    windows_installers = [f"{stem}-{rid}-setup.exe" for rid in windows_rids]
    mac_installer = f"{stem}-Apple-Silicon.dmg"
    source_zip = f"{stem}-source.zip"
    mac_sources_zip = f"{stem}-macos-third-party-sources.zip"
    names = [*windows_installers, mac_installer, source_zip, mac_sources_zip]
    vlc_sources_zip = None
    if version == "2.0.2":
        require(args.shared_vlc_sources is not None, "Provide the shared VLC source archive for 2.0.2")
        require(file_digest(args.shared_vlc_sources) == SHARED_VLC_SHA256, "Shared VLC archive differs from v2.0.1")
    elif tuple(map(int, version.split("."))) >= (2, 0, 1):
        vlc_sources_zip = f"{stem}-vlc-third-party-sources.zip"
        names.append(vlc_sources_zip)
    expected = {name + suffix for name in names for suffix in ("", ".sha256")}
    require({p.name for p in args.directory.iterdir()} == expected, "Unexpected or missing release files")
    digests = {name: file_digest(args.directory / name) for name in sorted(expected)}
    for name in names:
        checksum_path = args.directory / (name + ".sha256")
        require(checksum_path.stat().st_size <= 256, f"Invalid checksum length: {name}")
        require(checksum_path.read_text(encoding="ascii").split() == [digests[name], name],
                f"Incorrect checksum or name: {name}")

    for name in windows_installers:
        with (args.directory / name).open("rb") as installer:
            require(installer.read(2) == b"MZ", f"Windows installer is not a PE executable: {name}")
    require(all(0 < (args.directory / name).stat().st_size <= 512 * 1024 * 1024
                for name in (*windows_installers, mac_installer)),
            "Binary asset exceeds the updater's size limit")

    reference = subprocess.check_output(
        ["git", "-C", str(ROOT), "-c", "core.autocrlf=false", "-c", "core.eol=lf",
         "archive", "--format=zip", f"--prefix={stem}/", args.commit]
    )
    with zipfile.ZipFile(io.BytesIO(reference)) as tracked, zipfile.ZipFile(args.directory / source_zip) as shipped:
        tracked_names = source_files(tracked)
        require(tracked_names == source_files(shipped), "Source ZIP does not contain the exact committed tree")
        for name in sorted(tracked_names):
            with tracked.open(name) as expected_stream, shipped.open(name) as actual_stream:
                require(sha256(expected_stream) == sha256(actual_stream), f"Source ZIP differs at {name}")

        require(shipped.read(f"{stem}/macos/VERSION").decode().strip() == version, "Wrong macOS source version")
        windows_project = shipped.read(f"{stem}/src/TelePilot.App/TelePilot.App.csproj").decode()
        require(f"<Version>{version}</Version>" in windows_project, "Wrong Windows source version")

    prefix = f"{stem}-macos-third-party-sources/"
    with zipfile.ZipFile(args.directory / mac_sources_zip) as sources:
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

    if vlc_sources_zip is not None:
        verify_vlc_sources(args.directory / vlc_sources_zip)
    elif version == "2.0.2":
        verify_vlc_sources(args.shared_vlc_sources)

    if args.github_json is not None:
        release = json.loads(args.github_json.read_text(encoding="utf-8"))
        require(release["tag_name"] == tag and release["prerelease"] is False, "Wrong GitHub release")
        if version == "2.0.2":
            base = f"https://github.com/{REPOSITORY}/releases/download/v2.0.1/{SHARED_VLC_SOURCES}"
            links = re.findall(r"\]\((https://[^\s)]+)\)", release["body"] or "")
            require(base in links and f"{base}.sha256" in links, "Release notes must link to the shared VLC sources and checksum")
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


def verify_vlc_sources(path):
    with zipfile.ZipFile(path) as archive:
        names = source_files(archive)
        manifest = json.loads(archive.read("SOURCE-MANIFEST.json"))
        require(manifest["vlcVersion"] == "3.0.24", "Wrong VLC source version")
        # The three shipped platforms have an FPU and use libvorbis; the
        # upstream Tremor target for processors without an FPU is out of scope.
        require({item["component"] for item in manifest["missing"]} <= {"tremor"},
                "Incomplete VLC source archive")
        source_index = {item["path"]: item for item in manifest["files"]}
        require(len(source_index) == len(manifest["files"]), "Duplicate VLC source manifest entry")
        require(source_index.get("vlc-3.0.24.tar.xz", {}).get("sha256")
                == "e7cab503d1d7d5849b89d2cf0e1ee60d0ef6d012407791b644b9cfc0cc225fdf" and
                sum(bool(item.get("sha512")) for item in source_index.values()) >= 127,
                "Missing VLC or contrib source materials")
        git_pins = set()
        for name, item in source_index.items():
            require(archive.getinfo(name).file_size == item["size"], f"VLC source size mismatch: {name}")
            with archive.open(name) as stream:
                require(sha256(stream) == item["sha256"], f"VLC source checksum mismatch: {name}")
            if "gitPin" in item:
                pin = item["gitPin"]
                require(archive.read(pin).decode("ascii").split()
                        == [item["commit"], f"../tarballs/{PurePosixPath(name).name}"],
                        f"VLC source commit mismatch: {name}")
                git_pins.add(pin)
        notices = json.loads(archive.read("NOTICE-MANIFEST.json"))
        require(bool(notices), "Missing VLC license notices")
        for notice in notices:
            with archive.open(notice["path"]) as stream:
                require(sha256(stream) == notice["sha256"], f"VLC notice checksum mismatch: {notice['path']}")
        require(all(notice["sourceArchive"] in source_index for notice in notices),
                "A VLC notice references missing corresponding source")
        inventory = json.loads(archive.read("vlc-binaries.json"))
        require({item["platform"] for item in inventory} == {"win-x64", "win-arm64", "macos-arm64"}
                and len(inventory) == 970, "Incomplete VLC binary provenance")
        require({"README.md", "SOURCE-MANIFEST.json", "NOTICE-MANIFEST.json",
                 "vlc-binaries.json", "vlc-upstream-build-configuration.txt"}
                | set(source_index) | git_pins | {notice["path"] for notice in notices} == names,
                "Unexpected VLC source archive contents")


if __name__ == "__main__":
    main()
