#!/usr/bin/env python3
"""Install the pinned GameCI Linux Unity Editor layer, with digest verification.

This installs binaries only; it does not activate a Unity license.
The image is publicly distributed by GameCI, not an official Unity image.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request

VERSION = "2022.3.22f1"
IMAGE = "unityci/editor"
MANIFEST_DIGEST = "sha256:1c7b9cf8a65a304bb99f222d91c3452f99148ad647d1416ed658a3908a9f8dea"
EDITOR_DIGEST = "sha256:b252a233962509a78660c17e85fde0358cbfbb0f989ce630e503a887b6e093f9"
EDITOR_BYTES = 3699079532


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def request(url, token=None):
    headers = {"Authorization": "Bearer " + token} if token else {}
    return urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=120)


def verify_version(binary):
    result = subprocess.run([str(binary), "-version"], capture_output=True, text=True, timeout=30)
    if result.returncode != 0 or result.stdout.strip() != VERSION:
        raise RuntimeError("Editor version check failed: " + result.stdout + result.stderr)


def install(cache, destination):
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / (EDITOR_DIGEST.split(":")[1] + ".tar.gz")
    stamp = destination / "unitybox-install.json"
    binary = destination / "opt/unity/Editor/Unity"
    if stamp.is_file() and binary.is_file():
        if json.loads(stamp.read_text()).get("layer") == EDITOR_DIGEST:
            verify_version(binary)
            print("Reusing installed Editor:", binary, flush=True)
            return binary
        raise RuntimeError("Installation stamp differs; use a new destination.")
    if destination.exists() and any(destination.iterdir()):
        raise RuntimeError("Destination is not empty and has no matching completed installation.")
    if not archive.exists():
        if shutil.disk_usage(cache).free < 13 * 1024**3:
            raise RuntimeError("Need at least 13 GiB free for the archive and extracted Editor.")
        with request("https://auth.docker.io/token?service=registry.docker.io&scope=repository:unityci/editor:pull") as response:
            token = json.load(response)["token"]
        with request("https://registry-1.docker.io/v2/" + IMAGE + "/manifests/" + MANIFEST_DIGEST, token) as response:
            manifest_bytes = response.read()
        if hashlib.sha256(manifest_bytes).hexdigest() != MANIFEST_DIGEST.split(":")[1]:
            raise RuntimeError("OCI manifest digest mismatch.")
        manifest = json.loads(manifest_bytes)
        if not any(layer["digest"] == EDITOR_DIGEST and layer["size"] == EDITOR_BYTES for layer in manifest["layers"]):
            raise RuntimeError("Pinned Editor layer not found in verified manifest.")
        partial = archive.with_suffix(".partial")
        downloaded = 0
        next_progress = 512 * 1024**2
        print("Downloading Unity", VERSION, "from pinned GameCI OCI layer (3.45 GiB).", flush=True)
        with request("https://registry-1.docker.io/v2/" + IMAGE + "/blobs/" + EDITOR_DIGEST, token) as response, partial.open("wb") as output:
            while chunk := response.read(8 * 1024 * 1024):
                output.write(chunk)
                downloaded += len(chunk)
                if downloaded >= next_progress:
                    print("Downloaded", downloaded // 1024**2, "MiB", flush=True)
                    next_progress += 512 * 1024**2
        if downloaded != EDITOR_BYTES or sha256(partial) != EDITOR_DIGEST.split(":")[1]:
            partial.unlink(missing_ok=True)
            raise RuntimeError("Editor layer size/digest mismatch; discarded download.")
        partial.replace(archive)
    elif archive.stat().st_size != EDITOR_BYTES or sha256(archive) != EDITOR_DIGEST.split(":")[1]:
        raise RuntimeError("Cached Editor layer failed integrity verification.")
    destination.mkdir(parents=True, exist_ok=True)
    print("Verified layer SHA256; extracting Editor only.", flush=True)
    with tarfile.open(archive) as tar:
        members = [member for member in tar if member.name.rstrip("/") == "opt/unity/Editor" or member.name.startswith("opt/unity/Editor/")]
        if not members:
            raise RuntimeError("Editor directory missing from layer.")
        tar.extractall(destination, members=members, filter="data")
    if not binary.is_file() or not os.access(binary, os.X_OK):
        raise RuntimeError("Extracted Editor executable missing.")
    verify_version(binary)
    stamp.write_text(json.dumps({"version": VERSION, "image": IMAGE, "manifest": MANIFEST_DIGEST, "layer": EDITOR_DIGEST}, indent=2) + "\n")
    print("Installed Editor:", binary, flush=True)
    return binary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=Path("/workspace/unitybox-setup/unity/cache"))
    parser.add_argument("--destination", type=Path, default=Path("/workspace/unitybox-setup/unity/editor"))
    args = parser.parse_args()
    install(args.cache.resolve(), args.destination.resolve())
