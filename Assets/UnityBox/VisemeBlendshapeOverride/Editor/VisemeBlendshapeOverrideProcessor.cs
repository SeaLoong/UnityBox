using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

#if NDMF_AVAILABLE
using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverridePlugin))]
#endif

namespace UnityBox.VisemeBlendshapeOverride
{
#if NDMF_AVAILABLE
    public class VisemeBlendshapeOverridePlugin : Plugin<VisemeBlendshapeOverridePlugin>
    {
        public override string QualifiedName => "top.sealoong.unitybox.viseme-blendshape-override";
        public override string DisplayName => "Viseme Blendshape Override";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("VisemeBlendshapeOverride/Apply", ctx =>
                {
                    VisemeBlendshapeOverrideProcessor.ProcessAvatar(ctx.AvatarRootObject);
                });
        }
    }
#endif

    public class VisemeBlendshapeOverridePreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1026;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
#if NDMF_AVAILABLE
            return true;
#else
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
#endif
        }
    }

    public static class VisemeBlendshapeOverrideProcessor
    {
        private sealed class ResolvedBinding
        {
            public VRC_AvatarDescriptor.Viseme Viseme;
            public string BlendshapeName;
            public float Weight;
            public VisemeBlendshapeOverrideComponent.VoiceModulationMode VoiceModulationMode;
            public float VoiceMin;
            public float VoiceMax;
        }

        public static void ProcessAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;

            var existingController = VisemeBlendshapeOverrideUtils.GetExistingFxController(descriptor);
            if (VisemeBlendshapeOverrideUtils.IsGeneratedController(existingController))
                VisemeBlendshapeOverrideUtils.CleanupGeneratedContent(existingController);

            var config = avatarRoot.GetComponent<VisemeBlendshapeOverrideComponent>();
            if (config == null)
            {
                AssetDatabase.SaveAssets();
                return;
            }

            config.EnsureBindings();

            var targetRenderer = config.ResolveTargetRenderer(descriptor);
            if (targetRenderer == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] '{avatarRoot.name}' has no renderer. " +
                    "Assign Renderer or configure Avatar Descriptor > Face Mesh.");
                return;
            }

            if (targetRenderer.sharedMesh == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] Renderer '{targetRenderer.name}' on '{avatarRoot.name}' has no shared mesh.");
                return;
            }

            var relativePath = VisemeBlendshapeOverrideUtils.GetRelativePath(avatarRoot, targetRenderer.gameObject);
            if (relativePath == null)
            {
                Debug.LogWarning(
                    $"[Viseme Blendshape Override] Renderer '{targetRenderer.name}' is not a child of avatar root '{avatarRoot.name}'.");
                return;
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
                return;
            }

            var controller = VisemeBlendshapeOverrideUtils.GetOrCreateEditableFxController(descriptor, avatarRoot);
            if (controller == null)
            {
                Debug.LogWarning($"[Viseme Blendshape Override] Failed to get or create an editable FX controller for '{avatarRoot.name}'.");
                return;
            }

            VisemeBlendshapeOverrideUtils.CleanupGeneratedContent(controller);
            VisemeBlendshapeOverrideUtils.EnsureVisemeParameter(controller);
            if (resolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled))
                VisemeBlendshapeOverrideUtils.EnsureVoiceParameter(controller);

            var useWriteDefaultsOn = VisemeBlendshapeOverrideUtils.ResolveWriteDefaults(
                controller,
                descriptor,
                config.writeDefaultsMode);

            GenerateLayer(
                controller,
                relativePath,
                controlledBlendshapes,
                resolvedBindings,
                useWriteDefaultsOn);

            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(descriptor);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[Viseme Blendshape Override] Generated viseme layer for '{avatarRoot.name}' on renderer '{targetRenderer.name}' " +
                $"({controlledBlendshapes.Count} blendshapes, WD {(useWriteDefaultsOn ? "On" : "Off")}).");
        }

        private static List<ResolvedBinding> ResolveBindings(
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

        private static void GenerateLayer(
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