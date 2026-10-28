#!/usr/bin/env python3
"""Type-check asmdefs using the real Unity Editor Roslyn compiler and references.

This does not start Unity, import assets, run IL postprocessors, or execute tests.
It is an additional diagnostic when Editor execution is blocked, never a test pass.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import types


def version_tuple(value):
    match = re.fullmatch(r"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?", value)
    if not match:
        raise ValueError("Unsupported package version: " + value)
    numbers = tuple(int(part or 0) for part in match.groups()[:3])
    prerelease = match[4]
    identifiers = tuple((0, int(part)) if part.isdigit() else (1, part) for part in prerelease.split(".")) if prerelease else ()
    return numbers + (0 if prerelease else 1, identifiers)


def version_matches(version, expression):
    if not expression:
        return True
    actual = version_tuple(version)
    if expression[0] in "[()":
        parts = expression[1:-1].split(",")
        if len(parts) == 1:
            return actual == version_tuple(parts[0])
        low, high = (part.strip() for part in parts)
        return (not low or (actual >= version_tuple(low) if expression[0] == "[" else actual > version_tuple(low))) and (not high or (actual <= version_tuple(high) if expression[-1] == "]" else actual < version_tuple(high)))
    return actual >= version_tuple(expression)


def allowed(constraints, symbols):
    return all(any((term.strip()[1:] not in symbols) if term.strip().startswith("!") else term.strip() in symbols for term in condition.split("||")) for condition in constraints)


def is_managed(path):
    """Exclude native DLLs from compiler references without loading their code."""
    try:
        with path.open("rb") as stream:
            if stream.read(2) != b"MZ":
                return False
            stream.seek(0x3C)
            offset = struct.unpack("<I", stream.read(4))[0]
            stream.seek(offset)
            if stream.read(4) != b"PE\0\0":
                return False
            optional_header = offset + 24
            stream.seek(optional_header)
            magic = struct.unpack("<H", stream.read(2))[0]
            directory_offset = {0x10B: 96, 0x20B: 112}.get(magic)
            if directory_offset is None:
                return False
            stream.seek(optional_header + directory_offset + 14 * 8)
            address, size = struct.unpack("<II", stream.read(8))
            return address != 0 and size != 0
    except (OSError, struct.error):
        return False


def automatic_editor_reference(path):
    meta = path.with_suffix(path.suffix + ".meta")
    if not meta.exists():
        return True
    content = meta.read_text()
    if re.search(r"isExplicitlyReferenced:\s*1", content) or re.search(r"Exclude Editor:\s*1", content):
        return False
    entries = re.findall(r"- first:\s*\n\s+([^\n]+)\n\s+second:\s*\n\s+enabled:\s*(\d)", content)
    relevant = [(label, value) for label, value in entries if "Editor" in label or "Any" in label]
    return not relevant or any(value == "1" for label, value in relevant)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--editor", type=Path, required=True)
    parser.add_argument("--dependency-cache", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--package", action="append", default=[])
    parser.add_argument("--profile", choices=["core", "vrc"], default="core")
    parser.add_argument("--tests", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    data = args.editor.resolve().parent / "Data"
    filter_path = args.repo / ".github/scripts/package_vpm.py"
    packaging = types.ModuleType("package_vpm")
    exec(compile(filter_path.read_bytes(), str(filter_path), "exec"), packaging.__dict__)
    roots = []
    versions = {}
    for manifest in sorted((args.repo / "Assets/UnityBox").glob("*/package.json")):
        package = json.loads(manifest.read_text())
        if args.package and manifest.parent.name not in args.package and package["name"] not in args.package:
            continue
        roots.append((manifest.parent, package["name"], True))
        versions[package["name"]] = package["version"]
    if not roots:
        raise ValueError("No repository packages selected")
    for manifest in sorted(args.dependency_cache.glob("*/package.json")):
        package = json.loads(manifest.read_text())
        if args.profile == "core" and package["name"] not in {"com.unity.test-framework", "com.unity.ext.nunit"}:
            continue
        roots.append((manifest.parent, package["name"], False))
        versions[package["name"]] = package["version"]
    base_symbols = {"UNITY_EDITOR", "UNITY_EDITOR_LINUX", "UNITY_STANDALONE", "UNITY_STANDALONE_LINUX", "UNITY_64", "NET_STANDARD_2_1", "NET_STANDARD", "ENABLE_MONO", "ENABLE_CLOUD_SERVICES", "ENABLE_AUDIO", "ENABLE_PHYSICS", "ENABLE_PHYSICS2D", "ENABLE_ANIMATION", "UNITY_2022_3", "UNITY_2022_3_OR_NEWER"}
    base_symbols.update(f"UNITY_{year}_{minor}_OR_NEWER" for year in range(2017, 2023) for minor in range(1, 5) if year < 2022 or minor <= 3)
    if args.tests:
        base_symbols.add("UNITY_INCLUDE_TESTS")
    if args.profile == "vrc":
        base_symbols.update({"VRC_SDK_VRCSDK3", "VRC_SDK_VRCSDK3_AVATARS", "VRC_SDK_BASE"})
    assemblies = {}
    excluded = []
    guid_names = {}
    precompiled = {}
    automatic_dlls = []
    for root, package_name, own in roots:
        for dll in root.rglob("*.dll"):
            if any(part.endswith("~") or part.startswith(".") for part in dll.relative_to(root).parts):
                continue
            if not is_managed(dll):
                continue
            precompiled.setdefault(dll.name, dll)
            if automatic_editor_reference(dll):
                automatic_dlls.append(dll)
        for path in root.rglob("*.asmdef"):
            relative = path.relative_to(root)
            if any(part.endswith("~") or part.startswith(".") for part in relative.parts):
                continue
            definition = json.loads(path.read_text(encoding="utf-8-sig"))
            name = definition["name"]
            meta = path.with_suffix(path.suffix + ".meta")
            if meta.exists():
                match = re.search(r"^guid: (\w+)", meta.read_text(), re.M)
                if match:
                    guid_names["GUID:" + match[1]] = name
            symbols = set(base_symbols)
            for rule in definition.get("versionDefines", []):
                if rule["name"] in versions and version_matches(versions[rule["name"]], rule["expression"]):
                    symbols.add(rule["define"])
            included = definition.get("includePlatforms", [])
            if (included and "Editor" not in included) or "Editor" in definition.get("excludePlatforms", []) or not allowed(definition.get("defineConstraints", []), symbols):
                excluded.append(name)
                continue
            if not args.tests and ("TestAssemblies" in definition.get("optionalUnityReferences", []) or "test-framework" in package_name):
                excluded.append(name)
                continue
            sources = []
            for source in path.parent.rglob("*.cs"):
                if any(part.endswith("~") or part.startswith(".") for part in source.relative_to(root).parts):
                    continue
                if own and not packaging.include_file(source.relative_to(root).as_posix(), package_name):
                    continue
                parent = source.parent
                while parent != path.parent and not list(parent.glob("*.asmdef")) and not list(parent.glob("*.asmref")):
                    parent = parent.parent
                if parent == path.parent:
                    sources.append(source)
            assemblies[name] = {"path": path, "definition": definition, "symbols": symbols, "sources": sources, "own": own}
    system = list((data / "NetStandard/ref/2.1.0").glob("*.dll"))
    system += list((data / "NetStandard/compat/2.1.0/shims/netfx").glob("*.dll"))
    system += list((data / "NetStandard/compat/2.1.0/shims/netstandard").glob("*.dll"))
    engine = {path.name: path for path in (data / "Managed").glob("Unity*.dll") if path.name not in {"UnityEngine.dll", "UnityEditor.dll"}}
    for path in (data / "Managed/UnityEngine").glob("*.dll"):
        engine.setdefault(path.name, path)
    for path in (data / "Managed").glob("*.dll"):
        precompiled.setdefault(path.name, path)
    # Unity's package asmdefs use the historical Mono.Cecil names for these shipped assemblies.
    for suffix in ("", ".Pdb", ".Mdb", ".Rocks"):
        precompiled.setdefault("Mono.Cecil" + suffix + ".dll", data / ("Managed/Unity.Cecil" + suffix + ".dll"))
    completed = {}
    failed = []
    def references_for(name):
        definition = assemblies[name]["definition"]
        references = [guid_names.get(reference, reference) for reference in definition.get("references", [])]
        if "TestAssemblies" in definition.get("optionalUnityReferences", []):
            references += ["UnityEngine.TestRunner", "UnityEditor.TestRunner"]
        # Unity 2022.3 TestRunnerHelpers adds available test runners to Editor-only
        # assemblies, not just assemblies marked TestAssemblies. Mirror that rule.
        runners = {"UnityEngine.TestRunner", "UnityEditor.TestRunner"}
        if "Editor" in definition.get("includePlatforms", []) and name not in runners and not runners.intersection(references):
            references += sorted(runners.intersection(assemblies))
        # UGUI is a built-in UnityEngine assembly, supplied as a source package in this Editor.
        if "UnityEngine.UI" in assemblies and name != "UnityEngine.UI":
            references.append("UnityEngine.UI")
        return list(dict.fromkeys(references))

    required = {name for name, assembly in assemblies.items() if assembly["own"]}
    if not required:
        raise ValueError("No repository assemblies enabled for this profile; this is not a successful compilation.")
    queued = list(required)
    while queued:
        for reference in references_for(queued.pop()):
            if reference in assemblies and reference not in required:
                required.add(reference)
                queued.append(reference)
    pending = {name: assembly for name, assembly in assemblies.items() if name in required}
    diagnostics = []
    while pending:
        progress = False
        for name, assembly in list(pending.items()):
            definition = assembly["definition"]
            references = references_for(name)
            if any(reference in pending for reference in references):
                continue
            progress = True
            del pending[name]
            missing = [reference for reference in references if reference not in completed]
            # Some upstream asmdefs retain optional names when their package is absent.
            # Let real C# type resolution verify those sources, while recording the omission.
            omitted = [reference for reference in missing if not assembly["own"] and reference not in assemblies]
            references = [reference for reference in references if reference not in omitted]
            missing = [reference for reference in missing if reference not in omitted]
            dll_names = list(definition.get("precompiledReferences", []))
            if "TestAssemblies" in definition.get("optionalUnityReferences", []):
                dll_names.append("nunit.framework.dll")
            if "Editor" in definition.get("includePlatforms", []) and "nunit.framework.dll" in precompiled:
                dll_names.append("nunit.framework.dll")
            missing += [dll for dll in dll_names if dll not in precompiled or not precompiled[dll].is_file()]
            if missing:
                failed.append(name)
                diagnostics.append({"assembly": name, "missing_references": missing})
                print("BLOCKED", name, ", ".join(missing), flush=True)
                continue
            output = args.output / (name + ".dll")
            refs = system + list(engine.values()) + [completed[reference] for reference in references]
            refs += [precompiled[dll] for dll in dll_names]
            if not definition.get("overrideReferences", False):
                refs += automatic_dlls
            response = args.output / (name + ".rsp")
            options = ["-nologo", "-target:library", "-langversion:9.0", "-nostdlib+", "-deterministic+", "-out:" + str(output), "-define:" + ";".join(sorted(assembly["symbols"]))]
            if definition.get("allowUnsafeCode"):
                options.append("-unsafe+")
            options += ['-r:"' + str(path) + '"' for path in dict.fromkeys(refs)]
            options += ['"' + str(path) + '"' for path in assembly["sources"]]
            response.write_text("\n".join(options) + "\n")
            result = subprocess.run([str(data / "NetCoreRuntime/dotnet"), str(data / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            (args.output / (name + ".log")).write_text(result.stdout)
            diagnostics.append({"assembly": name, "exit_code": result.returncode, "sources": len(assembly["sources"]), "omitted_upstream_references": omitted, "source_sha256": hashlib.sha256(b"".join(path.read_bytes() for path in sorted(assembly["sources"]))).hexdigest()})
            if result.returncode:
                failed.append(name)
                print("FAIL", name, "\n" + result.stdout[-10000:], flush=True)
            else:
                completed[name] = output
                print("COMPILED", name, "sources=" + str(len(assembly["sources"])), flush=True)
        if not progress:
            failed.extend(pending)
            diagnostics.append({"unresolved_cycle": list(pending)})
            break
    report = {"scope": "Roslyn type checking only; not Unity import, build, or tests", "profile": args.profile, "compiled": list(completed), "failed": failed, "excluded_by_constraints": excluded, "diagnostics": diagnostics}
    (args.output / "compile-report.json").write_text(json.dumps(report, indent=2) + "\n")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
