#!/usr/bin/env python3
"""Cache real, pinned Unity/VRChat packages for offline test-project preparation.

Normal runs verify dependencies.lock.json and cached archives/package contents.
Use --update-lock deliberately to resolve the selected profile and refresh the lock.
Unity registry archives are checked against the official dist.shasum before their
SHA256 is recorded. Editor modules are used in place, never replaced by stubs.
"""
import argparse
import copy
import errno
import hashlib
import json
from pathlib import Path
import re
import shutil
import socket
import tarfile
import tempfile
import time
import urllib.request
import urllib.error
import zipfile

EDITOR_VERSION = "2022.3.22f1"
CORE_ROOTS = {"com.unity.test-framework": "1.1.33", "com.unity.ext.nunit": "1.0.6"}
VRC_ROOTS = {
    **CORE_ROOTS,
    # MA 1.18 needs the newer VRCConstraintSource constructor; SDK 3.7 lacks it.
    "com.vrchat.base": "3.8.2", "com.vrchat.avatars": "3.8.2",
    # SDK 3.8.2 explicitly declares UGUI; keep it pinned in the isolated fixture.
    "com.unity.ugui": "1.0.0",
    "nadena.dev.modular-avatar": "1.18.0", "nadena.dev.ndmf": "1.11.0",
    # Exact Unity versions declared by VRChat SDK Base 3.8.2 take precedence
    # over lower transitive minimums (e.g. MA's Newtonsoft 2.0.0).
    "com.unity.burst": "1.8.7", "com.unity.collections": "2.1.4",
    "com.unity.mathematics": "1.2.6", "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.unity.timeline": "1.7.6", "com.unity.xr.management": "4.3.3",
    "com.unity.xr.oculus": "4.0.0", "com.unity.postprocessing": "3.2.2",
}
RELEASES = {
    "com.vrchat.base": ("3.8.2", "vrchat/packages", "768736b737bd7e2624399301e19175f8b4d1e5022d4a3d64bb2f16c9d74a2c94"),
    "com.vrchat.avatars": ("3.8.2", "vrchat/packages", "e770adf8ac2a4f440b7fd85a9d6b7d6895448f75ae288d6e9193848ca8400bdf"),
    "nadena.dev.modular-avatar": ("1.18.0", "bdunderscore/modular-avatar", "3041535095c1f3365721aae786407db5f0a3c0491d1e2942918c9aa59edddefa"),
    "nadena.dev.ndmf": ("1.11.0", "bdunderscore/ndmf", "bc27b4da59d6aea70127d3c23ac8dd1024f435c92879834ec3261514ed46feee"),
}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def digest(path, algorithm="sha256"):
    value = hashlib.new(algorithm)
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def tree_digest(root):
    value = hashlib.sha256()
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Package symlink is not supported: {path}")
        if path.is_file():
            value.update(path.relative_to(root).as_posix().encode("utf-8") + b"\0")
            value.update(digest(path).encode("ascii") + b"\n")
    return value.hexdigest()


def transient_network_error(error):
    if isinstance(error, urllib.error.HTTPError):
        return error.code in {408, 425, 429, 500, 502, 503, 504}
    if isinstance(error, urllib.error.URLError):
        return transient_network_error(error.reason)
    if isinstance(error, (TimeoutError, ConnectionError)):
        return True
    if isinstance(error, socket.gaierror):
        return error.errno == socket.EAI_AGAIN
    return isinstance(error, OSError) and error.errno in {
        errno.ETIMEDOUT, errno.ECONNRESET, errno.ECONNREFUSED,
        errno.ECONNABORTED, errno.ENETDOWN, errno.ENETUNREACH, errno.EHOSTUNREACH,
    }


def download(url, destination):
    if not url.startswith("https://"):
        raise ValueError(f"Only HTTPS sources are allowed: {url}")
    partial = destination.with_name(destination.name + ".partial")
    for attempt in range(3):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "UnityBox-dependency-cache/1"})
            with urllib.request.urlopen(request, timeout=60) as response, partial.open("wb") as output:
                shutil.copyfileobj(response, output, 1024 * 1024)
            partial.replace(destination)
            return
        except (OSError, TimeoutError) as error:
            partial.unlink(missing_ok=True)
            if attempt == 2 or not transient_network_error(error):
                raise
            time.sleep(2)


def version_key(version):
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?", version)
    if not match:
        raise ValueError(f"Unsupported package version: {version}")
    prerelease = match[4]
    suffix = tuple((0, int(part)) if part.isdigit() else (1, part) for part in (prerelease or "").split("."))
    return tuple(map(int, match.group(1, 2, 3))) + (prerelease is None, suffix)


