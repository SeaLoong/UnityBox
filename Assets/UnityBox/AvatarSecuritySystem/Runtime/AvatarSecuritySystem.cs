using UnityEngine;
using System.Collections.Generic;
using VRC.SDKBase;

namespace UnityBox.AvatarSecuritySystem
{
    /// <summary>
    /// Avatar Security System (ASS) - 防止盗模的密码保护系统
    /// 
    /// 工作原理：
    /// 1. Avatar 启动时所有对象被禁用，参数被反转
    /// 2. 同时开始倒计时（默认30秒）
    /// 3. 用户必须在倒计时结束前输入正确的手势密码
    /// 4. 只有倒计时到0才会触发反制措施
    /// </summary>
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Avatar Security System (ASS)")]
#endif
    public class AvatarSecuritySystemComponent : MonoBehaviour, IEditorOnly
    {
        // ============ UI 语言配置 ============
        [Tooltip("#{language.ui_language_tooltip}")]
        public SystemLanguage uiLanguage = SystemLanguage.Unknown;

        // ============ 密码配置 ============
        [Tooltip("#{password.gesture_tooltip}")]
        public List<int> gesturePassword = new List<int> { 1, 7, 2, 4 };

        [Tooltip("#{password.use_right_hand_tooltip}")]
        public bool useRightHand = false;

        // ============ 倒计时配置 ============
        [Range(30f, 120f)]
        [Tooltip("#{countdown.duration_tooltip}")]
        public float countdownDuration = 30f;

        [HideInInspector] public float warningThreshold = 10f;

        // ============ 手势识别配置 ============
        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.hold_time_tooltip}")]
        public float gestureHoldTime = 0.15f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.error_tolerance_tooltip}")]
        public float gestureErrorTolerance = 0.3f;

        // ============ 音频资源 ============
        [HideInInspector] public AudioClip warningBeep;
        [HideInInspector] public AudioClip successSound;

        // ============ 高级选项 ============
        [Tooltip("#{advanced.play_mode_tooltip}")]
        public bool disabledInPlaymode = true;

        [Tooltip("#{advanced.disable_defense_tooltip}")]
        public bool disableDefense = false;

        [Tooltip("#{advanced.disable_objects_tooltip}")]
        public bool disableRootChildren = true;

        [Tooltip("防御等级：0=仅密码, 1=密码+CPU防御, 2=密码+CPU+GPU防御")]
        [Range(0, 2)]
        public int defenseLevel = 2;

        /// <summary>
        /// Write Defaults 模式
        /// </summary>
        public enum WriteDefaultsMode
        {
            /// <summary>Auto: 自动检测 FX Controller 中已有层的 WD 设置</summary>
            Auto,
            /// <summary>WD On: 不写入原始值，依赖动画系统自动恢复</summary>
            On,
            /// <summary>WD Off: 显式写入原始值，允许其他系统修改</summary>
            Off
        }

        [Tooltip("动画 Write Defaults 模式：\nAuto = 自动检测(推荐)\nOn = 自动恢复\nOff = 显式恢复")]
        public WriteDefaultsMode writeDefaultsMode = WriteDefaultsMode.Auto;



        /// <summary>
        /// VRChat 手势枚举
        /// </summary>
        public enum VRChatGesture
        {
            Idle = 0,
            Fist = 1,
            HandOpen = 2,
            Fingerpoint = 3,
            Victory = 4,
            RockNRoll = 5,
            HandGun = 6,
            ThumbsUp = 7
        }

        /// <summary>
        /// 获取手势名称（用于UI显示）
        /// </summary>
        public static string GetGestureName(int gestureIndex)
        {
            const int minimumGestureIndex = 0;
            const int maximumGestureIndex = 7;

            if (gestureIndex < minimumGestureIndex || gestureIndex > maximumGestureIndex)
            {
                return "Invalid";
            }

            return ((VRChatGesture)gestureIndex).ToString();
        }

        /// <summary>
        /// 验证密码配置是否有效
        /// 0位密码表示不启用ASS，也是合法的
        /// </summary>
        public bool IsPasswordValid()
        {
            if (gesturePassword == null || gesturePassword.Count == 0)
            {
                return true; // 0位密码合法，表示不启用ASS
            }

            return AreAllGesturesValid();
        }

        private bool AreAllGesturesValid()
        {
            const int minimumGestureValue = 1;
            const int maximumGestureValue = 7;

            foreach (int gesture in gesturePassword)
            {
                if (gesture < minimumGestureValue || gesture > maximumGestureValue) return false;
            }

            return true;
        }

        /// <summary>
        /// 获取密码强度评级
        /// </summary>
        public string GetPasswordStrength()
        {
            if (!IsPasswordValid()) return "Invalid";
            if (gesturePassword.Count == 0) return "Weak";

            int passwordLength = gesturePassword.Count;
            int uniqueGestureCount = new HashSet<int>(gesturePassword).Count;

            if (passwordLength >= 6 && uniqueGestureCount >= 4) return "Strong";
            if (passwordLength >= 4) return "Medium";

            return "Weak";
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InitializePasswordIfNeeded();
            ValidateWarningThreshold();
            ClampDefenseLevel();
        }

        private void InitializePasswordIfNeeded()
        {
            if (gesturePassword == null)
            {
                gesturePassword = new List<int> { 1, 7, 2, 4 };
            }
        }

        private void ValidateWarningThreshold()
        {
            if (warningThreshold > countdownDuration)
            {
                warningThreshold = countdownDuration / 2f;
            }
        }

        private void ClampDefenseLevel()
        {
            const int minimumLevel = 0;
            const int maximumLevel = 2;
            defenseLevel = Mathf.Clamp(defenseLevel, minimumLevel, maximumLevel);
        }

        private void Reset()
        {
            const float defaultCountdownDuration = 30f;
            const float defaultWarningThreshold = 10f;
            const int defaultDefenseLevel = 2;

            gesturePassword = new List<int> { 1, 7, 2, 4 };
            countdownDuration = defaultCountdownDuration;
            warningThreshold = defaultWarningThreshold;
            disabledInPlaymode = true;
            disableRootChildren = true;
            defenseLevel = defaultDefenseLevel;
        }
#endif
    }
}
