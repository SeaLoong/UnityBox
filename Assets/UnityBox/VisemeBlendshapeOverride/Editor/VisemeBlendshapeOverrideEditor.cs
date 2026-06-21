using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.VisemeBlendshapeOverride
{
    [CustomEditor(typeof(VisemeBlendshapeOverrideComponent))]
    public class VisemeBlendshapeOverrideEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetRenderer;
        private SerializedProperty _writeDefaultsMode;
        private SerializedProperty _voiceModulationMode;
        private SerializedProperty _voiceMin;
        private SerializedProperty _voiceMax;
        private SerializedProperty _bindings;

        private float _batchWeight = 100f;

        private void OnEnable()
        {
            _targetRenderer = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.targetRenderer));
            _writeDefaultsMode = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.writeDefaultsMode));
            _voiceModulationMode = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceModulationMode));
            _voiceMin = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceMin));
            _voiceMax = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceMax));
            _bindings = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.bindings));

            var component = (VisemeBlendshapeOverrideComponent)target;
            component.EnsureBindings();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var component = (VisemeBlendshapeOverrideComponent)target;
            component.EnsureBindings();

            var descriptor = component.GetComponent<VRCAvatarDescriptor>();
            var resolvedRenderer = component.ResolveTargetRenderer(descriptor);
            var blendshapeOptions = GetBlendshapeOptions(resolvedRenderer);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Viseme Blendshape Override", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Drive custom viseme blendshape weights from VRChat's built-in Viseme parameter.", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "VRChat 官方文档说明：内建 Animator 参数 `Viseme` 会在所有口型模式中更新。\n" +
                "本组件会在构建时把 Avatar Descriptor 的 Lip Sync 模式切到 `VisemeParameterOnly`，" +
                "然后用一层 FX Animator 按 `Viseme` 参数驱动自定义 BlendShape 权重。",
                MessageType.Info);

            EditorGUILayout.PropertyField(
                _targetRenderer,
                new GUIContent("Target Renderer", "Leave empty to reuse Avatar Descriptor > Face Mesh."));
            EditorGUILayout.PropertyField(_writeDefaultsMode);
            DrawVoiceSection();

            if (_targetRenderer.objectReferenceValue == null)
            {
                var rendererName = resolvedRenderer != null ? resolvedRenderer.name : "<none>";
                EditorGUILayout.LabelField("Effective Renderer", rendererName, EditorStyles.miniLabel);
            }

            DrawToolbar(component);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Per-Viseme Settings", EditorStyles.boldLabel);

            for (var i = 0; i < _bindings.arraySize; i++)
                DrawBinding(component, descriptor, resolvedRenderer, blendshapeOptions, _bindings.GetArrayElementAtIndex(i));

            DrawValidationSummary(component, descriptor, resolvedRenderer);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToolbar(VisemeBlendshapeOverrideComponent component)
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制当前 Descriptor 映射"))
                {
                    Undo.RecordObject(component, "Copy viseme descriptor mappings");
                    component.CopyDescriptorMappingsToOverrides();
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }

                if (GUILayout.Button("恢复为跟随 Descriptor"))
                {
                    Undo.RecordObject(component, "Follow viseme descriptor mappings");
                    component.FollowAvatarDescriptorMappings();
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("权重预设", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保守"))
                {
                    ApplyPreset(component, VisemeBlendshapeOverrideComponent.WeightPreset.Conservative);
                }

                if (GUILayout.Button("均衡"))
                {
                    ApplyPreset(component, VisemeBlendshapeOverrideComponent.WeightPreset.Balanced);
                }

                if (GUILayout.Button("强调"))
                {
                    ApplyPreset(component, VisemeBlendshapeOverrideComponent.WeightPreset.Expressive);
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("响应区间批量工具", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部使用全局区间"))
                {
                    Undo.RecordObject(component, "Use global voice range for all visemes");
                    component.SetAllUseGlobalVoiceRange(true);
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }

                if (GUILayout.Button("全部切换为独立区间"))
                {
                    Undo.RecordObject(component, "Use local voice range for all visemes");
                    component.SetAllUseGlobalVoiceRange(false);
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("把当前全局区间写入全部条目"))
                {
                    Undo.RecordObject(component, "Copy global voice range to all visemes");
                    component.CopyGlobalVoiceRangeToAllBindings();
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _batchWeight = EditorGUILayout.Slider("Batch Weight", _batchWeight, 0f, 100f);
                if (GUILayout.Button("应用到全部", GUILayout.Width(90f)))
                {
                    Undo.RecordObject(component, "Set all viseme weights");
                    component.SetAllWeights(_batchWeight);
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }
            }
        }

        private void DrawVoiceSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Output Intensity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _voiceModulationMode,
                new GUIContent("Voice Modulation", "Optionally scale the configured viseme weight by VRChat's built-in Voice parameter."));

            if ((VisemeBlendshapeOverrideComponent.VoiceModulationMode)_voiceModulationMode.enumValueIndex !=
                VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled)
            {
                EditorGUILayout.PropertyField(
                    _voiceMin,
                    new GUIContent("Voice Min", "At or below this Voice value, output intensity is 0."));
                EditorGUILayout.PropertyField(
                    _voiceMax,
                    new GUIContent("Voice Max", "At or above this Voice value, output reaches the configured viseme weight."));

                EditorGUILayout.HelpBox(
                    "最终输出 = 当前 viseme 的配置 Weight × remap(Voice)。\n" +
                    "当 Voice <= Min 时输出 0；当 Voice >= Max 时输出完整权重。",
                    MessageType.None);

                EditorGUILayout.LabelField(
                    "推荐起点",
                    "Voice Min = 0.05, Voice Max = 0.15",
                    EditorStyles.miniLabel);
            }
        }

        private void ApplyPreset(
            VisemeBlendshapeOverrideComponent component,
            VisemeBlendshapeOverrideComponent.WeightPreset preset)
        {
            Undo.RecordObject(component, $"Apply {preset} viseme weight preset");
            component.ApplyWeightPreset(preset);
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void DrawBinding(
            VisemeBlendshapeOverrideComponent component,
            VRCAvatarDescriptor descriptor,
            SkinnedMeshRenderer resolvedRenderer,
            IReadOnlyList<string> blendshapeOptions,
            SerializedProperty bindingProperty)
        {
            var visemeProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.viseme));
            var useDescriptorProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.useAvatarDescriptorBlendshape));
            var blendshapeNameProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.blendshapeName));
            var weightProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.weight));
            var useGlobalVoiceRangeProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.useGlobalVoiceRange));
            var voiceMinProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.voiceMin));
            var voiceMaxProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.voiceMax));

            var viseme = (VRC_AvatarDescriptor.Viseme)visemeProperty.intValue;
            var descriptorBlendshape = VisemeBlendshapeOverrideComponent.GetDescriptorBlendshapeName(descriptor, viseme);
            var showVoiceControls = component.voiceModulationMode != VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(viseme.ToString(), EditorStyles.boldLabel);

            useDescriptorProperty.boolValue = EditorGUILayout.ToggleLeft(
                "Follow Avatar Descriptor mapping",
                useDescriptorProperty.boolValue);

            if (useDescriptorProperty.boolValue)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Descriptor Blendshape",
                        string.IsNullOrWhiteSpace(descriptorBlendshape) ? "<none>" : descriptorBlendshape);
                }
            }
            else
            {
                DrawManualBlendshapeSelector(resolvedRenderer, blendshapeOptions, blendshapeNameProperty);
            }

            weightProperty.floatValue = EditorGUILayout.Slider("Weight", weightProperty.floatValue, 0f, 100f);

            if (showVoiceControls)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Voice 响应区间", EditorStyles.miniBoldLabel);
                useGlobalVoiceRangeProperty.boolValue = EditorGUILayout.ToggleLeft(
                    "Use Global Voice Range",
                    useGlobalVoiceRangeProperty.boolValue);

                if (useGlobalVoiceRangeProperty.boolValue)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.FloatField("Voice Min", component.voiceMin);
                        EditorGUILayout.FloatField("Voice Max", component.voiceMax);
                    }
                }
                else
                {
                    voiceMinProperty.floatValue = EditorGUILayout.Slider("Voice Min", voiceMinProperty.floatValue, 0f, 1f);
                    voiceMaxProperty.floatValue = EditorGUILayout.Slider("Voice Max", voiceMaxProperty.floatValue, 0f, 1f);
                    if (voiceMaxProperty.floatValue <= voiceMinProperty.floatValue)
                        voiceMaxProperty.floatValue = Mathf.Min(1f, voiceMinProperty.floatValue + 0.001f);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawManualBlendshapeSelector(
            SkinnedMeshRenderer resolvedRenderer,
            IReadOnlyList<string> blendshapeOptions,
            SerializedProperty blendshapeNameProperty)
        {
            if (resolvedRenderer == null || resolvedRenderer.sharedMesh == null || blendshapeOptions.Count == 0)
            {
                EditorGUILayout.PropertyField(blendshapeNameProperty, new GUIContent("Blendshape Name"));
                return;
            }

            var currentName = blendshapeNameProperty.stringValue ?? string.Empty;
            var popupOptions = new List<string> { "-none-" };
            var currentIndex = 0;

            if (!string.IsNullOrWhiteSpace(currentName) && !blendshapeOptions.Contains(currentName))
            {
                popupOptions.Add($"<invalid> {currentName}");
                currentIndex = 1;
            }

            popupOptions.AddRange(blendshapeOptions);

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                var foundIndex = FindIndex(blendshapeOptions, currentName);
                if (foundIndex >= 0)
                    currentIndex = (popupOptions.Count - blendshapeOptions.Count) + foundIndex;
            }

            var nextIndex = EditorGUILayout.Popup("Blendshape", currentIndex, popupOptions.ToArray());
            if (nextIndex == 0)
            {
                blendshapeNameProperty.stringValue = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentName) && !blendshapeOptions.Contains(currentName) && nextIndex == 1)
            {
                blendshapeNameProperty.stringValue = currentName;
                return;
            }

            var optionOffset = popupOptions.Count - blendshapeOptions.Count;
            var selectedIndex = nextIndex - optionOffset;
            if (selectedIndex >= 0 && selectedIndex < blendshapeOptions.Count)
                blendshapeNameProperty.stringValue = blendshapeOptions[selectedIndex];
        }

        private static int FindIndex(IReadOnlyList<string> values, string target)
        {
            if (values == null)
                return -1;

            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == target)
                    return i;
            }

            return -1;
        }

        private static List<string> GetBlendshapeOptions(SkinnedMeshRenderer renderer)
        {
            var options = new List<string>();
            if (renderer == null || renderer.sharedMesh == null)
                return options;

            for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                options.Add(renderer.sharedMesh.GetBlendShapeName(i));

            return options;
        }

        private static void DrawValidationSummary(
            VisemeBlendshapeOverrideComponent component,
            VRCAvatarDescriptor descriptor,
            SkinnedMeshRenderer resolvedRenderer)
        {
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("This component must be placed on the avatar root object that contains a VRCAvatarDescriptor.", MessageType.Error);
                return;
            }

            if (resolvedRenderer == null)
            {
                EditorGUILayout.HelpBox("No target renderer resolved. Assign Target Renderer or configure Avatar Descriptor > Face Mesh first.", MessageType.Error);
                return;
            }

            if (resolvedRenderer.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("The resolved renderer has no shared mesh.", MessageType.Error);
                return;
            }

            var hasAtLeastOneEffectiveBinding = false;
            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                var binding = component.GetBinding(viseme);
                var effectiveBlendshape = component.ResolveBlendshapeName(descriptor, binding);
                if (!string.IsNullOrWhiteSpace(effectiveBlendshape))
                {
                    hasAtLeastOneEffectiveBinding = true;
                    break;
                }
            }

            if (!hasAtLeastOneEffectiveBinding)
            {
                EditorGUILayout.HelpBox(
                    "No viseme currently resolves to a blendshape. Configure Avatar Descriptor visemes or switch specific rows to manual override.",
                    MessageType.Warning);
            }

            if (descriptor.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                descriptor.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly)
            {
                EditorGUILayout.HelpBox(
                    "The build processor will still force the descriptor to VisemeParameterOnly, but your current Avatar Descriptor is not using viseme blendshape mode. " +
                    "If you want to follow the descriptor mapping, make sure the descriptor already contains valid viseme entries.",
                    MessageType.Info);
            }
        }
    }
}