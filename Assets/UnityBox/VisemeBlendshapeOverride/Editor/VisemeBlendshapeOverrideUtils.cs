using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.VisemeBlendshapeOverride
{
    public static class VisemeBlendshapeOverrideUtils
    {
        public const string BuiltInVisemeParameter = "Viseme";
        public const string BuiltInVoiceParameter = "Voice";
        public const string GeneratedLayerName = "Viseme Blendshape Override";
        public const string GeneratedAssetPrefix = "VisemeBlendshapeOverride_";
        internal const string FallbackGeneratedFolderRoot = "Assets/UnityBox/VisemeBlendshapeOverride/Generated";

        public static string GetRelativePath(GameObject root, GameObject node)
        {
            if (root == null || node == null)
                return null;

            if (root == node)
                return string.Empty;

            var parts = new List<string>();
            var current = node.transform;
            while (current != null && current != root.transform)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root.transform)
                return null;

            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static AnimatorControllerLayer CreateLayer(string name)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = GeneratedAssetPrefix + "StateMachine",
                hideFlags = HideFlags.HideInHierarchy,
            };

            return new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                stateMachine = stateMachine,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false,
            };
        }

        internal static AnimatorControllerParameter CreateParameter(
            string name,
            AnimatorControllerParameterType type,
            bool defaultBool = false,
            int defaultInt = 0,
            float defaultFloat = 0f)
        {
            return new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultBool = defaultBool,
                defaultInt = defaultInt,
                defaultFloat = defaultFloat,
            };
        }

        internal static void EnsureParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type,
            bool defaultBool = false,
            int defaultInt = 0,
            float defaultFloat = 0f)
        {
            var parameters = controller.parameters;
            var index = Array.FindIndex(parameters, parameter => parameter.name == name);
            if (index < 0)
            {
                controller.AddParameter(CreateParameter(name, type, defaultBool, defaultInt, defaultFloat));
                return;
            }

            if (parameters[index].type == type)
                return;

            parameters[index] = CreateParameter(name, type, defaultBool, defaultInt, defaultFloat);
            controller.parameters = parameters;
        }

        internal static AnimatorControllerParameterType? GetParameterType(AnimatorController controller, string name)
        {
            var parameter = controller.parameters.FirstOrDefault(p => p.name == name);
            return parameter == null ? null : parameter.type;
        }

        internal static void EnsureVisemeParameter(AnimatorController controller)
        {
            EnsureParameter(controller, BuiltInVisemeParameter, AnimatorControllerParameterType.Int, defaultInt: 0);
        }

        internal static void EnsureVoiceParameter(AnimatorController controller)
        {
            EnsureParameter(controller, BuiltInVoiceParameter, AnimatorControllerParameterType.Float, defaultFloat: 0f);
        }

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

        /// <summary>Ensure a destination exists for the NDMF commit as well as the SDK fallback.</summary>
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

        internal static AnimatorController GetOrCreateFallbackFxController(VRCAvatarDescriptor descriptor, GameObject avatarRoot)
        {
            if (descriptor == null)
                return null;

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

        internal static void CleanupGeneratedContent(AnimatorController controller)
        {
            if (controller == null)
                return;

            var changed = false;
            for (var i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name == GeneratedLayerName)
                {
                    controller.RemoveLayer(i);
                    changed = true;
                }
            }

            var controllerPath = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrWhiteSpace(controllerPath))
            {
                var generatedAssets = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .Where(asset => asset != null && asset != controller)
                    .Where(asset => asset.name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal))
                    .ToArray();

                foreach (var asset in generatedAssets)
                {
                    AssetDatabase.RemoveObjectFromAsset(asset);
                    UnityEngine.Object.DestroyImmediate(asset, true);
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(controller);
        }

        internal static void AddSubAsset(AnimatorController controller, UnityEngine.Object asset)
        {
            if (controller == null || asset == null)
                return;

            var controllerPath = AssetDatabase.GetAssetPath(controller);
            var assetPath = AssetDatabase.GetAssetPath(asset);

            if (!string.IsNullOrWhiteSpace(assetPath) && !string.Equals(assetPath, controllerPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (AssetDatabase.LoadAllAssetsAtPath(controllerPath).Any(existing => existing == asset))
                return;

            AssetDatabase.AddObjectToAsset(asset, controllerPath);
        }

        internal static AnimationClip CreateBlendshapeClip(
            string name,
            string relativePath,
            IEnumerable<string> controlledBlendshapes,
            string activeBlendshape,
            float activeWeight)
        {
            var clip = new AnimationClip
            {
                name = name,
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            foreach (var blendshape in controlledBlendshapes)
            {
                var value = string.Equals(blendshape, activeBlendshape, StringComparison.Ordinal)
                    ? Mathf.Clamp(activeWeight, 0f, 100f)
                    : 0f;
                clip.SetCurve(
                    relativePath,
                    typeof(SkinnedMeshRenderer),
                    $"blendShape.{blendshape}",
                    AnimationCurve.Constant(0f, 1f / 60f, value));
            }

            return clip;
        }

        internal static BlendTree CreateVoiceBlendTree(
            string name,
            Motion zeroMotion,
            Motion activeMotion,
            float voiceMin,
            float voiceMax)
        {
            var clampedMin = voiceMin;
            var clampedMax = voiceMax;
            NormalizeVoiceRange(ref clampedMin, ref clampedMax);

            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = BuiltInVoiceParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };

            tree.AddChild(zeroMotion, clampedMin);
            tree.AddChild(activeMotion, clampedMax);
            return tree;
        }

        public static void NormalizeVoiceRange(ref float min, ref float max)
        {
            min = float.IsNaN(min) ? 0f : Mathf.Clamp01(min);
            max = float.IsNaN(max) ? 1f : Mathf.Clamp01(max);
            if (max <= min)
            {
                max = Mathf.Min(1f, min + 0.001f);
                min = Mathf.Max(0f, max - 0.001f);
            }
        }

        public static bool ResolveWriteDefaults(
            AnimatorController controller,
            VRCAvatarDescriptor descriptor,
            VisemeBlendshapeOverrideComponent.WriteDefaultsMode mode)
        {
            switch (mode)
            {
                case VisemeBlendshapeOverrideComponent.WriteDefaultsMode.On:
                    return true;
                case VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off:
                    return false;
            }

            var controllers = new HashSet<AnimatorController>();
            if (descriptor != null)
            {
                foreach (var layer in (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                    .Concat(descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
                {
                    if (layer.isDefault)
                        continue;
                    if (!(layer.animatorController is AnimatorController referencedController) || referencedController == null)
                        continue;
                    if (referencedController.name.StartsWith("vrc_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    controllers.Add(referencedController);
                }
            }

            if (controller != null) controllers.Add(controller);

            foreach (var currentController in controllers)
            {
                foreach (var layer in currentController.layers)
                {
                    if (layer.name == GeneratedLayerName)
                        continue;
                    if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
                        continue;
                    if (layer.stateMachine == null)
                        continue;
                    if (IsWriteDefaultsRequiredLayer(layer))
                        continue;
                    if (HasWriteDefaultsOffState(layer.stateMachine))
                        return false;
                }
            }

            return true;
        }

        private static bool IsWriteDefaultsRequiredLayer(AnimatorControllerLayer layer)
        {
            if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
                return true;

            var stateMachine = layer.stateMachine;
            if (stateMachine == null)
                return false;
            if (stateMachine.stateMachines.Length != 0)
                return false;
            if (stateMachine.states.Length != 1)
                return false;
            if (stateMachine.anyStateTransitions.Length != 0)
                return false;

            var defaultState = stateMachine.defaultState;
            if (defaultState == null)
                return false;
            if (defaultState.transitions.Length != 0)
                return false;
            if (!(defaultState.motion is BlendTree blendTree))
                return false;

            return HasDirectBlendTree(blendTree);
        }

        private static bool HasDirectBlendTree(BlendTree blendTree)
        {
            if (blendTree.blendType == BlendTreeType.Direct)
                return true;

            foreach (var child in blendTree.children)
            {
                if (child.motion is BlendTree childBlendTree && HasDirectBlendTree(childBlendTree))
                    return true;
            }

            return false;
        }

        private static bool HasWriteDefaultsOffState(AnimatorStateMachine stateMachine)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state.motion == null)
                    continue;
                if (state.motion is BlendTree blendTree && blendTree.blendType == BlendTreeType.Direct)
                    continue;
                if (!state.writeDefaultValues)
                    return true;
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (HasWriteDefaultsOffState(childStateMachine.stateMachine))
                    return true;
            }

            return false;
        }

        private static AnimatorController CloneControllerToPath(AnimatorController source, string targetPath)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return source;

            EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
                AssetDatabase.DeleteAsset(targetPath);

            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                throw new InvalidOperationException($"Failed to clone FX controller from '{sourcePath}' to '{targetPath}'.");

            var clonedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetPath);
            if (clonedController == null)
                throw new InvalidOperationException($"Failed to load cloned FX controller at '{targetPath}'.");

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

    }
}