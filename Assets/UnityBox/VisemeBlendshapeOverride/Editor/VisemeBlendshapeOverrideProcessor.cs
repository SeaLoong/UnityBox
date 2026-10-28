using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.VisemeBlendshapeOverride
{


    public static class VisemeBlendshapeOverrideProcessor
    {
        public sealed class ResolvedBinding
        {
            public VisemeBlendshapeOverrideComponent.Viseme Viseme;
            public string BlendshapeName;
            public float Weight;
            public VisemeBlendshapeOverrideComponent.VoiceModulationMode VoiceModulationMode;
            public float VoiceMin;
            public float VoiceMax;
        }

        public sealed class BuildPlan
        {
            public GameObject AvatarRoot;
            public VisemeBlendshapeOverrideComponent Config;
            public SkinnedMeshRenderer TargetRenderer;
            public string RelativePath;
            public List<string> ControlledBlendshapes;
            public List<ResolvedBinding> ResolvedBindings;
        }

        public static bool TryCreateBuildPlan(GameObject avatarRoot, out BuildPlan plan,
            SkinnedMeshRenderer defaultRenderer = null, IReadOnlyList<string> defaultBlendshapes = null)
        {
            plan = null;
            if (avatarRoot == null)
                return false;

            var config = avatarRoot.GetComponent<VisemeBlendshapeOverrideComponent>();
            if (config == null || !config.enabled)
                return false;

            config.EnsureBindings();

            var targetRenderer = config.ResolveTargetRenderer(defaultRenderer);
            if (targetRenderer == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] '{avatarRoot.name}' has no renderer. " +
                    "Assign the target Renderer or provide a platform default.");
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

            var resolvedBindings = ResolveBindings(config, defaultBlendshapes, targetRenderer);
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
                Config = config,
                TargetRenderer = targetRenderer,
                RelativePath = relativePath,
                ControlledBlendshapes = controlledBlendshapes,
                ResolvedBindings = resolvedBindings,
            };
            return true;
        }

        /// <summary>Writes this tool's generated layer into an explicitly supplied editable controller asset.</summary>
        public static void BuildController(AnimatorController controller, BuildPlan plan,
            IEnumerable<AnimatorController> relatedControllers = null)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Save an editable AnimatorController under Assets/ before building.", nameof(controller));
            var useWriteDefaultsOn = VisemeBlendshapeOverrideUtils.ResolveWriteDefaults(
                controller, relatedControllers, plan.Config.writeDefaultsMode);
            VisemeBlendshapeOverrideUtils.CleanupGeneratedContent(controller);
            VisemeBlendshapeOverrideUtils.EnsureVisemeParameter(controller);
            if (plan.ResolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled))
                VisemeBlendshapeOverrideUtils.EnsureVoiceParameter(controller);
            GenerateFallbackLayer(controller, plan.RelativePath, plan.ControlledBlendshapes,
                plan.ResolvedBindings, useWriteDefaultsOn);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        public static List<ResolvedBinding> ResolveBindings(
            VisemeBlendshapeOverrideComponent config,
            IReadOnlyList<string> defaultBlendshapes,
            SkinnedMeshRenderer targetRenderer)
        {
            var resolved = new List<ResolvedBinding>();
            var mesh = targetRenderer.sharedMesh;

            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                var binding = config.GetBinding(viseme);
                var resolvedName = config.ResolveBlendshapeName(defaultBlendshapes, binding);
                var sourceLabel = binding != null &&
                                  (string.Equals(binding.blendshapeName?.Trim(), VisemeBlendshapeOverrideComponent.NoneBlendshapeValue, StringComparison.Ordinal) ||
                                   !string.IsNullOrWhiteSpace(binding.blendshapeName))
                    ? "override"
                    : "platform defaults";

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

                VisemeBlendshapeOverrideUtils.NormalizeVoiceRange(ref voiceMin, ref voiceMax);

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

                VisemeBlendshapeOverrideUtils.AddSubAsset(controller, state);
                var transition = layer.stateMachine.AddAnyStateTransition(state);
                transition.name = VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme + "_Transition";
                VisemeBlendshapeOverrideUtils.AddSubAsset(controller, transition);
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