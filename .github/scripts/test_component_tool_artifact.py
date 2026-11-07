import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

from component_tool_artifact import FILENAME, SOURCE, publish_component_tool


class ComponentToolArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name) / "repo"
        self.output = Path(self.temporary.name) / "site"
        self.asset = self.repo / SOURCE / "Tool.cs"
        self.guid = "123456789abcdef0123456789abcdef0"

    def fixture(self, extra=False):
        self.asset.parent.mkdir(parents=True)
        self.asset.write_bytes(b"public class Tool {}\n")
        metadata = ("fileFormatVersion: 2\nguid: " + self.guid + "\n").encode()
        Path(str(self.asset) + ".meta").write_bytes(metadata)
        entries = {self.guid + "/pathname": (SOURCE + "/Tool.cs").encode(),
                   self.guid + "/asset": self.asset.read_bytes(), self.guid + "/asset.meta": metadata}
        if extra:
            other = "223456789abcdef0123456789abcdef0"
            entries.update({other + "/pathname": b"Assets/Unrelated.cs", other + "/asset": b"other",
                            other + "/asset.meta": ("guid: " + other + "\n").encode()})
        archive = self.repo / "Tools" / FILENAME
        archive.parent.mkdir(parents=True)
        with tarfile.open(archive, "w:gz") as exported:
            for name, data in entries.items():
                member = tarfile.TarInfo(name)
                member.size = len(data)
                exported.addfile(member, io.BytesIO(data))
        return archive

    def test_repository_without_the_tool_keeps_existing_listing_behavior(self):
        self.assertIsNone(publish_component_tool(self.repo, self.output))
        self.assertFalse(self.output.exists())

    def test_exported_tool_is_published_with_verified_source_and_metadata(self):
        archive = self.fixture()
        report = publish_component_tool(self.repo, self.output)
        self.assertEqual((self.output / FILENAME).read_bytes(), archive.read_bytes())
        self.assertEqual(json.loads((self.output / "component-references.json").read_text()), report)
        self.assertEqual(report["source_files"][0]["path"], SOURCE + "/Tool.cs")

    def test_stale_installer_is_rejected_before_publication(self):
        self.fixture()
        self.asset.write_bytes(b"public class UpdatedTool {}\n")
        with self.assertRaisesRegex(ValueError, "payload differs"):
            publish_component_tool(self.repo, self.output)
        self.assertFalse((self.output / FILENAME).exists())

    def test_unrelated_assets_are_not_shipped_in_the_editor_installer(self):
        self.fixture(extra=True)
        with self.assertRaisesRegex(ValueError, "Unexpected asset"):
            publish_component_tool(self.repo, self.output)
        self.assertFalse((self.output / FILENAME).exists())
