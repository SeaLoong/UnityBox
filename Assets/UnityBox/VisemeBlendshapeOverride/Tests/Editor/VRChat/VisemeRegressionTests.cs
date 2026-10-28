using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public class VisemeRegressionTests
    {
        private GameObject avatar;
        private VRCAvatarDescriptor descriptor;
        private SkinnedMeshRenderer face;
        private Mesh mesh;
        private VisemeBlendshapeOverrideComponent config;
        private readonly HashSet<string> generatedFolders = new HashSet<string>();

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("VisemeRegression_" + Guid.NewGuid().ToString("N"));
            descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = true
                }
            };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            descriptor.VisemeBlendShapes = new string[15];
            descriptor.VisemeBlendShapes[1] = "A";
            descriptor.VisemeBlendShapes[2] = "B";
            var faceObject = new GameObject("Face");
            faceObject.transform.SetParent(avatar.transform, false);
            face = faceObject.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { vertices = new[] { Vector3.zero } };
            mesh.AddBlendShapeFrame("A", 100f, new[] { Vector3.up }, null, null);
            mesh.AddBlendShapeFrame("B", 100f, new[] { Vector3.right }, null, null);
            face.sharedMesh = mesh;
            descriptor.VisemeSkinnedMesh = face;
            config = avatar.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;
        }

        [TearDown]
        public void TearDown()
        {
            RememberGeneratedFolder();
            UnityEngine.Object.DestroyImmediate(avatar);
            UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var folder in generatedFolders) AssetDatabase.DeleteAsset(folder);
            generatedFolders.Clear();
        }

        [Test]
        public void SdkConfigurationKeepsRealEditorOnlyMarkerBeforePreprocessing()
        {
            Assert.That(config, Is.InstanceOf<IEditorOnly>());
        }

        [Test]
        public void OptionalAdapterRemovesAuthoringComponentsFromBuildObject()
        {
            var child = new GameObject("Child authoring settings");
            child.transform.SetParent(avatar.transform, false);
            child.AddComponent<VisemeBlendshapeOverrideComponent>();
            Assert.That(new VisemeBlendshapeOverrideStripCallback().OnPreprocessAvatar(avatar), Is.True);
            Assert.That(avatar.GetComponentsInChildren<VisemeBlendshapeOverrideComponent>(true), Is.Empty);
            Assert.That(descriptor, Is.Not.Null);
        }

        [Test]
        public void SdkPreprocessBuildsWhenNdmfDidNotConsumeConfiguration()
        {
            var callback = new VisemeBlendshapeOverridePreprocessCallback();
            Assert.That(callback.OnPreprocessAvatar(avatar), Is.True);
            var controller = descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.layers.Count(layer =>
                layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
            Assert.That(descriptor.lipSync, Is.EqualTo(VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly));
            Assert.That(avatar.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.Null);

            Assert.That(callback.OnPreprocessAvatar(avatar), Is.True);
            Assert.That(descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController, Is.SameAs(controller),
                "A repeated callback must not create another controller after successful consumption.");
        }

        [Test]
        public void SdkPreprocessPreservesCompletedNdmfOutput()
        {
            var controller = Build();
            // The NDMF pass consumes its configuration only after generating the virtual layer.
            // Preserve the same completion signal without introducing a hard NDMF test dependency.
            UnityEngine.Object.DestroyImmediate(config);
            Assert.That(new VisemeBlendshapeOverridePreprocessCallback().OnPreprocessAvatar(avatar), Is.True);
            Assert.That(descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController, Is.SameAs(controller));
        }

        [Test]
        public void NewConfigurationOnSameAvatarCanBuildAgain()
        {
            var callback = new VisemeBlendshapeOverridePreprocessCallback();
            Assert.That(callback.OnPreprocessAvatar(avatar), Is.True);
            var first = descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            RememberGeneratedFolder();
            var firstBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(first));
            config = avatar.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.globalWeight = 20f;
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;

            Assert.That(callback.OnPreprocessAvatar(avatar), Is.True);
            Assert.That(descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController, Is.Not.SameAs(first));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(first)), Is.EqualTo(firstBytes));
            Assert.That(avatar.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.Null);
        }

        [Test]
        public void FailedSdkPreprocessKeepsConfigurationForRetry()
        {
            var original = new AnimatorController();
            var overrides = new AnimatorOverrideController(original);
            descriptor.customizeAnimationLayers = true;
            try
            {
                descriptor.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX,
                        isDefault = false,
                        animatorController = overrides
                    }
                };
                var callback = new VisemeBlendshapeOverridePreprocessCallback();
                LogAssert.Expect(LogType.Error, new Regex("\\[Viseme Blendshape Override\\] Avatar processing failed:"));
                Assert.That(callback.OnPreprocessAvatar(avatar), Is.False);
                Assert.That(avatar.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.SameAs(config));
                Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(overrides));
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                Assert.That(callback.OnPreprocessAvatar(avatar), Is.True);
                Assert.That(avatar.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overrides);
                UnityEngine.Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void DisabledComponentDoesNotCreateABuildPlan()
        {
            config.enabled = false;
            Assert.That(VisemeBlendshapeOverrideVrcProcessor.TryCreateBuildPlan(avatar, out _), Is.False);
        }

        [Test]
        public void DefaultsPreferDescriptorFaceOverUnrelatedBody()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(avatar.transform, false);
            body.AddComponent<SkinnedMeshRenderer>();
            config.targetRenderer = null;
            config.EnsureEditorDefaults();
            Assert.That(config.ResolveTargetRenderer(descriptor.VisemeSkinnedMesh), Is.SameAs(face));
        }

        [Test]
        public void EmptyBindingContinuesFollowingDescriptorEdits()
        {
            var binding = config.GetBinding((VisemeBlendshapeOverrideComponent.Viseme)1);
            binding.blendshapeName = string.Empty;
            config.EnsureEditorDefaults();
            Assert.That(binding.blendshapeName, Is.Empty);
            Assert.That(config.ResolveBlendshapeName(descriptor.VisemeBlendShapes, binding), Is.EqualTo("A"));
            descriptor.VisemeBlendShapes[1] = "B";
            config.EnsureEditorDefaults();
            Assert.That(config.ResolveBlendshapeName(descriptor.VisemeBlendShapes, binding), Is.EqualTo("B"));
            binding.blendshapeName = VisemeBlendshapeOverrideComponent.NoneBlendshapeValue;
            Assert.That(config.ResolveBlendshapeName(descriptor.VisemeBlendShapes, binding), Is.Empty);
        }

        [TestCase(1f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(float.NaN, float.NaN)]
        public void VoiceRangeAlwaysHasTwoFiniteDistinctThresholds(float min, float max)
        {
            VisemeBlendshapeOverrideUtils.NormalizeVoiceRange(ref min, ref max);
            Assert.That(float.IsNaN(min) || float.IsNaN(max), Is.False);
            Assert.That(min, Is.InRange(0f, 1f));
            Assert.That(max, Is.InRange(0f, 1f));
            Assert.That(max, Is.GreaterThan(min));
        }

        [Test]
        public void FallbackPersistsStatesTransitionsAndResetsOtherMouthShapes()
        {
            config.globalWeight = 35f;
            descriptor.customizeAnimationLayers = false;
            var controller = Build();
            Assert.That(descriptor.customizeAnimationLayers, Is.True);
            Assert.That(descriptor.lipSync, Is.EqualTo(VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly));
            var layer = controller.layers.Single(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            Assert.That(layer.stateMachine.states, Has.Length.EqualTo(15));
            Assert.That(layer.stateMachine.anyStateTransitions, Has.Length.EqualTo(15));
            var assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            foreach (var state in layer.stateMachine.states)
                Assert.That(assets, Does.Contain(state.state), "Generated state must survive asset reload");
            foreach (var transition in layer.stateMachine.anyStateTransitions)
                Assert.That(assets, Does.Contain(transition), "Generated transition must survive asset reload");
            var targetState = layer.stateMachine.states.Single(item => item.state.name ==
                VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + (VisemeBlendshapeOverrideComponent.Viseme)1).state;
            var clip = (AnimationClip)targetState.motion;
            AssertCurve(clip, "blendShape.A", 35f);
            AssertCurve(clip, "blendShape.B", 0f);
        }

        [Test]
        public void RebuildingUsesANewControllerWithoutMutatingPreviousBuild()
        {
            var first = Build();
            var firstBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(first));
            config.globalWeight = 20f;
            var second = Build();
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(first)), Is.EqualTo(firstBytes));
            Assert.That(second.layers.Count(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
        }

        [Test]
        public void ExplicitNoneDoesNotAnimateTheDescriptorsMappedBlendshape()
        {
            config.GetBinding((VisemeBlendshapeOverrideComponent.Viseme)1).blendshapeName =
                VisemeBlendshapeOverrideComponent.NoneBlendshapeValue;
            var controller = Build();
            var layer = controller.layers.Single(item =>
                item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            var disabledState = layer.stateMachine.states.Single(item => item.state.name ==
                VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + (VisemeBlendshapeOverrideComponent.Viseme)1).state;
            var clip = (AnimationClip)disabledState.motion;

            Assert.That(AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.A")),
                Is.Null, "An explicitly unmapped shape must not be reclaimed from the Descriptor.");
            AssertCurve(clip, "blendShape.B", 0f);
            var mappedState = layer.stateMachine.states.Single(item => item.state.name ==
                VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + (VisemeBlendshapeOverrideComponent.Viseme)2).state;
            AssertCurve((AnimationClip)mappedState.motion, "blendShape.B", 100f);
        }

        [Test]
        public void UnsupportedOverrideControllerFailsWithoutReplacingExistingFx()
        {
            var original = new AnimatorController();
            var overrides = new AnimatorOverrideController(original);
            descriptor.customizeAnimationLayers = true;
            try
            {
                descriptor.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX,
                        isDefault = false,
                        animatorController = overrides
                    }
                };
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;

                Assert.Throws<InvalidOperationException>(() =>
                    VisemeBlendshapeOverrideVrcProcessor.ProcessAvatar(avatar));
                Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(overrides));
                Assert.That(descriptor.lipSync, Is.EqualTo(VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overrides);
                UnityEngine.Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void FallbackCopiesTheSelectedControllerSubassetFromAMultiControllerFile()
        {
            var folder = "Assets/VisemeControllerIdentity_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            generatedFolders.Add(folder);
            var sourcePath = folder + "/Source.controller";
            var main = AnimatorController.CreateAnimatorControllerAtPath(sourcePath);
            main.AddLayer("UnselectedMainLayer");
            var selected = new AnimatorController { name = "SelectedController" };
            AssetDatabase.AddObjectToAsset(selected, main);
            selected.AddLayer("SelectedSourceLayer");
            AssetDatabase.SaveAssets();
            var sourceBytes = File.ReadAllBytes(sourcePath);
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = selected,
            } };

            var result = Build();

            Assert.That(result, Is.Not.SameAs(selected));
            Assert.That(result.layers.Any(layer => layer.name == "SelectedSourceLayer"), Is.True);
            Assert.That(result.layers.Any(layer => layer.name == "UnselectedMainLayer"), Is.False);
            Assert.That(result.layers.Count(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(sourceBytes));
        }

        [Test]
        public void FallbackDoesNotEnableDormantControllerAssignments()
        {
            var dormant = new AnimatorController();
            var unsupportedDormant = new AnimatorOverrideController(dormant);
            try
            {
                descriptor.customizeAnimationLayers = false;
                descriptor.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false,
                        animatorController = unsupportedDormant,
                    },
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = false,
                        animatorController = dormant,
                    },
                };
                descriptor.specialAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.Sitting, isDefault = false,
                        animatorController = dormant,
                    },
                };

                var result = Build();

                Assert.That(result, Is.Not.Null, "The inactive override controller must not be read or cloned.");
                Assert.That(descriptor.customizeAnimationLayers, Is.True);
                Assert.That(descriptor.baseAnimationLayers[1].isDefault, Is.True);
                Assert.That(descriptor.baseAnimationLayers[1].animatorController, Is.Null);
                Assert.That(descriptor.specialAnimationLayers[0].isDefault, Is.True);
                Assert.That(descriptor.specialAnimationLayers[0].animatorController, Is.Null);
                Assert.That(dormant.layers, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unsupportedDormant);
                UnityEngine.Object.DestroyImmediate(dormant);
            }
        }

        [Test]
        public void FallbackCanInitializeMissingFxAndNullLayerArrays()
        {
            descriptor.baseAnimationLayers = null;
            descriptor.specialAnimationLayers = null;
            Assert.That(Build(), Is.Not.Null);
        }

        [Test]
        public void MissingFxEntryIsAddedWithoutReplacingOtherDescriptorLayers()
        {
            var existingController = new AnimatorController();
            try
            {
                descriptor.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.Gesture,
                        isDefault = false,
                        animatorController = existingController
                    }
                };
                int fxIndex = VisemeBlendshapeOverrideVrcUtils.EnsureFxLayer(descriptor);
                Assert.That(descriptor.baseAnimationLayers, Has.Length.EqualTo(2));
                Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(existingController));
                Assert.That(descriptor.baseAnimationLayers[0].isDefault, Is.False);
                Assert.That(descriptor.baseAnimationLayers[fxIndex].type,
                    Is.EqualTo(VRCAvatarDescriptor.AnimLayerType.FX));
                Assert.That(VisemeBlendshapeOverrideVrcUtils.EnsureFxLayer(descriptor), Is.EqualTo(fxIndex));
                Assert.That(descriptor.baseAnimationLayers, Has.Length.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(existingController);
            }
        }

        [TestCase("..")]
        [TestCase(".")]
        [TestCase("../Outside")]
        [TestCase("..\\Outside")]
        [TestCase("A\\B/C")]
        public void GeneratedControllerPathStaysInsideGeneratedFolder(string avatarName)
        {
            avatar.name = avatarName;
            var method = typeof(VisemeBlendshapeOverrideVrcUtils).GetMethod(
                "GetFallbackGeneratedControllerPath", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            string path = (string)method.Invoke(null, new object[] { avatar });
            const string generatedRoot = "Assets/UnityBox/VisemeBlendshapeOverride/Generated/";
            Assert.That(path, Does.StartWith(generatedRoot));
            Assert.That(path.Substring(generatedRoot.Length).Split('/'), Has.Length.EqualTo(2));
            Assert.That(path, Does.Not.Contain("\\"));
            string fullRoot = Path.GetFullPath(generatedRoot).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(path), Does.StartWith(fullRoot));
        }

        private AnimatorController Build()
        {
            VisemeBlendshapeOverrideVrcProcessor.ProcessAvatar(avatar);
            RememberGeneratedFolder();
            return VisemeBlendshapeOverrideVrcUtils.GetExistingFxController(descriptor);
        }

        private void RememberGeneratedFolder()
        {
            var controller = VisemeBlendshapeOverrideVrcUtils.GetExistingFxController(descriptor);
            var path = controller != null ? AssetDatabase.GetAssetPath(controller) : "";
            if (!string.IsNullOrEmpty(path)) generatedFolders.Add(Path.GetDirectoryName(path).Replace('\\', '/'));
        }

        private static void AssertCurve(AnimationClip clip, string property, float value)
        {
            var curve = AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), property));
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.Evaluate(0f), Is.EqualTo(value));
        }
    }
}