def minimum_version(requirement):
    requirement = requirement.strip()
    match = re.fullmatch(r"(?:>=)?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)(?:\s+<[^\s]+)?", requirement)
    if not match:
        raise ValueError(f"Unsupported dependency requirement: {requirement}")
    version_key(match[1])
    return match[1]


def satisfies(version, requirement):
    if version_key(version) < version_key(minimum_version(requirement)):
        return False
    upper = re.search(r"\s+<([^\s]+)$", requirement)
    return not upper or version_key(version) < version_key(upper[1])


class Fetcher:
    def __init__(self, cache, editor):
        self.cache = cache
        self.editor = editor
        self.package_manager = editor.parent / "Data/Resources/PackageManager"
        self.registry = {}
        self.resolved = {}
        cache.mkdir(parents=True, exist_ok=True)

    def builtin(self, name):
        return self.package_manager / "BuiltInPackages" / name

    def ensure_archive(self, name, entry):
        archive = self.cache / entry["archive"]
        if not archive.is_file():
            bundled = self.package_manager / "Editor" / entry["archive"]
            if bundled.is_file():
                shutil.copy2(bundled, archive)
            elif entry["source"] == "editor-bundled":
                raise ValueError(f"Required bundled Editor archive missing: {bundled}")
            else:
                print("Downloading", name, entry["version"], flush=True)
                download(entry["url"], archive)
        for algorithm, expected in (("sha1", entry.get("shasum")), ("sha256", entry.get("sha256"))):
            if expected and digest(archive, algorithm) != expected:
                raise ValueError(f"{algorithm} mismatch for {archive}; cached archive was not installed.")
        return archive

    def install(self, name, entry, update=False):
        target = self.cache / name
        if entry["source"] == "editor-builtin":
            source = self.builtin(name)
            if not (source / "package.json").is_file():
                raise ValueError(f"Required Editor builtin missing: {source}")
            actual = tree_digest(source)
            if entry.get("tree_sha256") and actual != entry["tree_sha256"]:
                raise ValueError(f"Editor builtin differs from the lock: {name}")
            entry["tree_sha256"] = actual
            archive = None
        else:
            archive = self.ensure_archive(name, entry)
            entry["sha256"] = digest(archive)
        if (target / "package.json").is_file() and entry.get("tree_sha256"):
            if tree_digest(target) == entry["tree_sha256"]:
                manifest = read_json(target / "package.json")
                self.check_manifest(name, entry, manifest, update)
                return manifest
            if not update:
                raise ValueError(f"Cached package contents changed: {target}; inspect/remove it before retrying.")
        with tempfile.TemporaryDirectory(prefix=".unitybox-fetch-", dir=self.cache) as temporary:
            unpacked = Path(temporary)
            if archive is None:
                source = self.builtin(name)
            elif archive.suffix == ".zip":
                with zipfile.ZipFile(archive) as zipped:
                    for member in zipped.infolist():
                        if not (unpacked / member.filename).resolve().is_relative_to(unpacked):
                            raise ValueError(f"Unsafe archive member: {member.filename}")
                    zipped.extractall(unpacked)
                source = unpacked
            else:
                with tarfile.open(archive) as tar:
                    tar.extractall(unpacked, filter="data")
                source = unpacked / "package"
            manifest = read_json(source / "package.json")
            self.check_manifest(name, entry, manifest, update)
            actual = tree_digest(source)
            if entry.get("tree_sha256") and actual != entry["tree_sha256"]:
                raise ValueError(f"Extracted package differs from lock: {name}")
            entry["tree_sha256"] = actual
            if target.exists():
                # Only the explicitly selected package cache entry is replaced.
                current = read_json(target / "package.json")
                if current["name"] != name:
                    raise ValueError(f"Refusing to replace unrelated cache directory: {target}")
                shutil.rmtree(target)
            shutil.copytree(source, target)
        print("Verified", name, entry["version"], flush=True)
        return manifest

    @staticmethod
    def check_manifest(name, entry, manifest, update):
        if (manifest["name"], manifest["version"]) != (name, entry["version"]):
            raise ValueError(f"Package identity mismatch for {name}")
        if not update:
            for field in ("dependencies", "vpmDependencies"):
                if manifest.get(field, {}) != entry.get(field, {}):
                    raise ValueError(f"Locked {field} differ from verified package manifest: {name}")

    def resolve(self, name, version):
        key = (name, version)
        if key in self.resolved:
            return self.resolved[key]
        entry = {"version": version}
        if name in RELEASES:
            pinned, repo, sha256 = RELEASES[name]
            if version != pinned:
                raise ValueError(f"Unexpected release version for {name}: {version}")
            entry.update(source="github-release", archive=f"{name}-{version}.zip", sha256=sha256,
                         url=f"https://github.com/{repo}/releases/download/{version}/{name}-{version}.zip")
        elif (self.builtin(name) / "package.json").is_file():
            entry.update(source="editor-builtin", editor_version=EDITOR_VERSION)
        elif (self.package_manager / "Editor" / f"{name}-{version}.tgz").is_file():
            # The installed Editor layer is pinned by install_editor.py. Core
            # dependencies can be verified without a registry/CDN request.
            bundled = self.package_manager / "Editor" / f"{name}-{version}.tgz"
            entry.update(source="editor-bundled", editor_version=EDITOR_VERSION,
                         archive=bundled.name, sha256=digest(bundled))
        elif name.startswith("com.unity."):
            if name not in self.registry:
                with tempfile.TemporaryDirectory(prefix=".unitybox-registry-", dir=self.cache) as temporary:
                    metadata = Path(temporary) / "registry.json"
                    download("https://packages.unity.com/" + name, metadata)
                    self.registry[name] = read_json(metadata)
            metadata = self.registry[name]["versions"][version]
            distribution = metadata["dist"]
            if not re.fullmatch(r"[0-9a-fA-F]{40}", distribution["shasum"]):
                raise ValueError(f"Missing/invalid official dist.shasum for {name}@{version}")
            entry.update(source="unity-registry", registry="https://packages.unity.com", archive=f"{name}-{version}.tgz",
                         url=distribution["tarball"], shasum=distribution["shasum"].lower())
        else:
            raise ValueError(f"No approved source for {name}@{version}")
        manifest = self.install(name, entry, update=True)
        entry["dependencies"] = manifest.get("dependencies", {})
        entry["vpmDependencies"] = manifest.get("vpmDependencies", {})
        self.resolved[key] = entry
        return entry

    def resolve_graph(self, roots):
        selected = dict(roots)
        while True:
            changed = False
            pending = list(roots)
            visited = set()
            modules = {}
            while pending:
                name = pending.pop(0)
                if name in visited:
                    continue
                visited.add(name)
                entry = self.resolve(name, selected[name])
                for child, requirement in {**entry["dependencies"], **entry["vpmDependencies"]}.items():
                    minimum = minimum_version(requirement)
                    if child.startswith("com.unity.modules."):
                        path = self.builtin(child) / "package.json"
                        metadata = read_json(path)
                        if not satisfies(metadata["version"], requirement):
                            raise ValueError(f"Editor module {child} does not meet {name}: {requirement}")
                        modules[child] = {"version": metadata["version"], "source": "editor-builtin", "manifest_sha256": digest(path)}
                        continue
                    current = selected.get(child)
                    if child in roots:
                        if not satisfies(current, requirement):
                            raise ValueError(f"Pinned {child}@{current} cannot satisfy {name}: {requirement}")
                    elif current is None or version_key(current) < version_key(minimum):
                        selected[child] = minimum
                        changed = True
                    elif not satisfies(current, requirement):
                        raise ValueError(f"Incompatible requirement {name} -> {child}: {requirement}")
                    pending.append(child)
            if not changed:
                return {name: self.resolve(name, selected[name]) for name in sorted(visited)}, modules


