using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.VisemeBlendshapeOverride
{
    public static class VisemeBlendshapeOverrideVrcUtils
    {
        internal const string FallbackGeneratedFolderRoot = "Assets/UnityBox/VisemeBlendshapeOverride/Generated";
        public static AnimatorController GetExistingFxController(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || descriptor.baseAnimationLayers == null)
                return null;

            return descriptor.baseAnimationLayers
                .FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .animatorController as AnimatorController;
        }

        internal static bool IsFallbackGeneratedController(AnimatorController controller)
        {
            if (controller == null)
                return false;

            var path = AssetDatabase.GetAssetPath(controller);
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Replace('\\', '/').StartsWith(FallbackGeneratedFolderRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static int EnsureFxLayer(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var layers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var fxIndex = Array.FindIndex(layers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
            if (fxIndex < 0)
            {
                fxIndex = layers.Length;
                Array.Resize(ref layers, layers.Length + 1);
                layers[fxIndex] = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = true,
                };
            }

            descriptor.baseAnimationLayers = layers;
            if (descriptor.specialAnimationLayers == null)
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            return fxIndex;
        }

        // Enabling custom layers for a generated FX must not revive old assignments that
        // were inactive while the Descriptor's customization switch was off.
        public static void DiscardInactiveLayerAssignments(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || descriptor.customizeAnimationLayers) return;
            descriptor.baseAnimationLayers = DefaultLayers(descriptor.baseAnimationLayers);
            descriptor.specialAnimationLayers = DefaultLayers(descriptor.specialAnimationLayers);
        }

        private static VRCAvatarDescriptor.CustomAnimLayer[] DefaultLayers(
            VRCAvatarDescriptor.CustomAnimLayer[] source)
        {
            var result = source?.ToArray() ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (var index = 0; index < result.Length; index++)
            {
                result[index].isDefault = true;
                result[index].animatorController = null;
                result[index].mask = null;
            }
            return result;
        }

        internal static AnimatorController GetOrCreateFallbackFxController(VRCAvatarDescriptor descriptor, GameObject avatarRoot)
        {
            if (descriptor == null)
                return null;

            DiscardInactiveLayerAssignments(descriptor);
            var fxIndex = EnsureFxLayer(descriptor);
            var layers = descriptor.baseAnimationLayers;

            var source = layers[fxIndex].isDefault ? null : layers[fxIndex].animatorController;
            if (source != null && !(source is AnimatorController))
                throw new InvalidOperationException("The fallback path requires an AnimatorController FX layer. " +
                    "Use NDMF 1.7 or newer for AnimatorOverrideController support.");

            // Each build gets its own asset; avatars with the same name must never overwrite each other.
            var controllerPath = GetFallbackGeneratedControllerPath(avatarRoot);
            EnsureFolder(Path.GetDirectoryName(controllerPath)?.Replace('\\', '/'));
            controllerPath = AssetDatabase.GenerateUniqueAssetPath(controllerPath);
            var controller = source is AnimatorController existingController
                ? CloneControllerToPath(existingController, controllerPath)
                : AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            layers[fxIndex].animatorController = controller;
            layers[fxIndex].isDefault = false;
            descriptor.baseAnimationLayers = layers;
            descriptor.customizeAnimationLayers = true;
            return controller;
        }

        private static AnimatorController CloneControllerToPath(AnimatorController source, string targetPath)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return source;

            EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
                AssetDatabase.DeleteAsset(targetPath);

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long sourceLocalId))
                throw new InvalidOperationException($"Failed to identify source FX controller at '{sourcePath}'.");
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                throw new InvalidOperationException($"Failed to clone FX controller from '{sourcePath}' to '{targetPath}'.");

            // A file can contain multiple AnimatorControllers. CopyAsset preserves its
            // local IDs; loading only the main asset can silently select another graph.
            var clonedController = AssetDatabase.LoadAllAssetsAtPath(targetPath).OfType<AnimatorController>()
                .FirstOrDefault(candidate => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    candidate, out string _, out long localId) && localId == sourceLocalId);
            if (clonedController == null)
                throw new InvalidOperationException($"Failed to resolve cloned FX controller {sourceLocalId} at '{targetPath}'.");

            return clonedController;
        }

        private static string GetFallbackGeneratedControllerPath(GameObject avatarRoot)
        {
            var safeAvatarName = SanitizeAssetName(avatarRoot != null ? avatarRoot.name : "Avatar");
            return $"{FallbackGeneratedFolderRoot}/{safeAvatarName}/{safeAvatarName}_VisemeBlendshapeOverride.controller";
        }

        private static string SanitizeAssetName(string rawName)
        {
            // Use the same single-segment name on every editor platform. In particular,
            // neither dot segments nor backslashes may become paths during folder creation.
            var sanitized = new string((rawName ?? string.Empty)
                .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character : '_')
                .ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Avatar" : sanitized;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            var normalizedPath = folderPath.Replace('\\', '/');
            var parts = normalizedPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
        public static IEnumerable<AnimatorController> GetRelatedControllers(VRCAvatarDescriptor descriptor)
        {
            return (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Concat(descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Where(layer => !layer.isDefault)
                .Select(layer => layer.animatorController as AnimatorController)
                .Where(controller => controller != null && !controller.name.StartsWith("vrc_", StringComparison.OrdinalIgnoreCase));
        }
    }
}
