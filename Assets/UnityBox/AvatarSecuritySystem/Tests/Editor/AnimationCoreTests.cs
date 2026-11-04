using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class AnimationCoreTests
    {
        private string folder;
        private GameObject root;
        private AnimatorController controller;
        private bool hadSharedEmptyClip;
        private string sharedEmptyPath;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSCore_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            root = new GameObject("CoreTest");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Controller.controller");
            Obfuscator.Initialize(root.name, true, false, false, folder + "/Generated");
            Constants.ApplyObfuscation();
            sharedEmptyPath = Constants.ASSET_FOLDER + "/" + Constants.SHARED_EMPTY_CLIP_FILE;
            hadSharedEmptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sharedEmptyPath) != null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(folder);
            if (!hadSharedEmptyClip) AssetDatabase.DeleteAsset(sharedEmptyPath);
        }

        [Test]
        public void ConfigurationSnapshotOwnsPasswordListAndPreservesSafeDefaults()
        {
            var component = root.AddComponent<ASSComponent>();
            component.gesturePassword = new List<int> { 0, 2, 7 };
            var snapshot = ASSConfigData.FromComponent(component);
            component.gesturePassword[0] = 6;
            component.gesturePassword.Add(3);
            Assert.That(snapshot.gesturePassword, Is.EqualTo(new[] { 0, 2, 7 }));
            Assert.That(snapshot.disableDefense, Is.True);
            Assert.That(snapshot.defaultEnableDefense, Is.False);
        }

        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(7, true)]
        [TestCase(8, false)]
        public void PasswordValidationUsesSerializedGestureRange(int gesture, bool expected)
        {
            var config = new ASSConfigData { gesturePassword = new List<int> { gesture } };
            Assert.That(config.IsPasswordValid(), Is.EqualTo(expected));
        }

        [TestCase(AnimatorControllerParameterType.Bool, true, AnimatorConditionMode.If, 0f)]
        [TestCase(AnimatorControllerParameterType.Bool, false, AnimatorConditionMode.IfNot, 0f)]
        [TestCase(AnimatorControllerParameterType.Int, true, AnimatorConditionMode.Greater, 0f)]
        [TestCase(AnimatorControllerParameterType.Int, false, AnimatorConditionMode.Less, 1f)]
        [TestCase(AnimatorControllerParameterType.Float, true, AnimatorConditionMode.Greater, 0.5f)]
        [TestCase(AnimatorControllerParameterType.Float, false, AnimatorConditionMode.Less, 0.5f)]
        public void LocalConditionMatchesExistingParameterType(
            AnimatorControllerParameterType type, bool local, AnimatorConditionMode mode, float threshold)
        {
            controller.AddParameter(Constants.PARAM_IS_LOCAL, type);
            var machine = controller.layers[0].stateMachine;
            var transition = Utils.CreateTransition(machine.AddState("Before"), machine.AddState("After"));
            Utils.AddIsLocalCondition(transition, controller, local);
            Assert.That(transition.conditions.Single().mode, Is.EqualTo(mode));
            Assert.That(transition.conditions.Single().threshold, Is.EqualTo(threshold));
        }

        [Test]
        public void GestureGraphRequiresContinuousHoldAndRequestsUnlockOnlyAtSuccess()
        {
            var adapter = new RecordingBehaviours();
            var config = new ASSConfigData
            {
                gesturePassword = new List<int> { 1, 2 }, gestureHoldTime = 0.15f,
                gestureMaxHoldTime = 3f, gestureErrorTolerance = 0.3f,
            };
            new GesturePassword(controller, root, config, adapter).Generate();
            var layer = controller.layers.Single(item => item.name == Constants.LAYER_PASSWORD_INPUT);
            var holding = layer.stateMachine.states.Single(item => item.state.name == "Step_1_Holding").state;
            var mismatch = holding.transitions.Where(item => !item.hasExitTime).ToArray();
            Assert.That(mismatch, Has.Length.EqualTo(2));
            Assert.That(mismatch.SelectMany(item => item.conditions).Select(item => item.mode),
                Is.EquivalentTo(new[] { AnimatorConditionMode.Less, AnimatorConditionMode.Greater }));
            Assert.That(mismatch.All(item => item.conditions.Single().threshold == 1f), Is.True);
            var confirm = holding.transitions.Single(item => item.hasExitTime && item.conditions.Length == 2);
            Assert.That(confirm.exitTime, Is.EqualTo(0.05f).Within(0.00001f));
            Assert.That(adapter.Drivers, Has.Count.EqualTo(1));
            Assert.That(adapter.Drivers[0].State.name, Is.EqualTo("Password_Success"));
            Assert.That(adapter.Drivers[0].Parameter, Is.EqualTo(Constants.PARAM_PASSWORD_CORRECT));
            Assert.That(adapter.Drivers[0].Value, Is.EqualTo(1f));
        }

        [Test]
        public void CountdownRequestsLocalTimeoutAndUsesActualAppendedLayerIndex()
        {
            Utils.EnsureInputParameters(controller);
            Utils.AddParameterIfNotExists(controller, Constants.PARAM_PASSWORD_CORRECT, AnimatorControllerParameterType.Bool);
            var adapter = new RecordingBehaviours();
            var config = new ASSConfigData { countdownDuration = 20f, warningThreshold = 5f, disableOverlay = true };
            new Countdown(controller, root, config, adapter).Generate();
            Assert.That(adapter.Drivers, Has.Count.EqualTo(1));
            Assert.That(adapter.Drivers[0].Parameter, Is.EqualTo(Constants.PARAM_TIME_UP));
            Assert.That(adapter.Drivers[0].LocalOnly, Is.True);
            Assert.That(adapter.DisabledLayers, Is.EqualTo(new[] { 1, 1 }));
            var layer = controller.layers[1];
            var countdown = layer.stateMachine.states.Single(item => item.state.name == "Countdown").state;
            Assert.That(countdown.transitions.Single(item => item.hasExitTime).exitTime, Is.EqualTo(1f));
        }

        [Test]
        public void MissingPlatformBackendFailsBeforeGeneratingIncompleteController()
        {
            var count = controller.layers.Length;
            Assert.Throws<ArgumentNullException>(() => new GesturePassword(controller, root, new ASSConfigData(), null));
            Assert.Throws<ArgumentNullException>(() => new Countdown(controller, root, new ASSConfigData(), null));
            Assert.Throws<ArgumentNullException>(() => new Lock(controller, root, new ASSConfigData(), null));
            Assert.That(controller.layers, Has.Length.EqualTo(count));
        }

        [Test]
        public void BuildCopyPreservesSourceStateMachineWithoutAnySdkTypes()
        {
            var sourceState = controller.layers[0].stateMachine.AddState("Source");
            AssetDatabase.SaveAssets();
            var copy = Obfuscator.DuplicateAnimatorControllerForBuild(controller, "CoreTest");
            Assert.That(copy, Is.Not.SameAs(controller));
            Assert.That(copy.layers[0].stateMachine, Is.Not.SameAs(controller.layers[0].stateMachine));
            copy.layers[0].stateMachine.states.Single().state.name = "Changed";
            Assert.That(sourceState.name, Is.EqualTo("Source"));
        }

        private sealed class RecordingBehaviours : IAnimatorBehaviourAdapter
        {
            public readonly List<(AnimatorState State, string Parameter, float Value, bool LocalOnly)> Drivers =
                new List<(AnimatorState, string, float, bool)>();
            public readonly List<int> DisabledLayers = new List<int>();
            public void AddDisableSelfLayerControl(AnimatorState state, int layerIndex, string debugString) => DisabledLayers.Add(layerIndex);
            public void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false) =>
                Drivers.Add((state, parameterName, value, localOnly));
            public void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
            {
                foreach (var item in parameters) AddParameterDriverBehaviour(state, item.Key, item.Value, localOnly);
            }
            public void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip) =>
                Assert.Fail("These graph fixtures should not request audio playback.");
        }
    }
}
