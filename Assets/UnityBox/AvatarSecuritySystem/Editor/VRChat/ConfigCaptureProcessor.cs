using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Captures platform-independent configuration before other build processing.
    /// ConfigurationStripper removes it from the upload clone after generation.
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
}
