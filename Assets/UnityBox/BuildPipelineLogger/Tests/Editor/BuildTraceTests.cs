using NUnit.Framework;
using UnityEditor;
using UnityEditor.UnityLinker;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace UnityBox.BuildPipelineLogger.Tests
{
    public class BuildTraceTests
    {
        [Test]
        public void TracePreservesCallbackOrderElapsedTimeAndCompletedSessionThroughJson()
        {
            BuildPipelineLoggerTrace.BeginBuild();
            BuildPipelineLoggerTrace.Record("BUILD", -100, "PRE", "start");
            BuildPipelineLoggerTrace.Record("NDMF", 0, "PHASE", "transform");
            BuildPipelineLoggerTrace.EndBuild();
            var data = JsonUtility.FromJson<BuildPipelineTraceData>(BuildPipelineLoggerTrace.ExportJson());
            Assert.That(data.completed, Is.True);
            Assert.That(data.entries, Has.Count.EqualTo(2));
            Assert.That(data.entries[0].sequence, Is.Zero);
            Assert.That(data.entries[1].sequence, Is.EqualTo(1));
            Assert.That(data.entries[1].elapsed_ms, Is.GreaterThanOrEqualTo(data.entries[0].elapsed_ms));
            Assert.That(data.elapsed_ms, Is.GreaterThanOrEqualTo(data.entries[1].elapsed_ms));
            Assert.That(data.entries[1].pipeline, Is.EqualTo("NDMF"));
            BuildPipelineLoggerTrace.Record("ASSET", 0, "IMPORT", "after build");
            var completed = JsonUtility.FromJson<BuildPipelineTraceData>(BuildPipelineLoggerTrace.ExportLastCompletedJson());
            Assert.That(completed.completed, Is.True);
            Assert.That(completed.entries, Has.Count.EqualTo(2));
            var next = JsonUtility.FromJson<BuildPipelineTraceData>(BuildPipelineLoggerTrace.ExportJson());
            Assert.That(next.completed, Is.False);
            Assert.That(next.started_utc, Is.Not.Null.And.Not.Empty);
            Assert.That(next.entries, Has.Count.EqualTo(1));
            Assert.That(next.entries[0].pipeline, Is.EqualTo("ASSET"));
            BuildPipelineLoggerTrace.BeginBuild();
            Assert.That(BuildPipelineLoggerTrace.Entries, Is.Empty);
            Assert.That(BuildPipelineLoggerTrace.Completed, Is.False);
        }

        [Test]
        public void UnityLinkerCallbackRecordsWithoutAddingLinkerInput()
        {
            bool previous = BuildPipelineLoggerSettings.BuildPipeline;
            BuildPipelineLoggerSettings.BuildPipeline = true;
            try
            {
                BuildPipelineLoggerLog.BeginBuild();
                LogAssert.Expect(LogType.Log, new Regex("IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile"));
                var result = new UnityLinkerLogger().GenerateAdditionalLinkXmlFile(null,
                    new UnityLinkerBuildPipelineData(BuildTarget.StandaloneLinux64, "Library"));
                Assert.That(result, Is.Null);
                Assert.That(BuildPipelineLoggerTrace.Entries[0].marker, Is.EqualTo("LINKER"));
            }
            finally { BuildPipelineLoggerSettings.BuildPipeline = previous; BuildPipelineLoggerLog.BeginBuild(); }
        }
    }
}
