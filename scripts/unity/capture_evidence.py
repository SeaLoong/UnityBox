#!/usr/bin/env python3
"""Capture checked-in Unity verification and performance evidence from genuine runs."""
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile

import test_project
import verify_player


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def fingerprint(files):
    return hashlib.sha256(json.dumps(sorted(files), separators=(",", ":")).encode()).hexdigest()


def command_project(command):
    return Path(command[command.index("-projectPath") + 1]).resolve()


def artifact(path):
    path = Path(path)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": sha256(path)}


def package_files(root, include):
    """Keep the executable/package snapshot stable across subsequent Markdown edits."""
    name = test_project.read_json(root / "package.json")["name"]
    return {p.relative_to(root).as_posix(): p for p in root.rglob("*")
            if p.is_file() and not p.name.lower().endswith((".md", ".md.meta"))
            and include(p.relative_to(root).as_posix(), name)}


def extra_source_files(root, include):
    return {relative: path for relative, path in test_project.extra_source_files(root, include).items()
            if not path.name.lower().endswith((".md", ".md.meta"))}


def checkout_info(source):
    checkout = next((parent for parent in (source, *source.parents)
                     if (parent / ".git").is_file() or (parent / ".git/HEAD").is_file()), None)
    if checkout is None:
        return {}
    def git(*args):
        return subprocess.check_output(["git", "-C", str(checkout), *args], text=True).strip()
    return {"checkout": str(checkout), "branch": git("branch", "--show-current"),
            "checkout_base_revision": git("rev-parse", "HEAD")}


def folder_metadata_format(data):
    if not re.search(rb"(?m)^folderAsset: yes\r?$", data):
        return data
    return re.sub(rb"(?m)^([ \t]*(?:userData|assetBundleName|assetBundleVariant):)[ \t]+(?=\r?$)",
                  rb"\1", data)


def source_evidence(project, prepared):
    include = test_project.load_filter(Path(prepared["repo"]))
    imported = {}
    for directory in (project / "Library/ScriptAssemblies", project / ".unitybox-native-assemblies"):
        for path in directory.glob("*.dll"):
            imported.setdefault(path.stem, path)
    require(imported, f"No retained imported assemblies: {project}")
    packages, extras, assemblies = {}, {}, {}
    sources = [("package", name, info, project / "Packages" / name)
               for name, info in sorted(prepared["packages"].items()) if info["origin"] == "local"]
    sources += [("extra_source", name, info, project / info["destination"])
                for name, info in sorted(prepared.get("extra_sources", {}).items())]
    for kind, name, info, copied in sources:
        source = Path(info["source"]).resolve()
        read_files = package_files if kind == "package" else extra_source_files
        current_files = read_files(source, include)
        copied_files = read_files(copied, include)
        added = current_files.keys() - copied_files.keys()
        removed = copied_files.keys() - current_files.keys()
        # Unity generates metadata for legacy source files that have no .meta.
        # Preserve and compare every supplied .meta; record importer additions.
        importer_metadata = sorted(name for name in removed if name.endswith(".meta"))
        removed = removed - set(importer_metadata)
        require(not added and not removed,
                f"Source file selection changed since preparation: {name}; "
                f"added={sorted(added)}, removed={sorted(removed)}")
        files, current_snapshot, formatting = [], [], []
        definitions = {}
        for relative, current in sorted(current_files.items()):
            path = copied_files[relative]
            original, latest = path.read_bytes(), current.read_bytes()
            if original != latest:
                require(path.suffix == ".meta" and folder_metadata_format(original) == folder_metadata_format(latest),
                        f"Source changed since preparation: {source / relative}")
                formatting.append({"path": relative, "imported_sha256": sha256(path),
                                   "current_sha256": sha256(current),
                                   "canonical_sha256": hashlib.sha256(folder_metadata_format(original)).hexdigest()})
            files.append((relative, sha256(path)))
            current_snapshot.append((relative, sha256(current)))
            if path.suffix == ".asmdef":
                assembly = test_project.read_json(path)["name"]
                definitions[path.parent] = assembly
                if assembly in imported:
                    require(assembly not in assemblies, f"Multiple source roots claim imported assembly: {assembly}")
                    assemblies[assembly] = {kind: name, "source_files": [],
                                            "dll": artifact(imported[assembly])}
        for relative in sorted(current_files):
            path = copied_files[relative]
            if path.suffix not in {".cs", ".asmdef", ".asmref"}:
                continue
            owner = next((definitions[p] for p in (path.parent, *path.parent.parents)
                          if p in definitions), None)
            if owner in assemblies:
                assemblies[owner]["source_files"].append((relative, sha256(path)))
        snapshot = {"source": str(source), **checkout_info(source),
                    "prepared_tree_sha256": info["sha256"],
                    "source_snapshot_sha256": fingerprint(current_snapshot),
                    "imported_snapshot_sha256": fingerprint(files), "file_count": len(files),
                    "metadata_formatting_only": formatting,
                    "importer_generated_metadata": importer_metadata}
        if kind == "package":
            packages[name] = {"version": info["version"], **snapshot}
        else:
            extras[name] = {"destination": info["destination"], **snapshot}
    for name, assembly in assemblies.items():
        files = assembly.pop("source_files")
        require(files, f"No local source for imported assembly: {name}")
        assembly.update(source_file_count=len(files), source_sha256=fingerprint(files))
    require(assemblies, f"No imported local assemblies: {project}")
    return {"packages": packages, "extra_sources": extras, "imported_assemblies": assemblies,
            "package_snapshot_matches_current_source": True,
            "extra_source_snapshot_matches_current_source": True,
            "comparison": "All source/resources and GUIDs match byte-for-byte; folder.meta may differ only in "
                          "trailing spaces on empty userData/assetBundleName/assetBundleVariant scalars. "
                          "Each such path retains imported/current/canonical SHA256 values.",
            "snapshot_excludes": ["*.md", "*.md.meta"],
            "fingerprint_encoding": "SHA256 of sorted [relative path, file SHA256] pairs as compact JSON"}


