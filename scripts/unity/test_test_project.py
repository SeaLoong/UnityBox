"""Tests for the Python harness only; these do not simulate passing Unity tests."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import time
import unittest

SPEC = importlib.util.spec_from_file_location("unitybox_test_project", Path(__file__).with_name("test_project.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class ResultEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.run = {"exit_code": 0, "started_epoch": time.time() - 1, "expected_assemblies": ["Fixture.Tests"]}

    def tearDown(self):
        self.temp.cleanup()

    def xml(self, cases, result="Passed"):
        (self.directory / "results.xml").write_text(
            f'<test-run result="{result}"><test-suite type="Assembly" name="Fixture.Tests.dll">'
            + cases + '</test-suite></test-run>', encoding="utf-8")

    def test_pass_and_skip_are_reported_separately(self):
        self.xml('<test-case fullname="Fixture.Pass" result="Passed"/>'
                 '<test-case fullname="Fixture.Skip" result="Skipped"><reason><message>Explicit</message></reason></test-case>')
        report = runner.evaluate_run(self.run, self.directory)
        self.assertTrue(report["passed"])
        self.assertEqual(report["tests"]["passed"], 1)
        self.assertEqual(report["tests"]["skipped_tests"][0]["reason"], "Explicit")
        self.assertEqual(report["tests"]["assembly_counts"]["Fixture.Tests"]["skipped"], 1)

    def test_nonzero_exit_cannot_be_overridden_by_passing_xml(self):
        self.xml('<test-case result="Passed"/>')
        self.run["exit_code"] = 1
        self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])

    def test_missing_zero_skipped_only_and_malformed_xml_fail(self):
        self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])
        for cases in ("", '<test-case result="Skipped"/>'):
            self.xml(cases)
            self.assertIn("No tests executed", runner.evaluate_run(self.run, self.directory)["reasons"])
        (self.directory / "results.xml").write_text("<broken", encoding="utf-8")
        self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])

    def test_stale_results_and_missing_expected_assembly_fail(self):
        self.xml('<test-case result="Passed"/>')
        self.run["started_epoch"] = time.time() + 30
        self.assertIn("NUnit XML predates this run", runner.evaluate_run(self.run, self.directory)["reasons"])
        self.run["started_epoch"] = time.time() - 1
        self.run["expected_assemblies"].append("Missing.Tests")
        self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])

    def test_license_compile_timeout_and_test_failures_are_not_success(self):
        self.xml('<test-case result="Passed"/>')
        for line in ("No valid Unity Editor license found", "Widget.cs(1,1): error CS0246: missing type"):
            (self.directory / "unity.log").write_text(line, encoding="utf-8")
            self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])
        (self.directory / "unity.log").unlink()
        self.run["timed_out"] = True
        self.assertFalse(runner.evaluate_run(self.run, self.directory)["passed"])
        self.run["timed_out"] = False
        self.xml('<test-case fullname="Fixture.Bad" result="Failed"><failure><message>assertion</message></failure></test-case>', "Failed")
        report = runner.evaluate_run(self.run, self.directory)
        self.assertFalse(report["passed"])
        self.assertEqual(report["tests"]["failures"][0]["message"], "assertion")


class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.cache = self.root / "cache"
        self.project = self.root / "project"
        scripts = self.repo / ".github/scripts"
        scripts.mkdir(parents=True)
        actual_filter = Path(__file__).resolve().parents[2] / ".github/scripts/package_vpm.py"
        (scripts / "package_vpm.py").write_bytes(actual_filter.read_bytes())
        self.package = self.repo / "Assets/UnityBox/AvatarSecuritySystem"
        self.package.mkdir(parents=True)
        runner.write_json(self.package / "package.json", {"name": "top.sealoong.unitybox.avatar-security-system", "version": "1.0.0"})
        for name, version in (("com.unity.test-framework", "1.1.33"), ("com.unity.ext.nunit", "1.0.6")):
            path = self.cache / name
            path.mkdir(parents=True)
            runner.write_json(path / "package.json", {"name": name, "version": version})
        (self.repo / "UserScene.unity").write_text("keep original scene", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def prepare(self, *extra):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return runner.main(["prepare", "--repo", str(self.repo), "--project", str(self.project),
                                "--dependency-cache", str(self.cache), *extra])

    def test_packaging_filter_preserves_tests_and_source_scene(self):
        for relative in ("Tests/Editor/Regression.cs", "Tests/Legacy.unity", "Tests/Bundles/old.asset",
                         "Editor/AnimationPathBindingProbe.cs", "Generated/output.asset"):
            path = self.package / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("fixture", encoding="utf-8")
        self.assertEqual(self.prepare("--package", "AvatarSecuritySystem"), 0)
        copy = self.project / "Packages/top.sealoong.unitybox.avatar-security-system"
        self.assertTrue((copy / "Tests/Editor/Regression.cs").is_file())
        for relative in ("Tests/Legacy.unity", "Tests/Bundles/old.asset", "Editor/AnimationPathBindingProbe.cs", "Generated/output.asset"):
            self.assertFalse((copy / relative).exists())
            self.assertTrue((self.package / relative).exists())
        self.assertEqual((self.repo / "UserScene.unity").read_text(), "keep original scene")
        self.assertFalse((self.project / "UserScene.unity").exists())
        stale = copy / "stale.cs"
        stale.write_text("stale", encoding="utf-8")
        self.assertEqual(self.prepare(), 0)
        self.assertFalse(stale.exists())

    def test_refuses_source_destination_unknown_package_and_missing_dependency(self):
        self.assertEqual(self.prepare("--package", "Missing"), 2)
        runner.write_json(self.package / "package.json", {"name": "top.sealoong.unitybox.avatar-security-system",
                          "version": "1.0.0", "dependencies": {"uncached.package": "1.0.0"}})
        self.assertEqual(self.prepare(), 2)
        self.assertFalse(self.project.exists())
        self.project = self.repo / "UnsafeProject"
        self.assertEqual(self.prepare(), 2)
        self.assertFalse(self.project.exists())

    def test_core_records_full_workflow_vpm_dependencies_without_installing_sdk(self):
        runner.write_json(self.package / "package.json", {"name": "top.sealoong.unitybox.avatar-security-system",
                          "version": "1.0.0", "vpmDependencies": {"com.vrchat.avatars": ">=3.7.0"}})
        self.assertEqual(self.prepare(), 0)
        metadata = runner.read_json(self.project / runner.MARKER)
        self.assertEqual(metadata["ignored_vpm_dependencies"], {
            "top.sealoong.unitybox.avatar-security-system": {"com.vrchat.avatars": ">=3.7.0"}})
        self.assertNotIn("com.vrchat.avatars", metadata["packages"])
        self.assertFalse((self.project / "Packages/com.vrchat.avatars").exists())

    def test_vrc_rejects_old_or_prerelease_sdk_before_creating_project(self):
        for version in ("3.6.0", "3.7.0-beta.1"):
            for name in ("com.vrchat.base", "com.vrchat.avatars"):
                path = self.cache / name
                path.mkdir(exist_ok=True)
                runner.write_json(path / "package.json", {"name": name, "version": version})
            self.assertEqual(self.prepare("--profile", "vrc", "--dependency", "com.vrchat.base"), 2)
            self.assertFalse(self.project.exists())

    def test_vrc_requires_ugui_even_when_sdk_manifest_omits_it(self):
        for name in ("com.vrchat.base", "com.vrchat.avatars"):
            path = self.cache / name
            path.mkdir()
            runner.write_json(path / "package.json", {"name": name, "version": "3.7.0"})
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()) as errors:
            code = runner.main(["prepare", "--repo", str(self.repo), "--project", str(self.project),
                                "--dependency-cache", str(self.cache), "--profile", "vrc"])
        self.assertEqual(code, 2)
        self.assertIn("com.unity.ugui", errors.getvalue())
        self.assertFalse(self.project.exists())

    def test_cached_dependency_range_must_match_without_damaging_existing_project(self):
        self.assertEqual(self.prepare(), 0)
        before = (self.project / runner.MARKER).read_bytes()
        runner.write_json(self.package / "package.json", {"name": "top.sealoong.unitybox.avatar-security-system",
                          "version": "1.0.0", "dependencies": {"fixture.integration": ">=1.11.0 <2.0.0-a"}})
        dependency = self.cache / "fixture.integration"
        dependency.mkdir()
        for version in ("1.10.0", "1.11.0-rc.1", "2.0.0"):
            runner.write_json(dependency / "package.json", {"name": "fixture.integration", "version": version})
            self.assertEqual(self.prepare(), 2)
            self.assertEqual((self.project / runner.MARKER).read_bytes(), before)
            self.assertFalse((self.project / "Packages/fixture.integration").exists())

    def test_editor_exclusion_and_prerelease_constraints_control_expected_assemblies(self):
        runner.write_json(self.package / "Excluded.asmdef", {"name": "Excluded.Tests",
                          "optionalUnityReferences": ["TestAssemblies"], "excludePlatforms": ["Editor"]})
        runner.write_json(self.package / "Versioned.asmdef", {"name": "Versioned.Tests",
                          "optionalUnityReferences": ["TestAssemblies"], "defineConstraints": ["INTEGRATION"],
                          "versionDefines": [{"name": "fixture.integration", "expression": "[1.11.0,2.0.0)", "define": "INTEGRATION"}]})
        self.assertEqual(runner.enabled_test_assemblies([self.package], {"fixture.integration": "1.11.0-rc.1"}), [])
        self.assertEqual(runner.enabled_test_assemblies([self.package], {"fixture.integration": "1.11.0"}), ["Versioned.Tests"])
        self.assertFalse(runner.version_matches("1.11.0-rc.1", "1.11.0"))
        self.assertTrue(runner.dependency_matches("1.11.1", ">=1.11.0 <2.0.0-a"))

    def test_run_launch_failure_is_persisted_and_report_exits_nonzero(self):
        self.assertEqual(self.prepare(), 0)
        with contextlib.redirect_stdout(io.StringIO()):
            code = runner.main(["run", "--project", str(self.project), "--unity", str(self.root / "missing-unity")])
            report_code = runner.main(["report", "--project", str(self.project)])
        self.assertEqual(code, 1)
        self.assertEqual(report_code, 1)
        run_id = runner.read_json(self.project / ".unitybox-test-results/latest.json")["run_id"]
        report = runner.read_json(self.project / ".unitybox-test-results" / run_id / "report.json")
        self.assertIsNone(report["run"]["exit_code"])
        self.assertFalse(report["passed"])
        self.assertIn("launch_error", report["run"])


if __name__ == "__main__":
    unittest.main()
