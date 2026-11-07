#!/usr/bin/env python3
"""Reproduce the real ASS 0.7.24 -> 0.8.0 -> fixed-package upgrade in an owned test project."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import wave
from zipfile import ZipFile

PACKAGE = "top.sealoong.unitybox.avatar-security-system"


def fingerprint(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def replace_package(project, archive):
    destination = project / "Packages" / PACKAGE
    with ZipFile(archive) as zipped:
        if zipped.testzip() is not None:
            raise ValueError("Archive CRC failed: " + str(archive))
        for entry in zipped.namelist():
            if Path(entry).is_absolute() or ".." in Path(entry).parts:
                raise ValueError("Invalid package archive path: " + entry)
        shutil.rmtree(destination)
        destination.mkdir()
        zipped.extractall(destination)
    return json.loads((destination / "package.json").read_text())["version"]


def run_phase(args, phase, method, archive):
    project = args.project.resolve()
    output = project / ".unitybox-real-upgrade"
    version = replace_package(project, archive)
    env = os.environ.copy()
    for kind in ("CONFIG", "DATA", "CACHE"):
        folder = args.xdg_root.resolve() / kind.lower()
        folder.mkdir(parents=True, exist_ok=True)
        env["XDG_" + kind + "_HOME"] = str(folder)
    command = [str(args.unity.resolve()), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "ASSRealZipUpgrade." + method, "-logFile", str(output / (phase + "-editor.log"))]
    record = {"phase": phase, "installed_package_version": version, "command": command,
              "input_archive_sha256": fingerprint(archive),
              "started_at_utc": datetime.now(timezone.utc).isoformat()}
    with (output / (phase + "-launcher.log")).open("w") as log:
        process = subprocess.Popen(command, cwd=project, env=env, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            record["exit_code"] = process.wait(timeout=args.timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt):
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            raise
    result = output / (phase + ".json")
    if result.is_file():
        record["result"] = json.loads(result.read_text())
    record["passed"] = record["exit_code"] == 0 and record.get("result", {}).get("passed") is True
    record["finished_at_utc"] = datetime.now(timezone.utc).isoformat()
    (output / (phase + "-run.json")).write_text(json.dumps(record, indent=2) + "\n")
    if not record["passed"]:
        raise RuntimeError("Upgrade phase failed; inspect " + str(output / (phase + "-run.json")))
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True, help="A disposable test_project.py-owned project with real SDK/MA/NDMF dependencies")
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--xdg-root", type=Path, required=True, help="This machine's legitimately activated writable XDG root")
    parser.add_argument("--legacy-zip", type=Path, required=True)
    parser.add_argument("--broken-zip", type=Path, required=True)
    parser.add_argument("--fixed-zip", type=Path, required=True)
    parser.add_argument("--timeout", type=float, default=1200)
    args = parser.parse_args()
    project = args.project.resolve()
    if not (project / ".unitybox-test-project.json").is_file():
        raise ValueError("Use a disposable project prepared by scripts/unity/test_project.py")
    output = project / ".unitybox-real-upgrade"
    if output.exists():
        raise ValueError("Use a fresh test project so previous evidence is preserved: " + str(output))
    output.mkdir()
    with ZipFile(args.legacy_zip) as zipped:
        if "Runtime/Component.cs.meta" in zipped.namelist():
            raise ValueError("The actual legacy archive must lack the configuration script meta")
    fixture = Path(__file__).with_name("ASSRealZipUpgrade.cs")
    target = project / "Assets/Editor/ASSRealZipUpgrade.cs"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(fixture, target)
    assets = project / "Assets/ASSUpgradeFixtures"
    assets.mkdir(parents=True, exist_ok=True)
    with wave.open(str(assets / "CustomWarning.wav"), "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(8000)
        audio.writeframes(b"\0\0" * 800)
    records = [run_phase(args, "legacy", "CreateLegacy", args.legacy_zip.resolve()),
               run_phase(args, "broken", "VerifyPublished080Breakage", args.broken_zip.resolve()),
               run_phase(args, "upgraded", "VerifyAutomaticUpgrade", args.fixed_zip.resolve())]
    summary = {"passed": True, "fixture_sha256": fingerprint(fixture), "phases": records}
    (output / "three-stage-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps({"passed": True, "output": str(output), "phases": len(records)}, indent=2))


if __name__ == "__main__":
    main()
