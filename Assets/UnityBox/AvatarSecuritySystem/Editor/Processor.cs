using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Avatar Security System (ASS) VRCSDK 构建处理器 — 系统入口点
    /// 
    /// 实现 IVRCSDKPreprocessAvatarCallback 在 VRChat Avatar 构建/上传时自动注入安全系统。
    /// callbackOrder = -1026，在 NDMF PreprocessHook (-11000) 和 VRCFury 主处理 (-10000) 之后、
    /// NDMF OptimizeHook (-1025) 之前执行。
    /// VRCFury 参数压缩 (ParameterCompressorHook, int.MaxValue - 100) 在 ASS 之后很久才执行，
    /// 因此 ASS 注入的参数会被 VRCFury 正确识别和处理。
    /// 
    /// 执行流程：
    /// 1. 从 Avatar 根对象提取 ASSComponent 配置
    /// 2. 验证密码有效性和运行模式（PlayMode 可选跳过）
    /// 3. 获取或创建 FX AnimatorController
    /// 4. 注册 VRChat 内置参数（IsLocal）
    /// 5. 创建视觉反馈（3D HUD 进度条 + 音频对象）
    /// 6. 按顺序生成子系统：Lock → Password → Countdown → Defense
    /// 7. 配置 Lock 层权重控制
    /// 8. 注册 ASS 参数到 VRCExpressionParameters
    /// 9. 保存并输出优化统计
    /// </summary>
    public class Processor : IVRCSDKPreprocessAvatarCallback
    {
        // callbackOrder = -1026: 在 NDMF Optimize (-1025) 之前执行
        // 构建管线执行顺序：
        //   int.MinValue+1 : VRCFury FailureCheckStart / IsActuallyUploadingHook
        //   -11000 : NDMF PreprocessHook (Resolving → Transforming)
        //   -10000 : VRCFury VrcPreuploadHook (主处理)
        //   -1026  : ★ ASS（本插件）← 在这里
        //   -1025  : NDMF OptimizeHook (Optimizing → Last)
        //   -1024  : VRCFury VrcfRemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
        //   int.MaxValue-100 : VRCFury ParameterCompressorHook (参数压缩)
        //   int.MaxValue : VRCFury Cleanup / MA RemoveIEditorOnly
        // ASS 在 NDMF/VRCFury 的主处理完成后注入 Animator 层和参数，
        // VRCFury 参数压缩在 int.MaxValue-100 执行，远在 ASS 之后，
        // 因此 ASS 新增的参数不会导致压缩后参数超限
        public int callbackOrder => -1026;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Debug.Log($"[ASS] OnPreprocessAvatar called (callbackOrder={callbackOrder})");

            try
            {
                return ProcessAvatar(avatarGameObject);
            }
            catch (System.Exception ex)
            {
                // TODO: I18n
                Debug.LogError($"[ASS] Avatar processing failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private bool ProcessAvatar(GameObject avatarGameObject)
        {
            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogError("[ASS] VRCAvatarDescriptor not found");
                return true;
            }

            var assConfig = avatarGameObject.GetComponent<ASSComponent>();
            if (assConfig == null)
            {
                Debug.Log("[ASS] No valid AvatarSecuritySystem component found, skipping");
                return true;
            }

            if (!assConfig.IsPasswordValid())
            {
                // TODO: I18n
                Debug.LogWarning("[ASS] Password configuration is invalid");
                return false;
            }

            if (assConfig.gesturePassword == null || assConfig.gesturePassword.Count == 0)
            {
                Debug.Log("[ASS] Password is empty (0 digits), ASS is disabled. Skipping generation.");
                return true;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode && !assConfig.enabledInPlaymode)
            {
                Debug.Log("[ASS] Play mode disabled, skipping");
                return true;
            }

            Debug.Log("[ASS] Starting to generate security system...");

            var fxController = GetFXController(descriptor);
            Utils.AddParameterIfNotExists(fxController, PARAM_IS_LOCAL,
                AnimatorControllerParameterType.Bool, defaultBool: false);

            LoadAudioResources(assConfig);
            new Feedback(avatarGameObject, assConfig).Generate();

            var isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

            new Lock(fxController, avatarGameObject, assConfig, descriptor).Generate();
            new GesturePassword(fxController, avatarGameObject, assConfig).Generate();

            var countdown = new Countdown(fxController, avatarGameObject, assConfig);
            countdown.Generate();
            countdown.GenerateAudioLayer();

            if (!assConfig.disableDefense)
            {
                try
                {
                    new Defense(fxController, avatarGameObject, assConfig, isDebugMode: isPlayMode).Generate();
                    if (isPlayMode)
                    {
                        Debug.Log("[ASS] Play mode: Added simplified defense layer");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ASS] Defense layer creation failed: {ex.Message}");
                }
            }

            RegisterASSParameters(descriptor);

            Utils.SaveAndRefresh();
            Utils.LogOptimizationStats(fxController);
            Debug.Log("[ASS] Security system generation complete!");
            return true;
        }

        /// <summary>
        /// 注册 ASS 参数到 VRCExpressionParameters
        /// </summary>
        private void RegisterASSParameters(VRCAvatarDescriptor descriptor)
        {
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters not found, cannot register parameters");
                return;
            }

            var existingParams = expressionParameters.parameters?.ToList()
                ?? new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();

            existingParams.RemoveAll(p =>
                p.name == PARAM_PASSWORD_CORRECT ||
                p.name == PARAM_TIME_UP);

            var assParams = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_PASSWORD_CORRECT,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true
                },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_TIME_UP,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false
                }
            };

            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();

            EditorUtility.SetDirty(expressionParameters);
            Debug.Log($"[ASS] Registered ASS parameters: " +
                     $"{PARAM_PASSWORD_CORRECT}(synced), {PARAM_TIME_UP}(local)");
        }

        /// <summary>
        /// 获取 FX Controller
        /// </summary>
        private AnimatorController GetFXController(VRCAvatarDescriptor descriptor)
        {
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
                string path = $"{ASSET_FOLDER}/{CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(ASSET_FOLDER);

                controller = AnimatorController.CreateAnimatorControllerAtPath(path);

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
        /// 从 Resources 文件夹加载音频资源
        /// </summary>
        private void LoadAudioResources(ASSComponent config)
        {
            config.successSound = Resources.Load<AudioClip>(AUDIO_PASSWORD_SUCCESS);
            config.warningBeep = Resources.Load<AudioClip>(AUDIO_COUNTDOWN_WARNING);
            EnsureAudioLoadInBackground(config.successSound);
            EnsureAudioLoadInBackground(config.warningBeep);
        }

        private static void EnsureAudioLoadInBackground(AudioClip clip)
        {
            if (clip == null) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            bool changed = false;
            if (!importer.loadInBackground) { importer.loadInBackground = true; changed = true; }
            var settings = importer.defaultSampleSettings;
            if (settings.quality > 0.01f) { settings.quality = 0.01f; importer.defaultSampleSettings = settings; changed = true; }
            if (changed) importer.SaveAndReimport();
        }
    }
}