def native_evidence(scope, filename, kind):
    path = Path(filename).resolve()
    report = test_project.read_json(path)
    run = report["run"]
    project = command_project(run["command"])
    prepared = test_project.read_json(project / test_project.MARKER)
    require(prepared["project_id"] == run["project_id"], f"Project was prepared again: {path}")
    directory = Path(report["results_xml"]).parent
    fresh = test_project.evaluate_run(run, directory)
    require(report["passed"] and fresh["passed"], f"Run failed: {path}; {fresh['reasons']}")
    require(report["tests"] == fresh["tests"] and report["benchmarks"] == fresh["benchmarks"],
            f"Report differs from its XML/log: {path}")
    require(report["results_sha256"] == fresh["results_sha256"], f"XML changed: {path}")
    tests = fresh["tests"]
    require(tests["total"] > 0 and tests["passed"] == tests["total"], f"Nonpassing tests: {path}")
    require(not run.get("test_filter"), f"A final scope must use its complete selected suites: {path}")
    selected = set(run["selected_assemblies"])
    actual = set(tests["assemblies"])
    require(actual <= selected, f"Unselected assemblies in XML: {path}")
    if run["expected_assemblies"]:
        require(actual == set(run["expected_assemblies"]), f"Assembly selection differs: {path}")
    if kind == "graphics":
        require(run.get("graphics") and "-nographics" not in run["command"], f"Graphics disabled: {path}")
    if kind == "benchmark":
        require(run.get("benchmark"), f"Use an explicit --benchmark run: {path}")
        expected = {Path(info["source"]).name for info in prepared["packages"].values()
                    if info["origin"] == "local"}
        measured = {item["plugin"] for item in fresh["benchmarks"]}
        require(expected <= measured, f"Plugins missing measurements: {sorted(expected - measured)}")
    sources = source_evidence(project, prepared)
    require(actual <= sources["imported_assemblies"].keys(), f"Test DLLs not retained: {path}")
    graphics_device = [line.strip() for line in (directory / "unity.log").read_text(errors="replace").splitlines()
                       if line.startswith(("Renderer:", "Vendor:", "Version:", "OPENGL LOG:"))]
    return {"scope": scope, "kind": kind, "passed": True, "run_id_utc": run["run_id"],
            "project": str(project), "project_id": run["project_id"],
            "prepared_base_revision": prepared["revision"], "profile": run["profile"],
            "package_versions": run["package_versions"], "tests": tests,
            "run": run, "benchmarks": fresh["benchmarks"], "source_evidence": sources,
            "graphics_device": graphics_device,
            "artifacts": {"report": artifact(path), "xml": artifact(directory / "results.xml"),
                          "unity_log": artifact(directory / "unity.log"),
                          "launcher_log": artifact(directory / "launcher.log")}}


