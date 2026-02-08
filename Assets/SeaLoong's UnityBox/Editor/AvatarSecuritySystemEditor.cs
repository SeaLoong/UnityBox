using UnityEngine;
using UnityEditor;
using System.Linq;
using SeaLoongUnityBox.AvatarSecuritySystem.Editor;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

namespace SeaLoongUnityBox
{
    /// <summary>
    /// AvatarSecuritySystem Inspector 编辑器
    /// </summary>
    [CustomEditor(typeof(AvatarSecuritySystemComponent))]
    public class AvatarSecuritySystemEditor : UnityEditor.Editor
    {
        private AvatarSecuritySystemComponent _target;
        private bool _showAdvancedDebug = false;
        
        private static readonly string[] GESTURE_NAMES =
        {
            "1: Fist ✊",
            "2: HandOpen ✋",
            "3: Fingerpoint ☝",
            "4: Victory ✌",
            "5: RockNRoll 🤘",
            "6: HandGun 🔫",
            "7: ThumbsUp 👍"
        };

        private static readonly int[] GESTURE_VALUES = { 1, 2, 3, 4, 5, 6, 7 };

        private void OnEnable()
        {
            _target = (AvatarSecuritySystemComponent)target;
            
            // 应用组件中保存的语言设置
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
            DrawPasswordSection();
            EditorGUILayout.Space(10);

            DrawCountdownSection();
            EditorGUILayout.Space(10);

            DrawDefenseSection();
            EditorGUILayout.Space(10);

            DrawAdvancedSection();
            EditorGUILayout.Space(10);

            DrawEstimationSection();
        }

        private void DrawAdvancedSection()
        {
            DrawDebugSection();
            EditorGUILayout.Space(10);
            DrawLockSection();
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
            EditorGUILayout.LabelField(T("defense.config"), EditorStyles.boldLabel);

            var defenseLevelProp = serializedObject.FindProperty("defenseLevel");
            EditorGUILayout.PropertyField(defenseLevelProp,
                new GUIContent(T("defense.level"), T("defense.level_tooltip")));

            EditorGUILayout.Space(3);
            int defenseLevelValue = defenseLevelProp.intValue;
            EditorGUILayout.HelpBox(GetDefenseLevelDescription(defenseLevelValue), MessageType.Info);
        }

        private string GetDefenseLevelDescription(int level)
        {
            return level switch
            {
                0 => T("defense.level_0_desc"),
                1 => T("defense.level_1_desc"),
                2 => T("defense.level_2_desc"),
                3 => T("defense.level_3_desc"),
                _ => "Unknown Defense Level"
            };
        }

        private void DrawDebugSection()
        {
            EditorGUILayout.LabelField(T("advanced.debug_options"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableInPlayMode"),
                new GUIContent(T("advanced.play_mode"), T("advanced.play_mode_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableDefense"),
                new GUIContent(T("advanced.disable_defense"), T("advanced.disable_defense_tooltip")));

            // 折叠的高级调试选项
            _showAdvancedDebug = EditorGUILayout.Foldout(_showAdvancedDebug, T("advanced.debug_advanced"), true);
            if (_showAdvancedDebug)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableVerboseLogging"),
                    new GUIContent(T("advanced.verbose_logging"), T("advanced.verbose_logging_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSkipLockSystem"),
                    new GUIContent(T("advanced.skip_lock"), T("advanced.skip_lock_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSkipPasswordSystem"),
                    new GUIContent(T("advanced.skip_password"), T("advanced.skip_password_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSkipCountdownSystem"),
                    new GUIContent(T("advanced.skip_countdown"), T("advanced.skip_countdown_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSkipFeedbackSystem"),
                    new GUIContent(T("advanced.skip_feedback"), T("advanced.skip_feedback_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSkipDefenseSystem"),
                    new GUIContent(T("advanced.skip_defense"), T("advanced.skip_defense_tooltip")));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugValidateAfterBuild"),
                    new GUIContent(T("advanced.validate_build"), T("advanced.validate_build_tooltip")));
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawLockSection()
        {
            EditorGUILayout.LabelField(T("advanced.lock_options"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lockFxLayers"),
                new GUIContent(T("advanced.lock_fx_layers"), T("advanced.lock_fx_layers_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableRootChildren"),
                new GUIContent(T("advanced.disable_objects"), T("advanced.disable_objects_tooltip")));
            
            EditorGUILayout.Space(5);
            
            // Write Defaults 模式选择
            var wdModeProp = serializedObject.FindProperty("writeDefaultsMode");
            var wdContent = new GUIContent(T("advanced.wd_mode"), T("advanced.wd_mode_tooltip"));
            
            string[] wdModeNames = new[]
            {
                T("advanced.wd_mode_on"),
                T("advanced.wd_mode_off")
            };
            
            wdModeProp.enumValueIndex = EditorGUILayout.Popup(wdContent, wdModeProp.enumValueIndex, wdModeNames);
            
            // 显示当前模式的说明
            string modeHint = wdModeProp.enumValueIndex == 0 
                ? T("advanced.wd_mode_on_hint") 
                : T("advanced.wd_mode_off_hint");
            EditorGUILayout.HelpBox(modeHint, MessageType.Info);
        }

        private void DrawEstimationSection()
        {
            EditorGUILayout.LabelField(T("estimate.title"), EditorStyles.boldLabel);

            float sizeKB = _target.EstimateFileSizeKB();
            string sizeText = FormatFileSize(sizeKB);
            EditorGUILayout.HelpBox($"📊 {T("estimate.file_size")}: {sizeText}", MessageType.None);
        }

        private string FormatFileSize(float sizeKB)
        {
            const float megabyteThreshold = 1024f;
            return sizeKB > megabyteThreshold
                ? $"{sizeKB / megabyteThreshold:F2} MB"
                : $"{sizeKB:F1} KB";
        }
    }
}
