using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace UnityBox.BuildPipelineLogger.Tests
{
    public class LoggerPerformanceTests
    {
        [TestCase(1000)]
        [TestCase(10000)]
        [Category("UnityBoxPerformance")]
        public void DisabledCallbacksUseCachedSettingsAndKeepVariants(int iterations)
        {
            bool before = BuildPipelineLoggerSettings.BuildPipeline;
            BuildPipelineLoggerSettings.BuildPipeline = false;
            try
            {
                var variants = new List<ShaderCompilerData> { default, default };
                var callback = new PreprocessShadersLogger_Early();
                bool reference = false;
                Measure("editorprefs_setting_read_reference", iterations, () =>
                {
                    for (int i = 0; i < iterations; i++)
                        reference = EditorPrefs.GetBool("UnityBox.BuildPipelineLogger.BuildPipeline", false);
                }, "Previous uncached settings read returns disabled", iterations);
                Assert.That(reference, Is.False);
                Measure("disabled_shader_callback", iterations, () =>
                {
                    for (int i = 0; i < iterations; i++) callback.OnProcessShader(null, default, variants);
                }, "Shader variants unchanged; cached disabled path", iterations);
                Assert.That(variants.Count, Is.EqualTo(2));
            }
            finally { BuildPipelineLoggerSettings.BuildPipeline = before; }
        }

        [TestCase(128)]
        [TestCase(1024)]
        [Category("UnityBoxPerformance")]
        public void StructuredTraceRecordingAndExportScaleWithEntries(int count)
        {
            Measure("record_callback_trace", count, () =>
            {
                BuildPipelineLoggerTrace.BeginBuild();
                for (int i = 0; i < count; i++)
                    BuildPipelineLoggerTrace.Record("BUILD", i, "SAMPLE", "Callback");
                BuildPipelineLoggerTrace.EndBuild();
            }, "Ordered timestamped entries retained", count);
            Assert.That(BuildPipelineLoggerTrace.Entries.Count, Is.EqualTo(count));
            string exported = null;
            Measure("export_callback_trace_json", count,
                () => exported = BuildPipelineLoggerTrace.ExportJson(), "Every entry survives JSON export");
            var data = JsonUtility.FromJson<BuildPipelineTraceData>(exported);
            Assert.That(data.entries.Count, Is.EqualTo(count));
            Assert.That(data.completed, Is.True);
            Assert.That(data.entries[count - 1].sequence, Is.EqualTo(count - 1));
            BuildPipelineLoggerLog.BeginBuild();
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
                plugin = "BuildPipelineLogger", scenario = scenario, size = size, samples = samples,
                operations_per_sample = operations, median_ms = times[4], p95_ms = times[8],
                min_ms = times[0], max_ms = times[8], allocated_bytes_per_op = allocations[4],
                correctness = correctness,
            }));
        }
    }
}
