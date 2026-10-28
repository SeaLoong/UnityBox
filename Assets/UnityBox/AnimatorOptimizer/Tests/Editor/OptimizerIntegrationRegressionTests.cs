using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer.Tests
{
    public sealed class OptimizerIntegrationRegressionTests
    {
        private string folder;
        private string controllerPath;
        private AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/AnimatorOptimizerIntegration_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            controllerPath = folder + "/Test.controller";
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void BooleanStateRegisterRejectsAliasedInputWithoutWritingSubAssets()
        {
            AddFloat("State");
            var count = AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length;
            Assert.Throws<ArgumentException>(() => AnimatorOptimizerAapBuilder.BuildBooleanStateRegisterUpdate(
                controller, "State", new[] { "State" }, 2, 0, TwoStateTable()));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(controllerPath), Has.Length.EqualTo(count));
        }

        [Test]
        public void BooleanStateRegisterRejectsVrchatBuiltinRegisterWithoutWritingSubAssets()
        {
            AddFloat("VelocityX");
            AddFloat("Toggle");
            var count = AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length;
            Assert.Throws<ArgumentException>(() => AnimatorOptimizerAapBuilder.BuildBooleanStateRegisterUpdate(
                controller, "VelocityX", new[] { "Toggle" }, 2, 0, TwoStateTable(), useVrChatParameterSemantics: true));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(controllerPath), Has.Length.EqualTo(count));
        }

        [TestCase(-1f)]
        [TestCase(0.5f)]
        [TestCase(2f)]
        public void StateOutputRejectsInvalidInitialStateWithoutWritingSubAssets(float defaultState)
        {
            AddFloat("State", defaultState);
            var first = new AnimationClip();
            var second = new AnimationClip();
            try
            {
                var count = AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length;
                Assert.Throws<ArgumentException>(() => AnimatorOptimizerAapBuilder.BuildStateOutputTree(
                    controller, "State", new Motion[] { first, second }));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(controllerPath), Has.Length.EqualTo(count));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void IndependentRegisterSupportsLastStateDefaultAndPreservesOutputOrder()
        {
            AddFloat("State", 1f);
            AddFloat("Toggle");
            var update = AnimatorOptimizerAapBuilder.BuildBooleanStateRegisterUpdate(
                controller, "State", new[] { "Toggle" }, 2, 1, TwoStateTable());
            var motions = new Motion[] { new AnimationClip { name = "Off" }, new AnimationClip { name = "On" } };
            foreach (var motion in motions) AssetDatabase.AddObjectToAsset(motion, controller);
            var output = AnimatorOptimizerAapBuilder.BuildStateOutputTree(controller, "State", motions);
            Assert.That(update.children, Has.Length.EqualTo(2));
            Assert.That(output.children[0].motion, Is.SameAs(motions[0]));
            Assert.That(output.children[1].motion, Is.SameAs(motions[1]));
            Assert.That(output.children[1].threshold, Is.EqualTo(1f));
            Assert.That(output.children[1].timeScale, Is.EqualTo(1f));
        }

        private void AddFloat(string name, float value = 0f)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name, type = AnimatorControllerParameterType.Float, defaultFloat = value,
            });
        }

        private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> TwoStateTable()
        {
            return new Dictionary<int, IReadOnlyDictionary<int, int>>
            {
                [0] = new Dictionary<int, int> { [0] = 0, [1] = 1 },
                [1] = new Dictionary<int, int> { [0] = 0, [1] = 1 },
            };
        }
    }
}
