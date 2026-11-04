using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityBox.BuildPipelineLogger.Tests
{
  public class RepeatedBuildLoggingTests
  {
    private bool buildLoggingBefore;
    private bool sdkLoggingBefore;

    [SetUp]
    public void SetUp()
    {
      buildLoggingBefore = BuildPipelineLoggerSettings.BuildPipeline;
      sdkLoggingBefore = BuildPipelineLoggerSettings.VRCSDK;
      BuildPipelineLoggerSettings.BuildPipeline = true;
      BuildPipelineLoggerSettings.VRCSDK = false;
    }

    [TearDown]
    public void TearDown()
    {
      BuildPipelineLoggerSettings.BuildPipeline = buildLoggingBefore;
      BuildPipelineLoggerSettings.VRCSDK = sdkLoggingBefore;
      BuildPipelineLoggerLog.BeginBuild();
    }

    [Test]
    public void PlayerPreparationAndPreprocessRemainInOneTraceAcrossRepeatedBuilds()
    {
      for (int build = 0; build < 2; build++)
      {
        LogAssert.Expect(LogType.Log, new Regex("BuildPlayerProcessor.PrepareForBuild"));
        new BuildPlayerProcessor_MinValue().PrepareForBuild(null);
        Assert.That(BuildPipelineLoggerTrace.Entries, Has.Count.EqualTo(1));
        LogAssert.Expect(LogType.Log, new Regex("IPreprocessBuildWithReport.OnPreprocessBuild"));
        new PreBuildLogger_MinValue().OnPreprocessBuild(null);
        var trace = JsonUtility.FromJson<BuildPipelineTraceData>(BuildPipelineLoggerTrace.ExportJson());
        Assert.That(trace.entries, Has.Count.EqualTo(2));
        Assert.That(trace.entries[0].message, Does.Contain("PrepareForBuild"));
        Assert.That(trace.entries[1].message, Does.Contain("OnPreprocessBuild"));
        Assert.That(trace.entries[1].sequence, Is.EqualTo(1));
        Assert.That(trace.entries[1].elapsed_ms, Is.GreaterThanOrEqualTo(trace.entries[0].elapsed_ms));
        BuildPipelineLoggerTrace.EndBuild();
      }
    }

    [Test]
    public void ShaderCallbackLogsAgainOnTheNextBuild()
    {
      var shader = Shader.Find("Hidden/InternalErrorShader");
      Assert.That(shader, Is.Not.Null);
      var callback = new PreprocessShadersLogger_Early();
      var data = new List<ShaderCompilerData>();
      for (int build = 0; build < 2; build++)
      {
        LogAssert.Expect(LogType.Log, new Regex("IPreprocessBuildWithReport.OnPreprocessBuild"));
        new PreBuildLogger_MinValue().OnPreprocessBuild(null);
        LogAssert.Expect(LogType.Log, new Regex("IPreprocessShaders.OnProcessShader"));
        callback.OnProcessShader(shader, default, data);
        callback.OnProcessShader(shader, default, data);
        LogAssert.NoUnexpectedReceived();
      }
    }

    [Test]
    public void DisabledShaderLoggingLeavesVariantsUntouchedAndDoesNotConsumeFirstLog()
    {
      var shader = Shader.Find("Hidden/InternalErrorShader");
      Assert.That(shader, Is.Not.Null);
      var variants = new List<ShaderCompilerData> { default, default };
      var callback = new PreprocessShadersLogger_Early();
      BuildPipelineLoggerLog.BeginBuild();
      BuildPipelineLoggerSettings.BuildPipeline = false;
      callback.OnProcessShader(shader, default, variants);
      Assert.That(variants, Has.Count.EqualTo(2));
      LogAssert.NoUnexpectedReceived();
      BuildPipelineLoggerSettings.BuildPipeline = true;
      LogAssert.Expect(LogType.Log, new Regex("IPreprocessShaders.OnProcessShader"));
      callback.OnProcessShader(shader, default, variants);
      Assert.That(variants, Has.Count.EqualTo(2));
      LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void AssemblyFilterReturnsOriginalInputWithoutRemovingAssemblies()
    {
      var assemblies = new[] { "Game.dll", "OptionalIntegration.dll" };
      LogAssert.Expect(LogType.Log, new Regex("IFilterBuildAssemblies.OnFilterAssemblies"));
      var result = new FilterAssembliesLogger_Early().OnFilterAssemblies(BuildOptions.None, assemblies);
      Assert.That(result, Is.SameAs(assemblies));
      Assert.That(result, Is.EqualTo(new[] { "Game.dll", "OptionalIntegration.dll" }));
      LogAssert.NoUnexpectedReceived();
    }
  }
}
