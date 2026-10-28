using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public class BuildAssetIsolationTests
    {
        private string folder;
        private GameObject avatar;
        private VRCAvatarDescriptor descriptor;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSAudit_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            avatar = new GameObject("ASSAuditAvatar");
            descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            Obfuscator.Initialize(avatar.name, true, false, false, folder + "/Generated");
            Invoke(typeof(Constants), "ApplyObfuscation");
        }

        [TearDown]
        public void TearDown()
        {
            Processor.ReleaseBuildSnapshot(avatar);
            if (descriptor != null && descriptor.expressionParameters != null &&
                !EditorUtility.IsPersistent(descriptor.expressionParameters))
                Object.DestroyImmediate(descriptor.expressionParameters);
            Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void NewComponentDoesNotEnableActiveLoadDefense()
        {
            var config = avatar.AddComponent<ASSComponent>();
            Assert.That(config.disableDefense, Is.True);
            Assert.That(config.defaultEnableDefense, Is.False);
        }

        [Test]
        public void PlayModePreviewDoesNotCreateActiveLoadObjectsOrLayers()
        {
            var controller = new AnimatorController();
            try
            {
                new Defense(controller, avatar, new ASSConfigData { disableDefense = false }, isDebugMode: true).Generate();
                Assert.That(controller.layers, Is.Empty);
                Assert.That(avatar.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(controller); }
        }

        [Test]
        public void NullPasswordCanBeDisplayedWithoutThrowing()
        {
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = null;
            Assert.That(config.GetPasswordStrength(), Is.EqualTo("Weak"));
        }

        [Test]
        public void UnsavedThirdPartyControllerIsNotDiscardedAsAnAssArtifact()
        {
            var controller = new AnimatorController();
            try
            {
                Assert.That(Invoke(typeof(Processor), "IsTransientGeneratedController", controller), Is.False);
            }
            finally { Object.DestroyImmediate(controller); }
        }

        [Test]
        public void FxControllerIsIsolatedEvenWhenObfuscationIsDisabled()
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            source.layers[0].stateMachine.AddState("UserState");
            AssetDatabase.SaveAssets();
            descriptor.baseAnimationLayers = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.FX) };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();

            descriptor.customizeAnimationLayers = true;
            VrcPlayableControllers.PreparePlayableControllerCopies(descriptor, fxOnly: true);

            var copy = (AnimatorController)descriptor.baseAnimationLayers[0].animatorController;
            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.layers[0].stateMachine, Is.Not.SameAs(source.layers[0].stateMachine));
            copy.layers[0].stateMachine.states[0].state.name = "BuildState";
            Assert.That(source.layers[0].stateMachine.states[0].state.name, Is.EqualTo("UserState"));
        }

        [Test]
        public void ReusedSourceGetsIndependentCopiesForDifferentPlayableSlots()
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            var motion = new AnimationClip();
            AssetDatabase.CreateAsset(motion, folder + "/Motion.anim");
            source.layers[0].stateMachine.AddState("UserState").motion = motion;
            AssetDatabase.SaveAssets();
            descriptor.baseAnimationLayers = new[]
            {
                Layer(source, VRCAvatarDescriptor.AnimLayerType.FX),
                Layer(source, VRCAvatarDescriptor.AnimLayerType.Gesture)
            };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            descriptor.customizeAnimationLayers = true;
            VrcPlayableControllers.PreparePlayableControllerCopies(descriptor);
            var first = descriptor.baseAnimationLayers[0].animatorController;
            var second = descriptor.baseAnimationLayers[1].animatorController;
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(first), Is.Not.EqualTo(AssetDatabase.GetAssetPath(second)));
            var firstMotion = ((AnimatorController)first).layers[0].stateMachine.states[0].state.motion;
            var secondMotion = ((AnimatorController)second).layers[0].stateMachine.states[0].state.motion;
            Assert.That(firstMotion, Is.Not.Null);
            Assert.That(secondMotion, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(firstMotion), Is.Not.EqualTo(AssetDatabase.GetAssetPath(secondMotion)));
        }

        [Test]
        public void ParameterRegistrationPreservesTheSourceAsset()
        {
            var source = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            source.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "UserParameter", valueType = VRCExpressionParameters.ValueType.Bool },
                null
            };
            AssetDatabase.CreateAsset(source, folder + "/Parameters.asset");
            descriptor.expressionParameters = source;
            Invoke(typeof(Processor), "RegisterASSParameters", descriptor, new ASSConfigData(), folder);

            Assert.That(descriptor.expressionParameters, Is.Not.SameAs(source));
            Assert.That(source.parameters, Has.Length.EqualTo(2));
            Assert.That(source.parameters[1], Is.Null);
            Assert.That(descriptor.expressionParameters.parameters.Select(p => p.name),
                Does.Contain("UserParameter").And.Contain(Constants.PARAM_PASSWORD_CORRECT));
        }

        [Test]
        public void MissingParameterAssetIsCreatedForSavedUnlockState()
        {
            descriptor.expressionParameters = null;
            descriptor.customExpressions = false;
            Invoke(typeof(Processor), "RegisterASSParameters", descriptor, new ASSConfigData(), folder);
            Assert.That(descriptor.customExpressions, Is.True);
            var parameter = descriptor.expressionParameters.parameters.Single(p => p.name == Constants.PARAM_PASSWORD_CORRECT);
            Assert.That(parameter.saved, Is.True);
            Assert.That(parameter.networkSynced, Is.True);
        }

        [Test]
        public void CapturedPasswordDoesNotChangeWhenSourceListIsEdited()
        {
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = new System.Collections.Generic.List<int> { 1, 2, 3 };
            try
            {
                Processor.CaptureConfigSnapshot(avatar, "test capture");
                config.gesturePassword[0] = 7;
                config.gesturePassword.Add(4);
                var captured = (ASSConfigData)Invoke(typeof(Processor), "GetCapturedConfig", avatar);
                Assert.That(captured.gesturePassword, Is.EqualTo(new[] { 1, 2, 3 }));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Processor.CaptureConfigSnapshot(avatar, "test cleanup");
            }
        }

        [Test]
        public void RecapturingAfterComponentRemovalClearsStaleConfiguration()
        {
            var config = avatar.AddComponent<ASSComponent>();
            try
            {
                Processor.CaptureConfigSnapshot(avatar, "test capture");
                Assert.That(Invoke(typeof(Processor), "GetCapturedConfig", avatar), Is.Not.Null);
                Object.DestroyImmediate(config);
                Processor.CaptureConfigSnapshot(avatar, "test recapture");
                Assert.That(Invoke(typeof(Processor), "GetCapturedConfig", avatar), Is.Null);
            }
            finally
            {
                if (config != null) Object.DestroyImmediate(config);
                Processor.CaptureConfigSnapshot(avatar, "test cleanup");
            }
        }

        [Test]
        public void OrdinaryModeRegistersTimeUpAsLocalUnsavedState()
        {
            Invoke(typeof(Processor), "RegisterASSParameters", descriptor,
                new ASSConfigData { defaultEnableDefense = false }, folder);
            var parameter = descriptor.expressionParameters.parameters.Single(p => p.name == Constants.PARAM_TIME_UP);
            Assert.That(parameter.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Bool));
            Assert.That(parameter.defaultValue, Is.Zero);
            Assert.That(parameter.saved, Is.False);
            Assert.That(parameter.networkSynced, Is.False);
        }

        [Test]
        public void VrcBackendKeepsSdkParameterDriverAndLayerControlSemantics()
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Backend.controller");
            var state = controller.layers[0].stateMachine.AddState("Success");
            VrcAnimatorBehaviourAdapter.Instance.AddParameterDriverBehaviour(state, "Unlock", 1f, localOnly: true);
            VrcAnimatorBehaviourAdapter.Instance.AddDisableSelfLayerControl(state, 2, "ASS_Test");
            var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driver.localOnly, Is.True);
            Assert.That(driver.parameters.Single().name, Is.EqualTo("Unlock"));
            Assert.That(driver.parameters.Single().value, Is.EqualTo(1f));
            var control = state.behaviours.OfType<VRCAnimatorLayerControl>().Single();
            Assert.That(control.layer, Is.EqualTo(2));
            Assert.That(control.goalWeight, Is.Zero);
            Assert.That(control.blendDuration, Is.Zero);
        }

        [Test]
        public void ConfigurationKeepsRealSdkEditorOnlyMarker()
        {
            Assert.That(avatar.AddComponent<ASSComponent>(), Is.InstanceOf<VRC.SDKBase.IEditorOnly>());
        }

        [Test]
        public void UploadStripperRemovesConfigurationButKeepsAvatarObjects()
        {
            avatar.AddComponent<ASSComponent>();
            var child = new GameObject("Inactive configuration");
            child.transform.SetParent(avatar.transform);
            child.AddComponent<ASSComponent>();
            child.SetActive(false);
            Assert.That(new ConfigurationStripper().OnPreprocessAvatar(avatar), Is.True);
            Assert.That(avatar.GetComponentsInChildren<ASSComponent>(true), Is.Empty);
            Assert.That(child, Is.Not.Null);
            Assert.That(descriptor, Is.Not.Null);
        }

        [Test]
        public void CapturingConfigurationDoesNotSuppressSdkFallback()
        {
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = new System.Collections.Generic.List<int> { 99 };
            new ConfigCaptureProcessor().OnPreprocessAvatar(avatar);

            Assert.That(Processor.WasProcessedByNdmf(avatar), Is.False);
            // Invalid input must reach generation validation. A captured snapshot alone
            // previously made the NDMF-enabled SDK callback falsely return success.
            Assert.That(new Processor().OnPreprocessAvatar(avatar), Is.False);
            Assert.That(Invoke(typeof(Processor), "GetCapturedConfig", avatar), Is.Null);
        }

        [Test]
        public void OnlyCompletedNdmfGenerationSuppressesSdkFallback()
        {
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = new System.Collections.Generic.List<int> { 99 };
            Processor.CaptureConfigSnapshot(avatar, "test capture");
            Processor.MarkNdmfCompleted(avatar);

            Assert.That(new Processor().OnPreprocessAvatar(avatar), Is.True);
            Assert.That(Processor.WasProcessedByNdmf(avatar), Is.True);
        }

        [Test]
        public void EarlyCaptureClearsCompletionBeforeRetryOnSameAvatar()
        {
            var config = avatar.AddComponent<ASSComponent>();
            Processor.CaptureConfigSnapshot(avatar, "first build");
            Processor.MarkNdmfCompleted(avatar);
            config.gesturePassword = new System.Collections.Generic.List<int> { 99 };

            new ConfigCaptureProcessor().OnPreprocessAvatar(avatar);

            Assert.That(Processor.WasProcessedByNdmf(avatar), Is.False);
            Assert.That(new Processor().OnPreprocessAvatar(avatar), Is.False);
        }

        [Test]
        public void NdmfCapturePreservesEarlyConfigurationAfterAuthoringComponentWasStripped()
        {
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = new System.Collections.Generic.List<int> { 2, 4 };
            new ConfigCaptureProcessor().OnPreprocessAvatar(avatar);
            Processor.MarkNdmfCompleted(avatar);
            Object.DestroyImmediate(config);

            Processor.CaptureNdmfConfigSnapshot(avatar);

            Assert.That(Processor.WasProcessedByNdmf(avatar), Is.False);
            Assert.That(Processor.GetCapturedConfig(avatar).gesturePassword, Is.EqualTo(new[] { 2, 4 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleaseOrPostprocessClearsNdmfCompletion(bool usePostprocess)
        {
            avatar.AddComponent<ASSComponent>();
            Processor.CaptureConfigSnapshot(avatar, "test capture");
            Processor.MarkNdmfCompleted(avatar);
            if (usePostprocess) new Processor().OnPostprocessAvatar();
            else Processor.ReleaseBuildSnapshot(avatar);
            Assert.That(Processor.WasProcessedByNdmf(avatar), Is.False);
            Assert.That(Processor.GetCapturedConfig(avatar), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LateProcessingIsolatesNewPersistentControllerInEitherBuildMode(bool ndmfCompleted)
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/LateSource.controller");
            source.layers[0].stateMachine.AddState("UserState");
            AssetDatabase.SaveAssets();
            var sourceName = source.name;
            var sourceLayerName = source.layers[0].name;
            var sourceMachineName = source.layers[0].stateMachine.name;
            descriptor.baseAnimationLayers = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.FX) };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = avatar.AddComponent<ASSComponent>();
            config.gesturePassword = new System.Collections.Generic.List<int> { 1 };
            config.disableDefense = true;
            config.disableObfuscation = false;
            config.enablePlayableLayerObfuscation = true;
            Processor.CaptureConfigSnapshot(avatar, "late hook test");
            if (ndmfCompleted) Processor.MarkNdmfCompleted(avatar);
            Obfuscator.Initialize(avatar.name, false, false, false, folder + "/Generated");
            Constants.ApplyObfuscation();

            descriptor.customizeAnimationLayers = true;
            Assert.That(new PlayableObfuscationProcessor().OnPreprocessAvatar(avatar), Is.True);

            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.Not.SameAs(source));
            Assert.That(source.name, Is.EqualTo(sourceName));
            Assert.That(source.layers[0].name, Is.EqualTo(sourceLayerName));
            Assert.That(source.layers[0].stateMachine.name, Is.EqualTo(sourceMachineName));
            Assert.That(source.layers[0].stateMachine.states[0].state.name, Is.EqualTo("UserState"));
        }

        [Test]
        public void DisabledCustomLayersAreNotCopiedOrReactivatedByPreparation()
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Inactive.controller");
            descriptor.baseAnimationLayers = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.FX) };
            descriptor.customizeAnimationLayers = false;

            VrcPlayableControllers.PreparePlayableControllerCopies(descriptor);

            Assert.That(descriptor.customizeAnimationLayers, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(source));
            Assert.That(AssetDatabase.IsValidFolder(folder + "/Generated"), Is.False);
        }

        [Test]
        public void EnablingFxDefaultsInactiveBaseAndSpecialSlotsAndSnapshotRestoresThem()
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Inactive.controller");
            var originalBase = new[]
            {
                Layer(source, VRCAvatarDescriptor.AnimLayerType.FX),
                Layer(source, VRCAvatarDescriptor.AnimLayerType.Gesture)
            };
            var originalSpecial = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.Sitting) };
            descriptor.baseAnimationLayers = originalBase;
            descriptor.specialAnimationLayers = originalSpecial;
            descriptor.customizeAnimationLayers = false;
            Invoke(typeof(Processor), "SnapshotPlayableLayers", descriptor);
            try
            {
                VrcPlayableControllers.EnableCustomAnimationLayers(descriptor);

                Assert.That(descriptor.customizeAnimationLayers, Is.True);
                Assert.That(descriptor.baseAnimationLayers.All(layer => layer.isDefault && layer.animatorController == null), Is.True);
                Assert.That(descriptor.specialAnimationLayers.All(layer => layer.isDefault && layer.animatorController == null), Is.True);
                Assert.That(originalBase[0].animatorController, Is.SameAs(source));
                Assert.That(originalSpecial[0].animatorController, Is.SameAs(source));
            }
            finally
            {
                Invoke(typeof(Processor), "RestorePlayableLayerSnapshots", "test restore");
            }
            Assert.That(descriptor.customizeAnimationLayers, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].isDefault, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(source));
            Assert.That(descriptor.specialAnimationLayers[0].animatorController, Is.SameAs(source));
        }

        [Test]
        public void EnablingFxPreservesActiveCustomLayersAndAddsMissingFxSlot()
        {
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Active.controller");
            descriptor.baseAnimationLayers = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.Gesture) };
            descriptor.specialAnimationLayers = new[] { Layer(source, VRCAvatarDescriptor.AnimLayerType.Sitting) };
            descriptor.customizeAnimationLayers = true;

            VrcPlayableControllers.EnableCustomAnimationLayers(descriptor);

            Assert.That(descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Gesture).animatorController,
                Is.SameAs(source));
            Assert.That(descriptor.specialAnimationLayers[0].animatorController, Is.SameAs(source));
            Assert.That(descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).isDefault,
                Is.True);
        }

        [Test]
        public void CopyingControllerSubassetPreservesTheSelectedLocalFileIdentity()
        {
            var first = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Container.controller");
            first.name = "FirstController";
            first.layers[0].stateMachine.AddState("FirstState");
            var second = new AnimatorController { name = "SecondController" };
            AssetDatabase.AddObjectToAsset(second, first);
            second.AddLayer("SecondLayer");
            second.layers[0].stateMachine.AddState("SecondState");
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(second, out _, out long sourceId), Is.True);

            var copy = Obfuscator.DuplicateAnimatorControllerForBuild(second, "SecondSlot");

            Assert.That(copy, Is.Not.SameAs(first).And.Not.SameAs(second));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(copy, out _, out long copyId), Is.True);
            Assert.That(copyId, Is.EqualTo(sourceId));
            Assert.That(copy.layers[0].name, Is.EqualTo("SecondLayer"));
            Assert.That(copy.layers[0].stateMachine.states[0].state.name, Is.EqualTo("SecondState"));
            copy.layers[0].stateMachine.states[0].state.name = "BuildState";
            Assert.That(first.layers[0].stateMachine.states[0].state.name, Is.EqualTo("FirstState"));
            Assert.That(second.layers[0].stateMachine.states[0].state.name, Is.EqualTo("SecondState"));
        }

        [Test]
        public void FailedMotionResolutionRejectsTheSourceRatherThanReturningAMutableFallback()
        {
            var source = new BlendTree { name = "SourceTree" };
            AssetDatabase.CreateAsset(source, folder + "/SourceTree.asset");
            var child = new AnimationClip { name = "SourceChild" };
            AssetDatabase.CreateAsset(child, folder + "/SourceChild.anim");
            source.children = new[] { new ChildMotion { motion = child, timeScale = 1f } };
            AssetDatabase.SaveAssets();

            // Reusing the source path is precisely the unsafe fallback formerly used
            // after CopyAsset failed. It must fail before walking and writing the tree.
            var samePath = Assert.Throws<TargetInvocationException>(() =>
                Invoke(typeof(Obfuscator), "ResolveMotionFromCopiedAsset", source, AssetDatabase.GetAssetPath(source)));
            Assert.That(samePath.InnerException, Is.TypeOf<InvalidOperationException>());

            var wrongContainer = AnimatorController.CreateAnimatorControllerAtPath(folder + "/NoMotion.controller");
            var missingMotion = Assert.Throws<TargetInvocationException>(() =>
                Invoke(typeof(Obfuscator), "ResolveMotionFromCopiedAsset", source, AssetDatabase.GetAssetPath(wrongContainer)));
            Assert.That(missingMotion.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(source.children[0].motion, Is.SameAs(child));
            Assert.That(source.name, Is.EqualTo("SourceTree"));
            Assert.That(child.name, Is.EqualTo("SourceChild"));
        }

        private static VRCAvatarDescriptor.CustomAnimLayer Layer(AnimatorController controller,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            return new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = false, animatorController = controller };
        }

        private static object Invoke(Type type, string name, params object[] arguments)
        {
            var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, arguments);
        }
    }
}
