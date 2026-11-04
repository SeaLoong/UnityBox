using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Configuration is platform-independent; remove it from upload clones explicitly.</summary>
    public sealed class ConfigurationStripper : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Strip(avatarGameObject);
            return true;
        }

        public static void Strip(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;
            foreach (var component in avatarRoot.GetComponentsInChildren<ASSComponent>(true))
                Object.DestroyImmediate(component);
        }
    }
}
