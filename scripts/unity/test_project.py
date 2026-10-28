#!/usr/bin/env python3
"""Prepare isolated Unity projects and run/report genuine EditMode tests.

No SDK substitutes, source edits, license activation, or online downloads are performed.
Core projects require a complete local dependency cache. VRC projects may resolve
uncached UPM dependencies through Unity unless --offline is supplied.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import types
import uuid
import xml.etree.ElementTree as ET

VERSION = "2022.3.22f1"
MARKER = ".unitybox-test-project.json"
DEFAULT_EDITOR = "/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity"
DEFAULT_CACHE = "/workspace/unitybox-setup/unity/dependencies"
BUILTINS = ("ai animation assetbundle audio cloth director imageconversion imgui jsonserialize "
            "particlesystem physics physics2d terrain terrainphysics ui uielements unitywebrequest "
            "unitywebrequestassetbundle unitywebrequestaudio unitywebrequesttexture video xr").split()
# The SDK's Oculus dependency uses Android.Permission even when importing in
# the Linux Editor. Unity's real module must be present in the VRC fixture.
VRC_BUILTINS = ("androidjni",)


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, data):
    Path(path).write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def load_filter(repo):
    path = repo / ".github/scripts/package_vpm.py"
    module = types.ModuleType("unitybox_package_filter")
    module.__file__ = str(path)
    # Do not write __pycache__ into the source checkout while preparing a copy.
    exec(compile(path.read_bytes(), str(path), "exec"), module.__dict__)
    return module.include_file


def copy_package(source, destination, include_file):
    name = read_json(source / "package.json")["name"]
    digest = hashlib.sha256()
    count = 0
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if not include_file(relative.as_posix(), name):
            continue
        if not path.resolve().is_relative_to(source.resolve()):
            raise ValueError(f"Package contains a file outside its directory: {path}")
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        digest.update(relative.as_posix().encode() + b"\0" + path.read_bytes() + b"\0")
        count += 1
    return {"files": count, "sha256": digest.hexdigest()}


def version_tuple(value):
    match = re.fullmatch(r"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?", value.strip())
    if not match:
        raise ValueError(f"Unsupported package version: {value}")
    prerelease = match[4]
    suffix = tuple((0, int(part)) if part.isdigit() else (1, part)
                   for part in (prerelease or "").split("."))
    return tuple(int(part or 0) for part in match.group(1, 2, 3)) + (prerelease is None, suffix)


def version_matches(version, expression):
    expression = expression.strip()
    if not expression:
        return True
    current = version_tuple(version)
    if expression[0] in "[(" and expression[-1] in "])":
        parts = expression[1:-1].split(",")
        if len(parts) == 1:
            return current == version_tuple(parts[0])
        lower, upper = (part.strip() for part in parts)
        return ((not lower or (current >= version_tuple(lower) if expression[0] == "[" else current > version_tuple(lower)))
                and (not upper or (current <= version_tuple(upper) if expression[-1] == "]" else current < version_tuple(upper))))
    # Unity's plain version expression means this version or newer.
    return current >= version_tuple(expression)


def dependency_matches(version, requirement):
    requirement = requirement.strip()
    if requirement.startswith(("[", "(")):
        return version_matches(version, requirement)
    current = version_tuple(version)
    position = 0
    pattern = r"\s*(>=|<=|>|<|=)?\s*(\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)"
    for match in re.finditer(pattern, requirement):
        if match.start() != position:
            raise ValueError(f"Unsupported dependency requirement: {requirement}")
        position = match.end()
        target = version_tuple(match[2])
        operator = match[1] or ">="  # UPM package dependencies express minimum versions.
        if not {">=": current >= target, "<=": current <= target, ">": current > target,
                "<": current < target, "=": current == target}[operator]:
            return False
    if not position or requirement[position:].strip():
        raise ValueError(f"Unsupported dependency requirement: {requirement}")
    return True


def enabled_test_assemblies(packages, versions):
    assemblies = []
    for package in packages:
        for path in sorted(package.rglob("*.asmdef")):
            data = read_json(path)
            if "TestAssemblies" not in data.get("optionalUnityReferences", []):
                continue
            platforms = data.get("includePlatforms", [])
            if platforms and "Editor" not in platforms:
                continue
            if "Editor" in data.get("excludePlatforms", []):
                continue
            symbols = {"UNITY_INCLUDE_TESTS", "UNITY_EDITOR", "UNITY_EDITOR_LINUX"}
            for define in data.get("versionDefines", []):
                version = versions.get(define["name"])
                if version and version_matches(version, define.get("expression", "")):
                    symbols.add(define["define"])
            def satisfied(condition):
                return any((part.strip()[1:] not in symbols if part.strip().startswith("!")
                            else part.strip() in symbols) for part in condition.split("||"))
            if all(satisfied(condition) for condition in data.get("defineConstraints", [])):
                assemblies.append(data["name"])
    return sorted(assemblies)


def prepare(args):
    repo, project, cache = args.repo.resolve(), args.project.resolve(), args.dependency_cache.resolve()
    if project == repo or project.is_relative_to(repo) or repo.is_relative_to(project):
        raise ValueError("The isolated project must be outside, and must not contain, the source repository.")
    if project.exists() and any(project.iterdir()):
        if not (project / MARKER).is_file() or read_json(project / MARKER).get("tool") != "UnityBox.test_project":
            raise ValueError("Refusing to reuse a nonempty directory without the UnityBox ownership marker.")
    include_file = load_filter(repo)
    available = {}
    for manifest in sorted((repo / "Assets/UnityBox").glob("*/package.json")):
        data = read_json(manifest)
        available[data["name"]] = manifest.parent
    aliases = {path.name: name for name, path in available.items()}
    selected = [aliases.get(name, name) for name in args.package] if args.package else list(available)
    if not selected or any(name not in available for name in selected):
        raise ValueError(f"Unknown or empty package selection: {selected}; available: {list(available)}")
    selected = sorted(set(selected))
    sources = {}
    registry = {}
    ignored_vpm = {}
    requirements = []
    pending = [(name, available[name], "local") for name in selected]
    required = {"com.unity.test-framework": "[1.1.33]", "com.unity.ext.nunit": "[1.0.6]"}
    if args.profile == "vrc":
        required.update({"com.vrchat.base": ">=3.7.0", "com.vrchat.avatars": ">=3.7.0",
                         "com.unity.ugui": "[1.0.0]"})
    for name in args.dependency:
        required.setdefault(name, None)
    for name, expected in required.items():
        source = cache / name
        if not (source / "package.json").is_file():
            raise ValueError(f"Missing real dependency in cache: {source}")
        data = read_json(source / "package.json")
        if data["name"] != name or (expected and not dependency_matches(data["version"], expected)):
            raise ValueError(f"Unexpected cached package identity/version: {source}")
        pending.append((name, source, "cache"))
    while pending:
        name, source, origin = pending.pop(0)
        if name in sources:
            continue
        if args.profile == "core" and name.startswith("com.vrchat."):
            raise ValueError(f"Core profile cannot include SDK dependency {name}; use --profile vrc.")
        data = read_json(source / "package.json")
        if data["name"] != name:
            raise ValueError(f"Cached package name mismatch: {source}")
        sources[name] = (source, data, origin)
        for kind in ("dependencies", "vpmDependencies"):
            if kind == "vpmDependencies" and args.profile == "core" and origin == "local":
                if data.get(kind):
                    ignored_vpm[name] = data[kind]
                continue
            for dependency, version in data.get(kind, {}).items():
                requirements.append((name, dependency, version))
                if dependency.startswith("com.unity.modules."):
                    registry[dependency] = version
                elif dependency in available:
                    pending.append((dependency, available[dependency], "local"))
                elif (cache / dependency / "package.json").is_file():
                    pending.append((dependency, cache / dependency, "cache"))
                elif args.profile == "core" or args.offline or kind == "vpmDependencies":
                    raise ValueError(f"Missing dependency {dependency} ({version}) required by {name}; populate --dependency-cache.")
                else:
                    registry[dependency] = version
    for parent, dependency, requirement in requirements:
        if dependency in sources:
            actual = sources[dependency][1]["version"]
            if not dependency_matches(actual, requirement):
                raise ValueError(f"Cached {dependency}@{actual} does not satisfy {parent}: {requirement}")
    # Only mutate an owned destination after all package/dependency checks succeed.
    project.mkdir(parents=True, exist_ok=True)
    write_json(project / MARKER, {"tool": "UnityBox.test_project", "preparing": True})
    packages = project / "Packages"
    if packages.exists():
        shutil.rmtree(packages)
    packages.mkdir()
    (project / "Assets").mkdir(exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    copies = {}
    versions = {}
    for name, (source, data, origin) in sorted(sources.items()):
        package_filter = include_file if origin == "local" else lambda relative, package_name: True
        copies[name] = {**copy_package(source, packages / name, package_filter),
                        "version": data["version"], "source": str(source), "origin": origin}
        versions[name] = data["version"]
    dependencies = {"com.unity.modules." + name: "1.0.0" for name in BUILTINS}
    if args.profile == "vrc":
        dependencies.update({"com.unity.modules." + name: "1.0.0" for name in VRC_BUILTINS})
    dependencies.update(registry)
    dependencies.update({name: "file:" + name for name in sources})
    local_names = sorted(name for name, (_, _, origin) in sources.items() if origin == "local")
    write_json(packages / "manifest.json", {"dependencies": dependencies, "testables": local_names})
    (project / "ProjectSettings/ProjectVersion.txt").write_text(f"m_EditorVersion: {VERSION}\n", encoding="utf-8")
    revision = subprocess.run(["git", "-C", str(repo), "rev-parse", "HEAD"], capture_output=True, text=True)
    metadata = {"tool": "UnityBox.test_project", "schema": 1, "project_id": uuid.uuid4().hex, "prepared_at": utc_now(),
                "repo": str(repo), "revision": revision.stdout.strip() if revision.returncode == 0 else None,
                "profile": args.profile, "packages": copies, "selected_packages": selected,
                "ignored_vpm_dependencies": ignored_vpm,
                "registry_dependencies": {name: version for name, version in registry.items()
                                          if not name.startswith("com.unity.modules.") and name not in sources},
                "test_assemblies": enabled_test_assemblies([packages / name for name in local_names], versions)}
    write_json(project / MARKER, metadata)
    print(json.dumps({"project": str(project), **metadata}, indent=2, ensure_ascii=False))
    return 0


def parse_results(path):
    root = ET.parse(path).getroot()
    cases = list(root.iter("test-case"))
    counts = {"passed": 0, "failed": 0, "skipped": 0, "inconclusive": 0, "unknown": 0}
    failures, skipped = [], []
    mapping = {"Passed": "passed", "Success": "passed", "Failed": "failed", "Failure": "failed",
               "Error": "failed", "Skipped": "skipped", "Ignored": "skipped", "NotRunnable": "failed",
               "Inconclusive": "inconclusive"}
    for case in cases:
        outcome = mapping.get(case.get("result"), "unknown")
        counts[outcome] += 1
        name = case.get("fullname") or case.get("name")
        if outcome in {"failed", "unknown", "inconclusive"}:
            failures.append({"test": name, "result": case.get("result"),
                             "message": case.findtext("failure/message") or case.findtext("reason/message") or ""})
        elif outcome == "skipped":
            skipped.append({"test": name, "reason": case.findtext("reason/message") or ""})
    assembly_counts = {}
    for suite in root.iter("test-suite"):
        if suite.get("type") != "Assembly":
            continue
        name = (suite.get("name") or suite.get("fullname", "")).removesuffix(".dll")
        outcomes = [mapping.get(case.get("result"), "unknown") for case in suite.iter("test-case")]
        assembly_counts[name] = {"total": len(outcomes), **{key: outcomes.count(key) for key in counts}}
    assemblies = sorted(assembly_counts)
    return {"total": len(cases), **counts, "result": root.get("result"), "assemblies": assemblies,
            "assembly_counts": assembly_counts, "failures": failures, "skipped_tests": skipped}


def evaluate_run(run, directory):
    reasons = []
    results = None
    xml = directory / "results.xml"
    if run.get("exit_code") != 0:
        reasons.append(f"Unity exit code: {run.get('exit_code')}")
    if run.get("timed_out"):
        reasons.append("Unity timed out; its process group was terminated")
    if run.get("launch_error"):
        reasons.append(run["launch_error"])
    text = "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in
                     (directory / "unity.log", directory / "launcher.log") if path.is_file())
    blockers = [line.strip() for line in text.splitlines() if re.search(
        r"No valid Unity (?:Editor )?license|No valid license|License is not active|Failed to activate.*license|"
        r"error CS\d+|Scripts have compiler errors|Compilation failed", line, re.IGNORECASE)]
    if blockers:
        reasons.append("Unity log contains licensing or compilation errors")
    if not xml.is_file():
        reasons.append("No fresh NUnit XML was produced")
    elif xml.stat().st_mtime < run["started_epoch"] - 2:
        reasons.append("NUnit XML predates this run")
    else:
        try:
            results = parse_results(xml)
            if results["total"] == 0 or results["passed"] + results["failed"] + results["inconclusive"] == 0:
                reasons.append("No tests executed")
            if results["failed"] or results["inconclusive"] or results["unknown"]:
                reasons.append("Tests failed, were inconclusive, or had unknown outcomes")
            if results["result"] not in {"Passed", "Success"}:
                reasons.append(f"NUnit overall result: {results['result']}")
            missing = sorted(set(run.get("expected_assemblies", [])) - set(results["assemblies"]))
            if missing:
                reasons.append("Missing expected test assemblies: " + ", ".join(missing))
            for name in run.get("expected_assemblies", []):
                counts = results["assembly_counts"].get(name)
                if counts and counts["passed"] + counts["failed"] + counts["inconclusive"] == 0:
                    reasons.append("No tests executed in expected assembly: " + name)
        except (ET.ParseError, ValueError) as exc:
            reasons.append(f"Invalid NUnit XML: {exc}")
    return {"passed": not reasons, "reasons": reasons, "tests": results, "log_errors": blockers[:50],
            "run": run, "results_xml": str(xml),
            "results_sha256": hashlib.sha256(xml.read_bytes()).hexdigest() if xml.is_file() else None}


def stop_process_group(process):
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass
    # A child can ignore SIGTERM even after its parent exits.
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    if process.poll() is None:
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            raise RuntimeError("Timed-out Unity process did not exit after SIGKILL")


def run_tests(args):
    project = args.project.resolve()
    metadata = read_json(project / MARKER)
    selected_assemblies = ([name.strip() for name in args.assembly_names.split(";") if name.strip()]
                           if args.assembly_names else metadata["test_assemblies"])
    if not selected_assemblies:
        raise ValueError("No enabled repository test assemblies were selected; refusing an unrestricted Unity run")
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
    directory = project / ".unitybox-test-results" / run_id
    directory.mkdir(parents=True)
    xdg_root = (args.xdg_root or project / ".unitybox-xdg").resolve()
    env = os.environ.copy()
    for kind in ("CONFIG", "DATA", "CACHE"):
        folder = xdg_root / kind.lower()
        folder.mkdir(parents=True, exist_ok=True)
        env["XDG_" + kind + "_HOME"] = str(folder)
    command = [str(args.unity.resolve()), "-batchmode", "-nographics", "-projectPath", str(project),
               "-runTests", "-testPlatform", "EditMode", "-testResults", str(directory / "results.xml"),
               "-logFile", str(directory / "unity.log")]
    if args.test_filter:
        command.extend(["-testFilter", args.test_filter])
    assemblies = (selected_assemblies if args.assembly_names
                  else [] if args.test_filter else metadata["test_assemblies"])
    # Unity can discover test methods in ordinary SDK Editor assemblies even
    # when they are absent from manifest.testables. Always restrict the actual
    # invocation to this checkout's selected suites, including filtered runs.
    command.extend(["-assemblyNames", ";".join(selected_assemblies)])
    run = {"run_id": run_id, "project_id": metadata["project_id"], "command": command,
           "profile": metadata["profile"], "ignored_vpm_dependencies": metadata.get("ignored_vpm_dependencies", {}),
           "package_versions": {name: package["version"] for name, package in metadata["packages"].items()},
           "started_at": utc_now(), "started_epoch": time.time(), "timeout_seconds": args.timeout,
           "expected_assemblies": assemblies, "timed_out": False, "exit_code": None,
           "selected_assemblies": selected_assemblies,
           "xdg_root": str(xdg_root), "test_filter": args.test_filter}
    process = None
    try:
        with (directory / "launcher.log").open("w", encoding="utf-8") as stream:
            process = subprocess.Popen(command, cwd=project, env=env, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                run["exit_code"] = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                run["timed_out"] = True
                stop_process_group(process)
                run["exit_code"] = process.returncode
    except OSError as exc:
        run["launch_error"] = str(exc)
    except KeyboardInterrupt:
        if process:
            stop_process_group(process)
            run["exit_code"] = process.returncode
        run["launch_error"] = "Interrupted by operator"
    finally:
        run["finished_at"] = utc_now()
        write_json(directory / "run.json", run)
        write_json(project / ".unitybox-test-results/latest.json", {"run_id": run_id})
    summary = evaluate_run(run, directory)
    write_json(directory / "report.json", summary)
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    return 0 if summary["passed"] else 1


def report(args):
    project = args.project.resolve()
    metadata = read_json(project / MARKER)
    results = project / ".unitybox-test-results"
    run_id = args.run_id or read_json(results / "latest.json")["run_id"]
    if not re.fullmatch(r"\d{8}T\d{6}-[0-9a-f]{8}", run_id):
        raise ValueError("Invalid run ID")
    directory = results / run_id
    run = read_json(directory / "run.json")
    summary = evaluate_run(run, directory)
    if run["project_id"] != metadata["project_id"]:
        summary["passed"] = False
        summary["reasons"].append("Results belong to an earlier prepared project")
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    return 0 if summary["passed"] else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare", help="Copy selected packages into an isolated project")
    prep.add_argument("--repo", type=Path, required=True)
    prep.add_argument("--project", type=Path, required=True)
    prep.add_argument("--package", action="append", default=[], help="Package name or Assets/UnityBox directory name; repeatable")
    prep.add_argument("--profile", choices=("core", "vrc"), default="core")
    prep.add_argument("--dependency-cache", type=Path, default=Path(DEFAULT_CACHE))
    prep.add_argument("--dependency", action="append", default=[], help="Additional real cached package, e.g. nadena.dev.ndmf")
    prep.add_argument("--offline", action="store_true", help="Also require all VRC UPM dependencies in cache")
    prep.set_defaults(action=prepare)
    run = commands.add_parser("run", help="Run actual Unity tests and fail closed on incomplete evidence")
    run.add_argument("--project", type=Path, required=True)
    run.add_argument("--unity", type=Path, default=Path(DEFAULT_EDITOR))
    run.add_argument("--timeout", type=float, default=1200)
    run.add_argument("--xdg-root", type=Path)
    run.add_argument("--test-filter")
    run.add_argument("--assembly-names", help="Semicolon-separated test assembly names")
    run.set_defaults(action=run_tests)
    rep = commands.add_parser("report", help="Re-evaluate recorded exit, log, and NUnit XML evidence")
    rep.add_argument("--project", type=Path, required=True)
    rep.add_argument("--run-id")
    rep.set_defaults(action=report)
    args = parser.parse_args(argv)
    if args.command == "run" and args.timeout <= 0:
        parser.error("--timeout must be positive")
    try:
        return args.action(args)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
