using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
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
    public sealed class ObfuscationNdmfOutputTests
    {
        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var item in machine.states) yield return item.state;
            foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
        }

        [Test]
        public void NdmfFinishThenLateSdkCallbackPreservesSelectedDecoysAndNativeUserAnimation()
        {
            string folder = "Assets/__ASSObfuscationNdmf_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("NDMF obfuscated graph");
            SceneManager.MoveGameObjectToScene(root, scene);
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            var light = body.AddComponent<Light>();
            var motion = new AnimationClip { name = "Original user motion" };
            motion.SetCurve("Body", typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 4.25f));
            AssetDatabase.CreateAsset(motion, folder + "/User.anim");
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/User.controller");
            var originalState = source.layers[0].stateMachine.AddState("Original user state");
            originalState.writeDefaultValues = false; originalState.motion = motion;
            source.layers[0].stateMachine.defaultState = originalState;
            AssetDatabase.SaveAssets();
            var original = new[] { "/User.anim", "/User.anim.meta", "/User.controller", "/User.controller.meta" }
                .ToDictionary(path => folder + path, path => File.ReadAllBytes(folder + path));
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
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var context = new BuildContext(root, folder + "/NdmfAssets");
            string generated = null;
            try
            {
                var resolverType = typeof(BuildContext).Assembly.GetType("nadena.dev.ndmf.PluginResolver", true);
                var constructor = resolverType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(candidate => candidate.GetParameters().Length == 3 &&
                        candidate.GetParameters()[0].ParameterType == typeof(IEnumerable<Type>));
                var resolver = constructor.Invoke(new object[] { new[] { typeof(NDMFPlugin) }, null, true });
                var phases = (IEnumerable)resolverType.GetProperty("Passes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(resolver);
                var runPass = typeof(BuildContext).GetMethod("RunPass", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var phase in phases)
                foreach (var pass in (IEnumerable)phase.GetType().GetField("Item2").GetValue(phase))
                    runPass.Invoke(context, new[] { pass });
                generated = Obfuscator.GeneratedFolder;
                typeof(BuildContext).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
                Assert.That(Processor.WasProcessedByNdmf(root), Is.True);
                Assert.That(root.GetComponent<ASSComponent>(), Is.Null);
                Assert.That(new PlayableObfuscationProcessor().OnPreprocessAvatar(root), Is.True);
                var final = (AnimatorController)descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(final, Is.Not.SameAs(source));
                Assert.That(final.layers[0].name, Is.Not.EqualTo(source.layers[0].name));
                Assert.That(States(final.layers[0].stateMachine).Single().name, Is.Not.EqualTo("Original user state"));
                var selectedStates = Obfuscator.GetInstructionalStateNames(100);
                Assert.That(final.layers.SelectMany(layer => States(layer.stateMachine)).Any(state => selectedStates.Contains(state.name)), Is.True);
                var clipNames = Obfuscator.GetInstructionalClipNames(100);
                Assert.That(final.animationClips.Any(clip => clipNames.Contains(clip.name)), Is.True);
                animator.runtimeAnimatorController = final; animator.Rebind();
                for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
                Assert.That(light.intensity, Is.EqualTo(4.25f).Within(0.001f));
                foreach (var file in original) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
                Assert.That(originalState.name, Is.EqualTo("Original user state"));
                Assert.That(originalState.writeDefaultValues, Is.False);
            }
            finally
            {
                context.DeactivateAllExtensionContexts();
                Processor.ReleaseBuildSnapshot(root);
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                if (generated != null) AssetDatabase.DeleteAsset(generated);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