def profile_packages(lock, profile):
    if profile not in lock["profiles"]:
        raise ValueError(f"Profile {profile} is not prepared in this lock; explicitly use --update-lock --profile {profile}.")
    for name, version in lock["profiles"][profile].items():
        if lock["packages"][name]["version"] != version:
            raise ValueError(f"Locked root version differs from pin: {name}@{version}")
    pending = list(lock["profiles"][profile])
    selected = set()
    modules = set()
    while pending:
        name = pending.pop()
        if name in selected:
            continue
        entry = lock["packages"][name]
        selected.add(name)
        for child, requirement in {**entry["dependencies"], **entry["vpmDependencies"]}.items():
            if child.startswith("com.unity.modules."):
                if not satisfies(lock["modules"][child]["version"], requirement):
                    raise ValueError(f"Locked module {child} does not meet {name}: {requirement}")
                modules.add(child)
            else:
                if not satisfies(lock["packages"][child]["version"], requirement):
                    raise ValueError(f"Locked dependency {child} does not meet {name}: {requirement}")
                pending.append(child)
    return selected, modules


def validate_lock(lock, updating_profiles=()):
    if lock.get("format_version") != 1 or lock.get("editor_version") != EDITOR_VERSION:
        raise ValueError("Unsupported lock format or Editor version.")
    profiles = lock.get("profiles", {})
    if not profiles or not set(profiles).issubset({"core", "vrc"}):
        raise ValueError("Lock has no prepared profiles or contains unknown profiles.")
    if set(lock.get("prepared_profiles", profiles)) != set(profiles):
        raise ValueError("Prepared-profile list does not match the locked graphs.")
    for profile, roots in profiles.items():
        if profile not in updating_profiles and roots != (CORE_ROOTS if profile == "core" else VRC_ROOTS):
            raise ValueError(f"Lock {profile} root pins differ from this script; review the lock explicitly.")
    for name, entry in lock.get("packages", {}).items():
        if not re.fullmatch(r"[0-9a-f]{64}", entry.get("tree_sha256", "")):
            raise ValueError(f"Missing/invalid verified package tree hash: {name}")
        if entry.get("source") not in {"editor-builtin", "editor-bundled", "github-release", "unity-registry"}:
            raise ValueError(f"Unknown package source: {name}")
        if entry["source"] != "editor-builtin":
            if not re.fullmatch(r"[0-9a-f]{64}", entry.get("sha256", "")):
                raise ValueError(f"Missing/invalid verified archive hash: {name}")
            archive = entry.get("archive", "")
            if not archive or Path(archive).name != archive:
                raise ValueError(f"Unsafe archive name in lock: {name}")
        version_key(entry["version"])
    for name, entry in lock.get("modules", {}).items():
        if not re.fullmatch(r"[0-9a-f]{64}", entry.get("manifest_sha256", "")):
            raise ValueError(f"Missing/invalid Editor module hash: {name}")
    try:
        for profile in profiles:
            profile_packages(lock, profile)
    except KeyError as error:
        raise ValueError(f"Incomplete locked dependency graph: {error}") from error


