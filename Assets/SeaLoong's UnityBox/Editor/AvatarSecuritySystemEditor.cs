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
    public class AvatarSecuritySystemEditor : Editor
    {
        private AvatarSecuritySystemComponent _target;
        private static readonly string[] GESTURE_NAMES = new string[]
        {
            "1: Fist ✊", "2: HandOpen ✋", "3: Fingerpoint ☝",
            "4: Victory ✌", "5: RockNRoll 🤘", "6: HandGun 🔫", "7: ThumbsUp 👍"
        };

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
            DrawTitle();
            EditorGUILayout.Space(5);
            
            DrawLanguageSelector();
            EditorGUILayout.Space(10);

            DrawPasswordSection();
            EditorGUILayout.Space(10);

            DrawCountdownSection();
            EditorGUILayout.Space(10);

            DrawDefenseSection();
            EditorGUILayout.Space(10);

            DrawDebugSection();
            EditorGUILayout.Space(10);

            DrawLockSection();
            EditorGUILayout.Space(10);

            DrawEstimationSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTitle()
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField($"🔒 {T("system.name")} ({T("system.short_name")})", style);
            EditorGUILayout.LabelField(T("system.subtitle"), EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawWarningBox()
        {
            EditorGUILayout.HelpBox(T("warning.main"), MessageType.Warning);
        }

        private void DrawPasswordSection()
        {
            EditorGUILayout.LabelField(T("password.config"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useRightHand"), 
                new GUIContent(T("password.use_right_hand"), T("password.use_right_hand_tooltip")));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("password.sequence"), EditorStyles.miniLabel);

            var passwordProp = serializedObject.FindProperty("gesturePassword");
            
            // 显示当前密码
            for (int i = 0; i < passwordProp.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format(T("password.step"), i + 1), GUILayout.Width(60));
                
                var element = passwordProp.GetArrayElementAtIndex(i);
                int currentValue = element.intValue;
                int newValue = EditorGUILayout.IntPopup(currentValue, GESTURE_NAMES, 
                    new int[] { 1, 2, 3, 4, 5, 6, 7 });
                element.intValue = newValue;

                if (GUILayout.Button(new GUIContent("X", T("password.delete_step")), GUILayout.Width(30)))
                {
                    passwordProp.DeleteArrayElementAtIndex(i);
                }

                EditorGUILayout.EndHorizontal();
            }

            // 添加按钮
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

            // 密码强度或空密码提示
            if (_target.gesturePassword.Count == 0)
            {
                // 0位密码：显示禁用提示
                EditorGUILayout.HelpBox("⚠️ " + T("password.empty_warning"), 
                    MessageType.Warning);
            }
            else
            {
                // 有密码：显示强度
                string strength = _target.GetPasswordStrength();
                string strengthKey = strength == "Strong" ? "password.strength.strong" : 
                                    strength == "Medium" ? "password.strength.medium" : "password.strength.weak";
                Color strengthColor = strength == "Strong" ? Color.green : 
                                      strength == "Medium" ? Color.yellow : Color.red;
                
                var oldColor = GUI.color;
                GUI.color = strengthColor;
                EditorGUILayout.HelpBox(string.Format(T("password.strength"), 
                    T(strengthKey), _target.gesturePassword.Count), 
                    MessageType.Info);
                GUI.color = oldColor;
            }
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
            SystemLanguage currentLang = (SystemLanguage)languageProp.intValue;
            
            // 如果是 Unknown，显示当前系统语言
            if (currentLang == SystemLanguage.Unknown)
            {
                currentLang = Application.systemLanguage;
            }
            
            string[] languageNames = { 
                T("language.auto"), 
                "简体中文", 
                "English", 
                "日本語" 
            };
            SystemLanguage[] languageValues = { 
                SystemLanguage.Unknown, 
                SystemLanguage.ChineseSimplified, 
                SystemLanguage.English, 
                SystemLanguage.Japanese 
            };
            
            int currentIndex = System.Array.IndexOf(languageValues, (SystemLanguage)languageProp.intValue);
            if (currentIndex < 0) currentIndex = 0;
            
            int newIndex = EditorGUILayout.Popup(currentIndex, languageNames);
            if (newIndex != currentIndex)
            {
                languageProp.intValue = (int)languageValues[newIndex];
                SetLanguage(languageValues[newIndex]);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_target);
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDefenseSection()
        {
            EditorGUILayout.LabelField(T("defense.config"), EditorStyles.boldLabel);

            // 防御等级选择
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defenseLevel"),
                new GUIContent(T("defense.level"), T("defense.level_tooltip")));

            int defenseLevel = serializedObject.FindProperty("defenseLevel").intValue;
            
            // 显示等级说明
            EditorGUILayout.Space(3);
            EditorGUILayout.HelpBox(GetDefenseLevelDescription(defenseLevel), MessageType.Info);
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
        }

        private void DrawLockSection()
        {
            EditorGUILayout.LabelField(T("advanced.lock_options"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lockFxLayers"),
                new GUIContent(T("advanced.lock_fx_layers"), T("advanced.lock_fx_layers_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableRootChildren"),
                new GUIContent(T("advanced.disable_objects"), T("advanced.disable_objects_tooltip")));
        }

        private void DrawEstimationSection()
        {
            EditorGUILayout.LabelField(T("estimate.title"), EditorStyles.boldLabel);
            
            float sizeKB = _target.EstimateFileSizeKB();
            string sizeText = sizeKB > 1024 ? $"{sizeKB / 1024f:F2} MB" : $"{sizeKB:F1} KB";
            
            EditorGUILayout.HelpBox($"📊 {T("estimate.file_size")}: {sizeText}", 
                MessageType.None);
        }
    }
}
