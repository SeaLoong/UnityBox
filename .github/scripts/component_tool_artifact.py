"""Validate and publish the standalone Unity Editor component-reference tool."""

import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import tarfile


SOURCE = "Assets/UnityBox/ComponentReferenceMigration/Editor"
FILENAME = "UnityBox-ComponentReferences.unitypackage"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def meta_guid(data):
    match = re.search(rb"(?m)^guid: ([0-9a-f]{32})\r?$", data)
    if not match:
        raise ValueError("Component tool asset metadata has no GUID")
    return match.group(1).decode()


def publish_component_tool(repo, output):
    repo, output = Path(repo), Path(output)
    source = repo / SOURCE
    if not source.is_dir():
        return None
    archive = repo / "Tools" / FILENAME
    expected = {}
    for path in source.rglob("*"):
        if path.is_file() and path.suffix != ".meta":
            metadata = Path(str(path) + ".meta").read_bytes()
            expected[path.relative_to(repo).as_posix()] = (path.read_bytes(), metadata)
    if not expected:
        raise ValueError("Component reference tool has no Editor source assets")
    entries = {}
    with tarfile.open(archive, "r:gz") as exported:
        for member in exported.getmembers():
            if not member.isfile():
                continue
            parts = PurePosixPath(member.name).parts
            if len(parts) != 2 or not re.fullmatch("[0-9a-f]{32}", parts[0]):
                raise ValueError("Unexpected component tool export member: " + member.name)
            files = entries.setdefault(parts[0], {})
            if parts[1] in files:
                raise ValueError("Duplicate component tool export member: " + member.name)
            files[parts[1]] = exported.extractfile(member).read()
    actual = set()
    allowed_folders = {p.relative_to(repo).as_posix() for p in source.rglob("*") if p.is_dir()}
    allowed_folders.update(str(p) for p in PurePosixPath(SOURCE).parents if str(p) != ".")
    allowed_folders.add(SOURCE)
    for guid, files in entries.items():
        name = files["pathname"].decode("utf-8").rstrip("\r\n")
        metadata = files["asset.meta"]
        if meta_guid(metadata) != guid:
            raise ValueError("Export path and metadata GUID disagree: " + name)
        if name in expected:
            content, original_meta = expected[name]
            if files.get("asset") != content or metadata != original_meta:
                raise ValueError("Component tool payload differs from current source: " + name)
            if name in actual:
                raise ValueError("Duplicate component tool asset: " + name)
            actual.add(name)
        elif name not in allowed_folders or not re.search(rb"(?m)^folderAsset: yes\r?$", metadata):
            raise ValueError("Unexpected asset in component tool export: " + name)
    if actual != set(expected):
        raise ValueError("Component tool export is missing current source assets: " + ", ".join(sorted(set(expected) - actual)))
    data = archive.read_bytes()
    report = {"filename": FILENAME, "sha256": digest(data), "bytes": len(data),
              "source_files": [{"path": name, "sha256": digest(content), "metadata_sha256": digest(metadata)}
                               for name, (content, metadata) in sorted(expected.items())]}
    output.mkdir(parents=True, exist_ok=True)
    (output / FILENAME).write_bytes(data)
    (output / "component-references.json").write_text(json.dumps(report, indent=2) + "\n")
    return report
