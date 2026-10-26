using System;
using System.Collections.Generic;
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
        private SerializedProperty _globalWeight;
        private SerializedProperty _voiceModulationMode;
        private SerializedProperty _voiceMin;
        private SerializedProperty _voiceMax;
        private SerializedProperty _bindings;

        private void OnEnable()
        {
            _targetRenderer = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.targetRenderer));
            _writeDefaultsMode = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.writeDefaultsMode));
            _globalWeight = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.globalWeight));
            _voiceModulationMode = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceModulationMode));
            _voiceMin = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceMin));
            _voiceMax = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.voiceMax));
            _bindings = serializedObject.FindProperty(nameof(VisemeBlendshapeOverrideComponent.bindings));

            var component = (VisemeBlendshapeOverrideComponent)target;
            component.EnsureEditorDefaults();
        }

        public override void OnInspectorGUI()
        {
            var component = (VisemeBlendshapeOverrideComponent)target;
            component.EnsureEditorDefaults();
            serializedObject.Update();

            var descriptor = component.GetComponent<VRCAvatarDescriptor>();
            var resolvedRenderer = component.ResolveTargetRenderer(descriptor);
            var blendshapeOptions = GetBlendshapeOptions(resolvedRenderer);

            EditorGUILayout.LabelField("Viseme Blendshape Override", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_targetRenderer, new GUIContent("Renderer"));
            EditorGUILayout.PropertyField(_writeDefaultsMode, new GUIContent("Write Defaults"));

            DrawSlider("Weight", _globalWeight, 0f, 100f);
            DrawVoiceSettings(_voiceModulationMode, _voiceMin, _voiceMax);

            EditorGUILayout.Space(6f);
            var visemesFoldoutKey = GetVisemesFoldoutKey(component);
            var visemesExpanded = SessionState.GetBool(visemesFoldoutKey, false);
            visemesExpanded = EditorGUILayout.Foldout(visemesExpanded, "Visemes", true);
            SessionState.SetBool(visemesFoldoutKey, visemesExpanded);

            if (visemesExpanded)
            {
                EditorGUI.indentLevel++;
                for (var i = 0; i < _bindings.arraySize; i++)
                    DrawBinding(descriptor, blendshapeOptions, _bindings.GetArrayElementAtIndex(i));
                EditorGUI.indentLevel--;
            }

            DrawValidationMessages(component, descriptor, resolvedRenderer);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBinding(
            VRCAvatarDescriptor descriptor,
            IReadOnlyList<string> blendshapeOptions,
            SerializedProperty bindingProperty)
        {
            var visemeProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.viseme));
            var blendshapeNameProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.blendshapeName));
            var useCustomSettingsProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.useCustomSettings));
            var weightProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.weight));
            var voiceModeProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.voiceMode));
            var voiceMinProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.voiceMin));
            var voiceMaxProperty = bindingProperty.FindPropertyRelative(nameof(VisemeBlendshapeOverrideComponent.VisemeBinding.voiceMax));

            var viseme = (VRC_AvatarDescriptor.Viseme)visemeProperty.intValue;
            var header = BuildBindingHeader(descriptor, viseme, blendshapeNameProperty);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
            DrawBlendshapePopup(descriptor, viseme, blendshapeOptions, blendshapeNameProperty);

            useCustomSettingsProperty.boolValue = EditorGUILayout.ToggleLeft("Custom Settings", useCustomSettingsProperty.boolValue);

            if (useCustomSettingsProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawSlider("Weight", weightProperty, 0f, 100f);
                DrawVoiceModeOverride(voiceModeProperty, voiceMinProperty, voiceMaxProperty);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawVoiceSettings(
            SerializedProperty modeProperty,
            SerializedProperty minProperty,
            SerializedProperty maxProperty)
        {
            EditorGUILayout.PropertyField(modeProperty, new GUIContent("Voice Mode"));

            var mode = (VisemeBlendshapeOverrideComponent.VoiceModulationMode)modeProperty.enumValueIndex;
            if (mode == VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled)
                return;

            DrawSlider("Voice Min", minProperty, 0f, 1f);
            DrawSlider("Voice Max", maxProperty, 0f, 1f);
            ClampVoiceRange(minProperty, maxProperty);
        }

        private static void DrawVoiceModeOverride(
            SerializedProperty modeProperty,
            SerializedProperty minProperty,
            SerializedProperty maxProperty)
        {
            EditorGUILayout.PropertyField(modeProperty, new GUIContent("Voice Mode"));

            var mode = (VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride)modeProperty.enumValueIndex;
            switch (mode)
            {
                case VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Linear:
                    DrawSlider("Voice Min", minProperty, 0f, 1f);
                    DrawSlider("Voice Max", maxProperty, 0f, 1f);
                    ClampVoiceRange(minProperty, maxProperty);
                    break;
            }
        }

        private static void DrawSlider(string label, SerializedProperty property, float min, float max)
        {
            property.floatValue = EditorGUILayout.Slider(label, property.floatValue, min, max);
        }

        private static void ClampVoiceRange(SerializedProperty minProperty, SerializedProperty maxProperty)
        {
            var min = minProperty.floatValue;
            var max = maxProperty.floatValue;
            VisemeBlendshapeOverrideUtils.NormalizeVoiceRange(ref min, ref max);
            minProperty.floatValue = min;
            maxProperty.floatValue = max;
        }

        private static string BuildBindingHeader(
            VRCAvatarDescriptor descriptor,
            VRC_AvatarDescriptor.Viseme viseme,
            SerializedProperty blendshapeNameProperty)
        {
            var selectedBlendshape = ResolveCurrentBlendshapeName(descriptor, viseme, blendshapeNameProperty);
            return string.IsNullOrWhiteSpace(selectedBlendshape)
                ? FormatVisemeName(viseme)
                : $"{FormatVisemeName(viseme)}  ·  {selectedBlendshape}";
        }

        private static string GetVisemesFoldoutKey(VisemeBlendshapeOverrideComponent component)
        {
            return $"UnityBox.VisemeBlendshapeOverride.Visemes.{component.GetInstanceID()}";
        }

        private static void DrawBlendshapePopup(
            VRCAvatarDescriptor descriptor,
            VRC_AvatarDescriptor.Viseme viseme,
            IReadOnlyList<string> blendshapeOptions,
            SerializedProperty blendshapeNameProperty)
        {
            var storedName = blendshapeNameProperty.stringValue ?? string.Empty;
            var options = new List<string> { "- Avatar Descriptor -", "- None -" };
            int currentIndex = string.IsNullOrWhiteSpace(storedName) ? 0 : 1;
            bool explicitName = !string.IsNullOrWhiteSpace(storedName) &&
                storedName != VisemeBlendshapeOverrideComponent.NoneBlendshapeValue;
            bool missing = explicitName && !Contains(blendshapeOptions, storedName);
            if (missing)
            {
                options.Add($"<Missing> {storedName}");
                currentIndex = 2;
            }
            int optionOffset = options.Count;
            foreach (var option in blendshapeOptions) options.Add(option);
            if (explicitName && !missing)
                currentIndex = optionOffset + FindIndex(blendshapeOptions, storedName);

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup("Blendshape", currentIndex, options.ToArray());
            if (!EditorGUI.EndChangeCheck()) return;
            if (nextIndex == 0)
                blendshapeNameProperty.stringValue = string.Empty;
            else if (nextIndex == 1)
                blendshapeNameProperty.stringValue = VisemeBlendshapeOverrideComponent.NoneBlendshapeValue;
            else if (nextIndex >= optionOffset)
                blendshapeNameProperty.stringValue = blendshapeOptions[nextIndex - optionOffset];
        }

        private static string ResolveCurrentBlendshapeName(
            VRCAvatarDescriptor descriptor,
            VRC_AvatarDescriptor.Viseme viseme,
            SerializedProperty blendshapeNameProperty)
        {
            var storedName = blendshapeNameProperty.stringValue ?? string.Empty;
            if (string.Equals(storedName, VisemeBlendshapeOverrideComponent.NoneBlendshapeValue, StringComparison.Ordinal))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(storedName))
                return storedName;

            return descriptor != null
                ? VisemeBlendshapeOverrideComponent.GetDescriptorBlendshapeName(descriptor, viseme)
                : string.Empty;
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

        private static bool Contains(IReadOnlyList<string> values, string target)
        {
            return FindIndex(values, target) >= 0;
        }

        private static string FormatVisemeName(VRC_AvatarDescriptor.Viseme viseme)
        {
            return viseme.ToString().ToLowerInvariant();
        }

        private static void DrawValidationMessages(
            VisemeBlendshapeOverrideComponent component,
            VRCAvatarDescriptor descriptor,
            SkinnedMeshRenderer resolvedRenderer)
        {
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("VRCAvatarDescriptor was not found on this object.", MessageType.Error);
                return;
            }

            if (resolvedRenderer == null)
            {
                EditorGUILayout.HelpBox("Renderer was not resolved.", MessageType.Error);
                return;
            }

            if (resolvedRenderer.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("The selected renderer has no shared mesh.", MessageType.Error);
                return;
            }

            var hasAtLeastOneBinding = false;
            foreach (var viseme in VisemeBlendshapeOverrideComponent.GetSupportedVisemes())
            {
                var binding = component.GetBinding(viseme);
                var effectiveBlendshape = component.ResolveBlendshapeName(descriptor, binding);
                if (!string.IsNullOrWhiteSpace(effectiveBlendshape))
                {
                    hasAtLeastOneBinding = true;
                    break;
                }
            }

            if (!hasAtLeastOneBinding)
                EditorGUILayout.HelpBox("No viseme currently resolves to a blendshape.", MessageType.Warning);
        }
    }
}