#if NDMF_AVAILABLE
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

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
        public override string DisplayName => Constants.SYSTEM_NAME;
        public override string QualifiedName => Constants.PLUGIN_QUALIFIED_NAME;

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
                    Debug.Log(I18n.T("log.not_found"));
                    return;
                }

                // 如果密码为空（0位），等同于不启用ASS，直接跳过
                if (assConfig.gesturePassword == null || assConfig.gesturePassword.Count == 0)
                {
                    Debug.Log(I18n.T("log.plugin_password_empty"));
                    return;
                }

                Debug.Log(I18n.T("log.generating"));

                // 自动加载音频资源
                LoadAudioResources(assConfig);

                // 检测是否在 Play 模式
                bool isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

                if (isPlayMode && !assConfig.enableInPlayMode)
                {
                    Debug.Log(I18n.T("log.plugin_play_disabled"));
                    return;
                }

                // 显示确认对话框（仅构建模式）
                if (!isPlayMode)
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        I18n.T("build.confirm_title"),
                        string.Format(I18n.T("build.confirm_message"),
                            assConfig.gesturePassword.Count,
                            assConfig.countdownDuration,
                            assConfig.defenseLevel,
                            assConfig.EstimateFileSizeKB()),
                        I18n.T("build.continue"),
                        I18n.T("common.cancel")
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


                    Debug.Log(I18n.T("log.complete"));
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
            Debug.Log(isPlayMode ? I18n.T("log.play_mode_test") : I18n.T("log.build_mode_full"));

            var avatarRoot = ctx.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();

            if (descriptor == null)
            {
                Debug.LogError(I18n.T("log.plugin_no_descriptor"));
                return;
            }

            // 1. 获取或创建 FX Controller
            var fxController = GetOrCreateFXController(descriptor);

            // 添加VRChat内置参数（如果不存在）
            AnimatorUtils.AddParameterIfNotExists(fxController, Constants.PARAM_IS_LOCAL, 
                AnimatorControllerParameterType.Bool, defaultBool: false);

            // 2. 创建UI Canvas和视觉反馈
            var canvasObj = FeedbackSystem.CreateHUDCanvas(avatarRoot, config);
            FeedbackSystem.CreateCountdownBar(canvasObj, config);

            // 3. 设置AudioSource（必须在生成层之前创建）
#if VRC_SDK_VRCSDK3
            AnimatorUtils.SetupAudioSource(avatarRoot, Constants.GO_FEEDBACK_AUDIO);
            AnimatorUtils.SetupAudioSource(avatarRoot, Constants.GO_WARNING_AUDIO);
#endif

            // 4. 生成所有系统层（所有模式都生成相同的层，只是内部状态不同）
            var lockLayer = InitialLockSystem.CreateLockLayer(fxController, avatarRoot, config);
            fxController.AddLayer(lockLayer);

                var passwordLayer = GesturePasswordSystem.CreatePasswordLayer(fxController, avatarRoot, config);
            fxController.AddLayer(passwordLayer);

            // 倒计时层（所有模式都生成）
            bool useLooping = config.unlimitedPasswordTime && isPlayMode;
            var countdownLayer = CountdownSystem.CreateCountdownLayer(fxController, avatarRoot, config, isLooping: useLooping);
            fxController.AddLayer(countdownLayer);

            // 警告音效层（所有模式都生成，只要配置了警告音）
            if (config.warningBeep != null)
            {
                var warningAudioLayer = CountdownSystem.CreateWarningAudioLayer(fxController, avatarRoot, config, isLooping: useLooping);
                fxController.AddLayer(warningAudioLayer);
            }

            // 防御层（所有模式都生成）
            if (!config.disableDefense)
            {
                var defenseLayer = DefenseSystem.CreateDefenseLayer(fxController, avatarRoot, config, isDebugMode: isPlayMode);
                fxController.AddLayer(defenseLayer);
                
                if (isPlayMode)
                {
                    Debug.Log(I18n.T("log.play_mode_simplified"));
                }
            }

            // 5. 构建模式：反转参数（如果启用）
            if (!isPlayMode && config.invertParameters)
            {
                InitialLockSystem.InvertAvatarParameters(avatarRoot, config);
            }

            // 7. 保存
            AnimatorUtils.SaveAndRefresh();
            AnimatorUtils.LogOptimizationStats(fxController);
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
                string path = $"{Constants.ASSET_FOLDER}/{Constants.CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(Constants.ASSET_FOLDER);
                
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
                Debug.LogWarning(I18n.T("log.plugin_config_empty"));
                return;
            }

            // 加载每步输入成功提示音
            config.stepSuccessSound = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_STEP_SUCCESS}");
            
            // 加载密码成功音效
            config.successSound = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_PASSWORD_SUCCESS}");
            
            // 加载错误音效
            config.errorSound = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_INPUT_ERROR}");
            
            // 加载倒计时警告音效
            config.warningBeep = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_COUNTDOWN_WARNING}");

            // 验证
            int loadedCount = 0;
            if (config.stepSuccessSound != null) loadedCount++;
            if (config.successSound != null) loadedCount++;
            if (config.errorSound != null) loadedCount++;
            if (config.warningBeep != null) loadedCount++;

            Debug.Log($"[ASS] 音频资源加载完成：{loadedCount}/4 个文件");
            
            if (loadedCount < 4)
            {
                Debug.LogWarning(I18n.T("log.plugin_audio_missing"));
            }
        }
    }
}
#endif
