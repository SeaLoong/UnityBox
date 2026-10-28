using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

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

        private GameObject CreateAvatar(out VRCAvatarDescriptor descriptor, out AnimatorController dormant)
        {
            var root = Track(new GameObject("Viseme NDMF regression"));
            root.AddComponent<Animator>();
            descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = false;
            dormant = Track(new AnimatorController());
            dormant.layers = new[] { new AnimatorControllerLayer
            {
                name = "Dormant source layer", stateMachine = Track(new AnimatorStateMachine()), defaultWeight = 1f,
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