def update_lock(fetcher, path, profile):
    if path.is_file():
        lock = read_json(path)
        # --update-lock explicitly permits changed root pins for this profile.
        # Still verify the old graph and hashes before replacing it atomically.
        validate_lock(lock, updating_profiles=("core", "vrc") if profile == "vrc" else ("core",))
        lock = copy.deepcopy(lock)
    else:
        lock = {"format_version": 1, "editor_version": EDITOR_VERSION,
                "profiles": {}, "packages": {}, "modules": {}}
    roots = CORE_ROOTS if profile == "core" else VRC_ROOTS
    # Keep the old lock intact until every package in this profile has actually
    # downloaded, unpacked, and passed identity/content verification.
    packages, modules = fetcher.resolve_graph(roots)
    if profile == "vrc":
        sdk_dependencies = packages["com.vrchat.base"]["dependencies"]
        if any(VRC_ROOTS.get(name) != version for name, version in sdk_dependencies.items()):
            raise ValueError("The SDK's declared Unity versions differ from the root pins.")
        lock["profiles"]["vrc"] = dict(VRC_ROOTS)
    lock["profiles"]["core"] = dict(CORE_ROOTS)
    lock["packages"].update(packages)
    lock["modules"].update(modules)
    lock["prepared_profiles"] = sorted(lock["profiles"])
    validate_lock(lock)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".json.partial")
    temporary.write_text(json.dumps(lock, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(path)
    print(f"Wrote verified {profile} dependency graph; prepared profiles: {', '.join(lock['prepared_profiles'])}", flush=True)
    return lock


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", choices=("core", "vrc"), default="core")
    parser.add_argument("--dependency-cache", "--cache", dest="cache", type=Path,
                        default=Path("/workspace/unitybox-setup/unity/dependencies"))
    parser.add_argument("--editor", type=Path, default=Path("/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity"))
    parser.add_argument("--lock", type=Path, default=Path(__file__).with_name("dependencies.lock.json"))
    parser.add_argument("--update-lock", action="store_true", help="Resolve and verify the selected profile; preserve other completed profiles.")
    args = parser.parse_args(argv)
    fetcher = Fetcher(args.cache.resolve(), args.editor.resolve())
    if args.update_lock:
        lock = update_lock(fetcher, args.lock, args.profile)
    else:
        lock = read_json(args.lock)
        validate_lock(lock)
    packages, modules = profile_packages(lock, args.profile)
    for name in sorted(packages):
        fetcher.install(name, lock["packages"][name])
    for name in sorted(modules):
        if digest(fetcher.builtin(name) / "package.json") != lock["modules"][name]["manifest_sha256"]:
            raise ValueError(f"Editor module changed: {name}")
    print(f"{args.profile}: verified {len(packages)} real packages and {len(modules)} Editor modules in {fetcher.cache}", flush=True)


if __name__ == "__main__":
    main()
