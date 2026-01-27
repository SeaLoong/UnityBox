#if NDMF_AVAILABLE
using System.Collections.Generic;
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

            // 4. 生成所有系统层
            // 注意：锁定层需要先创建，但层权重控制需要在所有层添加后配置
            var lockResult = InitialLockSystem.CreateLockLayer(fxController, avatarRoot, config);
            fxController.AddLayer(lockResult.Layer);

            var passwordLayer = GesturePasswordSystem.CreatePasswordLayer(fxController, avatarRoot, config);
            fxController.AddLayer(passwordLayer);

            // 倒计时层
            var countdownLayer = CountdownSystem.CreateCountdownLayer(fxController, avatarRoot, config);
            fxController.AddLayer(countdownLayer);

            // 警告音效层（只要配置了警告音）
            AnimatorControllerLayer warningAudioLayer = null;
            if (config.warningBeep != null)
            {
                warningAudioLayer = CountdownSystem.CreateWarningAudioLayer(fxController, avatarRoot, config);
                fxController.AddLayer(warningAudioLayer);
            }

            // 防御层（所有模式都生成）
            AnimatorControllerLayer defenseLayer = null;
            if (!config.disableDefense)
            {
                defenseLayer = DefenseSystem.CreateDefenseLayer(fxController, avatarRoot, config, isDebugMode: isPlayMode);
                fxController.AddLayer(defenseLayer);
                
                if (isPlayMode)
                {
                    Debug.Log(I18n.T("log.play_mode_simplified"));
                }
            }

            // 5. 配置层权重控制（所有层已添加后）
            var assLayerNames = new List<string>
            {
                Constants.LAYER_INITIAL_LOCK,
                Constants.LAYER_PASSWORD_INPUT,
                Constants.LAYER_COUNTDOWN
            };
            if (warningAudioLayer != null)
                assLayerNames.Add(warningAudioLayer.name);
            if (defenseLayer != null)
                assLayerNames.Add(defenseLayer.name);
            
            InitialLockSystem.ConfigureLayerWeightControl(fxController, lockResult, assLayerNames.ToArray());

            // 6. 注册 ASS 参数到 VRCExpressionParameters（高优先级位置）
            RegisterASSParameters(descriptor, config);

            // 7. 锁定 FX 层权重（如果启用）
            if (!isPlayMode && config.lockFxLayers)
            {
                InitialLockSystem.LockFxLayerWeights(fxController, lockResult, assLayerNames.ToArray());
            }

            // 8. 保存
            AnimatorUtils.SaveAndRefresh();
            AnimatorUtils.LogOptimizationStats(fxController);
        }

        /// <summary>
        /// 注册 ASS 参数到 VRCExpressionParameters
        /// 直接修改 VRC 参数列表，将 ASS 参数插入到数组开头（高优先级位置）
        /// 参数配置说明：
        /// - ASS_PasswordCorrect: 同步参数(networkSynced=true)，让其他玩家看到解锁状态
        /// - ASS_TimeUp: 本地参数(networkSynced=false)，仅用于触发防御
        /// </summary>
        private void RegisterASSParameters(VRCAvatarDescriptor descriptor, AvatarSecuritySystemComponent config)
        {
#if VRC_SDK_VRCSDK3
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters 不存在，无法注册参数");
                return;
            }

            // 获取现有参数列表
            var existingParams = expressionParameters.parameters?.ToList() 
                ?? new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();

            // 移除已存在的 ASS 参数（避免重复）
            existingParams.RemoveAll(p => p.name == Constants.PARAM_PASSWORD_CORRECT || p.name == Constants.PARAM_TIME_UP);

            // 创建 ASS 参数
            var assParams = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>
            {
                // ASS_PasswordCorrect - 同步参数（其他玩家需要看到解锁状态）
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = Constants.PARAM_PASSWORD_CORRECT,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,      // 默认锁定
                    saved = true,           // 保存参数，切换世界后保持解锁状态
                    networkSynced = true    // 同步给其他玩家
                },
                // ASS_TimeUp - 本地参数（仅用于触发防御，不同步）
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = Constants.PARAM_TIME_UP,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false   // 本地参数，不同步
                }
            };

            // 将 ASS 参数插入到数组开头（高优先级位置，避免被压缩参数插件影响）
            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();

            EditorUtility.SetDirty(expressionParameters);
            Debug.Log($"[ASS] 已注册 ASS 参数到 VRCExpressionParameters 开头位置: " +
                     $"{Constants.PARAM_PASSWORD_CORRECT}(同步), {Constants.PARAM_TIME_UP}(本地)");
#else
            Debug.LogWarning("[ASS] VRC SDK 不可用，无法注册参数");
#endif
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

            // 加载密码成功音效
            config.successSound = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_PASSWORD_SUCCESS}");
            
            // 加载倒计时警告音效
            config.warningBeep = Resources.Load<AudioClip>($"{Constants.AUDIO_RESOURCE_PATH}/{Constants.AUDIO_COUNTDOWN_WARNING}");

            // 验证
            int loadedCount = 0;
            if (config.successSound != null) loadedCount++;
            if (config.warningBeep != null) loadedCount++;

            Debug.Log($"[ASS] 音频资源加载完成：{loadedCount}/2 个文件");
            
            if (loadedCount < 2)
            {
                Debug.LogWarning(I18n.T("log.plugin_audio_missing"));
            }
        }
    }
}
#endif
