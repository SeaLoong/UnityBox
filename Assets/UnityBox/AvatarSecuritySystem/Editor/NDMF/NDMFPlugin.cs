using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(UnityBox.AvatarSecuritySystem.Editor.NDMFPlugin))]

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 存在 NDMF 时，ASS 不再依赖 VRCSDK 的 callbackOrder 数轴（其与 VRCFury 等其他
    /// 直接注册在 VRCSDK 层的收尾钩子之间没有确定的相对顺序保证），而是直接使用 NDMF
    /// 的 Plugin API 注册到 NDMF 概念中真正的最后一个 BuildPhase（PlatformFinish，
    /// 在 Optimizing 之后、平台专属收尾校验阶段），在 MA / VRCFury 等所有 NDMF pass
    /// 完成之后、NDMF 把结果写回真实资产（context.Finish()）之前执行。
    /// </summary>
    public class NDMFPlugin : Plugin<NDMFPlugin>
    {
        public override string QualifiedName => "com.unitybox.avatarsecuritysystem";
        public override string DisplayName => "UnityBox Avatar Security System";

        protected override void Configure()
        {
            InPhase(BuildPhase.PlatformFinish).Run("Generate Avatar Security System", ctx =>
            {
                if (!Processor.ProcessAvatar(ctx.AvatarRootObject, hasNDMF: true))
                {
                    throw new System.Exception(
                        "[ASS] Avatar Security System processing failed (see previous log for details)");
                }
            });
        }
    }
}
