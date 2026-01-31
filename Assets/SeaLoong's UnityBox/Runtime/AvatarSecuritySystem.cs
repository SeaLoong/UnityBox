using UnityEngine;
using System.Collections.Generic;
#if VRC_SDK_VRCSDK3
using VRC.SDKBase;
#endif

namespace SeaLoongUnityBox
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
    [AddComponentMenu("SeaLoong's UnityBox/Avatar Security System (ASS)")]
#endif
    public class AvatarSecuritySystemComponent : MonoBehaviour
#if VRC_SDK_VRCSDK3
        , IEditorOnly
#endif
    {
        [Tooltip("#{language.ui_language_tooltip}")]
        public SystemLanguage uiLanguage = SystemLanguage.Unknown; // Unknown 表示跟随系统语言

        [Tooltip("#{password.gesture_tooltip}")]
        public List<int> gesturePassword = new List<int> { 1, 7, 2, 4 }; // 默认：拳头→大拉指→张开手→胜利

        [Tooltip("#{password.use_right_hand_tooltip}")]
        public bool useRightHand = false;

        [Range(30f, 120f)]
        [Tooltip("#{countdown.duration_tooltip}")]
        public float countdownDuration = 30f;

        // 警告阈值固定为10秒
        [HideInInspector] public float warningThreshold = 10f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.hold_time_tooltip}")]
        public float gestureHoldTime = 0.15f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.error_tolerance_tooltip}")]
        public float gestureErrorTolerance = 0.3f;

        // 音频资源（由 Editor 自动从 Resources 加载）
        [HideInInspector] public AudioClip warningBeep;
        [HideInInspector] public AudioClip successSound;

        [Tooltip("#{advanced.play_mode_tooltip}")]
        public bool enableInPlayMode = true;

        [Tooltip("#{advanced.disable_defense_tooltip}")]
        public bool disableDefense = false;

        [Tooltip("#{advanced.lock_fx_layers_tooltip}")]
        public bool lockFxLayers = true;

        [Tooltip("#{advanced.disable_objects_tooltip}")]
        public bool disableRootChildren = true;

        [Tooltip("防御等级：0=仅密码, 1=密码+CPU防御, 2=密码+CPU+GPU(中低), 3=密码+CPU+GPU(最高)")]
        [Range(0, 3)]
        public int defenseLevel = 3;

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
            if (gestureIndex < 0 || gestureIndex > 7) return "Invalid";
            return ((VRChatGesture)gestureIndex).ToString();
        }

        /// <summary>
        /// 验证密码配置是否有效
        /// 0位密码表示不启用ASS，也是合法的
        /// </summary>
        public bool IsPasswordValid()
        {
            if (gesturePassword == null || gesturePassword.Count == 0)
                return true; // 0位密码合法，表示不启用ASS

            // 有密码时，验证所有手势必须在 1-7 范围内
            foreach (int gesture in gesturePassword)
            {
                if (gesture < 1 || gesture > 7)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 获取密码强度评级
        /// </summary>
        public string GetPasswordStrength()
        {
            if (!IsPasswordValid()) return "Invalid";

            int length = gesturePassword.Count;
            int uniqueGestures = new HashSet<int>(gesturePassword).Count;

            if (length < 4) return "Weak";
            if (length >= 6 && uniqueGestures >= 4) return "Strong";
            return "Medium";
        }

        /// <summary>
        /// 预估生成的文件大小（KB）
        /// </summary>
        public float EstimateFileSizeKB()
        {
            float baseSize = 100f; // 基础动画和状态机
            float audioSize = 0f;

            if (warningBeep != null) audioSize += 10f;
            if (successSound != null) audioSize += 20f;

            return baseSize + audioSize;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 如果密码为null，设置默认密码
            if (gesturePassword == null)
                gesturePassword = new List<int> { 1, 7, 2, 4 };

            // 确保阈值逻辑正确
            if (warningThreshold > countdownDuration)
                warningThreshold = countdownDuration / 2f;

            // 确保防御等级在有效范围内
            defenseLevel = Mathf.Clamp(defenseLevel, 0, 3);
        }

        private void Reset()
        {
            // 默认配置
            gesturePassword = new List<int> { 1, 7, 2, 4 };
            countdownDuration = 30f;
            warningThreshold = 10f;
            enableInPlayMode = true;
            lockFxLayers = true;
            disableRootChildren = true;
            defenseLevel = 3;
        }
#endif
    }
}
