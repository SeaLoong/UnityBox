using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.PrefabDiffPatcher.Tests
{
    public class PrefabAdvancedWorkflowTests
    {
        private Scene scene;
        private GameObject a, b;
        private string folder;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            a = Create("Avatar"); b = Create("Avatar");
            folder = "Assets/__PrefabAdvanced_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void AssetInputKeepsVariantAncestryNestedLinksAndSelectedOverridesAfterReload()
        {
            var nested = Create("Nested");
            nested.AddComponent<BoxCollider>().size = new Vector3(2, 3, 4);
            string nestedPath = folder + "/Nested.prefab";
            var nestedAsset = PrefabUtility.SaveAsPrefabAsset(nested, nestedPath);
            UnityEngine.Object.DestroyImmediate(nested);
            var nestedInstance = (GameObject)PrefabUtility.InstantiatePrefab(nestedAsset, scene);
            nestedInstance.transform.SetParent(a.transform, false);
            a.AddComponent<Light>().intensity = 1;
            b.AddComponent<Light>().intensity = 4;
            var baseAsset = PrefabUtility.SaveAsPrefabAsset(a, folder + "/Base.prefab");
            var variantInstance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset, scene);
            variantInstance.GetComponent<Light>().range = 21;
            PrefabUtility.RecordPrefabInstancePropertyModifications(variantInstance.GetComponent<Light>());
            string variantPath = folder + "/Variant.prefab";
            var variant = PrefabUtility.SaveAsPrefabAsset(variantInstance, variantPath);
            var donor = PrefabUtility.SaveAsPrefabAsset(b, folder + "/Donor.prefab");
            string outputPath = folder + "/Result.prefab";
            var result = PrefabDiffApplier.Apply(variant, donor, Array.Empty<string>(),
                new[] { new ComponentSelection("", "UnityEngine.Light#0") }, outputPath);
            Assert.That(PrefabUtility.GetPrefabAssetType(result), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(result)),
                Is.EqualTo(variantPath));

            var loaded = PrefabUtility.LoadPrefabContents(outputPath);
            try
            {
                Assert.That(loaded.GetComponent<Light>().intensity, Is.EqualTo(4));
                var child = loaded.transform.Find("Nested").gameObject;
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child), Is.EqualTo(nestedPath));
                Assert.That(child.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(variant.GetComponent<Light>().intensity, Is.EqualTo(1));
                Assert.That(variant.GetComponent<Light>().range, Is.EqualTo(21));
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplyLeavesAnIndependentlySavedCleanUserSceneUnchanged(bool usePrefabAssets)
        {
            a.AddComponent<Light>().intensity = 1; b.AddComponent<Light>().intensity = 2;
            var sourceA = usePrefabAssets ? PrefabUtility.SaveAsPrefabAsset(a, folder + "/SceneStateA.prefab") : a;
            var sourceB = usePrefabAssets ? PrefabUtility.SaveAsPrefabAsset(b, folder + "/SceneStateB.prefab") : b;
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            // The Test Runner starts a batch run in its own untitled scene. Open a
            // dedicated scene in Single mode, then restore the runner's scene setup.
            var cleanScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                SceneManager.SetActiveScene(cleanScene);
                var marker = new GameObject("UserSceneMarker");
                marker.AddComponent<Light>().intensity = 7;
                Assert.That(EditorSceneManager.SaveScene(cleanScene, folder + "/CleanUserScene.unity"), Is.True);
                Assert.That(cleanScene.isDirty, Is.False, "The fixture starts with a saved, clean scene.");
                int roots = cleanScene.rootCount;
                int loadedScenes = SceneManager.sceneCount;
                PrefabDiffApplier.Apply(sourceA, sourceB, Array.Empty<string>(),
                    new[] { new ComponentSelection("", "UnityEngine.Light#0") }, folder + "/SceneStateOutput.prefab");
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(cleanScene));
                Assert.That(cleanScene.isDirty, Is.False);
                Assert.That(cleanScene.rootCount, Is.EqualTo(roots));
                Assert.That(SceneManager.sceneCount, Is.EqualTo(loadedScenes));
                Assert.That(marker.GetComponent<Light>().intensity, Is.EqualTo(7));
            }
            finally
            {
                if (originalSetup.Length == 0 || originalSetup.Length == 1 && string.IsNullOrEmpty(originalSetup[0].path))
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                else
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }
        }

        [Test]
        public void RootShellStaysWithBaseWhileRootComponentsRemainSelectable()
        {
            a.layer = 1; b.layer = 2;
            a.AddComponent<Light>().intensity = 1; b.AddComponent<Light>().intensity = 3;
            var result = PrefabDiffApplier.Apply(a, b, new[] { "" },
                new[] { new ComponentSelection("", "UnityEngine.Light#0") }, folder + "/Root.prefab");
            Assert.That(result.layer, Is.EqualTo(1));
            Assert.That(result.GetComponent<Light>().intensity, Is.EqualTo(3));
        }

        [Test]
        public void RectTransformSelectionPersistsAllLayoutPropertiesWithItsParent()
        {
            var left = new GameObject("Panel", typeof(RectTransform));
            var right = new GameObject("Panel", typeof(RectTransform));
            left.transform.SetParent(a.transform, false); right.transform.SetParent(b.transform, false);
            var donor = (RectTransform)right.transform;
            donor.anchorMin = new Vector2(.1f, .2f); donor.anchorMax = new Vector2(.8f, .9f);
            donor.pivot = new Vector2(.3f, .6f); donor.sizeDelta = new Vector2(250, 100);
            donor.anchoredPosition3D = new Vector3(15, 30, 4);
            donor.localRotation = Quaternion.Euler(0, 0, 20); donor.localScale = Vector3.one * .8f;
            var output = PrefabDiffApplier.Apply(a, b, Array.Empty<string>(),
                new[] { new ComponentSelection("Panel", "UnityEngine.RectTransform#0") }, folder + "/Layout.prefab");
            var actual = (RectTransform)output.transform.Find("Panel");
            Assert.That(actual.parent, Is.EqualTo(output.transform));
            Assert.That(actual.anchorMin, Is.EqualTo(donor.anchorMin));
            Assert.That(actual.anchorMax, Is.EqualTo(donor.anchorMax));
            Assert.That(actual.pivot, Is.EqualTo(donor.pivot));
            Assert.That(actual.sizeDelta, Is.EqualTo(donor.sizeDelta));
            Assert.That(actual.anchoredPosition3D, Is.EqualTo(donor.anchoredPosition3D));
            Assert.That(Quaternion.Angle(actual.localRotation, donor.localRotation), Is.LessThan(.001f));
            Assert.That(actual.localScale, Is.EqualTo(donor.localScale));
        }

        [Test]
        public void CyclicManagedGraphAndCustomSerializationReferencesRemapAndReload()
        {
            a.AddComponent<ExposedReferenceFixture>();
            var source = b.AddComponent<ExposedReferenceFixture>();
            var boneA = Create("Bone"); boneA.transform.SetParent(a.transform, false);
            var boneB = Create("Bone"); boneB.transform.SetParent(b.transform, false);
            source.graph = new ReferenceGraph { label = "cycle", target = boneB };
            source.graph.next = source.graph; source.graph.alias = source.graph;
            source.lookup[7] = boneB.transform;
            source.target = new ExposedReference<GameObject>
                { exposedName = new PropertyName("Actor"), defaultValue = boneB };
            string path = folder + "/References.prefab";
            PrefabDiffApplier.Apply(a, b, Array.Empty<string>(),
                new[] { new ComponentSelection("", "UnityBox.PrefabDiffPatcher.Tests.ExposedReferenceFixture#0") }, path);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actual = loaded.GetComponent<ExposedReferenceFixture>();
                var bone = loaded.transform.Find("Bone");
                Assert.That(actual.graph.label, Is.EqualTo("cycle"));
                Assert.That(actual.graph.target, Is.EqualTo(bone.gameObject));
                Assert.That(actual.graph.next, Is.SameAs(actual.graph));
                Assert.That(actual.graph.alias, Is.SameAs(actual.graph));
                Assert.That(actual.lookup[7], Is.EqualTo(bone));
                Assert.That(actual.target.defaultValue, Is.EqualTo(bone.gameObject));
                Assert.That(actual.target.exposedName, Is.EqualTo(new PropertyName("Actor")));
                Assert.That(source.graph.target, Is.EqualTo(boneB));
                Assert.That(source.lookup[7], Is.EqualTo(boneB.transform));
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }

        private GameObject Create(string name)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
        }
    }
}
