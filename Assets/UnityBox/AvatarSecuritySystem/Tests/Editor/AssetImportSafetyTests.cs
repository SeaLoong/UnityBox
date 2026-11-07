using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class AssetImportSafetyTests
    {
        private const string ComponentGuid = "1c51e6a139920bb4ca12700dc82627ff";
        private const string LegacyGuid = "70959987f069e36d39b8a69869eef7f5";
        private string folder;
        private SceneSetup[] previousScenes;

        [SetUp]
        public void SetUp()
        {
            previousScenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            folder = "Assets/__ASSImportSafety_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(folder);
            if (previousScenes.Length > 0 && previousScenes.All(item => !string.IsNullOrEmpty(item.path)))
                EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
        }

        private static GameObject CreateConfiguration()
        {
            var root = new GameObject("Stored ASS configuration");
            var component = root.AddComponent<ASSComponent>();
            component.gestureHoldTime = 0.15f;
            component.gestureErrorTolerance = 0.3f;
            component.gestureMaxHoldTime = 0.1f;
            component.countdownDuration = 12f;
            component.warningThreshold = 17f;
            component.defaultEnableDefense = true;
            component.disableDefense = true;
            return root;
        }

        private static void AssertStoredConfiguration(ASSComponent component)
        {
            Assert.That(component.gestureHoldTime, Is.EqualTo(0.15f));
            Assert.That(component.gestureErrorTolerance, Is.EqualTo(0.3f));
            Assert.That(component.gestureMaxHoldTime, Is.EqualTo(0.1f));
            Assert.That(component.warningThreshold, Is.EqualTo(17f));
            Assert.That(component.defaultEnableDefense, Is.True);
            Assert.That(component.disableDefense, Is.True);
        }

        private static void Import(string path) => AssetDatabase.ImportAsset(path,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        [Test]
        public void LoadedConfigurationIsUnchangedWhileBuildCopiesHaveEffectiveValues()
        {
            string path = folder + "/Stored.prefab";
            var root = CreateConfiguration();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            byte[] original = File.ReadAllBytes(path);
            byte[] metadata = File.ReadAllBytes(path + ".meta");
            Import(path);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var component = loaded.GetComponent<ASSComponent>();
                AssertStoredConfiguration(component);
                var config = ASSConfigData.FromComponent(component);
                Assert.That(config.gestureErrorTolerance, Is.EqualTo(0.14f).Within(0.00001f));
                Assert.That(config.gestureMaxHoldTime, Is.EqualTo(1f));
                Assert.That(config.warningThreshold, Is.EqualTo(6f));
                Assert.That(config.disableDefense, Is.False);
                AssertStoredConfiguration(component);
                Assert.That(typeof(ASSComponent).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata));
        }

        [UnityTest]
        public IEnumerator ImportingALegacyPrefabDoesNotRepairItOrReloadItsDirtyScene()
        {
            string path = folder + "/Legacy.prefab";
            var source = CreateConfiguration();
            PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            File.WriteAllText(path, File.ReadAllText(path).Replace(ComponentGuid, LegacyGuid));
            byte[] original = File.ReadAllBytes(path);
            byte[] metadata = File.ReadAllBytes(path + ".meta");
            Import(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            string scenePath = folder + "/Editing.unity";
            Assert.That(EditorSceneManager.SaveScene(instance.scene, scenePath), Is.True);
            instance.transform.localPosition = new Vector3(4, 5, 6);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(instance.scene);
            int instanceId = instance.GetInstanceID();
            Import(path);
            for (int frame = 0; frame < 4; frame++) yield return null;
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetInstanceID(), Is.EqualTo(instanceId));
            Assert.That(instance.transform.localPosition, Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(instance.scene.isDirty, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata));
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OpeningALegacySceneDoesNotRewriteItsScriptReference()
        {
            string path = folder + "/Legacy.unity";
            var source = CreateConfiguration();
            Assert.That(EditorSceneManager.SaveScene(source.scene, path), Is.True);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.WriteAllText(path, File.ReadAllText(path).Replace(ComponentGuid, LegacyGuid));
            byte[] original = File.ReadAllBytes(path);
            byte[] metadata = File.ReadAllBytes(path + ".meta");
            Import(path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().Single(item => item.name == "Stored ASS configuration");
            int instanceId = root.GetInstanceID();
            for (int frame = 0; frame < 4; frame++) yield return null;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetInstanceID(), Is.EqualTo(instanceId));
            Assert.That(scene.isDirty, Is.False);
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root), Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata));
        }
    }
}
