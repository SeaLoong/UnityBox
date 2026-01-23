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

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.hold_time_tooltip}")]
        public float gestureHoldTime = 0.15f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.error_tolerance_tooltip}")]
        public float gestureErrorTolerance = 0.3f;

        // 音频资源（由 Editor 自动从 Resources 加载）
        [HideInInspector] public AudioClip errorSound;
        [HideInInspector] public AudioClip stepSuccessSound;
        [HideInInspector] public AudioClip warningBeep;
        [HideInInspector] public AudioClip successSound;

        [Tooltip("#{advanced.play_mode_tooltip}")]
        public bool enableInPlayMode = true;

        [Tooltip("#{advanced.unlimited_time_tooltip}")]
        public bool unlimitedPasswordTime = true;

        [Tooltip("#{advanced.disable_defense_tooltip}")]
        public bool disableDefense = false;

        [Tooltip("#{advanced.invert_params_tooltip}")]
        public bool invertParameters = true;

        [Tooltip("#{advanced.disable_objects_tooltip}")]
        public bool disableRootChildren = true;

        [Tooltip("倒计时结束后触发的防御强度（调试模式下自动简化）")]
        [Range(0, 4)]
        public int defenseLevel = 4;

        [Tooltip("#{defense.use_custom_tooltip}")]
        public bool useCustomDefenseSettings = false;

        [Tooltip("#{defense.constraint_chain_desc}")]
        public bool enableConstraintChain = true;

        [Range(10, 100)]
        [Tooltip("#{defense.constraint_depth_desc}")]
        public int constraintChainDepth = 50;

        [Tooltip("#{defense.phys_bone_desc}")]
        public bool enablePhysBone = true;

        [Range(5, 50)]
        [Tooltip("#{defense.phys_bone_length_desc}")]
        public int physBoneChainLength = 20;

        [Range(0, 100)]
        [Tooltip("#{defense.phys_bone_colliders_desc}")]
        public int physBoneColliderCount = 50;

        [Tooltip("#{defense.contact_system_desc}")]
        public bool enableContactSystem = true;

        [Range(10, 200)]
        [Tooltip("#{defense.contact_count_desc}")]
        public int contactComponentCount = 100;

        [Tooltip("#{defense.heavy_shader_desc}")]
        public bool enableHeavyShader = true;

        [Range(0, 200)]
        [Tooltip("#{defense.shader_loops_desc}")]
        public int shaderLoopCount = 100;

        [Tooltip("#{defense.overdraw_desc}")]
        public bool enableOverdraw = true;

        [Range(5, 50)]
        [Tooltip("#{defense.overdraw_layers_desc}")]
        public int overdrawLayerCount = 20;

        [Tooltip("#{defense.high_poly_desc}")]
        public bool enableHighPolyMesh = true;

        [Range(10000, 200000)]
        [Tooltip("#{defense.high_poly_vertices_desc}")]
        public int highPolyVertexCount = 100000;

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

            if (errorSound != null) audioSize += 15f;
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

            // 如果不使用自定义设置，则根据防御等级自动配置参数
            if (!useCustomDefenseSettings)
            {
                ApplyDefenseLevelConfiguration();
            }
        }

        /// <summary>
        /// 根据defenseLevel自动应用防御配置
        /// </summary>
        private void ApplyDefenseLevelConfiguration()
        {
            switch (defenseLevel)
            {
                case 0: // 禁用所有防御
                    enableConstraintChain = false;
                    enablePhysBone = false;
                    enableContactSystem = false;
                    enableHeavyShader = false;
                    enableOverdraw = false;
                    enableHighPolyMesh = false;
                    break;

                case 1: // 基础CPU防御
                    enableConstraintChain = true;
                    constraintChainDepth = 30;
                    enablePhysBone = true;
                    physBoneChainLength = 10;
                    physBoneColliderCount = 30;
                    enableContactSystem = true;
                    contactComponentCount = 50;
                    enableHeavyShader = false;
                    enableOverdraw = false;
                    enableHighPolyMesh = false;
                    break;

                case 2: // CPU + 基础GPU防御
                    enableConstraintChain = true;
                    constraintChainDepth = 50;
                    enablePhysBone = true;
                    physBoneChainLength = 20;
                    physBoneColliderCount = 50;
                    enableContactSystem = true;
                    contactComponentCount = 100;
                    enableHeavyShader = true;
                    shaderLoopCount = 50;
                    enableOverdraw = true;
                    overdrawLayerCount = 10;
                    enableHighPolyMesh = false;
                    break;

                case 3: // CPU + 加强GPU防御
                    enableConstraintChain = true;
                    constraintChainDepth = 70;
                    enablePhysBone = true;
                    physBoneChainLength = 30;
                    physBoneColliderCount = 70;
                    enableContactSystem = true;
                    contactComponentCount = 150;
                    enableHeavyShader = true;
                    shaderLoopCount = 120;
                    enableOverdraw = true;
                    overdrawLayerCount = 25;
                    enableHighPolyMesh = true;
                    highPolyVertexCount = 120000;
                    break;

                case 4: // 最大防御强度
                    enableConstraintChain = true;
                    constraintChainDepth = 100;
                    enablePhysBone = true;
                    physBoneChainLength = 50;
                    physBoneColliderCount = 100;
                    enableContactSystem = true;
                    contactComponentCount = 200;
                    enableHeavyShader = true;
                    shaderLoopCount = 200;
                    enableOverdraw = true;
                    overdrawLayerCount = 50;
                    enableHighPolyMesh = true;
                    highPolyVertexCount = 200000;
                    break;
            }
        }

        private void Reset()
        {
            // 默认配置
            gesturePassword = new List<int> { 1, 7, 2, 4 };
            countdownDuration = 30f;
            warningThreshold = 10f;
            enableInPlayMode = true;
            unlimitedPasswordTime = true;
            invertParameters = true;
            disableRootChildren = true;
            defenseLevel = 4;
        }
#endif
    }
}
