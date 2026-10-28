using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.VisemeBlendshapeOverride
{
    public class VisemeBlendshapeOverridePreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10001;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            try
            {
                // NDMF consumes the authoring component only after successful generation.
                // If its build hook was disabled or never ran, the component is still present
                // and this real SDK fallback must run even when NDMF is installed.
                if (VisemeBlendshapeOverrideVrcProcessor.TryProcessAvatar(avatarGameObject))
                    UnityEngine.Object.DestroyImmediate(
                        avatarGameObject.GetComponent<VisemeBlendshapeOverrideComponent>());
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Viseme Blendshape Override] Avatar processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }
    public static class VisemeBlendshapeOverrideVrcProcessor
    {
        public static bool TryCreateBuildPlan(GameObject avatarRoot, out VisemeBlendshapeOverrideProcessor.BuildPlan plan)
        {
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            plan = null;
            return descriptor != null && VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(
                avatarRoot, out plan, descriptor.VisemeSkinnedMesh, descriptor.VisemeBlendShapes);
        }

        public static void ProcessAvatar(GameObject avatarRoot)
        {
            TryProcessAvatar(avatarRoot);
        }

        internal static bool TryProcessAvatar(GameObject avatarRoot)
        {
            if (!TryCreateBuildPlan(avatarRoot, out var plan)) return false;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var controller = VisemeBlendshapeOverrideVrcUtils.GetOrCreateFallbackFxController(descriptor, avatarRoot);
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan,
                VisemeBlendshapeOverrideVrcUtils.GetRelatedControllers(descriptor));
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            EditorUtility.SetDirty(descriptor);
            return true;
        }
    }

    // Keep an explicit build-clone cleanup in addition to the optional SDK IEditorOnly marker.
    public sealed class VisemeBlendshapeOverrideStripCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            foreach (var component in avatarGameObject.GetComponentsInChildren<VisemeBlendshapeOverrideComponent>(true))
                UnityEngine.Object.DestroyImmediate(component);
            return true;
        }
    }
}
