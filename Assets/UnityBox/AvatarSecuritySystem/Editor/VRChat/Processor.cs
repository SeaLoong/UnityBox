using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
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
            public string GeneratedFolder;
        }

        private static readonly List<PlayableLayerSnapshot> PlayableLayerSnapshots = new List<PlayableLayerSnapshot>();
        private sealed class BuildSnapshot
        {
            public ASSConfigData Config;
            public bool NdmfCompleted;
            public Obfuscator.BuildContext ObfuscationContext;
            public string GeneratedFolder;
        }

        // Object identity and weak keys prevent a destroyed avatar's instance ID from
        // identifying an unrelated later build, and do not keep build clones alive.
        private static ConditionalWeakTable<GameObject, BuildSnapshot> BuildSnapshots =
            new ConditionalWeakTable<GameObject, BuildSnapshot>();

        /// <summary>Generate after NDMF's late hook; only a completed NDMF pass suppresses fallback.</summary>
        public int callbackOrder => -1024;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (WasProcessedByNdmf(avatarGameObject))
            {
                Debug.Log("[ASS] NDMF completed this avatar; skipping SDK generation");
                return true;
            }
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
            var generatedFolders = PlayableLayerSnapshots.Select(snapshot => snapshot.GeneratedFolder).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToArray();
            bool hasSnapshots = generatedFolders.Length > 0;

            RestorePlayableLayerSnapshots("postprocess");
            ClearConfigSnapshots("postprocess");

            // 只有独立路径才需要清理 Generated（该路径下控制器已被复制到 Generated 中，
            // 构建完成后通过快照恢复原始控制器，删除 Generated 副本）。
            // NDMF 的构建输出仍可能引用本插件复制的 Controller 和 Generated 中的资产，
            // 因此该路径不在 SDK 后处理立即删除 Generated。
            if (hasSnapshots)
            {
                foreach (var folder in generatedFolders) CleanupTransientGeneratedAssets("postprocess", folder);
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
            var build = BuildSnapshots.GetValue(avatarGameObject, _ => new BuildSnapshot { Config = assConfig });
            if (string.IsNullOrEmpty(build.GeneratedFolder))
                build.GeneratedFolder = ASSET_FOLDER + "/Build_" + System.Guid.NewGuid().ToString("N");
            Debug.Log("[ASS] Generated ASS states use WD On; existing user state settings are preserved.");
            Obfuscator.Initialize(avatarGameObject.name,
                disableObfuscation: assConfig.disableObfuscation,
                enableDecoyLayers: assConfig.enableDecoyLayers,
                enableDecoyStates: assConfig.enableDecoyStates,
                generatedFolder: build.GeneratedFolder);
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
                SnapshotPlayableLayers(descriptor);
            RemoveTransientGeneratedControllerReferences(descriptor);
            CleanupTransientGeneratedAssets("preprocess");

            // NDMF itself does not guarantee that another plugin has virtualized or
            // cloned these controllers. Isolate persistent sources in both paths.
            VrcPlayableControllers.PreparePlayableControllerCopies(descriptor,
                fxOnly: !Obfuscator.IsEnabled || !assConfig.enablePlayableLayerObfuscation);

            Debug.Log("[ASS] Starting to generate security system...");
            var fxController = GetFXController(descriptor);
            Obfuscator.RegisterGeneratedAsset(fxController);
            CleanupASSGeneratedLayers(fxController, avatarGameObject.name);
            Utils.EnsureInputParameters(fxController,
                ensureIsLocal: true,
                ensureGestureParameters: true);
            LoadAudioResources(assConfig);
            new Feedback(avatarGameObject, assConfig).Generate();
            var isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
            if (!assConfig.defaultEnableDefense)
            {
                new GesturePassword(fxController, avatarGameObject, assConfig, VrcAnimatorBehaviourAdapter.Instance).Generate();
            }
            else
            {
                Debug.Log("[ASS] Default enable defense mode: skipping gesture password layer generation");
            }
            if (!assConfig.defaultEnableDefense)
            {
                var countdown = new Countdown(fxController, avatarGameObject, assConfig, VrcAnimatorBehaviourAdapter.Instance);
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
                        Debug.Log("[ASS] Play mode: Active-load defense skipped; password/feedback preview only");
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
            ObfuscationGraph.Generate(fxController, avatarGameObject);
            if (!assConfig.defaultEnableDefense)
                AnimatorLifecycle.Generate(fxController, VrcAnimatorBehaviourAdapter.Instance, VrcAnimatorBehaviourAdapter.Instance);
            RegisterASSParameters(descriptor, assConfig);
            Utils.SaveControllerGraph(fxController);
            Utils.SaveAndRefresh();
            Utils.LogOptimizationStats(fxController);
            build.ObfuscationContext = Obfuscator.CaptureContext();
            Debug.Log("[ASS] Security system generation complete!");
            return true;
        }

        private static void CleanupTransientGeneratedAssets(string stage, string folder = null)
        {
            string generatedPath = (folder ?? Obfuscator.GeneratedFolder).Replace('\\', '/');
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
            BuildSnapshots.TryGetValue(avatarGameObject, out var previous);
            BuildSnapshots.Remove(avatarGameObject);
            var config = ASSConfigData.FromAvatar(avatarGameObject);
            if (config == null) return;
            BuildSnapshots.Add(avatarGameObject, new BuildSnapshot { Config = config, GeneratedFolder = previous?.GeneratedFolder });
            Debug.Log($"[ASS] Captured AvatarSecuritySystem configuration at {stage} from '{config.sourceObjectName}'");
        }

        public static void CaptureNdmfConfigSnapshot(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return;
            if (ASSConfigData.FromAvatar(avatarGameObject) != null)
            {
                CaptureConfigSnapshot(avatarGameObject, "NDMF Resolving");
            }
            else if (BuildSnapshots.TryGetValue(avatarGameObject, out var snapshot))
            {
                // Preserve the early SDK capture if an intervening pass stripped the
                // authoring component, but never inherit an earlier completion flag.
                snapshot.NdmfCompleted = false;
            }
        }

        public static ASSConfigData GetCapturedConfig(GameObject avatarGameObject)
        {
            return avatarGameObject != null && BuildSnapshots.TryGetValue(avatarGameObject, out var snapshot)
                ? snapshot.Config : null;
        }

        public static bool WasProcessedByNdmf(GameObject avatarGameObject)
        {
            return avatarGameObject != null && BuildSnapshots.TryGetValue(avatarGameObject, out var snapshot)
                && snapshot.NdmfCompleted;
        }

        public static void MarkNdmfCompleted(GameObject avatarGameObject)
        {
            if (avatarGameObject != null && BuildSnapshots.TryGetValue(avatarGameObject, out var snapshot))
                snapshot.NdmfCompleted = true;
        }

        public static void ReleaseBuildSnapshot(GameObject avatarGameObject)
        {
            if (avatarGameObject != null) BuildSnapshots.Remove(avatarGameObject);
        }

        private static void ClearConfigSnapshots(string stage)
        {
            BuildSnapshots = new ConditionalWeakTable<GameObject, BuildSnapshot>();
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
                CustomizeAnimationLayers = descriptor.customizeAnimationLayers,
                GeneratedFolder = Obfuscator.GeneratedFolder
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

            var assConfig = GetCapturedConfig(avatarGameObject)
                ?? ASSConfigData.FromAvatar(avatarGameObject);
            if (assConfig == null)
                return true;

            if (!ShouldProcessAvatar(assConfig))
                return true;

            if (BuildSnapshots.TryGetValue(avatarGameObject, out var build) && build.ObfuscationContext != null)
            {
                Obfuscator.RestoreContext(build.ObfuscationContext);
                Constants.ApplyObfuscation();
            }
            // Late build hooks may have installed a different persistent controller.
            // Isolate it before repairing layer indices or renaming its graph.
            VrcPlayableControllers.PreparePlayableControllerCopies(descriptor,
                fxOnly: assConfig.disableObfuscation || !Obfuscator.IsEnabled || !assConfig.enablePlayableLayerObfuscation);
            RefreshASSLayerControlIndices(descriptor);

            if (assConfig.disableObfuscation || !Obfuscator.IsEnabled || !assConfig.enablePlayableLayerObfuscation)
                return true;

            if (HasVRCContactComponents(avatarGameObject))
            {
                // VRCFury HapticContacts/WorldScaleDetector 会为 ContactReceiver
                // 生成隐藏的 FX/AAP AllReceiversOff 图。ASS 不应改名或重排这套第三方图，
                // 否则其 ScaleFactor/counter 恢复链可能一直停留在 Contact disabled 状态。
                Debug.LogWarning("[ASS] VRC Contact components detected; preserving third-party playable graph names. ASS-owned names, enabled decoy layers and states remain obfuscated.");
                return true;
            }

            VrcPlayableControllers.ObfuscatePlayableControllers(descriptor, hasNDMF);
            // 混淆/复制完成后再校正一次，覆盖可能发生的层重排或 Controller 替换。
            RefreshASSLayerControlIndices(descriptor);
            return true;
        }

        private static void RefreshASSLayerControlIndices(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.customizeAnimationLayers) return;

            foreach (var playableLayer in descriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
            {
                if (playableLayer.isDefault || playableLayer.type != VRCAvatarDescriptor.AnimLayerType.FX)
                    continue;
                if (!(playableLayer.animatorController is AnimatorController controller))
                    continue;

                int changedCount = 0;
                var layers = controller.layers;
                for (int i = 0; i < layers.Length; i++)
                {
                    RefreshASSLayerControlIndices(layers[i].stateMachine, i, layers, ref changedCount);
                }

                if (changedCount > 0)
                {
                    EditorUtility.SetDirty(controller);
                    Debug.Log($"[ASS] Refreshed {changedCount} final FX layer-control index(es)");
                }
            }
        }

        private static void RefreshASSLayerControlIndices(
            AnimatorStateMachine stateMachine, int layerIndex, AnimatorControllerLayer[] layers, ref int changedCount)
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
                    int targetIndex = layerIndex;
                    if (control.debugString.StartsWith(AnimatorLifecycle.TargetLayerPrefix, System.StringComparison.Ordinal))
                    {
                        string targetName = control.debugString.Substring(AnimatorLifecycle.TargetLayerPrefix.Length);
                        targetIndex = System.Array.FindIndex(layers, layer => layer.name == targetName);
                        if (targetIndex < 0) continue;
                    }
                    if (control.layer != targetIndex)
                    {
                        control.layer = targetIndex;
                        changedCount++;
                    }
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                RefreshASSLayerControlIndices(childMachine.stateMachine, layerIndex, layers, ref changedCount);
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
                existingParams.RemoveAll(p => p.name == PARAM_TIME_UP || p.name == PARAM_RESTART);
                assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_RESTART, valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f, saved = false, networkSynced = false,
                });
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
            assetFolder = assetFolder ?? Obfuscator.GeneratedFolder;
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
            VrcPlayableControllers.EnableCustomAnimationLayers(descriptor);
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
                string path = $"{Obfuscator.GeneratedFolder}/{CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(Obfuscator.GeneratedFolder);
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                var idle = controller.layers[0].stateMachine.AddState("Idle");
                idle.writeDefaultValues = true;
                idle.motion = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
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
            var managedNames = new[]
            {
                (key: "LAYER_LOCK", clean: "ASS_Lock"),
                (key: "LAYER_PASSWORD_INPUT", clean: "ASS_PasswordInput"),
                (key: "LAYER_COUNTDOWN", clean: "ASS_Countdown"),
                (key: "LAYER_AUDIO", clean: "ASS_Audio"),
                (key: "LAYER_DEFENSE", clean: "ASS_Defense"),
                (key: "LAYER_LIFECYCLE", clean: "ASS_Lifecycle"),
            };
            foreach (var name in managedNames)
            {
                if (layer.name == Obfuscator.GetObfuscatedLayerNameForCleanup(
                    name.key, name.clean, avatarName))
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
        private static List<(string name, AnimatorControllerParameterType type, float defaultValue)>
            PrepareDecoyParameters(AnimatorController controller) => ObfuscationGraph.PrepareDecoyParameters(controller);

        private static void InjectFakeStatesIntoRealLayers(AnimatorController controller,
            List<AnimationClip> instructionalClips = null) =>
            ObfuscationGraph.InjectFakeStatesIntoRealLayers(controller, instructionalClips);

        private static List<AnimationClip> GenerateInstructionalClips(AnimatorController controller) =>
            ObfuscationGraph.GenerateInstructionalClips(controller);

        private static void GenerateDecoyLayer(AnimatorController controller,
            List<AnimationClip> instructionalClips = null) =>
            ObfuscationGraph.GenerateDecoyLayer(controller, instructionalClips);
    }
}
