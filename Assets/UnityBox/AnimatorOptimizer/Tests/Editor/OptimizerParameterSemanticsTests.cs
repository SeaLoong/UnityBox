using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerParameterSemanticsTests
    {
        private string path;
        private AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            path = "Assets/AOParameterSemantics_" + Guid.NewGuid().ToString("N") + ".controller";
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.layers = Array.Empty<AnimatorControllerLayer>();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(path);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UndeclaredIsLocalIsRejectedEvenWhenOutputsAreEquivalent(bool sameOutput)
        {
            AddSelector("IsLocal", AnimatorControllerParameterType.Bool, sameOutput);
            var options = Options();
            var before = AssetDatabase.LoadAllAssetsAtPath(path).Length;

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options));
            Assert.That(controller.parameters, Is.Empty);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).Length, Is.EqualTo(before));
        }

        [TestCase("IsLocal", AnimatorControllerParameterType.Bool)]
        [TestCase("Viseme", AnimatorControllerParameterType.Int)]
        public void DeclaredPlatformNamesUpgradeAsLocalParametersInUnityMode(string name, AnimatorControllerParameterType type)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name, type = type, defaultBool = true, defaultInt = 1,
            });
            AddSelector(name, type);
            var retained = AddLayer("Retained");
            var state = retained.AddState("State");
            var transition = retained.AddAnyStateTransition(state);
            transition.AddCondition(type == AnimatorControllerParameterType.Bool
                ? AnimatorConditionMode.If : AnimatorConditionMode.Greater, 0f, name);
            var options = Options();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            var parameter = controller.parameters.Single(item => item.name == name);
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(parameter.defaultFloat, Is.EqualTo(1f));
            Assert.That(result.UpgradedParameters, Does.Contain(name));
            Assert.That(transition.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(transition.conditions.Single().threshold,
                Is.EqualTo(type == AnimatorControllerParameterType.Bool ? 0.5f : 0f));
        }

        [TestCase("IsLocal", AnimatorControllerParameterType.Bool, false)]
        [TestCase("Viseme", AnimatorControllerParameterType.Int, false)]
        [TestCase("IsLocal", AnimatorControllerParameterType.Bool, true)]
        [TestCase("Viseme", AnimatorControllerParameterType.Int, true)]
        public void ExplicitVrchatModeRetainsNativePlatformConditionLayers(string name, AnimatorControllerParameterType type, bool declared)
        {
            if (declared) controller.AddParameter(name, type);
            AddSelector(name, type);
            var sourceMachine = controller.layers[0].stateMachine;
            var options = Options();
            options.UseVrChatParameterSemantics = true;
            options.UpgradeBoolParametersToFloat = false;
            options.UpgradeIntParametersToFloat = false;

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("参数槽")), Is.True);
            Assert.Throws<InvalidOperationException>(() => AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options));
            Assert.That(controller.layers[0].stateMachine, Is.SameAs(sourceMachine));
            Assert.That(controller.parameters.Any(parameter => parameter.name == name), Is.EqualTo(declared));
            if (declared) Assert.That(controller.parameters.Single(parameter => parameter.name == name).type, Is.EqualTo(type));
        }

        [Test]
        public void OrdinaryUnityDoesNotConvertLocalIsLocalWhenBoolUpgradeIsDisabled()
        {
            controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
            AddSelector("IsLocal", AnimatorControllerParameterType.Bool);
            var options = Options();
            options.UpgradeBoolParametersToFloat = false;

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options));
            Assert.That(controller.parameters.Single(parameter => parameter.name == "IsLocal").type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
        }

        [TestCase(false, "VelocityX")]
        [TestCase(true, "VelocityX_1")]
        public void GeneratedWeightReservesBuiltinNamesOnlyInExplicitVrchatMode(bool vrchat, string expected)
        {
            var machine = AddLayer("Constant");
            var state = machine.AddState("State");
            state.writeDefaultValues = true;
            state.motion = Clip(1f);
            var options = Options();
            options.UseVrChatParameterSemantics = vrchat;
            options.WeightParameterPrefix = "VelocityX";

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            var root = (BlendTree)result.GeneratedLayer.stateMachine.defaultState.motion;
            Assert.That(root.children.All(child => child.directBlendParameter == expected), Is.True);
            Assert.That(controller.parameters.Single(parameter => parameter.name == expected).defaultFloat, Is.EqualTo(1f));
        }

        [Test]
        public void AapBuiltinBoolKeepsItsNativeStateMachineInsteadOfUsingAFloatAxis()
        {
            var graph = new AnimatorOptimizerFiniteGraph { DefaultState = 0 };
            graph.States.Add(new List<AnimatorOptimizerFiniteGraph.Edge>());
            graph.States.Add(new List<AnimatorOptimizerFiniteGraph.Edge>());
            graph.AnyState.Add(new AnimatorOptimizerFiniteGraph.Edge
            {
                Destination = 1, CanTransitionToSelf = true,
                Conditions = new[] { new AnimatorCondition { parameter = "IsLocal", mode = AnimatorConditionMode.If } },
            });
            var declarations = new Dictionary<string, AnimatorControllerParameter>();
            var options = Options();
            Assert.That(AnimatorOptimizerAapProgram.TryCompile(graph, declarations, options, out _, out _), Is.False);
            options.UseVrChatParameterSemantics = true;
            Assert.That(AnimatorOptimizerAapProgram.TryCompile(graph, declarations, options, out var program, out var reason), Is.False);
            Assert.That(reason, Does.Contain("参数槽"));
            Assert.That(program, Is.Null);
            Assert.That(controller.parameters, Is.Empty);
        }

        [Test]
        public void LocalAapRegisterMayUseNameReservedOnlyByVrchat()
        {
            controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
            controller.AddParameter("Toggle", AnimatorControllerParameterType.Float);
            var table = new Dictionary<int, IReadOnlyDictionary<int, int>>
            {
                [0] = new Dictionary<int, int> { [0] = 0, [1] = 1 },
                [1] = new Dictionary<int, int> { [0] = 0, [1] = 1 },
            };

            var tree = AnimatorOptimizerAapBuilder.BuildBooleanStateRegisterUpdate(
                controller, "VelocityX", new[] { "Toggle" }, 2, 0, table);

            Assert.That(tree.blendParameter, Is.EqualTo("VelocityX"));
        }

        private void AddSelector(string name, AnimatorControllerParameterType type, bool sameOutput = false)
        {
            var machine = AddLayer("Selector");
            var off = machine.AddState("Off");
            var on = machine.AddState("On");
            off.motion = Clip(0f);
            on.motion = Clip(sameOutput ? 0f : 1f);
            off.writeDefaultValues = on.writeDefaultValues = true;
            machine.defaultState = off;
            var toOff = machine.AddAnyStateTransition(off);
            toOff.duration = 0f;
            toOff.hasExitTime = false;
            toOff.AddCondition(type == AnimatorControllerParameterType.Bool
                ? AnimatorConditionMode.IfNot : AnimatorConditionMode.NotEqual, 1f, name);
            var toOn = machine.AddAnyStateTransition(on);
            toOn.duration = 0f;
            toOn.hasExitTime = false;
            toOn.AddCondition(type == AnimatorControllerParameterType.Bool
                ? AnimatorConditionMode.If : AnimatorConditionMode.Equals, 1f, name);
        }

        private AnimatorStateMachine AddLayer(string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = machine, defaultWeight = 1f });
            return machine;
        }

        private AnimationClip Clip(float value)
        {
            var clip = new AnimationClip();
            clip.SetCurve("Output", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, value));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static AnimatorOptimizerOptions Options() => new AnimatorOptimizerOptions
        {
            RequireAtLeastTwoLayers = false,
            MergeSynchronizedStateMachines = false,
            RemoveEffectivelyEmptyLayers = false,
        };
    }
}
