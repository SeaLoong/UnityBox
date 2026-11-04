using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.PrefabDiffPatcher.Tests
{
    public class PrefabDiffRegressionTests
    {
        private Scene scene;
        private GameObject a;
        private GameObject b;
        private string folder;

        [SetUp]
        public void SetUp()
        {
            // A preview scene leaves any open, unsaved scene and its active status intact.
            scene = EditorSceneManager.NewPreviewScene();
            a = new GameObject("Avatar");
            SceneManager.MoveGameObjectToScene(a, scene);
            b = new GameObject("Avatar");
            SceneManager.MoveGameObjectToScene(b, scene);
            folder = "Assets/__PrefabDiffTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void SelectedTransformCopiesLocalValuesAndKeepsChildren()
        {
            b.transform.localPosition = new Vector3(1, 2, 3);
            Child(a, "Kept");
            var result = Apply(new ComponentSelection("", "UnityEngine.Transform#0"));
            Assert.That(result.transform.localPosition, Is.EqualTo(b.transform.localPosition));
            Assert.That(result.transform.Find("Kept"), Is.Not.Null);
            Assert.That(a.transform.localPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ObjectSelectionDoesNotApplyAnUnselectedTransform()
        {
            var childA = Child(a, "Child");
            var childB = Child(b, "Child");
            childA.transform.localPosition = Vector3.one;
            childB.transform.localPosition = Vector3.one * 5;
            childB.SetActive(false);
            var result = PrefabDiffApplier.Apply(a, b, new[] { "Child" },
                Array.Empty<ComponentSelection>(), folder + "/Result.prefab");
            var child = result.transform.Find("Child");
            Assert.That(child.gameObject.activeSelf, Is.False);
            Assert.That(child.localPosition, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void UnselectedSubtreeRetainsLocalTransformUnderMovedParent()
        {
            a.transform.position = new Vector3(10, 20, 30);
            var child = Child(a, "Kept");
            child.transform.localPosition = new Vector3(1, 2, 3);
            a.AddComponent<Light>().intensity = 1;
            b.AddComponent<Light>().intensity = 2;
            var result = Apply(new ComponentSelection("", "UnityEngine.Light#0"));
            Assert.That(result.transform.Find("Kept").localPosition, Is.EqualTo(child.transform.localPosition));
        }

        [Test]
        public void BonesArrayReferencesAreRemappedIntoOutput()
        {
            var aBone = Child(a, "Bone").transform;
            var bBone = Child(b, "Bone").transform;
            a.AddComponent<SkinnedMeshRenderer>().bones = new[] { aBone };
            b.AddComponent<SkinnedMeshRenderer>().bones = new[] { bBone };
            var result = Apply(new ComponentSelection("", "UnityEngine.SkinnedMeshRenderer#0"));
            Assert.That(result.GetComponent<SkinnedMeshRenderer>().bones[0],
                Is.EqualTo(result.transform.Find("Bone")));
        }

        [Test]
        public void UnselectedDonorOnlyReferenceIsCleared()
        {
            a.AddComponent<SkinnedMeshRenderer>();
            var renderer = b.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { Child(b, "DonorOnly").transform };
            var result = Apply(new ComponentSelection("", "UnityEngine.SkinnedMeshRenderer#0"));
            Assert.That(result.transform.Find("DonorOnly"), Is.Null);
            Assert.That(result.GetComponent<SkinnedMeshRenderer>().bones[0], Is.Null);
        }

        [Test]
        public void NestedArraysAndSmallVectorChangesAreReported()
        {
            var boneA = Child(a, "BoneA").transform;
            Child(a, "BoneB");
            Child(b, "BoneA");
            var boneB = Child(b, "BoneB").transform;
            a.AddComponent<SkinnedMeshRenderer>().bones = new[] { boneA };
            b.AddComponent<SkinnedMeshRenderer>().bones = new[] { boneB };
            a.AddComponent<BoxCollider>().center = Vector3.zero;
            b.AddComponent<BoxCollider>().center = new Vector3(0.0001f, 0, 0);
            var diff = PrefabDiffAnalyzer.Analyze(a, b);
            Assert.That(diff.Kind, Is.EqualTo(DiffKind.Modified));
            Assert.That(diff.ComponentDiffs.Any(c => c.Key == "UnityEngine.SkinnedMeshRenderer#0"), Is.True);
            Assert.That(diff.ComponentDiffs.Single(c => c.Key == "UnityEngine.SkinnedMeshRenderer#0")
                .PropertyChanges.Any(change => change.Path == "m_Bones.Array.data[0]" && change.Before != change.After), Is.True);
            Assert.That(diff.ComponentDiffs.Any(c => c.Key == "UnityEngine.BoxCollider#0"), Is.True);
        }

        [Test]
        public void EquivalentLocalReferencesDoNotCreateDifferences()
        {
            a.AddComponent<SkinnedMeshRenderer>().bones = new[] { Child(a, "Bone").transform };
            b.AddComponent<SkinnedMeshRenderer>().bones = new[] { Child(b, "Bone").transform };
            var diff = PrefabDiffAnalyzer.Analyze(a, b);
            Assert.That(diff.HasAnyChanges, Is.False, DescribeChanges(diff));
        }

        [Test]
        public void ExposedReferenceBindingKeysRemainSignificant()
        {
            a.AddComponent<ExposedReferenceFixture>().target = new ExposedReference<GameObject>
                { exposedName = new PropertyName("ActorA"), defaultValue = Child(a, "Target") };
            b.AddComponent<ExposedReferenceFixture>().target = new ExposedReference<GameObject>
                { exposedName = new PropertyName("ActorB"), defaultValue = Child(b, "Target") };
            var diff = PrefabDiffAnalyzer.Analyze(a, b);
            var component = diff.ComponentDiffs.Single(c => c.Key ==
                "UnityBox.PrefabDiffPatcher.Tests.ExposedReferenceFixture#0");
            Assert.That(component.PropertyChanges.Any(change => change.Path == "target.exposedName" &&
                change.Before != change.After), Is.True, DescribeChanges(diff));
        }

        [Test]
        public void EquivalentExposedReferenceDefaultsDoNotCreateDifferences()
        {
            a.AddComponent<ExposedReferenceFixture>().target = new ExposedReference<GameObject>
                { exposedName = new PropertyName("Actor"), defaultValue = Child(a, "Target") };
            b.AddComponent<ExposedReferenceFixture>().target = new ExposedReference<GameObject>
                { exposedName = new PropertyName("Actor"), defaultValue = Child(b, "Target") };
            var diff = PrefabDiffAnalyzer.Analyze(a, b);
            Assert.That(diff.HasAnyChanges, Is.False, DescribeChanges(diff));
        }

        [Test]
        public void DuplicateAndReservedNamesHaveDistinctRoundTripPaths()
        {
            var objects = new[] { Child(a, "Bone"), Child(a, "Bone"), Child(a, "Bone[1]"),
                Child(a, "Part/Sub"), Child(a, "Part|Sub"), Child(a, "100%") };
            var map = PrefabDiffUtility.GetChildMap(a.transform);
            Assert.That(map.Count, Is.EqualTo(objects.Length));
            foreach (var pair in map)
                Assert.That(PrefabDiffUtility.FindByPath(a.transform, pair.Key), Is.EqualTo(pair.Value));
        }

        [Test]
        public void ReplacingOutputPreservesGuidAndRejectsInputAndTraversalPaths()
        {
            a.AddComponent<Light>();
            b.AddComponent<Light>();
            var first = Apply(new ComponentSelection("", "UnityEngine.Light#0"));
            string path = AssetDatabase.GetAssetPath(first);
            string guid = AssetDatabase.AssetPathToGUID(path);
            b.GetComponent<Light>().intensity = 3;
            Apply(new ComponentSelection("", "UnityEngine.Light#0"));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.Throws<InvalidOperationException>(() => PrefabDiffApplier.ValidateOutputPath(first, b, path));
            Assert.Throws<ArgumentException>(() => PrefabDiffApplier.ValidateOutputPath(a, b, "Assets/../escape.prefab"));
        }

        private static string DescribeChanges(DiffNode node)
        {
            return string.Join("\n", node.ObjectChanges.Select(change => node.Path + ": " + change)
                .Concat(node.ComponentDiffs.SelectMany(component => component.PropertyChanges.Select(change =>
                    node.Path + " / " + component.Key + " / " + change.Path + ": " + change.Before + " -> " + change.After)))
                .Concat(node.Children.Select(DescribeChanges)));
        }

        private GameObject Apply(params ComponentSelection[] components)
        {
            return PrefabDiffApplier.Apply(a, b, Array.Empty<string>(), components, folder + "/Result.prefab");
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
    }
}
