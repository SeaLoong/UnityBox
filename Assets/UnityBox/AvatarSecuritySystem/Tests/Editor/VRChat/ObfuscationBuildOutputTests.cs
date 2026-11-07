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
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class ObfuscationBuildOutputTests
    {
        private string folder, generated;
        private Scene scene;
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSObfuscationBuild_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Final playable obfuscation");
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        [TearDown]
        public void TearDown()
        {
            Processor.ReleaseBuildSnapshot(root);
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            if (generated != null) AssetDatabase.DeleteAsset(generated);
            AssetDatabase.DeleteAsset(folder);
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var item in machine.states) yield return item.state;
            foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LateSdkHookRenamesTheIsolatedGraphAndKeepsContactsAndSelectedDecoys(bool contact)
        {
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            var light = body.AddComponent<Light>();
            var clip = new AnimationClip { name = "Original user motion" };
            clip.SetCurve("Body", typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 3.5f));
            AssetDatabase.CreateAsset(clip, folder + "/User.anim");
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/User.controller");
            var state = source.layers[0].stateMachine.AddState("Original user state");
            state.motion = clip; state.writeDefaultValues = false;
            source.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();
            var original = new[] { "/User.anim", "/User.anim.meta", "/User.controller", "/User.controller.meta" }
                .ToDictionary(path => folder + path, path => File.ReadAllBytes(folder + path));
            string sourceLayer = source.layers[0].name;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = source,
            } };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = root.AddComponent<ASSComponent>();
            config.disableDefense = config.disableOverlay = config.disableWarningSound = true;
            config.disableObfuscation = false;
            config.enablePlayableLayerObfuscation = config.enableDecoyLayers = config.enableDecoyStates = true;
            config.gesturePassword = new List<int> { 1, 2 };
            if (contact)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver", false))
                    .FirstOrDefault(candidate => candidate != null);
                Assert.That(type, Is.Not.Null);
                body.AddComponent(type);
            }
            Assert.That(Processor.ProcessAvatar(root, true), Is.True);
            generated = Obfuscator.GeneratedFolder;
            Assert.That(new PlayableObfuscationProcessor().OnPreprocessAvatar(root), Is.True);
            var final = (AnimatorController)descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(final, Is.Not.SameAs(source));
            Assert.That(final.layers[0].name == sourceLayer, Is.EqualTo(contact));
            Assert.That(States(final.layers[0].stateMachine).Single().name == "Original user state", Is.EqualTo(contact));
            var markers = Obfuscator.GetInstructionalStateNames(100);
            Assert.That(final.layers.SelectMany(layer => States(layer.stateMachine)).Any(item => markers.Contains(item.name)), Is.True);
            var selectedClips = Obfuscator.GetInstructionalClipNames(100);
            var metadataClips = final.animationClips.Where(item => selectedClips.Contains(item.name)).ToArray();
            Assert.That(metadataClips, Is.Not.Empty);
            foreach (var metadata in metadataClips)
            foreach (var binding in AnimationUtility.GetCurveBindings(metadata))
                Assert.That(root.transform.Find(binding.path), Is.Null);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = final; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            Assert.That(light.intensity, Is.EqualTo(3.5f).Within(0.001f));
            Assert.That(source.layers[0].stateMachine.states.Single().state.name, Is.EqualTo("Original user state"));
            Assert.That(source.layers[0].stateMachine.states.Single().state.writeDefaultValues, Is.False);
            foreach (var file in original) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        }
    }
}
