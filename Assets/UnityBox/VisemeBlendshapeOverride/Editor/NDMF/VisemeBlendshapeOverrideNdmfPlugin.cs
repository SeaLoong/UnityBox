using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

[assembly: ExportsPlugin(typeof(UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverridePlugin))]

namespace UnityBox.VisemeBlendshapeOverride
{
    public class VisemeBlendshapeOverridePlugin : Plugin<VisemeBlendshapeOverridePlugin>
    {
        public override string QualifiedName => "top.sealoong.unitybox.viseme-blendshape-override";
        public override string DisplayName => "Viseme Blendshape Override";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq => seq.Run("VisemeBlendshapeOverride/Apply", ctx =>
                {
                    VisemeBlendshapeOverrideNdmfProcessor.ProcessAvatar(ctx);
                }));
        }
    }

    internal static class VisemeBlendshapeOverrideNdmfProcessor
    {
        public static void ProcessAvatar(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null)
                return;

            if (!VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(context.AvatarRootObject, out var plan))
                return;

            var animatorServices = context.Extension<AnimatorServicesContext>();
            if (!animatorServices.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fxController) || fxController == null)
            {
                Debug.LogWarning($"[Viseme Blendshape Override] Failed to access virtual FX controller for '{plan.AvatarRoot.name}'.");
                return;
            }

            fxController.RemoveLayers(layer => layer.Name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            EnsureVirtualVisemeParameter(fxController);

            if (plan.ResolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled))
                EnsureVirtualVoiceParameter(fxController);

            var useWriteDefaultsOn = ResolveVirtualWriteDefaults(plan.Descriptor, plan.Config);
            GenerateVirtualLayer(fxController, plan.RelativePath, plan.ControlledBlendshapes, plan.ResolvedBindings, useWriteDefaultsOn);

            plan.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            EditorUtility.SetDirty(plan.Config);
            EditorUtility.SetDirty(plan.Descriptor);
        }

        private static void EnsureVirtualVisemeParameter(VirtualAnimatorController controller)
        {
            var parameters = controller.Parameters;
            if (parameters.ContainsKey(VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter))
                return;

            parameters = parameters.Add(
                VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter,
                new AnimatorControllerParameter
                {
                    name = VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter,
                    type = AnimatorControllerParameterType.Int,
                    defaultInt = 0,
                });
            controller.Parameters = parameters;
        }

        private static void EnsureVirtualVoiceParameter(VirtualAnimatorController controller)
        {
            var parameters = controller.Parameters;
            if (parameters.ContainsKey(VisemeBlendshapeOverrideUtils.BuiltInVoiceParameter))
                return;

            parameters = parameters.Add(
                VisemeBlendshapeOverrideUtils.BuiltInVoiceParameter,
                new AnimatorControllerParameter
                {
                    name = VisemeBlendshapeOverrideUtils.BuiltInVoiceParameter,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f,
                });
            controller.Parameters = parameters;
        }

        private static bool ResolveVirtualWriteDefaults(
            VRCAvatarDescriptor descriptor,
            VisemeBlendshapeOverrideComponent config)
        {
            var currentController = VisemeBlendshapeOverrideUtils.GetExistingFxController(descriptor);
            return currentController != null
                ? VisemeBlendshapeOverrideUtils.ResolveWriteDefaults(currentController, descriptor, config.writeDefaultsMode)
                : config.writeDefaultsMode != VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off;
        }

        private static void GenerateVirtualLayer(
            VirtualAnimatorController controller,
            string relativePath,
            System.Collections.Generic.IReadOnlyCollection<string> controlledBlendshapes,
            System.Collections.Generic.IEnumerable<VisemeBlendshapeOverrideProcessor.ResolvedBinding> resolvedBindings,
            bool useWriteDefaultsOn)
        {
            var bindingsByViseme = resolvedBindings.ToDictionary(binding => binding.Viseme, binding => binding);
            var layer = controller.AddLayer(new LayerPriority(0), VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            layer.DefaultWeight = 1f;
            layer.BlendingMode = AnimatorLayerBlendingMode.Override;

            var useAnyVoiceModulation = resolvedBindings.Any(binding => binding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled);
            VirtualClip zeroClip = null;
            if (useAnyVoiceModulation)
            {
                zeroClip = CreateVirtualBlendshapeClip(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + "VoiceZero_Clip",
                    relativePath,
                    controlledBlendshapes,
                    null,
                    0f);
            }

            var row = 0;
            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                bindingsByViseme.TryGetValue(viseme, out var resolvedBinding);

                var clip = CreateVirtualBlendshapeClip(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme + "_Clip",
                    relativePath,
                    controlledBlendshapes,
                    resolvedBinding?.BlendshapeName,
                    resolvedBinding?.Weight ?? 0f);

                VirtualMotion motion = clip;
                if (resolvedBinding != null &&
                    resolvedBinding.VoiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled)
                {
                    motion = CreateVirtualVoiceBlendTree(
                        VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme + "_VoiceTree",
                        zeroClip,
                        clip,
                        resolvedBinding.VoiceMin,
                        resolvedBinding.VoiceMax);
                }

                var state = layer.StateMachine.AddState(
                    VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme,
                    motion,
                    new Vector3(280f, row * 70f, 0f));
                state.WriteDefaultValues = useWriteDefaultsOn;

                var transition = VirtualStateTransition.Create();
                transition.SetDestination(state);
                transition.CanTransitionToSelf = false;
                transition.Duration = 0f;
                transition.HasFixedDuration = true;
                transition.ExitTime = null;
                transition.Conditions = ImmutableList.Create(new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Equals,
                    threshold = (float)(int)viseme,
                    parameter = VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter,
                });

                layer.StateMachine.AnyStateTransitions = layer.StateMachine.AnyStateTransitions.Add(transition);

                if ((int)viseme == 0 || layer.StateMachine.DefaultState == null)
                    layer.StateMachine.DefaultState = state;

                row++;
            }
        }

        private static VirtualClip CreateVirtualBlendshapeClip(
            string name,
            string relativePath,
            System.Collections.Generic.IEnumerable<string> controlledBlendshapes,
            string activeBlendshape,
            float activeWeight)
        {
            var clip = VirtualClip.Create(name);
            foreach (var blendshape in controlledBlendshapes)
            {
                var value = string.Equals(blendshape, activeBlendshape, System.StringComparison.Ordinal)
                    ? Mathf.Clamp(activeWeight, 0f, 100f)
                    : 0f;
                clip.SetFloatCurve(relativePath, typeof(SkinnedMeshRenderer), $"blendShape.{blendshape}", AnimationCurve.Constant(0f, 1f / 60f, value));
            }

            return clip;
        }

        private static VirtualBlendTree CreateVirtualVoiceBlendTree(
            string name,
            VirtualClip zeroClip,
            VirtualClip activeClip,
            float voiceMin,
            float voiceMax)
        {
            var clampedMin = Mathf.Clamp01(voiceMin);
            var clampedMax = Mathf.Clamp01(voiceMax);
            if (clampedMax <= clampedMin)
                clampedMax = Mathf.Min(1f, clampedMin + 0.001f);

            var tree = VirtualBlendTree.Create(name);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = VisemeBlendshapeOverrideUtils.BuiltInVoiceParameter;
            tree.UseAutomaticThresholds = false;
            tree.Children = ImmutableList.Create(
                new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = zeroClip,
                    Threshold = clampedMin,
                },
                new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = activeClip,
                    Threshold = clampedMax,
                });
            return tree;
        }
    }
#endif

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
#if NDMF_AVAILABLE
            return;
#else
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
#endif
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

#if !NDMF_AVAILABLE
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
#endif
    }
}