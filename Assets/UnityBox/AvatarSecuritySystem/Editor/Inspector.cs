using UnityEngine;
using UnityEditor;
using System.Linq;
using UnityBox.AvatarSecuritySystem.Editor;
using static UnityBox.AvatarSecuritySystem.Editor.I18n;
namespace UnityBox.AvatarSecuritySystem
{
    [CustomEditor(typeof(ASSComponent))]
    public class Inspector : UnityEditor.Editor
    {
        private ASSComponent _target;
        private static readonly string[] GESTURE_NAMES =
        {
            "0: Idle",
            "1: Fist ✊",
            "2: HandOpen ✋",
            "3: Fingerpoint ☝",
            "4: Victory ✌",
            "5: RockNRoll 🤘",
            "6: HandGun 🔫",
            "7: ThumbsUp 👍"
        };
        private static readonly int[] GESTURE_VALUES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private void OnEnable()
        {
            _target = (ASSComponent)target;
            if (_target.uiLanguage != SystemLanguage.Unknown)
            {
                SetLanguage(_target.uiLanguage);
            }
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(10);
            DrawHeaderSection();
            EditorGUILayout.Space(10);
            DrawConfigurationSections();
            serializedObject.ApplyModifiedProperties();
        }
        private void DrawHeaderSection()
        {
            DrawTitle();
            EditorGUILayout.Space(5);
            DrawLanguageSelector();
        }
        private void DrawConfigurationSections()
        {
            if (_target.defaultEnableDefense)
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
            DrawDefenseSection();
            EditorGUILayout.Space(10);
            DrawObfuscationSection();
            EditorGUILayout.Space(10);
            DrawAdvancedSections();
        }
        private void DrawAdvancedSections()
        {
            DrawLockSection();
            EditorGUILayout.Space(10);
            DrawAdvancedOptionsSection();
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
            EditorGUILayout.LabelField($"🔒 {systemName} ({systemShortName})", style);
            EditorGUILayout.LabelField(subtitle, EditorStyles.centeredGreyMiniLabel);
        }
        private void DrawPasswordSection()
        {
            EditorGUILayout.LabelField(T("password.config"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useRightHand"),
                new GUIContent(T("password.use_right_hand"), T("password.use_right_hand_tooltip")));
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("password.sequence"), EditorStyles.miniLabel);
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
            string stepLabel = string.Format(T("password.step"), index + 1);
            EditorGUILayout.LabelField(stepLabel, GUILayout.Width(60));
            var element = passwordProp.GetArrayElementAtIndex(index);
            int currentValue = element.intValue;
            int newValue = EditorGUILayout.IntPopup(currentValue, GESTURE_NAMES, GESTURE_VALUES);
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
            if (_target.gesturePassword.Count == 0)
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
            string message = string.Format(T("password.strength"),
                T(strengthKey),
                _target.gesturePassword.Count);
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
            EditorGUILayout.HelpBox(T("advanced.default_defense_warning"), MessageType.Error);
        }
        private void DrawGestureRecognitionSection()
        {
            EditorGUILayout.LabelField(T("gesture.config"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gestureHoldTime"),
                new GUIContent(T("gesture.hold_time"), T("gesture.hold_time_tooltip")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gestureMaxHoldTime"),
                new GUIContent(T("gesture.max_hold_time"), T("gesture.max_hold_time_tooltip")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gestureErrorTolerance"),
                new GUIContent(T("gesture.error_tolerance"), T("gesture.error_tolerance_tooltip")));
        }
        private void DrawCountdownSection()
        {
            EditorGUILayout.LabelField(T("countdown.config"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("countdownDuration"),
                new GUIContent(T("countdown.duration"), T("countdown.duration_tooltip")));
        }
        private void DrawLanguageSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🌐 " + T("language.title"), GUILayout.Width(120));
            var languageProp = serializedObject.FindProperty("uiLanguage");
            SystemLanguage currentLanguage = GetCurrentLanguage(languageProp);
            int currentIndex = GetLanguageIndex(languageProp);
            int newIndex = EditorGUILayout.Popup(currentIndex, GetLanguageNames());
            if (newIndex != currentIndex)
            {
                ApplyLanguageChange(languageProp, GetLanguageValues()[newIndex]);
            }
            EditorGUILayout.EndHorizontal();
        }
        private SystemLanguage GetCurrentLanguage(SerializedProperty languageProp)
        {
            SystemLanguage currentLang = (SystemLanguage)languageProp.intValue;
            return currentLang == SystemLanguage.Unknown ? Application.systemLanguage : currentLang;
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
                "简体中文",
                "English",
                "日本語"
            };
        }
        private SystemLanguage[] GetLanguageValues()
        {
            return new[]
            {
                SystemLanguage.Unknown,
                SystemLanguage.ChineseSimplified,
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
            using (new EditorGUI.DisabledGroupScope(_target.defaultEnableDefense))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("disableDefense"),
                    new GUIContent(T("defense.disable_defense"), T("defense.disable_defense_tooltip")));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableOverflow"),
                new GUIContent(T("defense.enable_overflow"), T("defense.enable_overflow_tooltip")));
        }
        private void DrawAdvancedOptionsSection()
        {
            EditorGUILayout.LabelField(T("advanced.options"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enabledInPlaymode"),
                new GUIContent(T("advanced.play_mode"), T("advanced.play_mode_tooltip")));
            using (new EditorGUI.DisabledGroupScope(_target.defaultEnableDefense))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("disableOverlay"),
                    new GUIContent(T("advanced.disable_overlay"), T("advanced.disable_overlay_tooltip")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("disableWarningSound"),
                    new GUIContent(T("advanced.mute_warning"), T("advanced.mute_warning_tooltip")));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultEnableDefense"),
                new GUIContent(T("advanced.default_defense"), T("advanced.default_defense_tooltip")));
        }
        private void DrawObfuscationSection()
        {
            EditorGUILayout.LabelField(T("advanced.obfuscation_section"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableObfuscation"),
                new GUIContent(T("advanced.disable_obfuscation"), T("advanced.disable_obfuscation_tooltip")));
            EditorGUI.BeginDisabledGroup(serializedObject.FindProperty("disableObfuscation").boolValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDecoyLayers"),
                new GUIContent(T("advanced.decoy_layers"), T("advanced.decoy_layers_tooltip")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDecoyStates"),
                new GUIContent(T("advanced.decoy_states"), T("advanced.decoy_states_tooltip")));
            EditorGUI.EndDisabledGroup();
        }
        private void DrawLockSection()
        {
            EditorGUILayout.LabelField(T("advanced.lock_options"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableRootChildren"),
                new GUIContent(T("advanced.disable_objects"), T("advanced.disable_objects_tooltip")));
            var wdModeProp = serializedObject.FindProperty("writeDefaultsMode");
            var wdContent = new GUIContent(T("advanced.wd_mode"), T("advanced.wd_mode_tooltip"));
            string[] wdModeNames = new[]
            {
                T("advanced.wd_mode_auto"),
                T("advanced.wd_mode_on"),
                T("advanced.wd_mode_off")
            };
            wdModeProp.enumValueIndex = EditorGUILayout.Popup(wdContent, wdModeProp.enumValueIndex, wdModeNames);
        }
    }
}
