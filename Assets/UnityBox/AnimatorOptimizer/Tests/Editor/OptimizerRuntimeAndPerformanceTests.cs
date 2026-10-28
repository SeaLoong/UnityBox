using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerRuntimeAndPerformanceTests
    {
        private string folder;
        private Scene scene;
        private readonly List<GameObject> roots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/AO_Runtime_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in roots) if (root != null) Object.DestroyImmediate(root);
            roots.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void SuppliedSceneComponentGuidResolvesToTheOptimizerMonoScript()
        {
            var path = AssetDatabase.GUIDToAssetPath("a157d3d35699dd84abde9e5412bd7017");
            Assert.That(path, Is.Not.Empty);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.GetClass(), Is.EqualTo(typeof(AnimatorOptimizerComponent)));
        }

        [TestCase(AnimatorControllerParameterType.Bool, true)]
        [TestCase(AnimatorControllerParameterType.Bool, false)]
        [TestCase(AnimatorControllerParameterType.Int, true)]
        [TestCase(AnimatorControllerParameterType.Int, false)]
        [TestCase(AnimatorControllerParameterType.Float, true)]
        [TestCase(AnimatorControllerParameterType.Float, false)]
        public void ParameterSelectorsMatchNativeAnimatorOnEveryFrame(AnimatorControllerParameterType type, bool writeDefaults)
        {
            var source = CreateController("Source");
            source.AddParameter("Input", type);
            AddSelector(source, "Selector", "Target", "Input", type, writeDefaults);
            if (writeDefaults) AddConstant(source, "Other", "Other", 7f, true);
            var optimized = OptimizeCopy(source, writeDefaults ? new[] { 0, 1 } : new[] { 0 },
                new AnimatorOptimizerOptions { RequireAtLeastTwoLayers = writeDefaults });
            var paths = writeDefaults ? new[] { "Target", "Other" } : new[] { "Target" };
            var before = CreateAnimator(source, paths);
            var after = CreateAnimator(optimized, paths);
            var values = type == AnimatorControllerParameterType.Bool
                ? new[] { 0f, 1f, 0f, 1f, 1f, 0f }
                : type == AnimatorControllerParameterType.Int
                    ? new[] { -2f, 0f, 1f, 2f, 5f, 1f, 0f }
                    : new[] { -1f, 0f, 0.25f, 0.5f, 0.749f, 0.75f, 1f, 0.5f };
            CompareFrames(before, after, 120, frame =>
            {
                var value = values[(frame / 3) % values.Length];
                SetParameter(before, "Input", type, value);
                SetParameter(after, "Input", AnimatorControllerParameterType.Float, value);
            });
        }

        [TestCase("IsLocal", AnimatorControllerParameterType.Bool)]
        [TestCase("Viseme", AnimatorControllerParameterType.Int)]
        public void ExplicitPlatformInputsExecuteTheirDeclaredNativeParameterType(string parameter, AnimatorControllerParameterType type)
        {
            var source = CreateController("PlatformInput");
            source.AddParameter(parameter, type);
            AddSelector(source, "Selector", "Target", parameter, type, true);
            AddConstant(source, "Other", "Other", 3f, true);
            var options = new AnimatorOptimizerOptions { UseVrChatParameterSemantics = true, RequireAtLeastTwoLayers = false };
            var report = AnimatorOptimizerAnalyzer.Analyze(source, options);
            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[1].CanConvert, Is.True);
            var optimized = OptimizeCopy(source, new[] { 1 }, options);
            Assert.That(optimized.layers[0].stateMachine.states.Length, Is.EqualTo(2));
            var before = CreateAnimator(source, "Target", "Other"); var after = CreateAnimator(optimized, "Target", "Other");
            var outputType = optimized.parameters.Single(input => input.name == parameter).type;
            Assert.That(outputType, Is.EqualTo(type));
            CompareFrames(before, after, 90, frame =>
            {
                var value = (frame / 3) % 2;
                SetParameter(before, parameter, type, value); SetParameter(after, parameter, outputType, value);
            });
        }

        [Test]
        public void NullMotionSelectorsRetainNativePoseHistoryAlongsideGeneratedLayers()
        {
            var source = CreateController("EmptySelector");
            source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            AddSelector(source, "Selector", "Target", "Gate", AnimatorControllerParameterType.Bool, true);
            source.layers[0].stateMachine.defaultState.motion = null;
            AddConstant(source, "Other", "Other", 4f, true);
            var options = new AnimatorOptimizerOptions { RequireAtLeastTwoLayers = false };
            Assert.That(AnimatorOptimizerAnalyzer.Analyze(source, options).Layers[0].CanConvert, Is.False);
            var optimized = OptimizeCopy(source, new[] { 1 }, options);
            Assert.That(optimized.parameters.Single(parameter => parameter.name == "Gate").type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
            var before = CreateAnimator(source, "Target", "Other");
            var after = CreateAnimator(optimized, "Target", "Other");
            CompareFrames(before, after, 90, frame =>
            {
                var value = (frame / 10) % 2 != 0;
                before.SetBool("Gate", value); after.SetBool("Gate", value);
            });
        }

        [Test]
        public void OverridePriorityMatchesNativeAnimatorAcrossAllInputCombinations()
        {
            var source = CreateController("Priority");
            source.AddParameter("Lower", AnimatorControllerParameterType.Bool);
            source.AddParameter("Upper", AnimatorControllerParameterType.Bool);
            AddSelector(source, "Lower", "Target", "Lower", AnimatorControllerParameterType.Bool, true, 1f, 3f);
            AddSelector(source, "Upper", "Target", "Upper", AnimatorControllerParameterType.Bool, true, 5f, 9f);
            var optimized = OptimizeCopy(source, new[] { 0, 1 });
            var before = CreateAnimator(source, "Target");
            var after = CreateAnimator(optimized, "Target");
            CompareFrames(before, after, 120, frame =>
            {
                var lower = (frame / 3) % 2;
                var upper = (frame / 6) % 2;
                before.SetBool("Lower", lower != 0); before.SetBool("Upper", upper != 0);
                after.SetFloat("Lower", lower); after.SetFloat("Upper", upper);
            });
        }

        [Test]
        public void TransformMasksKeepIncludedAndExcludedBindingsAcrossNativeEvaluation()
        {
            var source = CreateController("Masked");
            source.AddParameter("Input", AnimatorControllerParameterType.Bool);
            var mask = new AvatarMask { name = "OnlyOther", transformCount = 3 };
            mask.SetTransformPath(0, ""); mask.SetTransformActive(0, true);
            mask.SetTransformPath(1, "Target"); mask.SetTransformActive(1, false);
            mask.SetTransformPath(2, "Other"); mask.SetTransformActive(2, true);
            AssetDatabase.AddObjectToAsset(mask, source);
            AddSelector(source, "MaskedSelector", "Target", "Input", AnimatorControllerParameterType.Bool, true);
            AddConstant(source, "MaskedOther", "Other", 5f, true);
            var layers = source.layers;
            foreach (var layer in layers) layer.avatarMask = mask;
            source.layers = layers;
            var optimized = OptimizeCopy(source, new[] { 0, 1 });
            var before = CreateAnimator(source, "Target", "Other");
            var after = CreateAnimator(optimized, "Target", "Other");
            CompareFrames(before, after, 90, frame =>
            {
                before.SetBool("Input", frame % 6 >= 3);
                after.SetFloat("Input", frame % 6 >= 3 ? 1f : 0f);
            });
        }

        [Test]
        public void DynamicClipPlaybackMatchesOverMultipleCycles()
        {
            var source = CreateController("Dynamic");
            AddConstant(source, "DynamicA", "Target", 0f, true, true);
            AddConstant(source, "DynamicB", "Other", 0.5f, true, true);
            var optimized = OptimizeCopy(source, new[] { 0, 1 });
            CompareFrames(CreateAnimator(source, "Target", "Other"), CreateAnimator(optimized, "Target", "Other"), 240, _ => { });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AapEntryAndStateHistoryMatchNativeAnimatorFromTheFirstFrame(bool entryOn)
        {
            var source = CreateController("History");
            source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            var machine = NewMachine(source, "History");
            var off = AddState(source, machine, "Off", "Target", 0f, true);
            var on = AddState(source, machine, "On", "Target", 1f, true);
            machine.defaultState = off;
            var transition = off.AddTransition(on);
            transition.hasExitTime = false; transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            var entry = machine.AddEntryTransition(on);
            entry.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            AddLayer(source, machine);
            var optimized = OptimizeCopy(source, new[] { 0 }, AapOptions());
            var before = CreateAnimator(source, "Target");
            var after = CreateAnimator(optimized, "Target");
            CompareFrames(before, after, 90, frame =>
            {
                var value = frame == 0 ? entryOn : frame >= 10 && frame < 20;
                before.SetBool("Gate", value); after.SetFloat("Gate", value ? 1f : 0f);
            });
        }

        [Test]
        public void AapIntegerHistoryKeepsOutputAndSurroundingLayerOrder()
        {
            var source = CreateController("IntegerHistory");
            source.AddParameter("Choice", AnimatorControllerParameterType.Int);
            AddConstant(source, "Before", "Before", 2f, true);
            var machine = NewMachine(source, "History");
            var off = AddState(source, machine, "Off", "Target", 0f, true);
            var on = AddState(source, machine, "On", "Target", 1f, true);
            machine.defaultState = off;
            var up = off.AddTransition(on); up.hasExitTime = false; up.duration = 0f;
            up.AddCondition(AnimatorConditionMode.Greater, 0f, "Choice");
            var down = on.AddTransition(off); down.hasExitTime = false; down.duration = 0f;
            down.AddCondition(AnimatorConditionMode.Less, 0f, "Choice");
            AddLayer(source, machine);
            AddConstant(source, "After", "After", 4f, true);
            var optimized = OptimizeCopy(source, new[] { 1 }, AapOptions());
            var before = CreateAnimator(source, "Before", "Target", "After");
            var after = CreateAnimator(optimized, "Before", "Target", "After");
            CompareFrames(before, after, 120, frame =>
            {
                var value = new[] { 0, 1, 0, -1, 0 }[(frame / 8) % 5];
                before.SetInteger("Choice", value); after.SetFloat("Choice", value);
            });
        }

        [TestCase(8)]
        [TestCase(32)]
        [TestCase(64)]
        [Category("UnityBoxPerformance")]
        public void NativeAnimatorUpdatePerformanceBeforeAndAfterLayerMerge(int layerCount)
        {
            var source = CreateController("Performance");
            var paths = Enumerable.Range(0, layerCount).Select(index => "Target" + index).ToArray();
            for (var index = 0; index < layerCount; index++) AddConstant(source, "Layer" + index, paths[index], index + 1f, true);
            var optimized = OptimizeCopy(source, Enumerable.Range(0, layerCount).ToArray());
            var before = CreateAnimator(source, paths);
            var after = CreateAnimator(optimized, paths);
            CompareFrames(before, after, 8, _ => { });
            Measure("animator_update_before_merge", layerCount, () => before.Update(1f / 60f), 300);
            Measure("animator_update_after_merge", layerCount, () => after.Update(1f / 60f), 300);
            Assert.That(optimized.layers.Length, Is.EqualTo(1));
        }

        [TestCase(8)]
        [TestCase(32)]
        [TestCase(64)]
        [Category("UnityBoxPerformance")]
        public void AnalysisPerformanceAtRepresentativeLayerCounts(int layerCount)
        {
            var source = CreateController("AnalysisPerformance");
            for (var index = 0; index < layerCount; index++) AddConstant(source, "Layer" + index, "Target" + index, index + 1f, true);
            var options = new AnimatorOptimizerOptions();
            var report = AnimatorOptimizerAnalyzer.Analyze(source, options);
            Assert.That(report.ConvertibleLayerCount, Is.EqualTo(layerCount));
            Measure("analyze_static_layers", layerCount, () => AnimatorOptimizerAnalyzer.Analyze(source, options), 1);
        }

        [TestCase("add")]
        [TestCase("subtract")]
        [TestCase("multiply")]
        [TestCase("one_plus_divide")]
        public void AapArithmeticExecutesTheDeclaredFormulaInNativeAnimator(string operation)
        {
            var controller = CreateController("Arithmetic_" + operation);
            foreach (var name in new[] { "X", "Y", "Output", "One" })
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = name, type = AnimatorControllerParameterType.Float, defaultFloat = name == "One" ? 1f : 0f,
                });
            var range = new AnimatorOptimizerAapRange(-1f, 1f);
            var tree = operation == "add" ? AnimatorOptimizerAapBuilder.BuildAddition(controller, "Output", "X", "Y", range, range, "One")
                : operation == "subtract" ? AnimatorOptimizerAapBuilder.BuildSubtraction(controller, "Output", "X", "Y", range, range, "One")
                : operation == "multiply" ? AnimatorOptimizerAapBuilder.BuildMultiplication(controller, "Output", "X", "Y", range, range)
                : AnimatorOptimizerAapBuilder.BuildOnePlusDivision(controller, "Output", "X", "Y", range, "One");
            var machine = NewMachine(controller, "Math");
            var state = machine.AddState("Evaluate"); state.writeDefaultValues = true; state.motion = tree;
            machine.defaultState = state; AddLayer(controller, machine);
            var animator = CreateAnimator(controller);
            for (var frame = 0; frame < 60; frame++)
            {
                var x = new[] { -1f, -0.5f, 0f, 0.25f, 1f }[frame % 5];
                var y = new[] { -1f, 0f, 0.5f, 1f }[(frame / 5) % 4];
                if (operation == "one_plus_divide") y = Mathf.Abs(y);
                animator.SetFloat("X", x); animator.SetFloat("Y", y);
                animator.Update(1f / 60f);
                var expected = operation == "add" ? x + y : operation == "subtract" ? x - y
                    : operation == "multiply" ? x * y : x / (1f + y);
                Assert.That(animator.GetFloat("Output"), Is.EqualTo(expected).Within(0.0001f), operation + " frame=" + frame);
            }
        }

        [Test]
        public void AapFloatHistoryPreservesOverlappingAnyStatePriorityOnEveryFrame()
        {
            var source = CreateController("FloatHistory");
            source.AddParameter("Input", AnimatorControllerParameterType.Float);
            AddSelector(source, "History", "Target", "Input", AnimatorControllerParameterType.Float, true);
            foreach (var transition in source.layers[0].stateMachine.anyStateTransitions) transition.canTransitionToSelf = false;
            var options = AapOptions(); options.AllowFloatConditionApproximation = true;
            var optimized = OptimizeCopy(source, new[] { 0 }, options);
            var before = CreateAnimator(source, "Target"); var after = CreateAnimator(optimized, "Target");
            CompareFrames(before, after, 120, frame =>
            {
                var value = new[] { -1f, 0.5f, 1f, 0.5f }[(frame / 12) % 4];
                before.SetFloat("Input", value); after.SetFloat("Input", value);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AapRootExitReentersWithTheOriginalDefaultPose(bool emptyDefaultMotion)
        {
            var source = CreateController("RootExit");
            source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            source.AddParameter("Reset", AnimatorControllerParameterType.Bool);
            var machine = NewMachine(source, "History");
            var off = AddState(source, machine, "Off", "Target", 0f, true);
            if (emptyDefaultMotion) off.motion = null;
            var on = AddState(source, machine, "On", "Target", 1f, true);
            machine.defaultState = off;
            var enter = off.AddTransition(on); enter.hasExitTime = false; enter.duration = 0f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            var exit = on.AddExitTransition(); exit.hasExitTime = false; exit.duration = 0f;
            exit.AddCondition(AnimatorConditionMode.If, 0f, "Reset");
            AddLayer(source, machine);
            var options = AapOptions();
            if (emptyDefaultMotion) AddConstant(source, "Other", "Other", 3f, true);
            var report = AnimatorOptimizerAnalyzer.Analyze(source, options);
            Assert.That(report.Layers[0].CanConvert, Is.EqualTo(!emptyDefaultMotion));
            var optimized = OptimizeCopy(source, emptyDefaultMotion ? new[] { 1 } : new[] { 0 }, options);
            var before = CreateAnimator(source, "Target", "Other"); var after = CreateAnimator(optimized, "Target", "Other");
            CompareFrames(before, after, 90, frame =>
            {
                var gate = frame >= 10 && frame < 20;
                var reset = frame >= 30 && frame < 40;
                before.SetBool("Gate", gate); before.SetBool("Reset", reset);
                SetParameter(after, "Gate", optimized.parameters.Single(p => p.name == "Gate").type, gate ? 1f : 0f);
                SetParameter(after, "Reset", optimized.parameters.Single(p => p.name == "Reset").type, reset ? 1f : 0f);
            });
        }

        [Test]
        public void SavedOptimizedControllerReloadsItsCompleteGraphAndRunsIdentically()
        {
            var source = CreateController("RoundTrip");
            source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            AddSelector(source, "Selector", "Target", "Gate", AnimatorControllerParameterType.Bool, true);
            AddConstant(source, "Other", "Other", 5f, true);
            var optimized = OptimizeCopy(source, new[] { 0, 1 });
            AssetDatabase.SaveAssets();
            var reloadPath = folder + "/Reloaded.controller";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(optimized), reloadPath), Is.True);
            AssetDatabase.ImportAsset(reloadPath, ImportAssetOptions.ForceSynchronousImport);
            var reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(reloadPath);
            Assert.That(reloaded.layers[0].stateMachine.defaultState, Is.Not.Null);
            var before = CreateAnimator(source, "Target", "Other"); var after = CreateAnimator(reloaded, "Target", "Other");
            CompareFrames(before, after, 60, frame =>
            {
                before.SetBool("Gate", frame % 8 >= 4); after.SetFloat("Gate", frame % 8 >= 4 ? 1f : 0f);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UndoAndRedoRestoreTheControllerAndGeneratedSubassetsTogether(bool aap)
        {
            var source = CreateController("UndoRoundTrip");
            source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            if (aap)
            {
                var machine = NewMachine(source, "History");
                var off = AddState(source, machine, "Off", "Target", 0f, true);
                var on = AddState(source, machine, "On", "Target", 1f, true);
                machine.defaultState = off;
                var transition = off.AddTransition(on); transition.hasExitTime = false; transition.duration = 0f;
                transition.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
                machine.AddEntryTransition(on).AddCondition(AnimatorConditionMode.If, 0f, "Gate");
                off.tag = "Action"; on.tag = "Action";
                AddLayer(source, machine);
            }
            else
            {
                AddSelector(source, "Selector", "Target", "Gate", AnimatorControllerParameterType.Bool, true);
                AddConstant(source, "Other", "Other", 5f, true);
            }
            AssetDatabase.SaveAssets();
            var path = AssetDatabase.GetAssetPath(source);
            var baselinePath = folder + "/UndoBaseline.controller";
            Assert.That(AssetDatabase.CopyAsset(path, baselinePath), Is.True);
            var baseline = AssetDatabase.LoadAssetAtPath<AnimatorController>(baselinePath);
            var originalLayers = source.layers.Select(layer => layer.stateMachine).ToArray();
            var originalCount = AssetDatabase.LoadAllAssetsAtPath(path).Length;
            Undo.ClearAll(); Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            AnimatorOptimizerConverter.ConvertInPlace(source, aap ? new[] { 0 } : new[] { 0, 1 }, aap ? AapOptions() : null);
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            var generatedCount = AssetDatabase.LoadAllAssetsAtPath(path).Length;
            Assert.That(generatedCount, Is.GreaterThan(originalCount));
            Undo.PerformUndo();
            Assert.That(source.layers.Select(layer => layer.stateMachine), Is.EqualTo(originalLayers));
            Assert.That(source.parameters.Single(parameter => parameter.name == "Gate").type, Is.EqualTo(AnimatorControllerParameterType.Bool));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).Length, Is.EqualTo(originalCount), "Undo must remove generated subassets");
            Undo.PerformRedo();
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).Length, Is.EqualTo(generatedCount));
            Assert.That(source.parameters.Single(parameter => parameter.name == "Gate").type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(source.layers.All(layer => layer.stateMachine.defaultState != null), Is.True);
            if (aap) Assert.That(source.layers.All(layer => layer.stateMachine.defaultState.tag == "Action"), Is.True);
            var paths = aap ? new[] { "Target" } : new[] { "Target", "Other" };
            var before = CreateAnimator(baseline, paths); var after = CreateAnimator(source, paths);
            CompareFrames(before, after, 60, frame =>
            {
                var on = frame >= 10 && frame < 20;
                before.SetBool("Gate", on); after.SetFloat("Gate", on ? 1f : 0f);
            });
            Undo.ClearAll();
        }

        public static void ConfigurePlayerProbe(GameObject root, string assetFolder)
        {
            var factory = new OptimizerRuntimeAndPerformanceTests { folder = assetFolder };
            var selector = factory.CreateController("AO_PlayerSelector");
            selector.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            AddSelector(selector, "Selector", "Target", "Gate", AnimatorControllerParameterType.Bool, true);
            var selectorCopy = factory.OptimizeCopy(selector, new[] { 0 }, new AnimatorOptimizerOptions { RequireAtLeastTwoLayers = false });
            AddProbeAnimator(root, "AO_Selector_Source", selector);
            AddProbeAnimator(root, "AO_Selector_Optimized", selectorCopy);
            EmitProbe(root, "AnimatorOptimizer/Bool selector", "AO_Selector_Source", "AO_Selector_Optimized", new[]
            {
                ProbeStepFor("AO_Selector_Source", "AO_Selector_Optimized", "Gate", 0f, 0f, 3),
                ProbeStepFor("AO_Selector_Source", "AO_Selector_Optimized", "Gate", 1f, 1f, 1),
                ProbeStepFor("AO_Selector_Source", "AO_Selector_Optimized", "Gate", 0f, 0f, 1),
            });
            foreach (var entryOn in new[] { false, true })
            {
                var suffix = entryOn ? "EntryOn" : "EntryOff";
                var source = factory.CreateController("AO_PlayerHistory_" + suffix);
                source.AddParameter("Gate", AnimatorControllerParameterType.Bool);
                var machine = NewMachine(source, "History");
                var off = AddState(source, machine, "Off", "Target", 0f, true);
                var on = AddState(source, machine, "On", "Target", 1f, true);
                machine.defaultState = off;
                var transition = off.AddTransition(on); transition.hasExitTime = false; transition.duration = 0f;
                transition.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
                machine.AddEntryTransition(on).AddCondition(AnimatorConditionMode.If, 0f, "Gate");
                AddLayer(source, machine);
                var copy = factory.OptimizeCopy(source, new[] { 0 }, AapOptions());
                var beforeName = "AO_History_Source_" + suffix; var afterName = "AO_History_Optimized_" + suffix;
                AddProbeAnimator(root, beforeName, source); AddProbeAnimator(root, afterName, copy);
                EmitProbe(root, "AnimatorOptimizer/AAP " + suffix, beforeName, afterName, new[]
                {
                    ProbeStepFor(beforeName, afterName, "Gate", entryOn ? 1f : 0f, entryOn ? 1f : 0f, 1),
                    ProbeStepFor(beforeName, afterName, "Gate", 1f, 1f, 1),
                    ProbeStepFor(beforeName, afterName, "Gate", 0f, 1f, 8),
                });
            }
            foreach (var layerCount in new[] { 8, 32, 64 })
            {
                var source = factory.CreateController("AO_PlayerLayers_" + layerCount);
                var paths = Enumerable.Range(0, layerCount).Select(index => "Target" + index).ToArray();
                for (var index = 0; index < layerCount; index++)
                    AddConstant(source, "Layer" + index, paths[index], index + 1f, true);
                var copy = factory.OptimizeCopy(source, Enumerable.Range(0, layerCount).ToArray());
                var beforeName = "AO_Layers_Source_" + layerCount; var afterName = "AO_Layers_Optimized_" + layerCount;
                AddProbeAnimator(root, beforeName, source, paths); AddProbeAnimator(root, afterName, copy, paths);
                var checks = Enumerable.Range(0, layerCount).SelectMany(index => new[]
                {
                    new ProbeCheck { path = beforeName + "/Target" + index, expected = index + 1f },
                    new ProbeCheck { path = afterName + "/Target" + index, expected = index + 1f },
                }).ToArray();
                EmitProbe(root, "AnimatorOptimizer/layers " + layerCount, beforeName, afterName, new[]
                {
                    new ProbeStep { frames = 8, inputs = Array.Empty<ProbeInput>(), checks = checks },
                });
            }
            AssetDatabase.SaveAssets();
        }

        private static void AddProbeAnimator(GameObject root, string name, AnimatorController controller, params string[] paths)
        {
            var actor = new GameObject(name); actor.transform.SetParent(root.transform, false);
            foreach (var path in paths.Length == 0 ? new[] { "Target" } : paths)
            {
                var target = new GameObject(path); target.transform.SetParent(actor.transform, false);
            }
            var animator = actor.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;
        }

        private static ProbeStep ProbeStepFor(string before, string after, string input, float value, float expected, int frames) => new ProbeStep
        {
            frames = frames,
            inputs = new[]
            {
                new ProbeInput { animator_path = before, name = input, type = "bool", value = value },
                new ProbeInput { animator_path = after, name = input, type = "float", value = value },
            },
            checks = new[]
            {
                new ProbeCheck { path = before + "/Target", expected = expected },
                new ProbeCheck { path = after + "/Target", expected = expected },
            },
        };

        private static void EmitProbe(GameObject root, string name, string before, string after, ProbeStep[] steps) =>
            root.SendMessage("AddScenarioJson", JsonUtility.ToJson(new ProbeScenario
            {
                name = name, animator_paths = new[] { before, after }, steps = steps,
            }), SendMessageOptions.RequireReceiver);

        [Serializable] private sealed class ProbeScenario
        {
            public string name;
            public string[] animator_paths;
            public ProbeStep[] steps;
        }
        [Serializable] private sealed class ProbeStep
        {
            public int frames;
            public float delta_time = 1f / 60f;
            public ProbeInput[] inputs;
            public ProbeCheck[] checks;
        }
        [Serializable] private sealed class ProbeInput
        {
            public string animator_path;
            public string name;
            public string type;
            public float value;
        }
        [Serializable] private sealed class ProbeCheck
        {
            public string path;
            public string property = "localPosition.x";
            public int index;
            public float expected;
            public float tolerance = 0.0001f;
        }

        private static AnimatorOptimizerOptions AapOptions() => new AnimatorOptimizerOptions
        {
            EnableAapStateRegisterOptimization = true,
            RequireAtLeastTwoLayers = false,
            RemoveEffectivelyEmptyLayers = false,
        };

        private AnimatorController CreateController(string name)
        {
            var controller = new AnimatorController { name = name };
            AssetDatabase.CreateAsset(controller, folder + "/" + name + ".controller");
            return controller;
        }

        private AnimatorController OptimizeCopy(AnimatorController source, int[] layers, AnimatorOptimizerOptions options = null)
        {
            AssetDatabase.SaveAssets();
            return AnimatorOptimizerConverter.CreateOptimizedCopy(source, folder + "/" + source.name + "_Optimized.controller", layers, options);
        }

        private static AnimatorStateMachine NewMachine(AnimatorController controller, string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            return machine;
        }

        private static void AddConstant(AnimatorController controller, string name, string path, float value, bool writeDefaults, bool dynamic = false)
        {
            var machine = NewMachine(controller, name);
            machine.defaultState = AddState(controller, machine, name, path, value, writeDefaults, dynamic);
            AddLayer(controller, machine);
        }

        private static void AddSelector(AnimatorController controller, string name, string path, string parameter,
            AnimatorControllerParameterType type, bool writeDefaults, float offValue = 0f, float onValue = 1f)
        {
            var machine = NewMachine(controller, name);
            var off = AddState(controller, machine, "Off", path, offValue, writeDefaults);
            var on = AddState(controller, machine, "On", path, onValue, writeDefaults);
            machine.defaultState = off;
            if (type == AnimatorControllerParameterType.Bool)
            {
                Any(machine, off, AnimatorConditionMode.IfNot, 0f, parameter);
                Any(machine, on, AnimatorConditionMode.If, 0f, parameter);
            }
            else if (type == AnimatorControllerParameterType.Int)
            {
                Any(machine, off, AnimatorConditionMode.Less, 1f, parameter);
                Any(machine, on, AnimatorConditionMode.Greater, 0f, parameter);
            }
            else
            {
                Any(machine, off, AnimatorConditionMode.Less, 0.75f, parameter);
                Any(machine, on, AnimatorConditionMode.Greater, 0.25f, parameter);
            }
            if (type == AnimatorControllerParameterType.Float)
                foreach (var transition in machine.anyStateTransitions) transition.canTransitionToSelf = true;
            AddLayer(controller, machine);
        }

        private static void Any(AnimatorStateMachine machine, AnimatorState target, AnimatorConditionMode mode, float threshold, string parameter)
        {
            var transition = machine.AddAnyStateTransition(target);
            transition.hasExitTime = false; transition.duration = 0f; transition.canTransitionToSelf = false;
            transition.AddCondition(mode, threshold, parameter);
        }

        private static AnimatorState AddState(AnimatorController controller, AnimatorStateMachine machine, string name,
            string path, float value, bool writeDefaults, bool dynamic = false)
        {
            var state = machine.AddState(name);
            var clip = new AnimationClip { name = name + "_" + path, frameRate = 60f };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"),
                dynamic ? AnimationCurve.Linear(0f, value, 1f, value + 1f) : AnimationCurve.Constant(0f, 1f, value));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = dynamic;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.AddObjectToAsset(clip, controller);
            state.motion = clip; state.writeDefaultValues = writeDefaults;
            return state;
        }

        private static void AddLayer(AnimatorController controller, AnimatorStateMachine machine)
        {
            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = machine.name, stateMachine = machine, defaultWeight = 1f, syncedLayerIndex = -1,
            });
            controller.layers = layers.ToArray();
        }

        private Animator CreateAnimator(AnimatorController controller, params string[] paths)
        {
            var root = new GameObject("Runtime " + controller.name);
            SceneManager.MoveGameObjectToScene(root, scene);
            roots.Add(root);
            foreach (var path in paths)
            {
                var child = new GameObject(path);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = new Vector3(0.125f, 0.25f, 0.5f);
            }
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            return animator;
        }

        private static void SetParameter(Animator animator, string name, AnimatorControllerParameterType type, float value)
        {
            if (type == AnimatorControllerParameterType.Bool) animator.SetBool(name, value != 0f);
            else if (type == AnimatorControllerParameterType.Int) animator.SetInteger(name, (int)value);
            else animator.SetFloat(name, value);
        }

        private static void CompareFrames(Animator before, Animator after, int frames, Action<int> setInputs)
        {
            var beforeChildren = before.GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
            var afterChildren = after.GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
            Assert.That(afterChildren.Length, Is.EqualTo(beforeChildren.Length));
            for (var frame = 0; frame < frames; frame++)
            {
                setInputs(frame);
                before.Update(1f / 60f); after.Update(1f / 60f);
                for (var index = 0; index < beforeChildren.Length; index++)
                {
                    Assert.That(Vector3.Distance(beforeChildren[index].localPosition, afterChildren[index].localPosition),
                        Is.LessThan(0.0001f), "frame=" + frame + ", binding=" + beforeChildren[index].name +
                        ", before=" + beforeChildren[index].localPosition + ", after=" + afterChildren[index].localPosition +
                        ", parameters=" + string.Join(",", after.parameters.Where(p => p.type == AnimatorControllerParameterType.Float)
                            .Select(p => p.name + "=" + after.GetFloat(p.name))));
                }
            }
        }

        [Serializable]
        private sealed class BenchmarkRecord
        {
            public string plugin = "AnimatorOptimizer";
            public string scenario;
            public int size;
            public int samples;
            public double median_ms;
            public double p95_ms;
            public double min_ms;
            public double max_ms;
            public long allocated_bytes_per_op;
            public string correctness = "native-frame-output-match";
        }

        private static void Measure(string scenario, int size, Action operation, int iterations)
        {
            for (var warmup = 0; warmup < Math.Max(3, iterations / 10); warmup++) operation();
            var values = new double[9];
            var probeStart = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[4096]);
            var allocationsAvailable = GC.GetAllocatedBytesForCurrentThread() > probeStart;
            long allocated = 0;
            for (var sample = 0; sample < values.Length; sample++)
            {
                var allocationStart = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                for (var index = 0; index < iterations; index++) operation();
                watch.Stop();
                allocated += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                values[sample] = watch.Elapsed.TotalMilliseconds / iterations;
            }
            Array.Sort(values);
            Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new BenchmarkRecord
            {
                scenario = scenario, size = size, samples = values.Length,
                median_ms = values[values.Length / 2], p95_ms = values[values.Length - 1],
                min_ms = values[0], max_ms = values[values.Length - 1],
                allocated_bytes_per_op = allocationsAvailable ? allocated / values.Length / iterations : -1,
                correctness = scenario.StartsWith("analyze_", StringComparison.Ordinal)
                    ? "convertible-layer-count-match" : "native-frame-output-match",
            }));
        }
    }
}
