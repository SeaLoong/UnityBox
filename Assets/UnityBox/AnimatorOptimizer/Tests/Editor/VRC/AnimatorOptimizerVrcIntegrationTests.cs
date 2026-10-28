using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.AnimatorOptimizer.Tests
{
    // These tests compile only against the installed, real VRChat SDK.
    public sealed class AnimatorOptimizerVrcIntegrationTests
    {
        private string controllerPath;
        private AnimatorController controller;
        private GameObject avatar;

        [SetUp]
        public void SetUp()
        {
            AnimatorOptimizerPlatform.Register(new AnimatorOptimizerVrcPlatformAdapter());
            controllerPath = "Assets/AOVrcTest_" + Guid.NewGuid().ToString("N") + ".controller";
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            avatar = new GameObject("Avatar");
            AnimatorOptimizerBuildState.ReleaseAll();
        }

        [TearDown]
        public void TearDown()
        {
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            AnimatorOptimizerBuildState.ReleaseAll();
            UnityEngine.Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(controllerPath);
        }

        [Test]
        public void RejectsVrcFuryStyleParameterDriverLayer()
        {
            controller.AddParameter("VF/DriverToggle", AnimatorControllerParameterType.Bool);
            var machine = AddLayer("Driver", "Driver output");
            var driver = machine.defaultState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                name = "VF/DriverToggle", type = VRC_AvatarParameterDriver.ChangeType.Set, value = 1f,
            });

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, new AnimatorOptimizerOptions());

            Assert.That(report.Layers[0].CanConvert, Is.False);
            Assert.That(report.Layers[0].Reasons.Any(reason => reason.Contains("Behaviour")), Is.True);
        }

        [Test]
        public void RemapsVrcAnimatorLayerControlWhenMergedLayersShiftFollowingLayer()
        {
            var holder = AddLayer("Controller Holder", "Holder");
            var control = holder.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = 3;
            control.goalWeight = 1f;
            AddLayer("Merge A", "A");
            AddLayer("Merge B", "B");
            AddLayer("Controlled Target", "Target");
            AssetDatabase.SaveAssets();

            AnimatorOptimizerConverter.ConvertInPlace(controller, new[] { 1, 2 }, new AnimatorOptimizerOptions
            {
                HasKnownPlayableLayer = true,
                TargetPlayableLayer = AnimatorOptimizerComponent.PlayableLayer.FX,
            });

            Assert.That(control.layer, Is.EqualTo(2));
            Assert.That(controller.layers[0].stateMachine.behaviours.OfType<VRCAnimatorLayerControl>().Single().layer,
                Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayableAssignmentPreservesDescriptorCustomization(bool customizeAnimationLayers)
        {
            new GameObject("Target").transform.SetParent(avatar.transform, false);
            AddLayer("Base", "Target");
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = controller,
            } };
            descriptor.customizeAnimationLayers = customizeAnimationLayers;
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer;
            component.optimizeAllConvertibleLayers = false;
            component.layerIndices.Clear();
            component.mergeSynchronizedStateMachines = false;
            var target = AnimatorOptimizerPlatform.Adapter.ResolvePlayableTarget(avatar, component.playableLayer);
            Assert.That(target.Controller, customizeAnimationLayers ? Is.SameAs(controller) : Is.Null);

            Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar,
                AnimatorOptimizerConfigData.FromComponent(component, avatar)), Is.True);

            Assert.That(descriptor.customizeAnimationLayers, Is.EqualTo(customizeAnimationLayers));
            Assert.That(descriptor.baseAnimationLayers[0].animatorController,
                customizeAnimationLayers ? Is.Not.SameAs(controller) : Is.SameAs(controller));
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            Assert.That(descriptor.customizeAnimationLayers, Is.EqualTo(customizeAnimationLayers));
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(controller));
        }

        [Test]
        public void NewSdkBuildDropsSnapshotForRemovedComponent()
        {
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            AnimatorOptimizerBuildState.Capture(avatar, "old build");
            UnityEngine.Object.DestroyImmediate(component);

            new AnimatorOptimizerConfigCaptureCallback().OnPreprocessAvatar(avatar);

            Assert.That(AnimatorOptimizerBuildState.Get(avatar), Is.Null);
        }

        [Test]
        public void SdkLateOptimizationCallbackRunsLast()
        {
            Assert.That(new AnimatorOptimizerLatePreprocessCallback().callbackOrder, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ConfigurationKeepsRealSdkEditorOnlyMarker()
        {
            Assert.That(typeof(IEditorOnly).IsAssignableFrom(typeof(AnimatorOptimizerComponent)), Is.True);
        }

        [TestCase(AnimatorOptimizerComponent.TargetMode.AnimatorComponent, false)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.AnimatorComponent, true)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.ExplicitController, false)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.ExplicitController, true)]
        public void SdkCaptureStripsConfigurationAndPreservesExplicitAnimatorSemantics(
            AnimatorOptimizerComponent.TargetMode targetMode, bool useVrChatParameterSemantics)
        {
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.generatedLayerName = "Saved configuration";
            component.targetMode = targetMode;
            component.useVrChatParameterSemantics = useVrChatParameterSemantics;

            Assert.That(new AnimatorOptimizerConfigCaptureCallback().OnPreprocessAvatar(avatar), Is.True);

            Assert.That(avatar.GetComponent<AnimatorOptimizerComponent>(), Is.Null);
            Assert.That(AnimatorOptimizerBuildState.Get(avatar).GeneratedLayerName, Is.EqualTo("Saved configuration"));
            Assert.That(AnimatorOptimizerBuildState.Get(avatar).CreateOptions().UseVrChatParameterSemantics, Is.EqualTo(useVrChatParameterSemantics));
        }

        [Test]
        public void SdkCaptureUsesVrchatSemanticsForPlayableLayerTarget()
        {
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer;

            new AnimatorOptimizerConfigCaptureCallback().OnPreprocessAvatar(avatar);

            Assert.That(AnimatorOptimizerBuildState.Get(avatar).CreateOptions().UseVrChatParameterSemantics, Is.True);
        }

        private AnimatorStateMachine AddLayer(string name, string path)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var state = machine.AddState("Default");
            var clip = new AnimationClip { name = name };
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, 1f));
            AssetDatabase.AddObjectToAsset(clip, controller);
            state.motion = clip;
            state.writeDefaultValues = true;
            machine.defaultState = state;
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = machine, defaultWeight = 1f });
            return machine;
        }
    }
}
