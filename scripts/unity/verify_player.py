#!/usr/bin/env python3
"""Build an isolated project's real Linux player and execute package runtime probes."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

import test_project

PLAYER_PROVIDERS = {
    "animator-optimizer": "OptimizerRuntimeAndPerformanceTests",
    "advanced-costume-controller": "CostumeRuntimeTests",
    "blendshape-controller-generator": "BlendshapeRuntimeTests",
    "avatar-security-system": "StandaloneRuntimeTests",
    "viseme-blendshape-override": "VisemeAnimatorEvaluationTests",
}


def missing_providers(required, actual):
    return [name for name in required if not any(value == name or value.endswith("." + name) for value in actual)]


def player_passed(report, required):
    player = report.get("player", {})
    return (report.get("build_exit_code") == 0 and report.get("build", {}).get("result") == "Succeeded"
            and report.get("player_exit_code") == 0 and player.get("passed") is True
            and player.get("checks", 0) > 0 and player.get("scenarios", 0) > 0
            and not missing_providers(required, player.get("providers", [])))


def execute(command, project, env, log, timeout):
    with log.open("w") as stream:
        process = subprocess.Popen(command, cwd=project, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            test_project.stop_process_group(process)
            raise
        except KeyboardInterrupt:
            test_project.stop_process_group(process)
            raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--unity", type=Path, default=Path(test_project.DEFAULT_EDITOR))
    parser.add_argument("--xdg-root", type=Path, required=True)
    parser.add_argument("--timeout", type=float, default=1200)
    args = parser.parse_args(argv)
    project = args.project.resolve()
    metadata = test_project.read_json(project / test_project.MARKER)
    required = [provider for suffix, provider in PLAYER_PROVIDERS.items()
                if "top.sealoong.unitybox." + suffix in metadata["packages"]]
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
    directory = project / ".unitybox-player-results" / run_id
    directory.mkdir(parents=True)
    source = Path(__file__).with_name("verification")
    destination = project / "Assets/UnityBoxVerification"
    shutil.copytree(source, destination, dirs_exist_ok=True)
    env = os.environ.copy()
    for kind in ("CONFIG", "DATA", "CACHE"):
        folder = args.xdg_root.resolve() / kind.lower()
        folder.mkdir(parents=True, exist_ok=True)
        env["XDG_" + kind + "_HOME"] = str(folder)
    env["UNITYBOX_VERIFICATION_OUTPUT"] = str(directory)
    env["UNITYBOX_REQUIRED_PLAYER_PROVIDERS"] = ";".join(required)
    build_command = [str(args.unity.resolve()), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
                     "-executeMethod", "UnityBoxPlayerBuilder.Build", "-logFile", str(directory / "build.log")]
    report = {"schema": 1, "run_id": run_id, "project_id": metadata["project_id"],
              "started_at": test_project.utc_now(), "package_versions": {name: item["version"] for name, item in metadata["packages"].items()},
              "build_command": build_command, "required_providers": required, "passed": False}
    try:
        report["build_exit_code"] = execute(build_command, project, env, directory / "builder-launcher.log", args.timeout)
        build = directory / "build.json"
        if build.is_file():
            report["build"] = test_project.read_json(build)
        if report["build_exit_code"] == 0 and report.get("build", {}).get("result") == "Succeeded":
            player_report = directory / "player.json"
            player_command = [str(directory / "UnityBoxPlayer"), "-batchmode", "-nographics",
                              "-logFile", str(directory / "player.log"), "-unityboxReport", str(player_report)]
            report["player_command"] = player_command
            report["player_exit_code"] = execute(player_command, directory, env, directory / "player-launcher.log", args.timeout)
            if player_report.is_file():
                report["player"] = test_project.read_json(player_report)
            report["passed"] = player_passed(report, required)
            report["missing_providers"] = missing_providers(required, report.get("player", {}).get("providers", []))
    except (OSError, ValueError, subprocess.TimeoutExpired, KeyboardInterrupt) as exc:
        report["error"] = str(exc) or type(exc).__name__
    finally:
        report["finished_at"] = test_project.utc_now()
        test_project.write_json(directory / "report.json", report)
        test_project.write_json(project / ".unitybox-player-results/latest.json", {"run_id": run_id})
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
