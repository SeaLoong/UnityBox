using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerStandaloneIntegrationTests
    {
        [Test]
        public void CoreEditorAndTestAssembliesDoNotReferenceSdkAssemblies()
        {
            foreach (var assembly in new[] { typeof(AnimatorOptimizerAnalyzer).Assembly, GetType().Assembly })
                Assert.That(assembly.GetReferencedAssemblies().Any(reference =>
                    reference.Name.StartsWith("VRC", StringComparison.Ordinal) ||
                    reference.Name.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)), Is.False,
                    assembly.GetName().Name + " must remain usable without an avatar SDK.");
        }

        [Test]
        public void CapturedConfigurationSurvivesStrippingWithoutRemovingAnimatorOrChildren()
        {
            var root = new GameObject("Root");
            try
            {
                var animator = root.AddComponent<Animator>();
                var child = new GameObject("Configuration");
                child.transform.SetParent(root.transform, false);
                var component = child.AddComponent<AnimatorOptimizerComponent>();
                component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
                component.animator = animator;
                component.generatedLayerName = "Standalone configuration";
                AnimatorOptimizerBuildState.Capture(root, "standalone test");

                AnimatorOptimizerPlatform.StripConfigurationComponents(root);

                Assert.That(root.GetComponentsInChildren<AnimatorOptimizerComponent>(true), Is.Empty);
                Assert.That(root.GetComponent<Animator>(), Is.SameAs(animator));
                Assert.That(root.transform.childCount, Is.EqualTo(1));
                var config = AnimatorOptimizerBuildState.Get(root);
                Assert.That(config.GeneratedLayerName, Is.EqualTo("Standalone configuration"));
                Assert.That(config.ResolveAnimator(root), Is.SameAs(animator));
            }
            finally
            {
                AnimatorOptimizerBuildState.Release(root);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(AnimatorOptimizerComponent.TargetMode.AnimatorComponent, false)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.AnimatorComponent, true)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.ExplicitController, false)]
        [TestCase(AnimatorOptimizerComponent.TargetMode.ExplicitController, true)]
        public void StandaloneAnimatorConfigurationResolvesWithoutAvatarDescriptor(
            AnimatorOptimizerComponent.TargetMode targetMode, bool useVrChatParameterSemantics)
        {
            var root = new GameObject("Standalone");
            try
            {
                var animator = root.AddComponent<Animator>();
                var component = root.AddComponent<AnimatorOptimizerComponent>();
                component.targetMode = targetMode;
                component.useVrChatParameterSemantics = useVrChatParameterSemantics;

                var config = AnimatorOptimizerConfigData.FromComponent(component);

                Assert.That(config.ResolveAnimator(root), Is.SameAs(animator));
                Assert.That(config.CreateOptions().HasKnownPlayableLayer, Is.False);
                Assert.That(config.CreateOptions().UseVrChatParameterSemantics, Is.EqualTo(useVrChatParameterSemantics));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
