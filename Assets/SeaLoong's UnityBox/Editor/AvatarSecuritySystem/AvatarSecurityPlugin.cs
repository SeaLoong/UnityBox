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
            InPhase(BuildPhase.Optimizing).Run("Generate ASS", ctx =>
            {
                var assConfig = ExtractASSConfiguration(ctx);
                if (assConfig == null)
                {
                    return;
                }

                if (!HasValidPassword(assConfig))
                {
                    Debug.Log(I18n.T("log.plugin_password_empty"));
                    return;
                }

                if (!ShouldGenerateInCurrentMode(assConfig))
                {
                    return;
                }

                ExecuteGeneration(ctx, assConfig);
            });
        }

        private AvatarSecuritySystemComponent ExtractASSConfiguration(BuildContext ctx)
        {
            var avatarRoot = ctx.AvatarRootObject;
            var assConfig = avatarRoot.GetComponent<AvatarSecuritySystemComponent>();

            if (assConfig == null || !assConfig.IsPasswordValid())
            {
                Debug.Log(I18n.T("log.not_found"));
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

        private void ExecuteGeneration(BuildContext ctx, AvatarSecuritySystemComponent config)
        {
            Debug.Log(I18n.T("log.generating"));
            LoadAudioResources(config);

            bool isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

            try
            {
                GenerateSystem(ctx, config, isPlayMode);
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
        private void GenerateSystem(BuildContext ctx, AvatarSecuritySystemComponent config, bool isPlayMode)
        {
            string modeMsg = isPlayMode ? I18n.T("log.play_mode_test") : I18n.T("log.build_mode_full");
            Debug.Log(modeMsg);

            var descriptor = ExtractAvatarDescriptor(ctx);
            if (descriptor == null)
            {
                return;
            }

            var fxController = GetOrCreateFXController(descriptor);
            AddVRChatBuiltinParameters(fxController);
            CreateVisualFeedback(ctx, config);
            SetupAudioSources(ctx);
            
            var lockResult = new LockSystem.LockLayerResult();
            var layerNames = GenerateSystemLayers(fxController, ctx, config, isPlayMode, ref lockResult);
            ConfigureLayerWeights(fxController, lockResult, layerNames);
            RegisterASSParameters(descriptor, config);
            LockLayerWeights(fxController, lockResult, layerNames, isPlayMode, config);
            
            SaveAndOptimize(fxController);
        }

        private VRCAvatarDescriptor ExtractAvatarDescriptor(BuildContext ctx)
        {
            var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) Debug.LogError(I18n.T("log.plugin_no_descriptor"));
            return descriptor;
        }

        private void AddVRChatBuiltinParameters(AnimatorController fxController) =>
            AnimatorUtils.AddParameterIfNotExists(fxController, Constants.PARAM_IS_LOCAL,
                AnimatorControllerParameterType.Bool, defaultBool: false);

        private void CreateVisualFeedback(BuildContext ctx, AvatarSecuritySystemComponent config)
        {
            var canvasObj = FeedbackSystem.CreateHUDCanvas(ctx.AvatarRootObject, config);
            FeedbackSystem.CreateCountdownBar(canvasObj, config);
        }

        private void SetupAudioSources(BuildContext ctx)
        {
#if VRC_SDK_VRCSDK3
            AnimatorUtils.SetupAudioSource(ctx.AvatarRootObject, Constants.GO_AUDIO);
#endif
        }

        private List<string> GenerateSystemLayers(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, bool isPlayMode, ref LockSystem.LockLayerResult lockResult)
        {
            var layerNames = new List<string>();

            AddInitialLockLayer(fxController, ctx, config, layerNames, ref lockResult);
            AddPasswordLayer(fxController, ctx, config, layerNames);
            AddCountdownLayer(fxController, ctx, config, layerNames);
            AddWarningAudioLayer(fxController, ctx, config, layerNames);
            AddDefenseLayer(fxController, ctx, config, isPlayMode, layerNames);

            return layerNames;
        }

        private void AddInitialLockLayer(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, List<string> layerNames, ref LockSystem.LockLayerResult lockResult)
        {
            lockResult = LockSystem.CreateLockLayer(fxController, ctx.AvatarRootObject, config);
            fxController.AddLayer(lockResult.Layer);
            layerNames.Add(lockResult.Layer.name);
        }

        private void AddPasswordLayer(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var passwordLayer = GesturePasswordSystem.CreatePasswordLayer(fxController, ctx.AvatarRootObject, config);
            fxController.AddLayer(passwordLayer);
            layerNames.Add(passwordLayer.name);
        }

        private void AddCountdownLayer(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var countdownLayer = CountdownSystem.CreateCountdownLayer(fxController, ctx.AvatarRootObject, config);
            fxController.AddLayer(countdownLayer);
            layerNames.Add(countdownLayer.name);
        }

        private void AddWarningAudioLayer(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, List<string> layerNames)
        {
            var warningAudioLayer = CountdownSystem.CreateWarningAudioLayer(fxController, ctx.AvatarRootObject, config);
            fxController.AddLayer(warningAudioLayer);
            layerNames.Add(warningAudioLayer.name);
        }

        private void AddDefenseLayer(AnimatorController fxController, BuildContext ctx,
            AvatarSecuritySystemComponent config, bool isPlayMode, List<string> layerNames)
        {
            if (config.disableDefense)
            {
                return;
            }

            try
            {
                var defenseLayer = DefenseSystem.CreateDefenseLayer(fxController, ctx.AvatarRootObject, config,
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
                Debug.LogError($"[ASS] 防御层创建失败: {ex.Message}");
            }
        }

        private void ConfigureLayerWeights(AnimatorController fxController, LockSystem.LockLayerResult lockResult,
            List<string> layerNames)
        {
            LockSystem.ConfigureLayerWeightControl(fxController, lockResult, layerNames.ToArray());
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
#if VRC_SDK_VRCSDK3
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters 不存在，无法注册参数");
                return;
            }

            var existingParams = GetExistingParameters(expressionParameters);
            RemoveExistingASSParameters(existingParams);

            var assParams = CreateASSParameters();
            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();

            EditorUtility.SetDirty(expressionParameters);
            LogParameterRegistration();
#else
            Debug.LogWarning("[ASS] VRC SDK 不可用，无法注册参数");
#endif
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
            string message = $"[ASS] 已注册 ASS 参数到 VRCExpressionParameters 开头位置: " +
                            $"{Constants.PARAM_PASSWORD_CORRECT}(同步), {Constants.PARAM_TIME_UP}(本地)";
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
            Debug.Log($"[ASS] 音频资源加载完成：{loadedCount}/2 个文件");

            if (loadedCount < 2)
            {
                Debug.LogWarning(I18n.T("log.plugin_audio_missing"));
            }
        }
    }
}
#endif
