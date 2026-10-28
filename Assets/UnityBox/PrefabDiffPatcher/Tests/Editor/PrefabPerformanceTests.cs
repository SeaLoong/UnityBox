using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.PrefabDiffPatcher.Tests
{
    public class PrefabPerformanceTests
    {
        [TestCase(32)]
        [TestCase(256)]
        [Category("UnityBoxPerformance")]
        public void AnalyzeHierarchy(int count)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var a = new GameObject("A"); var b = new GameObject("B");
            SceneManager.MoveGameObjectToScene(a, scene); SceneManager.MoveGameObjectToScene(b, scene);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var left = new GameObject("Node" + i); left.transform.SetParent(a.transform, false);
                    var right = new GameObject("Node" + i); right.transform.SetParent(b.transform, false);
                    left.AddComponent<BoxCollider>().size = Vector3.one;
                    right.AddComponent<BoxCollider>().size = i % 4 == 0 ? Vector3.one * 2 : Vector3.one;
                }
                DiffNode actual = null;
                Measure("analyze_serialized_hierarchy", count, () => actual = PrefabDiffAnalyzer.Analyze(a, b),
                    "Every modified collider found without local-reference noise");
                Assert.That(actual.Children.Count(node => node.HasAnyChanges), Is.EqualTo(count / 4));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase(16)]
        [TestCase(64)]
        [Category("UnityBoxPerformance")]
        public void ApplyAssetVariantVersusSceneComposition(int depth)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            string folder = "Assets/__PrefabPerformance_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var a = new GameObject("Avatar"); var b = new GameObject("Avatar");
            SceneManager.MoveGameObjectToScene(a, scene); SceneManager.MoveGameObjectToScene(b, scene);
            try
            {
                var left = a.transform; var right = b.transform;
                string path = "";
                for (int i = 0; i < depth; i++)
                {
                    string name = "Node" + i;
                    var l = new GameObject(name); l.transform.SetParent(left, false); left = l.transform;
                    var r = new GameObject(name); r.transform.SetParent(right, false); right = r.transform;
                    path += (path.Length == 0 ? "" : "/") + name;
                }
                left.gameObject.AddComponent<Light>().intensity = 1;
                right.gameObject.AddComponent<Light>().intensity = 2;
                var assetA = PrefabUtility.SaveAsPrefabAsset(a, folder + "/A.prefab");
                var assetB = PrefabUtility.SaveAsPrefabAsset(b, folder + "/B.prefab");
                var selections = new[] { new ComponentSelection(path, "UnityEngine.Light#0") };
                GameObject actual = null;
                Measure("apply_scene_composition_reference", depth,
                    () => actual = PrefabDiffApplier.Apply(a, b, Array.Empty<string>(), selections, folder + "/Scene.prefab"),
                    "Selected deep Light value persisted, source scene unchanged");
                Assert.That(actual.transform.Find(path).GetComponent<Light>().intensity, Is.EqualTo(2));
                Measure("apply_prefab_variant", depth,
                    () => actual = PrefabDiffApplier.Apply(assetA, assetB, Array.Empty<string>(), selections, folder + "/Variant.prefab"),
                    "Single base instantiation, inherited hierarchy and selected override persisted");
                Assert.That(actual.transform.Find(path).GetComponent<Light>().intensity, Is.EqualTo(2));
                Assert.That(PrefabUtility.GetPrefabAssetType(actual), Is.EqualTo(PrefabAssetType.Variant));
                Assert.That(left.GetComponent<Light>().intensity, Is.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Serializable]
        private sealed class Measurement
        {
            public string plugin, scenario, correctness;
            public int size, samples, operations_per_sample;
            public double median_ms, p95_ms, min_ms, max_ms;
            public long allocated_bytes_per_op;
        }

        private static readonly bool AllocationCounterSupported = ProbeAllocationCounter();
        private static bool ProbeAllocationCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var retained = new byte[8192];
            long difference = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(retained);
            return difference >= 8192;
        }

        private static void Measure(string scenario, int size, Action action, string correctness, int operations = 1)
        {
            for (int i = 0; i < 2; i++) action();
            const int samples = 9;
            var times = new double[samples];
            var allocations = new long[samples];
            for (int i = 0; i < samples; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                action();
                long stop = Stopwatch.GetTimestamp();
                allocations[i] = AllocationCounterSupported
                    ? (GC.GetAllocatedBytesForCurrentThread() - before) / operations : -1;
                times[i] = (stop - start) * 1000.0 / Stopwatch.Frequency / operations;
            }
            Array.Sort(times); Array.Sort(allocations);
            UnityEngine.Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Measurement
            {
                plugin = "PrefabDiffPatcher", scenario = scenario, size = size, samples = samples,
                operations_per_sample = operations, median_ms = times[4], p95_ms = times[8],
                min_ms = times[0], max_ms = times[8], allocated_bytes_per_op = allocations[4],
                correctness = correctness,
            }));
        }
    }
}