def player_evidence(scope, filename):
    path = Path(filename).resolve()
    report = test_project.read_json(path)
    require(report["passed"] and verify_player.player_passed(report, report["required_providers"]),
            f"Player verification failed: {path}")
    project = command_project(report["build_command"])
    prepared = test_project.read_json(project / test_project.MARKER)
    require(prepared["project_id"] == report["project_id"], f"Player project was prepared again: {path}")
    required = [provider for suffix, provider in verify_player.PLAYER_PROVIDERS.items()
                if "top.sealoong.unitybox." + suffix in prepared["packages"]]
    require(report["required_providers"] == required, f"Player provider scope differs from prepared packages: {path}")
    directory = Path(report["build_command"][report["build_command"].index("-logFile") + 1]).parent
    require(test_project.read_json(directory / "player.json") == report["player"], f"Player JSON changed: {path}")
    require(test_project.read_json(directory / "build.json") == report["build"], f"Build JSON changed: {path}")
    for source in Path(__file__).with_name("verification").rglob("*.cs"):
        copied = project / "Assets/UnityBoxVerification" / source.relative_to(Path(__file__).with_name("verification"))
        require(copied.is_file() and copied.read_bytes() == source.read_bytes(),
                f"Player template changed: {source}")
    artifacts = {name: artifact(directory / name) for name in
                 ("build.json", "player.json", "build.log", "player.log", "pipeline-trace.json")}
    return {"scope": scope, "kind": "player", **report, "source_evidence": source_evidence(project, prepared),
            "artifacts": {"report": artifact(path), **artifacts}}


def tooling_evidence(filename):
    path = Path(filename).resolve()
    report = test_project.read_json(path)
    checks, names = [], set()
    for check in report["checks"]:
        name = check["name"]
        require(name and name not in names, f"Duplicate or empty tooling name: {name}")
        names.add(name)
        require(check["exit_code"] == 0, f"Tooling command failed: {name}")
        require(isinstance(check["command"], list) and check["command"], f"Missing command arguments: {name}")
        log = Path(check["log"]).resolve()
        summaries = re.findall(r"(?m)^Ran (\d+) tests? in [^\n]+\n\s*\nOK[ \t]*(?:\n|$)",
                               log.read_text(errors="replace"))
        require(len(summaries) == 1 and int(summaries[0]) > 0,
                f"Expected one successful nonzero unittest summary: {log}")
        checks.append({"name": name, "passed": True, "tests": int(summaries[0]),
                       "command": check["command"], "exit_code": check["exit_code"],
                       "workspace_log": artifact(log)})
    require(checks, f"No tooling checks: {path}")
    return {"input_report": artifact(path), "checks": checks}


