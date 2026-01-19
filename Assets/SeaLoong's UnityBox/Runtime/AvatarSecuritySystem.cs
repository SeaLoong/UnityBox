using UnityEngine;
using System.Collections.Generic;

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

        // 输入间隔固定为0.5秒
        [HideInInspector] public float inputCooldown = 0.5f;

        // 音频资源（由 Editor 自动从 Resources 加载）
        [HideInInspector] public AudioClip errorSound;
        [HideInInspector] public AudioClip stepSuccessSound;
        [HideInInspector] public AudioClip warningBeep;
        [HideInInspector] public AudioClip successSound;

        [Range(1000, 50000)]
        [Tooltip("#{defense.state_count_tooltip}")]
        public int stateCount = 10000;

        [Range(0, 100)]
        [Tooltip("#{defense.particle_count_tooltip}")]
        public int particleSystemCount = 50;

        [Range(0, 200)]
        [Tooltip("#{defense.material_count_tooltip}")]
        public int extraMaterialCount = 100;

        [Range(0, 50)]
        [Tooltip("#{defense.light_count_tooltip}")]
        public int pointLightCount = 20;

        [Tooltip("#{defense.cloth_enabled_tooltip}")]
        public bool enableClothCountermeasure = true;

        [Range(1000, 50000)]
        [Tooltip("#{defense.cloth_vertex_count_tooltip}")]
        public int clothVertexCount = 10000;

        [Tooltip("#{advanced.play_mode_tooltip}")]
        public bool enableInPlayMode = true;

        [Tooltip("#{advanced.unlimited_time_tooltip}")]
        public bool unlimitedPasswordTime = true;

        [Tooltip("#{advanced.disable_defense_tooltip}")]
        public bool disableCountermeasures = false;

        [Tooltip("#{advanced.invert_params_tooltip}")]
        public bool invertParameters = true;

        [Tooltip("#{advanced.disable_objects_tooltip}")]
        public bool disableRootChildren = true;

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
            float stateSize = stateCount * 1.5f; // 每个状态约1.5KB
            float audioSize = 0f;

            if (errorSound != null) audioSize += 15f;
            if (warningBeep != null) audioSize += 10f;
            if (successSound != null) audioSize += 20f;

            return baseSize + stateSize + audioSize;
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
        }

        private void Reset()
        {
            // 默认配置
            gesturePassword = new List<int> { 1, 7, 2, 4 };
            countdownDuration = 30f;
            warningThreshold = 10f;
            stateCount = 10000;
            particleSystemCount = 50;
            extraMaterialCount = 100;
            pointLightCount = 20;
            enableClothCountermeasure = true;
            clothVertexCount = 10000;
            enableInPlayMode = true;
            unlimitedPasswordTime = true;
            invertParameters = true;
            disableRootChildren = true;
        }
#endif
    }
}
