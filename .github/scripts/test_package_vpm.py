import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

from package_vpm import build_package


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name) / "source"
        self.source.mkdir()
        self.output = Path(self.temp.name) / "package.zip"
        self.write("package.json", json.dumps({"name": "top.sealoong.unitybox.avatar-security-system"}))

    def write(self, name, text="fixture"):
        path = self.source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def entries(self):
        build_package(self.source, self.output)
        with ZipFile(self.output) as archive:
            self.assertIsNone(archive.testzip())
            return {name: archive.read(name) for name in archive.namelist()}

    def test_preserves_unity_guids_and_editor_regression_tests(self):
        self.write("Runtime/Component.cs")
        self.write("Runtime/Component.cs.meta", "guid: 0123456789abcdef0123456789abcdef\n")
        self.write("Tests/Editor/Regression.cs")
        result = self.entries()
        self.assertEqual(result["Runtime/Component.cs.meta"], b"guid: 0123456789abcdef0123456789abcdef\n")
        self.assertIn("Tests/Editor/Regression.cs", result)

    def test_excludes_probe_scripts_and_generated_fixtures_with_metadata(self):
        assets = ["Editor/AnimationPathBindingProbe.cs", "Editor/AssetBundlePathBindingProbeBuilder.cs",
                  "Runtime/AnimationPathBindingProbe.cs", "Runtime/BundleProbeSceneRunner.cs",
                  "Tests/BundlePathProbe.prefab", "Tests/Bundles/ass_path_probe"]
        for asset in assets:
            self.write(asset)
            self.write(asset + ".meta")
        self.assertEqual(set(self.entries()), {"package.json"})

    def test_excludes_generated_folders_at_any_depth(self):
        for asset in ["Generated/a.anim", "Editor/Generated/a.anim", "Generated.meta"]:
            self.write(asset)
        self.assertEqual(set(self.entries()), {"package.json"})

    def test_rebuild_drops_removed_files(self):
        self.write("obsolete.cs")
        self.assertIn("obsolete.cs", self.entries())
        (self.source / "obsolete.cs").unlink()
        self.assertNotIn("obsolete.cs", self.entries())

    def test_other_packages_keep_their_test_assets(self):
        self.write("package.json", json.dumps({"name": "example.other"}))
        self.write("Tests/Example.prefab")
        self.assertIn("Tests/Example.prefab", self.entries())


if __name__ == "__main__":
    unittest.main()
