using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Playable controller obfuscation is intentionally delayed to the very end of the VRCSDK
    /// preprocess chain so it can observe the final FX controller after VRCFury special processing
    /// and parameter compression have completed.
    /// </summary>
    public class PlayableObfuscationProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // 在 VRCFury ParameterCompressor(int.MaxValue - 100) 之后执行，
        // 并尽量靠后（int.MaxValue - 1）以减少被后续构建步骤覆盖的概率。
        public int callbackOrder => int.MaxValue - 1;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            try
            {
#if NDMF_AVAILABLE
                const bool hasNDMF = true;
                return Processor.ProcessPlayableObfuscation(avatarGameObject, hasNDMF);
#else
                const bool hasNDMF = false;
                return Processor.ProcessPlayableObfuscation(avatarGameObject, hasNDMF);
#endif
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] Playable obfuscation failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
    }
}