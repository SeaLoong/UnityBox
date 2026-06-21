using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.VisemeBlendshapeOverride
{
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Viseme Blendshape Override")]
#endif
    [DisallowMultipleComponent]
    public class VisemeBlendshapeOverrideComponent : MonoBehaviour, IEditorOnly
    {
        [Serializable]
        public class VisemeBinding
        {
            [HideInInspector]
            public VRC_AvatarDescriptor.Viseme viseme;

            [Tooltip("Follow the blendshape currently configured on the Avatar Descriptor for this viseme.")]
            public bool useAvatarDescriptorBlendshape = true;

            [Tooltip("When not following the Avatar Descriptor, drive this blendshape name on the target renderer.")]
            public string blendshapeName = string.Empty;

            [Range(0f, 100f)]
            [Tooltip("Blendshape weight written while this viseme is active.")]
            public float weight = 100f;

            [Tooltip("Use the component-level Voice Min / Voice Max values for this viseme.")]
            public bool useGlobalVoiceRange = true;

            [Range(0f, 1f)]
            [Tooltip("When not using the global range, Voice values at or below this threshold output 0 intensity for this viseme.")]
            public float voiceMin = 0.05f;

            [Range(0f, 1f)]
            [Tooltip("When not using the global range, Voice values at or above this threshold output the full configured weight for this viseme.")]
            public float voiceMax = 0.15f;
        }

        public enum WriteDefaultsMode
        {
            Auto,
            On,
            Off,
        }

        public enum VoiceModulationMode
        {
            Disabled,
            Linear,
        }

        public enum WeightPreset
        {
            Conservative,
            Balanced,
            Expressive,
        }

        [Tooltip("Optional override face mesh. Leave empty to reuse Avatar Descriptor > Face Mesh.")]
        public SkinnedMeshRenderer targetRenderer;

        [Tooltip("Write Defaults mode used by the generated FX layer.")]
        public WriteDefaultsMode writeDefaultsMode = WriteDefaultsMode.Auto;

        [Tooltip("Scale the configured viseme weight by VRChat's built-in Voice parameter.")]
        public VoiceModulationMode voiceModulationMode = VoiceModulationMode.Linear;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or below this threshold outputs 0 intensity when voice modulation is enabled.")]
        public float voiceMin = 0.05f;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or above this threshold outputs the full configured viseme weight.")]
        public float voiceMax = 0.15f;

        [Tooltip("Per-viseme settings. By default each entry reuses the Avatar Descriptor mapping and only overrides the output weight.")]
        public List<VisemeBinding> bindings = new List<VisemeBinding>();

        public void EnsureBindings()
        {
            if (bindings == null)
                bindings = new List<VisemeBinding>();

            var existing = bindings
                .Where(binding => binding != null)
                .GroupBy(binding => binding.viseme)
                .ToDictionary(group => group.Key, group => group.First());

            var orderedBindings = new List<VisemeBinding>();
            foreach (var viseme in GetSupportedVisemes())
            {
                if (!existing.TryGetValue(viseme, out var binding) || binding == null)
                {
                    binding = new VisemeBinding
                    {
                        viseme = viseme,
                        useAvatarDescriptorBlendshape = true,
                        blendshapeName = string.Empty,
                        weight = 100f,
                        useGlobalVoiceRange = true,
                        voiceMin = 0.05f,
                        voiceMax = 0.15f,
                    };
                }
                else
                {
                    binding.viseme = viseme;
                    binding.blendshapeName ??= string.Empty;
                    binding.weight = Mathf.Clamp(binding.weight, 0f, 100f);
                    binding.voiceMin = Mathf.Clamp01(binding.voiceMin);
                    binding.voiceMax = Mathf.Clamp01(binding.voiceMax);
                    if (binding.voiceMax <= binding.voiceMin)
                    {
                        binding.voiceMax = Mathf.Min(1f, binding.voiceMin + 0.001f);
                        binding.voiceMin = Mathf.Max(0f, binding.voiceMax - 0.001f);
                    }
                }

                orderedBindings.Add(binding);
            }

            bindings = orderedBindings;
        }

        public VisemeBinding GetBinding(VRC_AvatarDescriptor.Viseme viseme)
        {
            EnsureBindings();
            return bindings.FirstOrDefault(binding => binding.viseme == viseme);
        }

        public SkinnedMeshRenderer ResolveTargetRenderer(VRCAvatarDescriptor descriptor)
        {
            return targetRenderer != null ? targetRenderer : descriptor != null ? descriptor.VisemeSkinnedMesh : null;
        }

        public string ResolveBlendshapeName(VRCAvatarDescriptor descriptor, VisemeBinding binding)
        {
            if (binding == null)
                return string.Empty;

            if (!binding.useAvatarDescriptorBlendshape)
                return binding.blendshapeName?.Trim() ?? string.Empty;

            return GetDescriptorBlendshapeName(descriptor, binding.viseme);
        }

        public static string GetDescriptorBlendshapeName(
            VRCAvatarDescriptor descriptor,
            VRC_AvatarDescriptor.Viseme viseme)
        {
            if (descriptor == null || descriptor.VisemeBlendShapes == null)
                return string.Empty;

            var index = (int)viseme;
            if (index < 0 || index >= descriptor.VisemeBlendShapes.Length)
                return string.Empty;

            return descriptor.VisemeBlendShapes[index] ?? string.Empty;
        }

        public static List<VRC_AvatarDescriptor.Viseme> GetSupportedVisemes()
        {
            return Enum.GetValues(typeof(VRC_AvatarDescriptor.Viseme))
                .Cast<VRC_AvatarDescriptor.Viseme>()
                .Where(viseme => viseme != VRC_AvatarDescriptor.Viseme.Count)
                .OrderBy(viseme => (int)viseme)
                .ToList();
        }

        private static float GetBalancedPresetWeight(VRC_AvatarDescriptor.Viseme viseme)
        {
            switch (viseme.ToString())
            {
                case "sil":
                case "silence":
                    return 0f;

                case "PP":
                    return 18f;
                case "FF":
                    return 22f;
                case "TH":
                    return 24f;
                case "DD":
                    return 28f;
                case "kk":
                    return 28f;
                case "CH":
                    return 32f;
                case "SS":
                    return 20f;
                case "nn":
                    return 22f;
                case "RR":
                    return 30f;
                case "aa":
                    return 55f;
                case "E":
                    return 36f;
                case "ih":
                case "I":
                    return 32f;
                case "oh":
                case "O":
                    return 45f;
                case "ou":
                case "U":
                    return 40f;
                case "laugh":
                    return 65f;
                default:
                    return 35f;
            }
        }

        private static float GetPresetWeight(VRC_AvatarDescriptor.Viseme viseme, WeightPreset preset)
        {
            var balanced = GetBalancedPresetWeight(viseme);
            switch (preset)
            {
                case WeightPreset.Conservative:
                    return Mathf.Clamp(balanced * 0.75f, 0f, 100f);
                case WeightPreset.Expressive:
                    return Mathf.Clamp(balanced * 1.25f, 0f, 100f);
                default:
                    return balanced;
            }
        }

#if UNITY_EDITOR
        public void CopyDescriptorMappingsToOverrides()
        {
            EnsureBindings();

            var descriptor = GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;

            if (targetRenderer == null && descriptor.VisemeSkinnedMesh != null)
                targetRenderer = descriptor.VisemeSkinnedMesh;

            foreach (var binding in bindings)
            {
                binding.useAvatarDescriptorBlendshape = false;
                binding.blendshapeName = GetDescriptorBlendshapeName(descriptor, binding.viseme);
            }
        }

        public void FollowAvatarDescriptorMappings()
        {
            EnsureBindings();

            foreach (var binding in bindings)
            {
                binding.useAvatarDescriptorBlendshape = true;
                binding.blendshapeName = string.Empty;
            }

            var descriptor = GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null && targetRenderer == descriptor.VisemeSkinnedMesh)
                targetRenderer = null;
        }

        public void SetAllWeights(float weight)
        {
            EnsureBindings();

            var clamped = Mathf.Clamp(weight, 0f, 100f);
            foreach (var binding in bindings)
                binding.weight = clamped;
        }

        public void ApplyWeightPreset(WeightPreset preset)
        {
            EnsureBindings();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.weight = GetPresetWeight(binding.viseme, preset);
            }
        }

        public void SetAllUseGlobalVoiceRange(bool useGlobalVoiceRange)
        {
            EnsureBindings();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.useGlobalVoiceRange = useGlobalVoiceRange;
            }
        }

        public void CopyGlobalVoiceRangeToAllBindings()
        {
            EnsureBindings();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.voiceMin = voiceMin;
                binding.voiceMax = voiceMax;
            }
        }

        private void Reset()
        {
            EnsureBindings();
        }

        private void OnValidate()
        {
            EnsureBindings();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.blendshapeName ??= string.Empty;
                binding.weight = Mathf.Clamp(binding.weight, 0f, 100f);
                binding.voiceMin = Mathf.Clamp01(binding.voiceMin);
                binding.voiceMax = Mathf.Clamp01(binding.voiceMax);
                if (binding.voiceMax <= binding.voiceMin)
                {
                    binding.voiceMax = Mathf.Min(1f, binding.voiceMin + 0.001f);
                    binding.voiceMin = Mathf.Max(0f, binding.voiceMax - 0.001f);
                }
            }

            voiceMin = Mathf.Clamp01(voiceMin);
            voiceMax = Mathf.Clamp01(voiceMax);
            if (voiceMax <= voiceMin)
            {
                voiceMax = Mathf.Min(1f, voiceMin + 0.001f);
                voiceMin = Mathf.Max(0f, voiceMax - 0.001f);
            }
        }
#endif
    }
}