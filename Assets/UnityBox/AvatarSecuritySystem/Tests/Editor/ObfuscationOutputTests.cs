using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class ObfuscationOutputTests
    {
        private string folder;
        private Scene scene;
        private GameObject root;
        private Light light;
        private Animator animator;
        private AnimatorController source;
        private Dictionary<string, byte[]> sourceBytes;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ASSObfuscationOutput_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Restricted metadata output");
            SceneManager.MoveGameObjectToScene(root, scene);
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            light = body.AddComponent<Light>(); light.intensity = 0.25f;
            var clip = new AnimationClip { name = "Original user light motion" };
            clip.SetCurve("Body", typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 2.5f));
            AssetDatabase.CreateAsset(clip, folder + "/User.anim");
            source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/User.controller");
            var state = source.layers[0].stateMachine.AddState("Original user state");
            state.motion = clip; state.writeDefaultValues = false;
            source.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();
            sourceBytes = new[] { "/User.anim", "/User.anim.meta", "/User.controller", "/User.controller.meta" }
                .ToDictionary(path => folder + path, path => File.ReadAllBytes(folder + path));
            animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = source;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var item in machine.states) yield return item.state;
            foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void StandaloneOptionsProducePersistedSelectedDecoysWithoutChangingTheUserGraph(bool layers, bool states)
        {
            var config = new ASSConfigData
            {
                disableObfuscation = false, enableDecoyLayers = layers, enableDecoyStates = states,
                disableDefense = true, disableOverlay = true, disableWarningSound = true,
                gesturePassword = new List<int> { 1, 2 }, countdownDuration = 20f,
            };
            string path = folder + "/Combined.controller";
            var output = StandaloneSecurityBuilder.Build(root, config, path);
            string passwordLayer = Constants.LAYER_PASSWORD_INPUT;
            var clipNames = new HashSet<string>(Obfuscator.GetInstructionalClipNames(100));
            var stateNames = new HashSet<string>(Obfuscator.GetInstructionalStateNames(100));
            string decoyLayer = Obfuscator.GetDecoyLayer()?.layerName;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            output = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(clip => clipNames.Contains(clip.name)).ToArray();
            Assert.That(clips.Length, Is.EqualTo(layers || states ? 5 : 0));
            Assert.That(output.layers.Any(layer => layer.name == decoyLayer), Is.EqualTo(layers));
            var password = output.layers.Single(layer => layer.name == passwordLayer);
            Assert.That(States(password.stateMachine).Any(state => stateNames.Contains(state.name)), Is.EqualTo(states));
            if (layers)
                Assert.That(States(output.layers.Single(layer => layer.name == decoyLayer).stateMachine)
                    .Any(state => stateNames.Contains(state.name)), Is.True);
            foreach (var clip in clips)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.That(bindings, Is.Not.Empty);
                foreach (var binding in bindings)
                {
                    Assert.That(binding.path, Does.StartWith("__ASS_Metadata_"));
                    Assert.That(root.transform.Find(binding.path), Is.Null, "Metadata cannot animate a real Avatar object.");
                    Assert.That(binding.type, Is.EqualTo(typeof(GameObject)));
                    Assert.That(binding.propertyName, Is.EqualTo("m_IsActive"));
                }
            }
            var parameters = output.parameters.ToDictionary(parameter => parameter.name, parameter => parameter.type);
            foreach (var layer in output.layers.Skip(1))
            foreach (var state in States(layer.stateMachine))
            {
                Assert.That(state.writeDefaultValues, Is.True, state.name);
                foreach (var transition in state.transitions)
                foreach (var condition in transition.conditions)
                {
                    Assert.That(parameters.ContainsKey(condition.parameter), Is.True, condition.parameter);
                    if (condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot)
                        Assert.That(parameters[condition.parameter], Is.EqualTo(AnimatorControllerParameterType.Bool));
                }
            }
            animator.runtimeAnimatorController = output;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            Assert.That(light.intensity, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(output.layers[0].stateMachine.states.Single().state.name, Is.EqualTo("Original user state"));
            Assert.That(output.layers[0].stateMachine.states.Single().state.writeDefaultValues, Is.False);
            foreach (var file in sourceBytes) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void RebuildingAnAssignedOutputReplacesOwnedDecoysAndPreservesAnUntaggedUserLayer(
            bool decoysEnabled, bool disableObfuscation)
        {
            Obfuscator.Initialize(root.name, false, true, true, folder);
            string sharedName = Obfuscator.GetDecoyLayer().layerName;
            var userLayers = source.layers;
            userLayers[0].name = sharedName;
            source.layers = userLayers;
            AssetDatabase.SaveAssets();
            var originalUserBytes = sourceBytes.Keys.ToDictionary(path => path, File.ReadAllBytes);
            Assert.That(ObfuscationGraph.IsGeneratedDecoyLayer(source.layers[0]), Is.False);
            var first = StandaloneSecurityBuilder.Build(root, new ASSConfigData
            {
                disableObfuscation = false, enableDecoyLayers = true, enableDecoyStates = true,
                disableDefense = true, disableOverlay = true, disableWarningSound = true,
                gesturePassword = new List<int> { 1, 2 },
            }, folder + "/FirstAssigned.controller");
            string firstPath = AssetDatabase.GetAssetPath(first);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(firstPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            first = AssetDatabase.LoadAssetAtPath<AnimatorController>(firstPath);
            Assert.That(first.layers.Count(ObfuscationGraph.IsGeneratedDecoyLayer), Is.EqualTo(1));
            Assert.That(first.layers[0].name, Is.EqualTo(sharedName));
            Assert.That(ObfuscationGraph.IsGeneratedDecoyLayer(first.layers[0]), Is.False);
            var assignedBytes = new[] { firstPath, firstPath + ".meta" }.ToDictionary(path => path, File.ReadAllBytes);
            animator.runtimeAnimatorController = first;
            var rebuilt = StandaloneSecurityBuilder.Build(root, new ASSConfigData
            {
                disableObfuscation = disableObfuscation,
                enableDecoyLayers = decoysEnabled, enableDecoyStates = decoysEnabled,
                disableDefense = true, disableOverlay = true, disableWarningSound = true,
                gesturePassword = new List<int> { 1, 2 },
            }, folder + "/Rebuilt.controller");
            AssetDatabase.SaveAssets();
            string rebuiltPath = AssetDatabase.GetAssetPath(rebuilt);
            AssetDatabase.ImportAsset(rebuiltPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            rebuilt = AssetDatabase.LoadAssetAtPath<AnimatorController>(rebuiltPath);
            Assert.That(rebuilt.layers.Count(ObfuscationGraph.IsGeneratedDecoyLayer),
                Is.EqualTo(decoysEnabled && !disableObfuscation ? 1 : 0));
            Assert.That(rebuilt.layers[0].name, Is.EqualTo(sharedName));
            Assert.That(ObfuscationGraph.IsGeneratedDecoyLayer(rebuilt.layers[0]), Is.False);
            Assert.That(rebuilt.layers[0].stateMachine.states.Single().state.name, Is.EqualTo("Original user state"));
            Assert.That(rebuilt.layers[0].stateMachine.states.Single().state.writeDefaultValues, Is.False);
            animator.runtimeAnimatorController = rebuilt;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            Assert.That(light.intensity, Is.EqualTo(2.5f).Within(0.001f));
            foreach (var file in originalUserBytes) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
            foreach (var file in assignedBytes) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        }

        [Serializable]
        private sealed class ObfuscationMeasurement
        {
            public string plugin = "AvatarSecuritySystem";
            public string scenario;
            public int size, samples = 9, warmups = 30, animator_layers, state_count, updates_per_sample = 300;
            public double median_ms, p95_ms, min_ms, max_ms;
            public long allocated_bytes_per_op = -1;
            public string correctness = "Remote idle states remain active; user Light intensity stays 2.5; source assets unchanged";
        }

        [Test, Category("UnityBoxPerformance")]
        public void MeasureNativeAnimatorIdleCostWithAndWithoutGeneratedDecoys()
        {
            foreach (bool enabled in new[] { false, true })
            {
                var target = new GameObject("Measured standalone graph");
                SceneManager.MoveGameObjectToScene(target, scene);
                try
                {
                    var body = new GameObject("Body"); body.transform.SetParent(target.transform, false);
                    var targetLight = body.AddComponent<Light>();
                    var targetAnimator = target.AddComponent<Animator>();
                    targetAnimator.runtimeAnimatorController = source;
                    var output = StandaloneSecurityBuilder.Build(target, new ASSConfigData
                    {
                        disableObfuscation = !enabled, enableDecoyLayers = enabled, enableDecoyStates = enabled,
                        disableDefense = true, disableOverlay = true, disableWarningSound = true,
                        gesturePassword = new List<int> { 1, 2 }, countdownDuration = 20f,
                    }, folder + (enabled ? "/MeasuredDecoys.controller" : "/MeasuredPlain.controller"));
                    targetAnimator.runtimeAnimatorController = output;
                    targetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    targetAnimator.Rebind();
                    targetAnimator.SetBool(Constants.PARAM_IS_LOCAL, false);
                    for (int frame = 0; frame < 30; frame++) targetAnimator.Update(1f / 60f);
                    Assert.That(targetLight.intensity, Is.EqualTo(2.5f).Within(0.001f));
                    var samples = new List<double>();
                    for (int sample = 0; sample < 9; sample++)
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        for (int frame = 0; frame < 300; frame++) targetAnimator.Update(1f / 60f);
                        timer.Stop();
                        samples.Add(timer.Elapsed.TotalMilliseconds / 300d);
                        Assert.That(targetLight.intensity, Is.EqualTo(2.5f).Within(0.001f));
                        Assert.That(targetAnimator.GetBool(Constants.PARAM_IS_LOCAL), Is.False);
                        for (int index = 0; index < output.layers.Length; index++)
                        {
                            var layer = output.layers[index];
                            int expected = Animator.StringToHash(layer.name + "." + layer.stateMachine.defaultState.name);
                            Assert.That(targetAnimator.GetCurrentAnimatorStateInfo(index).fullPathHash, Is.EqualTo(expected),
                                "The fixed remote workload must not enter any fake or functional input state.");
                        }
                    }
                    samples.Sort();
                    Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new ObfuscationMeasurement
                    {
                        scenario = enabled ? "animator_idle_with_decoys" : "animator_idle_without_decoys",
                        size = output.layers.SelectMany(layer => States(layer.stateMachine)).Count(),
                        animator_layers = output.layers.Length,
                        state_count = output.layers.SelectMany(layer => States(layer.stateMachine)).Count(),
                        median_ms = samples[4], p95_ms = samples[8], min_ms = samples[0], max_ms = samples[8],
                    }));
                    foreach (var file in sourceBytes)
                        Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
                }
                finally { Object.DestroyImmediate(target); }
            }
        }

        [Test]
        public void DisablingObfuscationSuppressesBothDecoyOptionsAndPreservesCleanNames()
        {
            var output = StandaloneSecurityBuilder.Build(root, new ASSConfigData
            {
                disableObfuscation = true, enableDecoyLayers = true, enableDecoyStates = true,
                disableDefense = true, disableOverlay = true, disableWarningSound = true,
                gesturePassword = new List<int> { 1 },
            }, folder + "/Clean.controller");
            Assert.That(output.layers.Any(layer => layer.name == "ASS_PasswordInput"), Is.True);
            Assert.That(output.layers.Select(layer => layer.name), Is.EqualTo(new[]
            {
                source.layers[0].name, Constants.LAYER_PASSWORD_INPUT, Constants.LAYER_COUNTDOWN, Constants.LAYER_LIFECYCLE,
            }));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(output)).OfType<AnimationClip>()
                .SelectMany(AnimationUtility.GetCurveBindings).Any(binding => binding.path.StartsWith("__ASS_Metadata_", StringComparison.Ordinal)), Is.False);
            foreach (var file in sourceBytes) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        }
    }
}
