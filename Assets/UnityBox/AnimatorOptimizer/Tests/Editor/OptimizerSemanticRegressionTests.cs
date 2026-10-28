using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerSemanticRegressionTests
    {
        private string controllerPath;
        private AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            controllerPath = "Assets/AnimatorOptimizerSemantic_" + Guid.NewGuid().ToString("N") + ".controller";
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers = Array.Empty<AnimatorControllerLayer>();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(controllerPath);
        }

        [TestCase(AnimatorConditionMode.Equals, 0f, AnimatorConditionMode.Less)]
        [TestCase(AnimatorConditionMode.Equals, 1f, AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.NotEqual, 0f, AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.NotEqual, 1f, AnimatorConditionMode.Less)]
        [TestCase(AnimatorConditionMode.If, 0f, AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.IfNot, 0f, AnimatorConditionMode.Less)]
        public void BoolUpgradeRewritesConditionsInRetainedNestedGraph(
            AnimatorConditionMode original, float threshold, AnimatorConditionMode expected)
        {
            AddSelectorLayer(AnimatorControllerParameterType.Bool);
            var retained = AddLayer("Retained");
            var nested = retained.AddStateMachine("Nested");
            var state = nested.AddState("State");
            state.motion = CreateClip("Retained output", 1f);
            var entry = nested.AddEntryTransition(state);
            entry.AddCondition(original, threshold, "Selector");
            var any = nested.AddAnyStateTransition(state);
            any.AddCondition(original, threshold, "Selector");
            var exit = state.AddExitTransition();
            exit.AddCondition(original, threshold, "Selector");
            var machineExit = retained.AddStateMachineExitTransition(nested);
            machineExit.AddCondition(original, threshold, "Selector");
            AssetDatabase.SaveAssets();
            var options = SingleLayerOptions();

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            Assert.That(controller.parameters.Single(parameter => parameter.name == "Selector").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
            foreach (var transition in new AnimatorTransitionBase[] { entry, any, exit, machineExit })
            {
                Assert.That(transition.conditions.Single().mode, Is.EqualTo(expected));
                Assert.That(transition.conditions.Single().threshold, Is.EqualTo(0.5f));
            }
        }

        [TestCase(AnimatorConditionMode.Equals)]
        [TestCase(AnimatorConditionMode.NotEqual)]
        public void IntUpgradeRejectsEqualityConditionsInRetainedGraphBeforeMutation(AnimatorConditionMode mode)
        {
            AddSelectorLayer(AnimatorControllerParameterType.Int);
            var retained = AddLayer("Retained");
            var state = retained.AddState("State");
            var transition = retained.AddAnyStateTransition(state);
            transition.AddCondition(mode, 1f, "Selector");
            AssetDatabase.SaveAssets();
            var originalLayers = controller.layers.Select(layer => layer.stateMachine).ToArray();
            var originalAssets = AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length;
            var options = SingleLayerOptions();
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].CanConvert, Is.True, string.Join("; ", report.Layers[0].Reasons));

            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0 }, options);

            Assert.That(issues.Any(issue => issue.Contains("Int Equals/NotEqual")), Is.True, string.Join("; ", issues));
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options));
            Assert.That(controller.parameters.Single(parameter => parameter.name == "Selector").type,
                Is.EqualTo(AnimatorControllerParameterType.Int));
            Assert.That(controller.layers.Select(layer => layer.stateMachine), Is.EqualTo(originalLayers));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length, Is.EqualTo(originalAssets));
            Assert.That(transition.conditions.Single().mode, Is.EqualTo(mode));
        }

        [TestCase(AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.Less)]
        public void IntUpgradePreservesSafeComparisonConditionsInRetainedGraph(AnimatorConditionMode mode)
        {
            AddSelectorLayer(AnimatorControllerParameterType.Int);
            var retained = AddLayer("Retained");
            var state = retained.AddState("State");
            var transition = retained.AddAnyStateTransition(state);
            transition.AddCondition(mode, 1f, "Selector");
            AssetDatabase.SaveAssets();

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, SingleLayerOptions());

            Assert.That(controller.parameters.Single(parameter => parameter.name == "Selector").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(transition.conditions.Single().mode, Is.EqualTo(mode));
            Assert.That(transition.conditions.Single().threshold, Is.EqualTo(1f));
        }

        [Test]
        public void UndoRestoresBoolDeclarationAndRetainedTransitionTogether()
        {
            AddSelectorLayer(AnimatorControllerParameterType.Bool);
            var retained = AddLayer("Retained");
            var state = retained.AddState("State");
            var transition = retained.AddAnyStateTransition(state);
            transition.AddCondition(AnimatorConditionMode.If, 0f, "Selector");
            AssetDatabase.SaveAssets();
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, SingleLayerOptions());
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();

            Assert.That(controller.parameters.Single(parameter => parameter.name == "Selector").type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
            Assert.That(transition.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(controller.layers[0].name, Is.EqualTo("Selected"));
        }

        [Test]
        public void UndoRestoresEmptyMotionAndRemovedLayer()
        {
            var machine = AddLayer("Base");
            var state = machine.AddState("Empty");
            state.writeDefaultValues = false;
            var original = new AnimationClip { name = "Original empty" };
            AssetDatabase.AddObjectToAsset(original, controller);
            state.motion = original;
            var removed = AddLayer("Unused");
            var removedState = removed.AddState("Empty");
            removedState.writeDefaultValues = false;
            removedState.motion = original;
            AssetDatabase.SaveAssets();
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null, new AnimatorOptimizerOptions
            {
                MergeSynchronizedStateMachines = false,
            });
            Assert.That(controller.layers.Length, Is.EqualTo(1));
            Assert.That(state.motion, Is.Not.SameAs(original));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();

            Assert.That(controller.layers.Length, Is.EqualTo(2));
            Assert.That(controller.layers[1].stateMachine, Is.SameAs(removed));
            Assert.That(state.motion, Is.SameAs(original));
        }

        [Test]
        public void RejectedUnsavedCleanupDoesNotCloneOrReplaceBorrowedStateMachine()
        {
            var externalPath = controllerPath.Replace(".controller", "_External.controller");
            var unsaved = new AnimatorController { name = "Unsaved cleanup target" };
            try
            {
                var external = AnimatorController.CreateAnimatorControllerAtPath(externalPath);
                var borrowed = external.layers[0].stateMachine;
                unsaved.layers = new[] { new AnimatorControllerLayer
                {
                    name = "Borrowed source", stateMachine = borrowed, syncedLayerIndex = -1,
                } };
                var originalAssetCount = AssetDatabase.LoadAllAssetsAtPath(externalPath).Length;

                var error = Assert.Throws<InvalidOperationException>(() =>
                    AnimatorOptimizerConverter.CleanupControllerInPlace(unsaved, null));

                Assert.That(error.Message, Does.Contain("Assets/"));
                Assert.That(unsaved.layers[0].stateMachine, Is.SameAs(borrowed));
                Assert.That(AssetDatabase.GetAssetPath(borrowed), Is.EqualTo(externalPath));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(externalPath).Length, Is.EqualTo(originalAssetCount));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unsaved);
                AssetDatabase.DeleteAsset(externalPath);
            }
        }

        [Test]
        public void UndoRestoresNormalizedAlwaysOneBlendTree()
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AlwaysOne", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AliasAlwaysOne", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });
            var machine = AddLayer("Base");
            var state = machine.AddState("State");
            var tree = new BlendTree
            {
                name = "Direct", blendType = BlendTreeType.Direct,
                children = new[] { new ChildMotion
                {
                    motion = CreateClip("Output", 1f), directBlendParameter = "AliasAlwaysOne", timeScale = 1f,
                } },
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            state.motion = tree;
            AssetDatabase.SaveAssets();
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null, new AnimatorOptimizerOptions
            {
                NormalizeAlwaysOneParameters = true, MergeSynchronizedStateMachines = false,
            });
            Assert.That(tree.children.Single().directBlendParameter, Is.EqualTo("AlwaysOne"));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();

            Assert.That(tree.children.Single().directBlendParameter, Is.EqualTo("AliasAlwaysOne"));
        }

        private void AddSelectorLayer(AnimatorControllerParameterType type)
        {
            controller.AddParameter("Selector", type);
            var machine = AddLayer("Selected");
            var off = machine.AddState("Off");
            var on = machine.AddState("On");
            off.motion = CreateClip("Selected output", 0f);
            on.motion = CreateClip("Selected output", 1f);
            off.writeDefaultValues = true;
            on.writeDefaultValues = true;
            machine.defaultState = off;
            var toOff = machine.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(type == AnimatorControllerParameterType.Bool
                ? AnimatorConditionMode.IfNot : AnimatorConditionMode.NotEqual, 1f, "Selector");
            var toOn = machine.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(type == AnimatorControllerParameterType.Bool
                ? AnimatorConditionMode.If : AnimatorConditionMode.Equals, 1f, "Selector");
        }

        private AnimatorStateMachine AddLayer(string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = name, stateMachine = machine, defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });
            return machine;
        }

        private AnimationClip CreateClip(string path, float value)
        {
            var clip = new AnimationClip { name = path };
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, value));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static AnimatorOptimizerOptions SingleLayerOptions() => new AnimatorOptimizerOptions
        {
            RequireAtLeastTwoLayers = false,
            MergeSynchronizedStateMachines = false,
            RemoveEffectivelyEmptyLayers = false,
        };
    }
}
