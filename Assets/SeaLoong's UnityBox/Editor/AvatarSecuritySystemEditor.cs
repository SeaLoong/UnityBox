using UnityEngine;
using UnityEditor;
using System.Linq;
using SeaLoongUnityBox.AvatarSecuritySystem.Editor;

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
                ASSI18n.SetLanguage(_target.uiLanguage);
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
            EditorGUILayout.LabelField($"🔒 {ASSI18n.T("system.name")} ({ASSI18n.T("system.short_name")})", style);
            EditorGUILayout.LabelField(ASSI18n.T("system.subtitle"), EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawWarningBox()
        {
            EditorGUILayout.HelpBox(ASSI18n.T("warning.main"), MessageType.Warning);
        }

        private void DrawPasswordSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("password.config"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useRightHand"), 
                new GUIContent(ASSI18n.T("password.use_right_hand"), ASSI18n.T("password.use_right_hand_tooltip")));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(ASSI18n.T("password.sequence"), EditorStyles.miniLabel);

            var passwordProp = serializedObject.FindProperty("gesturePassword");
            
            // 显示当前密码
            for (int i = 0; i < passwordProp.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format(ASSI18n.T("password.step"), i + 1), GUILayout.Width(60));
                
                var element = passwordProp.GetArrayElementAtIndex(i);
                int currentValue = element.intValue;
                int newValue = EditorGUILayout.IntPopup(currentValue, GESTURE_NAMES, 
                    new int[] { 1, 2, 3, 4, 5, 6, 7 });
                element.intValue = newValue;

                if (GUILayout.Button(new GUIContent("X", ASSI18n.T("password.delete_step")), GUILayout.Width(30)))
                {
                    passwordProp.DeleteArrayElementAtIndex(i);
                }

                EditorGUILayout.EndHorizontal();
            }

            // 添加按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(ASSI18n.T("password.add_gesture")))
            {
                passwordProp.InsertArrayElementAtIndex(passwordProp.arraySize);
                passwordProp.GetArrayElementAtIndex(passwordProp.arraySize - 1).intValue = 1;
            }
            if (GUILayout.Button(ASSI18n.T("password.clear")))
            {
                if (EditorUtility.DisplayDialog(ASSI18n.T("common.warning"), 
                    ASSI18n.T("password.clear_confirm"), 
                    ASSI18n.T("common.confirm"), 
                    ASSI18n.T("common.cancel")))
                {
                    passwordProp.ClearArray();
                }
            }
            EditorGUILayout.EndHorizontal();

            // 密码强度或空密码提示
            if (_target.gesturePassword.Count == 0)
            {
                // 0位密码：显示禁用提示
                EditorGUILayout.HelpBox("⚠️ " + ASSI18n.T("password.empty_warning"), 
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
                EditorGUILayout.HelpBox(string.Format(ASSI18n.T("password.strength"), 
                    ASSI18n.T(strengthKey), _target.gesturePassword.Count), 
                    MessageType.Info);
                GUI.color = oldColor;
            }
        }

        private void DrawCountdownSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("countdown.config"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("countdownDuration"),
                new GUIContent(ASSI18n.T("countdown.duration"), ASSI18n.T("countdown.duration_tooltip")));
        }

        private void DrawLanguageSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🌐 " + ASSI18n.T("language.title"), GUILayout.Width(120));
            
            var languageProp = serializedObject.FindProperty("uiLanguage");
            SystemLanguage currentLang = (SystemLanguage)languageProp.intValue;
            
            // 如果是 Unknown，显示当前系统语言
            if (currentLang == SystemLanguage.Unknown)
            {
                currentLang = Application.systemLanguage;
            }
            
            string[] languageNames = { 
                ASSI18n.T("language.auto"), 
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
                ASSI18n.SetLanguage(languageValues[newIndex]);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_target);
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDefenseSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("defense.config"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stateCount"),
                new GUIContent(ASSI18n.T("defense.state_count"), ASSI18n.T("defense.state_count_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("particleSystemCount"),
                new GUIContent(ASSI18n.T("defense.particle_count"), ASSI18n.T("defense.particle_count_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("extraMaterialCount"),
                new GUIContent(ASSI18n.T("defense.material_count"), ASSI18n.T("defense.material_count_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pointLightCount"),
                new GUIContent(ASSI18n.T("defense.light_count"), ASSI18n.T("defense.light_count_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableClothCountermeasure"),
                new GUIContent(ASSI18n.T("defense.cloth_enabled"), ASSI18n.T("defense.cloth_enabled_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clothVertexCount"),
                new GUIContent(ASSI18n.T("defense.cloth_vertex_count"), ASSI18n.T("defense.cloth_vertex_count_tooltip")));
        }

        private void DrawDebugSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("advanced.debug_options"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableInPlayMode"),
                new GUIContent(ASSI18n.T("advanced.play_mode"), ASSI18n.T("advanced.play_mode_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("unlimitedPasswordTime"),
                new GUIContent(ASSI18n.T("advanced.unlimited_time"), ASSI18n.T("advanced.unlimited_time_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableCountermeasures"),
                new GUIContent(ASSI18n.T("advanced.disable_defense"), ASSI18n.T("advanced.disable_defense_tooltip")));
        }

        private void DrawLockSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("advanced.lock_options"), EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("invertParameters"),
                new GUIContent(ASSI18n.T("advanced.invert_params"), ASSI18n.T("advanced.invert_params_tooltip")));
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableRootChildren"),
                new GUIContent(ASSI18n.T("advanced.disable_objects"), ASSI18n.T("advanced.disable_objects_tooltip")));
        }

        private void DrawEstimationSection()
        {
            EditorGUILayout.LabelField(ASSI18n.T("estimate.title"), EditorStyles.boldLabel);
            
            float sizeKB = _target.EstimateFileSizeKB();
            string sizeText = sizeKB > 1024 ? $"{sizeKB / 1024f:F2} MB" : $"{sizeKB:F1} KB";
            
            EditorGUILayout.HelpBox($"📊 {ASSI18n.T("estimate.file_size")}: {sizeText}", 
                MessageType.None);
        }
    }
}
