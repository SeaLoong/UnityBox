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

            Obfuscator.PreparePlayableControllerCopies(descriptor, fxOnly: true);

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
            Obfuscator.PreparePlayableControllerCopies(descriptor);
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
