using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class GestureTimingTests
    {
        private string folder;
        private Scene scene;
        private GameObject root;
        private Animator animator;
        private float delta;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSGestureTiming_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Gesture timing");
            SceneManager.MoveGameObjectToScene(root, scene);
            delta = 1f / 60f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        private static ASSConfigData Config(params int[] password) => new ASSConfigData
        {
            gesturePassword = new List<int>(password),
            gestureHoldTime = 0.2f,
            gestureErrorTolerance = 0.08f,
            gestureMaxHoldTime = 1f,
            countdownDuration = 300f,
            disableObfuscation = true,
            disableDefense = true,
            disableOverlay = true,
            disableWarningSound = true,
        };

        private AnimatorController Build(ASSConfigData config, int frameRate = 60)
        {
            delta = 1f / frameRate;
            var controller = StandaloneSecurityBuilder.Build(root, config, folder + "/Timing.controller");
            animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            Tick(3);
            return controller;
        }

        private void Tick(int frames)
        {
            for (int i = 0; i < frames; i++) animator.Update(delta);
        }

        private void Input(int gesture, float seconds)
        {
            animator.SetInteger(Constants.PARAM_GESTURE_LEFT, gesture);
            Tick(Mathf.CeilToInt(seconds / delta));
        }

        private bool Accepted => animator.GetBool(Constants.PARAM_PASSWORD_CORRECT);

        [TestCase(60)]
        [TestCase(90)]
        [TestCase(120)]
        public void CyclingEveryGestureEveryFrameCannotAccumulateAValidHold(int frameRate)
        {
            var authoring = root.AddComponent<ASSComponent>();
            var config = ASSConfigData.FromComponent(authoring);
            config.disableObfuscation = config.disableDefense = config.disableOverlay = config.disableWarningSound = true;
            config.countdownDuration = 300f;
            Build(config, frameRate);
            for (int frame = 0; frame < 4096; frame++)
            {
                animator.SetInteger(Constants.PARAM_GESTURE_LEFT, frame % 8);
                animator.Update(delta);
                Assert.That(Accepted, Is.False, "A single sampled pose must never count as a held entry, frame " + frame);
            }
            foreach (int gesture in config.gesturePassword) Input(gesture, config.gestureHoldTime + 0.1f);
            Assert.That(Accepted, Is.True, "The legitimate continuous sequence still works after rapid cycling.");
        }

        [TestCase(60)]
        [TestCase(90)]
        [TestCase(120)]
        public void AShortWrongPoseRestartsTheCurrentHoldInsteadOfAccumulatingItsPreviousTime(int frameRate)
        {
            Build(Config(1, 2), frameRate);
            Input(1, 0.3f);
            Input(2, 0.12f);
            Input(0, delta * 2);
            Input(2, 0.12f);
            Assert.That(Accepted, Is.False, "The two partial holds may not be added across a wrong pose.");
            Input(2, 0.2f);
            Assert.That(Accepted, Is.True, "A fresh uninterrupted hold completes the retained sequence step.");
        }

        [Test]
        public void WrongInputBeyondToleranceClearsEarlierCompletedEntries()
        {
            Build(Config(1, 2));
            Input(1, 0.3f);
            Input(0, 0.2f);
            Input(2, 0.3f);
            Assert.That(Accepted, Is.False);
            Input(1, 0.3f);
            Input(2, 0.3f);
            Assert.That(Accepted, Is.True);
        }

        [TestCase(0.08f)]
        [TestCase(0f)]
        public void AdjacentRepeatedGesturesRequireAReleaseAndASecondContinuousHold(float tolerance)
        {
            var config = Config(1, 1);
            config.gestureErrorTolerance = tolerance;
            Build(config);
            Input(1, 0.65f);
            Assert.That(Accepted, Is.False, "One long pose is one entry, even when adjacent password entries are identical.");
            Input(0, delta * 2);
            Input(1, 0.3f);
            Assert.That(Accepted, Is.True);
        }

        [Test]
        public void RestartDuringAHoldDiscardsItsElapsedTime()
        {
            Build(Config(1));
            Input(1, 0.12f);
            root.GetComponent<StandaloneSecurityInput>().Restart();
            Tick(3);
            Input(1, 0.1f);
            Assert.That(Accepted, Is.False);
            Input(1, 0.2f);
            Assert.That(Accepted, Is.True);
        }

        [Test]
        public void ExpiredToleranceCannotBeReopenedByTheNextCorrectPose()
        {
            var controller = Build(Config(1, 2));
            int layer = Array.FindIndex(controller.layers, item => item.name == Constants.LAYER_PASSWORD_INPUT);
            Input(1, 0.3f);
            Input(0, delta * 2);
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName("Step_1_ErrorTolerance"), Is.True,
                "The real input path must enter the tolerance window before measuring its expiry.");
            animator.Update(0.12f);
            Tick(2);
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName("InputWaiting"), Is.True,
                "Crossing the deadline resets the earlier entry before sampling the next correct pose.");
            Input(2, 0.3f);
            Assert.That(Accepted, Is.False, "A correct pose sampled after expiry starts no next entry.");
        }

        [Test]
        public void ImportPreservesStoredTimesAndBuildCopiesEnforceTheTimingContract()
        {
            var original = root.AddComponent<ASSComponent>();
            Assert.That(original.gestureHoldTime, Is.EqualTo(0.5f));
            Assert.That(original.gestureErrorTolerance, Is.EqualTo(0.3f));
            original.writeDefaultsMode = ASSComponent.WriteDefaultsMode.Off;
            string path = folder + "/LegacyTiming.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            string yaml = System.IO.File.ReadAllText(path);
            foreach (var replacement in new[] {
                new KeyValuePair<string, string>("gestureHoldTime", "0.15"),
                new KeyValuePair<string, string>("gestureErrorTolerance", "0.3"),
                new KeyValuePair<string, string>("gestureMaxHoldTime", "0.1") })
                yaml = System.Text.RegularExpressions.Regex.Replace(yaml,
                    @"(?m)^  " + replacement.Key + @":[^\r\n]*", "  " + replacement.Key + ": " + replacement.Value);
            System.IO.File.WriteAllText(path, yaml);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var component = loaded.GetComponent<ASSComponent>();
                var captured = ASSConfigData.FromComponent(component);
                var input = Config(1);
                input.gestureHoldTime = 0.15f;
                input.gestureErrorTolerance = 0.3f;
                input.gestureMaxHoldTime = 0.1f;
                var normalized = input.Clone();
                Assert.That(component.gestureHoldTime, Is.EqualTo(0.15f));
                Assert.That(component.gestureErrorTolerance, Is.EqualTo(0.3f));
                Assert.That(component.gestureMaxHoldTime, Is.EqualTo(0.1f));
                Assert.That(captured.gestureErrorTolerance, Is.LessThan(captured.gestureHoldTime));
                Assert.That(captured.gestureHoldTime, Is.EqualTo(normalized.gestureHoldTime));
                Assert.That(captured.gestureErrorTolerance, Is.EqualTo(normalized.gestureErrorTolerance));
                Assert.That(captured.gestureMaxHoldTime, Is.EqualTo(normalized.gestureMaxHoldTime));
                Assert.That(input.gestureErrorTolerance, Is.EqualTo(0.3f), "Building a normalized copy does not rewrite caller configuration.");
                Assert.That(component.writeDefaultsMode, Is.EqualTo(ASSComponent.WriteDefaultsMode.Off), "The retired option is retained as data and never controls new states.");
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }

        [Test]
        public void InvalidLegacyToleranceCannotTurnRepeatedPartialHoldsIntoOneAcceptedEntry()
        {
            var config = Config(1);
            config.gestureHoldTime = 0.12f;
            config.gestureErrorTolerance = 0.3f;
            var controller = Build(config);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Input(1, 0.05f);
                Input(0, delta * 2);
                Assert.That(Accepted, Is.False);
            }
            Input(1, 0.22f);
            Assert.That(Accepted, Is.True);
            Assert.That(controller.layers.SelectMany(layer => layer.stateMachine.states)
                .All(item => !item.state.writeDefaultValues), Is.True);
        }
    }
}
