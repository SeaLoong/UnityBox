using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class AssUpstreamVirtualProbe : Plugin<AssUpstreamVirtualProbe>
    {
        public override string QualifiedName => "unitybox.tests.ass.upstream";
        protected override void Configure() => InPhase(BuildPhase.PlatformFinish)
            .BeforePlugin("top.sealoong.unitybox.avatar-security-system")
            .WithRequiredExtension(typeof(AnimatorServicesContext), sequence => sequence.Run("Upstream virtual edit", context =>
            {
                var services = context.Extension<AnimatorServicesContext>();
                var controller = services.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                var layer = controller.AddLayer(new LayerPriority(100), "Upstream virtual output");
                layer.DefaultWeight = 1f;
                var clip = VirtualClip.Create("Upstream light");
                string path = services.ObjectPathRemapper.GetVirtualPathForObject(context.AvatarRootTransform.Find("Body").gameObject);
                clip.SetFloatCurve(path, typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 3f));
                layer.StateMachine.DefaultState = layer.StateMachine.AddState("Upstream", clip, Vector3.zero);
            }));
    }

    public sealed class AssDownstreamVirtualProbe : Plugin<AssDownstreamVirtualProbe>
    {
        public override string QualifiedName => "unitybox.tests.ass.downstream";
        protected override void Configure() => InPhase(BuildPhase.PlatformFinish)
            .AfterPlugin("top.sealoong.unitybox.avatar-security-system")
            .WithRequiredExtension(typeof(AnimatorServicesContext), sequence => sequence.Run("Downstream virtual edit", context =>
            {
                var services = context.Extension<AnimatorServicesContext>();
                var controller = services.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                Assert.That(controller.Layers.Any(layer => layer.Name == Constants.LAYER_PASSWORD_INPUT), Is.True);
                var layer = controller.AddLayer(new LayerPriority(200), "Downstream virtual output");
                layer.DefaultWeight = 1f;
                var clip = VirtualClip.Create("Downstream audio volume");
                string path = services.ObjectPathRemapper.GetVirtualPathForObject(context.AvatarRootTransform.Find("Body").gameObject);
                clip.SetFloatCurve(path, typeof(AudioSource), "m_Volume", AnimationCurve.Constant(0f, 1f, 0.75f));
                layer.StateMachine.DefaultState = layer.StateMachine.AddState("Downstream", clip, Vector3.zero);
            }));
    }

    public sealed class NdmfFinalizationTests
    {
        [Test]
        public void ScheduledPhysicalGenerationPreservesVirtualEditsBeforeAndAfterFinalCommit()
        {
            string folder = "Assets/__ASSNdmf_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("ASS NDMF scheduled finalization");
            SceneManager.MoveGameObjectToScene(root, scene);
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var light = body.AddComponent<Light>();
            var audio = body.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            source.layers[0].stateMachine.AddState("User original");
            AssetDatabase.SaveAssets();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = source,
            } };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = root.AddComponent<ASSComponent>();
            config.disableDefense = config.disableOverlay = config.disableWarningSound = config.disableObfuscation = true;
            config.gesturePassword = new List<int> { 1, 2 };
            var context = new BuildContext(root, folder + "/NdmfAssets");
            string generatedFolder = null;
            try
            {
                var resolverType = typeof(BuildContext).Assembly.GetType("nadena.dev.ndmf.PluginResolver", true);
                var constructor = resolverType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(candidate => candidate.GetParameters().Length == 3 &&
                        candidate.GetParameters()[0].ParameterType == typeof(IEnumerable<Type>));
                var resolver = constructor.Invoke(new object[] { new[]
                {
                    typeof(AssUpstreamVirtualProbe), typeof(NDMFPlugin), typeof(AssDownstreamVirtualProbe),
                }, null, true });
                var phases = (IEnumerable)resolverType.GetProperty("Passes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(resolver);
                var runPass = typeof(BuildContext).GetMethod("RunPass", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var phase in phases)
                foreach (var pass in (IEnumerable)phase.GetType().GetField("Item2").GetValue(phase))
                    runPass.Invoke(context, new[] { pass });
                generatedFolder = Obfuscator.GeneratedFolder;
                typeof(BuildContext).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);

                var final = descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    .animatorController as AnimatorController;
                Assert.That(final, Is.Not.Null);
                Assert.That(final.layers.Count(layer => layer.name == Constants.LAYER_PASSWORD_INPUT), Is.EqualTo(1));
                Assert.That(final.layers.Count(layer => layer.name == Constants.LAYER_LIFECYCLE), Is.EqualTo(1));
                Assert.That(final.layers.Any(layer => layer.name == "Upstream virtual output"), Is.True);
                Assert.That(final.layers.Any(layer => layer.name == "Downstream virtual output"), Is.True);
                Assert.That(root.GetComponent<ASSComponent>(), Is.Null);
                Assert.That(Processor.WasProcessedByNdmf(root), Is.True);
                Assert.That(source.layers.Length, Is.EqualTo(1));
                Assert.That(source.layers[0].stateMachine.states.Single().state.name, Is.EqualTo("User original"));
                Assert.That(source.parameters, Is.Empty);

                var graph = PlayableGraph.Create("ASS committed upstream/downstream outputs");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimatorControllerPlayable.Create(graph, final);
                    for (int i = 0; i < final.layers.Length; i++)
                        if (Constants.IsASSManagedLayerName(final.layers[i].name)) playable.SetLayerWeight(i, 0f);
                    AnimationPlayableOutput.Create(graph, "Final FX", animator).SetSourcePlayable(playable);
                    graph.Play();
                    for (int frame = 0; frame < 4; frame++) graph.Evaluate(1f / 60f);
                    Assert.That(light.intensity, Is.EqualTo(3f).Within(0.001f));
                    Assert.That(audio.volume, Is.EqualTo(0.75f).Within(0.001f));
                }
                finally { graph.Destroy(); }
            }
            finally
            {
                context.DeactivateAllExtensionContexts();
                Processor.ReleaseBuildSnapshot(root);
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                if (generatedFolder != null) AssetDatabase.DeleteAsset(generatedFolder);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
