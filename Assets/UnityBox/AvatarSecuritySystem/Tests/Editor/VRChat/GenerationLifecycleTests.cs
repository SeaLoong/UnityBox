using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class GenerationLifecycleTests
    {
        private Scene scene;
        private readonly List<GameObject> avatars = new List<GameObject>();
        private readonly HashSet<string> generatedFolders = new HashSet<string>();

        [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            new Processor().OnPostprocessAvatar();
            foreach (var avatar in avatars)
            {
                Processor.ReleaseBuildSnapshot(avatar);
                Object.DestroyImmediate(avatar);
            }
            avatars.Clear();
            foreach (var folder in generatedFolders) AssetDatabase.DeleteAsset(folder);
            generatedFolders.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        private GameObject Avatar(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRCAvatarDescriptor>().baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = root.AddComponent<ASSComponent>();
            config.disableDefense = config.disableOverlay = config.disableWarningSound = config.disableObfuscation = true;
            config.gesturePassword = new List<int> { 1, 2 };
            new GameObject("Body").transform.SetParent(root.transform, false);
            avatars.Add(root);
            return root;
        }

        private AnimatorController Process(GameObject avatar, bool ndmf)
        {
            Assert.That(Processor.ProcessAvatar(avatar, ndmf), Is.True);
            var controller = (AnimatorController)avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string folder = controllerPath.Substring(0, controllerPath.IndexOf("/", Constants.ASSET_FOLDER.Length + 1));
            generatedFolders.Add(folder);
            return controller;
        }

        [Test]
        public void DifferentAvatarOutputsAndParametersSurviveAnotherAvatarGenerationAndRetry()
        {
            var first = Avatar("First");
            var second = Avatar("Second");
            var firstController = Process(first, true);
            string firstPath = AssetDatabase.GetAssetPath(firstController);
            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            var firstParameters = first.GetComponent<VRCAvatarDescriptor>().expressionParameters;
            var secondController = Process(second, true);
            Assert.That(AssetDatabase.GetAssetPath(secondController), Is.Not.EqualTo(firstPath));
            Process(second, true);
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(firstPath), Is.SameAs(firstController));
            Assert.That(AssetDatabase.AssetPathToGUID(firstPath), Is.EqualTo(firstGuid));
            Assert.That(first.GetComponent<VRCAvatarDescriptor>().expressionParameters, Is.SameAs(firstParameters));
            Assert.That(firstParameters.parameters.Any(parameter => parameter.name == Constants.PARAM_PASSWORD_CORRECT), Is.True);
            Assert.That(firstController.layers.All(layer => layer.syncedLayerIndex == -1), Is.True);
        }

        [Test]
        public void SdkPostprocessReclaimsOnlyItsOwnOutputAndPreservesOtherBuildOutputs()
        {
            var ndmfAvatar = Avatar("NdmfOutput");
            var controller = Process(ndmfAvatar, true);
            string outputPath = AssetDatabase.GetAssetPath(controller);
            var sdkAvatar = Avatar("SdkOutput");
            var sdkController = Process(sdkAvatar, false);
            string sdkPath = AssetDatabase.GetAssetPath(sdkController);
            new Processor().OnPostprocessAvatar();
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath), Is.SameAs(controller));
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(sdkPath), Is.Null);
            Assert.That(sdkAvatar.GetComponent<VRCAvatarDescriptor>().customizeAnimationLayers, Is.False);
        }

        [Test]
        public void SupervisorControlsKeepTheirSiblingTargetsAfterLayerInsertion()
        {
            var avatar = Avatar("Targets");
            var controller = Process(avatar, true);
            var layers = controller.layers.ToList();
            layers.Insert(1, Utils.CreateLayer("Inserted user layer"));
            controller.layers = layers.ToArray();
            Processor.ProcessPlayableObfuscation(avatar, true);
            var final = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            foreach (var state in final.layers.SelectMany(layer => layer.stateMachine.states).Select(item => item.state))
            foreach (var control in state.behaviours.OfType<VRCAnimatorLayerControl>())
            {
                if (!control.debugString.StartsWith(AnimatorLifecycle.TargetLayerPrefix, StringComparison.Ordinal)) continue;
                string target = control.debugString.Substring(AnimatorLifecycle.TargetLayerPrefix.Length);
                Assert.That(final.layers[control.layer].name, Is.EqualTo(target));
                Assert.That(control.goalWeight, Is.EqualTo(1f));
            }
        }
    }
}
