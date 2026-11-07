"""Build a version-preserving VPM Pages site from tracked package manifests."""

import argparse
import copy
import hashlib
import html
import io
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import tempfile
from urllib.error import HTTPError
from urllib.parse import unquote, urlparse
from urllib.request import urlopen
from zipfile import ZipFile

from package_vpm import build_package


def read_bytes(location, *, first_listing=False):
    location = str(location)
    if urlparse(location).scheme in {"http", "https"}:
        try:
            with urlopen(location, timeout=60) as response:
                return response.read()
        except HTTPError as error:
            if first_listing and error.code == 404:
                return b'{"packages": {}}'
            raise
    return Path(location).read_bytes()


def archive_bytes(entry, previous_listing):
    """Reuse a verified sibling archive for an explicit local history fixture."""
    if urlparse(str(previous_listing)).scheme not in {"http", "https"}:
        cached = Path(previous_listing).parent / PurePosixPath(urlparse(entry["url"]).path).name
        if cached.is_file():
            data = cached.read_bytes()
        else:
            data = read_bytes(entry["url"])
    else:
        data = read_bytes(entry["url"])
    actual = hashlib.sha256(data).hexdigest()
    if actual.lower() != entry["zipSHA256"].lower():
        raise ValueError(f"Published archive SHA256 mismatch: {entry['url']} ({actual})")
    with ZipFile(io.BytesIO(data)) as archive:
        if archive.testzip() is not None:
            raise ValueError(f"Published archive CRC failure: {entry['url']}")
    return data


def archive_payload(data):
    """Ignore legacy ZIP timestamps/compression while retaining content and execution."""
    with ZipFile(io.BytesIO(data)) as archive:
        return {entry.filename: (archive.read(entry), bool((entry.external_attr >> 16) & 0o111))
                for entry in archive.infolist() if not entry.is_dir()}


def repository_archive_path(url, repository_url):
    prefix = repository_url.rstrip("/") + "/"
    if not url.startswith(prefix):
        return None
    relative = PurePosixPath(unquote(url[len(prefix):]))
    if relative.is_absolute() or ".." in relative.parts or relative.suffix != ".zip":
        raise ValueError(f"Invalid repository archive URL: {url}")
    return relative


def discover_packages(repo):
    tracked = subprocess.check_output(
        ["git", "ls-files", "-z", "Assets/UnityBox/*/package.json"], cwd=repo)
    manifests = []
    for item in tracked.decode().split("\0"):
        if item and (repo / item).is_file():
            manifests.append(repo / item)
    if not manifests:
        raise ValueError("No tracked UnityBox package manifests found")
    return sorted(manifests)


def build_listing(repo, output, repository_url, previous_listing=None):
    repo, output = Path(repo).resolve(), Path(output).resolve()
    repository_url = repository_url.rstrip("/")
    previous_listing = previous_listing or repository_url + "/vpm.json"
    previous = json.loads(read_bytes(previous_listing, first_listing=True))
    packages = copy.deepcopy(previous["packages"])
    output.mkdir(parents=True, exist_ok=True)
    retained_archives = {}
    for package_name, package in packages.items():
        for version, entry in package["versions"].items():
            target = repository_archive_path(entry["url"], repository_url)
            if target is None:
                continue
            data = archive_bytes(entry, previous_listing)
            target = output / target
            target.parent.mkdir(parents=True, exist_ok=True)
            if target in retained_archives and retained_archives[target] != data:
                raise ValueError(f"Published versions conflict at {target.name}")
            retained_archives[target] = data
            target.write_bytes(data)

    built = []
    with tempfile.TemporaryDirectory(prefix="unitybox-vpm-") as temporary:
        for manifest_path in discover_packages(repo):
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            name, version = manifest["name"], manifest["version"]
            filename = f"{name}-{version}.zip"
            candidate = Path(temporary) / filename
            build_package(manifest_path.parent, candidate)
            data = candidate.read_bytes()
            versions = packages.setdefault(name, {"versions": {}})["versions"]
            if version in versions:
                published = versions[version]
                old_data = archive_bytes(published, previous_listing)
                if archive_payload(old_data) != archive_payload(data):
                    raise ValueError(f"{name}@{version} is already published with different content; increment its package.json version")
                # Preserve the original manifest, URL, ZIP bytes and SHA. Older ZIPs
                # may use variable timestamps; identical payload is still immutable.
                built.append({"name": name, "version": version, "status": "preserved"})
                continue
            (output / filename).write_bytes(data)
            versions[version] = {**manifest, "url": repository_url + "/" + filename,
                                 "zipSHA256": hashlib.sha256(data).hexdigest()}
            built.append({"name": name, "version": version, "status": "added"})

    listing = {**previous, "name": previous.get("name", "UnityBox"),
               "id": previous.get("id", "top.sealoong.unitybox.listing"),
               "url": repository_url + "/vpm.json", "author": previous.get("author", "SeaLoong"),
               "packages": packages}
    (output / "vpm.json").write_text(json.dumps(listing, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    listing_url = html.escape(listing["url"], quote=True)
    (output / "index.html").write_text(f'''<!doctype html>
<html lang="zh"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>UnityBox VPM Repository</title>
<style>body{{font:18px system-ui;margin:10vh auto;padding:24px;max-width:640px;line-height:1.6}}a{{color:#0969da}}code{{overflow-wrap:anywhere}}</style>
<h1>UnityBox VPM Repository</h1><p><a href="vcc://vpm/addRepo?url={listing_url}">添加到 VCC / ALCOM</a></p>
<p>或手动添加 VPM 源地址：<code>{listing_url}</code></p></html>
''', encoding="utf-8")
    return {"packages": built, "preserved_versions": sum(len(item["versions"]) for item in previous["packages"].values()),
            "output": str(output)}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("_site"))
    parser.add_argument("--repository-url", default=os.environ.get("VPM_REPOSITORY_URL"))
    parser.add_argument("--previous-listing", default=os.environ.get("VPM_PREVIOUS_LISTING"))
    args = parser.parse_args(argv)
    repository_url = args.repository_url or "https://" + os.environ.get("GITHUB_OWNER", "SeaLoong").lower() + ".github.io/" + os.environ.get("GITHUB_REPO", "UnityBox")
    print(json.dumps(build_listing(args.repo, args.output, repository_url, args.previous_listing), indent=2))


if __name__ == "__main__":
    main()