def vpm_evidence(filename):
    path = Path(filename).resolve()
    report = test_project.read_json(path)
    require(report.get("passed", True) and report.get("exit_code", 0) == 0,
            f"VPM workflow failed: {path}")
    repo = Path(report["repo"]).resolve()
    site = Path(report.get("site", path.parent / "site")).resolve()
    listing = test_project.read_json(site / "vpm.json")
    include = test_project.load_filter(repo)
    manifests = {test_project.read_json(p)["name"]: p for p in (repo / "Assets/UnityBox").glob("*/package.json")}
    selected = report["packages"]
    require(selected and len(selected) == len(set(selected)), f"Empty or duplicate VPM package scope: {path}")
    packages = []
    for name in selected:
        manifest_path = manifests[name]
        manifest = test_project.read_json(manifest_path)
        version = manifest["version"]
        archive = site / (name + "-" + version + ".zip")
        entry = listing["packages"][name]["versions"][version]
        require(sha256(archive) == entry["zipSHA256"], f"VPM listing checksum differs: {archive}")
        source = manifest_path.parent
        expected = {p.relative_to(source).as_posix(): p for p in source.rglob("*")
                    if p.is_file() and include(p.relative_to(source).as_posix(), name)}
        with ZipFile(archive) as zipped:
            require(zipped.testzip() is None, f"VPM archive CRC differs: {archive}")
            require(len(zipped.namelist()) == len(expected) and set(zipped.namelist()) == set(expected),
                    f"VPM archive file selection differs: {archive}")
            require(all(zipped.read(relative) == current.read_bytes() for relative, current in expected.items()),
                    f"VPM package payload differs from current source: {archive}")
        packages.append({"name": name, "version": version, "files": len(expected), "archive": artifact(archive)})
    require("vcc://vpm/addRepo?url=" in (site / "index.html").read_text(), f"Missing VCC registration link: {site}")
    return {"passed": True, "checkout": str(repo), "scope": report["scope"],
            "package_count": len(packages), "packages": packages, "checks": report["checks"],
            "rechecked": ["ZIP CRC", "exact source bytes and metadata", "manifest version", "listing SHA256", "VCC link"],
            "workspace_report": artifact(path), "listing": artifact(site / "vpm.json"),
            "workflow_log": artifact(path.parent / "build.log")}


def sanitized_xml(data):
    """Remove execution identity/command metadata while preserving every test case."""
    root = ET.fromstring(data)
    cases = [ET.tostring(node) for node in root.iter("test-case")]
    removed = []
    for parent in root.iter():
        for child in list(parent):
            if child.tag.lower().replace("_", "-") in {"environment", "environment-variables", "command-line", "commandline"}:
                parent.remove(child)
                removed.append(child.tag)
        for key in list(parent.attrib):
            normalized = key.lower().replace("_", "-")
            if normalized in {"machine-name", "machinename", "user-name", "username", "user-domain", "userdomain"}:
                del parent.attrib[key]
                removed.append("attribute:" + key)
    require(cases == [ET.tostring(node) for node in root.iter("test-case")],
            "Identity metadata occurs inside test cases; review the XML before archiving")
    return (ET.tostring(root, encoding="utf-8", xml_declaration=True) if removed else data), sorted(set(removed))


def archive_name(scope):
    return re.sub(r"[^A-Za-z0-9_.-]", "_", scope).strip(".") or "run"


def archived_paths(value):
    if isinstance(value, dict):
        path = value.get("path")
        if isinstance(path, str) and len(Path(path).parts) == 2 and Path(path).parts[0] == "Verification":
            yield path
        for item in value.values():
            yield from archived_paths(item)
    elif isinstance(value, list):
        for item in value:
            yield from archived_paths(item)


