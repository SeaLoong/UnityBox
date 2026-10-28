using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityBox.BuildPipelineLogger.Tests
{
  public class RepeatedVrcBuildLoggingTests
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
    public void ShaderCallbackLogsAgainOnTheNextAvatarBuild()
    {
      var shader = Shader.Find("Hidden/InternalErrorShader");
      Assert.That(shader, Is.Not.Null);
      var callback = new PreprocessShadersLogger_Early();
      var data = new List<ShaderCompilerData>();
      for (int build = 0; build < 2; build++)
      {
        new VRCSDKPreprocessAvatarLogger_MinValue().OnPreprocessAvatar(null);
        LogAssert.Expect(LogType.Log, new Regex("IPreprocessShaders.OnProcessShader"));
        callback.OnProcessShader(shader, default, data);
        callback.OnProcessShader(shader, default, data);
        LogAssert.NoUnexpectedReceived();
      }
    }
  }
}
