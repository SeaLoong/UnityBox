using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
                layer.StateMachine.DefaultState.WriteDefaultValues = true;
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
                layer.StateMachine.DefaultState.WriteDefaultValues = true;
            }));
    }

    public sealed class NdmfFinalizationTests
    {
        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            foreach (var state in machine.states) yield return state.state;
            foreach (var child in machine.stateMachines)
            foreach (var state in AllStates(child.stateMachine)) yield return state;
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ScheduledPhysicalGenerationPreservesVirtualEditsBeforeAndAfterFinalCommit(
            bool disableWarningSound, bool nullNeutralMotion)
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
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var faceRenderer = face.AddComponent<SkinnedMeshRenderer>();
            var faceMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            var deltaVertices = new[] { Vector3.forward * 0.1f, Vector3.zero, Vector3.zero };
            var zero = new Vector3[3];
            faceMesh.AddBlendShapeFrame("Expression", 100f, deltaVertices, zero, zero);
            AssetDatabase.CreateAsset(faceMesh, folder + "/Face.asset");
            faceRenderer.sharedMesh = faceMesh;
            var neutralClip = new AnimationClip { name = "User empty neutral" };
            AssetDatabase.CreateAsset(neutralClip, folder + "/Neutral.anim");
            var expressionClip = new AnimationClip { name = "User expression" };
            expressionClip.SetCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Expression",
                AnimationCurve.Constant(0f, 1f, 85f));
            AssetDatabase.CreateAsset(expressionClip, folder + "/Expression.anim");
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            var original = source.layers[0].stateMachine.AddState("User original");
            original.motion = neutralClip;
            original.writeDefaultValues = true;
            source.layers[0].stateMachine.defaultState = original;
            source.AddLayer("User face");
            var sourceLayers = source.layers;
            sourceLayers[1].defaultWeight = 1f;
            source.layers = sourceLayers;
            var faceMachine = source.layers[1].stateMachine;
            var expression = faceMachine.AddState("Expression");
            expression.writeDefaultValues = true;
            expression.motion = expressionClip;
            var neutral = faceMachine.AddState("Neutral");
            neutral.writeDefaultValues = true;
            neutral.motion = nullNeutralMotion ? null : neutralClip;
            faceMachine.defaultState = neutral;
            AssetDatabase.SaveAssets();
            var sourceBytes = new Dictionary<string, byte[]>();
            foreach (string asset in new[] { "/Source.controller", "/Expression.anim", "/Neutral.anim", "/Face.asset" })
            {
                sourceBytes.Add(folder + asset, File.ReadAllBytes(folder + asset));
                sourceBytes.Add(folder + asset + ".meta", File.ReadAllBytes(folder + asset + ".meta"));
            }
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = source,
            } };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = root.AddComponent<ASSComponent>();
            config.disableDefense = config.disableOverlay = config.disableWarningSound = config.disableObfuscation = true;
            config.disableWarningSound = disableWarningSound;
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
                var feedbackClips = final.GetBehaviours<VRCAnimatorPlayAudio>().SelectMany(item => item.Clips).Distinct().ToArray();
                Assert.That(feedbackClips.Select(clip => clip.name), Is.EquivalentTo(disableWarningSound
                    ? new[] { Constants.AUDIO_PASSWORD_SUCCESS }
                    : new[] { Constants.AUDIO_PASSWORD_SUCCESS, Constants.AUDIO_COUNTDOWN_WARNING }));
                Assert.That(feedbackClips.All(clip => clip.loadInBackground), Is.True);
                var sdkBuilder = Type.GetType("VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder, VRC.SDK3A.Editor", true);
                var sdkScan = sdkBuilder.GetMethod("ScanAvatarForAnimatorPlayAudio", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((List<AudioClip>)sdkScan.Invoke(null, new object[] { descriptor }), Is.Empty);

                Assert.That(final.layers.Count(layer => layer.name == Constants.LAYER_PASSWORD_INPUT), Is.EqualTo(1));
                Assert.That(final.layers.Count(layer => layer.name == Constants.LAYER_LIFECYCLE), Is.EqualTo(1));
                Assert.That(final.layers.Any(layer => layer.name == Constants.LAYER_LOCK), Is.False);
                foreach (var layer in final.layers.Where(layer => Constants.IsASSManagedLayerName(layer.name)))
                foreach (var state in AllStates(layer.stateMachine))
                    Assert.That(state.writeDefaultValues, Is.True, layer.name + "/" + state.name);
                Assert.That(final.layers.Any(layer => layer.name == "Upstream virtual output"), Is.True);
                Assert.That(final.layers.Any(layer => layer.name == "Downstream virtual output"), Is.True);
                Assert.That(root.GetComponent<ASSComponent>(), Is.Null);
                Assert.That(Processor.WasProcessedByNdmf(root), Is.True);
                Assert.That(source.layers.Length, Is.EqualTo(2));
                Assert.That(source.layers[0].stateMachine.states.Single().state.name, Is.EqualTo("User original"));
                Assert.That(AllStates(source.layers[1].stateMachine).All(state => state.writeDefaultValues), Is.True);
                Assert.That(source.layers[1].stateMachine.states.Single(item => item.state.name == "Expression").state.motion,
                    Is.SameAs(expressionClip));
                Assert.That(source.layers[1].stateMachine.states.Single(item => item.state.name == "Neutral").state.motion,
                    Is.EqualTo(nullNeutralMotion ? null : neutralClip));
                Assert.That(source.parameters, Is.Empty);
                AssetDatabase.SaveAssets();
                foreach (var item in sourceBytes)
                    Assert.That(File.ReadAllBytes(item.Key), Is.EqualTo(item.Value), item.Key);

                var graph = PlayableGraph.Create("ASS committed upstream/downstream outputs");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimatorControllerPlayable.Create(graph, final);
                    AnimationPlayableOutput.Create(graph, "Final FX", animator).SetSourcePlayable(playable);
                    graph.Play();
                    for (int frame = 0; frame < 4; frame++) graph.Evaluate(1f / 60f);
                    Assert.That(light.intensity, Is.EqualTo(3f).Within(0.001f));
                    Assert.That(audio.volume, Is.EqualTo(0.75f).Within(0.001f));
                    Assert.That(faceRenderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.001f));
                    int faceLayerIndex = Array.FindIndex(final.layers, layer => layer.name == "User face");
                    Assert.That(faceLayerIndex, Is.GreaterThanOrEqualTo(0));
                    playable.Play(Animator.StringToHash("User face.Expression"), faceLayerIndex, 0f);
                    for (int frame = 0; frame < 8; frame++) graph.Evaluate(1f / 60f);
                    Assert.That(faceRenderer.GetBlendShapeWeight(0), Is.EqualTo(85f).Within(0.001f));
                    playable.Play(Animator.StringToHash("User face.Neutral"), faceLayerIndex, 0f);
                    for (int frame = 0; frame < 8; frame++) graph.Evaluate(1f / 60f);
                    Assert.That(faceRenderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.001f),
                        "ASS WD On states must leave the original WD On face layer able to restore its neutral defaults.");
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