def save_archives(docs, native, players, tooling):
    directory = docs / "Verification"
    directory.mkdir(exist_ok=True)
    previous = docs / "VerificationResults.json"
    previous_paths = set(archived_paths(test_project.read_json(previous))) if previous.is_file() else set()
    claimed = set()
    for run in native:
        name = archive_name(run["scope"]) + "-results.xml.gz"
        require(name not in claimed, f"Duplicate archive scope: {run['scope']}")
        claimed.add(name)
        original = Path(run["artifacts"]["xml"]["path"]).read_bytes()
        sanitized, removed = sanitized_xml(original)
        archived = gzip.compress(sanitized, mtime=0)
        (directory / name).write_bytes(archived)
        run["archived_xml"] = {"path": "Verification/" + name, "compression": "gzip; mtime=0",
                               "bytes": len(archived), "sha256": hashlib.sha256(archived).hexdigest(),
                               "original_xml_sha256": hashlib.sha256(original).hexdigest(),
                               "archived_xml_sha256": hashlib.sha256(sanitized).hexdigest(),
                               "removed_metadata": removed}
    for run in players:
        archived = {}
        for key in ("build.json", "player.json", "pipeline-trace.json"):
            source = Path(run["artifacts"][key]["path"])
            name = archive_name(run["scope"]) + "-" + key
            require(name not in claimed, f"Duplicate archive scope: {run['scope']}")
            claimed.add(name)
            data = test_project.read_json(source)
            fields = {
                "build.json": {"result", "output", "total_errors", "total_warnings", "total_size_bytes",
                               "duration_seconds", "hook_providers", "source_configuration_components"},
                "player.json": {"unity_version", "passed", "scenarios", "frames", "checks", "providers",
                                "failures", "measurements", "mono_heap_bytes"},
                "pipeline-trace.json": {"schema", "started_utc", "completed", "elapsed_ms", "entries"},
            }[key]
            removed = sorted(data.keys() - fields)
            data = {name: value for name, value in data.items() if name in fields}
            encoded = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode()
            (directory / name).write_bytes(encoded)
            archived[key] = {"path": "Verification/" + name, "bytes": len(encoded),
                             "sha256": hashlib.sha256(encoded).hexdigest(),
                             "original_sha256": run["artifacts"][key]["sha256"], "removed_fields": removed}
        run["archived_artifacts"] = archived
    for check in tooling.get("checks", []):
        name = "tooling-" + archive_name(check["name"]) + ".log"
        require(name not in claimed, f"Duplicate tooling archive: {name}")
        claimed.add(name)
        source = Path(check["workspace_log"]["path"])
        encoded = source.read_bytes()
        (directory / name).write_bytes(encoded)
        check["archived_log"] = {"path": "Verification/" + name, "bytes": len(encoded), "sha256": sha256(source)}
    if "vpm_workflow" in tooling:
        workflow = tooling["vpm_workflow"]
        name = "vpm-workflow.json"
        claimed.add(name)
        original = test_project.read_json(workflow["workspace_report"]["path"])
        fields = {"repo", "scope", "packages", "checks", "passed", "exit_code", "site"}
        archived = {key: value for key, value in original.items() if key in fields}
        encoded = (json.dumps(archived, ensure_ascii=False, indent=2) + "\n").encode()
        (directory / name).write_bytes(encoded)
        workflow["archived_report"] = {"path": "Verification/" + name, "bytes": len(encoded),
                                        "sha256": hashlib.sha256(encoded).hexdigest(),
                                        "original_sha256": workflow["workspace_report"]["sha256"],
                                        "removed_fields": sorted(original.keys() - fields)}
    for stale in previous_paths - {"Verification/" + name for name in claimed}:
        (docs / stale).unlink(missing_ok=True)
    (directory / "README.md").write_text(
        "# 原始验证记录\n\n"
        "`*-results.xml.gz` 保存真实 Unity Test Framework / NUnit 输出，使用标准 gzip 压缩（mtime=0）。"
        "执行身份、环境变量与命令行元数据在存在时移除；每个测试用例保留原内容。"
        "[VerificationResults.json](../VerificationResults.json) 分别记录原 XML、归档 XML 和 gzip 文件 SHA256。\n\n"
        "`*-build.json`、`*-player.json` 和 `*-pipeline-trace.json` 分别保存实际 Linux 构建结果、"
        "Player 场景断言与性能数据、构建回调顺序。归档内容来自固定测试 fixture，"
        "不包含 Hub 会话、账号登录信息或完整 Editor 启动日志。\n\n"
        "`tooling-*.log` 保存实际 Python unittest 输出，测试数从成功摘要读取；"
        "`vpm-workflow.json` 保存本地发布工作流的报告。对应包 ZIP 已逐文件与当前源码比对，"
        "CRC、manifest 版本和 listing SHA256 记录在结果索引中。当前归档以索引引用为准，"
        "旧运行通过提交历史保留。\n\n"
        "解压示例：`gzip -dc core-results.xml.gz > core-results.xml`。\n", encoding="utf-8")


def number(value):
    return f"{value:.6f}".rstrip("0").rstrip(".")


