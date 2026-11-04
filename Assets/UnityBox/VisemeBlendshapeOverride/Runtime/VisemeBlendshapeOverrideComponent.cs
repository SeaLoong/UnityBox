using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityBox.VisemeBlendshapeOverride
{
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Viseme Blendshape Override")]
#endif
    [DisallowMultipleComponent]
    public class VisemeBlendshapeOverrideComponent : MonoBehaviour
#if UNITYBOX_VRC_BASE
        , VRC.SDKBase.IEditorOnly
#endif
    {
        // Numeric values match the original serialized phoneme IDs. This is a Unity-owned data model.
        public enum Viseme
        {
            sil = 0, PP = 1, FF = 2, TH = 3, DD = 4, kk = 5, CH = 6, SS = 7,
            nn = 8, RR = 9, aa = 10, E = 11, ih = 12, oh = 13, ou = 14,
        }

        public const string NoneBlendshapeValue = "__NONE__";

        [Serializable]
        public class VisemeBinding : ISerializationCallbackReceiver
        {
            [HideInInspector]
            public Viseme viseme;

            [Tooltip("Blendshape used for this viseme. Leave it empty to use an optional platform default mapping.")]
            public string blendshapeName = string.Empty;

            [FormerlySerializedAs("overrideGlobalVoiceSettings")]
            [Tooltip("Use custom settings for this viseme.")]
            public bool useCustomSettings = false;

            [Range(0f, 100f)]
            [Tooltip("Blendshape weight used when custom settings are enabled for this viseme.")]
            public float weight = 100f;

            [SerializeField, HideInInspector, FormerlySerializedAs("voiceModulationMode")]
            private int legacyVoiceMode = -1;

            [Tooltip("Voice mode for this viseme when custom settings are enabled.")]
            public VoiceModeOverride voiceMode = VoiceModeOverride.Global;

            public enum VoiceModeOverride
            {
                Global,
                Disabled,
                Linear,
            }

            public void OnBeforeSerialize() { }

            public void OnAfterDeserialize()
            {
                if (legacyVoiceMode < 0) return;
                voiceMode = legacyVoiceMode == 0 ? VoiceModeOverride.Disabled :
                    legacyVoiceMode == 1 ? VoiceModeOverride.Linear : VoiceModeOverride.Global;
                legacyVoiceMode = -1;
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

        [Tooltip("Scale the configured viseme weight by the Animator Voice float parameter.")]
        public VoiceModulationMode voiceModulationMode = VoiceModulationMode.Linear;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or below this threshold outputs 0 intensity when voice modulation is enabled.")]
        public float voiceMin = 0f;

        [Range(0f, 1f)]
        [Tooltip("Voice value at or above this threshold outputs the full configured viseme weight.")]
        public float voiceMax = 1f;

        [Tooltip("Per-viseme settings.")]
        public List<VisemeBinding> bindings = new List<VisemeBinding>();

        private static readonly Viseme[] SupportedVisemes =
        {
            Viseme.sil, Viseme.PP, Viseme.FF, Viseme.TH, Viseme.DD, Viseme.kk, Viseme.CH, Viseme.SS,
            Viseme.nn, Viseme.RR, Viseme.aa, Viseme.E, Viseme.ih, Viseme.oh, Viseme.ou,
        };

        public void EnsureBindings()
        {
            var ordered = bindings != null && bindings.Count == SupportedVisemes.Length;
            if (ordered)
                for (var index = 0; index < SupportedVisemes.Length; index++)
                    if (bindings[index] == null || bindings[index].viseme != SupportedVisemes[index])
                    {
                        ordered = false;
                        break;
                    }
            if (!ordered)
            {
                var existing = new Dictionary<Viseme, VisemeBinding>();
                if (bindings != null)
                    foreach (var binding in bindings)
                        if (binding != null && !existing.ContainsKey(binding.viseme)) existing.Add(binding.viseme, binding);
                bindings = new List<VisemeBinding>(SupportedVisemes.Length);
                foreach (var viseme in SupportedVisemes)
                    bindings.Add(existing.TryGetValue(viseme, out var binding) ? binding : new VisemeBinding { viseme = viseme });
            }

            foreach (var binding in bindings)
            {
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
        }

        public VisemeBinding GetBinding(Viseme viseme)
        {
            var index = (int)viseme;
            if ((uint)index >= SupportedVisemes.Length) return null;
            if (bindings == null || bindings.Count != SupportedVisemes.Length ||
                bindings[index] == null || bindings[index].viseme != viseme) EnsureBindings();
            return bindings[index];
        }

        public SkinnedMeshRenderer ResolveTargetRenderer(SkinnedMeshRenderer fallback = null)
        {
            return targetRenderer != null ? targetRenderer : fallback;
        }

        public string ResolveBlendshapeName(IReadOnlyList<string> defaults, VisemeBinding binding)
        {
            if (binding == null)
                return string.Empty;

            var storedName = binding.blendshapeName?.Trim() ?? string.Empty;
            if (string.Equals(storedName, NoneBlendshapeValue, StringComparison.Ordinal))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(storedName))
                return storedName;

            return GetDefaultBlendshapeName(defaults, binding.viseme);
        }

        public static string GetDefaultBlendshapeName(
            IReadOnlyList<string> defaults,
            Viseme viseme)
        {
            if (defaults == null)
                return string.Empty;

            var index = (int)viseme;
            if (index < 0 || index >= defaults.Count)
                return string.Empty;

            return defaults[index] ?? string.Empty;
        }

        public static List<Viseme> GetSupportedVisemes()
        {
            return new List<Viseme>(SupportedVisemes);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            InitializeEditorDefaults();
        }

        private void OnValidate()
        {
            InitializeEditorDefaults();

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
            InitializeEditorDefaults();
        }

        private void InitializeEditorDefaults()
        {
            EnsureBindings();
            // Platform defaults are resolved by optional editor adapters; never copy them into explicit overrides.
        }


#endif

        private static VoiceModulationMode SanitizeVoiceMode(VoiceModulationMode mode)
        {
            return (uint)mode <= (uint)VoiceModulationMode.Linear ? mode : VoiceModulationMode.Linear;
        }

        private static VisemeBinding.VoiceModeOverride SanitizeVoiceModeOverride(VisemeBinding.VoiceModeOverride mode)
        {
            return (uint)mode <= (uint)VisemeBinding.VoiceModeOverride.Linear
                ? mode : VisemeBinding.VoiceModeOverride.Global;
        }
    }
}