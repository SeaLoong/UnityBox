using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class StandaloneBuildTests
    {
        private string folder;
        private Scene authoringScene, buildScene;
        private readonly List<string> generatedFolders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSAutomaticBuild_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            authoringScene = EditorSceneManager.NewPreviewScene();
            buildScene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(buildScene);
            EditorSceneManager.ClosePreviewScene(authoringScene);
            foreach (string generated in generatedFolders.Distinct()) AssetDatabase.DeleteAsset(generated);
            generatedFolders.Clear();
            AssetDatabase.DeleteAsset(folder);
        }

        private GameObject CreateAuthoring()
        {
            var root = new GameObject("AutomaticASS");
            SceneManager.MoveGameObjectToScene(root, authoringScene);
            new GameObject("Body").transform.SetParent(root.transform, false);
            var config = root.AddComponent<ASSComponent>();
            config.gesturePassword = new List<int> { 1, 2 };
            config.gestureHoldTime = 0.12f;
            config.gestureErrorTolerance = 0.04f;
            config.gestureMaxHoldTime = 1f;
            config.countdownDuration = 8f;
            config.disableDefense = true;
            config.disableOverlay = true;
            config.disableWarningSound = true;
            config.disableObfuscation = true;
            return root;
        }

        private GameObject BuildCopy(GameObject source)
        {
            var clone = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(clone, buildScene);
            Assert.That(StandaloneConfigurationStripper.ProcessSceneForBuild(buildScene), Is.EqualTo(1));
            var controller = (AnimatorController)clone.GetComponent<Animator>().runtimeAnimatorController;
            generatedFolders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller)).Replace('\\', '/'));
            Assert.That(clone.GetComponent<ASSComponent>(), Is.Null);
            Assert.That(clone.GetComponent<StandaloneSecurityInput>(), Is.Not.Null);
            Assert.That(StandaloneConfigurationStripper.ProcessSceneForBuild(buildScene), Is.Zero);
            return clone;
        }

        [Test]
        public void ASceneWithoutAnAnimatorBuildsItsGestureSystemWithoutAnyManualGeneration()
        {
            var source = CreateAuthoring();
            var config = source.GetComponent<ASSComponent>();
            var clone = BuildCopy(source);
            var controller = (AnimatorController)clone.GetComponent<Animator>().runtimeAnimatorController;
            Assert.That(controller.layers.Count(layer => layer.name == Constants.LAYER_PASSWORD_INPUT), Is.EqualTo(1));
            Assert.That(controller.layers.Any(layer => layer.name == Constants.LAYER_LOCK), Is.False);
            Assert.That(source.GetComponent<ASSComponent>(), Is.SameAs(config));
            Assert.That(source.GetComponent<Animator>(), Is.Null);
            Assert.That(source.GetComponent<StandaloneSecurityInput>(), Is.Null);
            Assert.That(source.transform.childCount, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BuildPreservesExistingWdModesUserAnimationAndSourceController(bool writeDefaults)
        {
            var source = CreateAuthoring();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            var clip = new AnimationClip();
            clip.SetCurve("Body", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0f, 1f, 2.5f));
            AssetDatabase.CreateAsset(clip, folder + "/Source.anim");
            var state = controller.layers[0].stateMachine.AddState("User state");
            state.writeDefaultValues = writeDefaults;
            state.motion = clip;
            var oldLock = new AnimatorStateMachine();
            oldLock.AddState("Legacy lock").motion = clip;
            controller.AddLayer(new AnimatorControllerLayer { name = "ASS_Lock", stateMachine = oldLock, defaultWeight = 1f, syncedLayerIndex = -1 });
            source.AddComponent<Animator>().runtimeAnimatorController = controller;
            Utils.SaveControllerGraph(controller);
            AssetDatabase.SaveAssets();
            byte[] original = File.ReadAllBytes(AssetDatabase.GetAssetPath(controller));

            var clone = BuildCopy(source);
            var animator = clone.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var generated = (AnimatorController)animator.runtimeAnimatorController;
            Assert.That(generated, Is.Not.SameAs(controller));
            Assert.That(generated.layers[0].stateMachine, Is.Not.SameAs(controller.layers[0].stateMachine));
            Assert.That(generated.layers[0].stateMachine.states.Single().state.writeDefaultValues, Is.EqualTo(writeDefaults));
            Assert.That(generated.layers.Any(layer => layer.name == "ASS_Lock"), Is.False);
            for (int frame = 0; frame < 240; frame++)
            {
                animator.SetInteger(Constants.PARAM_GESTURE_LEFT, frame < 30 ? 0 : frame < 60 ? 1 : 2);
                animator.Update(1f / 60f);
                Assert.That(clone.transform.Find("Body").localScale.x, Is.EqualTo(2.5f).Within(0.001f), "Frame " + frame);
            }
            Assert.That(animator.GetBool(Constants.PARAM_PASSWORD_CORRECT), Is.True);
            Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.SameAs(controller));
            Assert.That(controller.layers, Has.Length.EqualTo(2));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(controller)), Is.EqualTo(original));
        }

        [TestCase(AnimatorControllerParameterType.Bool)]
        [TestCase(AnimatorControllerParameterType.Int)]
        [TestCase(AnimatorControllerParameterType.Float)]
        public void BuildPreservesExistingParameterTypesAndDefaults(AnimatorControllerParameterType localType)
        {
            var source = CreateAuthoring();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Defaults.controller");
            controller.layers[0].stateMachine.AddState("User").writeDefaultValues = false;
            controller.AddParameter(new AnimatorControllerParameter { name = "IsLocal", type = localType,
                defaultBool = false, defaultInt = 0, defaultFloat = 0.25f });
            controller.AddParameter(new AnimatorControllerParameter { name = "UserSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 2.75f });
            controller.AddParameter(new AnimatorControllerParameter { name = "UserSaved", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            source.AddComponent<Animator>().runtimeAnimatorController = controller;
            AssetDatabase.SaveAssets();
            var originalParameters = controller.parameters;
            byte[] originalBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(controller));
            var clone = BuildCopy(source);
            var generated = clone.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
            foreach (var original in originalParameters)
            {
                var actual = generated.parameters.Single(parameter => parameter.name == original.name);
                Assert.That(actual.type, Is.EqualTo(original.type));
                Assert.That(actual.defaultBool, Is.EqualTo(original.defaultBool));
                Assert.That(actual.defaultInt, Is.EqualTo(original.defaultInt));
                Assert.That(actual.defaultFloat, Is.EqualTo(original.defaultFloat));
            }
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(controller)), Is.EqualTo(originalBytes));
        }

        [Test]
        public void BuildPreservesTheEffectiveOverrideClip()
        {
            var source = CreateAuthoring();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Base.controller");
            var original = new AnimationClip();
            original.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 1f));
            var replacement = new AnimationClip();
            replacement.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 7f));
            AssetDatabase.CreateAsset(original, folder + "/Original.anim");
            AssetDatabase.CreateAsset(replacement, folder + "/Replacement.anim");
            controller.layers[0].stateMachine.AddState("User").motion = original;
            var overridden = new AnimatorOverrideController(controller);
            overridden[original] = replacement;
            AssetDatabase.CreateAsset(overridden, folder + "/Override.overrideController");
            source.AddComponent<Animator>().runtimeAnimatorController = overridden;
            var clone = BuildCopy(source);
            var animator = clone.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            for (int i = 0; i < 30; i++) animator.Update(1f / 60f);
            Assert.That(clone.transform.Find("Body").localPosition.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(source.GetComponent<Animator>().runtimeAnimatorController, Is.SameAs(overridden));
            Assert.That(controller.layers[0].stateMachine.states.Single().state.motion, Is.SameAs(original));
        }

        [Test]
        public void AnEmptyPasswordDisablesGenerationButStripsTheAuthoringComponent()
        {
            var source = CreateAuthoring();
            source.GetComponent<ASSComponent>().gesturePassword.Clear();
            var clone = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(clone, buildScene);
            Assert.That(StandaloneConfigurationStripper.ProcessSceneForBuild(buildScene), Is.Zero);
            Assert.That(clone.GetComponent<ASSComponent>(), Is.Null);
            Assert.That(clone.GetComponent<Animator>(), Is.Null);
            Assert.That(source.GetComponent<ASSComponent>(), Is.Not.Null);
        }

        [Test]
        public void OrdinarySceneLoadingDoesNotGenerateOrStripAuthoring()
        {
            var source = CreateAuthoring();
            new StandaloneConfigurationStripper().OnProcessScene(authoringScene, null);
            Assert.That(source.GetComponent<ASSComponent>(), Is.Not.Null);
            Assert.That(source.GetComponent<Animator>(), Is.Null);
        }
    }
}
