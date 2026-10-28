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
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

using VRC.SDK3.Avatars.Components;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeNdmfRegressionTests
    {
        private static readonly MethodInfo Process = typeof(VisemeBlendshapeOverridePlugin).Assembly.GetType(
            "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideNdmfProcessor", true)
            .GetMethod("ProcessAvatar", BindingFlags.Public | BindingFlags.Static);
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<BuildContext> contexts = new List<BuildContext>();

        [TearDown]
        public void TearDown()
        {
            foreach (var context in contexts) context.DeactivateAllExtensionContexts();
            contexts.Clear();
            foreach (var item in objects.Where(item => item != null)) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void ScheduledPassesDoNotRequireAnimatorServicesBeforeConfigurationChecks()
        {
            var resolverType = typeof(BuildContext).Assembly.GetType("nadena.dev.ndmf.PluginResolver", true);
            var constructor = resolverType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.GetParameters().Length == 3 &&
                    candidate.GetParameters()[0].ParameterType == typeof(IEnumerable<Type>));
            var resolver = constructor.Invoke(new object[] { new[] { typeof(VisemeBlendshapeOverridePlugin) }, null, true });
            var phases = (IEnumerable)resolverType.GetProperty("Passes", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(resolver);
            var count = 0;
            foreach (var phase in phases)
            foreach (var pass in (IEnumerable)phase.GetType().GetField("Item2").GetValue(phase))
            {
                var activations = (IEnumerable<Type>)pass.GetType()
                    .GetProperty("ActivatePlugins", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
                Assert.That(activations.Contains(typeof(AnimatorServicesContext)), Is.False);
                Assert.That(activations.Contains(typeof(VirtualControllerContext)), Is.False);
                count++;
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDisabledConfigurationLeavesInactiveDescriptorUntouched(bool disabled)
        {
            var root = CreateAvatar(out var descriptor, out var dormant);
            if (disabled) root.AddComponent<VisemeBlendshapeOverrideComponent>().enabled = false;
            var context = CreateContext(root);

            Process.Invoke(null, new object[] { context });

            Assert.That(descriptor.customizeAnimationLayers, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(dormant));
            Assert.Throws<Exception>(() => context.Extension<AnimatorServicesContext>());
            Assert.Throws<Exception>(() => context.Extension<VirtualControllerContext>());
        }

        [Test]
        public void GeneratingFxDoesNotActivateDormantControllersAndReleasesOwnedContexts()
        {
            var root = CreateAvatar(out var descriptor, out var dormant);
            AddValidConfiguration(root, descriptor);
            var context = CreateContext(root);

            Process.Invoke(null, new object[] { context });

            var fx = descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            Assert.That(fx, Is.Not.Null);
            Assert.That(fx.layers.Any(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.True);
            Assert.That(fx.layers.Any(layer => layer.name == "Dormant source layer"), Is.False);
            Assert.That(descriptor.baseAnimationLayers.Any(layer => layer.animatorController == dormant), Is.False);
            Assert.That(descriptor.specialAnimationLayers.Any(layer => layer.animatorController == dormant), Is.False);
            Assert.That(dormant.layers[0].name, Is.EqualTo("Dormant source layer"));
            Assert.That(descriptor.customizeAnimationLayers, Is.True);
            Assert.That(root.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.Null);
            Assert.Throws<Exception>(() => context.Extension<AnimatorServicesContext>());
            Assert.Throws<Exception>(() => context.Extension<VirtualControllerContext>());
        }

        [Test]
        public void ExistingAnimatorServicesRemainActiveAfterVisemeGeneration()
        {
            var root = CreateAvatar(out var descriptor, out _);
            descriptor.customizeAnimationLayers = true;
            AddValidConfiguration(root, descriptor);
            var context = CreateContext(root);
            var services = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var controllers = context.Extension<VirtualControllerContext>();

            Process.Invoke(null, new object[] { context });

            Assert.That(context.Extension<AnimatorServicesContext>(), Is.SameAs(services));
            Assert.That(context.Extension<VirtualControllerContext>(), Is.SameAs(controllers));
            Assert.That(root.GetComponent<VisemeBlendshapeOverrideComponent>(), Is.Null);
        }

        [Test]
        public void AutoWriteDefaultsIncludesRelatedGestureController()
        {
            var root = CreateAvatar(out var descriptor, out var related);
            related.name = "vrc_UserGesture";
            descriptor.customizeAnimationLayers = true;
            var motion = Track(new AnimationClip());
            var state = related.layers[0].stateMachine.AddState("Gesture WD Off");
            state.motion = motion;
            state.writeDefaultValues = false;
            related.layers[0].stateMachine.defaultState = state;
            var fx = Track(new AnimatorController { name = "Empty FX" });
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false, animatorController = fx },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Gesture,
                    isDefault = false, animatorController = related },
            };
            AddValidConfiguration(root, descriptor);
            var context = CreateContext(root);

            Process.Invoke(null, new object[] { context });

            var physical = (AnimatorController)descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var layer = physical.layers.Single(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            Assert.That(layer.stateMachine.states.All(child => !child.state.writeDefaultValues), Is.True);
            Assert.That(state.writeDefaultValues, Is.False);
        }

        [Test]
        public void FinalCommitPreservesLaterVirtualEditsAndResolvesRenamedRenderer()
        {
            var root = CreateAvatar(out var descriptor, out var original);
            descriptor.customizeAnimationLayers = true;
            AddValidConfiguration(root, descriptor);
            var renderer = descriptor.VisemeSkinnedMesh;
            var context = CreateContext(root);
            var services = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            Process.Invoke(null, new object[] { context });
            var fx = services.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
            var later = fx.AddLayer(new LayerPriority(100), "Later plugin transform");
            later.DefaultWeight = 1f;
            var clip = VirtualClip.Create("Later plugin transform");
            clip.SetFloatCurve(services.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject), typeof(Transform),
                "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f, 5f));
            later.StateMachine.DefaultState = later.StateMachine.AddState("Later", clip, Vector3.zero);
            renderer.name = "Renamed Face";

            context.DeactivateAllExtensionContexts();

            var physical = (AnimatorController)descriptor.baseAnimationLayers.Single(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(physical.layers.Count(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
            Assert.That(physical.layers.Any(layer => layer.name == "Later plugin transform"), Is.True);
            Assert.That(original.layers.Any(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.False);
            Assert.That(new VisemeBlendshapeOverridePreprocessCallback().OnPreprocessAvatar(root), Is.True);
            Assert.That(descriptor.baseAnimationLayers.Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .animatorController, Is.SameAs(physical));

            var graph = PlayableGraph.Create("Committed viseme controller");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, physical);
                AnimationPlayableOutput.Create(graph, "Committed FX", root.GetComponent<Animator>()).SetSourcePlayable(playable);
                graph.Play();
                foreach (var id in new[] { 1, 0, 1 })
                {
                    playable.SetInteger("Viseme", id);
                    for (var frame = 0; frame < 4; frame++) graph.Evaluate(1f / 60f);
                    Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(id == 1 ? 100f : 0f).Within(0.02f));
                    Assert.That(renderer.transform.localPosition.y, Is.EqualTo(5f).Within(0.02f));
                }
            }
            finally { graph.Destroy(); }
        }

        private GameObject CreateAvatar(out VRCAvatarDescriptor descriptor, out AnimatorController dormant)
        {
            var root = Track(new GameObject("Viseme NDMF regression"));
            root.AddComponent<Animator>();
            descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = false;
            dormant = Track(new AnimatorController());
            dormant.layers = new[] { new AnimatorControllerLayer
            {
                name = "Dormant source layer", stateMachine = Track(new AnimatorStateMachine()), defaultWeight = 1f, syncedLayerIndex = -1,
            } };
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = dormant,
                },
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = false, animatorController = dormant,
                },
            };
            descriptor.specialAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Sitting, isDefault = false, animatorController = dormant,
            } };
            return root;
        }

        private void AddValidConfiguration(GameObject root, VRCAvatarDescriptor descriptor)
        {
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh { vertices = new[] { Vector3.zero } });
            mesh.AddBlendShapeFrame("A", 100f, new[] { Vector3.up }, null, null);
            renderer.sharedMesh = mesh;
            descriptor.VisemeSkinnedMesh = renderer;
            descriptor.VisemeBlendShapes = new string[15];
            descriptor.VisemeBlendShapes[1] = "A";
            var config = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;
        }

        private BuildContext CreateContext(GameObject root)
        {
            var context = new BuildContext(root, null);
            contexts.Add(context);
            return context;
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            objects.Add(value);
            return value;
        }
    }
}
