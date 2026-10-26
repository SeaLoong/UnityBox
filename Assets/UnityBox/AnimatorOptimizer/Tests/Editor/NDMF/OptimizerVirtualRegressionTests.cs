using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerVirtualRegressionTests
    {
        private static readonly Type Processor = typeof(AnimatorOptimizerNdmfPlugin).Assembly.GetType(
            "UnityBox.AnimatorOptimizer.AnimatorOptimizerNdmfProcessor", true);
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in objects.Where(item => item != null)) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
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
                new AnimatorControllerLayer { name = "First", stateMachine = first },
                new AnimatorControllerLayer { name = "Second", stateMachine = second },
            };
            var context = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            var controller = context.Clone(source);
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
                new AnimatorControllerLayer { name = "First", stateMachine = shared },
                new AnimatorControllerLayer { name = "Second", stateMachine = shared },
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
            var method = Processor.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
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
