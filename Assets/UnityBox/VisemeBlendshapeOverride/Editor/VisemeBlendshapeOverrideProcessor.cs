using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
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
                VisemeBlendshapeOverrideProcessor.ProcessAvatar(avatarGameObject);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Viseme Blendshape Override] Avatar processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }

    public static class VisemeBlendshapeOverrideProcessor
    {
        public sealed class ResolvedBinding
        {
            public VRC_AvatarDescriptor.Viseme Viseme;
            public string BlendshapeName;
            public float Weight;
            public VisemeBlendshapeOverrideComponent.VoiceModulationMode VoiceModulationMode;
            public float VoiceMin;
            public float VoiceMax;
        }

        public sealed class BuildPlan
        {
            public GameObject AvatarRoot;
            public VRCAvatarDescriptor Descriptor;
            public VisemeBlendshapeOverrideComponent Config;
            public SkinnedMeshRenderer TargetRenderer;
            public string RelativePath;
            public List<string> ControlledBlendshapes;
            public List<ResolvedBinding> ResolvedBindings;
        }

        public static bool TryCreateBuildPlan(GameObject avatarRoot, out BuildPlan plan)
        {
            plan = null;
            if (avatarRoot == null)
                return false;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return false;

            var config = avatarRoot.GetComponent<VisemeBlendshapeOverrideComponent>();
            if (config == null)
                return false;

            config.EnsureBindings();

            var targetRenderer = config.ResolveTargetRenderer(descriptor);
            if (targetRenderer == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] '{avatarRoot.name}' has no renderer. " +
                    "Assign Renderer or configure Avatar Descriptor > Face Mesh.");
                return false;
            }

            if (targetRenderer.sharedMesh == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] Renderer '{targetRenderer.name}' on '{avatarRoot.name}' has no shared mesh.");
                return false;
            }

            var relativePath = VisemeBlendshapeOverrideUtils.GetRelativePath(avatarRoot, targetRenderer.gameObject);
            if (relativePath == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] Renderer '{targetRenderer.name}' is not a child of avatar root '{avatarRoot.name}'.");
                return false;
            }

            var resolvedBindings = ResolveBindings(config, descriptor, targetRenderer);
            var controlledBlendshapes = resolvedBindings
                .Select(binding => binding.BlendshapeName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (controlledBlendshapes.Count == 0)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] '{avatarRoot.name}' does not have any valid viseme blendshape mapping on renderer '{targetRenderer.name}'.");
                return false;
            }

            plan = new BuildPlan
            {
                AvatarRoot = avatarRoot,
                Descriptor = descriptor,
                Config = config,
                TargetRenderer = targetRenderer,
                RelativePath = relativePath,
                ControlledBlendshapes = controlledBlendshapes,
                ResolvedBindings = resolvedBindings,
            };
            return true;
        }

        public static void ProcessAvatar(GameObject avatarRoot)
        {
            if (!TryCreateBuildPlan(avatarRoot, out var plan))
                return;

            var controller = VisemeBlendshapeOverrideUtils.GetOrCreateFallbackFxController(plan.Descriptor, plan.AvatarRoot);
            if (controller == null)
            {
                Debug.LogWarning($"[Viseme Blendshape Override] Failed to get or create an editable FX controller for '{plan.AvatarRoot.name}'.");
                return;
            }

            VisemeBlendshapeOverrideUtils.CleanupGeneratedContent(controller);
            VisemeBlendshapeOverrideUtils.EnsureVisemeParameter(controller);
            if (plan.ResolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled))
                VisemeBlendshapeOverrideUtils.EnsureVoiceParameter(controller);

            var useWriteDefaultsOn = VisemeBlendshapeOverrideUtils.ResolveWriteDefaults(
                controller,
                plan.Descriptor,
                plan.Config.writeDefaultsMode);

            GenerateFallbackLayer(
                controller,
                plan.RelativePath,
                plan.ControlledBlendshapes,
                plan.ResolvedBindings,
                useWriteDefaultsOn);

            plan.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;

            EditorUtility.SetDirty(plan.Config);
            EditorUtility.SetDirty(plan.Descriptor);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        public static List<ResolvedBinding> ResolveBindings(
            VisemeBlendshapeOverrideComponent config,
            VRCAvatarDescriptor descriptor,
            SkinnedMeshRenderer targetRenderer)
        {
            var resolved = new List<ResolvedBinding>();
            var mesh = targetRenderer.sharedMesh;

            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                var binding = config.GetBinding(viseme);
                var resolvedName = config.ResolveBlendshapeName(descriptor, binding);
                var sourceLabel = binding != null &&
                                  (string.Equals(binding.blendshapeName?.Trim(), VisemeBlendshapeOverrideComponent.NoneBlendshapeValue, StringComparison.Ordinal) ||
                                   !string.IsNullOrWhiteSpace(binding.blendshapeName))
                    ? "override"
                    : "Avatar Descriptor";

                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    resolvedName = resolvedName.Trim();
                    if (mesh.GetBlendShapeIndex(resolvedName) < 0)
                    {
                        Debug.LogWarning(
                            $"[Viseme Blendshape Override] Blendshape '{resolvedName}' for viseme '{viseme}' was not found on renderer '{targetRenderer.name}' " +
                            $"(source: {sourceLabel}). This viseme will fall back to no blendshape.");
                        resolvedName = string.Empty;
                    }
                }

                var effectiveVoiceMode = config.voiceModulationMode;
                var voiceMin = config.voiceMin;
                var voiceMax = config.voiceMax;
                var resolvedWeight = Mathf.Clamp(config.globalWeight, 0f, 100f);
                if (binding != null && binding.useCustomSettings)
                {
                    resolvedWeight = Mathf.Clamp(binding.weight, 0f, 100f);

                    switch (binding.voiceMode)
                    {
                        case VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Disabled:
                            effectiveVoiceMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;
                            break;
                        case VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Linear:
                            effectiveVoiceMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Linear;
                            voiceMin = binding.voiceMin;
                            voiceMax = binding.voiceMax;
                            break;
                        default:
                            effectiveVoiceMode = config.voiceModulationMode;
                            voiceMin = config.voiceMin;
                            voiceMax = config.voiceMax;
                            break;
                    }
                }

                voiceMin = Mathf.Clamp01(voiceMin);
                voiceMax = Mathf.Clamp01(voiceMax);
                if (voiceMax <= voiceMin)
                    voiceMax = Mathf.Min(1f, voiceMin + 0.001f);

                resolved.Add(new ResolvedBinding
                {
                    Viseme = viseme,
                    BlendshapeName = resolvedName,
                    Weight = resolvedWeight,
                    VoiceModulationMode = effectiveVoiceMode,
                    VoiceMin = voiceMin,
                    VoiceMax = voiceMax,
                });
            }

            return resolved;
        }

        private static void GenerateFallbackLayer(
            AnimatorController controller,
            string relativePath,
            IReadOnlyCollection<string> controlledBlendshapes,
            IEnumerable<ResolvedBinding> resolvedBindings,
            bool useWriteDefaultsOn)
        {
            var bindingsByViseme = resolvedBindings.ToDictionary(binding => binding.Viseme, binding => binding);
            var layer = VisemeBlendshapeOverrideUtils.CreateLayer(VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            var useAnyVoiceModulation = resolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled);
            AnimationClip zeroClip = null;
            if (useAnyVoiceModulation)
            {
                zeroClip = VisemeBlendshapeOverrideUtils.CreateBlendshapeClip(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + "VoiceZero_Clip",
                    relativePath,
                    controlledBlendshapes,
                    null,
                    0f);
                VisemeBlendshapeOverrideUtils.AddSubAsset(controller, zeroClip);
            }

            var row = 0;
            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                bindingsByViseme.TryGetValue(viseme, out var resolvedBinding);

                var state = layer.stateMachine.AddState(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme,
                    new Vector3(280f, row * 70f, 0f));
                state.writeDefaultValues = useWriteDefaultsOn;

                var clip = VisemeBlendshapeOverrideUtils.CreateBlendshapeClip(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme + "_Clip",
                    relativePath,
                    controlledBlendshapes,
                    resolvedBinding?.BlendshapeName,
                    resolvedBinding?.Weight ?? 0f);

                VisemeBlendshapeOverrideUtils.AddSubAsset(controller, clip);

                Motion motion = clip;
                if (resolvedBinding != null &&
                    resolvedBinding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled)
                {
                    var tree = VisemeBlendshapeOverrideUtils.CreateVoiceBlendTree(
                        VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme + "_VoiceTree",
                        zeroClip,
                        clip,
                        resolvedBinding.VoiceMin,
                        resolvedBinding.VoiceMax);
                    VisemeBlendshapeOverrideUtils.AddSubAsset(controller, tree);
                    motion = tree;
                }

                state.motion = motion;

                var transition = layer.stateMachine.AddAnyStateTransition(state);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.hasFixedDuration = true;
                transition.canTransitionToSelf = false;
                transition.AddCondition(
                    AnimatorConditionMode.Equals,
                    (float)(int)viseme,
                    VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter);

                if ((int)viseme == 0 || layer.stateMachine.defaultState == null)
                    layer.stateMachine.defaultState = state;

                row++;
            }

            VisemeBlendshapeOverrideUtils.AddSubAsset(controller, layer.stateMachine);
            controller.AddLayer(layer);
        }
    }
}