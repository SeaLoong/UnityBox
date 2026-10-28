"""Lock/cache/network policy tests; these fixtures do not emulate Unity or SDKs."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import urllib.error

SPEC = importlib.util.spec_from_file_location("unitybox_fetch", Path(__file__).with_name("fetch_dependencies.py"))
fetch = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(fetch)


class DependencyLockTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.path = self.root / "dependencies.lock.json"
        complete = fetch.read_json(Path(__file__).with_name("dependencies.lock.json"))
        packages, modules = fetch.profile_packages(complete, "core")
        self.core = {**complete, "profiles": {"core": fetch.CORE_ROOTS.copy()}, "prepared_profiles": ["core"],
                     "packages": {name: complete["packages"][name] for name in packages},
                     "modules": {name: complete["modules"][name] for name in modules}}
        self.path.write_text(json.dumps(self.core), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_core_lock_is_complete_but_does_not_claim_vrc(self):
        fetch.validate_lock(self.core)
        packages, modules = fetch.profile_packages(self.core, "core")
        self.assertEqual(packages, set(fetch.CORE_ROOTS))
        self.assertEqual(modules, {"com.unity.modules.imgui", "com.unity.modules.jsonserialize"})
        with self.assertRaisesRegex(ValueError, "not prepared"):
            fetch.profile_packages(self.core, "vrc")

    def test_incomplete_hash_graph_and_root_version_are_rejected(self):
        for mutation in (
            lambda lock: lock["packages"].pop("com.unity.ext.nunit"),
            lambda lock: lock["packages"]["com.unity.ext.nunit"].pop("tree_sha256"),
            lambda lock: lock["packages"]["com.unity.ext.nunit"].update(version="1.0.7"),
            lambda lock: lock.update(prepared_profiles=["core", "vrc"]),
        ):
            broken = copy.deepcopy(self.core)
            mutation(broken)
            with self.assertRaises(ValueError):
                fetch.validate_lock(broken)

    def test_core_update_preserves_previously_completed_vrc_records(self):
        old = copy.deepcopy(self.core)
        old["profiles"]["vrc"] = fetch.VRC_ROOTS.copy()
        old["prepared_profiles"] = ["core", "vrc"]
        for name, version in fetch.VRC_ROOTS.items():
            old["packages"].setdefault(name, {"version": version, "source": "github-release",
                "archive": name + ".zip", "sha256": "a" * 64, "tree_sha256": "b" * 64,
                "dependencies": {}, "vpmDependencies": {}})
        self.path.write_text(json.dumps(old), encoding="utf-8")
        fetcher = mock.Mock()
        fetcher.resolve_graph.return_value = (self.core["packages"], self.core["modules"])
        updated = fetch.update_lock(fetcher, self.path, "core")
        fetcher.resolve_graph.assert_called_once_with(fetch.CORE_ROOTS)
        self.assertEqual(updated, old)

    def test_failed_vrc_expansion_leaves_core_lock_byte_for_byte_unchanged(self):
        before = self.path.read_bytes()
        fetcher = mock.Mock()
        fetcher.resolve_graph.side_effect = urllib.error.HTTPError("https://cdn.packages.unity.com/blocked", 403, "Forbidden", {}, None)
        with self.assertRaises(urllib.error.HTTPError):
            fetch.update_lock(fetcher, self.path, "vrc")
        self.assertEqual(self.path.read_bytes(), before)
        self.assertFalse(self.path.with_suffix(".json.partial").exists())

    def test_explicit_update_can_change_selected_root_pins(self):
        roots = {**fetch.CORE_ROOTS, "com.unity.test-framework": "1.1.34"}
        packages = copy.deepcopy(self.core["packages"])
        packages["com.unity.test-framework"]["version"] = "1.1.34"
        fetcher = mock.Mock()
        fetcher.resolve_graph.return_value = (packages, self.core["modules"])
        with mock.patch.object(fetch, "CORE_ROOTS", roots):
            with self.assertRaisesRegex(ValueError, "root pins differ"):
                fetch.validate_lock(self.core)
            updated = fetch.update_lock(fetcher, self.path, "core")
            fetch.validate_lock(updated)
        self.assertEqual(updated["profiles"]["core"], roots)

    def test_editor_builtin_package_copies_sources_and_checks_tree(self):
        fetcher = fetch.Fetcher(self.root / "cache", self.root / "Editor/Unity")
        package = fetcher.builtin("fixture.ui")
        (package / "Runtime").mkdir(parents=True)
        (package / "package.json").write_text(json.dumps({"name": "fixture.ui", "version": "1.0.0"}))
        (package / "Runtime/Widget.cs").write_text("// source fixture")
        entry = fetcher.resolve("fixture.ui", "1.0.0")
        self.assertEqual(entry["source"], "editor-builtin")
        self.assertTrue((fetcher.cache / "fixture.ui/Runtime/Widget.cs").is_file())
        self.assertEqual(entry["tree_sha256"], fetch.tree_digest(package))
        fetcher.install("fixture.ui", entry)
        (package / "Runtime/Widget.cs").write_text("// changed source fixture")
        with self.assertRaisesRegex(ValueError, "builtin differs"):
            fetcher.install("fixture.ui", entry)

    def test_modified_archive_and_extracted_contents_are_rejected(self):
        cache = self.root / "cache"
        fetcher = fetch.Fetcher(cache, self.root / "Editor/Unity")
        archive = cache / "fixture.tgz"
        archive.write_bytes(b"archive checksum fixture")
        target = cache / "fixture.package"
        target.mkdir()
        (target / "package.json").write_text(json.dumps({"name": "fixture.package", "version": "1.0.0"}))
        entry = {"source": "editor-bundled", "version": "1.0.0", "archive": archive.name,
                 "sha256": fetch.digest(archive), "tree_sha256": fetch.tree_digest(target),
                 "dependencies": {}, "vpmDependencies": {}}
        archive.write_bytes(b"corrupt archive")
        with self.assertRaisesRegex(ValueError, "sha256 mismatch"):
            fetcher.install("fixture.package", entry)
        archive.write_bytes(b"archive checksum fixture")
        (target / "unexpected.txt").write_text("modified")
        with self.assertRaisesRegex(ValueError, "contents changed"):
            fetcher.install("fixture.package", entry)

    def test_http_denial_is_not_retried_but_temporary_timeout_is(self):
        denied = urllib.error.HTTPError("https://cdn.packages.unity.com/blocked", 403, "Forbidden", {}, None)
        with mock.patch.object(fetch.urllib.request, "urlopen", side_effect=denied) as opening, mock.patch.object(fetch.time, "sleep") as sleeping:
            with self.assertRaises(urllib.error.HTTPError):
                fetch.download("https://cdn.packages.unity.com/blocked", self.root / "out")
            self.assertEqual(opening.call_count, 1)
            sleeping.assert_not_called()
        with mock.patch.object(fetch.urllib.request, "urlopen", side_effect=urllib.error.URLError(TimeoutError())) as opening, mock.patch.object(fetch.time, "sleep"):
            with self.assertRaises(urllib.error.URLError):
                fetch.download("https://packages.unity.com/temporary", self.root / "out")
            self.assertEqual(opening.call_count, 3)


if __name__ == "__main__":
    unittest.main()
