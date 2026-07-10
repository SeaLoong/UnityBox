using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AvatarSecuritySystem.Editor
{
#if !NDMF_AVAILABLE
    /// <summary>
    /// Captures ASS configuration before VRCSDK / third-party EditorOnly cleanup can remove
    /// the IEditorOnly ASSComponent in non-NDMF builds.
    /// </summary>
    public class ConfigCaptureProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 10;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Processor.CaptureConfigSnapshot(avatarGameObject, $"early VRCSDK preprocess (callbackOrder={callbackOrder})");
            return true;
        }
    }
#endif
}
