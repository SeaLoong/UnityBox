using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerProcessorRegressionTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__AOProcessor_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            AnimatorOptimizerBuildState.ReleaseAll();
        }

        [TearDown]
        public void TearDown()
        {
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            AnimatorOptimizerBuildState.ReleaseAll();
            foreach (var item in objects.AsEnumerable().Reverse())
                if (item != null && !AssetDatabase.Contains(item)) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void DisabledRootDoesNotHideEnabledChildConfiguration()
        {
            var avatar = CreateObject("Avatar");
            avatar.AddComponent<AnimatorOptimizerComponent>().enabled = false;
            var child = CreateObject("Configured", avatar);
            child.AddComponent<AnimatorOptimizerComponent>();
            Assert.That(AnimatorOptimizerConfigData.FromAvatar(avatar).SourceObjectName, Is.EqualTo("Configured"));
        }

        [Test]
        public void DisabledConfigurationClearsCapturedState()
        {
            var avatar = CreateObject("Avatar");
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            AnimatorOptimizerBuildState.Capture(avatar, "test");
            AnimatorOptimizerBuildState.MarkNdmfProcessed(avatar);
            component.enabled = false;
            AnimatorOptimizerBuildState.Capture(avatar, "test disabled");
            Assert.That(AnimatorOptimizerBuildState.Get(avatar), Is.Null);
            Assert.That(AnimatorOptimizerBuildState.WasNdmfProcessed(avatar), Is.False);
        }

        [Test]
        public void NewSdkBuildDropsSnapshotForRemovedComponent()
        {
            var avatar = CreateObject("Avatar");
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            AnimatorOptimizerBuildState.Capture(avatar, "old build");
            UnityEngine.Object.DestroyImmediate(component);
            new AnimatorOptimizerConfigCaptureCallback().OnPreprocessAvatar(avatar);
            Assert.That(AnimatorOptimizerBuildState.Get(avatar), Is.Null);
        }

        [Test]
        public void ExternalAnimatorDoesNotFallBackToAvatarAnimator()
        {
            var avatar = CreateObject("Avatar");
            avatar.AddComponent<Animator>();
            var external = CreateObject("Other").AddComponent<Animator>();
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            component.animator = external;
            Assert.That(AnimatorOptimizerConfigData.FromComponent(component, avatar).ResolveAnimator(avatar), Is.Null);
        }

        [Test]
        public void AmbiguousAutomaticAnimatorSelectionRequiresExplicitTarget()
        {
            var avatar = CreateObject("Avatar");
            CreateObject("First", avatar).AddComponent<Animator>();
            CreateObject("Second", avatar).AddComponent<Animator>();
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            Assert.That(AnimatorOptimizerConfigData.FromComponent(component, avatar).ResolveAnimator(avatar), Is.Null);
        }

        [Test]
        public void LiveTargetSurvivesRenameAndCloneLookupTreatsSlashAsName()
        {
            var avatar = CreateObject("Avatar");
            var target = CreateObject("A/B", avatar).AddComponent<Animator>();
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            component.animator = target;
            var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);
            var cloneRoot = CreateObject("Clone");
            var cloneTarget = CreateObject("A/B", cloneRoot).AddComponent<Animator>();
            Assert.That(config.ResolveAnimator(cloneRoot), Is.SameAs(cloneTarget));
            target.name = "Renamed";
            Assert.That(config.ResolveAnimator(avatar), Is.SameAs(target));
        }

        [Test]
        public void AmbiguousClonePathDoesNotPickFirstSibling()
        {
            var avatar = CreateObject("Avatar");
            var target = CreateObject("Target", avatar).AddComponent<Animator>();
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            component.animator = target;
            var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);
            var clone = CreateObject("Clone");
            CreateObject("Target", clone).AddComponent<Animator>();
            CreateObject("Target", clone).AddComponent<Animator>();
            Assert.That(config.ResolveAnimator(clone), Is.Null);
        }

        [Test]
        public void ExplicitControllerWithoutRecipientFailsInsteadOfReportingSuccess()
        {
            var avatar = CreateObject("Avatar");
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.ExplicitController;
            component.controller = CreateController();
            LogAssert.Expect(LogType.Error,
                "[AnimatorOptimizer] No unambiguous target Animator exists inside this Avatar. Assign the intended Animator explicitly.");
            Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar,
                AnimatorOptimizerConfigData.FromComponent(component, avatar)), Is.False);
        }

        [Test]
        public void MemoryControllerGraphIsCopiedWithoutMutatingSourceAndThenRestored()
        {
            var avatar = CreateObject("Avatar");
            CreateObject("Target", avatar);
            var animator = avatar.AddComponent<Animator>();
            var source = CreateController();
            animator.runtimeAnimatorController = source;
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            component.animator = animator;
            component.optimizeAllConvertibleLayers = false;
            component.layerIndices.Clear();
            component.removeEffectivelyEmptyLayers = true;
            component.mergeSynchronizedStateMachines = false;
            var config = AnimatorOptimizerConfigData.FromComponent(component, avatar);
            Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar, config), Is.True);
            var generated = (AnimatorController)animator.runtimeAnimatorController;
            Assert.That(generated, Is.Not.SameAs(source));
            var sourceState = source.layers[0].stateMachine.defaultState;
            var generatedState = generated.layers[0].stateMachine.defaultState;
            Assert.That(generatedState, Is.Not.SameAs(sourceState));
            Assert.That(generatedState.motion, Is.Not.SameAs(sourceState.motion));
            Assert.That(AssetDatabase.GetAssetPath(generatedState.motion), Is.EqualTo(AssetDatabase.GetAssetPath(generated)));
            generatedState.speed = 2f;
            Assert.That(sourceState.speed, Is.EqualTo(1f));
            Assert.That(AssetDatabase.GetAssetPath(source), Is.Empty);
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            Assert.That(animator.runtimeAnimatorController, Is.SameAs(source));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayableAssignmentPreservesDescriptorCustomization(bool customizeAnimationLayers)
        {
            var avatar = CreateObject("Avatar");
            CreateObject("Target", avatar);
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            var source = CreateController();
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
                { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = source } };
            descriptor.customizeAnimationLayers = customizeAnimationLayers;
            var component = avatar.AddComponent<AnimatorOptimizerComponent>();
            component.optimizeAllConvertibleLayers = false;
            component.layerIndices.Clear();
            component.mergeSynchronizedStateMachines = false;
            var preview = typeof(AnimatorOptimizerEditor).GetMethod("ResolveController",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { component });
            if (customizeAnimationLayers) Assert.That(preview, Is.SameAs(source));
            else Assert.That(preview, Is.Null);
            Assert.That(AnimatorOptimizerProcessor.ProcessAvatar(avatar,
                AnimatorOptimizerConfigData.FromComponent(component, avatar)), Is.True);
            Assert.That(descriptor.customizeAnimationLayers, Is.EqualTo(customizeAnimationLayers));
            if (customizeAnimationLayers)
                Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.Not.SameAs(source));
            else
                Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(source),
                    "Disabled custom layers are stale configuration, not an optimization target.");
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            Assert.That(descriptor.customizeAnimationLayers, Is.EqualTo(customizeAnimationLayers));
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(source));
        }

        [TestCase("..")]
        [TestCase("../Other")]
        [TestCase("..\\Other")]
        [TestCase("A:B/C")]
        public void GeneratedNamesStayInsideTheirSingleDirectory(string name)
        {
            var method = typeof(AnimatorOptimizerProcessor).GetMethod("SanitizeAssetName",
                BindingFlags.Static | BindingFlags.NonPublic);
            var result = (string)method.Invoke(null, new object[] { name });
            Assert.That(result, Is.Not.EqualTo("."));
            Assert.That(result, Is.Not.EqualTo(".."));
            Assert.That(result, Does.Not.Contain("/"));
            Assert.That(result, Does.Not.Contain("\\"));
            Assert.That(result, Does.Not.Contain(":"));
        }

        private GameObject CreateObject(string name, GameObject parent = null)
        {
            var result = new GameObject(name);
            objects.Add(result);
            if (parent != null) result.transform.SetParent(parent.transform, false);
            return result;
        }

        private AnimatorController CreateController()
        {
            var controller = new AnimatorController { name = "Memory" };
            var machine = new AnimatorStateMachine { name = "Base" };
            var state = machine.AddState("Default");
            var clip = new AnimationClip { name = "Constant" };
            clip.SetCurve("Target", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1f / 60f, 1));
            state.motion = clip;
            state.writeDefaultValues = true;
            machine.defaultState = state;
            controller.layers = new[] { new AnimatorControllerLayer
                { name = "Base", stateMachine = machine, defaultWeight = 1 } };
            objects.Add(controller);
            objects.Add(machine);
            objects.Add(state);
            objects.Add(clip);
            return controller;
        }
    }
}
