using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class AnimatorOptimizerTestMarkerBehaviour : StateMachineBehaviour
    {
    }

    public sealed class ModularAvatarMMDLayerControl : StateMachineBehaviour
    {
        public bool DisableInMMDMode;
    }

    public sealed class AnimatorOptimizerTests
    {
        private const string TestFolder = "Assets/UnityBox/Generated/AnimatorOptimizerTests";
        private string _controllerPath;

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TestFolder);
            _controllerPath = TestFolder + "/Test_" + Guid.NewGuid().ToString("N") + ".controller";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_controllerPath))
                AssetDatabase.DeleteAsset(_controllerPath);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void ConvertsTwoParameterBoolTruthTable()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "A",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "B",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            AddBoolTruthTableLayer(controller, "Bool Logic", "A", "B");
            AddSingleMotionLayer(controller, "Other", "Other", 1f);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                UpgradeBoolParametersToFloat = true,
                UpgradeIntParametersToFloat = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            Assert.That(result.MergedLayerCount, Is.EqualTo(2));
            Assert.That(controller.layers, Has.Length.EqualTo(1));
            Assert.That(controller.parameters.First(parameter => parameter.name == "A").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(controller.parameters.First(parameter => parameter.name == "B").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));

            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.blendType, Is.EqualTo(BlendTreeType.Direct));
            Assert.That(root.children.Any(child => child.motion is BlendTree), Is.True);
        }

        [Test]
        public void RemovesIrrelevantBooleanDimensionsFromSharedDecisionTree()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter { name = "A", type = AnimatorControllerParameterType.Bool });
            controller.AddParameter(new AnimatorControllerParameter { name = "UnusedB", type = AnimatorControllerParameterType.Bool });
            var stateMachine = new AnimatorStateMachine { name = "Shared Boolean Function" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, "Shared Off", "Shared/Off", 0f);
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, "Shared On", "Shared/On", 1f);
            on.writeDefaultValues = true;
            stateMachine.defaultState = off;
            var toOff = stateMachine.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, "A");
            var toOn = stateMachine.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, "A");
            AddLayer(controller, "Shared Boolean Function", stateMachine);
            AddSingleMotionLayer(controller, "Shared Other", "Shared/Other", 1f);

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });
            var root = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
            var generatedTrees = root.children.Select(child => child.motion)
                .OfType<BlendTree>().ToList();

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(generatedTrees.SelectMany(EnumerateBlendTrees)
                .Any(tree => tree.blendParameter == "A"), Is.True);
            Assert.That(generatedTrees.SelectMany(EnumerateBlendTrees)
                .Any(tree => tree.blendParameter == "UnusedB"), Is.False);
        }

        [Test]
        public void ConvertsIntComparisonPartitionAndUpgradesParameter()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Choice",
                type = AnimatorControllerParameterType.Int,
                defaultInt = 1,
            });
            AddIntPartitionLayer(controller, "Choice Logic", "Choice");
            AddSingleMotionLayer(controller, "Other", "Other", 1f);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                UpgradeIntParametersToFloat = true,
                UpgradeBoolParametersToFloat = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.NumericSelector));

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            Assert.That(result.UpgradedParameters, Does.Contain("Choice"));
            Assert.That(controller.parameters.First(parameter => parameter.name == "Choice").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));

            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(root, Is.Not.Null);
            var selector = root.children.Select(child => child.motion).OfType<BlendTree>()
                .Single(tree => tree.blendParameter == "Choice");
            Assert.That(selector.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(selector.children.Length, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ConvertsMixedBooleanAndIntConditionsWithFiniteDecisionTable()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Enabled",
                type = AnimatorControllerParameterType.Bool,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Choice",
                type = AnimatorControllerParameterType.Int,
            });

            var stateMachine = new AnimatorStateMachine { name = "Mixed Conditions" };
            var offLow = stateMachine.AddState("Off Low", new Vector3(300f, 0f));
            offLow.motion = CreateClip(controller, "Off Low", "Mixed/OffLow", 0f);
            offLow.writeDefaultValues = true;
            var offHigh = stateMachine.AddState("Off High", new Vector3(300f, 70f));
            offHigh.motion = CreateClip(controller, "Off High", "Mixed/OffHigh", 1f);
            offHigh.writeDefaultValues = true;
            var onLow = stateMachine.AddState("On Low", new Vector3(300f, 140f));
            onLow.motion = CreateClip(controller, "On Low", "Mixed/OnLow", 2f);
            onLow.writeDefaultValues = true;
            var onHigh = stateMachine.AddState("On High", new Vector3(300f, 210f));
            onHigh.motion = CreateClip(controller, "On High", "Mixed/OnHigh", 3f);
            onHigh.writeDefaultValues = true;
            stateMachine.defaultState = offLow;

            AddAnyStateMixedTransition(stateMachine, offLow, false, AnimatorConditionMode.Less, 1f);
            AddAnyStateMixedTransition(stateMachine, offHigh, false, AnimatorConditionMode.Greater, 0f);
            AddAnyStateMixedTransition(stateMachine, onLow, true, AnimatorConditionMode.Less, 1f);
            AddAnyStateMixedTransition(stateMachine, onHigh, true, AnimatorConditionMode.Greater, 0f);
            AddLayer(controller, "Mixed Conditions", stateMachine);
            AddSingleMotionLayer(controller, "Other", "Mixed/Other", 0f);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                UpgradeBoolParametersToFloat = true,
                UpgradeIntParametersToFloat = true,
                MaxPriorityCombinations = 64,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.FiniteParameterTable));
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.parameters.First(parameter => parameter.name == "Choice").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
        }

        [Test]
        public void SkipsFloatConditionsUnlessApproximationIsEnabled()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Amount",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
            AddSingleFloatConditionLayer(controller, "Float Logic", "Amount");
            AddSingleMotionLayer(controller, "Other", "Other", 1f);
            AssetDatabase.SaveAssets();

            var strictReport = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions
            {
                AllowFloatConditionApproximation = false,
                UpgradeBoolParametersToFloat = true,
                UpgradeIntParametersToFloat = true,
            });
            Assert.That(strictReport.Layers[0].CanConvert, Is.False);
            Assert.That(strictReport.Layers[0].Reasons.Any(reason => reason.Contains("Float 参数")), Is.True);

            var approximateReport = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions
            {
                AllowFloatConditionApproximation = true,
                UpgradeBoolParametersToFloat = true,
                UpgradeIntParametersToFloat = true,
            });
            Assert.That(approximateReport.Layers[0].CanConvert, Is.True);
        }

        [Test]
        public void RejectsNarrowOverlappingFloatStateTransitions()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "NarrowFloat",
                type = AnimatorControllerParameterType.Float,
            });
            var stateMachine = new AnimatorStateMachine { name = "Narrow Float Overlap" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "Narrow First", "NarrowFloat/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "Narrow Second", "NarrowFloat/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;

            var toSecond = first.AddTransition(second);
            toSecond.hasExitTime = false;
            toSecond.duration = 0f;
            toSecond.AddCondition(AnimatorConditionMode.Greater, 0.5f, "NarrowFloat");
            var toFirst = second.AddTransition(first);
            toFirst.hasExitTime = false;
            toFirst.duration = 0f;
            toFirst.AddCondition(AnimatorConditionMode.Less, 0.5005f, "NarrowFloat");
            AddLayer(controller, "Narrow Float Overlap", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions
            {
                AllowFloatConditionApproximation = true,
            });

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason =>
                reason.Contains("重叠") || reason.Contains("历史") || reason.Contains("Transition")), Is.True);
        }

        [Test]
        public void ConvertsWriteDefaultsOnNullMotionStateAsEmptyOutput()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "EmptyGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "Empty Motion Toggle" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = null;
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, "Empty Motion On", "EmptyMotion/On", 1f);
            on.writeDefaultValues = true;
            stateMachine.defaultState = off;
            var toOff = stateMachine.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, "EmptyGate");
            var toOn = stateMachine.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, "EmptyGate");
            AddLayer(controller, "Empty Motion Toggle", stateMachine);
            AddSingleMotionLayer(controller, "Empty Motion Other", "EmptyMotion/Other", 2f);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
            Assert.That(AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }).GeneratedLayers,
                Has.Count.EqualTo(1));
        }

        [Test]
        public void RejectsWriteDefaultsOffNullMotionState()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Empty Motion WD Off" };
            var state = stateMachine.AddState("Empty", new Vector3(300f, 0f));
            state.motion = null;
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;
            AddLayer(controller, "Empty Motion WD Off", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("Write Defaults")), Is.True);
        }

        [Test]
        public void UnreachableStaticStatesCollapseToDefaultMotion()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Static" };
            var defaultState = stateMachine.AddState("Default", new Vector3(300f, 0f));
            defaultState.motion = CreateClip(controller, "Default", "Default", 1f);
            defaultState.writeDefaultValues = true;
            var unreachable = stateMachine.AddState("Unreachable", new Vector3(300f, 80f));
            unreachable.motion = CreateClip(controller, "Unreachable", "Unreachable", 1f);
            unreachable.writeDefaultValues = true;
            stateMachine.defaultState = defaultState;
            AddLayer(controller, "Static", stateMachine);
            AddSingleMotionLayer(controller, "Other", "Other", 1f);
            AssetDatabase.SaveAssets();

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());
            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
        }

        [Test]
        public void CollapsesExitTimeAndEntryWhenAllStateEffectsMatch()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Equivalent Timed States" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "First Effect", "Equivalent/Target", 1f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "Second Effect", "Equivalent/Target", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;

            var timedTransition = first.AddTransition(second);
            timedTransition.hasExitTime = true;
            timedTransition.exitTime = 0.5f;
            timedTransition.duration = 0.25f;
            timedTransition.hasFixedDuration = false;
            stateMachine.AddEntryTransition(second);
            AddLayer(controller, "Equivalent Timed States", stateMachine);
            AddSingleMotionLayer(controller, "Other", "Equivalent/Other", 0f);
            AssetDatabase.SaveAssets();

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
            Assert.That(report.Layers[0].Warnings.Any(warning => warning.Contains("可观测效果")), Is.True);
        }

        [Test]
        public void CollapsesStaticClipsWithDifferentTimingMetadataWhenOutputMatches()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Equivalent Static Metadata" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "Static First", "Equivalent/Static", 1f);
            first.writeDefaultValues = true;
            first.speed = 1f;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "Static Second", "Equivalent/Static", 1f);
            second.writeDefaultValues = true;
            second.speed = 2f;
            var secondClip = (AnimationClip)second.motion;
            secondClip.frameRate = 30f;
            var secondSettings = AnimationUtility.GetAnimationClipSettings(secondClip);
            secondSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(secondClip, secondSettings);
            stateMachine.defaultState = first;

            var transition = first.AddTransition(second);
            transition.hasExitTime = true;
            transition.exitTime = 0.25f;
            transition.duration = 0.2f;
            stateMachine.AddEntryTransition(second);
            AddLayer(controller, "Equivalent Static Metadata", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
        }

        [Test]
        public void PrunesUnreachableStateBeforeAnalyzingTransitions()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Gate",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "Reachability" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, "Off", "Reachable/Off", 0f);
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, "On", "Reachable/On", 1f);
            on.writeDefaultValues = true;
            var dead = stateMachine.AddState("Dead Unsupported", new Vector3(300f, 160f));
            dead.motion = CreateClip(controller, "Dead", "Reachable/Dead", 1f);
            dead.writeDefaultValues = true;
            dead.speedParameterActive = true;
            dead.speedParameter = "MissingSpeed";
            stateMachine.defaultState = off;

            var toOff = stateMachine.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, "Gate");
            var toOn = stateMachine.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            AddLayer(controller, "Reachability", stateMachine);
            AddSingleMotionLayer(controller, "Other", "Reachable/Other", 0f);
            AssetDatabase.SaveAssets();

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
        }

        [Test]
        public void DoesNotCollapseExitTimeWhenStateEffectsDiffer()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Different Timed States" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "First Effect", "Different/Target", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "Second Effect", "Different/Target", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var timedTransition = first.AddTransition(second);
            timedTransition.hasExitTime = true;
            timedTransition.exitTime = 0.5f;
            timedTransition.duration = 0.25f;
            AddLayer(controller, "Different Timed States", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("Exit") || reason.Contains("Transition")), Is.True);
        }

        [Test]
        public void DoesNotCollapseSameDynamicMotionAcrossTimedStateTransitions()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Same Dynamic Timed States" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateTimedClip(controller, "First Dynamic", "SameDynamic/Target", 0f, 1f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateTimedClip(controller, "Second Dynamic", "SameDynamic/Target", 0f, 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var timedTransition = first.AddTransition(second);
            timedTransition.hasExitTime = true;
            timedTransition.exitTime = 0.5f;
            timedTransition.duration = 0f;
            AddLayer(controller, "Same Dynamic Timed States", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason =>
                reason.Contains("时间") || reason.Contains("Exit") || reason.Contains("Transition")), Is.True);
        }

            [Test]
            public void IgnoresMutedExitTransitionWhenProvingEquivalentStaticStates()
            {
                var controller = CreateController();
                var stateMachine = new AnimatorStateMachine { name = "Muted Exit Equivalent" };
                var first = stateMachine.AddState("First", new Vector3(300f, 0f));
                first.motion = CreateClip(controller, "Muted Exit First", "MutedExit/Target", 1f);
                first.writeDefaultValues = true;
                var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
                second.motion = CreateClip(controller, "Muted Exit Second", "MutedExit/Target", 1f);
                second.writeDefaultValues = true;
                stateMachine.defaultState = first;
                var mutedExit = first.AddExitTransition();
                mutedExit.mute = true;
                AddLayer(controller, "Muted Exit Equivalent", stateMachine);

                var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

                Assert.That(report.Layers[0].CanConvert, Is.True);
                Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
            }

        [Test]
        public void DoesNotDeduplicateEquivalentAnimationEventLayers()
        {
            var controller = CreateController();
            var firstClip = CreateClip(controller, "Event First", "EventTarget", 1f);
            firstClip.events = new[] { new AnimationEvent { time = 0f, functionName = "OnEvent" } };
            var secondClip = CreateClip(controller, "Event Second", "EventTarget", 1f);
            secondClip.events = new[] { new AnimationEvent { time = 0f, functionName = "OnEvent" } };
            AddMotionLayer(controller, "Event First", firstClip);
            AddMotionLayer(controller, "Event Second", secondClip);

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = true,
                ResolveOverridePriorityWithStaticComposite = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);

            Assert.That(issues, Is.Not.Empty);
            Assert.That(issues.Any(issue => issue.Contains("时间") || issue.Contains("重复")), Is.True);
        }

        [Test]
        public void ConvertsPureStaticNestedStateMachine()
        {
            var controller = CreateController();
            var root = new AnimatorStateMachine { name = "Nested Root" };
            var nested = new AnimatorStateMachine { name = "Nested Child" };
            var nestedState = nested.AddState("Nested Default", new Vector3(300f, 0f));
            nestedState.motion = CreateClip(controller, "Nested", "NestedTarget", 1f);
            nestedState.writeDefaultValues = true;
            var nestedTimedState = nested.AddState("Nested Timed Equivalent", new Vector3(300f, 80f));
            nestedTimedState.motion = CreateClip(controller, "Nested Timed Equivalent", "NestedTarget", 1f);
            nestedTimedState.writeDefaultValues = true;
            nested.defaultState = nestedState;
            var nestedTransition = nestedState.AddTransition(nestedTimedState);
            nestedTransition.hasExitTime = true;
            nestedTransition.exitTime = 0.5f;
            nestedTransition.duration = 0.25f;
            AssetDatabase.AddObjectToAsset(nested, controller);
            root.AddStateMachine(nested, new Vector3(300f, 0f));
            root.AddEntryTransition(nested);
            AddLayer(controller, "Nested", root);
            AddSingleMotionLayer(controller, "Other", "Other", 1f);
            AssetDatabase.SaveAssets();

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
        }

        [Test]
        public void ConvertsFiniteToggleInsideTransparentNestedStateMachineWrapper()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "NestedGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var root = new AnimatorStateMachine { name = "Nested Toggle Wrapper Root" };
            var child = new AnimatorStateMachine { name = "Nested Toggle Wrapper Child" };
            var off = child.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, "Nested Toggle Off", "NestedToggle/Off", 0f);
            off.writeDefaultValues = true;
            var on = child.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, "Nested Toggle On", "NestedToggle/On", 1f);
            on.writeDefaultValues = true;
            child.defaultState = off;
            var toOff = child.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, "NestedGate");
            var toOn = child.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, "NestedGate");
            AssetDatabase.AddObjectToAsset(child, controller);
            root.AddStateMachine(child, new Vector3(300f, 0f));
            root.AddEntryTransition(child);
            AddLayer(controller, "Nested Toggle Wrapper", root);
            AddSingleMotionLayer(controller, "Nested Toggle Other", "NestedToggle/Other", 1f);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.layers, Has.Length.EqualTo(1));
        }

        [Test]
        public void DoesNotDropReachableNestedStateMachineWithDifferentOutput()
        {
            var controller = CreateController();
            var root = new AnimatorStateMachine { name = "Nested Reachability Root" };
            var rootState = root.AddState("Root Output", new Vector3(300f, 0f));
            rootState.motion = CreateClip(controller, "Root Output", "NestedReachability/Root", 0f);
            rootState.writeDefaultValues = true;
            root.defaultState = rootState;

            var nested = new AnimatorStateMachine { name = "Nested Reachability Child" };
            var nestedState = nested.AddState("Nested Output", new Vector3(300f, 0f));
            nestedState.motion = CreateClip(controller, "Nested Output", "NestedReachability/Nested", 1f);
            nestedState.writeDefaultValues = true;
            nested.defaultState = nestedState;
            root.AddStateMachine(nested, new Vector3(300f, 80f));
            root.AddEntryTransition(nested);
            AssetDatabase.AddObjectToAsset(nested, controller);
            AddLayer(controller, "Nested Reachability", root);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("嵌套")), Is.True);
        }

        [Test]
        public void IgnoresUnreachableNestedStateMachineWhenDirectDefaultIsStable()
        {
            var controller = CreateController();
            var root = new AnimatorStateMachine { name = "Unreachable Nested Root" };
            var rootState = root.AddState("Root Default", new Vector3(300f, 0f));
            rootState.motion = CreateClip(controller, "Root Default", "Unreachable/Root", 0f);
            rootState.writeDefaultValues = true;
            root.defaultState = rootState;

            var deadMachine = new AnimatorStateMachine { name = "Unreachable Child" };
            var deadState = deadMachine.AddState("Dead Output", new Vector3(300f, 0f));
            deadState.motion = CreateClip(controller, "Dead Output", "Unreachable/Dead", 1f);
            deadState.writeDefaultValues = true;
            deadMachine.defaultState = deadState;
            AssetDatabase.AddObjectToAsset(deadMachine, controller);
            root.AddStateMachine(deadMachine, new Vector3(300f, 80f));
            AddLayer(controller, "Unreachable Nested", root);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
        }

        [Test]
        public void ConvertsStateDependentGraphWhenAllSourcesConvergeToOneOutput()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Gate",
                type = AnimatorControllerParameterType.Bool,
            });

            var stateMachine = new AnimatorStateMachine { name = "Converging StateMachine" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "First", "Converging/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 70f));
            second.motion = CreateClip(controller, "Second", "Converging/Second", 0f);
            second.writeDefaultValues = true;
            var target = stateMachine.AddState("Target", new Vector3(300f, 140f));
            target.motion = CreateClip(controller, "Target", "Converging/Target", 1f);
            target.writeDefaultValues = true;
            stateMachine.defaultState = first;

            AddBooleanPairTransition(stateMachine, first, target, "Gate");
            AddBooleanPairTransition(stateMachine, second, target, "Gate");
            AddLayer(controller, "Converging", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
        }

        [Test]
        public void EnumeratesAllBooleanCombinationsWhenStateGraphUsesTwoParameters()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "GateA",
                type = AnimatorControllerParameterType.Bool,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "GateB",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "Two Gate Convergence" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "First", "TwoGate/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 70f));
            second.motion = CreateClip(controller, "Second", "TwoGate/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;

            var toSecond = first.AddTransition(second);
            toSecond.hasExitTime = false;
            toSecond.duration = 0f;
            toSecond.offset = 0f;
            toSecond.AddCondition(AnimatorConditionMode.If, 0f, "GateA");
            toSecond.AddCondition(AnimatorConditionMode.If, 0f, "GateB");
            var secondToFirstA = second.AddTransition(first);
            secondToFirstA.hasExitTime = false;
            secondToFirstA.duration = 0f;
            secondToFirstA.offset = 0f;
            secondToFirstA.AddCondition(AnimatorConditionMode.If, 0f, "GateA");
            secondToFirstA.AddCondition(AnimatorConditionMode.IfNot, 0f, "GateB");
            AddLayer(controller, "Two Gate Convergence", stateMachine);
            AddSingleMotionLayer(controller, "Other", "TwoGate/Other", 0f);

            var options = new AnimatorOptimizerOptions
            {
                MaxBooleanTruthTableEntries = 4,
                EnableAapStateRegisterOptimization = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister));
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);
            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers[0].name, Does.Contain("Update"));
            Assert.That(controller.layers[1].name, Does.Contain("Output"));
        }

        [Test]
        public void MergesDisjointStaticLayerCurvesIntoOneClip()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Left", "Disjoint/Left", 1f);
            AddSingleMotionLayer(controller, "Right", "Disjoint/Right", 2f);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children, Has.Length.EqualTo(1));
            var composite = root.children[0].motion as AnimationClip;
            Assert.That(composite, Is.Not.Null);
            Assert.That(AnimationUtility.GetCurveBindings(composite)
                .Select(binding => binding.path), Does.Contain("Disjoint/Left"));
            Assert.That(AnimationUtility.GetCurveBindings(composite)
                .Select(binding => binding.path), Does.Contain("Disjoint/Right"));
        }

        [Test]
        public void PreservesUniformStateTagWhenStaticLayersMerge()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Tagged First", "Tagged/First", 1f,
                stateTag: "SharedTag");
            AddSingleMotionLayer(controller, "Tagged Second", "Tagged/Second", 2f,
                stateTag: "SharedTag");

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.layers[0].stateMachine.defaultState.tag, Is.EqualTo("SharedTag"));
        }

        [Test]
        public void IgnoresTagOnUnreachableStateWhenAnalyzingLayer()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Unreachable Tag StateMachine" };
            var reachable = stateMachine.AddState("Reachable", new Vector3(300f, 0f));
            reachable.motion = CreateClip(controller, "Reachable Tagged", "ReachableTagged", 1f);
            reachable.tag = "ReachableTag";
            reachable.writeDefaultValues = true;
            var unreachable = stateMachine.AddState("Unreachable", new Vector3(300f, 80f));
            unreachable.motion = CreateClip(controller, "Unreachable Tagged", "UnreachableTagged", 2f);
            unreachable.tag = "DifferentDeadTag";
            unreachable.writeDefaultValues = true;
            stateMachine.defaultState = reachable;
            AddLayer(controller, "Unreachable Tag", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.ConstantMotion));
        }

        [Test]
        public void SeparatesLayersWithDifferentStateTags()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Tagged First", "Tagged/DifferentFirst", 1f,
                stateTag: "FirstTag");
            AddSingleMotionLayer(controller, "Tagged Second", "Tagged/DifferentSecond", 2f,
                stateTag: "SecondTag");

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers.Select(layer => layer.stateMachine.defaultState.tag),
                Does.Contain("FirstTag"));
            Assert.That(controller.layers.Select(layer => layer.stateMachine.defaultState.tag),
                Does.Contain("SecondTag"));
        }

        [Test]
        public void MergesDynamicLayersOnlyWhenTheirTimeAxesMatch()
        {
            var controller = CreateController();
            AddMotionLayer(controller, "Timed Left",
                CreateTimedClip(controller, "Timed Left", "Timed/Left", 0f, 1f));
            AddMotionLayer(controller, "Timed Right",
                CreateTimedClip(controller, "Timed Right", "Timed/Right", 2f, 4f));
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            var composite = root.children.Single().motion as AnimationClip;

            Assert.That(composite, Is.Not.Null);
            var leftBinding = AnimationUtility.GetCurveBindings(composite)
                .Single(binding => binding.path == "Timed/Left");
            var rightBinding = AnimationUtility.GetCurveBindings(composite)
                .Single(binding => binding.path == "Timed/Right");
            Assert.That(AnimationUtility.GetEditorCurve(composite, leftBinding).Evaluate(1f / 60f),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(AnimationUtility.GetEditorCurve(composite, rightBinding).Evaluate(1f / 60f),
                Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void CombinesStaticLayersWithDifferentClipLocalBoundsUsingUnion()
        {
            var controller = CreateController();
            var firstClip = CreateClip(controller, "Bounds First", "Bounds/First", 1f);
            firstClip.localBounds = new Bounds(Vector3.zero, Vector3.one);
            var secondClip = CreateClip(controller, "Bounds Second", "Bounds/Second", 1f);
            secondClip.localBounds = new Bounds(Vector3.one, Vector3.one * 2f);
            AddMotionLayer(controller, "Bounds First", firstClip);
            AddMotionLayer(controller, "Bounds Second", secondClip);

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = true,
                ResolveOverridePriorityWithStaticComposite = true,
            };
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children, Has.Length.EqualTo(1));
            var composite = root.children[0].motion as AnimationClip;
            Assert.That(composite, Is.Not.Null);
            Assert.That(composite.localBounds.Contains(firstClip.localBounds.min), Is.True);
            Assert.That(composite.localBounds.Contains(firstClip.localBounds.max), Is.True);
            Assert.That(composite.localBounds.Contains(secondClip.localBounds.min), Is.True);
            Assert.That(composite.localBounds.Contains(secondClip.localBounds.max), Is.True);
        }

        [Test]
        public void KeepsDisjointDynamicLayersAsIndependentMotionsWhenLengthsDiffer()
        {
            var controller = CreateController();
            var firstClip = CreateTimedClip(controller, "Different Length First", "DifferentLength/First", 0f, 1f);
            var secondClip = new AnimationClip
            {
                name = "Different Length Second",
                frameRate = 60f,
                hideFlags = HideFlags.HideInHierarchy,
            };
            secondClip.SetCurve("DifferentLength/Second", typeof(GameObject), "m_IsActive", new AnimationCurve(
                new Keyframe(0f, 2f),
                new Keyframe(2f, 4f)));
            AssetDatabase.AddObjectToAsset(secondClip, controller);
            AddMotionLayer(controller, "Different Length First", firstClip);
            AddMotionLayer(controller, "Different Length Second", secondClip);

            var options = new AnimatorOptimizerOptions { StrictCurveConflicts = true };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options), Is.Empty);

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children.Select(child => child.motion), Does.Contain(firstClip));
            Assert.That(root.children.Select(child => child.motion), Does.Contain(secondClip));
        }

        [Test]
        public void DeduplicatesSafeSingletonBlendTreeWrappers()
        {
            var controller = CreateController();
            var sharedClip = CreateClip(controller, "Shared", "Singleton/Target", 1f);
            var singletonTree = new BlendTree
            {
                name = "Singleton Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Unused",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            singletonTree.children = new[]
            {
                new ChildMotion
                {
                    motion = sharedClip,
                    threshold = 0f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
            };
            AssetDatabase.AddObjectToAsset(singletonTree, controller);
            AddMotionLayer(controller, "Direct", sharedClip);
            AddMotionLayer(controller, "Wrapped", singletonTree);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children, Has.Length.EqualTo(1));
            Assert.That(root.children[0].motion, Is.SameAs(sharedClip));
        }

        [Test]
        public void FlattensSafeUnnormalizedDirectBlendTreeIntoMergeRoot()
        {
            var controller = CreateController();
            foreach (var parameterName in new[] { "Weight", "PartA", "PartB" })
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = parameterName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f,
                });
            }
            var direct = new BlendTree
            {
                name = "Source Direct",
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
            };
            direct.children = new[]
            {
                new ChildMotion
                {
                    motion = CreateClip(controller, "Part A", "Flatten/PartA", 1f),
                    directBlendParameter = "PartA",
                    timeScale = 1f,
                },
                new ChildMotion
                {
                    motion = CreateClip(controller, "Part B", "Flatten/PartB", 1f),
                    directBlendParameter = "PartB",
                    timeScale = 1f,
                },
            };
            AssetDatabase.AddObjectToAsset(direct, controller);
            var serializedDirect = new SerializedObject(direct);
            serializedDirect.FindProperty("m_NormalizedBlendValues").boolValue = false;
            serializedDirect.ApplyModifiedPropertiesWithoutUndo();
            AddMotionLayer(controller, "Source Direct", direct);
            AddSingleMotionLayer(controller, "Other", "Flatten/Other", 1f);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });
            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children.Any(child => child.directBlendParameter == "PartA"), Is.True);
            Assert.That(root.children.Any(child => child.directBlendParameter == "PartB"), Is.True);
            Assert.That(root.children.Any(child => child.motion is BlendTree &&
                ((BlendTree)child.motion).blendType == BlendTreeType.Direct), Is.False);
        }

        [Test]
        public void DoesNotDeduplicateDirectTreesWithDifferentNormalizationModes()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Weight",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            var normalized = CreateDirectTree(controller, "Normalized", "Weight", true, "Structural/Normalized");
            var unnormalized = CreateDirectTree(controller, "Unnormalized", "Weight", false, "Structural/Unnormalized");
            AddMotionLayer(controller, "Normalized", normalized);
            AddMotionLayer(controller, "Unnormalized", unnormalized);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = false,
                ResolveOverridePriorityWithStaticComposite = false,
            };
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            var root = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
            Assert.That(root.children, Has.Length.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void MergesEquivalentMaskLayersAndResolvesStaticOverridePriority()
        {
            var controller = CreateController();
            var sharedMask = new AvatarMask { name = "Shared Mask" };
            AddSingleMotionLayer(controller, "Lower", "SharedTarget", 1f, sharedMask);
            AddSingleMotionLayer(controller, "Upper", "SharedTarget", 2f, sharedMask);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                MergeLayersWithEquivalentMasks = true,
                ResolveOverridePriorityWithStaticComposite = true,
                MaxPriorityCombinations = 64,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);
            Assert.That(issues, Is.Empty);

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            Assert.That(result.MergedLayerCount, Is.EqualTo(2));
            Assert.That(controller.layers, Has.Length.EqualTo(1));
            Assert.That(controller.layers[0].avatarMask, Is.SameAs(sharedMask));

            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children, Has.Length.EqualTo(1));
            var composite = root.children[0].motion as AnimationClip;
            Assert.That(composite, Is.Not.Null);
            var binding = AnimationUtility.GetCurveBindings(composite)
                .Single(item => item.path == "SharedTarget");
            var curve = AnimationUtility.GetEditorCurve(composite, binding);
            Assert.That(curve.Evaluate(0f), Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void FoldsIrrelevantPriorityDimensionFromHeuristicCompositeTree()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "PriorityA",
                type = AnimatorControllerParameterType.Bool,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "PriorityB",
                type = AnimatorControllerParameterType.Bool,
            });
            AddBooleanToggleLayer(controller, "Priority A", "PriorityA", "PriorityShared");

            var stateMachine = new AnimatorStateMachine { name = "Priority A B Redundant" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, "Priority AB Off", "PriorityShared", 0f);
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, "Priority AB On", "PriorityShared", 2f);
            on.writeDefaultValues = true;
            stateMachine.defaultState = off;

            var offWhenAFalseBFalse = stateMachine.AddAnyStateTransition(off);
            offWhenAFalseBFalse.hasExitTime = false;
            offWhenAFalseBFalse.duration = 0f;
            offWhenAFalseBFalse.AddCondition(AnimatorConditionMode.IfNot, 0f, "PriorityA");
            offWhenAFalseBFalse.AddCondition(AnimatorConditionMode.IfNot, 0f, "PriorityB");
            var offWhenAFalseBTrue = stateMachine.AddAnyStateTransition(off);
            offWhenAFalseBTrue.hasExitTime = false;
            offWhenAFalseBTrue.duration = 0f;
            offWhenAFalseBTrue.AddCondition(AnimatorConditionMode.IfNot, 0f, "PriorityA");
            offWhenAFalseBTrue.AddCondition(AnimatorConditionMode.If, 0f, "PriorityB");
            var onWhenATrue = stateMachine.AddAnyStateTransition(on);
            onWhenATrue.hasExitTime = false;
            onWhenATrue.duration = 0f;
            onWhenATrue.AddCondition(AnimatorConditionMode.If, 0f, "PriorityA");
            AddLayer(controller, "Priority A B Redundant", stateMachine);

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = true,
                ResolveOverridePriorityWithStaticComposite = true,
                MaxPriorityCombinations = 16,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options), Is.Empty);

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            var root = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
            var nestedTrees = root.children.SelectMany(child => EnumerateBlendTrees(child.motion)).ToList();

            Assert.That(nestedTrees.Any(tree => tree.blendParameter == "PriorityA"), Is.True);
            Assert.That(nestedTrees.Any(tree => tree.blendParameter == "PriorityB"), Is.False);
        }

        [Test]
        public void RejectsPriorityFallbackWhenStaticClipMetadataDiffers()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Metadata Lower", "Metadata/Shared", 1f);
            AddSingleMotionLayer(controller, "Metadata Upper", "Metadata/Shared", 2f);
            var upperClip = (AnimationClip)controller.layers[1].stateMachine.defaultState.motion;
            upperClip.frameRate = 30f;
            var upperSettings = AnimationUtility.GetAnimationClipSettings(upperClip);
            upperSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(upperClip, upperSettings);

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = true,
                ResolveOverridePriorityWithStaticComposite = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);

            Assert.That(issues, Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options));
        }

        [Test]
        public void MergesStaticAdditiveLayersBySummingFloatCurves()
        {
            var controller = CreateController();
            AddMotionLayer(controller, "Additive Lower",
                CreateTransformPositionClip(controller, "Additive Lower", "AdditiveTarget", 1f),
                null, AnimatorLayerBlendingMode.Additive);
            AddMotionLayer(controller, "Additive Upper",
                CreateTransformPositionClip(controller, "Additive Upper", "AdditiveTarget", 2f),
                null, AnimatorLayerBlendingMode.Additive);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                ResolveOverridePriorityWithStaticComposite = true,
                MaxPriorityCombinations = 64,
                StrictCurveConflicts = true,
            };
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.layers, Has.Length.EqualTo(1));
            Assert.That(controller.layers[0].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Additive));

            var root = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.children, Has.Length.EqualTo(1));
            var composite = root.children[0].motion as AnimationClip;
            Assert.That(composite, Is.Not.Null);
            var binding = AnimationUtility.GetCurveBindings(composite)
                .Single(item => item.path == "AdditiveTarget");
            var curve = AnimationUtility.GetEditorCurve(composite, binding);
            Assert.That(curve.Evaluate(0f), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void RejectsOverlappingAdditiveRotationCurves()
        {
            var controller = CreateController();
            AddMotionLayer(controller, "Additive Rotation Lower",
                CreateTransformCurveClip(controller, "Additive Rotation Lower", "AdditiveRotation", "m_LocalRotation.x", 1f),
                null, AnimatorLayerBlendingMode.Additive);
            AddMotionLayer(controller, "Additive Rotation Upper",
                CreateTransformCurveClip(controller, "Additive Rotation Upper", "AdditiveRotation", "m_LocalRotation.x", 2f),
                null, AnimatorLayerBlendingMode.Additive);

            var options = new AnimatorOptimizerOptions
            {
                ResolveOverridePriorityWithStaticComposite = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);

            Assert.That(issues, Is.Not.Empty);
            Assert.That(issues.Any(issue => issue.Contains("重复动画绑定") || issue.Contains("安全")), Is.True);
        }

        [Test]
        public void RejectsOverlappingAdditiveMuscleAndObjectCurves()
        {
            var controller = CreateController();
            var muscleLower = CreateFloatCurveClip(controller, "Muscle Lower", "MuscleTarget", typeof(Animator), "Muscle.Left", 1f);
            var muscleUpper = CreateFloatCurveClip(controller, "Muscle Upper", "MuscleTarget", typeof(Animator), "Muscle.Left", 2f);
            AddMotionLayer(controller, "Muscle Lower", muscleLower, null, AnimatorLayerBlendingMode.Additive);
            AddMotionLayer(controller, "Muscle Upper", muscleUpper, null, AnimatorLayerBlendingMode.Additive);

            var objectLower = CreateObjectCurveClip(controller, "Object Lower", "ObjectTarget");
            var objectUpper = CreateObjectCurveClip(controller, "Object Upper", "ObjectTarget");
            AddMotionLayer(controller, "Object Lower", objectLower, null, AnimatorLayerBlendingMode.Additive);
            AddMotionLayer(controller, "Object Upper", objectUpper, null, AnimatorLayerBlendingMode.Additive);

            var options = new AnimatorOptimizerOptions
            {
                ResolveOverridePriorityWithStaticComposite = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1, 2, 3 }, options),
                Is.Not.Empty);
        }

        [Test]
        public void DoesNotTreatDelayedObjectReferenceCurvesAsStatic()
        {
            var controller = CreateController();
            var first = CreateObjectCurveClip(controller, "Delayed Object First", "DelayedObject", 1f);
            var second = CreateObjectCurveClip(controller, "Delayed Object Second", "DelayedObject", 2f);
            AddMotionLayer(controller, "Delayed Object First", first);
            AddMotionLayer(controller, "Delayed Object Second", second);

            var options = new AnimatorOptimizerOptions { StrictCurveConflicts = true };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);

            Assert.That(issues, Is.Not.Empty);
            Assert.That(issues.Any(issue => issue.Contains("时间") || issue.Contains("绑定")), Is.True);
        }

        [Test]
        public void RejectsAdditiveReferencePoseWhenAdditiveCurvesOverlap()
        {
            var controller = CreateController();
            AddSingleMotionLayer(
                controller,
                "Reference Pose",
                "AdditiveTarget",
                1f,
                null,
                AnimatorLayerBlendingMode.Additive,
                true);
            AddSingleMotionLayer(
                controller,
                "Regular Additive",
                "AdditiveTarget",
                2f,
                null,
                AnimatorLayerBlendingMode.Additive);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                ResolveOverridePriorityWithStaticComposite = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 }, options);

            Assert.That(issues, Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options));
        }

        [Test]
        public void RejectsTimedAdditiveReferencePoseEvenWhenBindingsAreDisjoint()
        {
            var controller = CreateController();
            var firstClip = CreateTimedClip(controller, "Timed Reference", "Additive/Reference", 0f, 1f);
            var firstSettings = AnimationUtility.GetAnimationClipSettings(firstClip);
            firstSettings.hasAdditiveReferencePose = true;
            AnimationUtility.SetAnimationClipSettings(firstClip, firstSettings);
            AddMotionLayer(controller, "Timed Reference", firstClip, null, AnimatorLayerBlendingMode.Additive);
            AddMotionLayer(controller, "Timed Regular",
                CreateTimedClip(controller, "Timed Regular", "Additive/Regular", 0f, 1f),
                null,
                AnimatorLayerBlendingMode.Additive);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions
            {
                ResolveOverridePriorityWithStaticComposite = true,
            });
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1 },
                new AnimatorOptimizerOptions
                {
                    ResolveOverridePriorityWithStaticComposite = true,
                });

            Assert.That(issues.Any(issue => issue.Contains("时间基准") || issue.Contains("Reference")), Is.True);
        }

        [Test]
        public void GeneratesSeparateLayersForDifferentMaskContents()
        {
            var controller = CreateController();
            var firstMask = new AvatarMask { name = "First Mask" };
            firstMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            var secondMask = new AvatarMask { name = "Second Mask" };
            secondMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
            AddSingleMotionLayer(controller, "First", "MaskedTarget", 1f, firstMask);
            AddSingleMotionLayer(controller, "Second", "MaskedTarget", 2f, secondMask);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, new AnimatorOptimizerOptions());

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(result.GeneratedLayers[0].avatarMask, Is.SameAs(firstMask));
            Assert.That(result.GeneratedLayers[1].avatarMask, Is.SameAs(secondMask));
            Assert.That(result.GeneratedLayers[0].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(result.GeneratedLayers[1].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
        }

        [Test]
        public void PreservesContiguousOverrideAndAdditiveGroupOrder()
        {
            var controller = CreateController();
            AddSingleMotionLayer(
                controller,
                "Override",
                "MixedTarget",
                1f,
                null,
                AnimatorLayerBlendingMode.Override);
            AddSingleMotionLayer(
                controller,
                "Additive",
                "MixedTarget",
                2f,
                null,
                AnimatorLayerBlendingMode.Additive);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, new AnimatorOptimizerOptions());

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[0].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(controller.layers[1].blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Additive));
        }

        [Test]
        public void RejectsInterleavedGroupsThatShareBindings()
        {
            var controller = CreateController();
            AddSingleMotionLayer(
                controller,
                "Override Lower",
                "InterleavedTarget",
                1f,
                null,
                AnimatorLayerBlendingMode.Override);
            AddSingleMotionLayer(
                controller,
                "Additive Middle",
                "InterleavedTarget",
                2f,
                null,
                AnimatorLayerBlendingMode.Additive);
            AddSingleMotionLayer(
                controller,
                "Override Upper",
                "InterleavedTarget",
                3f,
                null,
                AnimatorLayerBlendingMode.Override);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions { StrictCurveConflicts = true };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0, 1, 2 }, options);

            Assert.That(issues.Any(issue => issue.Contains("交错")), Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1, 2 }, options));
        }

        [Test]
        public void AapValueClipUsesAnimatorParameterBinding()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Output",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
            AssetDatabase.SaveAssets();

            var clip = AnimatorOptimizerAapBuilder.CreateAapValueClip(controller, "Output", 1f);
            var binding = AnimationUtility.GetCurveBindings(clip).Single();

            Assert.That(binding.path, Is.EqualTo(string.Empty));
            Assert.That(binding.type, Is.EqualTo(typeof(Animator)));
            Assert.That(binding.propertyName, Is.EqualTo("Output"));
            Assert.That(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f), Is.EqualTo(1f));
        }

        [Test]
        public void BuildsAapLogicAndMathWithBlendTreeWeights()
        {
            var controller = CreateController();
            foreach (var name in new[] { "A", "B", "Output", "Weight" })
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = name,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = name == "Weight" ? 1f : 0f,
                });
            }
            AssetDatabase.SaveAssets();

            var truthTable = AnimatorOptimizerAapBuilder.BuildBooleanTruthTable(
                controller,
                "Output",
                new[] { "A", "B" },
                new Dictionary<int, float>
                {
                    [0] = 0f,
                    [1] = 0f,
                    [2] = 0f,
                    [3] = 1f,
                });
            var addition = AnimatorOptimizerAapBuilder.BuildAddition(
                controller,
                "Output",
                "A",
                "B",
                new AnimatorOptimizerAapRange(0f, 1f),
                new AnimatorOptimizerAapRange(0f, 1f),
                "Weight");
            var multiplication = AnimatorOptimizerAapBuilder.BuildMultiplication(
                controller,
                "Output",
                "A",
                "B",
                new AnimatorOptimizerAapRange(0f, 1f),
                new AnimatorOptimizerAapRange(0f, 1f));
            var division = AnimatorOptimizerAapBuilder.BuildOnePlusDivision(
                controller,
                "Output",
                "A",
                "B",
                new AnimatorOptimizerAapRange(0f, 1f),
                "Weight");

            Assert.That(truthTable.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(truthTable.blendParameter, Is.EqualTo("A"));
            Assert.That((truthTable.children[1].motion as BlendTree).blendParameter, Is.EqualTo("B"));
            Assert.That(addition.blendType, Is.EqualTo(BlendTreeType.Direct));
            Assert.That(addition.children, Has.Length.EqualTo(2));
            Assert.That(multiplication.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That((multiplication.children[0].motion as BlendTree).blendParameter, Is.EqualTo("B"));
            Assert.That(division.blendType, Is.EqualTo(BlendTreeType.Direct));
            var serializedDivision = new SerializedObject(division);
            Assert.That(serializedDivision.FindProperty("m_NormalizedBlendValues").boolValue, Is.True);
        }

        [Test]
        public void RejectsAapTruthTablesBeyondFiniteBudgetBeforeBitShift()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Output",
                type = AnimatorControllerParameterType.Float,
            });
            var inputs = Enumerable.Range(0, 13).Select(index => "AAP/Input" + index).ToArray();
            foreach (var input in inputs)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = input,
                    type = AnimatorControllerParameterType.Float,
                });
            }

            Assert.Throws<ArgumentException>(() => AnimatorOptimizerAapBuilder.BuildBooleanTruthTable(
                controller,
                "Output",
                inputs,
                new Dictionary<int, float>()));
        }

        [Test]
        public void BuildsAapStateRegisterFeedbackAndOutputTrees()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "StateRegister",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Gate",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
            var firstMotion = CreateClip(controller, "State 0", "Register/First", 0f);
            var secondMotion = CreateClip(controller, "State 1", "Register/Second", 1f);
            AssetDatabase.SaveAssets();

            var update = AnimatorOptimizerAapBuilder.BuildBooleanStateRegisterUpdate(
                controller,
                "StateRegister",
                new[] { "Gate" },
                2,
                0,
                new Dictionary<int, IReadOnlyDictionary<int, int>>
                {
                    [0] = new Dictionary<int, int> { [0] = 0, [1] = 1 },
                    [1] = new Dictionary<int, int> { [0] = 1, [1] = 0 },
                });
            var output = AnimatorOptimizerAapBuilder.BuildStateOutputTree(
                controller,
                "StateRegister",
                new Motion[] { firstMotion, secondMotion });

            Assert.That(update.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(update.blendParameter, Is.EqualTo("StateRegister"));
            Assert.That(update.children, Has.Length.EqualTo(2));
            Assert.That((update.children[0].motion as BlendTree).blendParameter, Is.EqualTo("Gate"));
            Assert.That(output.blendParameter, Is.EqualTo("StateRegister"));
            Assert.That(output.children[1].motion, Is.SameAs(secondMotion));
        }

        [Test]
        public void ConvertsStateHistoryThroughAapStateRegister()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Gate",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            var stateMachine = new AnimatorStateMachine { name = "State Register Source" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "First", "StateRegister/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "Second", "StateRegister/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;

            var toSecond = first.AddTransition(second);
            toSecond.hasExitTime = false;
            toSecond.duration = 0f;
            toSecond.offset = 0f;
            toSecond.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            AddLayer(controller, "State Register Source", stateMachine);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
                UpgradeBoolParametersToFloat = true,
                StrictCurveConflicts = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister));
            Assert.That(AnimatorOptimizerConverter.GetSelectionIssues(report, new[] { 0 }, options), Is.Empty);

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[0].name, Does.Contain("Update"));
            Assert.That(controller.layers[1].name, Does.Contain("Output"));
            Assert.That(controller.layers[1].stateMachine.defaultState.motion, Is.TypeOf<BlendTree>());
            Assert.That(((BlendTree)controller.layers[1].stateMachine.defaultState.motion).blendParameter,
                Does.StartWith("AO/StateRegister/0"));
            Assert.That(controller.parameters.First(parameter => parameter.name.StartsWith("AO/StateRegister/0"))
                .type, Is.EqualTo(AnimatorControllerParameterType.Float));
        }

        [Test]
        public void InitializesAapStateRegisterFromEntryTransition()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Gate",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "AAP Entry Source" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "AAP Entry First", "AAPEntry/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "AAP Entry Second", "AAPEntry/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var entry = stateMachine.AddEntryTransition(second);
            entry.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            var toSecond = first.AddTransition(second);
            toSecond.hasExitTime = false;
            toSecond.duration = 0f;
            toSecond.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            var secondToFirst = second.AddTransition(first);
            secondToFirst.hasExitTime = false;
            secondToFirst.duration = 0f;
            secondToFirst.AddCondition(AnimatorConditionMode.IfNot, 0f, "Gate");
            AddLayer(controller, "AAP Entry Source", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
            });

            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister));
            Assert.That(report.Layers[0].CanConvert, Is.True);
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
                UpgradeBoolParametersToFloat = true,
            });
            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name.StartsWith("AO/StateRegisterInitialized/")), Is.True);
        }

        [Test]
        public void PreservesMmdBehaviourWhenAapStateRegisterConverts()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AapMmdGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "AAP MMD Source" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "AAP MMD First", "AAPMmd/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "AAP MMD Second", "AAPMmd/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var transition = first.AddTransition(second);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "AapMmdGate");
            var behaviour = stateMachine.AddStateMachineBehaviour<ModularAvatarMMDLayerControl>();
            behaviour.DisableInMMDMode = false;
            AddLayer(controller, "AAP MMD Source", stateMachine);

            var options = new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
                NormalizeAlwaysOneParameters = false,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister));

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers[1].stateMachine.behaviours, Has.Length.EqualTo(1));
            Assert.That(controller.layers[1].stateMachine.behaviours[0].GetType().Name,
                Is.EqualTo("ModularAvatarMMDLayerControl"));
        }

        [Test]
        public void ConvertsIntStateHistoryThroughAapFiniteDecisionTree()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Choice",
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0,
            });
            var stateMachine = new AnimatorStateMachine { name = "AAP Int State Source" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.motion = CreateClip(controller, "AAP Int First", "AAPInt/First", 0f);
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.motion = CreateClip(controller, "AAP Int Second", "AAPInt/Second", 1f);
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;

            var toSecond = first.AddTransition(second);
            toSecond.hasExitTime = false;
            toSecond.duration = 0f;
            toSecond.offset = 0f;
            toSecond.AddCondition(AnimatorConditionMode.Greater, 0f, "Choice");
            var toFirst = second.AddTransition(first);
            toFirst.hasExitTime = false;
            toFirst.duration = 0f;
            toFirst.offset = 0f;
            toFirst.AddCondition(AnimatorConditionMode.Less, 0f, "Choice");
            AddLayer(controller, "AAP Int State Source", stateMachine);

            var options = new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
                UpgradeIntParametersToFloat = true,
                MaxPriorityCombinations = 16,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister));
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);
            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.parameters.First(parameter => parameter.name == "Choice").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
            var update = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.That(update, Is.Not.Null);
            Assert.That(update.children.Select(child => child.motion).OfType<BlendTree>()
                .Any(tree => tree.blendParameter == "Choice"), Is.True);
        }

        [Test]
        public void ContinuesMergingMaStyleExistingDirectAndFreeformBlendTrees()
        {
            var controller = CreateController();
            foreach (var name in new[] { "__ModularAvatarInternal/One", "MA/Toggle", "MA/X", "MA/Y" })
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = name,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = name.Contains("One") ? 1f : 0f,
                });
            }

            var freeform = new BlendTree
            {
                name = "MA Responsive Freeform",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MA/X",
                blendParameterY = "MA/Y",
                hideFlags = HideFlags.HideInHierarchy,
            };
            freeform.children = new[]
            {
                new ChildMotion
                {
                    motion = CreateClip(controller, "MA Freeform A", "MA/FreeformA", 1f),
                    position = new Vector2(0f, 0f),
                },
                new ChildMotion
                {
                    motion = CreateClip(controller, "MA Freeform B", "MA/FreeformB", 2f),
                    position = new Vector2(1f, 1f),
                },
            };
            AssetDatabase.AddObjectToAsset(freeform, controller);

            var mergedTree = new BlendTree
            {
                name = "Modular Avatar: Merge Blend Tree",
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
            };
            mergedTree.children = new[]
            {
                new ChildMotion
                {
                    motion = freeform,
                    directBlendParameter = "__ModularAvatarInternal/One",
                    timeScale = 1f,
                },
            };
            AssetDatabase.AddObjectToAsset(mergedTree, controller);
            AddMotionLayer(controller, "Modular Avatar: Merge Blend Tree", mergedTree);
            AddSingleMotionLayer(controller, "MA Other Property", "MA/Other", 1f);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                StrictCurveConflicts = true,
                NormalizeAlwaysOneParameters = true,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].CanConvert, Is.True);

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            var root = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(root.blendParameter, Is.EqualTo("__ModularAvatarInternal/One"));
            Assert.That(root.children.Any(child =>
                child.directBlendParameter == "__ModularAvatarInternal/One" && child.motion is BlendTree), Is.True);
            Assert.That(root.children.Select(child => child.motion).OfType<BlendTree>()
                .Any(tree => tree.blendType == BlendTreeType.FreeformCartesian2D), Is.True);
        }

        [Test]
        public void PreservesEquivalentMaMmdLayerControlWhenMergingResponsiveLayers()
        {
            var controller = CreateController();
            var firstMachine = new AnimatorStateMachine { name = "MA MMD First StateMachine" };
            var firstState = firstMachine.AddState("First", new Vector3(300f, 0f));
            firstState.motion = CreateClip(controller, "MA MMD First", "MA/MmdFirst", 1f);
            firstState.writeDefaultValues = true;
            firstMachine.defaultState = firstState;
            firstMachine.AddStateMachineBehaviour<ModularAvatarMMDLayerControl>().DisableInMMDMode = false;
            AddLayer(controller, "MA Responsive: First", firstMachine);

            var secondMachine = new AnimatorStateMachine { name = "MA MMD Second StateMachine" };
            var secondState = secondMachine.AddState("Second", new Vector3(300f, 0f));
            secondState.motion = CreateClip(controller, "MA MMD Second", "MA/MmdSecond", 1f);
            secondState.writeDefaultValues = true;
            secondMachine.defaultState = secondState;
            secondMachine.AddStateMachineBehaviour<ModularAvatarMMDLayerControl>().DisableInMMDMode = false;
            AddLayer(controller, "MA Responsive: Second", secondMachine);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.layers[0].stateMachine.behaviours,
                Has.Length.EqualTo(1));
            Assert.That(controller.layers[0].stateMachine.behaviours[0].GetType().Name,
                Is.EqualTo("ModularAvatarMMDLayerControl"));
        }

        [Test]
        public void ConvertsVrcFuryStyleZeroDurationToggleLayer()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "VF/Toggle",
                type = AnimatorControllerParameterType.Bool,
            });
            AddBooleanToggleLayer(controller, "VRCFury Toggle", "VF/Toggle");
            AddSingleMotionLayer(controller, "VRCFury Other", "VRCFury/Other", 1f);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
            Assert.That(AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }).GeneratedLayers,
                Has.Count.EqualTo(1));
        }

        [Test]
        public void ConvertsVrcFuryLocalRemoteStyleGlobalIsLocalToggle()
        {
            var controller = CreateController();
            AddBooleanToggleLayer(controller, "VRCFury Local Remote", "IsLocal");
            AddSingleMotionLayer(controller, "VRCFury Local Remote Other", "VRCFury/LocalRemoteOther", 1f);

            var options = new AnimatorOptimizerOptions { UseVrChatParameterSemantics = true };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options).GeneratedLayers,
                Has.Count.EqualTo(1));
            Assert.That(controller.parameters.Any(parameter => parameter.name == "IsLocal"), Is.False);
        }

        [Test]
        public void ConvertsMaReactiveEntryExitLayerToDecisionTree()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "MA/ReactiveToggle",
                type = AnimatorControllerParameterType.Bool,
            });
            var stateMachine = new AnimatorStateMachine { name = "MA Responsive: Object Active" };
            var defaultState = stateMachine.AddState("<default>", new Vector3(300f, 0f));
            defaultState.motion = CreateClip(controller, "MA Default", "MA/Reactive", 0f);
            defaultState.writeDefaultValues = true;
            var onState = stateMachine.AddState("On", new Vector3(300f, 80f));
            onState.motion = CreateClip(controller, "MA On", "MA/Reactive", 1f);
            onState.writeDefaultValues = true;
            stateMachine.defaultState = defaultState;

            var entry = stateMachine.AddEntryTransition(onState);
            entry.AddCondition(AnimatorConditionMode.If, 0f, "MA/ReactiveToggle");
            var defaultExit = defaultState.AddExitTransition();
            defaultExit.hasExitTime = false;
            defaultExit.duration = 0f;
            defaultExit.AddCondition(AnimatorConditionMode.If, 0f, "MA/ReactiveToggle");
            var onExit = onState.AddExitTransition();
            onExit.hasExitTime = false;
            onExit.duration = 0f;
            onExit.AddCondition(AnimatorConditionMode.IfNot, 0f, "MA/ReactiveToggle");
            AddLayer(controller, "MA Responsive: Object Active", stateMachine);
            AddSingleMotionLayer(controller, "MA Reactive Other", "MA/Other", 1f);

            var options = new AnimatorOptimizerOptions
            {
                UpgradeBoolParametersToFloat = true,
                MaxBooleanTruthTableEntries = 4,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.True);
            Assert.That(report.Layers[0].ConversionKind,
                Is.EqualTo(AnimatorOptimizerConversionKind.BooleanTruthTable));
            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);
            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
        }

        [Test]
        public void RejectsVrcFuryStyleSliderMotionTimeLayer()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "VF/Slider",
                type = AnimatorControllerParameterType.Float,
            });
            var stateMachine = new AnimatorStateMachine { name = "VRCFury Slider StateMachine" };
            var state = stateMachine.AddState("Slider", new Vector3(300f, 0f));
            state.motion = CreateTimedClip(controller, "VRCFury Slider", "VRCFury/Slider", 0f, 1f);
            state.writeDefaultValues = true;
            state.timeParameterActive = true;
            state.timeParameter = "VF/Slider";
            stateMachine.defaultState = state;
            AddLayer(controller, "VRCFury Slider", stateMachine);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("时间") || reason.Contains("Time")),
                Is.True);
        }

        [Test]
        public void RejectsProxyClipInsteadOfTreatingItAsEmptyOrStatic()
        {
            var controller = CreateController();
            var proxy = CreateClip(controller, "proxy_VRCGesture", "Proxy/Target", 1f);
            AddMotionLayer(controller, "Proxy Layer", proxy);
            AddSingleMotionLayer(controller, "Proxy Other", "Proxy/Other", 1f);

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("proxy")), Is.True);
        }

        [Test]
        public void DetectsExternalAnimationClipAapWriterThroughBlendTreeGraph()
        {
            var externalPath = TestFolder + "/ExternalAap_" + Guid.NewGuid().ToString("N") + ".anim";
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "ExternalGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var external = new AnimationClip { name = "External AAP Writer" };
            external.SetCurve(string.Empty, typeof(Animator), "ExternalGate",
                AnimationCurve.Constant(0f, 1f / 60f, 1f));
            AssetDatabase.CreateAsset(external, externalPath);
            var tree = new BlendTree
            {
                name = "External AAP Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "ExternalGate",
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = new[]
            {
                new ChildMotion { motion = external, threshold = 0f },
                new ChildMotion { motion = CreateClip(controller, "External B", "External/B", 1f), threshold = 1f },
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            AddMotionLayer(controller, "External AAP Tree", tree);
            AddSingleMotionLayer(controller, "External AAP Other", "External/Other", 1f);
            AssetDatabase.SaveAssets();

            try
            {
                var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

                Assert.That(report.Layers[0].CanConvert, Is.False);
                Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("AAP") || reason.Contains("Animator")),
                    Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(externalPath);
            }
        }

        [Test]
        public void RejectsBlendTreeParameterThatIsMissingOrExternallyControlled()
        {
            var missingController = CreateController();
            var missingTree = new BlendTree
            {
                name = "Missing Tree Parameter",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Missing/BlendParameter",
                hideFlags = HideFlags.HideInHierarchy,
            };
            missingTree.children = new[]
            {
                new ChildMotion { motion = CreateClip(missingController, "Missing A", "Tree/A", 0f), threshold = 0f },
                new ChildMotion { motion = CreateClip(missingController, "Missing B", "Tree/B", 1f), threshold = 1f },
            };
            AssetDatabase.AddObjectToAsset(missingTree, missingController);
            AddMotionLayer(missingController, "Missing Tree", missingTree);
            AddSingleMotionLayer(missingController, "Missing Other", "Tree/Other", 1f);

            var missingReport = AnimatorOptimizerAnalyzer.Analyze(missingController, new AnimatorOptimizerOptions());
            Assert.That(missingReport.Layers[0].CanConvert, Is.False);
            Assert.That(missingReport.Layers[0].Reasons.Any(reason => reason.Contains("不存在")), Is.True);

            var globalController = CreateController();
            globalController.AddParameter(new AnimatorControllerParameter
            {
                name = "Viseme",
                type = AnimatorControllerParameterType.Int,
            });
            var globalTree = new BlendTree
            {
                name = "Global Tree Parameter",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Viseme",
                hideFlags = HideFlags.HideInHierarchy,
            };
            globalTree.children = new[]
            {
                new ChildMotion { motion = CreateClip(globalController, "Global A", "Tree/GlobalA", 0f), threshold = 0f },
                new ChildMotion { motion = CreateClip(globalController, "Global B", "Tree/GlobalB", 1f), threshold = 1f },
            };
            AssetDatabase.AddObjectToAsset(globalTree, globalController);
            AddMotionLayer(globalController, "Global Tree", globalTree);
            AddSingleMotionLayer(globalController, "Global Other", "Tree/GlobalOther", 1f);

            var globalOptions = new AnimatorOptimizerOptions { UseVrChatParameterSemantics = true };
            var globalReport = AnimatorOptimizerAnalyzer.Analyze(globalController, globalOptions);
            var globalIssues = AnimatorOptimizerConverter.GetSelectionIssues(
                globalReport, new[] { 0, 1 }, globalOptions);
            Assert.That(globalIssues, Is.Empty);
            Assert.That(AnimatorOptimizerConverter.ConvertInPlace(globalController, new[] { 0, 1 }, globalOptions).GeneratedLayers,
                Has.Count.EqualTo(1));
            Assert.That(globalController.parameters.First(parameter => parameter.name == "Viseme").type,
                Is.EqualTo(AnimatorControllerParameterType.Int));
        }

        [Test]
        public void UpgradesLocalBoolBlendTreeSelectorAsPartOfParameterClosure()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "LocalTreeGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var tree = new BlendTree
            {
                name = "Local Bool Selector",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "LocalTreeGate",
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = new[]
            {
                new ChildMotion { motion = CreateClip(controller, "Tree Off", "Tree/Off", 0f), threshold = 0f },
                new ChildMotion { motion = CreateClip(controller, "Tree On", "Tree/On", 1f), threshold = 1f },
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            AddMotionLayer(controller, "Local Bool Tree", tree);
            AddSingleMotionLayer(controller, "Local Bool Other", "Tree/Other", 1f);

            var options = new AnimatorOptimizerOptions
            {
                UpgradeBoolParametersToFloat = true,
                NormalizeAlwaysOneParameters = false,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].CanConvert, Is.True);

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 }, options);

            Assert.That(controller.parameters.First(parameter => parameter.name == "LocalTreeGate").type,
                Is.EqualTo(AnimatorControllerParameterType.Float));
        }

        [Test]
        public void DetectsEmptyMissingTargetAndAapClipsDifferently()
        {
            var root = new GameObject("AnimatorOptimizer Empty Clip Root");
            var transientClips = new List<AnimationClip>();
            try
            {
                var empty = new AnimationClip();
                transientClips.Add(empty);
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(empty, root.transform), Is.True);

                var missing = CreateMissingPathClip(
                    "Missing One Frame",
                    "Missing/Target",
                    new Keyframe(0f, 0f));
                transientClips.Add(missing);
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(missing, root.transform), Is.True);

                var missingTwoFrames = CreateMissingPathClip(
                    "Missing Two Frames",
                    "Missing/Target",
                    new Keyframe(0f, 0f),
                    new Keyframe(1f / 60f, 1f));
                transientClips.Add(missingTwoFrames);
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(missingTwoFrames, root.transform), Is.True);

                var target = new GameObject("Target");
                target.transform.SetParent(root.transform);
                var existing = new AnimationClip();
                transientClips.Add(existing);
                existing.SetCurve("Target", typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 1f / 60f, 1f));
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(existing, root.transform), Is.False);

                var aap = new AnimationClip();
                transientClips.Add(aap);
                aap.SetCurve(string.Empty, typeof(Animator), "AAP/Output",
                    AnimationCurve.Constant(0f, 1f / 60f, 1f));
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(aap, root.transform), Is.False);
            }
            finally
            {
                foreach (var clip in transientClips)
                    UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CanonicalizesEmptyOneAndTwoFrameMotionsWithoutTimeObservers()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Empty Motion Canonicalization" };
            var oneFrame = stateMachine.AddState("One Frame", new Vector3(300f, 0f));
            var oneFrameClip = CreateMissingPathClip("One Frame", "Missing/One", new Keyframe(0f, 0f));
            AssetDatabase.AddObjectToAsset(oneFrameClip, controller);
            oneFrame.motion = oneFrameClip;
            oneFrame.writeDefaultValues = false;
            var twoFrames = stateMachine.AddState("Two Frames", new Vector3(300f, 80f));
            var twoFrameClip = CreateMissingPathClip(
                "Two Frames", "Missing/Two", new Keyframe(0f, 0f), new Keyframe(1f / 60f, 1f));
            AssetDatabase.AddObjectToAsset(twoFrameClip, controller);
            twoFrames.motion = twoFrameClip;
            twoFrames.writeDefaultValues = false;
            stateMachine.defaultState = oneFrame;
            AddLayer(controller, "Empty Motion Canonicalization", stateMachine);
            AssetDatabase.SaveAssets();

            var root = new GameObject("Empty Motion Root");
            try
            {
                AnimatorOptimizerConverter.CleanupControllerInPlace(controller, root.transform,
                    new AnimatorOptimizerOptions
                    {
                        RemoveEffectivelyEmptyLayers = true,
                        NormalizeAlwaysOneParameters = false,
                    });

                Assert.That(oneFrame.motion, Is.SameAs(twoFrames.motion));
                Assert.That(oneFrame.motion, Is.TypeOf<AnimationClip>());
                Assert.That(AnimatorOptimizerConverter.IsEffectivelyEmptyClip(
                    (AnimationClip)oneFrame.motion, root.transform), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DoesNotCanonicalizeEmptyMotionWhenExitTimeObservesLength()
        {
            var controller = CreateController();
            var stateMachine = new AnimatorStateMachine { name = "Timed Empty Motion" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            var firstClip = CreateMissingPathClip("Timed First", "Missing/Timed", new Keyframe(0f, 0f));
            AssetDatabase.AddObjectToAsset(firstClip, controller);
            first.motion = firstClip;
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            var secondClip = CreateMissingPathClip("Timed Second", "Missing/Timed2", new Keyframe(0f, 0f));
            AssetDatabase.AddObjectToAsset(secondClip, controller);
            second.motion = secondClip;
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var transition = first.AddTransition(second);
            transition.hasExitTime = true;
            transition.exitTime = 0.5f;
            transition.duration = 0f;
            AddLayer(controller, "Timed Empty Motion", stateMachine);
            AssetDatabase.SaveAssets();

            var original = first.motion;
            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = true,
                    NormalizeAlwaysOneParameters = false,
                });

            Assert.That(first.motion, Is.SameAs(original));
        }

        [Test]
        public void RemovesEmptyLayersButKeepsFirstAndBehaviourLayers()
        {
            var controller = CreateController();
            AddEmptyLayer(controller, "First Empty");
            AddEmptyLayer(controller, "Second Empty");
            AddEmptyLayer(controller, "Behaviour Empty", addBehaviour: true);
            AssetDatabase.SaveAssets();

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = true,
                    NormalizeAlwaysOneParameters = false,
                });

            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[0].name, Is.EqualTo("First Empty"));
            Assert.That(controller.layers[1].name, Is.EqualTo("Behaviour Empty"));
        }

        [Test]
        public void KeepsEmptyLayerWithTransition()
        {
            var controller = CreateController();
            AddEmptyLayer(controller, "Base Empty");
            var stateMachine = new AnimatorStateMachine { name = "Transition Empty StateMachine" };
            var first = stateMachine.AddState("First", new Vector3(300f, 0f));
            first.writeDefaultValues = true;
            var second = stateMachine.AddState("Second", new Vector3(300f, 80f));
            second.writeDefaultValues = true;
            stateMachine.defaultState = first;
            var transition = first.AddTransition(second);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "MissingParameter");
            AddLayer(controller, "Transition Empty", stateMachine);

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = true,
                    NormalizeAlwaysOneParameters = false,
                });

            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[1].name, Is.EqualTo("Transition Empty"));
        }

        [Test]
        public void NormalizesAlwaysOneBlendTreeReferencesWithoutTouchingAapReferences()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Internal/AlwaysOne",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Generated/AlwaysOne",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

            var direct = new BlendTree
            {
                name = "AlwaysOne Direct",
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
            };
            direct.children = new[]
            {
                new ChildMotion
                {
                    motion = CreateClip(controller, "AlwaysOne A", "AlwaysOne/A", 1f),
                    directBlendParameter = "Generated/AlwaysOne",
                },
            };
            AssetDatabase.AddObjectToAsset(direct, controller);
            AddMotionLayer(controller, "AlwaysOne", direct);
            AssetDatabase.SaveAssets();

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = false,
                    NormalizeAlwaysOneParameters = true,
                });

            var normalizedTree = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
            Assert.That(normalizedTree.children[0].directBlendParameter, Is.EqualTo("Internal/AlwaysOne"));

            var aapPath = TestFolder + "/AAP_" + Guid.NewGuid().ToString("N") + ".controller";
            var aapController = CreateControllerAtPath(aapPath);
            try
            {
                aapController.AddParameter(new AnimatorControllerParameter
                {
                    name = "Internal/AlwaysOne",
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f,
                });
                aapController.AddParameter(new AnimatorControllerParameter
                {
                    name = "Generated/AlwaysOne",
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f,
                });
                var aapClip = new AnimationClip { name = "AAP AlwaysOne" };
                aapClip.SetCurve(string.Empty, typeof(Animator), "Generated/AlwaysOne",
                    AnimationCurve.Constant(0f, 1f / 60f, 1f));
                AssetDatabase.AddObjectToAsset(aapClip, aapController);
                var aapTree = new BlendTree
                {
                    name = "AAP Direct",
                    blendType = BlendTreeType.Direct,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                aapTree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = aapClip,
                        directBlendParameter = "Generated/AlwaysOne",
                    },
                };
                AssetDatabase.AddObjectToAsset(aapTree, aapController);
                AddMotionLayer(aapController, "AAP AlwaysOne", aapTree);
                AssetDatabase.SaveAssets();

                AnimatorOptimizerConverter.CleanupControllerInPlace(aapController, null,
                    new AnimatorOptimizerOptions
                    {
                        RemoveEffectivelyEmptyLayers = false,
                        NormalizeAlwaysOneParameters = true,
                    });

                var unchangedTree = (BlendTree)aapController.layers[0].stateMachine.defaultState.motion;
                Assert.That(unchangedTree.children[0].directBlendParameter, Is.EqualTo("Generated/AlwaysOne"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(aapPath);
            }
        }

        [Test]
        public void PreservesMmdLayerIndicesAndDoesNotDuplicateDummies()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "FX Base", "MMD/Base", 1f);
            AddSingleMotionLayer(controller, "FX Content", "MMD/Content", 1f);
            AssetDatabase.SaveAssets();

            var options = new AnimatorOptimizerOptions
            {
                RemoveEffectivelyEmptyLayers = true,
                NormalizeAlwaysOneParameters = false,
                PreserveMmdCompatibility = true,
            };
            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null, options);
            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null, options);

            Assert.That(controller.layers, Has.Length.EqualTo(4));
            Assert.That(controller.layers[1].name, Is.EqualTo("MMD Dummy Layer 1"));
            Assert.That(controller.layers[2].name, Is.EqualTo("MMD Dummy Layer 2"));
            Assert.That(controller.layers[1].defaultWeight, Is.EqualTo(0f));
            Assert.That(controller.layers[2].defaultWeight, Is.EqualTo(0f));
        }

        [Test]
        public void DoesNotDeleteExistingMmdDummyLayersWhenOptionIsOff()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Base", "MMD/ExistingBase", 1f);
            AddLayer(controller, "MMD Dummy Layer 1", new AnimatorStateMachine
            {
                name = "MMD Dummy Layer 1 StateMachine",
            });
            AddLayer(controller, "MMD Dummy Layer 2", new AnimatorStateMachine
            {
                name = "MMD Dummy Layer 2 StateMachine",
            });
            AddEmptyLayer(controller, "Removable Empty");

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = true,
                    NormalizeAlwaysOneParameters = false,
                    PreserveMmdCompatibility = false,
                });

            Assert.That(controller.layers.Select(layer => layer.name),
                Does.Contain("MMD Dummy Layer 1"));
            Assert.That(controller.layers.Select(layer => layer.name),
                Does.Contain("MMD Dummy Layer 2"));
            Assert.That(controller.layers.Select(layer => layer.name),
                Does.Not.Contain("Removable Empty"));
        }

        [Test]
        public void KeepsWriteDefaultsOnEmptyLayer()
        {
            var controller = CreateController();
            AddEmptyLayer(controller, "Base Empty");
            var stateMachine = new AnimatorStateMachine { name = "Write Defaults Empty StateMachine" };
            var state = stateMachine.AddState("Write Defaults State", new Vector3(300f, 0f));
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            AddLayer(controller, "Write Defaults Empty", stateMachine);

            AnimatorOptimizerConverter.CleanupControllerInPlace(controller, null,
                new AnimatorOptimizerOptions
                {
                    RemoveEffectivelyEmptyLayers = true,
                    NormalizeAlwaysOneParameters = false,
                });

            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(controller.layers[1].name, Is.EqualTo("Write Defaults Empty"));
        }

        [Test]
        public void LateOptimizationConfigurationIsCaptured()
        {
            var avatar = new GameObject("Animator Optimizer Late Avatar");
            try
            {
                var component = avatar.AddComponent<AnimatorOptimizerComponent>();
                component.executeLateOptimization = true;
                component.targetMode = AnimatorOptimizerComponent.TargetMode.ExplicitController;
                var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);

                Assert.That(config.ExecuteLateOptimization, Is.True);
                Assert.That(config.CreateOptions().ExecuteLateOptimization, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void ProcessorRunsCleanupOnlyWhenNoLayersAreSelected()
        {
            var controller = CreateController();
            AddSingleMotionLayer(controller, "Base", "CleanupOnly/Base", 1f);
            AddEmptyLayer(controller, "Empty Layer");
            AssetDatabase.SaveAssets();

            var avatar = new GameObject("Animator Optimizer Cleanup Avatar");
            try
            {
                var animator = avatar.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var component = avatar.AddComponent<AnimatorOptimizerComponent>();
                component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
                component.animator = animator;
                component.optimizeAllConvertibleLayers = false;
                component.layerIndices.Clear();
                component.removeEffectivelyEmptyLayers = true;
                component.normalizeAlwaysOneParameters = false;
                component.preserveMmdCompatibility = false;
                var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);

                Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar, config), Is.True);
                var generated = animator.runtimeAnimatorController as AnimatorController;
                Assert.That(generated, Is.Not.Null);
                Assert.That(generated, Is.Not.SameAs(controller));
                Assert.That(generated.layers, Has.Length.EqualTo(1));

                AnimatorOptimizerProcessor.RestoreAndCleanup();
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
            }
            finally
            {
                AnimatorOptimizerProcessor.RestoreAndCleanup();
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void LocatesAapLayerByStableMarkerWhenLayerNamesAreDuplicated()
        {
            var controller = CreateController();
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "MarkerGate",
                type = AnimatorControllerParameterType.Bool,
            });
            var aapMachine = new AnimatorStateMachine { name = "Duplicate AAP StateMachine" };
            var aapFirst = aapMachine.AddState("AAP First", new Vector3(300f, 0f));
            aapFirst.motion = CreateClip(controller, "AAP Duplicate First", "Duplicate/AapFirst", 0f);
            aapFirst.writeDefaultValues = true;
            var aapSecond = aapMachine.AddState("AAP Second", new Vector3(300f, 80f));
            aapSecond.motion = CreateClip(controller, "AAP Duplicate Second", "Duplicate/AapSecond", 1f);
            aapSecond.writeDefaultValues = true;
            aapMachine.defaultState = aapFirst;
            var aapTransition = aapFirst.AddTransition(aapSecond);
            aapTransition.hasExitTime = false;
            aapTransition.duration = 0f;
            aapTransition.AddCondition(AnimatorConditionMode.If, 0f, "MarkerGate");
            AddLayer(controller, "Duplicate", aapMachine);
            AddSingleMotionLayer(controller, "Duplicate", "Duplicate/RegularA", 1f);
            AddSingleMotionLayer(controller, "Duplicate", "Duplicate/RegularB", 1f);
            AssetDatabase.SaveAssets();

            var avatar = new GameObject("AAP Duplicate Marker Avatar");
            try
            {
                var animator = avatar.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var component = avatar.AddComponent<AnimatorOptimizerComponent>();
                component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
                component.animator = animator;
                component.optimizeAllConvertibleLayers = false;
                component.layerIndices = new List<int> { 0, 1, 2 };
                component.enableAapStateRegisterOptimization = true;
                component.normalizeAlwaysOneParameters = false;
                var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);

                Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar, config), Is.True);
                var generated = animator.runtimeAnimatorController as AnimatorController;
                Assert.That(generated, Is.Not.Null);
                Assert.That(generated.layers.Any(layer => layer.name.Contains("AAPStateRegister")), Is.True);
                Assert.That(generated.layers.Any(layer => layer.name.Contains("AnimatorOptimizer")), Is.True);
            }
            finally
            {
                AnimatorOptimizerProcessor.RestoreAndCleanup();
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        private AnimatorController CreateController()
        {
            return CreateControllerAtPath(_controllerPath);
        }

        private static AnimatorController CreateControllerAtPath(string path)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            return controller;
        }

        private static void AddBoolTruthTableLayer(
            AnimatorController controller,
            string layerName,
            string parameterA,
            string parameterB)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var states = new List<AnimatorState>();
            for (var index = 0; index < 4; index++)
            {
                var state = stateMachine.AddState("State" + index, new Vector3(300f, index * 70f));
                state.motion = CreateClip(controller, layerName + "_" + index, layerName + "/" + index, index + 1f);
                state.writeDefaultValues = true;
                states.Add(state);
            }
            stateMachine.defaultState = states[0];

            for (var index = 0; index < states.Count; index++)
            {
                var transition = stateMachine.AddAnyStateTransition(states[index]);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.offset = 0f;
                transition.AddCondition(
                    (index & 1) == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                    0f,
                    parameterA);
                transition.AddCondition(
                    (index & 2) == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                    0f,
                    parameterB);
            }
            AddLayer(controller, layerName, stateMachine);
        }

        private static void AddIntPartitionLayer(
            AnimatorController controller,
            string layerName,
            string parameter)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var low = stateMachine.AddState("Low", new Vector3(300f, 0f));
            low.motion = CreateClip(controller, layerName + "_Low", layerName + "/Low", 1f);
            low.writeDefaultValues = true;
            var middle = stateMachine.AddState("Middle", new Vector3(300f, 70f));
            middle.motion = CreateClip(controller, layerName + "_Middle", layerName + "/Middle", 2f);
            middle.writeDefaultValues = true;
            var high = stateMachine.AddState("High", new Vector3(300f, 140f));
            high.motion = CreateClip(controller, layerName + "_High", layerName + "/High", 3f);
            high.writeDefaultValues = true;
            stateMachine.defaultState = middle;

            var lowTransition = stateMachine.AddAnyStateTransition(low);
            lowTransition.hasExitTime = false;
            lowTransition.duration = 0f;
            lowTransition.AddCondition(AnimatorConditionMode.Less, 1f, parameter);
            var middleTransition = stateMachine.AddAnyStateTransition(middle);
            middleTransition.hasExitTime = false;
            middleTransition.duration = 0f;
            middleTransition.AddCondition(AnimatorConditionMode.Equals, 1f, parameter);
            var highTransition = stateMachine.AddAnyStateTransition(high);
            highTransition.hasExitTime = false;
            highTransition.duration = 0f;
            highTransition.AddCondition(AnimatorConditionMode.Greater, 1f, parameter);
            AddLayer(controller, layerName, stateMachine);
        }

        private static void AddSingleFloatConditionLayer(
            AnimatorController controller,
            string layerName,
            string parameter)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, layerName + "_Off", layerName + "/Off", 0f);
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 70f));
            on.motion = CreateClip(controller, layerName + "_On", layerName + "/On", 1f);
            on.writeDefaultValues = true;
            stateMachine.defaultState = off;

            var offTransition = stateMachine.AddAnyStateTransition(off);
            offTransition.hasExitTime = false;
            offTransition.duration = 0f;
            offTransition.AddCondition(AnimatorConditionMode.Less, 0.5f, parameter);
            var onTransition = stateMachine.AddAnyStateTransition(on);
            onTransition.hasExitTime = false;
            onTransition.duration = 0f;
            onTransition.AddCondition(AnimatorConditionMode.Greater, 0.5f, parameter);
            AddLayer(controller, layerName, stateMachine);
        }

        private static void AddSingleMotionLayer(
            AnimatorController controller,
            string layerName,
            string curvePath,
            float value,
            AvatarMask avatarMask = null,
            AnimatorLayerBlendingMode blendingMode = AnimatorLayerBlendingMode.Override,
            bool additiveReferencePose = false,
            string stateTag = null)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var state = stateMachine.AddState(layerName + " State", new Vector3(300f, 0f));
            var clip = CreateClip(controller, layerName + " Clip", curvePath, value);
            if (additiveReferencePose)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.hasAdditiveReferencePose = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            state.motion = clip;
            state.tag = stateTag ?? string.Empty;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            AddLayer(controller, layerName, stateMachine, avatarMask, blendingMode);
        }

        private static void AddBooleanToggleLayer(
            AnimatorController controller,
            string layerName,
            string parameterName,
            string outputPath = null)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var off = stateMachine.AddState("Off", new Vector3(300f, 0f));
            off.motion = CreateClip(controller, layerName + " Off", outputPath ?? layerName + "/Off", 0f);
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On", new Vector3(300f, 80f));
            on.motion = CreateClip(controller, layerName + " On", outputPath ?? layerName + "/On", 1f);
            on.writeDefaultValues = true;
            stateMachine.defaultState = off;

            var toOff = stateMachine.AddAnyStateTransition(off);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.offset = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);
            var toOn = stateMachine.AddAnyStateTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.offset = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
            AddLayer(controller, layerName, stateMachine);
        }

        private static void AddMotionLayer(
            AnimatorController controller,
            string layerName,
            Motion motion,
            AvatarMask avatarMask = null,
            AnimatorLayerBlendingMode blendingMode = AnimatorLayerBlendingMode.Override)
        {
            var stateMachine = new AnimatorStateMachine { name = layerName + " StateMachine" };
            var state = stateMachine.AddState(layerName + " State", new Vector3(300f, 0f));
            state.motion = motion;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            AddLayer(controller, layerName, stateMachine, avatarMask, blendingMode);
        }

        private static void AddBooleanPairTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState source,
            AnimatorState destination,
            string parameter)
        {
            var whenFalse = source.AddTransition(destination);
            whenFalse.hasExitTime = false;
            whenFalse.duration = 0f;
            whenFalse.offset = 0f;
            whenFalse.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);

            var whenTrue = source.AddTransition(destination);
            whenTrue.hasExitTime = false;
            whenTrue.duration = 0f;
            whenTrue.offset = 0f;
            whenTrue.AddCondition(AnimatorConditionMode.If, 0f, parameter);
        }

        private static void AddAnyStateMixedTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            bool enabled,
            AnimatorConditionMode numericMode,
            float numericThreshold)
        {
            var transition = stateMachine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.offset = 0f;
            transition.AddCondition(
                enabled ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                "Enabled");
            transition.AddCondition(numericMode, numericThreshold, "Choice");
        }

        private static AnimationClip CreateClip(
            AnimatorController controller,
            string name,
            string path,
            float value)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, value));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static AnimationClip CreateTransformPositionClip(
            AnimatorController controller,
            string name,
            string path,
            float value)
        {
            return CreateTransformCurveClip(controller, name, path, "m_LocalPosition.x", value);
        }

        private static AnimationClip CreateTransformCurveClip(
            AnimatorController controller,
            string name,
            string path,
            string propertyName,
            float value)
        {
            return CreateFloatCurveClip(controller, name, path, typeof(Transform), propertyName, value);
        }

        private static AnimationClip CreateFloatCurveClip(
            AnimatorController controller,
            string name,
            string path,
            Type type,
            string propertyName,
            float value)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            clip.SetCurve(path, type, propertyName, AnimationCurve.Constant(0f, 1f / 60f, value));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static AnimationClip CreateObjectCurveClip(
            AnimatorController controller,
            string name,
            string path,
            float time = 0f)
        {
            var texture = new Texture2D(1, 1) { name = name + " Texture" };
            AssetDatabase.AddObjectToAsset(texture, controller);
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            var binding = EditorCurveBinding.PPtrCurve(
                path, typeof(Renderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = time, value = texture },
            });
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static AnimationClip CreateTimedClip(
            AnimatorController controller,
            string name,
            string path,
            float firstValue,
            float secondValue)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            clip.frameRate = 60f;
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", new AnimationCurve(
                new Keyframe(0f, firstValue),
                new Keyframe(1f / 60f, secondValue)));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static BlendTree CreateDirectTree(
            AnimatorController controller,
            string name,
            string weightParameter,
            bool normalized,
            string curvePath)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var clip = CreateClip(controller, name + " Clip", curvePath, 1f);
            tree.children = new[]
            {
                new ChildMotion
                {
                    motion = clip,
                    directBlendParameter = weightParameter,
                },
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var serialized = new SerializedObject(tree);
            serialized.FindProperty("m_NormalizedBlendValues").boolValue = normalized;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return tree;
        }

        private static AnimationClip CreateMissingPathClip(
            string name,
            string path,
            params Keyframe[] keys)
        {
            var clip = new AnimationClip
            {
                name = name,
                frameRate = 60f,
                hideFlags = HideFlags.HideInHierarchy,
            };
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", new AnimationCurve(keys));
            return clip;
        }

        private static void AddEmptyLayer(
            AnimatorController controller,
            string name,
            bool addBehaviour = false)
        {
            var stateMachine = new AnimatorStateMachine { name = name + " StateMachine" };
            var state = stateMachine.AddState(name + " State", new Vector3(300f, 0f));
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;
            if (addBehaviour)
                state.AddStateMachineBehaviour<AnimatorOptimizerTestMarkerBehaviour>();
            AddLayer(controller, name, stateMachine);
        }

        private static IEnumerable<BlendTree> EnumerateBlendTrees(Motion motion)
        {
            if (!(motion is BlendTree tree))
                yield break;
            yield return tree;
            foreach (var child in tree.children ?? Array.Empty<ChildMotion>())
            {
                foreach (var nested in EnumerateBlendTrees(child.motion))
                    yield return nested;
            }
        }

        private static void AddLayer(
            AnimatorController controller,
            string name,
            AnimatorStateMachine stateMachine,
            AvatarMask avatarMask = null,
            AnimatorLayerBlendingMode blendingMode = AnimatorLayerBlendingMode.Override)
        {
            AssetDatabase.AddObjectToAsset(stateMachine, controller);
            if (avatarMask != null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(avatarMask)))
                AssetDatabase.AddObjectToAsset(avatarMask, controller);
            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                blendingMode = blendingMode,
                avatarMask = avatarMask,
                stateMachine = stateMachine,
            });
            controller.layers = layers.ToArray();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
