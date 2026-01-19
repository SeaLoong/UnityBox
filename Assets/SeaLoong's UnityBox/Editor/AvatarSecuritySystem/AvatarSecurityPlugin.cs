#if NDMF_AVAILABLE
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(SeaLoongUnityBox.AvatarSecuritySystem.Editor.AvatarSecurityPlugin))]

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    using AvatarSecuritySystemComponent = SeaLoongUnityBox.AvatarSecuritySystemComponent;

    /// <summary>
    /// Avatar Security System (ASS) NDMF 插件
    /// 在构建时生成完整的安全系统
    /// </summary>
    public class AvatarSecurityPlugin : Plugin<AvatarSecurityPlugin>
    {
        public override string DisplayName => ASSConstants.SYSTEM_NAME;
        public override string QualifiedName => ASSConstants.PLUGIN_QUALIFIED_NAME;

        protected override void Configure()
        {
            // 在 Optimizing 阶段生成安全系统
            InPhase(BuildPhase.Optimizing).Run("Generate ASS", ctx =>
            {
                var avatarRoot = ctx.AvatarRootObject;
                var assConfig = avatarRoot.GetComponent<AvatarSecuritySystemComponent>();

                // 如果没有组件或密码配置无效，跳过
                if (assConfig == null || !assConfig.IsPasswordValid())
                {
                    Debug.Log(ASSI18n.T("log.not_found"));
                    return;
                }

                // 如果密码为空（0位），等同于不启用ASS，直接跳过
                if (assConfig.gesturePassword == null || assConfig.gesturePassword.Count == 0)
                {
                    Debug.Log(ASSI18n.T("log.plugin_password_empty"));
                    return;
                }

                Debug.Log(ASSI18n.T("log.generating"));

                // 自动加载音频资源
                LoadAudioResources(assConfig);

                // 检测是否在 Play 模式
                bool isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

                if (isPlayMode && !assConfig.enableInPlayMode)
                {
                    Debug.Log(ASSI18n.T("log.plugin_play_disabled"));
                    return;
                }

                // 显示确认对话框（仅构建模式）
                if (!isPlayMode)
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        ASSI18n.T("build.confirm_title"),
                        string.Format(ASSI18n.T("build.confirm_message"),
                            assConfig.gesturePassword.Count,
                            assConfig.countdownDuration,
                            assConfig.stateCount,
                            assConfig.EstimateFileSizeKB()),
                        ASSI18n.T("build.continue"),
                        ASSI18n.T("common.cancel")
                    );

                    if (!confirmed)
                    {
                        throw new System.Exception("[ASS] Build cancelled by user");
                    }
                }

                // 生成系统
                try
                {
                    GenerateSystem(ctx, assConfig, isPlayMode);


                    Debug.Log(ASSI18n.T("log.complete"));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ASS] Generation failed: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            });
        }

        /// <summary>
        /// 统一的系统生成方法
        /// </summary>
        /// <param name="ctx">构建上下文</param>
        /// <param name="config">配置组件</param>
        /// <param name="isPlayMode">是否为播放模式（调试用）</param>
        private void GenerateSystem(BuildContext ctx, AvatarSecuritySystemComponent config, bool isPlayMode)
        {
            Debug.Log(isPlayMode ? ASSI18n.T("log.play_mode_test") : ASSI18n.T("log.build_mode_full"));

            var avatarRoot = ctx.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();

            if (descriptor == null)
            {
                Debug.LogError(ASSI18n.T("log.plugin_no_descriptor"));
                return;
            }

            // 1. 构建模式：创建防御用的 GameObject（粒子系统、Draw Calls、光源、Cloth）
            if (!isPlayMode && !config.disableCountermeasures)
            {
                DefenseSystem.CreateParticleSystemObjects(avatarRoot, config);
                DefenseSystem.CreateDrawCallObjects(avatarRoot, config);
                DefenseSystem.CreateLightObjects(avatarRoot, config);
                DefenseSystem.CreateClothObjects(avatarRoot, config);
            }

            // 2. 获取或创建 FX Controller
            var fxController = GetOrCreateFXController(descriptor);

            // 添加VRChat内置参数（如果不存在）
            ASSAnimatorUtils.AddParameterIfNotExists(fxController, ASSConstants.PARAM_IS_LOCAL, 
                AnimatorControllerParameterType.Bool, defaultBool: false);

            // 3. 创建UI Canvas和视觉反馈
            var canvasObj = FeedbackSystem.CreateHUDCanvas(avatarRoot, config);
            FeedbackSystem.CreateCountdownBar(canvasObj, config);

            // 4. 设置AudioSource（必须在生成层之前创建）
#if VRC_SDK_VRCSDK3
            ASSAnimatorUtils.SetupAudioSource(avatarRoot, ASSConstants.GO_FEEDBACK_AUDIO);
            ASSAnimatorUtils.SetupAudioSource(avatarRoot, ASSConstants.GO_WARNING_AUDIO);
#endif

            // 5. 生成所有系统层
            var lockLayer = InitialLockSystem.CreateLockLayer(fxController, avatarRoot, config);
            fxController.AddLayer(lockLayer);

            var passwordLayer = GesturePasswordSystem.CreatePasswordLayer(fxController, avatarRoot, config);
            fxController.AddLayer(passwordLayer);

            // 倒计时层
            CreateCountdownLayerByMode(fxController, avatarRoot, config, isPlayMode);

            // 警告音效层（独立层，与倒计时层并行工作）
            if (!config.unlimitedPasswordTime && config.warningBeep != null)
            {
                var warningAudioLayer = CountdownSystem.CreateWarningAudioLayer(fxController, avatarRoot, config);
                fxController.AddLayer(warningAudioLayer);
            }

            // 不再创建独立的FeedbackLayer，反馈已集成到各状态中

            // 防御层
            if (!config.disableCountermeasures)
            {
                // 播放模式：简化版防御；构建模式：完整防御
                var defenseLayer = DefenseSystem.CreateDefenseLayer(fxController, avatarRoot, config, isDebugMode: isPlayMode);
                fxController.AddLayer(defenseLayer);
                
                if (isPlayMode)
                {
                    Debug.Log(ASSI18n.T("log.play_mode_simplified"));
                }
            }

            // 6. 构建模式：反转参数（如果启用）
            if (!isPlayMode && config.invertParameters)
            {
                InitialLockSystem.InvertAvatarParameters(avatarRoot, config);
            }

            // 7. 保存
            ASSAnimatorUtils.SaveAndRefresh();
            ASSAnimatorUtils.LogOptimizationStats(fxController);
        }

        /// <summary>
        /// 根据模式创建倒计时层
        /// </summary>
        private void CreateCountdownLayerByMode(
            AnimatorController fxController, 
            GameObject avatarRoot, 
            AvatarSecuritySystemComponent config, 
            bool isPlayMode)
        {
            if (!config.unlimitedPasswordTime)
            {
                // 正常倒计时模式
                var countdownLayer = CountdownSystem.CreateCountdownLayer(fxController, avatarRoot, config);
                fxController.AddLayer(countdownLayer);
            }
            else
            {
                // 无限时间模式
                if (isPlayMode)
                {
                    // 播放模式：创建循环倒计时（仅用于显示效果）
                    var countdownLayer = CountdownSystem.CreateLoopingCountdownLayer(fxController, avatarRoot, config);
                    fxController.AddLayer(countdownLayer);
                    
                    // 添加TimeUp参数（防御层会用到，但永远不会触发）
                    ASSAnimatorUtils.AddParameterIfNotExists(fxController, ASSConstants.PARAM_TIME_UP,
                        AnimatorControllerParameterType.Bool, defaultBool: false);
                }
                
                Debug.Log(ASSI18n.T("log.debug_mode"));
            }
        }

        /// <summary>
        /// 获取或创建 FX Controller
        /// </summary>
        private AnimatorController GetOrCreateFXController(VRCAvatarDescriptor descriptor)
        {
            // 获取现有的 FX Controller
            var fxLayer = descriptor.baseAnimationLayers
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);

            AnimatorController controller = null;

            if (fxLayer.animatorController != null && fxLayer.animatorController is AnimatorController)
            {
                controller = fxLayer.animatorController as AnimatorController;
                Debug.Log($"[ASS] 使用现有 FX Controller: {controller.name}");
            }
            else
            {
                // 创建新的 FX Controller
                string path = $"{ASSConstants.ASSET_FOLDER}/{ASSConstants.CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(ASSConstants.ASSET_FOLDER);
                
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                fxLayer.animatorController = controller;
                fxLayer.isDefault = false;

                Debug.Log($"[ASS] 创建新 FX Controller: {path}");
            }

            return controller;
        }

        /// <summary>
        /// 从 Resources 文件夹自动加载音频资源
        /// </summary>
        private void LoadAudioResources(AvatarSecuritySystemComponent config)
        {
            if (config == null)
            {
                Debug.LogWarning(ASSI18n.T("log.plugin_config_empty"));
                return;
            }

            // 加载每步输入成功提示音
            config.stepSuccessSound = Resources.Load<AudioClip>($"{ASSConstants.AUDIO_RESOURCE_PATH}/{ASSConstants.AUDIO_STEP_SUCCESS}");
            
            // 加载密码成功音效
            config.successSound = Resources.Load<AudioClip>($"{ASSConstants.AUDIO_RESOURCE_PATH}/{ASSConstants.AUDIO_PASSWORD_SUCCESS}");
            
            // 加载错误音效
            config.errorSound = Resources.Load<AudioClip>($"{ASSConstants.AUDIO_RESOURCE_PATH}/{ASSConstants.AUDIO_INPUT_ERROR}");
            
            // 加载倒计时警告音效
            config.warningBeep = Resources.Load<AudioClip>($"{ASSConstants.AUDIO_RESOURCE_PATH}/{ASSConstants.AUDIO_COUNTDOWN_WARNING}");

            // 验证
            int loadedCount = 0;
            if (config.stepSuccessSound != null) loadedCount++;
            if (config.successSound != null) loadedCount++;
            if (config.errorSound != null) loadedCount++;
            if (config.warningBeep != null) loadedCount++;

            Debug.Log($"[ASS] 音频资源加载完成：{loadedCount}/4 个文件");
            
            if (loadedCount < 4)
            {
                Debug.LogWarning(ASSI18n.T("log.plugin_audio_missing"));
            }
        }
    }
}
#endif
