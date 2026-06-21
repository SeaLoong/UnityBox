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
                .AfterPlugin("nadena.dev.modular-avatar")
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

            var useWriteDefaultsOn = ResolveVirtualWriteDefaults(fxController, plan.Config);
            GenerateVirtualLayer(fxController, plan.RelativePath, plan.ControlledBlendshapes, plan.ResolvedBindings, useWriteDefaultsOn);

            plan.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            EditorUtility.SetDirty(plan.Config);
            EditorUtility.SetDirty(plan.Descriptor);
        }

        private static void EnsureVirtualVisemeParameter(VirtualAnimatorController controller)
        {
            EnsureVirtualParameter(controller, VisemeBlendshapeOverrideUtils.BuiltInVisemeParameter, AnimatorControllerParameterType.Int, defaultInt: 0);
        }

        private static void EnsureVirtualVoiceParameter(VirtualAnimatorController controller)
        {
            EnsureVirtualParameter(controller, VisemeBlendshapeOverrideUtils.BuiltInVoiceParameter, AnimatorControllerParameterType.Float, defaultFloat: 0f);
        }

        private static void EnsureVirtualParameter(
            VirtualAnimatorController controller,
            string name,
            AnimatorControllerParameterType type,
            bool defaultBool = false,
            int defaultInt = 0,
            float defaultFloat = 0f)
        {
            var parameters = controller.Parameters;
            if (parameters.TryGetValue(name, out var existingParameter) && existingParameter.type == type)
                return;

            controller.Parameters = parameters.SetItem(name, new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultBool = defaultBool,
                defaultInt = defaultInt,
                defaultFloat = defaultFloat,
            });
        }

        private static bool ResolveVirtualWriteDefaults(
            VirtualAnimatorController controller,
            VisemeBlendshapeOverrideComponent config)
        {
            switch (config.writeDefaultsMode)
            {
                case VisemeBlendshapeOverrideComponent.WriteDefaultsMode.On:
                    return true;
                case VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off:
                    return false;
            }

            foreach (var layer in controller.Layers)
            {
                if (layer.Name == VisemeBlendshapeOverrideUtils.GeneratedLayerName)
                    continue;
                if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive)
                    continue;
                if (layer.StateMachine == null)
                    continue;
                if (IsWriteDefaultsRequiredLayer(layer))
                    continue;
                if (HasWriteDefaultsOffState(layer.StateMachine))
                    return false;
            }

            return true;
        }

        private static bool IsWriteDefaultsRequiredLayer(VirtualLayer layer)
        {
            if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive)
                return true;

            var stateMachine = layer.StateMachine;
            if (stateMachine == null)
                return false;
            if (stateMachine.StateMachines.Count != 0)
                return false;
            if (stateMachine.States.Count != 1)
                return false;
            if (stateMachine.AnyStateTransitions.Count != 0)
                return false;

            var defaultState = stateMachine.DefaultState;
            if (defaultState == null)
                return false;
            if (defaultState.Transitions.Count != 0)
                return false;
            if (!(defaultState.Motion is VirtualBlendTree blendTree))
                return false;

            return HasDirectBlendTree(blendTree);
        }

        private static bool HasDirectBlendTree(VirtualBlendTree blendTree)
        {
            if (blendTree.BlendType == BlendTreeType.Direct)
                return true;

            foreach (var child in blendTree.Children)
            {
                if (child.Motion is VirtualBlendTree childBlendTree && HasDirectBlendTree(childBlendTree))
                    return true;
            }

            return false;
        }

        private static bool HasWriteDefaultsOffState(VirtualStateMachine stateMachine)
        {
            foreach (var state in stateMachine.AllStates())
            {
                if (state.Motion == null)
                    continue;
                if (state.Motion is VirtualBlendTree blendTree && blendTree.BlendType == BlendTreeType.Direct)
                    continue;
                if (!state.WriteDefaultValues)
                    return true;
            }

            return false;
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
