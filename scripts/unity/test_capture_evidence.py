"""Source snapshots and saved execution evidence must still match their raw inputs."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

import capture_evidence as evidence
import test_project


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "project"
        self.project.mkdir()
        self.metadata = {"repo": str(self.root), "packages": {}}
        self.filter = patch.object(test_project, "load_filter", return_value=lambda *args: True)
        self.filter.start()
        self.addCleanup(self.filter.stop)

    def add_package(self, name):
        source = self.root / ("checkout-" + name) / "Assets/UnityBox" / name
        copied = self.project / "Packages" / name
        for folder in (source, copied):
            folder.mkdir(parents=True)
            (folder / "package.json").write_text(json.dumps({"name": name, "version": "1.0.0"}))
            (folder / "Runtime.asmdef").write_text(json.dumps({"name": name + ".Runtime"}))
            (folder / "Behaviour.cs").write_text("class Behaviour {}")
            (folder / "Behaviour.cs.meta").write_text("guid: original-guid")
        self.metadata["packages"][name] = {"origin": "local", "source": str(source),
                                            "sha256": "prepared-tree", "version": "1.0.0"}
        backup = self.project / ".unitybox-native-assemblies"
        backup.mkdir(exist_ok=True)
        (backup / (name + ".Runtime.dll")).write_bytes(b"retained actual-DLL fixture")
        return source, copied

    def add_extra_source(self):
        name = test_project.EXTRA_SOURCE_MODULE
        source = self.root / "checkout-module" / test_project.EXTRA_SOURCE_DESTINATION
        copied = self.project / test_project.EXTRA_SOURCE_DESTINATION
        for relative in ("Editor/Module.cs", "Tests/Editor/ModuleTests.cs"):
            path = source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("// independent module source\n")
            path.with_name(path.name + ".meta").write_text("fileFormatVersion: 2\nguid: module-source-guid\n")
        for relative, assembly in (("Editor/Module.asmdef", "Fixture.Module.Editor"),
                                   ("Tests/Editor/ModuleTests.asmdef", "Fixture.Module.Tests")):
            path = source / relative
            path.write_text(json.dumps({"name": assembly}))
            path.with_name(path.name + ".meta").write_text("fileFormatVersion: 2\nguid: module-assembly-guid\n")
            retained = self.project / ".unitybox-native-assemblies"
            retained.mkdir(exist_ok=True)
            (retained / (assembly + ".dll")).write_bytes(b"retained DLL fixture for source ownership")
        source.with_name(source.name + ".meta").write_text(
            "fileFormatVersion: 2\nguid: module-root-guid\nfolderAsset: yes\nDefaultImporter:\n  userData:\n")
        copied_info = test_project.copy_extra_source(source, copied, lambda *args: True)
        self.metadata.setdefault("extra_sources", {})[name] = {
            **copied_info, "destination": test_project.EXTRA_SOURCE_DESTINATION.as_posix()}
        return source, copied

    def test_extra_source_assemblies_record_their_real_module_ownership_and_source_hashes(self):
        self.add_package("First")
        source, copied = self.add_extra_source()
        result = evidence.source_evidence(self.project, self.metadata)
        self.assertEqual(set(result["packages"]), {"First"})
        self.assertEqual(set(result["extra_sources"]), {test_project.EXTRA_SOURCE_MODULE})
        self.assertEqual(set(result["imported_assemblies"]), {"First.Runtime", "Fixture.Module.Editor", "Fixture.Module.Tests"})
        for assembly in ("Fixture.Module.Editor", "Fixture.Module.Tests"):
            record = result["imported_assemblies"][assembly]
            self.assertEqual(record["extra_source"], test_project.EXTRA_SOURCE_MODULE)
            self.assertNotIn("package", record)
            self.assertEqual(record["source_file_count"], 2)
            self.assertTrue(Path(record["dll"]["path"]).is_file())
        expected = [(test_project.EXTRA_SOURCE_MODULE + "/Editor/" + name, evidence.sha256(copied / "Editor" / name))
                    for name in ("Module.cs", "Module.asmdef")]
        self.assertEqual(result["imported_assemblies"]["Fixture.Module.Editor"]["source_sha256"], evidence.fingerprint(expected))
        self.assertTrue(result["extra_source_snapshot_matches_current_source"])
        self.assertEqual(result["extra_sources"][test_project.EXTRA_SOURCE_MODULE]["source"], str(source))

    def test_extra_source_code_and_root_guid_changes_invalidate_evidence(self):
        source, _ = self.add_extra_source()
        for path in (source / "Editor/Module.cs", source.with_name(source.name + ".meta")):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(original.replace(b"module-root-guid", b"changed-root-guid") + b"// changed\n")
                with self.assertRaisesRegex(ValueError, "Source changed since preparation"):
                    evidence.source_evidence(self.project, self.metadata)
                path.write_bytes(original)

    def test_extra_source_files_added_after_preparation_invalidate_evidence(self):
        source, _ = self.add_extra_source()
        (source / "Editor/NewWindow.cs").write_text("// new executable source\n")
        with self.assertRaisesRegex(ValueError, "NewWindow.cs"):
            evidence.source_evidence(self.project, self.metadata)

    def test_extra_source_markdown_and_folder_metadata_formatting_follow_package_policy(self):
        source, copied = self.add_extra_source()
        (source / "README.md").write_text("current instructions\n")
        (copied / "README.md").write_text("instructions at preparation\n")
        metadata = copied.with_name(copied.name + ".meta")
        metadata.write_text(metadata.read_text().replace("  userData:\n", "  userData: \n"))
        result = evidence.source_evidence(self.project, self.metadata)
        module = result["extra_sources"][test_project.EXTRA_SOURCE_MODULE]
        self.assertEqual([item["path"] for item in module["metadata_formatting_only"]], [test_project.EXTRA_SOURCE_MODULE + ".meta"])
        self.assertNotEqual(module["source_snapshot_sha256"], module["imported_snapshot_sha256"])

    def test_cross_checkout_snapshot_accepts_markdown_updates_and_records_unity_metadata(self):
        self.add_package("First")
        source, copied = self.add_package("Second")
        (source / "README.md").write_text("updated workflow")
        (copied / "README.md").write_text("workflow when prepared")
        (copied / "package.json.meta").write_text("guid: unity-generated")
        result = evidence.source_evidence(self.project, self.metadata)
        self.assertEqual(set(result["packages"]), {"First", "Second"})
        self.assertEqual(set(result["imported_assemblies"]), {"First.Runtime", "Second.Runtime"})
        self.assertEqual(result["packages"]["Second"]["importer_generated_metadata"], ["package.json.meta"])

    def test_source_file_added_after_preparation_invalidates_evidence(self):
        source, _ = self.add_package("First")
        (source / "NewRuntime.cs").write_text("class NewRuntime {}")
        with self.assertRaisesRegex(ValueError, "NewRuntime.cs"):
            evidence.source_evidence(self.project, self.metadata)

    def test_source_guid_changed_after_preparation_invalidates_evidence(self):
        source, _ = self.add_package("First")
        (source / "Behaviour.cs.meta").write_text("guid: different-guid")
        with self.assertRaisesRegex(ValueError, "Behaviour.cs.meta"):
            evidence.source_evidence(self.project, self.metadata)

    def test_changed_xml_cannot_reuse_a_saved_passing_report(self):
        directory = self.project / ".unitybox-test-results/run"
        directory.mkdir(parents=True)
        xml = directory / "results.xml"
        xml.write_text('<test-run result="Passed"><test-suite type="Assembly" name="First.Tests.dll">'
                       '<test-case fullname="First.One" result="Passed"/></test-suite></test-run>')
        (directory / "unity.log").write_text("done")
        (directory / "launcher.log").write_text("")
        run = {"project_id": "fixture", "started_epoch": 0, "exit_code": 0,
               "command": ["Unity", "-projectPath", str(self.project)], "expected_assemblies": ["First.Tests"]}
        report = test_project.evaluate_run(run, directory)
        report_path = directory / "report.json"
        test_project.write_json(report_path, report)
        test_project.write_json(self.project / test_project.MARKER, {"project_id": "fixture"})
        xml.write_text(xml.read_text().replace("First.One", "First.Changed"))
        with self.assertRaisesRegex(ValueError, "XML changed"):
            evidence.native_evidence("core", report_path, "functional")

    def test_tooling_counts_come_from_the_successful_command_log(self):
        log = self.root / "unittest.log"
        log.write_text("test_scope ... ok\n\nRan 7 tests in 0.05s\n\nOK\n")
        report = self.root / "tooling.json"
        check = {"name": "python", "command": ["python3", "-m", "unittest"], "exit_code": 0, "log": str(log)}
        report.write_text(json.dumps({"checks": [check]}))
        self.assertEqual(evidence.tooling_evidence(report)["checks"][0]["tests"], 7)
        check["exit_code"] = 1
        report.write_text(json.dumps({"checks": [check]}))
        with self.assertRaisesRegex(ValueError, "Tooling command failed"):
            evidence.tooling_evidence(report)

    def test_vpm_payload_must_match_source_even_with_a_matching_listing_checksum(self):
        source, _ = self.add_package("First")
        repo = source.parents[2]
        output = self.root / "workflow"
        site = output / "site"
        site.mkdir(parents=True)
        archive = site / "First-1.0.0.zip"
        listing_path = site / "vpm.json"

        def write_archive(replacement=None):
            with ZipFile(archive, "w") as zipped:
                for current in source.iterdir():
                    data = replacement if current.name == "Behaviour.cs" and replacement is not None else current.read_bytes()
                    zipped.writestr(current.name, data)
            listing_path.write_text(json.dumps({"packages": {"First": {"versions": {
                "1.0.0": {"zipSHA256": evidence.sha256(archive)}}}}}))

        write_archive()
        (site / "index.html").write_text('<a href="vcc://vpm/addRepo?url=https://example.test/vpm.json">Add</a>')
        (output / "build.log").write_text("workflow completed")
        report = output / "report.json"
        report.write_text(json.dumps({"repo": str(repo), "scope": "Local fixture workflow", "packages": ["First"],
                                      "checks": ["workflow exit 0"]}))
        self.assertEqual(evidence.vpm_evidence(report)["package_count"], 1)
        write_archive(b"different compiled source")
        with self.assertRaisesRegex(ValueError, "payload differs"):
            evidence.vpm_evidence(report)

    def test_performance_methods_and_singleton_comparisons_use_the_supplied_measurements(self):
        def measurement(scenario, median):
            return {"plugin": "AnimatorOptimizer", "scenario": scenario, "size": 8, "samples": 9,
                    "median_ms": median, "p95_ms": median * 2}
        performance = {"editor_runs": [{"scope": "serial", "run_id_utc": "actual-run", "measurements": [
            measurement("automatic_retained_singleton_groups", 10), measurement("explicit_singleton_conversion", 5)]}],
            "graphics_runs": [], "player_runs": []}
        performance["measurement_protocols"] = evidence.measurement_protocols(performance)
        markdown = evidence.performance_markdown(performance)
        self.assertIn("共同预热 30 次，各测量函数再预热 30 次", markdown)
        self.assertIn("自动保留原图 10 ms，显式转换 5 ms，减少 50.0%", markdown)
        self.assertNotIn("AAP EntryOn", markdown)
        self.assertEqual(len(performance["measurement_protocols"]), 1)


if __name__ == "__main__":
    unittest.main()