def measurement_protocols(performance):
    present = {(item["plugin"], item["scenario"]) for key in ("editor_runs", "graphics_runs")
               for run in performance[key] for item in run["measurements"]}
    protocols = [
        ({("AnimatorOptimizer", "analyze_static_layers")},
         "AnimatorOptimizer 分析", "3 次", "9 组 × 1 次分析"),
        ({("AnimatorOptimizer", "animator_update_before_merge"), ("AnimatorOptimizer", "animator_update_after_merge")},
         "AnimatorOptimizer 层合并更新对照", "测量函数 30 次", "9 组 × 300 次更新；固定末态输入"),
        ({("AnimatorOptimizer", "automatic_retained_singleton_groups"), ("AnimatorOptimizer", "explicit_singleton_conversion")},
         "AnimatorOptimizer 自动保留 / 显式转换单层分组", "共同预热 30 次，各测量函数再预热 30 次",
         "9 组 × 300 次更新；先比较 60 帧切换输出，再固定末态输入"),
        ({("AdvancedCostumeController", "persistent_outfit_parts_generation"),
          ("BlendshapeControllerGenerator", "persistent_float_controller_generation")},
         "ACC、Blendshape 持久化控制器生成", "1 次", "5 次生成，核对层、参数与 Clip 数"),
        ({("AdvancedCostumeController", "animator_update"), ("BlendshapeControllerGenerator", "animator_update")},
         "ACC、Blendshape 参数切换更新", "128 次", "9 组 × 512 次更新"),
        ({("AvatarSecuritySystem", "gesture_graph_generation")},
         "AvatarSecuritySystem 手势判定图生成", "1 次", "7 次；按密码长度检查连续保持状态数、独立层与 WD Off"),
        ({("ComponentReferenceMigration", "scan_component_references")},
         "组件引用工具只读扫描", "1 次", "5 次；128/1024/4096 组件，核对识别数量和原文件字节"),
        ({("VisemeBlendshapeOverride", "Resolve15Mappings"), ("VisemeBlendshapeOverride", "BuildPersist15VoiceStates")},
         "Viseme 映射解析 / 持久化控制器重建", "2 次", "21 组 × 25 次解析 / 7 次重建"),
        ({("PrefabDiffPatcher", "analyze_serialized_hierarchy"), ("PrefabDiffPatcher", "apply_prefab_variant")},
         "PrefabDiffPatcher 分析与应用", "2 次", "9 次；保存后核对 Light 值与 Variant 继承"),
        ({("AvatarColliderMonitor", "capture_3d_colliders"), ("AvatarColliderMonitor", "history_cached_read")},
         "Collider 捕获 / 历史与预览缓存读取", "2 次", "9 次捕获或重算 / 9 组 × 10,000 次缓存读取"),
        ({("BuildPipelineLogger", "disabled_shader_callback"), ("BuildPipelineLogger", "record_callback_trace")},
         "Logger 回调、设置读取、trace 记录与 JSON 导出", "2 次", "9 组；每组操作次数见 JSON"),
        ({("AdvancedCostumeController", "menu_icon_rendering_256px")},
         "ACC 2 张 256×256 图标", "1 次", "5 次；核对实际红绿像素、原材质与 layer"),
    ]
    return [{"workload": label, "warmup": warmup, "sampling": sampling}
            for scenarios, label, warmup, sampling in protocols if scenarios & present]


