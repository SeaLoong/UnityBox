using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace UnityBox.AvatarColliderMonitor.Tests
{
    public class ColliderPerformanceTests
    {
        [TestCase(32)]
        [TestCase(256)]
        [TestCase(1024)]
        [Category("UnityBoxPerformance")]
        public void CaptureScalesWithColliderHierarchy(int count)
        {
            var root = new GameObject("BenchmarkRoot");
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var child = new GameObject("Collider" + i);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<BoxCollider>();
                }
                ColliderSnapshot actual = null;
                Measure("capture_3d_colliders", count,
                    () => actual = AvatarColliderMonitor.CaptureSnapshot(root, "Benchmark", "Capture"),
                    "Every collider configuration and world bound captured");
                Assert.That(actual.paths.Count, Is.EqualTo(count));
                Assert.That(actual.worldBounds.Count, Is.EqualTo(count));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(100)]
        [TestCase(1000)]
        [Category("UnityBoxPerformance")]
        public void CachedHistoryReplacesRepeatedHistoryAnalysis(int count)
        {
            var root = new GameObject("HistoryBenchmark");
            bool logBefore = AvatarColliderMonitor.LogChanges;
            AvatarColliderMonitor.LogChanges = false;
            AvatarColliderMonitor.Clear();
            try
            {
                var collider = root.AddComponent<SphereCollider>();
                for (int i = 0; i < count; i++)
                {
                    collider.radius = i % 2 == 0 ? 1 : 2;
                    AvatarColliderMonitor.RecordSnapshot(root, "Manual", "Benchmark");
                }
                var cached = AvatarColliderMonitor.GetHistoryComparisons();
                int expected = AvatarColliderMonitor.TotalChanges;
                int recomputed = 0;
                Measure("history_recompute_reference", count, () =>
                {
                    recomputed = 0;
                    var snapshots = AvatarColliderMonitor.GetSnapshots();
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        var previous = AvatarColliderMonitor.GetPreviousSnapshot(i);
                        foreach (var key in snapshots[i].KeysComparedTo(previous))
                            if (snapshots[i].HasChangedFrom(previous, key)) recomputed++;
                    }
                }, "Reference full history scan agrees with cached changes");
                Assert.That(recomputed, Is.EqualTo(expected));
                Measure("history_cached_read", count, () =>
                {
                    for (int i = 0; i < 10000; i++)
                        cached = AvatarColliderMonitor.GetHistoryComparisons();
                }, "Cached immutable comparison view reused", 10000);
                Assert.That(cached.Count, Is.EqualTo(count));
                var preview = new ColliderSnapshotPreview();
                var sampled = preview.Get(root);
                ColliderSnapshot reused = null;
                Measure("preview_cached_read", count, () =>
                {
                    for (int i = 0; i < 10000; i++) reused = preview.Get(root);
                }, "No resampling or serialization during cached preview reads", 10000);
                Assert.That(reused, Is.SameAs(sampled));
            }
            finally
            {
                AvatarColliderMonitor.Clear(); AvatarColliderMonitor.LogChanges = logBefore;
                UnityEngine.Object.DestroyImmediate(root);
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
                plugin = "AvatarColliderMonitor", scenario = scenario, size = size, samples = samples,
                operations_per_sample = operations, median_ms = times[4], p95_ms = times[8],
                min_ms = times[0], max_ms = times[8], allocated_bytes_per_op = allocations[4],
                correctness = correctness,
            }));
        }
    }
}
