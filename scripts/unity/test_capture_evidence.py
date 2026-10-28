"""Source snapshots and saved execution evidence must still match their raw inputs."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

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


if __name__ == "__main__":
    unittest.main()
