using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class StandaloneRuntimeTests
    {
        private string folder;
        private GameObject root;
        private GameObject body;
        private Animator animator;
        private Scene scene;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSRuntime_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("ASSRuntime");
            SceneManager.MoveGameObjectToScene(root, scene);
            body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        private static ASSConfigData Config() => new ASSConfigData
        {
            disableObfuscation = true, disableDefense = true, disableOverlay = true,
            disableWarningSound = true, disableRootChildren = true,
            gesturePassword = new List<int> { 1, 2 }, gestureHoldTime = 0.12f,
            gestureMaxHoldTime = 1f, gestureErrorTolerance = 0.2f, countdownDuration = 8f,
        };

        private AnimatorController Build(ASSConfigData config = null, bool local = true)
        {
            var controller = StandaloneSecurityBuilder.Build(root, config ?? Config(), folder + "/Runtime.controller");
            var parameters = controller.parameters;
            foreach (var parameter in parameters)
                if (parameter.name == Constants.PARAM_IS_LOCAL) parameter.defaultBool = local;
            controller.parameters = parameters;
            animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            Advance(2);
            return controller;
        }

        private void Advance(int frames)
        {
            for (int i = 0; i < frames; i++) animator.Update(1f / 60f);
        }

        private void Input(int gesture, int frames)
        {
            animator.SetInteger(Constants.PARAM_GESTURE_LEFT, gesture);
            Advance(frames);
        }

        [Test]
        public void OriginalSceneComponentGuidResolvesToTheRuntimeConfigurationClass()
        {
            string path = AssetDatabase.GUIDToAssetPath("1c51e6a139920bb4ca12700dc82627ff");
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.GetClass(), Is.EqualTo(typeof(ASSComponent)));
        }

        [Test]
        public void GeneratedLayersAreIndependentAndEveryStateBehaviourPersists()
        {
            var controller = Build();
            Assert.That(controller.layers.All(layer => layer.syncedLayerIndex == -1), Is.True);
            var assetPath = AssetDatabase.GetAssetPath(controller);
            foreach (var state in controller.layers.SelectMany(layer => layer.stateMachine.states).Select(item => item.state))
            {
                Assert.That(AssetDatabase.GetAssetPath(state), Is.EqualTo(assetPath), state.name);
                foreach (var behaviour in state.behaviours)
                    Assert.That(AssetDatabase.GetAssetPath(behaviour), Is.EqualTo(assetPath), behaviour.name);
            }
        }

        [Test]
        public void NativeAnimatorRequiresContinuousHoldAndUnlocksAfterTheCompleteSequence()
        {
            Build();
            Advance(5);
            Assert.That(body.transform.localScale.x, Is.Zero.Within(0.001f));
            Input(1, 3);
            Input(0, 4);
            Input(2, 20);
            Assert.That(animator.GetBool(Constants.PARAM_PASSWORD_CORRECT), Is.False);
            Input(1, 20);
            Input(2, 20);
            Assert.That(animator.GetBool(Constants.PARAM_PASSWORD_CORRECT), Is.True);
            Advance(5);
            Assert.That(body.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void NativeAnimatorRearmsReleasedPasswordCountdownAndLockLayers()
        {
            var controller = Build();
            Input(1, 20);
            Input(2, 20);
            Advance(150);
            int passwordLayer = Array.FindIndex(controller.layers, layer => layer.name == Constants.LAYER_PASSWORD_INPUT);
            Assert.That(animator.GetLayerWeight(passwordLayer), Is.Zero);
            animator.SetBool(Constants.PARAM_PASSWORD_CORRECT, false);
            Input(0, 10);
            Assert.That(animator.GetLayerWeight(passwordLayer), Is.EqualTo(1f));
            Assert.That(body.transform.localScale.x, Is.Zero.Within(0.001f));
            Assert.That(animator.GetBool(Constants.PARAM_TIME_UP), Is.False);
            Input(1, 20);
            Input(2, 20);
            Assert.That(animator.GetBool(Constants.PARAM_PASSWORD_CORRECT), Is.True);
            Advance(5);
            Assert.That(body.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CountdownRuntimeWritesTimeoutOnlyForTheLocalAnimator(bool local)
        {
            var config = Config();
            config.countdownDuration = 0.3f;
            Build(config, local);
            Advance(40);
            Assert.That(animator.GetBool(Constants.PARAM_TIME_UP), Is.EqualTo(local));
            Assert.That(animator.GetBool(Constants.PARAM_PASSWORD_CORRECT), Is.False);
        }

        [Test]
        public void TimeoutRestartRearmsTheCompleteControllerAndAcceptsFreshInput()
        {
            var config = Config();
            config.countdownDuration = 0.4f;
            config.gesturePassword = new List<int> { 1 };
            Build(config);
            Advance(40);
            var input = root.GetComponent<StandaloneSecurityInput>();
            Assert.That(input.HasTimedOut, Is.True);
            input.Restart();
            input.SetGesture(1);
            Advance(18);
            Assert.That(input.IsUnlocked, Is.True);
            Assert.That(input.HasTimedOut, Is.False);
        }

        [Test]
        public void UnlockedLayerReleasesScaleToTheUsersAnimation()
        {
            var controller = Build();
            var userClip = new AnimationClip();
            userClip.SetCurve("Body", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0f, 1f, 2f));
            AssetDatabase.AddObjectToAsset(userClip, controller);
            controller.layers[0].stateMachine.states[0].state.motion = userClip;
            AssetDatabase.SaveAssets();
            animator.Rebind();
            Advance(5);
            Assert.That(body.transform.localScale.x, Is.Zero.Within(0.001f));
            Input(1, 20);
            Input(2, 20);
            Advance(15);
            Assert.That(body.transform.localScale.x, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void OverrideControllerSwapsDuplicateNamedClipsByIdentityWithoutEditingTheSource()
        {
            Obfuscator.Initialize(root.name, true, false, false, folder + "/Generated");
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Base.controller");
            var first = new AnimationClip { name = "Duplicate" };
            var second = new AnimationClip { name = "Duplicate" };
            first.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 1f));
            second.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 2f));
            AssetDatabase.CreateAsset(first, folder + "/First.anim");
            AssetDatabase.CreateAsset(second, folder + "/Second.anim");
            source.layers[0].stateMachine.AddState("First").motion = first;
            source.layers[0].stateMachine.AddState("Second").motion = second;
            AssetDatabase.SaveAssets();
            var overridden = new AnimatorOverrideController(source);
            try
            {
                overridden.ApplyOverrides(new List<KeyValuePair<AnimationClip, AnimationClip>>
                {
                    new KeyValuePair<AnimationClip, AnimationClip>(first, second),
                    new KeyValuePair<AnimationClip, AnimationClip>(second, first),
                });
                var copy = Obfuscator.DuplicateAnimatorControllerForBuild((RuntimeAnimatorController)overridden, "Override");
                float Value(string stateName)
                {
                    var clip = (AnimationClip)copy.layers[0].stateMachine.states.Single(item => item.state.name == stateName).state.motion;
                    var binding = AnimationUtility.GetCurveBindings(clip).Single(item => item.propertyName == "m_LocalPosition.x");
                    return AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
                }
                Assert.That(Value("First"), Is.EqualTo(2f));
                Assert.That(Value("Second"), Is.EqualTo(1f));
                Assert.That(source.layers[0].stateMachine.states.Single(item => item.state.name == "First").state.motion, Is.SameAs(first));
                Assert.That(copy.layers[0].stateMachine, Is.Not.SameAs(source.layers[0].stateMachine));
            }
            finally { Object.DestroyImmediate(overridden); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MemoryControllerBuildCopyOwnsItsStatesClipsAndBehaviours(bool persistentClip)
        {
            Obfuscator.Initialize(root.name, true, false, false, folder + "/Generated");
            var source = new AnimatorController { name = "Transient" };
            var machine = new AnimatorStateMachine();
            var state = machine.AddState("User");
            var motion = new AnimationClip();
            motion.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 2f));
            if (persistentClip) AssetDatabase.CreateAsset(motion, folder + "/MemorySource.anim");
            state.motion = motion;
            source.AddParameter("UserBool", AnimatorControllerParameterType.Bool);
            UnityAnimatorBehaviourAdapter.Instance.AddParameterDriverBehaviour(state, "UserBool", 1f);
            source.layers = new[] { new AnimatorControllerLayer { name = "Base", stateMachine = machine, syncedLayerIndex = -1 } };
            try
            {
                var copy = Obfuscator.DuplicateAnimatorControllerForBuild(source, "Memory");
                var copiedState = copy.layers[0].stateMachine.states.Single().state;
                Assert.That(copiedState, Is.Not.SameAs(state));
                Assert.That(copiedState.motion, Is.Not.SameAs(motion));
                Assert.That(AssetDatabase.GetAssetPath(copiedState), Is.EqualTo(AssetDatabase.GetAssetPath(copy)));
                if (persistentClip)
                    Assert.That(AssetDatabase.GetAssetPath(copiedState.motion), Does.StartWith(folder + "/Generated/"));
                else
                    Assert.That(AssetDatabase.GetAssetPath(copiedState.motion), Is.EqualTo(AssetDatabase.GetAssetPath(copy)));
                Assert.That(AssetDatabase.GetAssetPath(copiedState.motion), Is.Not.EqualTo(AssetDatabase.GetAssetPath(motion)));
                var copiedBehaviour = copiedState.behaviours.OfType<AnimatorStateEntryActions>().Single();
                Assert.That(copiedBehaviour, Is.Not.SameAs(state.behaviours.Single()));
                Assert.That(AssetDatabase.GetAssetPath(copiedBehaviour), Is.EqualTo(AssetDatabase.GetAssetPath(copy)));
                copiedBehaviour.assignments[0].value = 0f;
                Assert.That(((AnimatorStateEntryActions)state.behaviours.Single()).assignments[0].value, Is.EqualTo(1f));
                copiedState.name = "Changed";
                Assert.That(state.name, Is.EqualTo("User"));
            }
            finally { DestroyGraph(source); }
        }

        [Serializable]
        private sealed class Benchmark
        {
            public string plugin = "AvatarSecuritySystem";
            public string scenario;
            public int size, samples;
            public double median_ms, p95_ms, min_ms, max_ms;
            public long allocated_bytes_per_op;
            public string correctness;
        }

        [TestCase(16)]
        [TestCase(64)]
        [TestCase(256)]
        [Category("UnityBoxPerformance")]
        public void LockGenerationScalesWithTheAvatarHierarchy(int childCount)
        {
            for (int i = 1; i < childCount; i++)
                new GameObject("Child" + i).transform.SetParent(root.transform, false);
            Obfuscator.Initialize(root.name, true, false, false, folder);
            Constants.ApplyObfuscation();
            var measurements = new List<double>();
            long allocated = 0;
            long allocationProbeBefore = GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[1024];
            bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationProbeBefore;
            GC.KeepAlive(allocationProbe);
            var previousFilter = Debug.unityLogger.filterLogType;
            try
            {
                Debug.unityLogger.filterLogType = LogType.Warning;
                for (int sample = -1; sample < 7; sample++)
                {
                    var controller = new AnimatorController();
                    Utils.EnsureInputParameters(controller);
                    var timer = Stopwatch.StartNew();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    new Lock(controller, root, Config(), UnityAnimatorBehaviourAdapter.Instance).Generate();
                    timer.Stop();
                    if (sample >= 0)
                    {
                        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                        measurements.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    Assert.That(controller.layers[0].syncedLayerIndex, Is.EqualTo(-1));
                    var state = controller.layers[0].stateMachine.states.Single(item => item.state.name == "LockedA").state;
                    Assert.That(AnimationUtility.GetCurveBindings((AnimationClip)state.motion).Length, Is.EqualTo(childCount * 3));
                    DestroyGraph(controller);
                }
            }
            finally { Debug.unityLogger.filterLogType = previousFilter; }
            measurements.Sort();
            Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Benchmark
            {
                scenario = "lock_graph_generation", size = childCount, samples = measurements.Count,
                median_ms = measurements[measurements.Count / 2], p95_ms = measurements[measurements.Count - 1],
                min_ms = measurements[0], max_ms = measurements[measurements.Count - 1],
                allocated_bytes_per_op = allocationCounterAvailable ? allocated / measurements.Count : -1,
                correctness = "independent layer; three scale curves per root child; input hierarchy retained",
            }));
        }

        private static void DestroyGraph(AnimatorController controller)
        {
            var objects = new HashSet<Object> { controller };
            foreach (var layer in controller.layers)
            {
                objects.Add(layer.stateMachine);
                foreach (var child in layer.stateMachine.states)
                {
                    objects.Add(child.state);
                    objects.Add(child.state.motion);
                    foreach (var transition in child.state.transitions) objects.Add(transition);
                    foreach (var behaviour in child.state.behaviours) objects.Add(behaviour);
                }
            }
            foreach (var item in objects)
                if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }

        [Serializable] private sealed class Scenario { public string name; public string[] animator_paths; public Step[] steps; }
        [Serializable] private sealed class Step { public int frames; public float delta_time = 1f / 60f; public InputValue[] inputs = Array.Empty<InputValue>(); public Check[] checks; }
        [Serializable] private sealed class InputValue { public string animator_path, name, type; public float value; }
        [Serializable] private sealed class Check { public string path, property, parameter; public int index; public float expected, tolerance = 0.001f; }

        public static void ConfigurePlayerProbe(GameObject root, string assetFolder)
        {
            const string path = "ASS_StandaloneProbe";
            var avatar = new GameObject(path);
            avatar.transform.SetParent(root.transform, false);
            new GameObject("Body").transform.SetParent(avatar.transform, false);
            var controller = StandaloneSecurityBuilder.Build(avatar, Config(), assetFolder + "/ASS_Standalone.controller");
            avatar.AddComponent<Animator>().runtimeAnimatorController = controller;
            InputValue Gesture(int gesture) => new InputValue { animator_path = path, name = Constants.PARAM_GESTURE_LEFT, type = "int", value = gesture };
            Check Scale(float value) => new Check { path = path + "/Body", property = "localScale.x", expected = value };
            Check Password(bool value) => new Check { path = path, property = "parameter.bool", parameter = Constants.PARAM_PASSWORD_CORRECT, expected = value ? 1 : 0 };
            var scenario = new Scenario
            {
                name = "ASS gesture hold, unlock, released-layer rearm, relock and second unlock",
                animator_paths = new[] { path },
                steps = new[]
                {
                    new Step { frames = 10, inputs = new[] { Gesture(0) }, checks = new[] { Scale(0f), Password(false) } },
                    new Step { frames = 20, inputs = new[] { Gesture(1) }, checks = new[] { Password(false) } },
                    new Step { frames = 20, inputs = new[] { Gesture(2) }, checks = new[] { Password(true), Scale(1f) } },
                    new Step { frames = 150, checks = new[] { Password(true), Scale(1f) } },
                    new Step { frames = 10, inputs = new[] { Gesture(0), new InputValue { animator_path = path, name = Constants.PARAM_PASSWORD_CORRECT, type = "bool", value = 0f } }, checks = new[] { Password(false), Scale(0f) } },
                    new Step { frames = 20, inputs = new[] { Gesture(1) }, checks = new[] { Password(false) } },
                    new Step { frames = 20, inputs = new[] { Gesture(2) }, checks = new[] { Password(true), Scale(1f) } },
                },
            };
            root.SendMessage("AddScenarioJson", JsonUtility.ToJson(scenario), SendMessageOptions.RequireReceiver);
        }
    }
}
