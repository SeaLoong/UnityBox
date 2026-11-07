using UnityEngine;
using UnityEditor;
using UnityBox.AvatarSecuritySystem.Editor;
using static UnityBox.AvatarSecuritySystem.Editor.I18n;
namespace UnityBox.AvatarSecuritySystem
{
    [CustomEditor(typeof(ASSComponent))]
    public class Inspector : UnityEditor.Editor
    {
        private ASSComponent _target;
        private static readonly int[] GESTURE_VALUES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private void OnEnable()
        {
            _target = (ASSComponent)target;
            SetLanguage(_target.uiLanguage);
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SetLanguage((SystemLanguage)serializedObject.FindProperty("uiLanguage").intValue);
            EditorGUILayout.Space(10);
            DrawHeaderSection();
            EditorGUILayout.HelpBox(T("build.automatic_help"), MessageType.Info);
            if (!HasVrChatIntegration)
                EditorGUILayout.HelpBox(T("build.standalone_help"), MessageType.Info);
            EditorGUILayout.Space(10);
            DrawConfigurationSections();
            serializedObject.ApplyModifiedProperties();
        }
        private static bool HasVrChatIntegration => System.Type.GetType(
            "UnityBox.AvatarSecuritySystem.Editor.Processor, UnityBox.AvatarSecuritySystem.VRChat") != null;
        private bool UsesDefaultDefense => HasVrChatIntegration && _target.defaultEnableDefense;

