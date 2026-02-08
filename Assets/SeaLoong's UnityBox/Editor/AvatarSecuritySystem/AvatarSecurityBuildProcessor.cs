using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Avatar Security System (ASS) VRCSDK 构建处理器 — 系统入口点
    /// 
    /// 实现 IVRCSDKPreprocessAvatarCallback 在 VRChat Avatar 构建/上传时自动注入安全系统。
    /// callbackOrder = 10000 确保在 NDMF (-1024~0) 和 VRCFury (~0) 之后执行，
    /// 以避免被其他预处理器覆盖生成的 Animator 层和组件。
    /// 
    /// 执行流程：
    /// 1. 从 Avatar 根对象提取 AvatarSecuritySystemComponent 配置
    /// 2. 验证密码有效性和运行模式（PlayMode 可选跳过）
    /// 3. 从 Resources 加载音频资源（成功音效、警告蜂鸣）
    /// 4. 获取或创建 FX AnimatorController
    /// 5. 注册 VRChat 内置参数（IsLocal）
    /// 6. 创建视觉反馈（3D HUD 进度条 + 遮挡 Mesh）
    /// 7. 创建 AudioSource 对象
    /// 8. 按顺序生成 5 个子系统层：Lock → Password → Countdown → Audio → Defense
    /// 9. 配置 Lock 层权重控制（锁定/解锁时切换其他层的权重）
    /// 10. 注册 ASS 参数到 VRCExpressionParameters
    /// 11. 保存并输出优化统计
    /// </summary>
    public class AvatarSecurityBuildProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // 使用较晚的 callbackOrder，确保在 NDMF/VRCFury 之后执行
        // NDMF 通常使用 -1024 到 0 的范围
        // VRCFury 通常使用类似范围
        // 我们使用 10000 确保在它们之后
        public int callbackOrder => 10000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Debug.Log($"[ASS] OnPreprocessAvatar called (callbackOrder={callbackOrder})");
            
            try
            {
                return ProcessAvatar(avatarGameObject);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] Avatar processing failed: {ex.Message}\n{ex.StackTrace}");
                // 返回 true 继续构建，不阻止上传
                return true;
            }
        }

        private bool ProcessAvatar(GameObject avatarGameObject)
        {
            var assConfig = ExtractASSConfiguration(avatarGameObject);
            if (assConfig == null)
            {
                return true; // 无ASS组件，跳过
            }

            if (!HasValidPassword(assConfig))
            {
                Debug.Log(I18n.T("log.plugin_password_empty"));
                return true;
            }

            if (!ShouldGenerateInCurrentMode(assConfig))
            {
                return true;
            }

            ExecuteGeneration(avatarGameObject, assConfig);
            return true; // 返回 true 继续构建
        }

        private AvatarSecuritySystemComponent ExtractASSConfiguration(GameObject avatarGameObject)
        {
            var assConfig = avatarGameObject.GetComponent<AvatarSecuritySystemComponent>();

            if (assConfig == null)
            {
                Debug.Log(I18n.T("log.not_found"));
                return null;
            }
            
            if (!assConfig.IsPasswordValid())
            {
                Debug.LogWarning("[ASS] Password configuration is invalid");
                return null;
            }

            return assConfig;
        }

        private bool HasValidPassword(AvatarSecuritySystemComponent config) =>
            config.gesturePassword != null && config.gesturePassword.Count > 0;

        private bool ShouldGenerateInCurrentMode(AvatarSecuritySystemComponent config)
        {
            bool isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
            if (isPlayMode && !config.enableInPlayMode)
            {
                Debug.Log(I18n.T("log.plugin_play_disabled"));
                return false;
            }

            return true;
        }

        private void ExecuteGeneration(GameObject avatarGameObject, AvatarSecuritySystemComponent config)
        {
            Debug.Log(I18n.T("log.generating"));
            LoadAudioResources(config);

            bool isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

            try
            {
                GenerateSystem(avatarGameObject, config, isPlayMode);
                Debug.Log(I18n.T("log.complete"));
            }
            catch (System.Exception ex)
            {
                HandleGenerationError(ex);
            }
        }

        private void HandleGenerationError(System.Exception ex) =>
            Debug.LogError($"[ASS] Generation failed: {ex.Message}\n{ex.StackTrace}");

        /// <summary>
        /// 统一的系统生成方法
        /// </summary>
        private void GenerateSystem(GameObject avatarGameObject, AvatarSecuritySystemComponent config, bool isPlayMode)
        {
            string modeMsg = isPlayMode ? I18n.T("log.play_mode_test") : I18n.T("log.build_mode_full");
            Debug.Log(modeMsg);

            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogError(I18n.T("log.plugin_no_descriptor"));
                return;
            }

            var fxController = GetOrCreateFXController(descriptor);
            AddVRChatBuiltinParameters(fxController);
            if (!config.debugSkipFeedbackSystem)
            {
                CreateVisualFeedback(avatarGameObject, config);
                SetupAudioSources(avatarGameObject);
            }

            LockSystem.LockLayerResult lockResult = null;
            var layerNames = GenerateSystemLayers(fxController, avatarGameObject, config, isPlayMode, ref lockResult);
            if (lockResult?.Layer != null)
            {
                LockSystem.ConfigureLockLayerWeight(fxController, lockResult);
            }
            RegisterASSParameters(descriptor, config);
            if (lockResult?.Layer != null)
            {
                LockLayerWeights(fxController, lockResult, layerNames, isPlayMode, config);
            }

            SaveAndOptimize(fxController);
        }

        private void AddVRChatBuiltinParameters(AnimatorController fxController) =>
            AnimatorUtils.AddParameterIfNotExists(fxController, Constants.PARAM_IS_LOCAL,
                AnimatorControllerParameterType.Bool, defaultBool: false);

        private void CreateVisualFeedback(GameObject avatarGameObject, AvatarSecuritySystemComponent config)
        {
            var canvasObj = FeedbackSystem.CreateHUDCanvas(avatarGameObject, config);
            FeedbackSystem.CreateCountdownBar(canvasObj, config);
        }

        private void SetupAudioSources(GameObject avatarGameObject)
        {
            AnimatorUtils.SetupAudioSource(avatarGameObject, Constants.GO_AUDIO_WARNING);
            AnimatorUtils.SetupAudioSource(avatarGameObject, Constants.GO_AUDIO_SUCCESS);
        }

        private List<string> GenerateSystemLayers(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, bool isPlayMode, ref LockSystem.LockLayerResult lockResult)
        {
            var layerNames = new List<string>();

            if (!config.debugSkipLockSystem)
            {
                AddLockLayer(fxController, avatarGameObject, config, layerNames, ref lockResult);
            }
            if (!config.debugSkipPasswordSystem)
            {
                AddPasswordLayer(fxController, avatarGameObject, config, layerNames);
            }
            if (!config.debugSkipCountdownSystem)
            {
                AddCountdownLayer(fxController, avatarGameObject, config, layerNames);
            }
            if (!config.debugSkipFeedbackSystem)
            {
                AddAudioLayer(fxController, avatarGameObject, config, layerNames);
            }
            if (!config.debugSkipDefenseSystem)
            {
                AddDefenseLayer(fxController, avatarGameObject, config, isPlayMode, layerNames);
            }

            return layerNames;
        }

        private void AddLockLayer(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, List<string> layerNames, ref LockSystem.LockLayerResult lockResult)
        {
            lockResult = LockSystem.CreateLockLayer(fxController, avatarGameObject, config);
            fxController.AddLayer(lockResult.Layer);
            layerNames.Add(lockResult.Layer.name);
        }

        private void AddPasswordLayer(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var passwordLayer = GesturePasswordSystem.CreatePasswordLayer(fxController, avatarGameObject, config);
            if (passwordLayer == null)
            {
                return;
            }
            fxController.AddLayer(passwordLayer);
            layerNames.Add(passwordLayer.name);
        }

        private void AddCountdownLayer(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var countdownLayer = CountdownSystem.CreateCountdownLayer(fxController, avatarGameObject, config);
            if (countdownLayer == null)
            {
                return;
            }
            fxController.AddLayer(countdownLayer);
            layerNames.Add(countdownLayer.name);
        }

        private void AddAudioLayer(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var audioLayer = CountdownSystem.CreateAudioLayer(fxController, avatarGameObject, config);
            if (audioLayer == null)
            {
                return;
            }
            fxController.AddLayer(audioLayer);
            layerNames.Add(audioLayer.name);
        }

        private void AddDefenseLayer(AnimatorController fxController, GameObject avatarGameObject,
            AvatarSecuritySystemComponent config, bool isPlayMode, List<string> layerNames)
        {
            if (config.disableDefense || config.debugSkipDefenseSystem)
            {
                return;
            }

            try
            {
                var defenseLayer = DefenseSystem.CreateDefenseLayer(fxController, avatarGameObject, config,
                    isDebugMode: isPlayMode);

                if (defenseLayer != null)
                {
                    fxController.AddLayer(defenseLayer);
                    layerNames.Add(defenseLayer.name);

                    if (isPlayMode)
                    {
                        Debug.Log(I18n.T("log.play_mode_simplified"));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] Defense layer creation failed: {ex.Message}");
            }
        }

        private void LockLayerWeights(AnimatorController fxController, LockSystem.LockLayerResult lockResult,
            List<string> layerNames, bool isPlayMode, AvatarSecuritySystemComponent config)
        {
            if (isPlayMode || !config.lockFxLayers)
            {
                return;
            }

            LockSystem.LockFxLayerWeights(fxController, lockResult, layerNames.ToArray());
        }

        private void SaveAndOptimize(AnimatorController fxController)
        {
            AnimatorUtils.SaveAndRefresh();
            AnimatorUtils.LogOptimizationStats(fxController);
        }

        /// <summary>
        /// 注册 ASS 参数到 VRCExpressionParameters
        /// </summary>
        private void RegisterASSParameters(VRCAvatarDescriptor descriptor, AvatarSecuritySystemComponent config)
        {
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters not found, cannot register parameters");
                return;
            }

            var existingParams = GetExistingParameters(expressionParameters);
            RemoveExistingASSParameters(existingParams);

            var assParams = CreateASSParameters();
            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();

            EditorUtility.SetDirty(expressionParameters);
            LogParameterRegistration();
        }

        private List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter> GetExistingParameters(
            VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expressionParameters)
        {
            return expressionParameters.parameters?.ToList()
                ?? new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();
        }

        private void RemoveExistingASSParameters(
            List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter> parameters)
        {
            parameters.RemoveAll(p =>
                p.name == Constants.PARAM_PASSWORD_CORRECT ||
                p.name == Constants.PARAM_TIME_UP);
        }

        private List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter> CreateASSParameters()
        {
            return new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>
            {
                CreatePasswordCorrectParameter(),
                CreateTimeUpParameter()
            };
        }

        private VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter CreatePasswordCorrectParameter()
        {
            return new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
            {
                name = Constants.PARAM_PASSWORD_CORRECT,
                valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = true,
                networkSynced = true
            };
        }

        private VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter CreateTimeUpParameter()
        {
            return new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
            {
                name = Constants.PARAM_TIME_UP,
                valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = false,
                networkSynced = false
            };
        }

        private void LogParameterRegistration()
        {
            string message = $"[ASS] Registered ASS parameters: " +
                            $"{Constants.PARAM_PASSWORD_CORRECT}(synced), {Constants.PARAM_TIME_UP}(local)";
            Debug.Log(message);
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
                Debug.Log($"[ASS] Using existing FX Controller: {controller.name}");
            }
            else
            {
                // 创建新的 FX Controller
                string path = $"{Constants.ASSET_FOLDER}/{Constants.CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(Constants.ASSET_FOLDER);

                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                
                // 更新 descriptor 的引用
                var layers = descriptor.baseAnimationLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    {
                        layers[i].animatorController = controller;
                        layers[i].isDefault = false;
                        break;
                    }
                }
                descriptor.baseAnimationLayers = layers;

                Debug.Log($"[ASS] Created new FX Controller: {path}");
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

            config.successSound = LoadAudioClip(Constants.AUDIO_PASSWORD_SUCCESS);
            config.warningBeep = LoadAudioClip(Constants.AUDIO_COUNTDOWN_WARNING);

            int loadedCount = CountLoadedAudioClips(config);
            LogAudioLoadingResult(loadedCount);
        }

        private AudioClip LoadAudioClip(string audioFileName)
        {
            string resourcePath = $"{Constants.AUDIO_RESOURCE_PATH}/{audioFileName}";
            return Resources.Load<AudioClip>(resourcePath);
        }

        private int CountLoadedAudioClips(AvatarSecuritySystemComponent config)
        {
            int count = 0;
            if (config.successSound != null) count++;
            if (config.warningBeep != null) count++;
            return count;
        }

        private void LogAudioLoadingResult(int loadedCount)
        {
            Debug.Log($"[ASS] Audio resources loaded: {loadedCount}/2 files");

            if (loadedCount < 2)
            {
                Debug.LogWarning(I18n.T("log.plugin_audio_missing"));
            }
        }
    }
}
