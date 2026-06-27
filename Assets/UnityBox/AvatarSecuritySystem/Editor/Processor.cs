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

            // ★ 初始化混淆引擎（必须在任何使用 Constants 的模块之前）
            Obfuscator.Initialize(avatarGameObject.name,
                disableObfuscation: assConfig.disableObfuscation,
                enableDecoyLayers: assConfig.enableDecoyLayers,
                enableDecoyStates: assConfig.enableDecoyStates,
                generatedFolder: ASSET_FOLDER);
            Constants.ApplyObfuscation();

            // 默认启用防御模式不需要密码，跳过密码有效性检查
            if (!assConfig.defaultEnableDefense)
            {
                if (!assConfig.IsPasswordValid())
                {
                    Debug.LogWarning("[ASS] Password configuration is invalid");
                    return false;
                }

                if (assConfig.gesturePassword == null || assConfig.gesturePassword.Count == 0)
                {
                    Debug.Log("[ASS] Password is empty (0 digits), ASS is disabled. Skipping generation.");
                    return true;
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode && !assConfig.enabledInPlaymode)
            {
                Debug.Log("[ASS] Play mode disabled, skipping");
                return true;
            }

            Debug.Log("[ASS] Starting to generate security system...");

            var fxController = GetFXController(descriptor);
            Utils.EnsureBuiltInVRCParameters(fxController,
                ensureIsLocal: true,
                ensureGestureParameters: true);

            LoadAudioResources(assConfig);
            new Feedback(avatarGameObject, assConfig).Generate();

            var isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

            new Lock(fxController, avatarGameObject, assConfig, descriptor).Generate();

            if (!assConfig.defaultEnableDefense)
            {
                new GesturePassword(fxController, avatarGameObject, assConfig).Generate();
            }
            else
            {
                Debug.Log("[ASS] Default enable defense mode: skipping gesture password layer generation");
            }

            if (!assConfig.defaultEnableDefense)
            {
                var countdown = new Countdown(fxController, avatarGameObject, assConfig);
                countdown.Generate();
                countdown.GenerateAudioLayer();
            }
            else
            {
                Debug.Log("[ASS] Default enable defense mode: skipping countdown layer");
            }

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

            if (Obfuscator.IsEnabled)
            {
                var instructionalClips = GenerateInstructionalClips(fxController);
                GenerateDecoyLayer(fxController, instructionalClips);
                InjectFakeStatesIntoRealLayers(fxController, instructionalClips);
            }

            RegisterASSParameters(descriptor, assConfig);

            Utils.SaveAndRefresh();
            Utils.LogOptimizationStats(fxController);
            Debug.Log("[ASS] Security system generation complete!");
            return true;
        }

        /// <summary>
        /// 注册 ASS 参数到 VRCExpressionParameters
        /// 当混淆启用时，额外注册迷惑性假参数（提示词注入）
        /// </summary>
        private void RegisterASSParameters(VRCAvatarDescriptor descriptor, ASSComponent assConfig)
        {
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters not found, cannot register parameters");
                return;
            }

            var existingParams = expressionParameters.parameters?.ToList()
                ?? new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();

            existingParams.RemoveAll(p => p.name == PARAM_PASSWORD_CORRECT);

            var assParams = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_PASSWORD_CORRECT,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true
                }
            };

            // 默认启用防御模式不需要 TimeUp 参数（无倒计时）
            bool needTimeUp = !(assConfig != null && assConfig.defaultEnableDefense);
            if (needTimeUp)
            {
                existingParams.RemoveAll(p => p.name == PARAM_TIME_UP);
                assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_TIME_UP,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false
                });
            }

            if (Obfuscator.IsEnabled)
            {
                var decoyParams = Obfuscator.GetDecoyParameters();
                foreach (var decoy in decoyParams)
                {
                    VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType vrcType;
                    switch (decoy.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float;
                            break;
                        case AnimatorControllerParameterType.Int:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int;
                            break;
                        default:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool;
                            break;
                    }

                    // 避免与已有参数重名
                    if (existingParams.Any(p => p.name == decoy.name) ||
                        assParams.Any(p => p.name == decoy.name))
                        continue;

                    assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                    {
                        name = decoy.name,
                        valueType = vrcType,
                        defaultValue = decoy.defaultValue,
                        saved = false,
                        networkSynced = false
                    });
                }
            }

            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();

            EditorUtility.SetDirty(expressionParameters);
            Debug.Log($"[ASS] Registered ASS parameters: " +
                     $"{PARAM_PASSWORD_CORRECT}(synced)" +
                     (needTimeUp ? $", {PARAM_TIME_UP}(local)" : " (no TimeUp)") +
                     (Obfuscator.IsEnabled ? " + decoys" : ""));
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

        private static void InjectFakeStatesIntoRealLayers(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyStatesEnabled) return;

            var decoyParams = Obfuscator.GetDecoyParameters();
            if (decoyParams == null || decoyParams.Count == 0) return;

            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            int injectedCount = 0;

            foreach (var layer in controller.layers)
            {
                // 只对 ASS 生成的层注入假状态（通过 Constants 名称判断）
                bool isAssLayer = layer.name == Constants.LAYER_LOCK
                    || layer.name == Constants.LAYER_PASSWORD_INPUT
                    || layer.name == Constants.LAYER_COUNTDOWN
                    || layer.name == Constants.LAYER_AUDIO
                    || layer.name == Constants.LAYER_DEFENSE;

                if (!isAssLayer) continue;
                if (layer.stateMachine == null) continue;

                Obfuscator.InjectFakeStates(layer.stateMachine, decoyParams, emptyClip, instructionalClips);
                injectedCount++;
            }

            if (injectedCount > 0)
                Debug.Log($"[ASS] Obfuscator: Injected fake states into {injectedCount} real layers");
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

        private static List<AnimationClip> GenerateInstructionalClips(AnimatorController controller)
        {
            if (!Obfuscator.DecoyLayersEnabled && !Obfuscator.DecoyStatesEnabled)
                return null;

            int clipCount = 3;
            var clipNames = Obfuscator.GetInstructionalClipNames(clipCount);
            var bindingPaths = Obfuscator.GetInstructionalBindingPaths(clipCount * 2);

            if (clipNames.Length == 0) return null;

            var clips = new List<AnimationClip>();
            var dummyCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);

            for (int i = 0; i < clipNames.Length; i++)
            {
                var clip = new AnimationClip
                {
                    name = clipNames[i],
                    legacy = false
                };
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                // 在指令式绑定路径上添加 dummy 曲线
                int bindingsPerClip = 1 + (i % 2);
                for (int b = 0; b < bindingsPerClip; b++)
                {
                    int pathIdx = (i * 2 + b) % bindingPaths.Length;
                    string path = bindingPaths[pathIdx];
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", dummyCurve);
                }

                Utils.AddSubAsset(controller, clip);
                clips.Add(clip);
            }

            Debug.Log($"[ASS] Obfuscator: Generated {clips.Count} instructional clips");
            return clips;
        }

        private static void GenerateDecoyLayer(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyLayersEnabled) return;

            var decoyLayer = Obfuscator.GetDecoyLayer();
            if (decoyLayer == null) return;

            var decoyParams = Obfuscator.GetDecoyParameters();
            // 注册迷惑参数到 Animator Controller
            foreach (var decoy in decoyParams)
                Utils.AddParameterIfNotExists(controller, decoy.name, decoy.type);

            var layer = Utils.CreateLayer(decoyLayer.layerName, 1f); // weight=1，看起来像真层
            var sm = layer.stateMachine;

            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // 默认状态：空 Clip，无效果
            var defaultState = sm.AddState("Default", new Vector3(300, 0, 0));
            defaultState.motion = emptyClip;
            defaultState.writeDefaultValues = true;
            sm.defaultState = defaultState;

            // 创建假状态（交替使用空 Clip 和指令式 Clip）
            var fakeStates = new List<AnimatorState>();
            var instructionalNames = Obfuscator.GetInstructionalStateNames(decoyLayer.states.Length);
            int instrNameIdx = 0;

            for (int i = 0; i < decoyLayer.states.Length; i++)
            {
                // 每隔一个状态使用指令式名称和指令式 Clip
                bool useInstructional = (i % 2 == 0)
                    && instructionalClips != null && instructionalClips.Count > 0
                    && instrNameIdx < instructionalNames.Length;

                string stateName = useInstructional
                    ? instructionalNames[instrNameIdx++]
                    : decoyLayer.states[i];

                var fakeState = sm.AddState(stateName,
                    new Vector3(300 + (i + 1) * 180, 100 + (i % 3) * 80, 0));

                if (useInstructional)
                {
                    int clipIdx = i % instructionalClips.Count;
                    fakeState.motion = instructionalClips[clipIdx];
                }
                else
                {
                    fakeState.motion = emptyClip;
                }
                fakeState.writeDefaultValues = true;
                fakeStates.Add(fakeState);
            }

            // 默认状态 → 假状态：条件永远不满足（迷惑参数永不被驱动）
            for (int i = 0; i < fakeStates.Count; i++)
            {
                // 使用 hasExitTime=true 作为主退出机制，加一个永假条件
                var trans = Utils.CreateTransition(defaultState, fakeStates[i],
                    hasExitTime: true, exitTime: 999f); // 999秒后才可能自然退出
                trans.duration = 0.1f;
                if (decoyParams.Count > 0)
                {
                    var dp = decoyParams[i % decoyParams.Count];
                    if (dp.type == AnimatorControllerParameterType.Bool)
                        trans.AddCondition(AnimatorConditionMode.If, 0, dp.name);
                    else
                        trans.AddCondition(AnimatorConditionMode.Greater, 999999f, dp.name);
                }
            }

            // 假状态之间互相转换（看起来像一个功能网络）
            for (int i = 0; i < fakeStates.Count; i++)
            {
                int nextIdx = (i + 1) % fakeStates.Count;
                var trans = fakeStates[i].AddTransition(fakeStates[nextIdx]);
                trans.hasExitTime = true;
                trans.exitTime = 0.3f + i * 0.05f;
                trans.duration = 0.1f;
                trans.hasFixedDuration = true;

                if (i % 2 == 0)
                {
                    int fakeGesture = 1 + (i * 3) % 7;
                    // 随机混用左右手，避免泄漏用户手势惯用手信息
                    string gestureParam = (i % 4 == 0) ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT;
                    trans.AddCondition(AnimatorConditionMode.Equals, fakeGesture, gestureParam);
                }

                if (decoyParams.Count > 0)
                {
                    var dp = decoyParams[(i + 1) % decoyParams.Count];
                    if (dp.type == AnimatorControllerParameterType.Bool)
                        trans.AddCondition(AnimatorConditionMode.IfNot, 0, dp.name);
                    else
                        trans.AddCondition(AnimatorConditionMode.Less, 0.5f, dp.name);
                }
            }

            sm.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, sm);

            controller.AddLayer(layer);
            Debug.Log($"[ASS] Obfuscator: Added decoy layer \"{decoyLayer.layerName}\" "
                + $"(weight=1, {fakeStates.Count} fake states, unreachable)");
        }
    }
}
