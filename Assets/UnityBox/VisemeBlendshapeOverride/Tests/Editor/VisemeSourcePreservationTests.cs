using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeSourcePreservationTests
    {
        private string folder;
        private Scene scene;
        private GameObject root;
        private VisemeBlendshapeOverrideComponent component;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__VisemeSourcePreservation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Stored viseme configuration");
            SceneManager.MoveGameObjectToScene(root, scene);
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            mesh.AddBlendShapeFrame("A", 100f, new[] { Vector3.up }, null, null);
            AssetDatabase.CreateAsset(mesh, folder + "/Face.asset");
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            component = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            component.targetRenderer = renderer;
            component.globalWeight = 180f;
            component.voiceMin = 0.9f;
            component.voiceMax = 0.2f;
            component.bindings = new List<VisemeBlendshapeOverrideComponent.VisemeBinding>
            {
                new VisemeBlendshapeOverrideComponent.VisemeBinding
                {
                    viseme = VisemeBlendshapeOverrideComponent.Viseme.PP,
                    blendshapeName = "A",
                    useCustomSettings = true,
                    weight = 140f,
                    voiceMode = VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Linear,
                    voiceMin = 0.8f,
                    voiceMax = 0.2f,
                },
                new VisemeBlendshapeOverrideComponent.VisemeBinding
                {
                    viseme = VisemeBlendshapeOverrideComponent.Viseme.PP,
                    blendshapeName = "Duplicate row retained in source",
                    weight = -12f,
                },
            };
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        private static void AssertStoredValues(VisemeBlendshapeOverrideComponent value)
        {
            Assert.That(value.globalWeight, Is.EqualTo(180f));
            Assert.That(value.voiceMin, Is.EqualTo(0.9f));
            Assert.That(value.voiceMax, Is.EqualTo(0.2f));
            Assert.That(value.bindings, Has.Count.EqualTo(2));
            Assert.That(value.bindings[0].weight, Is.EqualTo(140f));
            Assert.That(value.bindings[0].voiceMax, Is.EqualTo(0.2f));
            Assert.That(value.bindings[1].blendshapeName, Is.EqualTo("Duplicate row retained in source"));
            Assert.That(value.bindings[1].weight, Is.EqualTo(-12f));
        }

        [Test]
        public void SavedPrefabReloadAndOpeningInspectorPreserveStoredRowsAndMetadata()
        {
            string path = folder + "/Stored.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            byte[] original = File.ReadAllBytes(path);
            byte[] metadata = File.ReadAllBytes(path + ".meta");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            UnityEditor.Editor inspector = null;
            try
            {
                var saved = loaded.GetComponent<VisemeBlendshapeOverrideComponent>();
                AssertStoredValues(saved);
                string serialized = EditorJsonUtility.ToJson(saved);
                inspector = UnityEditor.Editor.CreateEditor(saved);
                Assert.That(inspector, Is.TypeOf<VisemeBlendshapeOverrideEditor>());
                AssertStoredValues(saved);
                Assert.That(EditorJsonUtility.ToJson(saved), Is.EqualTo(serialized));
                Assert.That(typeof(VisemeBlendshapeOverrideComponent).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                PrefabUtility.UnloadPrefabContents(loaded);
            }
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata));
        }

        [Test]
        public void BuildPlanUsesIndependentNormalizedRowsWithoutChangingSourceConfiguration()
        {
            string original = EditorJsonUtility.ToJson(component);
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            Assert.That(plan.Config, Is.SameAs(component), "The source identity is retained for build-clone cleanup only.");
            Assert.That(plan.Settings.bindings, Is.Not.SameAs(component.bindings));
            var copied = plan.Settings.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP);
            Assert.That(copied, Is.Not.SameAs(component.bindings[0]));
            Assert.That(plan.Settings.bindings, Has.Count.EqualTo(15));
            Assert.That(copied.weight, Is.EqualTo(100f));
            Assert.That(copied.voiceMax, Is.GreaterThan(copied.voiceMin));
            var resolved = plan.ResolvedBindings.Single(row => row.Viseme == VisemeBlendshapeOverrideComponent.Viseme.PP);
            Assert.That(resolved.Weight, Is.EqualTo(100f));
            Assert.That(resolved.VoiceMax, Is.GreaterThan(resolved.VoiceMin));
            AssertStoredValues(component);
            Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(original));
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(folder + "/Generated.controller");
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
            AssertStoredValues(component);
            Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(original));
        }

        [Test]
        public void ExplicitRowCreationSupportsUndoRedoWithoutNormalizingOtherStoredRows()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            VisemeBlendshapeOverrideEditor.AddEditableBinding(component, VisemeBlendshapeOverrideComponent.Viseme.FF);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Assert.That(component.bindings, Has.Count.EqualTo(3));
            Assert.That(component.bindings[2].viseme, Is.EqualTo(VisemeBlendshapeOverrideComponent.Viseme.FF));
            Assert.That(component.bindings[0].weight, Is.EqualTo(140f));
            Undo.PerformUndo();
            AssertStoredValues(component);
            Undo.PerformRedo();
            Assert.That(component.bindings, Has.Count.EqualTo(3));
            Assert.That(component.bindings[2].viseme, Is.EqualTo(VisemeBlendshapeOverrideComponent.Viseme.FF));
            Assert.That(component.bindings[1].weight, Is.EqualTo(-12f));
        }
    }
}
