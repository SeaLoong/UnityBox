using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
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
        public const string NoneBlendshapeValue = "__NONE__";

        [Serializable]
        public class VisemeBinding
        {
            [HideInInspector]
            public VRC_AvatarDescriptor.Viseme viseme;

            [Tooltip("Blendshape used for this viseme. Leave it empty to use the Avatar Descriptor mapping.")]
            public string blendshapeName = string.Empty;

            [FormerlySerializedAs("overrideGlobalVoiceSettings")]
            [Tooltip("Use custom settings for this viseme.")]
            public bool useCustomSettings = false;

            [Range(0f, 100f)]
            [Tooltip("Blendshape weight written while this viseme is active.")]
            public float weight = 100f;

            [FormerlySerializedAs("voiceModulationMode")]
            [Tooltip("Voice mode for this viseme when custom settings are enabled.")]
            public VoiceModeOverride voiceMode = VoiceModeOverride.Global;

            public enum VoiceModeOverride
            {
                Global,
                Disabled,
                Linear,
            }

            [Range(0f, 1f)]
            [Tooltip("Voice values at or below this threshold output 0 intensity for this viseme.")]
            public float voiceMin = 0f;

            [Range(0f, 1f)]
            [Tooltip("Voice values at or above this threshold output the full configured weight for this viseme.")]
            public float voiceMax = 1f;
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

        [Tooltip("Target skinned mesh renderer.")]
        public SkinnedMeshRenderer targetRenderer;

        [Tooltip("Write Defaults mode used by the generated FX layer.")]
        public WriteDefaultsMode writeDefaultsMode = WriteDefaultsMode.Auto;

        [Range(0f, 100f)]
        [Tooltip("Default weight used when a viseme does not use custom settings.")]
        public float globalWeight = 100f;

        [Tooltip("Scale the configured viseme weight by VRChat's built-in Voice parameter.")]
        public VoiceModulationMode voiceModulationMode = VoiceModulationMode.Linear;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or below this threshold outputs 0 intensity when voice modulation is enabled.")]
        public float voiceMin = 0f;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or above this threshold outputs the full configured viseme weight.")]
        public float voiceMax = 1f;

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
                        blendshapeName = string.Empty,
                        weight = 100f,
                        useCustomSettings = false,
                        voiceMode = VisemeBinding.VoiceModeOverride.Global,
                        voiceMin = 0f,
                        voiceMax = 1f,
                    };
                }
                else
                {
                    binding.viseme = viseme;
                    binding.blendshapeName ??= string.Empty;
                    binding.weight = Mathf.Clamp(binding.weight, 0f, 100f);
                    binding.voiceMode = SanitizeVoiceModeOverride(binding.voiceMode);
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

            var storedName = binding.blendshapeName?.Trim() ?? string.Empty;
            if (string.Equals(storedName, NoneBlendshapeValue, StringComparison.Ordinal))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(storedName))
                return storedName;

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

#if UNITY_EDITOR
        private void Reset()
        {
            InitializeDefaultsFromDescriptor();
        }

        private void OnValidate()
        {
            InitializeDefaultsFromDescriptor();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.blendshapeName ??= string.Empty;
                binding.weight = Mathf.Clamp(binding.weight, 0f, 100f);
                binding.voiceMode = SanitizeVoiceModeOverride(binding.voiceMode);
                binding.voiceMin = Mathf.Clamp01(binding.voiceMin);
                binding.voiceMax = Mathf.Clamp01(binding.voiceMax);
                if (binding.voiceMax <= binding.voiceMin)
                {
                    binding.voiceMax = Mathf.Min(1f, binding.voiceMin + 0.001f);
                    binding.voiceMin = Mathf.Max(0f, binding.voiceMax - 0.001f);
                }
            }

            globalWeight = Mathf.Clamp(globalWeight, 0f, 100f);
            voiceModulationMode = SanitizeVoiceMode(voiceModulationMode);
            voiceMin = Mathf.Clamp01(voiceMin);
            voiceMax = Mathf.Clamp01(voiceMax);
            if (voiceMax <= voiceMin)
            {
                voiceMax = Mathf.Min(1f, voiceMin + 0.001f);
                voiceMin = Mathf.Max(0f, voiceMax - 0.001f);
            }
        }

        public void EnsureEditorDefaults()
        {
            InitializeDefaultsFromDescriptor();
        }

        private void InitializeDefaultsFromDescriptor()
        {
            EnsureBindings();

            var descriptor = GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null && targetRenderer == null)
                targetRenderer = ResolveDefaultRenderer(descriptor);

            if (descriptor == null)
                return;

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(binding.blendshapeName))
                    continue;

                var descriptorBlendshape = GetDescriptorBlendshapeName(descriptor, binding.viseme);
                if (!string.IsNullOrWhiteSpace(descriptorBlendshape))
                    binding.blendshapeName = descriptorBlendshape;
            }
        }

        private SkinnedMeshRenderer ResolveDefaultRenderer(VRCAvatarDescriptor descriptor)
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
                return null;

            var bodyRenderer = renderers.FirstOrDefault(renderer =>
                string.Equals(renderer.name, "Body", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(renderer.gameObject.name, "Body", StringComparison.OrdinalIgnoreCase));

            if (bodyRenderer != null)
                return bodyRenderer;

            if (descriptor != null && descriptor.VisemeSkinnedMesh != null)
                return descriptor.VisemeSkinnedMesh;

            return renderers.FirstOrDefault();
        }

        private static VoiceModulationMode SanitizeVoiceMode(VoiceModulationMode mode)
        {
            return Enum.IsDefined(typeof(VoiceModulationMode), mode)
                ? mode
                : VoiceModulationMode.Linear;
        }

        private static VisemeBinding.VoiceModeOverride SanitizeVoiceModeOverride(VisemeBinding.VoiceModeOverride mode)
        {
            return Enum.IsDefined(typeof(VisemeBinding.VoiceModeOverride), mode)
                ? mode
                : VisemeBinding.VoiceModeOverride.Global;
        }
#endif
    }
}