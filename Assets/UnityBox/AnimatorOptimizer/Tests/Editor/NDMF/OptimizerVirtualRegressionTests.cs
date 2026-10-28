using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerVirtualRegressionTests
    {
        private static readonly Type Processor = typeof(AnimatorOptimizerNdmfPlugin).Assembly.GetType(
            "UnityBox.AnimatorOptimizer.AnimatorOptimizerNdmfProcessor", true);
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<BuildContext> buildContexts = new List<BuildContext>();

        [SetUp]
        public void SetUp() => AnimatorOptimizerBuildState.ReleaseAll();

        [TearDown]
        public void TearDown()
        {
            foreach (var context in buildContexts) context.DeactivateAllExtensionContexts();
            buildContexts.Clear();
            AnimatorOptimizerBuildState.ReleaseAll();
            foreach (var item in objects.Where(item => item != null)) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void ConfiguredPassesDoNotUnconditionallyActivateAnimatorServices()
        {
            // Exercise NDMF's real scheduling metadata: checking Process alone would
            // miss extension activation performed by the scheduler before that callback.
            var resolverType = typeof(BuildContext).Assembly.GetType("nadena.dev.ndmf.PluginResolver", true);
            var constructor = resolverType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.GetParameters().Length == 3 &&
                    candidate.GetParameters()[0].ParameterType == typeof(IEnumerable<Type>));
            var resolver = constructor.Invoke(new object[] { new[] { typeof(AnimatorOptimizerNdmfPlugin) }, null, true });
            var phases = (IEnumerable)resolverType.GetProperty("Passes", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(resolver);
            var count = 0;
            foreach (var phase in phases)
            foreach (var pass in (IEnumerable)phase.GetType().GetField("Item2").GetValue(phase))
            {
                var activations = (IEnumerable<Type>)pass.GetType()
                    .GetProperty("ActivatePlugins", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
                Assert.That(activations, Has.No.Member(typeof(AnimatorServicesContext)));
                Assert.That(activations, Has.No.Member(typeof(VirtualControllerContext)));
                count++;
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingConfigurationDoesNotActivateServicesOrEnablePlayableLayers(bool latePhase)
        {
            var root = Track(new GameObject("No AO configuration"));
            var descriptor = CreateDisabledDescriptor(root, out var inactiveController);
            var context = CreateBuildContext(root);

            Invoke("Process", context, latePhase);

            Assert.That(descriptor.customizeAnimationLayers, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(inactiveController));
            Assert.Throws<Exception>(() => context.Extension<AnimatorServicesContext>());
            Assert.Throws<Exception>(() => context.Extension<VirtualControllerContext>());
            Assert.That(AnimatorOptimizerBuildState.WasNdmfProcessed(root), Is.False);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void InactivePlayableSourceStaysIneligibleAfterLaterCustomization(bool optimizeAll, bool enabledLater)
        {
            var root = Track(new GameObject("Disabled Playable Layers"));
            var descriptor = CreateDisabledDescriptor(root, out var inactiveController);
            var component = root.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer;
            component.optimizeAllConvertibleLayers = optimizeAll;
            component.layerIndices.Add(0);
            AnimatorOptimizerBuildState.Capture(root, "regression");
            var context = CreateBuildContext(root);
            Invoke("CaptureSourceLayerSelection", context);
            // Another plugin or NDMF's VRChat bindings may enable customization later.
            descriptor.customizeAnimationLayers = enabledLater;

            Invoke("Process", context, false);

            Assert.That(descriptor.customizeAnimationLayers, Is.EqualTo(enabledLater));
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(inactiveController));
            Assert.That(inactiveController.layers.Length, Is.EqualTo(1));
            Assert.That(AnimatorOptimizerBuildState.WasNdmfProcessed(root), Is.True,
                "SDK fallback must not reinterpret the disabled source after NDMF changes the descriptor");
            Assert.Throws<Exception>(() => context.Extension<AnimatorServicesContext>());
        }

        [Test]
        public void ChildAnimatorOptimizationRestoresUnrelatedDisabledPlayableLayersAndReleasesOwnedContexts()
        {
            var root = Track(new GameObject("Child Animator target"));
            var descriptor = CreateDisabledDescriptor(root, out var inactiveController);
            var context = CreateAnimatorBuildContext(root);

            Invoke("Process", context, false);

            Assert.That(AnimatorOptimizerBuildState.WasNdmfProcessed(root), Is.True);
            Assert.That(descriptor.customizeAnimationLayers, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].isDefault, Is.False);
            Assert.That(descriptor.baseAnimationLayers[0].animatorController, Is.SameAs(inactiveController));
            Assert.Throws<Exception>(() => context.Extension<AnimatorServicesContext>());
            Assert.Throws<Exception>(() => context.Extension<VirtualControllerContext>());
        }

        [Test]
        public void ExistingAnimatorServicesRemainActiveAfterOptimization()
        {
            var root = Track(new GameObject("Services owned by another plugin"));
            var context = CreateAnimatorBuildContext(root);
            var services = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var controllers = context.Extension<VirtualControllerContext>();

            Invoke("Process", context, false);

            Assert.That(AnimatorOptimizerBuildState.WasNdmfProcessed(root), Is.True);
            Assert.That(context.Extension<AnimatorServicesContext>(), Is.SameAs(services));
            Assert.That(context.Extension<VirtualControllerContext>(), Is.SameAs(controllers));
        }

        private VRCAvatarDescriptor CreateDisabledDescriptor(GameObject root, out AnimatorController controller)
        {
            controller = Track(new AnimatorController());
            controller.layers = new[] { new AnimatorControllerLayer
            {
                name = "Inactive", stateMachine = Track(new AnimatorStateMachine()), defaultWeight = 1f, syncedLayerIndex = -1,
            } };
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = controller,
            } };
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            descriptor.customizeAnimationLayers = false;
            return descriptor;
        }

        private BuildContext CreateAnimatorBuildContext(GameObject root)
        {
            var child = new GameObject("Animator target");
            child.transform.SetParent(root.transform, false);
            var animator = child.AddComponent<Animator>();
            var controller = Track(new AnimatorController());
            controller.layers = new[] { new AnimatorControllerLayer
            {
                name = "Target", stateMachine = Track(new AnimatorStateMachine()), defaultWeight = 1f, syncedLayerIndex = -1,
            } };
            animator.runtimeAnimatorController = controller;
            var component = root.AddComponent<AnimatorOptimizerComponent>();
            component.targetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent;
            component.animator = animator;
            component.optimizeAllConvertibleLayers = false;
            component.removeEffectivelyEmptyLayers = false;
            component.mergeSynchronizedStateMachines = false;
            component.normalizeAlwaysOneParameters = false;
            component.preserveMmdCompatibility = false;
            AnimatorOptimizerBuildState.Capture(root, "regression");
            var context = CreateBuildContext(root);
            Invoke("CaptureSourceLayerSelection", context);
            return context;
        }

        private BuildContext CreateBuildContext(GameObject root)
        {
            var context = new BuildContext(root, null);
            buildContexts.Add(context);
            return context;
        }

        [Test]
        public void AnimatorTargetUsesComponentAsVirtualControllerKey()
        {
            var root = Track(new GameObject("Avatar"));
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = Track(new AnimatorController());
            var config = new AnimatorOptimizerConfigData
            {
                TargetMode = AnimatorOptimizerComponent.TargetMode.AnimatorComponent,
                Animator = animator,
                Controller = animator.runtimeAnimatorController,
            };
            Assert.That(Invoke("ResolveVirtualControllerKey", config, root), Is.SameAs(animator));
        }

        [Test]
        public void DifferentExplicitSourceDoesNotOptimizeAssignedVirtualController()
        {
            var root = Track(new GameObject("Avatar"));
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = Track(new AnimatorController());
            var config = new AnimatorOptimizerConfigData
            {
                TargetMode = AnimatorOptimizerComponent.TargetMode.ExplicitController,
                Animator = animator,
                Controller = Track(new AnimatorController()),
            };
            Assert.That(Invoke("ResolveVirtualControllerKey", config, root), Is.Null);
        }

        [Test]
        public void SourceLayerSelectionSurvivesEarlierInsertedLayers()
        {
            var source = Track(new AnimatorController());
            var first = Track(new AnimatorStateMachine { name = "First" });
            var second = Track(new AnimatorStateMachine { name = "Second" });
            source.layers = new[]
            {
                new AnimatorControllerLayer { name = "First", stateMachine = first, syncedLayerIndex = -1 },
                new AnimatorControllerLayer { name = "Second", stateMachine = second, syncedLayerIndex = -1 },
            };
            Assert.That(source.layers[1].stateMachine, Is.SameAs(second), "fixture must preserve the independent source layer");
            var context = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            var controller = context.Clone(source);
            Assert.That(controller.Layers.All(layer => layer.SyncedLayerIndex < 0), Is.True);
            var expected = controller.Layers.Last();
            controller.AddLayer(new LayerPriority(-1), "InsertedBeforeSource");
            var arguments = new object[] { controller.Layers, new[] { second }, null };
            Assert.That(Invoke("TryResolveRequestedVirtualLayers", arguments), Is.True);
            Assert.That((HashSet<VirtualLayer>)arguments[2], Is.EquivalentTo(new[] { expected }));
            Assert.That(controller.Layers.ToList().IndexOf(expected), Is.EqualTo(2));
        }

        [Test]
        public void AmbiguousSourceLayerSelectionIsRejected()
        {
            var source = Track(new AnimatorController());
            var shared = Track(new AnimatorStateMachine());
            source.layers = new[]
            {
                new AnimatorControllerLayer { name = "First", stateMachine = shared, syncedLayerIndex = -1 },
                new AnimatorControllerLayer { name = "Second", stateMachine = shared, syncedLayerIndex = -1 },
            };
            var controller = new CloneContext(GenericPlatformAnimatorBindings.Instance).Clone(source);
            var arguments = new object[] { controller.Layers, new[] { shared }, null };
            Assert.That(Invoke("TryResolveRequestedVirtualLayers", arguments), Is.False);
        }

        [TestCase("entry")]
        [TestCase("any")]
        [TestCase("state")]
        [TestCase("childExit")]
        [TestCase("childEntry")]
        public void RetainedIntegerEqualityPreventsParameterUpgrade(string location)
        {
            var controller = CreateVirtualController(AnimatorControllerParameterType.Int, out var removed, out var retained);
            AddCondition(retained.StateMachine, location, AnimatorConditionMode.Equals, 1f);
            Assert.That(Upgrade(controller, removed), Is.Null);
            Assert.That(controller.Parameters["Choice"].type, Is.EqualTo(AnimatorControllerParameterType.Int));
        }

        [TestCase(AnimatorConditionMode.Equals, 1f, AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.Equals, 0f, AnimatorConditionMode.Less)]
        [TestCase(AnimatorConditionMode.NotEqual, 0f, AnimatorConditionMode.Greater)]
        [TestCase(AnimatorConditionMode.NotEqual, 1f, AnimatorConditionMode.Less)]
        public void RetainedBooleanEqualityBecomesFloatComparison(
            AnimatorConditionMode mode, float threshold, AnimatorConditionMode expected)
        {
            var controller = CreateVirtualController(AnimatorControllerParameterType.Bool, out var removed, out var retained);
            var transition = AddCondition(retained.StateMachine, "entry", mode, threshold);
            Assert.That(Upgrade(controller, removed), Is.Not.Null);
            Assert.That(controller.Parameters["Choice"].type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(transition.Conditions[0].mode, Is.EqualTo(expected));
            Assert.That(transition.Conditions[0].threshold, Is.EqualTo(0.5f));
        }

        [Test]
        public void RetainedIntegerComparisonAllowsSafeUpgrade()
        {
            var controller = CreateVirtualController(AnimatorControllerParameterType.Int, out var removed, out var retained);
            var transition = AddCondition(retained.StateMachine, "state", AnimatorConditionMode.Greater, 1f);
            Assert.That(Upgrade(controller, removed), Is.Not.Null);
            Assert.That(controller.Parameters["Choice"].type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(transition.Conditions[0].mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(transition.Conditions[0].threshold, Is.EqualTo(1f));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void UndeclaredIsLocalRequiresExplicitVirtualPlatformMode(bool vrchat, bool sameOutput)
        {
            var controller = CreateVirtualIsLocalSelector(sameOutput);
            var layer = controller.Layers.Single();
            var options = new AnimatorOptimizerOptions
            {
                UseVrChatParameterSemantics = vrchat,
                RequireAtLeastTwoLayers = false,
            };
            var arguments = new object[] { controller, layer, options, null, null };

            Assert.That(Invoke("TryBuildPlan", arguments), Is.EqualTo(vrchat));
            Assert.That(controller.Parameters.ContainsKey("IsLocal"), Is.False);
            if (vrchat)
            {
                var plans = Array.CreateInstance(arguments[3].GetType(), 1);
                plans.SetValue(arguments[3], 0);
                Assert.That(Invoke("UpgradeParameters", controller, plans, options,
                    new HashSet<VirtualLayer> { layer }), Is.Empty);
                Assert.That(controller.Parameters.ContainsKey("IsLocal"), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclaredIsLocalUsesVirtualTargetModeForUpgradeAndRetainedConditions(bool vrchat)
        {
            var controller = CreateVirtualIsLocalSelector(false);
            controller.Parameters = controller.Parameters.Add("IsLocal", new AnimatorControllerParameter
            {
                name = "IsLocal", type = AnimatorControllerParameterType.Bool, defaultBool = true,
            });
            var converted = controller.Layers.Single();
            var retained = controller.AddLayer(LayerPriority.Default, "Retained");
            var transition = AddCondition(retained.StateMachine, "entry", AnimatorConditionMode.If, 0f);
            transition.Conditions = ImmutableList.Create(new AnimatorCondition
            {
                parameter = "IsLocal", mode = AnimatorConditionMode.If,
            });
            var options = new AnimatorOptimizerOptions
            {
                UseVrChatParameterSemantics = vrchat,
                RequireAtLeastTwoLayers = false,
                UpgradeBoolParametersToFloat = true,
            };
            var arguments = new object[] { controller, converted, options, null, null };
            Assert.That(Invoke("TryBuildPlan", arguments), Is.True);
            var plans = Array.CreateInstance(arguments[3].GetType(), 1);
            plans.SetValue(arguments[3], 0);

            var upgraded = (List<string>)Invoke("UpgradeParameters", controller, plans, options,
                new HashSet<VirtualLayer> { converted });

            Assert.That(upgraded, vrchat ? Is.Empty : Is.EquivalentTo(new[] { "IsLocal" }));
            Assert.That(controller.Parameters["IsLocal"].type, Is.EqualTo(vrchat
                ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float));
            Assert.That(transition.Conditions.Single().mode, Is.EqualTo(vrchat
                ? AnimatorConditionMode.If : AnimatorConditionMode.Greater));
            Assert.That(transition.Conditions.Single().threshold, Is.EqualTo(vrchat ? 0f : 0.5f));
            if (!vrchat) Assert.That(controller.Parameters["IsLocal"].defaultFloat, Is.EqualTo(1f));
        }

        [Test]
        public void VirtualClipMetadataPreservesSourceIdentityAndInspectableBindings()
        {
            var source = Track(new AnimationClip { name = "Ordinary property animation" });
            source.SetCurve("Output", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, 1f));
            var clip = new CloneContext(GenericPlatformAnimatorBindings.Instance).Clone(source);
            var method = Processor.GetMethod("GetVirtualOriginalObject", BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(AnimationClip));

            Assert.That(method.Invoke(null, new object[] { clip }), Is.SameAs(source));
            Assert.That(Invoke("IsOrdinaryVirtualClip", clip), Is.True);
            Assert.That(Invoke("HasInspectableVirtualBindings", clip), Is.True);
            Assert.That(clip.GetFloatCurveBindings().Single().propertyName, Is.EqualTo("m_IsActive"));
        }

        [Test]
        public void VirtualOriginMetadataDistinguishesUnavailableFromGenerated()
        {
            var source = Track(new AnimationClip());
            var clip = new CloneContext(GenericPlatformAnimatorBindings.Instance).Clone(source);
            var method = Processor.GetMethod("TryReadVirtualOriginalObject", BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(AnimationClip));
            foreach (var property in new[]
            {
                null, typeof(AnimatorState).GetProperty("motion"), typeof(VirtualNode).GetProperty("Name"),
            })
            {
                var arguments = new object[] { clip, property, source };
                Assert.That(method.Invoke(null, arguments), Is.False);
                Assert.That(arguments[2], Is.Null, "missing/incompatible source metadata must not guess the original clip");
            }

            var context = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            var sourceMachine = Track(new AnimatorStateMachine());
            var originalProperty = typeof(VirtualNode).GetProperty("OriginalObject", BindingFlags.Instance | BindingFlags.NonPublic);
            var wrongTypeArguments = new object[] { context.Clone(sourceMachine), originalProperty, source };
            Assert.That(method.Invoke(null, wrongTypeArguments), Is.False);
            Assert.That(wrongTypeArguments[2], Is.Null, "an original state machine cannot be read as a clip");

            var generated = VirtualStateMachine.Create(context, "Generated without a physical source");
            var generatedReader = Processor.GetMethod("TryGetVirtualOriginalObject", BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(AnimatorStateMachine));
            var generatedArguments = new object[] { generated, sourceMachine };
            Assert.That(generatedReader.Invoke(null, generatedArguments), Is.True);
            Assert.That(generatedArguments[1], Is.Null, "available Object metadata can legitimately have no physical source");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UncoveredFloatValuePreservesVirtualStateHistory(bool allowApproximation)
        {
            var controller = CreateVirtualSelector("Amount", false,
                AnimatorConditionMode.Less, AnimatorConditionMode.Greater, 0.5f);
            controller.Parameters = controller.Parameters.Add("Amount", new AnimatorControllerParameter
            {
                name = "Amount", type = AnimatorControllerParameterType.Float,
            });
            var layer = controller.Layers.Single();
            var stateMachine = layer.StateMachine;
            var transitions = stateMachine.AnyStateTransitions;
            var options = new AnimatorOptimizerOptions
            {
                RequireAtLeastTwoLayers = false,
                AllowFloatConditionApproximation = allowApproximation,
            };
            var arguments = new object[] { controller, layer, options, null, null };

            Assert.That(Invoke("TryBuildPlan", arguments), Is.False,
                "Amount == 0.5 can retain either state indefinitely; approximation cannot choose its output");
            Assert.That(arguments[3], Is.Null);
            Assert.That(layer.StateMachine, Is.SameAs(stateMachine));
            Assert.That(stateMachine.AnyStateTransitions, Is.SameAs(transitions));
            Assert.That(controller.Parameters["Amount"].type, Is.EqualTo(AnimatorControllerParameterType.Float));
        }

        private VirtualAnimatorController CreateVirtualIsLocalSelector(bool sameOutput) =>
            CreateVirtualSelector("IsLocal", sameOutput, AnimatorConditionMode.IfNot, AnimatorConditionMode.If, 0f);

        private VirtualAnimatorController CreateVirtualSelector(
            string parameter, bool sameOutput, AnimatorConditionMode offCondition, AnimatorConditionMode onCondition,
            float threshold)
        {
            var source = Track(new AnimatorController());
            var machine = Track(new AnimatorStateMachine());
            var off = Track(machine.AddState("Off"));
            var on = Track(machine.AddState("On"));
            foreach (var state in new[] { off, on })
            {
                var clip = Track(new AnimationClip());
                clip.SetCurve("Output", typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 1f / 60f, state == off || sameOutput ? 0f : 1f));
                state.motion = clip;
                state.writeDefaultValues = true;
                var transition = Track(machine.AddAnyStateTransition(state));
                transition.duration = 0f;
                transition.hasExitTime = false;
                transition.canTransitionToSelf = true;
                transition.AddCondition(state == off ? offCondition : onCondition, threshold, parameter);
            }
            machine.defaultState = off;
            source.layers = new[]
            {
                new AnimatorControllerLayer { name = "Selector", stateMachine = machine, defaultWeight = 1f, syncedLayerIndex = -1 },
            };
            var controller = new CloneContext(GenericPlatformAnimatorBindings.Instance).Clone(source);
            Assert.That(controller.Layers.Single().SyncedLayerIndex, Is.LessThan(0), "fixture layer must be independent");
            return controller;
        }

        private static VirtualAnimatorController CreateVirtualController(
            AnimatorControllerParameterType type, out VirtualLayer removed, out VirtualLayer retained)
        {
            var context = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            var controller = VirtualAnimatorController.Create(context);
            controller.Parameters = controller.Parameters.Add("Choice", new AnimatorControllerParameter
            {
                name = "Choice", type = type,
            });
            removed = controller.AddLayer(LayerPriority.Default, "Converted");
            retained = controller.AddLayer(LayerPriority.Default, "Retained");
            return controller;
        }

        private static object Upgrade(VirtualAnimatorController controller, VirtualLayer removed)
        {
            var planType = Processor.GetNestedType("VirtualPlan", BindingFlags.NonPublic);
            Assert.That(planType, Is.Not.Null);
            var plan = Activator.CreateInstance(planType, true);
            planType.GetField("ReferencedParameters").SetValue(plan, new HashSet<string> { "Choice" });
            var plans = Array.CreateInstance(planType, 1);
            plans.SetValue(plan, 0);
            return Invoke("UpgradeParameters", controller, plans,
                new AnimatorOptimizerOptions { UpgradeBoolParametersToFloat = true, UpgradeIntParametersToFloat = true },
                new HashSet<VirtualLayer> { removed });
        }

        private static VirtualTransitionBase AddCondition(
            VirtualStateMachine machine, string location, AnimatorConditionMode mode, float threshold)
        {
            VirtualTransitionBase transition;
            if (location == "any" || location == "state")
            {
                var stateTransition = VirtualStateTransition.Create();
                if (location == "any") machine.AnyStateTransitions = machine.AnyStateTransitions.Add(stateTransition);
                else
                {
                    var state = machine.AddState("Waiting");
                    state.Transitions = state.Transitions.Add(stateTransition);
                }
                transition = stateTransition;
            }
            else
            {
                var entry = VirtualTransition.Create();
                transition = entry;
                if (location == "entry") machine.EntryTransitions = machine.EntryTransitions.Add(entry);
                else
                {
                    var context = new CloneContext(GenericPlatformAnimatorBindings.Instance);
                    var child = VirtualStateMachine.Create(context, "Child");
                    machine.StateMachines = machine.StateMachines.Add(new VirtualStateMachine.VirtualChildStateMachine
                    {
                        StateMachine = child,
                    });
                    if (location == "childEntry") child.EntryTransitions = child.EntryTransitions.Add(entry);
                    else machine.StateMachineTransitions = machine.StateMachineTransitions.Add(child, ImmutableList.Create(entry));
                }
            }
            transition.Conditions = ImmutableList.Create(new AnimatorCondition
            {
                parameter = "Choice", mode = mode, threshold = threshold,
            });
            return transition;
        }

        private static object Invoke(string name, params object[] arguments)
        {
            var method = Processor.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(null, arguments);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            objects.Add(value);
            return value;
        }
    }
}
