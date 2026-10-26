using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public sealed class ASSConfigData
    {
        public SystemLanguage uiLanguage;
        public List<int> gesturePassword;
        public bool useRightHand;
        public float countdownDuration;
        public float warningThreshold;
        public float gestureHoldTime;
        public float gestureErrorTolerance;
        public float gestureMaxHoldTime;
        public AudioClip warningBeep;
        public AudioClip successSound;
        public bool enabledInPlaymode;
        public bool disableDefense;
        public bool disableRootChildren;
        public bool disableOverlay;
        public bool disableWarningSound;
        public bool defaultEnableDefense;
        public bool lightweightDefense;
        public bool disableObfuscation;
        public bool enablePlayableLayerObfuscation;
        public bool enableDecoyLayers;
        public bool enableDecoyStates;
        public int writeDefaultsMode;
        public string sourceObjectName;

        public static ASSConfigData FromAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;

            return FromComponent(
                avatarRoot.GetComponent<ASSComponent>()
                ?? avatarRoot.GetComponentInChildren<ASSComponent>(true));
        }

        public static ASSConfigData FromComponent(ASSComponent source)
        {
            if (source == null) return null;

            return new ASSConfigData
            {
                uiLanguage = source.uiLanguage,
                gesturePassword = source.gesturePassword != null
                    ? new List<int>(source.gesturePassword)
                    : new List<int>(),
                useRightHand = source.useRightHand,
                countdownDuration = source.countdownDuration,
                warningThreshold = source.warningThreshold,
                gestureHoldTime = source.gestureHoldTime,
                gestureErrorTolerance = source.gestureErrorTolerance,
                gestureMaxHoldTime = source.gestureMaxHoldTime,
                warningBeep = source.warningBeep,
                successSound = source.successSound,
                enabledInPlaymode = source.enabledInPlaymode,
                disableDefense = source.disableDefense,
                disableRootChildren = source.disableRootChildren,
                disableOverlay = source.disableOverlay,
                disableWarningSound = source.disableWarningSound,
                defaultEnableDefense = source.defaultEnableDefense,
                lightweightDefense = source.lightweightDefense,
                disableObfuscation = source.disableObfuscation,
                enablePlayableLayerObfuscation = source.enablePlayableLayerObfuscation,
                enableDecoyLayers = source.enableDecoyLayers,
                enableDecoyStates = source.enableDecoyStates,
                writeDefaultsMode = (int)source.writeDefaultsMode,
                sourceObjectName = source.gameObject != null ? source.gameObject.name : null
            };
        }

        public bool IsPasswordValid()
        {
            if (gesturePassword == null || gesturePassword.Count == 0)
            {
                return true;
            }

            const int minimumGestureValue = 0;
            const int maximumGestureValue = 7;
            foreach (int gesture in gesturePassword)
            {
                if (gesture < minimumGestureValue || gesture > maximumGestureValue) return false;
            }

            return true;
        }
    }

    public class Processor : IVRCSDKPreprocessAvatarCallback, IVRCSDKPostprocessAvatarCallback
    {
        private sealed class PlayableLayerSnapshot
        {
            public VRCAvatarDescriptor Descriptor;
            public VRCAvatarDescriptor.CustomAnimLayer[] BaseLayers;
            public VRCAvatarDescriptor.CustomAnimLayer[] SpecialLayers;
            public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters ExpressionParameters;
            public bool CustomExpressions;
            public bool CustomizeAnimationLayers;
        }

        private static readonly List<PlayableLayerSnapshot> PlayableLayerSnapshots = new List<PlayableLayerSnapshot>();
        private static readonly Dictionary<int, ASSConfigData> ConfigSnapshots = new Dictionary<int, ASSConfigData>();

        /// <summary>
        /// 固定在 -1024：与 VRCFury 自身的 VrcfRemoveEditorOnlyObjectsHook 相同的 callbackOrder
        /// 不会造成问题（两者处理的内容互不影响，谁先谁后结果一致）。
        /// NDMF 存在时优先由 NDMFPlugin（Editor/NDMF 子程序集）在 NDMF 构建管线中处理；
        /// 但 `NDMF_AVAILABLE` 仅为编译期符号（由 asmdef versionDefine 提供），
        /// NDMF 子程序集可能因版本不匹配等原因未成功编译，此时此回调作为运行时兜底路径，
        /// 通过检测 Processor.ConfigSnapshots 中是否存在 NDMFPlugin 写入的快照判断。
        /// </summary>
        public int callbackOrder => -1024;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
#if NDMF_AVAILABLE
            // 存在 NDMF 时，由 NDMFPlugin 在 NDMF 构建管线中处理。
            // 通过 Processor.ConfigSnapshots 检测 NDMFPlugin 是否真的运行了
            // （NDMFPlugin.Resolving 已调用 CaptureConfigSnapshot）：
            //   - 快照存在 → NDMFPlugin 正常运行，此回调跳过
            //   - 快照不存在 → NDMF 子程序集可能未编译或 NDMFPlugin 未运行，
            //     走独立处理路径作为兜底
            var capturedConfig = GetCapturedConfig(avatarGameObject);
            if (capturedConfig != null)
            {
                Debug.Log("[ASS] NDMFPlugin 已处理该 Avatar，VRCSDK callback 跳过");
                return true;
            }
            Debug.LogWarning("[ASS] NDMF_AVAILABLE 已定义但 NDMFPlugin 未捕获到配置，" +
                "可能 NDMF 版本过旧或子程序集未编译，回退到独立处理路径");
#endif
            Debug.Log($"[ASS] OnPreprocessAvatar called (callbackOrder={callbackOrder})");
            try
            {
                bool result = ProcessAvatar(avatarGameObject, hasNDMF: false,
                    configOverride: GetCapturedConfig(avatarGameObject));
                if (!result)
                {
                    RestorePlayableLayerSnapshots("failed preprocess");
                    ClearConfigSnapshots("failed preprocess");
                }
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] Avatar processing failed: {ex.Message}\n{ex.StackTrace}");
                RestorePlayableLayerSnapshots("preprocess exception");
                ClearConfigSnapshots("preprocess exception");
                return false;
            }
        }

        public void OnPostprocessAvatar()
        {
            // 判断当前构建走的是哪个路径：
            //   - NDMF 路径：NDMFPlugin 在 NDMF 管线内处理，未调用 SnapshotPlayableLayers
            //   - 独立路径：OnPreprocessAvatar 调用了 SnapshotPlayableLayers
            // 通过 PlayableLayerSnapshots 有无快照来区分，以决定是否清理 Generated。
            bool hasSnapshots = PlayableLayerSnapshots.Count > 0;

            RestorePlayableLayerSnapshots("postprocess");
            ClearConfigSnapshots("postprocess");

            // 只有独立路径才需要清理 Generated（该路径下控制器已被复制到 Generated 中，
            // 构建完成后通过快照恢复原始控制器，删除 Generated 副本）。
            // NDMF 路径下控制器是原地修改的，可能引用了 Generated 中的资产（如 _E.anim），
            // 删除 Generated 将导致引用断裂。
            if (hasSnapshots)
            {
                CleanupTransientGeneratedAssets("postprocess");
            }
        }
        /// <summary>
        /// 核心处理逻辑。由 <see cref="OnPreprocessAvatar"/>（无 NDMF 场景）
        /// 或 NDMF 场景下的 NDMFPlugin（BuildPhase.PlatformFinish）调用。
        /// </summary>
        public static bool ProcessAvatar(GameObject avatarGameObject, bool hasNDMF, ASSConfigData configOverride = null)
        {
            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogError("[ASS] VRCAvatarDescriptor not found");
                return true;
            }
            var assConfig = configOverride ?? ASSConfigData.FromAvatar(avatarGameObject);
            if (assConfig == null)
            {
                Debug.LogWarning($"[ASS] No AvatarSecuritySystem component found on '{avatarGameObject.name}' or its children, skipping");
                return true;
            }
            if (configOverride != null)
            {
                Debug.Log("[ASS] Using captured AvatarSecuritySystem configuration");
            }
            else if (!string.IsNullOrEmpty(assConfig.sourceObjectName) && assConfig.sourceObjectName != avatarGameObject.name)
            {
                Debug.Log($"[ASS] Using AvatarSecuritySystem component from child object '{assConfig.sourceObjectName}'");
            }
            Debug.Log("[ASS] Write Defaults: all generated ASS states use WD On");
            Obfuscator.Initialize(avatarGameObject.name,
                disableObfuscation: assConfig.disableObfuscation,
                enableDecoyLayers: assConfig.enableDecoyLayers,
                enableDecoyStates: assConfig.enableDecoyStates,
                generatedFolder: ASSET_FOLDER);
            Constants.ApplyObfuscation();
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

            if (!hasNDMF)
            {
                SnapshotPlayableLayers(descriptor);
                RemoveTransientGeneratedControllerReferences(descriptor);
            }
            CleanupTransientGeneratedAssets("preprocess");

            // hasNDMF 由调用方决定执行路径：
            //   NDMF 模式：由 NDMFPlugin 在 BuildPhase.PlatformFinish 内调用，
            //     操作的是 NDMF 已克隆的虚拟控制器，无需再复制一份
            //   独立模式：由 VRCSDK 回调（callbackOrder=-1024）调用，
            //     ASS 自己复制所有控制器到 Generated
            if (!hasNDMF)
            {
                // FX generation modifies its controller regardless of the naming options.
                Obfuscator.PreparePlayableControllerCopies(descriptor,
                    fxOnly: !Obfuscator.IsEnabled || !assConfig.enablePlayableLayerObfuscation);
            }

            Debug.Log("[ASS] Starting to generate security system...");
            var fxController = GetFXController(descriptor);
            if (!hasNDMF)
            {
                Obfuscator.RegisterGeneratedAsset(fxController);
            }
            CleanupASSGeneratedLayers(fxController, avatarGameObject.name);
            Utils.EnsureBuiltInVRCParameters(fxController,
                ensureIsLocal: true,
                ensureGestureParameters: true);
            LoadAudioResources(assConfig);
            new Feedback(avatarGameObject, assConfig).Generate();
            var isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
            if (assConfig.disableRootChildren)
            {
                new Lock(fxController, avatarGameObject, assConfig, descriptor).Generate();
            }
            else
            {
                Debug.Log("[ASS] Avatar lock disabled, skipping ASS_Lock layer; password feedback remains in PasswordInput/Audio layers");
            }
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
                        Debug.Log("[ASS] Play mode: Active-load defense skipped; password/lock/feedback preview only");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ASS] Defense layer creation failed: {ex.Message}\n{ex.StackTrace}");
                    return false;
                }
            }
            else
            {
                Debug.Log("[ASS] disableDefense enabled, skipping defense generation");
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

        private static void CleanupTransientGeneratedAssets(string stage)
        {
            string generatedPath = ASSET_FOLDER.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(generatedPath))
                return;
            try
            {
                if (!AssetDatabase.DeleteAsset(generatedPath))
                {
                    Debug.LogWarning($"[ASS] Failed to delete transient generated folder at {stage}: {generatedPath}");
                    return;
                }
                AssetDatabase.Refresh();
                Debug.Log($"[ASS] Cleaned transient generated assets at {stage}: {generatedPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ASS] Exception while cleaning transient generated assets at {stage}: {ex.Message}");
            }
        }

        public static void CaptureConfigSnapshot(GameObject avatarGameObject, string stage)
        {
            if (avatarGameObject == null) return;

            var config = ASSConfigData.FromAvatar(avatarGameObject);
            if (config == null)
            {
                ConfigSnapshots.Remove(avatarGameObject.GetInstanceID());
                return;
            }

            ConfigSnapshots[avatarGameObject.GetInstanceID()] = config;
            Debug.Log($"[ASS] Captured AvatarSecuritySystem configuration at {stage} from '{config.sourceObjectName}'");
        }

        internal static ASSConfigData GetCapturedConfig(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return null;
            ConfigSnapshots.TryGetValue(avatarGameObject.GetInstanceID(), out var config);
            return config;
        }

        private static void ClearConfigSnapshots(string stage)
        {
            if (ConfigSnapshots.Count == 0) return;
            int count = ConfigSnapshots.Count;
            ConfigSnapshots.Clear();
            Debug.Log($"[ASS] Cleared {count} captured ASS configuration snapshot(s) at {stage}");
        }

        private static void SnapshotPlayableLayers(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return;
            if (PlayableLayerSnapshots.Any(s => s.Descriptor == descriptor)) return;

            PlayableLayerSnapshots.Add(new PlayableLayerSnapshot
            {
                Descriptor = descriptor,
                BaseLayers = CloneAndSanitizePlayableLayers(descriptor.baseAnimationLayers),
                SpecialLayers = CloneAndSanitizePlayableLayers(descriptor.specialAnimationLayers),
                ExpressionParameters = descriptor.expressionParameters,
                CustomExpressions = descriptor.customExpressions,
                CustomizeAnimationLayers = descriptor.customizeAnimationLayers
            });
        }

        private static VRCAvatarDescriptor.CustomAnimLayer[] CloneAndSanitizePlayableLayers(
            VRCAvatarDescriptor.CustomAnimLayer[] layers)
        {
            if (layers == null) return null;

            var clone = (VRCAvatarDescriptor.CustomAnimLayer[])layers.Clone();
            for (int i = 0; i < clone.Length; i++)
            {
                if (!IsTransientGeneratedController(clone[i].animatorController)) continue;
                clone[i].animatorController = null;
                clone[i].isDefault = true;
            }
            return clone;
        }

        private static void RestorePlayableLayerSnapshots(string stage)
        {
            if (PlayableLayerSnapshots.Count == 0)
                return;

            int restoredCount = 0;
            foreach (var snapshot in PlayableLayerSnapshots)
            {
                if (snapshot?.Descriptor == null) continue;

                snapshot.Descriptor.baseAnimationLayers = snapshot.BaseLayers;
                snapshot.Descriptor.specialAnimationLayers = snapshot.SpecialLayers;
                snapshot.Descriptor.expressionParameters = snapshot.ExpressionParameters;
                snapshot.Descriptor.customExpressions = snapshot.CustomExpressions;
                snapshot.Descriptor.customizeAnimationLayers = snapshot.CustomizeAnimationLayers;
                EditorUtility.SetDirty(snapshot.Descriptor);
                restoredCount++;
            }
            PlayableLayerSnapshots.Clear();
            if (restoredCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[ASS] Restored {restoredCount} avatar playable layer snapshot(s) at {stage}");
            }
        }

        private static void RemoveTransientGeneratedControllerReferences(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return;

            var baseLayers = descriptor.baseAnimationLayers;
            bool changed = RemoveTransientGeneratedControllerReferences(baseLayers);
            if (changed)
                descriptor.baseAnimationLayers = baseLayers;

            var specialLayers = descriptor.specialAnimationLayers;
            changed |= RemoveTransientGeneratedControllerReferences(specialLayers);
            if (changed)
                descriptor.specialAnimationLayers = specialLayers;

            if (changed)
            {
                EditorUtility.SetDirty(descriptor);
                Debug.LogWarning("[ASS] Removed stale Generated playable controller reference(s) before regeneration");
            }
        }

        private static bool RemoveTransientGeneratedControllerReferences(
            VRCAvatarDescriptor.CustomAnimLayer[] layers)
        {
            if (layers == null) return false;

            bool changed = false;
            for (int i = 0; i < layers.Length; i++)
            {
                if (!IsTransientGeneratedController(layers[i].animatorController)) continue;
                layers[i].animatorController = null;
                layers[i].isDefault = true;
                changed = true;
            }
            return changed;
        }

        private static bool IsTransientGeneratedController(RuntimeAnimatorController controller)
        {
            if (controller == null) return false;

            string path = AssetDatabase.GetAssetPath(controller)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(path)) return false;

            string generatedPath = ASSET_FOLDER.Replace('\\', '/').TrimEnd('/');
            return path.StartsWith(generatedPath + "/", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, generatedPath, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldProcessAvatar(ASSConfigData assConfig)
        {
            if (assConfig == null)
                return false;

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
                    return false;
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode && !assConfig.enabledInPlaymode)
            {
                Debug.Log("[ASS] Play mode disabled, skipping");
                return false;
            }

            return true;
        }

        public static bool ProcessPlayableObfuscation(GameObject avatarGameObject, bool hasNDMF)
        {
            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return true;

            // 先校正一次历史 ASS 控制行为，即使当前 Avatar 跳过 Playable 混淆，
            // 也不能让旧版本写入的层索引在构建链重排后失效。
            RefreshASSLayerControlIndices(descriptor);

            var assConfig = GetCapturedConfig(avatarGameObject)
                ?? ASSConfigData.FromAvatar(avatarGameObject);
            if (assConfig == null)
                return true;

            if (!ShouldProcessAvatar(assConfig))
                return true;

            if (assConfig.disableObfuscation || !Obfuscator.IsEnabled || !assConfig.enablePlayableLayerObfuscation)
                return true;

            if (HasVRCContactComponents(avatarGameObject))
            {
                // VRCFury HapticContacts/WorldScaleDetector 会为 ContactReceiver
                // 生成隐藏的 FX/AAP AllReceiversOff 图。ASS 不应改名或重排这套第三方图，
                // 否则其 ScaleFactor/counter 恢复链可能一直停留在 Contact disabled 状态。
                Debug.LogWarning("[ASS] VRC Contact components detected; skipping ASS playable-layer obfuscation to preserve third-party Contact/FX graphs");
                return true;
            }

            Obfuscator.ObfuscatePlayableControllers(descriptor, hasNDMF);
            // 混淆/复制完成后再校正一次，覆盖可能发生的层重排或 Controller 替换。
            RefreshASSLayerControlIndices(descriptor);
            return true;
        }

        private static void RefreshASSLayerControlIndices(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return;

            foreach (var playableLayer in descriptor.baseAnimationLayers)
            {
                if (playableLayer.type != VRCAvatarDescriptor.AnimLayerType.FX)
                    continue;
                if (!(playableLayer.animatorController is AnimatorController controller))
                    continue;

                int changedCount = 0;
                var layers = controller.layers;
                for (int i = 0; i < layers.Length; i++)
                {
                    RefreshASSLayerControlIndices(layers[i].stateMachine, i, ref changedCount);
                }

                if (changedCount > 0)
                {
                    EditorUtility.SetDirty(controller);
                    Debug.Log($"[ASS] Refreshed {changedCount} final FX layer-control index(es)");
                }
            }
        }

        private static void RefreshASSLayerControlIndices(
            AnimatorStateMachine stateMachine, int layerIndex, ref int changedCount)
        {
            if (stateMachine == null) return;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                foreach (var behaviour in state.behaviours)
                {
                    if (!(behaviour is VRCAnimatorLayerControl control) ||
                        string.IsNullOrEmpty(control.debugString) ||
                        !control.debugString.StartsWith("ASS_", System.StringComparison.Ordinal))
                        continue;

                    if (control.playable != VRC_AnimatorLayerControl.BlendableLayer.FX)
                    {
                        control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                        changedCount++;
                    }
                    if (control.layer != layerIndex)
                    {
                        control.layer = layerIndex;
                        changedCount++;
                    }
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                RefreshASSLayerControlIndices(childMachine.stateMachine, layerIndex, ref changedCount);
            }
        }

        private static bool HasVRCContactComponents(GameObject avatarRoot)
        {
            if (avatarRoot == null) return false;

            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var type = component.GetType();
                while (type != null)
                {
                    string fullName = type.FullName;
                    if (fullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactSender"
                        || fullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver"
                        || fullName == "VRC.Dynamics.ContactSender"
                        || fullName == "VRC.Dynamics.ContactReceiver")
                        return true;
                    type = type.BaseType;
                }
            }

            return false;
        }

        private static void RegisterASSParameters(VRCAvatarDescriptor descriptor, ASSConfigData assConfig, string assetFolder = null)
        {
            // Register on a build-local copy. The descriptor can still reference the user's
            // project asset even when its animator controllers were cloned by another tool.
            var sourceParameters = descriptor.expressionParameters;
            var expressionParameters = sourceParameters != null
                ? Object.Instantiate(sourceParameters)
                : ScriptableObject.CreateInstance<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>();
            expressionParameters.name = "ASS_BuildParameters";
            descriptor.expressionParameters = expressionParameters;
            descriptor.customExpressions = true;
            var existingParams = expressionParameters.parameters?.Where(p => p != null).ToList()
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
                    if (existingParams.Any(p => p.name == decoy.name) ||
                        assParams.Any(p => p.name == decoy.name))
                        continue;
                    assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                    {
                        name = decoy.name,
                        valueType = vrcType,
                        defaultValue = decoy.defaultValue,
                        saved = ((uint)decoy.name.GetHashCode() & 1) == 0,
                        networkSynced = false
                    });
                }
            }
            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();
            assetFolder = assetFolder ?? ASSET_FOLDER;
            System.IO.Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(expressionParameters,
                AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/_Parameters.asset"));
            EditorUtility.SetDirty(descriptor);
            Debug.Log($"[ASS] Registered ASS parameters: " +
                     $"{PARAM_PASSWORD_CORRECT}(synced)" +
                     (needTimeUp ? $", {PARAM_TIME_UP}(local)" : " (no TimeUp)") +
                     (Obfuscator.IsEnabled ? " + decoys" : ""));
        }
        private static AnimatorController GetFXController(VRCAvatarDescriptor descriptor)
        {
            descriptor.customizeAnimationLayers = true;
            var fxLayer = descriptor.baseAnimationLayers
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            if (!fxLayer.isDefault && fxLayer.animatorController != null &&
                !(fxLayer.animatorController is AnimatorController))
                throw new System.InvalidOperationException(
                    "[ASS] FX must be an AnimatorController. Resolve the override controller before generating ASS.");
            AnimatorController controller = null;
            if (!fxLayer.isDefault && fxLayer.animatorController is AnimatorController)
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
        private static void LoadAudioResources(ASSConfigData config)
        {
            if (config.successSound == null)
                config.successSound = Resources.Load<AudioClip>(AUDIO_PASSWORD_SUCCESS);
            if (config.warningBeep == null)
                config.warningBeep = Resources.Load<AudioClip>(AUDIO_COUNTDOWN_WARNING);
        }
        private static void CleanupASSGeneratedLayers(AnimatorController controller, string avatarName)
        {
            if (controller == null) return;

            string controllerPath = AssetDatabase.GetAssetPath(controller)?.Replace('\\', '/');
            string generatedFxPath = $"{ASSET_FOLDER}/{CONTROLLER_NAME}".Replace('\\', '/');
            if (string.Equals(controllerPath, generatedFxPath, System.StringComparison.OrdinalIgnoreCase))
            {
                bool hasLayers = controller.layers.Length > 0;
                bool hasParameters = controller.parameters.Length > 0;
                if (hasLayers || hasParameters)
                {
                    controller.layers = new AnimatorControllerLayer[0];
                    controller.parameters = new AnimatorControllerParameter[0];
                    EditorUtility.SetDirty(controller);
                    Debug.Log("[ASS] Reset generated FX controller layers and parameters before regeneration");
                }
                return;
            }

            var keptLayers = new List<AnimatorControllerLayer>();
            int removedCount = 0;
            foreach (var layer in controller.layers)
            {
                if (IsASSGeneratedLayer(layer, avatarName))
                {
                    removedCount++;
                    continue;
                }
                keptLayers.Add(layer);
            }

            if (removedCount == 0) return;

            controller.layers = keptLayers.ToArray();
            EditorUtility.SetDirty(controller);
            Debug.Log($"[ASS] Removed {removedCount} previously generated ASS layer(s) before regeneration");
        }
        private static bool IsASSGeneratedLayer(AnimatorControllerLayer layer, string avatarName)
        {
            if (Constants.IsASSManagedLayerName(layer.name))
                return true;

            // Constants 当前可能已经切换到清洁名；补查按当前 Avatar 名生成的历史混淆层名。
            string[] internalKeys =
            {
                "LAYER_LOCK", "LAYER_PASSWORD_INPUT", "LAYER_COUNTDOWN",
                "LAYER_AUDIO", "LAYER_DEFENSE"
            };
            string[] cleanNames =
            {
                "ASS_Lock", "ASS_PasswordInput", "ASS_Countdown",
                "ASS_Audio", "ASS_Defense"
            };
            for (int i = 0; i < internalKeys.Length; i++)
            {
                if (layer.name == Obfuscator.GetObfuscatedLayerNameForCleanup(
                    internalKeys[i], cleanNames[i], avatarName))
                    return true;
            }

            if (layer.stateMachine == null)
                return false;
            return StateMachineContainsASSArtifacts(layer.stateMachine);
        }
        private static bool StateMachineContainsASSArtifacts(AnimatorStateMachine stateMachine)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                if (StateContainsASSArtifacts(state))
                    return true;
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                if (StateMachineContainsASSArtifacts(childMachine.stateMachine))
                    return true;
            }

            return false;
        }
        private static bool StateContainsASSArtifacts(AnimatorState state)
        {
            foreach (var behaviour in state.behaviours)
            {
                if (behaviour is VRCAnimatorPlayAudio playAudio && IsASSObjectPath(playAudio.SourcePath))
                    return true;

                if (behaviour is VRCAnimatorLayerControl layerControl &&
                    !string.IsNullOrEmpty(layerControl.debugString) &&
                    layerControl.debugString.StartsWith("ASS_", System.StringComparison.Ordinal))
                    return true;

                if (behaviour is VRCAvatarParameterDriver parameterDriver)
                {
                    foreach (var parameter in parameterDriver.parameters)
                    {
                        if (IsASSParameterName(parameter.name))
                            return true;
                    }
                }
            }

            return MotionContainsASSArtifacts(state.motion);
        }
        private static bool MotionContainsASSArtifacts(Motion motion)
        {
            if (motion == null) return false;

            if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                {
                    if (MotionContainsASSArtifacts(child.motion))
                        return true;
                }
                return false;
            }

            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (IsASSObjectPath(binding.path))
                        return true;
                    if (binding.propertyName == "material._C9D4")
                        return true;
                }
            }

            return false;
        }
        private static bool IsASSParameterName(string parameterName)
        {
            return parameterName == PARAM_PASSWORD_CORRECT
                || parameterName == PARAM_TIME_UP
                || parameterName == "ASS_PasswordCorrect"
                || parameterName == "ASS_TimeUp";
        }
        private static bool IsASSObjectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            return path == GO_OVERLAY
                || path == GO_AUDIO_WARNING
                || path == GO_AUDIO_SUCCESS
                || path == GO_DEFENSE_ROOT
                || path.StartsWith(GO_OVERLAY + "/")
                || path.StartsWith(GO_AUDIO_WARNING + "/")
                || path.StartsWith(GO_AUDIO_SUCCESS + "/")
                || path.StartsWith(GO_DEFENSE_ROOT + "/")
                || path == "ASS_Overlay"
                || path == "ASS_Audio_Warning"
                || path == "ASS_Audio_Success"
                || path == "ASS_Defense"
                || path.StartsWith("ASS_Overlay/")
                || path.StartsWith("ASS_Audio_Warning/")
                || path.StartsWith("ASS_Audio_Success/")
                || path.StartsWith("ASS_Defense/");
        }
        private static void InjectFakeStatesIntoRealLayers(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyStatesEnabled) return;
            var decoyParams = Obfuscator.GetDecoyParameters();
            if (decoyParams == null || decoyParams.Count == 0) return;
            // 确保守卫参数在控制器中存在
            string guardParam = Obfuscator.Param("Guard", "_ASS_Guard");
            Utils.AddParameterIfNotExists(controller, guardParam,
                AnimatorControllerParameterType.Bool, false);
            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            int injectedCount = 0;
            foreach (var layer in controller.layers)
            {
                if (!Constants.IsASSManagedLayerName(layer.name)) continue;
                if (layer.stateMachine == null) continue;
                // 每层独立 seed 偏移，使假状态的拓扑/参数/位置各不相同
                uint layerOffset = (uint)layer.name.GetHashCode();
                Obfuscator.InjectFakeStates(layer.stateMachine, decoyParams, emptyClip,
                    instructionalClips, layerOffset);
                injectedCount++;
            }
            if (injectedCount > 0)
                Debug.Log($"[ASS] Obfuscator: Injected fake states into {injectedCount} real layers");
        }
        private static List<AnimationClip> GenerateInstructionalClips(AnimatorController controller)
        {
            if (!Obfuscator.DecoyLayersEnabled && !Obfuscator.DecoyStatesEnabled)
                return null;
            int clipCount = 5; // 增至 5 个，丰富伪装
            var clipNames = Obfuscator.GetInstructionalClipNames(clipCount);
            var bindingPaths = Obfuscator.GetInstructionalBindingPaths(clipCount * 3); // 更多虚假路径
            if (clipNames.Length == 0) return null;
            var clips = new List<AnimationClip>();
            var dummyCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var fakeChildPaths = new[]
            {
                "_Generated/Validation/PreCheck",
                "_Diagnostics/Runtime/SafetyScan",
                "_BuildCache/PostProcess/Verify",
                "_Temp/AutoFix/WriteDefaults",
                "_Assets/IntegrityCheck/Passed",
                "__MA/AuditTrail/NoIssuesFound",
                "_Trace/LayerCompile/Complete",
            };
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
                int primaryPathIdx = (i * 2) % bindingPaths.Length;
                string primaryPath = bindingPaths[primaryPathIdx];
                clip.SetCurve(primaryPath, typeof(GameObject), "m_IsActive", dummyCurve);
                int fakePathIdx = i % fakeChildPaths.Length;
                string fakePath = fakeChildPaths[fakePathIdx];
                clip.SetCurve(fakePath, typeof(GameObject), "m_IsActive", enableCurve);
                if (i % 3 == 0)
                {
                    string extraPath = fakeChildPaths[(i + 3) % fakeChildPaths.Length];
                    clip.SetCurve(extraPath, typeof(GameObject), "m_IsActive", dummyCurve);
                }
                Utils.AddSubAsset(controller, clip);
                clips.Add(clip);
            }
            Debug.Log($"[ASS] Obfuscator: Generated {clips.Count} instructional clips with fake object paths");
            return clips;
        }
        private static void GenerateDecoyLayer(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyLayersEnabled) return;
            var decoyLayer = Obfuscator.GetDecoyLayer();
            if (decoyLayer == null) return;

            // 诱饵层用独立的 seed 生成自己的伪随机
            uint rng = Obfuscator.GetDecoyLayerSeed();

            var decoyParams = Obfuscator.GetDecoyParameters();
            foreach (var decoy in decoyParams)
                Utils.AddParameterIfNotExists(controller, decoy.name, decoy.type);

            var layer = Utils.CreateLayer(decoyLayer.layerName, 1f);
            var sm = layer.stateMachine;
            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);

            var defaultState = sm.AddState(Obfuscator.State("DecoyDefault", "Default"), new Vector3(300, 0, 0));
            defaultState.motion = emptyClip;
            // 诱饵层与 ASS 其它状态统一使用 WD On；其动画只包含伪装路径。
            defaultState.writeDefaultValues = true;
            sm.defaultState = defaultState;

            var fakeStates = new List<AnimatorState>();
            var instructionalNames = Obfuscator.GetInstructionalStateNames(decoyLayer.states.Length);
            int instrNameIdx = 0;

            for (int i = 0; i < decoyLayer.states.Length; i++)
            {
                bool useInstructional = (Obfuscator.RngInt(ref rng, 0, 2) == 0)
                    && instructionalClips != null && instructionalClips.Count > 0
                    && instrNameIdx < instructionalNames.Length;
                string stateName = useInstructional
                    ? instructionalNames[instrNameIdx++]
                    : decoyLayer.states[i];

                float x = Obfuscator.RngRange(ref rng, 50, 900);
                float y = Obfuscator.RngRange(ref rng, -400, 300);
                var fakeState = sm.AddState(stateName, new Vector3(x, y, 0));

                if (useInstructional)
                {
                    int clipIdx = Obfuscator.RngInt(ref rng, 0, instructionalClips.Count - 1);
                    fakeState.motion = instructionalClips[clipIdx];
                }
                else
                {
                    fakeState.motion = emptyClip;
                }
                // 诱饵状态只模拟拓扑，不写入未绑定的真实属性。
                fakeState.writeDefaultValues = true;
                fakeStates.Add(fakeState);
            }

            var boolGuards = decoyParams
                .Where(p => p.type == AnimatorControllerParameterType.Bool)
                .ToList();

            for (int i = 0; i < fakeStates.Count; i++)
            {
                var trans = Utils.CreateTransition(defaultState, fakeStates[i],
                    hasExitTime: true, exitTime: Obfuscator.RngRange(ref rng, 100, 5000));
                trans.duration = Obfuscator.RngRange(ref rng, 0, 0.3f);

                if (boolGuards.Count > 0)
                {
                    var guard = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                    trans.AddCondition(AnimatorConditionMode.If, 0, guard.name);
                }
                if (Obfuscator.RngInt(ref rng, 0, 1) == 0)
                {
                    int fg = Obfuscator.RngInt(ref rng, 0, 7);
                    trans.AddCondition(AnimatorConditionMode.Equals, fg,
                        Obfuscator.RngInt(ref rng, 0, 1) == 0
                            ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                }
            }

            for (int i = 0; i < fakeStates.Count; i++)
            {
                int connections = Obfuscator.RngInt(ref rng, 1, 3);
                var seen = new HashSet<int>();
                for (int c = 0; c < connections; c++)
                {
                    int t = Obfuscator.RngInt(ref rng, 0, fakeStates.Count - 1);
                    if (t == i || !seen.Add(t)) continue;

                    var trans = fakeStates[i].AddTransition(fakeStates[t]);
                    trans.hasExitTime = true;
                    trans.exitTime = Obfuscator.RngRange(ref rng, 0.05f, 5f);
                    trans.duration = Obfuscator.RngRange(ref rng, 0, 0.3f);
                    trans.hasFixedDuration = true;

                    if (Obfuscator.RngInt(ref rng, 0, 1) == 0)
                    {
                        int fg = Obfuscator.RngInt(ref rng, 0, 7);
                        trans.AddCondition(AnimatorConditionMode.Greater, fg - 1,
                            Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                        trans.AddCondition(AnimatorConditionMode.Less, fg + 1,
                            Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                    }

                    if (decoyParams.Count > 0)
                    {
                        var dp = decoyParams[Obfuscator.RngInt(ref rng, 0, decoyParams.Count - 1)];
                        if (dp.type == AnimatorControllerParameterType.Bool)
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, dp.name);
                        else if (dp.type == AnimatorControllerParameterType.Float)
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                                Obfuscator.RngRange(ref rng, -10, 10), dp.name);
                        else
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                                Obfuscator.RngInt(ref rng, -5, 10), dp.name);
                    }
                    if (boolGuards.Count > 0)
                    {
                        var guard2 = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                        trans.AddCondition(AnimatorConditionMode.If, 0, guard2.name);
                    }
                }
            }

            // 部分假状态回到 default
            for (int i = 0; i < fakeStates.Count; i++)
            {
                if (Obfuscator.RngInt(ref rng, 0, 2) == 0) continue;
                var retTrans = Utils.CreateTransition(fakeStates[i], defaultState,
                    hasExitTime: true, exitTime: Obfuscator.RngRange(ref rng, 100, 5000));
                retTrans.duration = Obfuscator.RngRange(ref rng, 0, 0.2f);
                if (boolGuards.Count > 0)
                {
                    var guard = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                    retTrans.AddCondition(AnimatorConditionMode.If, 0, guard.name);
                }
            }

            sm.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, sm);
            Obfuscator.RegisterSkipSecondPassLayerName(layer.name);
            controller.AddLayer(layer);
            Debug.Log($"[ASS] Obfuscator: Added decoy layer \"{decoyLayer.layerName}\" "
                + $"({fakeStates.Count} fake states, seed=0x{rng:X8})");
        }
    }
}
