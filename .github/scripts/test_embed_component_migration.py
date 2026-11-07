"""Check owner-embedded sources and their stable Unity asset identities."""
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location("embed_component_migration", Path(__file__).with_name("embed_component_migration.py"))
embed = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(embed)


class EmbeddedMigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)

    def test_catalogs_namespaces_menus_and_backups_are_owned_by_each_plugin(self):
        files = embed.rendered_files()
        self.assertEqual(len(files), 13)
        all_types = {item["name"] for item in embed.COMPONENTS.values()}
        for owner in embed.OWNERS:
            with self.subTest(owner=owner["name"]):
                root = Path("Assets/UnityBox") / owner["name"]
                engine = files[root / owner["editor"] / "MigrationEngine.cs"]
                window = files[root / owner["editor"] / "ComponentReferenceMigrationWindow.cs"]
                owned_types = {embed.COMPONENTS[key]["name"] for key in owner["types"]}
                for name in owned_types:
                    self.assertIn('Name = "' + name + '"', engine)
                for name in all_types - owned_types:
                    self.assertNotIn(name, engine)
                self.assertEqual(engine.count("new Rule {"), len(owned_types))
                self.assertIn("namespace UnityBox." + owner["name"] + ".ComponentMigration", engine)
                self.assertIn("UserSettings/UnityBox/" + owner["name"] + "/ComponentReferenceMigration/", engine)
                self.assertIn('[MenuItem("Assets/UnityBox/' + owner["display"] + '/Repair Component References")]', window)
                self.assertIn('GetWindow<ComponentReferenceMigrationWindow>("' + owner["abbr"] + ' Component References")', window)
                self.assertIn("window.UseProjectSelection();", window)
                self.assertNotIn("UnityBox.ComponentReferenceMigration", engine + window)
                self.assertNotRegex(engine + window, r"\[InitializeOnLoad|AssetPostprocessor|OnValidate\(")

    def test_generated_sources_use_existing_package_assemblies_without_new_packages_or_asmdefs(self):
        actual_repo = Path(__file__).resolve().parents[2]
        expected = {
            "AdvancedCostumeController": ("UnityBox.AdvancedCostumeController.Core", "UnityBox.AdvancedCostumeController.Core.Tests"),
            "AnimatorOptimizer": ("UnityBox.AnimatorOptimizer", "UnityBox.AnimatorOptimizer.Tests"),
            "AvatarSecuritySystem": ("UnityBox.AvatarSecuritySystem", "UnityBox.AvatarSecuritySystem.Editor.Tests"),
            "VisemeBlendshapeOverride": ("UnityBox.VisemeBlendshapeOverride", "UnityBox.VisemeBlendshapeOverride.Editor.Tests"),
        }
        files = embed.rendered_files()
        self.assertTrue(all(path.suffix == ".cs" for path in files))
        for owner in embed.OWNERS:
            root = actual_repo / "Assets/UnityBox" / owner["name"]
            for relative, name in zip((owner["editor"], owner["tests"]), expected[owner["name"]]):
                assembly_parent = (root / relative).parent
                assemblies = [json.loads(path.read_text())["name"] for path in assembly_parent.glob("*.asmdef")]
                self.assertEqual(assemblies, [name])

    def test_write_and_check_preserve_existing_metadata_and_create_unique_stable_guids(self):
        self.assertEqual(embed.synchronize(self.repo, check=False), [])
        self.assertEqual(embed.synchronize(self.repo), [])
        files = embed.rendered_files()
        metadata = embed.metadata_paths(files)
        guids = []
        for relative, folder in metadata.items():
            path = self.repo / relative
            original = path.read_bytes()
            self.assertEqual(original.decode(), embed.new_metadata(Path(str(relative).removesuffix(".meta")), folder))
            guids.append(re.search(rb"guid: ([a-f0-9]{32})", original)[1])
        self.assertEqual(len(guids), len(set(guids)))
        first = self.repo / next(iter(metadata))
        original = first.read_text()
        custom = re.sub(r"guid: [a-f0-9]{32}", "guid: fedcba9876543210fedcba9876543210", original)
        first.write_text(custom)
        self.assertEqual(embed.synchronize(self.repo, check=False), [])
        self.assertEqual(first.read_text(), custom)
        self.assertEqual(embed.synchronize(self.repo), [])

    def test_check_rejects_stale_sources_missing_metadata_and_duplicate_identity(self):
        self.assertEqual(embed.synchronize(self.repo, check=False), [])
        files = embed.rendered_files()
        relative = next(iter(files))
        (self.repo / relative).write_text("// unsynchronized source\n")
        self.assertTrue(any(relative.as_posix() in problem for problem in embed.synchronize(self.repo)))
        self.assertEqual(embed.synchronize(self.repo, check=False), [])
        metadata = list(embed.metadata_paths(files))
        (self.repo / metadata[0]).unlink()
        self.assertTrue(any("Missing generated asset metadata" in problem for problem in embed.synchronize(self.repo)))
        self.assertEqual(embed.synchronize(self.repo, check=False), [])
        (self.repo / metadata[1]).write_bytes((self.repo / metadata[0]).read_bytes())
        self.assertTrue(any("Duplicate generated asset GUID" in problem for problem in embed.synchronize(self.repo)))

    def test_native_suites_are_owner_local_and_only_ass_contains_scanner_performance(self):
        files = embed.rendered_files()
        for owner in embed.OWNERS:
            root = Path("Assets/UnityBox") / owner["name"]
            test = files[root / owner["tests"] / "ComponentMigrationTests.cs"]
            self.assertIn('private const string Primary = "' + embed.COMPONENTS[owner["primary"]]["name"] + '";', test)
            self.assertIn("PeerPluginSchemaIsReportedWithoutOfferingAnOwnerTypeMapping", test)
            self.assertIn("ExplicitOverridesCannotSelectAnotherPluginsComponentType", test)
            self.assertIn("PrefabStageUnsavedNestedPrefabAndObjectDependenciesArePreserved", test)
            self.assertIn("中文名称与 emoji 🙂", test)
            self.assertNotIn("UNITYBOX_MIGRATION_ASS_TESTS", test)
            self.assertEqual("GenericPartGroupFieldsRequireExplicitIdentity" in test, owner["name"] == "AdvancedCostumeController")
        performance = [(path, text) for path, text in files.items() if path.name.endswith("PerformanceTests.cs")]
        self.assertEqual(len(performance), 1)
        self.assertIn("AvatarSecuritySystem", performance[0][0].parts)
        self.assertIn('public string plugin = "AvatarSecuritySystem";', performance[0][1])
        self.assertIn('public string scenario = "scan_component_references";', performance[0][1])
        for size in (128, 1024, 4096):
            self.assertIn("[TestCase(" + str(size) + ")]", performance[0][1])


if __name__ == "__main__":
    unittest.main()
