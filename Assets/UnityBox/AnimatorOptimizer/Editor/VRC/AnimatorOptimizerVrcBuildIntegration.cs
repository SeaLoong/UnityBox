using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerConfigCaptureCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 20;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            AnimatorOptimizerBuildState.Release(avatarGameObject);
            AnimatorOptimizerBuildState.Capture(avatarGameObject, "early VRCSDK preprocess");
            AnimatorOptimizerPlatform.StripConfigurationComponents(avatarGameObject);
            return true;
        }
    }

    public sealed class AnimatorOptimizerPreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        // VRCFury's EditorOnly cleanup is around -1024 in the current project. We use -1023
        // and a configuration snapshot so the component may already have been removed.
        public int callbackOrder => -1023;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var capturedConfig = AnimatorOptimizerBuildState.Get(avatarGameObject);
            if (capturedConfig != null && capturedConfig.ExecuteLateOptimization)
            {
                Debug.Log("[AnimatorOptimizer] Late optimization requested; early VRCSDK callback deferred.");
                return true;
            }
            // This state belongs to the core Editor assembly. Checking it needs
            // no NDMF reference or matching version define in a second assembly.
            if (AnimatorOptimizerBuildState.WasNdmfProcessed(avatarGameObject))
            {
                Debug.Log("[AnimatorOptimizer] NDMF processed this Avatar; VRCSDK fallback skipped.");
                return true;
            }
            try
            {
                var config = AnimatorOptimizerBuildState.Get(avatarGameObject) ??
                             AnimatorOptimizerConfigData.FromAvatar(avatarGameObject);
                if (config == null)
                    return true;
                return AnimatorOptimizerProcessor.ProcessAvatar(avatarGameObject, config);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Build processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }

    public sealed class AnimatorOptimizerLatePreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        // ASS uses int.MaxValue - 1 in this project. Run after that callback when NDMF
        // is not present; with NDMF, the PlatformFinish plugin owns the late pass.
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var config = AnimatorOptimizerBuildState.Get(avatarGameObject);
            if (config == null || !config.ExecuteLateOptimization)
                return true;
            if (AnimatorOptimizerBuildState.WasNdmfProcessed(avatarGameObject))
                return true;
            try
            {
                return AnimatorOptimizerProcessor.ProcessAvatar(avatarGameObject, config);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Late build processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }

    public sealed class AnimatorOptimizerPostprocessCallback : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -1023;

        public void OnPostprocessAvatar()
        {
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            AnimatorOptimizerBuildState.ReleaseAll();
        }
    }

}
