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
using Debug = UnityEngine.Debug;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemePerformanceTests
    {
        private Scene scene;
        private GameObject root;
        private Mesh mesh;
        private VisemeBlendshapeOverrideComponent config;
        private string path;

        [SetUp] public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Viseme benchmark");
            SceneManager.MoveGameObjectToScene(root, scene);
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { vertices = new[] { Vector3.zero } };
            renderer.sharedMesh = mesh;
            config = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.targetRenderer = renderer;
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Linear;
            path = "Assets/VisemeBenchmark_" + Guid.NewGuid().ToString("N") + ".controller";
        }

        [TearDown] public void TearDown()
        {
            AssetDatabase.DeleteAsset(path);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [TestCase(15)]
        [TestCase(150)]
        [TestCase(1500)]
        [Category("UnityBoxPerformance")]
        public void ResolvePlanWithLargeFaceMesh(int shapes)
        {
            Populate(shapes);
            Measure("Resolve15Mappings", shapes, 21, 25, () =>
            {
                if (!VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan) || plan.ResolvedBindings.Count != 15)
                    throw new InvalidOperationException("The benchmark requires all15 resolved visemes.");
            }, "15 valid names, renderer-relative paths and voice ranges resolved");
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(15)]
        [Category("UnityBoxPerformance")]
        public void BuildAndPersistController(int controlledShapes)
        {
            Populate(controlledShapes);
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            Measure("BuildPersist15VoiceStates", controlledShapes, 7, 1,
                () => VisemeBlendshapeOverrideProcessor.BuildController(controller, plan),
                "15 states,15 transitions,15 voice trees; assets replaced in one controller");
            var layer = controller.layers.Single(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            Assert.That(layer.stateMachine.states, Has.Length.EqualTo(15));
            Assert.That(layer.stateMachine.anyStateTransitions, Has.Length.EqualTo(15));
            Assert.That(layer.syncedLayerIndex, Is.EqualTo(-1));
            Assert.That(controller.layers.Count(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
            var owned = AssetDatabase.LoadAllAssetsAtPath(path);
            Assert.That(owned.OfType<AnimationClip>().Count(), Is.EqualTo(16));
            Assert.That(owned.OfType<BlendTree>().Count(), Is.EqualTo(15));
        }

        private void Populate(int shapes)
        {
            for (var index = 0; index < shapes; index++)
                mesh.AddBlendShapeFrame("Shape" + index, 100f, new[] { Vector3.up }, null, null);
            for (var id = 0; id < 15; id++)
                config.GetBinding((VisemeBlendshapeOverrideComponent.Viseme)id).blendshapeName =
                    "Shape" + (shapes >= 15 ? shapes - 15 + id : id % shapes);
        }

        private static void Measure(string scenario, int size, int samples, int operations, Action action, string correctness)
        {
            for (var warmup = 0; warmup < 2; warmup++) action();
            var elapsed = new double[samples];
            long allocated = 0;
            var counterBefore = GC.GetAllocatedBytesForCurrentThread();
            var counterProbe = new byte[1024];
            var hasAllocationCounter = GC.GetAllocatedBytesForCurrentThread() - counterBefore >= 1024;
            GC.KeepAlive(counterProbe);
            var watch = new Stopwatch();
            for (var sample = 0; sample < samples; sample++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                for (var operation = 0; operation < operations; operation++) action();
                watch.Stop();
                elapsed[sample] = watch.Elapsed.TotalMilliseconds / operations;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(elapsed);
            Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Measurement
            {
                plugin = "VisemeBlendshapeOverride", scenario = scenario, size = size, samples = samples,
                median_ms = elapsed[samples / 2], p95_ms = elapsed[(int)Math.Ceiling(samples * 0.95) - 1],
                min_ms = elapsed[0], max_ms = elapsed[samples - 1],
                allocated_bytes_per_op = hasAllocationCounter ? allocated / (samples * operations) : -1, correctness = correctness,
            }));
        }

        [Serializable] private sealed class Measurement
        {
            public string plugin, scenario, correctness;
            public int size, samples;
            public double median_ms, p95_ms, min_ms, max_ms;
            public long allocated_bytes_per_op;
        }
    }
}
