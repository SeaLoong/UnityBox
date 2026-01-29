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

        [Tooltip("倒计时结束后触发的防御强度（调试模式下自动简化）")]
        [Range(0, 4)]
        public int defenseLevel = 4;

        [Tooltip("#{defense.use_custom_tooltip}")]
        public bool useCustomDefenseSettings = false;

        [Tooltip("#{defense.enable_cpu_defense_desc}")]
        public bool enableCpuDefense = true;

        [Tooltip("#{defense.constraint_chain_desc}")]
        public bool enableConstraintChain = true;

        [Range(10, 100)]
        [Tooltip("#{defense.constraint_depth_desc}")]
        public int constraintChainDepth = 50;

        [Tooltip("#{defense.phys_bone_desc}")]
        public bool enablePhysBone = true;

        [Range(10, 256)]
        [Tooltip("#{defense.phys_bone_length_desc}")]
        public int physBoneChainLength = 50;

        [Range(10, 256)]
        [Tooltip("#{defense.phys_bone_colliders_desc}")]
        public int physBoneColliderCount = 100;

        [Tooltip("#{defense.contact_system_desc}")]
        public bool enableContactSystem = true;

        [Range(10, 200)]
        [Tooltip("#{defense.contact_count_desc}")]
        public int contactComponentCount = 150;

        [Tooltip("#{defense.enable_gpu_defense_desc}")]
        public bool enableGpuDefense = true;

        [Tooltip("#{defense.heavy_shader_desc}")]
        public bool enableHeavyShader = true;

        [Range(0, 100000000)]
        [Tooltip("#{defense.shader_loops_desc}")]
        public int shaderLoopCount = 50000000; // 5000万次循环（无VRChat限制）

        [Tooltip("#{defense.overdraw_desc}")]
        public bool enableOverdraw = true;

        [Range(5, 50000)]
        [Tooltip("#{defense.overdraw_layers_desc}")]
        public int overdrawLayerCount = 50000; // 5万层叠加（受25MB文件大小限制）

        [Tooltip("#{defense.high_poly_desc}")]
        public bool enableHighPolyMesh = true;

        [Range(50000, 500000)]
        [Tooltip("#{defense.high_poly_vertices_desc}")]
        public int highPolyVertexCount = 500000; // 50万顶点（受25MB文件大小限制）

        [Range(1, 100)]
        [Tooltip("同时创建的Constraint链数量（每条链都会消耗CPU）")]
        public int constraintChainCount = 5;

        [Range(1, 100)]
        [Tooltip("同时创建的PhysBone链数量（每条链都会消耗CPU）")]
        public int physBoneChainCount = 5;

        [Tooltip("是否启用粒子系统防御（高CPU消耗）")]
        public bool enableParticleDefense = true;

        [Range(1000, 5000000)]
        [Tooltip("粒子系统生成的粒子总数")]
        public int particleCount = 2000000;

        [Range(1, 500)]
        [Tooltip("粒子系统的数量（分散粒子计算）")]
        public int particleSystemCount = 200;

        [Tooltip("是否启用光源防御（GPU消耗，实时阴影）")]
        public bool enableLightDefense = true;

        [Range(1, 500)]
        [Tooltip("创建的高质量光源数量")]
        public int lightCount = 200;

        [Range(1, 5000)]
        [Tooltip("材质球数量（使用高消耗Shader）")]
        public int materialCount = 2000;

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

            // 如果不使用自定义设置，则根据防御等级自动配置参数
            if (!useCustomDefenseSettings)
            {
                ApplyDefenseLevelConfiguration();
            }
        }

        /// <summary>
        /// 根据defenseLevel自动应用防御配置
        /// 如果启用了自定义防御，还会根据enableCpuDefense和enableGpuDefense进行过滤
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
                    physBoneChainLength = 30;
                    physBoneColliderCount = 50;
                    enableContactSystem = true;
                    contactComponentCount = 80;
                    enableHeavyShader = false;
                    enableOverdraw = false;
                    enableHighPolyMesh = false;
                    break;

                case 2: // CPU + 基础GPU防御
                    enableConstraintChain = true;
                    constraintChainDepth = 50;
                    enablePhysBone = true;
                    physBoneChainLength = 60;
                    physBoneColliderCount = 100;
                    enableContactSystem = true;
                    contactComponentCount = 120;
                    enableHeavyShader = true;
                    shaderLoopCount = 150;
                    enableOverdraw = true;
                    overdrawLayerCount = 20;
                    enableHighPolyMesh = false;
                    break;

                case 3: // CPU + 加强GPU防御
                    enableConstraintChain = true;
                    constraintChainDepth = 75;
                    constraintChainCount = 3; // 3条constraint链
                    enablePhysBone = true;
                    physBoneChainLength = 120;
                    physBoneChainCount = 3; // 3条PhysBone链
                    physBoneColliderCount = 150;
                    enableContactSystem = true;
                    contactComponentCount = 160;
                    enableHeavyShader = true;
                    shaderLoopCount = 300;
                    enableOverdraw = true;
                    overdrawLayerCount = 75; // 增加到75层
                    enableHighPolyMesh = true;
                    highPolyVertexCount = 600000; // 增加到600000
                    enableParticleDefense = true;
                    particleCount = 10000; // 10000个粒子
                    enableLightDefense = true;
                    lightCount = 6; // 6个光源
                    break;

                case 4: // 最大防御强度 - 所有参数拉满到安全上限
                    enableConstraintChain = true;
                    constraintChainDepth = 100; // 上限
                    constraintChainCount = 100; // 100条constraint链
                    enablePhysBone = true;
                    physBoneChainLength = 256; // 上限
                    physBoneChainCount = 100; // 100条PhysBone链（受256总限制）
                    physBoneColliderCount = 256; // 上限
                    enableContactSystem = true;
                    contactComponentCount = 200; // 上限
                    enableHeavyShader = true;
                    shaderLoopCount = 1000000; // 100万次循环（构建优化，从5000万降低，GPU仍有过载效果）
                    enableOverdraw = true;
                    overdrawLayerCount = 500; // 500层（构建优化，从1万降到500层）
                    enableHighPolyMesh = true;
                    highPolyVertexCount = 200000; // 20万顶点（构建优化，从50万降低，仍有高面数效果）
                    enableParticleDefense = true;
                    particleCount = 500000; // 50万粒子（构建优化，从200万降低）
                    particleSystemCount = 200; // 200个粒子系统
                    enableLightDefense = true;
                    lightCount = 50; // 50个光源（构建优化，从200降低）
                    materialCount = 500; // 500个材质球（构建优化，从2000降低）
                    break;
            }

            // 确保参数不超过限制
            EnforceDefenseParameterLimits();
        }

        /// <summary>
        /// 强制限制防御参数在合理范围内（无VRChat硬限制）
        /// VRChat 不限制顶点/Shader/Overdraw，限制来自文件大小(25MB)
        /// </summary>
        private void EnforceDefenseParameterLimits()
        {
            // Constraint - VRChat无限制，编辑器端可优化
            constraintChainDepth = Mathf.Clamp(constraintChainDepth, 10, 100);
            constraintChainCount = Mathf.Clamp(constraintChainCount, 1, 100);

            // PhysBone - VRChat无限制
            physBoneChainLength = Mathf.Clamp(physBoneChainLength, 10, 256);
            physBoneChainCount = Mathf.Clamp(physBoneChainCount, 1, 100);
            physBoneColliderCount = Mathf.Clamp(physBoneColliderCount, 10, 256);

            // Contact - VRChat无限制
            contactComponentCount = Mathf.Clamp(contactComponentCount, 10, 200);

            // Shader 循环 - VRChat完全无限制，仅限GPU计算能力
            shaderLoopCount = Mathf.Clamp(shaderLoopCount, 0, 100000000);

            // Overdraw层数 - 受25MB文件大小限制
            // 每个Quad约64字节，25MB ≈ 40万层，安全限制5万层
            overdrawLayerCount = Mathf.Clamp(overdrawLayerCount, 5, 50000);

            // 高多边形顶点 - 受25MB文件大小限制
            // 每个顶点约32字节，25MB ≈ 80万顶点，安全限制50万顶点
            highPolyVertexCount = Mathf.Clamp(highPolyVertexCount, 50000, 500000);

            // 粒子参数 - VRChat无限制
            particleCount = Mathf.Clamp(particleCount, 1000, 5000000);
            particleSystemCount = Mathf.Clamp(particleSystemCount, 1, 500);

            // 光源 - VRChat无限制
            lightCount = Mathf.Clamp(lightCount, 1, 500);

            // 材质球 - VRChat无限制
            materialCount = Mathf.Clamp(materialCount, 1, 5000);
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
            defenseLevel = 4;
        }
#endif
    }
}
