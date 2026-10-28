using System;
using System.Linq;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    // These tests intentionally reference the real optional MA assembly.
    public sealed class AnimatorOptimizerModularAvatarTests
    {
        private string folder;
        private AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            var name = "AnimatorOptimizerMa_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            folder = "Assets/" + name;
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Fixture.controller");
            controller.layers = Array.Empty<AnimatorControllerLayer>();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void PreservesMmdBehaviourWhenAapStateRegisterConverts()
        {
            controller.AddParameter("Gate", AnimatorControllerParameterType.Bool);
            var machine = AddMachine("AAP MMD Source");
            var first = AddState(machine, "First", "AAPMmd/First", 0f);
            var second = AddState(machine, "Second", "AAPMmd/Second", 1f);
            machine.defaultState = first;
            var transition = first.AddTransition(second);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "Gate");
            AddMmdBehaviour(machine);
            AssetDatabase.SaveAssets();
            var options = new AnimatorOptimizerOptions
            {
                EnableAapStateRegisterOptimization = true,
                NormalizeAlwaysOneParameters = false,
            };
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            Assert.That(report.Layers[0].ConversionKind, Is.EqualTo(AnimatorOptimizerConversionKind.AapStateRegister),
                string.Join(" | ", report.Layers[0].Reasons));

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0 }, options);

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(2));
            Assert.That(controller.layers[1].stateMachine.behaviours, Has.Length.EqualTo(1));
            var retained = controller.layers[1].stateMachine.behaviours[0] as ModularAvatarMMDLayerControl;
            Assert.That(retained, Is.Not.Null);
            Assert.That(retained.DisableInMMDMode, Is.False);
        }

        [Test]
        public void PreservesEquivalentMaMmdLayerControlWhenMergingResponsiveLayers()
        {
            var first = AddMachine("MA Responsive: First");
            first.defaultState = AddState(first, "First", "MA/MmdFirst", 1f);
            AddMmdBehaviour(first);
            var second = AddMachine("MA Responsive: Second");
            second.defaultState = AddState(second, "Second", "MA/MmdSecond", 1f);
            AddMmdBehaviour(second);
            AssetDatabase.SaveAssets();

            var result = AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 0, 1 });

            Assert.That(result.GeneratedLayers, Has.Count.EqualTo(1));
            Assert.That(controller.layers[0].stateMachine.behaviours, Has.Length.EqualTo(1));
            var retained = controller.layers[0].stateMachine.behaviours[0] as ModularAvatarMMDLayerControl;
            Assert.That(retained, Is.Not.Null);
            Assert.That(retained.DisableInMMDMode, Is.False);
        }

        private AnimatorStateMachine AddMachine(string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.layers = controller.layers.Concat(new[] { new AnimatorControllerLayer
            {
                name = name, stateMachine = machine, defaultWeight = 1f, syncedLayerIndex = -1,
                blendingMode = AnimatorLayerBlendingMode.Override,
            } }).ToArray();
            return machine;
        }

        private AnimatorState AddState(AnimatorStateMachine machine, string name, string path, float value)
        {
            var clip = new AnimationClip { name = name + " Clip" };
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, value));
            AssetDatabase.AddObjectToAsset(clip, controller);
            var state = machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = true;
            return state;
        }

        private static void AddMmdBehaviour(AnimatorStateMachine machine)
        {
            var behaviour = machine.AddStateMachineBehaviour<ModularAvatarMMDLayerControl>();
            Assert.That(behaviour, Is.Not.Null);
            behaviour.DisableInMMDMode = false;
        }
    }
}