        private void DrawProperty(string propertyName, string labelKey)
        {
            var property = serializedObject.FindProperty(propertyName);
            EditorGUILayout.PropertyField(property, new GUIContent(T(labelKey), ExpandTokens(property.tooltip)));
        }
        private void DrawHeaderSection()
        {
            DrawTitle();
            EditorGUILayout.Space(5);
            DrawLanguageSelector();
        }
        private void DrawConfigurationSections()
        {
            if (UsesDefaultDefense)
            {
                DrawDefaultDefenseWarning();
                EditorGUILayout.Space(10);
            }
            else
            {
                if (_target.disableOverlay && _target.disableWarningSound)
                {
                    EditorGUILayout.HelpBox(T("advanced.no_feedback_warning"), MessageType.Warning);
                    EditorGUILayout.Space(5);
                }
                DrawPasswordSection();
                EditorGUILayout.Space(10);
                DrawGestureRecognitionSection();
                EditorGUILayout.Space(10);
                DrawCountdownSection();
                EditorGUILayout.Space(10);
            }
            if (HasVrChatIntegration)
            {
                DrawDefenseSection();
                EditorGUILayout.Space(10);
            }
            DrawObfuscationSection();
            EditorGUILayout.Space(10);
            DrawFeedbackSection();
            EditorGUILayout.Space(10);
            DrawBuildSection();
        }
        private void DrawTitle()
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            string systemName = T("system.name");
            string systemShortName = T("system.short_name");
            string subtitle = T("system.subtitle");
            EditorGUILayout.LabelField($"🖐 {systemName} ({systemShortName})", style);
            EditorGUILayout.LabelField(subtitle, EditorStyles.centeredGreyMiniLabel);
        }
        private void DrawPasswordSection()
        {
            EditorGUILayout.LabelField(T("password.config"), EditorStyles.boldLabel);
            DrawProperty("useRightHand", "password.use_right_hand");
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(Content("password.sequence", "password.gesture_tooltip"), EditorStyles.miniLabel);
            var passwordProp = serializedObject.FindProperty("gesturePassword");
            DrawPasswordElements(passwordProp);
            EditorGUILayout.Space(5);
            DrawPasswordActionButtons(passwordProp);
            EditorGUILayout.Space(5);
            DrawPasswordStatus();
        }
        private void DrawPasswordElements(SerializedProperty passwordProp)
        {
            for (int i = 0; i < passwordProp.arraySize; i++)
            {
                DrawPasswordElement(passwordProp, i);
            }
        }
        private void DrawPasswordElement(SerializedProperty passwordProp, int index)
        {
            EditorGUILayout.BeginHorizontal();
            string stepLabel = T("password.step", index + 1);
            EditorGUILayout.LabelField(stepLabel, GUILayout.Width(60));
            var element = passwordProp.GetArrayElementAtIndex(index);
            int currentValue = element.intValue;
            int newValue = EditorGUILayout.IntPopup(currentValue, GetGestureNames(), GESTURE_VALUES);
            element.intValue = newValue;
            if (GUILayout.Button(new GUIContent("X", T("password.delete_step")), GUILayout.Width(30)))
            {
                passwordProp.DeleteArrayElementAtIndex(index);
            }
            EditorGUILayout.EndHorizontal();
        }
        private void DrawPasswordActionButtons(SerializedProperty passwordProp)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("password.add_gesture")))
            {
                passwordProp.InsertArrayElementAtIndex(passwordProp.arraySize);
                passwordProp.GetArrayElementAtIndex(passwordProp.arraySize - 1).intValue = 1;
            }
            if (GUILayout.Button(T("password.clear")))
            {
                if (EditorUtility.DisplayDialog(T("common.warning"),
                    T("password.clear_confirm"),
                    T("common.confirm"),
                    T("common.cancel")))
                {
                    passwordProp.ClearArray();
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        private void DrawPasswordStatus()
        {
            if (_target.gesturePassword == null || _target.gesturePassword.Count == 0)
            {
                DrawPasswordEmptyWarning();
            }
            else
            {
                DrawPasswordStrengthInfo();
            }
        }
        private void DrawPasswordEmptyWarning()
        {
            EditorGUILayout.HelpBox("⚠️ " + T("password.empty_warning"), MessageType.Warning);
        }
        private void DrawPasswordStrengthInfo()
        {
            string strength = _target.GetPasswordStrength();
            string strengthKey = GetStrengthTranslationKey(strength);
            Color strengthColor = GetStrengthColor(strength);
            var oldColor = GUI.color;
            GUI.color = strengthColor;
            string message = T("password.strength", T(strengthKey), _target.gesturePassword.Count);
            EditorGUILayout.HelpBox(message, MessageType.Info);
            GUI.color = oldColor;
        }
        private string GetStrengthTranslationKey(string strength)
        {
            return strength switch
            {
                "Strong" => "password.strength.strong",
                "Medium" => "password.strength.medium",
                _ => "password.strength.weak"
            };
        }
        private Color GetStrengthColor(string strength)
        {
            return strength switch
            {
                "Strong" => Color.green,
                "Medium" => Color.yellow,
                _ => Color.red
            };
        }
        private void DrawDefaultDefenseWarning()
        {
            EditorGUILayout.HelpBox(T("advanced.default_defense_warning"), MessageType.Info);
        }
        private void DrawGestureRecognitionSection()
        {
            EditorGUILayout.LabelField(T("gesture.config"), EditorStyles.boldLabel);
            DrawProperty("gestureHoldTime", "gesture.hold_time");
            DrawProperty("gestureMaxHoldTime", "gesture.max_hold_time");
            DrawProperty("gestureErrorTolerance", "gesture.error_tolerance");
            EditorGUILayout.HelpBox(T("gesture.continuous_hold_help"), MessageType.Info);
        }
        private void DrawCountdownSection()
        {
            EditorGUILayout.LabelField(T("countdown.config"), EditorStyles.boldLabel);
            DrawProperty("countdownDuration", "countdown.duration");
        }
        private void DrawLanguageSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("🌐 " + T("language.title"), T("language.ui_language_tooltip")), GUILayout.Width(120));
            var languageProp = serializedObject.FindProperty("uiLanguage");
            int currentIndex = GetLanguageIndex(languageProp);
            int newIndex = EditorGUILayout.Popup(currentIndex, GetLanguageNames());
            if (newIndex != currentIndex)
            {
                ApplyLanguageChange(languageProp, GetLanguageValues()[newIndex]);
            }
            EditorGUILayout.EndHorizontal();
        }
        private int GetLanguageIndex(SerializedProperty languageProp)
        {
            SystemLanguage[] languageValues = GetLanguageValues();
            int currentIndex = System.Array.IndexOf(languageValues, (SystemLanguage)languageProp.intValue);
            return currentIndex < 0 ? 0 : currentIndex;
        }
        private string[] GetLanguageNames()
        {
            return new[]
            {
                T("language.auto"),
                T("language.chinese"),
                T("language.traditional_chinese"),
                T("language.english"),
                T("language.japanese")
            };
        }
        private SystemLanguage[] GetLanguageValues()
        {
            return new[]
            {
                SystemLanguage.Unknown,
                SystemLanguage.ChineseSimplified,
                SystemLanguage.ChineseTraditional,
                SystemLanguage.English,
                SystemLanguage.Japanese
            };
        }
        private void ApplyLanguageChange(SerializedProperty languageProp, SystemLanguage newLanguage)
        {
            languageProp.intValue = (int)newLanguage;
            SetLanguage(newLanguage);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_target);
        }
        private void DrawDefenseSection()
        {
            EditorGUILayout.LabelField(T("defense.options"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledGroupScope(UsesDefaultDefense))
            {
                DrawProperty("disableDefense", "defense.disable_defense");
            }
            DrawProperty("lightweightDefense", "defense.lightweight_defense");
            DrawProperty("defaultEnableDefense", "advanced.default_defense");
            if (_target.lightweightDefense)
            {
                EditorGUILayout.HelpBox(T("defense.lightweight_defense_note"), MessageType.Info);
            }
        }
        private void DrawFeedbackSection()
        {
            EditorGUILayout.LabelField(T("feedback.options"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledGroupScope(UsesDefaultDefense))
            {
                DrawProperty("disableOverlay", "advanced.disable_overlay");
                DrawProperty("disableWarningSound", "advanced.mute_warning");
            }
        }
        private void DrawBuildSection()
        {
            if (!HasVrChatIntegration) return;
            EditorGUILayout.LabelField(T("advanced.options"), EditorStyles.boldLabel);
            DrawProperty("enabledInPlaymode", "advanced.play_mode");
        }
        private void DrawObfuscationSection()
        {
            EditorGUILayout.LabelField(T("advanced.obfuscation_section"), EditorStyles.boldLabel);
            DrawProperty("disableObfuscation", "advanced.disable_obfuscation");
            EditorGUI.BeginDisabledGroup(serializedObject.FindProperty("disableObfuscation").boolValue);
            if (HasVrChatIntegration)
                DrawProperty("enablePlayableLayerObfuscation", "advanced.obfuscate_playable_layers");
            DrawProperty("enableDecoyLayers", "advanced.decoy_layers");
            DrawProperty("enableDecoyStates", "advanced.decoy_states");
            EditorGUI.EndDisabledGroup();
        }
    }
}
