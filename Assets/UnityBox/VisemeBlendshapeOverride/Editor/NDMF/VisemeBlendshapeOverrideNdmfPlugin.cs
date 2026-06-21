using System;
using System.Collections.Generic;
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
            IReadOnlyCollection<string> controlledBlendshapes,
            IReadOnlyCollection<VisemeBlendshapeOverrideProcessor.ResolvedBinding> resolvedBindings,
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
            IEnumerable<string> controlledBlendshapes,
            string activeBlendshape,
            float activeWeight)
        {
            var clip = VirtualClip.Create(name);
            foreach (var blendshape in controlledBlendshapes)
            {
                var value = string.Equals(blendshape, activeBlendshape, StringComparison.Ordinal)
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
}
using System;
using System.Collections.Generic;
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
            IReadOnlyCollection<string> controlledBlendshapes,
            IEnumerable<VisemeBlendshapeOverrideProcessor.ResolvedBinding> resolvedBindings,
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
            IEnumerable<string> controlledBlendshapes,
            string activeBlendshape,
            float activeWeight)
        {
            var clip = VirtualClip.Create(name);
            foreach (var blendshape in controlledBlendshapes)
            {
                var value = string.Equals(blendshape, activeBlendshape, StringComparison.Ordinal)
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
}
