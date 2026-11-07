using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerConfigurationPreservationTests
    {
        private string folder;
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__AOConfigurationPreservation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            root = new GameObject("Stored optimizer configuration");
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(folder);
        }

        private AnimatorOptimizerComponent CreateStoredConfiguration()
        {
            var component = root.AddComponent<AnimatorOptimizerComponent>();
            component.generatedLayerName = "  Stored layer  ";
            component.weightParameterPrefix = "  Stored/Weight  ";
            component.layerIndices = new List<int> { 3, -1, 3, 1 };
            component.maxPriorityCombinations = 0;
            component.maxAapDecisionNodes = 20000;
            component.heuristics = (AnimatorOptimizerComponent.HeuristicFlags)255;
            component.shortAnimationMaxFrames = 99;
            component.shortAnimationMaxSeconds = 0.8f;
            component.motionTimeSampleCount = 1;
            component.writeDefaultsPolicy = (AnimatorOptimizerComponent.WriteDefaultsPolicy)99;
            return component;
        }

        private static void AssertStoredConfiguration(AnimatorOptimizerComponent component)
        {
            Assert.That(component.generatedLayerName, Is.EqualTo("  Stored layer  "));
            Assert.That(component.weightParameterPrefix, Is.EqualTo("  Stored/Weight  "));
            Assert.That(component.layerIndices, Is.EqualTo(new[] { 3, -1, 3, 1 }));
            Assert.That(component.maxPriorityCombinations, Is.Zero);
            Assert.That(component.maxAapDecisionNodes, Is.EqualTo(20000));
            Assert.That((int)component.heuristics, Is.EqualTo(255));
            Assert.That(component.shortAnimationMaxFrames, Is.EqualTo(99));
            Assert.That(component.shortAnimationMaxSeconds, Is.EqualTo(0.8f));
            Assert.That(component.motionTimeSampleCount, Is.EqualTo(1));
            Assert.That((int)component.writeDefaultsPolicy, Is.EqualTo(99));
        }

        [Test]
        public void PrefabImportAndInspectorAnalysisPreserveStoredConfigurationAndMetadata()
        {
            var component = CreateStoredConfiguration();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            controller.layers[0].stateMachine.AddState("User state").writeDefaultValues = true;
            component.targetMode = AnimatorOptimizerComponent.TargetMode.ExplicitController;
            component.controller = controller;
            string path = folder + "/Stored.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            byte[] original = File.ReadAllBytes(path);
            byte[] metadata = File.ReadAllBytes(path + ".meta");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            UnityEditor.Editor inspector = null;
            try
            {
                var stored = loaded.GetComponent<AnimatorOptimizerComponent>();
                AssertStoredConfiguration(stored);
                string originalState = EditorJsonUtility.ToJson(stored);
                inspector = UnityEditor.Editor.CreateEditor(stored, typeof(AnimatorOptimizerEditor));
                typeof(AnimatorOptimizerEditor).GetMethod("EnsureAnalysisQueued", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(inspector, new object[] { stored });
                typeof(AnimatorOptimizerEditor).GetMethod("RunQueuedAnalysis", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(inspector, null);
                Assert.That(EditorJsonUtility.ToJson(stored), Is.EqualTo(originalState));
                AssertStoredConfiguration(stored);
                Assert.That(typeof(AnimatorOptimizerComponent).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
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
        public void BuildSnapshotAndOptionsUseEffectiveValuesWithoutNormalizingTheirSource()
        {
            var component = CreateStoredConfiguration();
            string original = EditorJsonUtility.ToJson(component);
            var captured = AnimatorOptimizerConfigData.FromComponent(component, root);
            var options = captured.CreateOptions();
            Assert.That(captured.LayerIndices, Is.EqualTo(new[] { 3, 1 }));
            Assert.That(captured.LayerIndices, Is.Not.SameAs(component.layerIndices));
            Assert.That(options.GeneratedLayerName, Is.EqualTo("Stored layer"));
            Assert.That(options.WeightParameterPrefix, Is.EqualTo("Stored/Weight"));
            Assert.That(options.MaxPriorityCombinations, Is.EqualTo(16));
            Assert.That(options.MaxAapDecisionNodes, Is.EqualTo(16384));
            Assert.That(options.Heuristics, Is.EqualTo(AnimatorOptimizerOptions.AggressiveHeuristics));
            Assert.That(options.ShortAnimationMaxFrames, Is.EqualTo(10));
            Assert.That(options.ShortAnimationMaxSeconds, Is.EqualTo(0.25f));
            Assert.That(options.MotionTimeSampleCount, Is.EqualTo(2));
            Assert.That(options.WriteDefaultsPolicy, Is.EqualTo(AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict));
            captured.LayerIndices.Clear();
            options.GeneratedLayerName = "Generated copy";
            Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(original));
            AssertStoredConfiguration(component);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void NonfiniteSamplingTimeUsesABuildDefaultWithoutChangingTheStoredValue(float stored)
        {
            var component = root.AddComponent<AnimatorOptimizerComponent>();
            component.shortAnimationMaxSeconds = stored;
            var options = AnimatorOptimizerConfigData.CreateOptions(component);
            Assert.That(options.ShortAnimationMaxSeconds, Is.EqualTo(0.1f));
            if (float.IsNaN(stored)) Assert.That(float.IsNaN(component.shortAnimationMaxSeconds), Is.True);
            else Assert.That(component.shortAnimationMaxSeconds, Is.EqualTo(stored));
        }

        [Test]
        public void ExplicitSerializedEditsSupportUndoWithoutCleaningOtherStoredSettings()
        {
            var component = CreateStoredConfiguration();
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var serialized = new SerializedObject(component);
            serialized.FindProperty(nameof(AnimatorOptimizerComponent.generatedLayerName)).stringValue = "Chosen layer";
            Assert.That(serialized.ApplyModifiedProperties(), Is.True);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Assert.That(component.generatedLayerName, Is.EqualTo("Chosen layer"));
            Assert.That(component.layerIndices, Is.EqualTo(new[] { 3, -1, 3, 1 }));
            Assert.That(component.shortAnimationMaxFrames, Is.EqualTo(99));
            Undo.PerformUndo();
            AssertStoredConfiguration(component);
            Undo.PerformRedo();
            Assert.That(component.generatedLayerName, Is.EqualTo("Chosen layer"));
            Assert.That(component.layerIndices, Is.EqualTo(new[] { 3, -1, 3, 1 }));
        }
    }
}
