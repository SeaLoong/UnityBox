using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(UnityBox.BuildPipelineLogger.BuildPipelineLoggerPlugin))]

namespace UnityBox.BuildPipelineLogger
{
  public class BuildPipelineLoggerPlugin : Plugin<BuildPipelineLoggerPlugin>
  {
    public override string QualifiedName => "top.sealoong.unitybox.build-pipeline-logger";
    public override string DisplayName => "Build Pipeline Logger";

    protected override void Configure()
    {
      // These are sampling passes, not global start/end hooks for other plugins.
      Observe(BuildPhase.Resolving, "Resolving");
      Observe(BuildPhase.Generating, "Generating");
      Observe(BuildPhase.Transforming, "Transforming");
      Observe(BuildPhase.Optimizing, "Optimizing");
    }

    private void Observe(BuildPhase phase, string name)
    {
      InPhase(phase).Run("Observe_" + name, ctx =>
      {
        if (!BuildPipelineLoggerSettings.NDMF) return;
        BuildPipelineLoggerLog.LogRaw("NDMF", "PHASE SAMPLE",
          name + " observation; Avatar: " + ctx.AvatarRootObject.name);
      });
    }
  }
}