def performance_markdown(performance):
    lines = ["# UnityBox 性能实测", "",
             "使用 Unity 2022.3.22f1、Linux x86-64，在隔离工程中执行。测量与生成物、Animator 输出检查"
             "在同一原生测试范围执行。原始数据、运行命令、源码指纹和日志 SHA256 见 [PerformanceResults.json](PerformanceResults.json)。", "",
             "时间单位为毫秒/次操作；表中的 p95 使用采样序列的 nearest-rank 分位数。"
             "Editor 生成耗时包含各测试明确计时的生成/保存操作。ACC 和 Blendshape 的 Editor 更新基准逐帧切换参数；"
             "优化器的合并对照与 Player 更新基准使用固定输入。", "",
             "`allocated_bytes_per_op = -1` 表示该 Unity Mono 分配计数 API 不提供此计数；"
             "`controller_memory_bytes = -1` 表示 Release Player 的对象内存 API 不提供该对象大小。"
             "Player 的 `mono_heap_bytes` 是所有场景完成后执行 GC 的整体托管堆存量。", "",
             "| 插件 / 工作负载 | 预热 | 采样方式 |", "| --- | --- | --- |"]
    for protocol in performance["measurement_protocols"]:
        lines.append(f"| {protocol['workload']} | {protocol['warmup']} | {protocol['sampling']} |")
    lines.append("")
    for heading, key in (("Editor 基准", "editor_runs"), ("图形输出", "graphics_runs"), ("Linux Player", "player_runs")):
        runs = performance[key]
        if not runs:
            continue
        lines.extend([f"## {heading}", ""])
        if key == "player_runs":
            lines.extend(["Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。"
                          "输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。", ""])
        for run in runs:
            lines.extend([f"运行 `{run['run_id_utc']}`，范围 `{run['scope']}`。", "",
                          "| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |", "| --- | ---: | ---: | ---: | ---: |"])
            for item in run["measurements"]:
                lines.append(f"| {item['plugin']} / `{item['scenario']}` | {item['size']} | {item['samples']} | "
                             f"{number(item['median_ms'])} | {number(item['p95_ms'])} |")
            lines.append("")
            if key == "graphics_runs":
                lines.extend(["图形设备：" + "；".join(run["graphics_device"]), ""])
            if key == "editor_runs":
                measurements = {(item["plugin"], item["scenario"], item["size"]): item for item in run["measurements"]}
                for (plugin, scenario, size), after in measurements.items():
                    if plugin == "AnimatorOptimizer" and scenario == "animator_update_after_merge":
                        before = measurements[(plugin, "animator_update_before_merge", size)]
                        reduction = (1 - after["median_ms"] / before["median_ms"]) * 100
                        direction = "减少" if reduction >= 0 else "增加"
                        lines.append(f"{size} 层合并：中位数 {number(before['median_ms'])} → "
                                     f"{number(after['median_ms'])} ms，{direction} {abs(reduction):.1f}%。")
                    if plugin == "AnimatorOptimizer" and scenario == "explicit_singleton_conversion":
                        retained = measurements[(plugin, "automatic_retained_singleton_groups", size)]
                        difference = (after["median_ms"] / retained["median_ms"] - 1) * 100
                        direction = "增加" if difference >= 0 else "减少"
                        lines.append(f"{size} 个独立单层分组：自动保留原图 {number(retained['median_ms'])} ms，"
                                     f"显式转换 {number(after['median_ms'])} ms，{direction} {abs(difference):.1f}%。")
                    if plugin == "AvatarColliderMonitor" and scenario == "history_cached_read":
                        reference = measurements[(plugin, "history_recompute_reference", size)]
                        lines.append(f"{size} 条 Collider 历史：完整重算中位数 {number(reference['median_ms'])} ms；"
                                     f"读取已缓存的比较视图 {number(after['median_ms'])} ms。")
                    if plugin == "BuildPipelineLogger" and scenario == "disabled_shader_callback":
                        reference = measurements[(plugin, "editorprefs_setting_read_reference", size)]
                        lines.append(f"Logger 每组 {size} 次：直接读取 EditorPrefs 的单次中位数 "
                                     f"{number(reference['median_ms'])} ms，缓存禁用回调 {number(after['median_ms'])} ms。")
                lines.append("")
            if key == "player_runs":
                lines.extend([f"功能场景 {run['scenarios']} 个、输出检查 {run['checks']} 项；"
                              f"完成后整体托管堆 {run['mono_heap_bytes']:,} bytes。", ""])
                lines.extend([f"Linux 构建耗时 {number(run['build']['duration_seconds'])} 秒，"
                              f"产物 {run['build']['total_size_bytes']:,} bytes，"
                              f"{run['build']['total_errors']} 个构建错误。", ""])
                measurements = {item["scenario"]: item for item in run["measurements"]}
                increases = []
                for name, before in measurements.items():
                    if name.startswith("AnimatorOptimizer/") and "_Source" in name:
                        after = measurements[name.replace("_Source", "_Optimized")]
                        reduction = (1 - after["median_ms"] / before["median_ms"]) * 100
                        direction = "减少" if reduction >= 0 else "增加"
                        if reduction < 0:
                            increases.append("`" + name.rsplit("/", 1)[0] + "`")
                        lines.append(f"Player `{name.rsplit('/', 1)[0]}`：{before['animator_layers']} → "
                                     f"{after['animator_layers']} 层，{before['clip_count']} → {after['clip_count']} 个 Clip；"
                                     f"中位数 {number(before['median_ms'])} → {number(after['median_ms'])} ms，"
                                     f"{direction} {abs(reduction):.1f}%。")
                lines.append("")
                if increases:
                    lines.extend(["本次测量中 " + "、".join(increases) + " 的稳态更新耗时增加。"
                                  "这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，"
                                  "转换范围按控制器结构与运行状态选择。", ""])
    return "\n".join(lines).rstrip() + "\n"


