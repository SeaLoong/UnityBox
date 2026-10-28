using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(UnityBox.AvatarColliderMonitor.AvatarColliderMonitorPlugin))]

namespace UnityBox.AvatarColliderMonitor
{
  public class AvatarColliderMonitorPlugin : Plugin<AvatarColliderMonitorPlugin>
  {
    public override string QualifiedName => "top.sealoong.unitybox.avatar-collider-monitor";
    public override string DisplayName => "Avatar Collider Monitor";

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
        if (!AvatarColliderMonitor.IsEnabled) return;
        AvatarColliderMonitor.RecordSnapshot(ctx.AvatarRootObject, "NDMF-" + name, "Observation");
      });
    }
  }
}