def performance_data(native, players):
    result = {"schema": 1, "editor": test_project.VERSION, "editor_runs": [], "graphics_runs": [], "player_runs": []}
    for run in native:
        if run["kind"] not in {"benchmark", "graphics"}:
            continue
        key = "editor_runs" if run["kind"] == "benchmark" else "graphics_runs"
        result[key].append({"scope": run["scope"], "run_id_utc": run["run_id_utc"],
                            "measurements": run["benchmarks"], "machine": run["run"]["machine"],
                            "measured_plugins": sorted({item["plugin"] for item in run["benchmarks"]}),
                            "command": run["run"]["command"], "artifacts": run["artifacts"],
                            "archived_xml": run.get("archived_xml"),
                            "source_evidence": run["source_evidence"], "graphics_device": run["graphics_device"]})
    for run in players:
        result["player_runs"].append({"scope": run["scope"], "run_id_utc": run["run_id"],
                                      "measurements": run["player"]["measurements"],
                                      "build": run["build"], "providers": run["player"]["providers"],
                                      "build_command": run["build_command"], "player_command": run["player_command"],
                                      **{key: run["player"][key] for key in ("scenarios", "checks", "frames", "mono_heap_bytes")},
                                      "artifacts": run["artifacts"], "archived_artifacts": run.get("archived_artifacts"),
                                      "source_evidence": run["source_evidence"]})
    result["measurement_protocols"] = measurement_protocols(result)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, action="append", required=True, help="Destination checkout; repeatable")
    for option in ("run", "graphics", "benchmark", "player"):
        parser.add_argument("--" + option, action="append", default=[], metavar="SCOPE=REPORT_JSON")
    parser.add_argument("--tooling", type=Path,
                        help="JSON checks containing name, command arguments, exit_code and unittest log path")
    parser.add_argument("--vpm", type=Path, help="Local workflow report whose site archives match the final source")
    args = parser.parse_args(argv)
    try:
        native, players = [], []
        for kind, entries in (("functional", args.run), ("graphics", args.graphics), ("benchmark", args.benchmark)):
            for entry in entries:
                scope, filename = entry.split("=", 1)
                native.append(native_evidence(scope, filename, kind))
        for entry in args.player:
            scope, filename = entry.split("=", 1)
            players.append(player_evidence(scope, filename))
        require(native or players, "Provide at least one completed run")
        tooling = tooling_evidence(args.tooling) if args.tooling else {}
        if args.vpm:
            tooling["vpm_workflow"] = vpm_evidence(args.vpm)
        evidence = {"schema": 3, "captured_at_utc": datetime.now(timezone.utc).isoformat(),
                    "editor": test_project.VERSION, "platform": "Linux x86-64",
                    "scope_accounting": "Native profiles overlap; counts are reported per run, not summed as distinct cases.",
                    "native_runs": native, "player_runs": players, "tooling": tooling}
        for repo in args.repo:
            docs = repo.resolve() / "Docs"
            docs.mkdir(exist_ok=True)
            save_archives(docs, native, players, tooling)
            performance = performance_data(native, players)
            branch = subprocess.check_output(["git", "-C", str(repo), "branch", "--show-current"], text=True).strip()
            test_project.write_json(docs / "VerificationResults.json", {"branch": branch, **evidence})
            test_project.write_json(docs / "UnityNativeResults.json", {"branch": branch, **evidence})
            test_project.write_json(docs / "PerformanceResults.json", performance)
            (docs / "Performance.md").write_text(performance_markdown(performance), encoding="utf-8")
        print(json.dumps({"native": {run["scope"]: run["tests"]["total"] for run in native},
                          "players": {run["scope"]: run["player"]["checks"] for run in players}}, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError, BadZipFile) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
