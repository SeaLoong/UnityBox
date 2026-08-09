using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerConfigData
    {
        public AnimatorOptimizerComponent.TargetMode TargetMode;
        public AnimatorOptimizerComponent.PlayableLayer PlayableLayer;
        public Animator Animator;
        public RuntimeAnimatorController Controller;
        public string AnimatorPath;
        public int AnimatorComponentIndex;
        public bool OptimizeAllConvertibleLayers;
        public List<int> LayerIndices = new List<int>();
        public bool UpgradeBoolParametersToFloat;
        public bool UpgradeIntParametersToFloat;
        public bool AllowFloatConditionApproximation;
        public bool StrictCurveConflicts;
        public bool MergeLayersWithEquivalentMasks;
        public bool ResolveOverridePriorityWithStaticComposite;
        public bool EnableAapStateRegisterOptimization;
        public bool RemoveEffectivelyEmptyLayers;
        public bool NormalizeAlwaysOneParameters;
        public bool ExecuteLateOptimization;
        public bool PreserveMmdCompatibility;
        public int MaxPriorityCombinations;
        public bool VerboseLogging;
        public string GeneratedLayerName;
        public string WeightParameterPrefix;
        public string SourceObjectName;

        public static AnimatorOptimizerConfigData FromAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return null;
            var component = avatarRoot.GetComponent<AnimatorOptimizerComponent>() ??
                            avatarRoot.GetComponentInChildren<AnimatorOptimizerComponent>(true);
            return FromComponent(component, avatarRoot);
        }

        public static AnimatorOptimizerConfigData FromComponent(
            AnimatorOptimizerComponent component,
            GameObject avatarRoot = null)
        {
            if (component == null || !component.enabled)
                return null;

            avatarRoot ??= component.gameObject;
            var animator = component.animator;
            if (component.targetMode != AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer && animator == null)
                animator = avatarRoot.GetComponentInChildren<Animator>(true);

            return new AnimatorOptimizerConfigData
            {
                TargetMode = component.targetMode,
                PlayableLayer = component.playableLayer,
                Animator = animator,
                Controller = component.controller != null
                    ? component.controller
                    : animator != null ? animator.runtimeAnimatorController : null,
                AnimatorPath = GetRelativePath(avatarRoot, animator != null ? animator.gameObject : null),
                AnimatorComponentIndex = GetComponentIndex(animator),
                OptimizeAllConvertibleLayers = component.optimizeAllConvertibleLayers,
                LayerIndices = component.layerIndices != null
                    ? new List<int>(component.layerIndices)
                    : new List<int>(),
                UpgradeBoolParametersToFloat = component.upgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = component.upgradeIntParametersToFloat,
                AllowFloatConditionApproximation = component.allowFloatConditionApproximation,
                StrictCurveConflicts = component.strictCurveConflicts,
                MergeLayersWithEquivalentMasks = component.mergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = component.resolveOverridePriorityWithStaticComposite,
                EnableAapStateRegisterOptimization = component.enableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = component.removeEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = component.normalizeAlwaysOneParameters,
                ExecuteLateOptimization = component.executeLateOptimization,
                PreserveMmdCompatibility = component.preserveMmdCompatibility,
                MaxPriorityCombinations = component.maxPriorityCombinations,
                VerboseLogging = component.verboseLogging,
                GeneratedLayerName = component.generatedLayerName,
                WeightParameterPrefix = component.weightParameterPrefix,
                SourceObjectName = component.gameObject != null ? component.gameObject.name : string.Empty,
            };
        }

        public AnimatorOptimizerOptions CreateOptions()
        {
            return new AnimatorOptimizerOptions
            {
                GeneratedLayerName = string.IsNullOrWhiteSpace(GeneratedLayerName)
                    ? AnimatorOptimizerConverter.GeneratedLayerName
                    : GeneratedLayerName,
                WeightParameterPrefix = string.IsNullOrWhiteSpace(WeightParameterPrefix)
                    ? "AO/LayerWeight"
                    : WeightParameterPrefix,
                RequireAtLeastTwoLayers = true,
                StrictCurveConflicts = StrictCurveConflicts,
                UpgradeBoolParametersToFloat = UpgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = UpgradeIntParametersToFloat,
                AllowFloatConditionApproximation = AllowFloatConditionApproximation,
                MergeLayersWithEquivalentMasks = MergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = ResolveOverridePriorityWithStaticComposite,
                EnableAapStateRegisterOptimization = EnableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = RemoveEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = NormalizeAlwaysOneParameters,
                ExecuteLateOptimization = ExecuteLateOptimization,
                PreserveMmdCompatibility = PreserveMmdCompatibility,
                HasKnownPlayableLayer = TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer,
                TargetPlayableLayer = PlayableLayer,
                MaxPriorityCombinations = MaxPriorityCombinations,
                VerboseLogging = VerboseLogging,
            };
        }

        public IEnumerable<int> GetRequestedLayerIndices(AnimatorController controller)
        {
            if (controller == null)
                return Enumerable.Empty<int>();
            if (OptimizeAllConvertibleLayers)
            {
                var report = AnimatorOptimizerAnalyzer.Analyze(controller, CreateOptions());
                return report.ConvertibleLayers.Select(layer => layer.Index).ToList();
            }
            return LayerIndices?.Distinct().OrderBy(index => index) ?? Enumerable.Empty<int>();
        }

        public Animator ResolveAnimator(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return Animator;
            if (string.IsNullOrEmpty(AnimatorPath))
                return Animator != null && Animator.transform.IsChildOf(avatarRoot.transform) ?
                    FindAnimatorByIndex(Animator.gameObject, AnimatorComponentIndex) :
                    avatarRoot.GetComponentInChildren<Animator>(true);

            var target = avatarRoot.transform.Find(AnimatorPath);
            return target == null ? null : FindAnimatorByIndex(target.gameObject, AnimatorComponentIndex);
        }

        private static Animator FindAnimatorByIndex(GameObject gameObject, int componentIndex)
        {
            if (gameObject == null)
                return null;
            var animators = gameObject.GetComponents<Animator>();
            return componentIndex >= 0 && componentIndex < animators.Length
                ? animators[componentIndex]
                : animators.FirstOrDefault();
        }

        private static int GetComponentIndex(Animator animator)
        {
            if (animator == null)
                return 0;
            var components = animator.gameObject.GetComponents<Animator>();
            return Array.IndexOf(components, animator);
        }

        private static string GetRelativePath(GameObject root, GameObject target)
        {
            if (root == null || target == null)
                return string.Empty;
            if (root == target)
                return string.Empty;

            var parts = new List<string>();
            var current = target.transform;
            while (current != null && current != root.transform)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            if (current != root.transform)
                return string.Empty;
            parts.Reverse();
            return string.Join("/", parts);
        }
    }

    public static class AnimatorOptimizerBuildState
    {
        private static readonly Dictionary<int, AnimatorOptimizerConfigData> Configs =
            new Dictionary<int, AnimatorOptimizerConfigData>();
        private static readonly HashSet<int> NdmfCaptured = new HashSet<int>();
        private static readonly HashSet<int> NdmfProcessed = new HashSet<int>();

        public static void Capture(GameObject avatarRoot, string stage)
        {
            if (avatarRoot == null)
                return;
            var config = AnimatorOptimizerConfigData.FromAvatar(avatarRoot);
            if (config == null)
                return;
            Configs[avatarRoot.GetInstanceID()] = config;
            if (config.VerboseLogging)
                Debug.Log($"[AnimatorOptimizer] Captured config at {stage} from '{config.SourceObjectName}'.");
        }

        public static AnimatorOptimizerConfigData Get(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return null;
            Configs.TryGetValue(avatarRoot.GetInstanceID(), out var config);
            return config;
        }

        public static void MarkNdmfProcessed(GameObject avatarRoot)
        {
            if (avatarRoot != null)
                NdmfProcessed.Add(avatarRoot.GetInstanceID());
        }

        public static void MarkNdmfCaptured(GameObject avatarRoot)
        {
            if (avatarRoot != null)
                NdmfCaptured.Add(avatarRoot.GetInstanceID());
        }

        public static bool WasNdmfCaptured(GameObject avatarRoot)
        {
            return avatarRoot != null && NdmfCaptured.Contains(avatarRoot.GetInstanceID());
        }

        public static bool WasNdmfProcessed(GameObject avatarRoot)
        {
            return avatarRoot != null && NdmfProcessed.Contains(avatarRoot.GetInstanceID());
        }

        public static void Release(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return;
            Configs.Remove(avatarRoot.GetInstanceID());
            NdmfCaptured.Remove(avatarRoot.GetInstanceID());
            NdmfProcessed.Remove(avatarRoot.GetInstanceID());
        }

        public static void ReleaseAll()
        {
            Configs.Clear();
            NdmfCaptured.Clear();
            NdmfProcessed.Clear();
        }
    }

    public sealed class AnimatorOptimizerConfigCaptureCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 20;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            AnimatorOptimizerBuildState.Capture(avatarGameObject, "early VRCSDK preprocess");
            return true;
        }
    }

    public sealed class AnimatorOptimizerPreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        // VRCFury's EditorOnly cleanup is around -1024 in the current project. We use -1023
        // and a configuration snapshot so the component may already have been removed.
        public int callbackOrder => -1023;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var capturedConfig = AnimatorOptimizerBuildState.Get(avatarGameObject);
            if (capturedConfig != null && capturedConfig.ExecuteLateOptimization)
            {
                Debug.Log("[AnimatorOptimizer] Late optimization requested; early VRCSDK callback deferred.");
                return true;
            }
#if NDMF_AVAILABLE
            if (AnimatorOptimizerBuildState.WasNdmfProcessed(avatarGameObject))
            {
                Debug.Log("[AnimatorOptimizer] NDMF processed this Avatar; VRCSDK fallback skipped.");
                return true;
            }
#endif
            try
            {
                var config = AnimatorOptimizerBuildState.Get(avatarGameObject) ??
                             AnimatorOptimizerConfigData.FromAvatar(avatarGameObject);
                if (config == null)
                    return true;
                return AnimatorOptimizerProcessor.ProcessAvatar(avatarGameObject, config);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Build processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }

    public sealed class AnimatorOptimizerLatePreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        // ASS uses int.MaxValue - 1 in this project. Run after that callback when NDMF
        // is not present; with NDMF, the PlatformFinish plugin owns the late pass.
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var config = AnimatorOptimizerBuildState.Get(avatarGameObject);
            if (config == null || !config.ExecuteLateOptimization)
                return true;
#if NDMF_AVAILABLE
            if (AnimatorOptimizerBuildState.WasNdmfProcessed(avatarGameObject))
                return true;
#endif
            try
            {
                return AnimatorOptimizerProcessor.ProcessAvatar(avatarGameObject, config);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Late build processing failed: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }
    }

    public sealed class AnimatorOptimizerPostprocessCallback : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -1023;

        public void OnPostprocessAvatar()
        {
            AnimatorOptimizerProcessor.RestoreAndCleanup();
            AnimatorOptimizerBuildState.ReleaseAll();
        }
    }

    public static class AnimatorOptimizerProcessor
    {
        private const string GeneratedRoot = "Assets/UnityBox/AnimatorOptimizer/Generated";
        private static readonly List<BuildAssignmentSnapshot> Snapshots = new List<BuildAssignmentSnapshot>();
        private static readonly HashSet<string> GeneratedControllerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private sealed class BuildAssignmentSnapshot
        {
            public VRCAvatarDescriptor Descriptor;
            public VRCAvatarDescriptor.CustomAnimLayer[] BaseLayers;
            public VRCAvatarDescriptor.CustomAnimLayer[] SpecialLayers;
            public List<(Animator Animator, RuntimeAnimatorController Controller)> Animators =
                new List<(Animator, RuntimeAnimatorController)>();
        }

        public static bool ProcessAvatar(GameObject avatarRoot, AnimatorOptimizerConfigData config)
        {
            if (avatarRoot == null || config == null)
                return true;

            var source = ResolveSourceController(avatarRoot, config, out var descriptor, out var targetAnimator);
            if (source == null)
            {
                Debug.LogWarning($"[AnimatorOptimizer] No AnimatorController found for '{avatarRoot.name}', skipping.");
                return true;
            }

            var report = AnimatorOptimizerAnalyzer.Analyze(source, config.CreateOptions());
            var selected = config.GetRequestedLayerIndices(source).ToList();
            if (config.PreserveMmdCompatibility)
                selected = selected.Where(index => index != 0).ToList();
            var aapSelected = selected.Where(index => index >= 0 && index < report.Layers.Count &&
                    report.Layers[index].ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister)
                .ToList();
            var regularSelected = selected.Except(aapSelected).ToList();
            var isAapStateRegisterRequest = selected.Count == 1 && aapSelected.Count == 1;
            var cleanupOnlyRequest = config.RemoveEffectivelyEmptyLayers ||
                                     config.NormalizeAlwaysOneParameters ||
                                     config.PreserveMmdCompatibility;
            if (selected.Count < 2 && !isAapStateRegisterRequest && !cleanupOnlyRequest)
            {
                if (config.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] '{source.name}' has fewer than two convertible Layers, skipping.");
                return true;
            }

            var options = config.CreateOptions();
            IReadOnlyList<string> issues;
            if (aapSelected.Count > 0)
            {
                var safetyOptions = options.Clone();
                safetyOptions.RequireAtLeastTwoLayers = false;
                var safetySelection = regularSelected.Count > 0 ? regularSelected : aapSelected;
                issues = AnimatorOptimizerConverter.GetSelectionIssues(report, safetySelection, safetyOptions);
                if (aapSelected.Count > 1)
                    issues = issues.Concat(new[] { "一次构建最多处理一个 AAP StateRegister Layer。" }).ToList();
            }
            else if (regularSelected.Count >= 2)
            {
                issues = AnimatorOptimizerConverter.GetSelectionIssues(report, selected, options);
            }
            else if (regularSelected.Count > 0)
            {
                var safetyOptions = options.Clone();
                safetyOptions.RequireAtLeastTwoLayers = false;
                issues = AnimatorOptimizerConverter.GetSelectionIssues(report, regularSelected, safetyOptions);
            }
            else
            {
                issues = Array.Empty<string>();
            }
            if (aapSelected.Count > 0 && regularSelected.Count > 0)
            {
                issues = issues.Concat(AnimatorOptimizerConverter.GetParameterSafetyIssues(report, aapSelected)).ToList();
                var aapReports = aapSelected.Select(index => report.Layers[index]).ToList();
                var regularReports = regularSelected.Select(index => report.Layers[index]).ToList();
                if (aapReports.SelectMany(layer => layer.Bindings)
                    .Intersect(regularReports.SelectMany(layer => layer.Bindings)).Any())
                {
                    issues = issues.Concat(new[]
                    {
                        "AAP StateRegister Layer 与普通合并 Layer 存在动画绑定重叠；分两阶段替换会改变原始 Layer 顺序，因此构建已阻止。",
                    }).ToList();
                }
                if (aapSelected.Select(index => report.Layers[index].Name).Distinct(StringComparer.Ordinal).Count() !=
                    aapSelected.Count)
                {
                    issues = issues.Concat(new[]
                    {
                        "多个 AAP StateRegister 源 Layer 名称重复，无法在普通 Layer 合并后可靠定位；请重命名后再构建。",
                    }).ToList();
                }
            }
            if (issues.Count > 0)
            {
                Debug.LogWarning(
                    $"[AnimatorOptimizer] Skipped '{source.name}' because the selected Layers are not safe:\n- " +
                    string.Join("\n- ", issues));
                var explicitSelection = !config.OptimizeAllConvertibleLayers &&
                    config.LayerIndices != null && config.LayerIndices.Count > 0;
                if (!cleanupOnlyRequest || explicitSelection)
                    return true;

                // Cleanup options are deliberately independent from Layer conversion.
                // If the requested conversion is unsafe, still run the proven cleanup
                // pass on a copy instead of leaving empty resources behind.
                aapSelected.Clear();
                regularSelected.Clear();
            }

            var descriptorSnapshot = descriptor != null || targetAnimator != null
                ? SnapshotDescriptor(descriptor, targetAnimator)
                : null;
            if (descriptorSnapshot != null)
                Snapshots.Add(descriptorSnapshot);

            var copyPath = string.Empty;
            try
            {
                copyPath = GetGeneratedControllerPath(avatarRoot, source);
                EnsureFolder(Path.GetDirectoryName(copyPath)?.Replace('\\', '/'));
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(copyPath) != null)
                    AssetDatabase.DeleteAsset(copyPath);
                var sourcePath = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrWhiteSpace(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, copyPath))
                    throw new InvalidOperationException("无法复制源 AnimatorController 到 Generated: " + sourcePath);
                GeneratedControllerPaths.Add(copyPath);

                AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
                var generated = AssetDatabase.LoadAssetAtPath<AnimatorController>(copyPath);
                if (generated == null)
                    throw new InvalidOperationException("复制后无法读取 Generated AnimatorController: " + copyPath);

                var aapMarkers = new Dictionary<int, string>();
                foreach (var aapIndex in aapSelected)
                {
                    if (aapIndex < 0 || aapIndex >= (generated.layers ?? Array.Empty<AnimatorControllerLayer>()).Length)
                        throw new InvalidOperationException("复制后的 Controller 缺少 AAP StateRegister 源 Layer: " + aapIndex);
                    var marker = "__AO_AAP_SOURCE_" + aapIndex + "_" + Guid.NewGuid().ToString("N");
                    generated.layers[aapIndex].name = marker;
                    aapMarkers[aapIndex] = marker;
                }

                if (regularSelected.Count >= 2)
                    AnimatorOptimizerConverter.ConvertInPlace(generated, regularSelected, options);

                foreach (var aapIndex in aapSelected)
                {
                    var sourceLayerName = aapMarkers[aapIndex];
                    var currentIndex = Array.FindIndex(generated.layers ?? Array.Empty<AnimatorControllerLayer>(),
                        layer => layer != null && string.Equals(layer.name, sourceLayerName, StringComparison.Ordinal));
                    if (currentIndex < 0)
                        throw new InvalidOperationException("普通 Layer 合并后找不到待转换的 AAP StateRegister Layer: " + sourceLayerName);
                    AnimatorOptimizerConverter.ConvertAapStateRegisterInPlace(generated, currentIndex, options);
                }
                var bindingRoot = targetAnimator != null ? targetAnimator.transform : avatarRoot.transform;
                if (regularSelected.Count < 2 && aapSelected.Count == 0)
                    options.RequireAtLeastTwoLayers = false;
                var cleanupMessages = AnimatorOptimizerConverter.CleanupControllerInPlace(
                    generated, bindingRoot, options);
                AssignController(descriptor, targetAnimator, config, generated);
                EditorUtility.SetDirty(avatarRoot);
                if (config.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] Optimized '{source.name}' into '{copyPath}'. " +
                              string.Join(" ", cleanupMessages));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Failed to optimize '{source.name}': {exception.Message}\n{exception.StackTrace}");
                RestoreSnapshot(descriptorSnapshot);
                if (!string.IsNullOrWhiteSpace(copyPath) &&
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(copyPath) != null)
                {
                    AssetDatabase.DeleteAsset(copyPath);
                    GeneratedControllerPaths.Remove(copyPath);
                }
                return false;
            }
        }

        public static void RestoreAndCleanup()
        {
            foreach (var snapshot in Snapshots)
                RestoreSnapshot(snapshot);
            Snapshots.Clear();

            foreach (var path in GeneratedControllerPaths.ToList())
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
            GeneratedControllerPaths.Clear();
            AssetDatabase.SaveAssets();
        }

        private static AnimatorController ResolveSourceController(
            GameObject avatarRoot,
            AnimatorOptimizerConfigData config,
            out VRCAvatarDescriptor descriptor,
            out Animator targetAnimator)
        {
            descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            targetAnimator = config.ResolveAnimator(avatarRoot);
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
                return config.Controller as AnimatorController;
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AnimatorComponent)
                return targetAnimator != null ? targetAnimator.runtimeAnimatorController as AnimatorController : null;
            return GetPlayableController(descriptor, config.PlayableLayer);
        }

        private static void AssignController(
            VRCAvatarDescriptor descriptor,
            Animator targetAnimator,
            AnimatorOptimizerConfigData config,
            AnimatorController controller)
        {
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AnimatorComponent ||
                config.TargetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
            {
                if (targetAnimator != null)
                    targetAnimator.runtimeAnimatorController = controller;
                return;
            }

            var layers = GetPlayableLayers(descriptor, config.PlayableLayer, out var isBase);
            if (layers == null)
                return;
            var layerIndex = Array.FindIndex(layers, layer => layer.type == ToVrcLayerType(config.PlayableLayer));
            if (layerIndex < 0)
                return;
            layers[layerIndex].animatorController = controller;
            layers[layerIndex].isDefault = false;
            if (isBase)
                descriptor.baseAnimationLayers = layers;
            else
                descriptor.specialAnimationLayers = layers;
        }

        private static BuildAssignmentSnapshot SnapshotDescriptor(
            VRCAvatarDescriptor descriptor,
            Animator targetAnimator)
        {
            var snapshot = new BuildAssignmentSnapshot
            {
                Descriptor = descriptor,
                BaseLayers = descriptor == null || descriptor.baseAnimationLayers == null
                    ? null
                    : (VRCAvatarDescriptor.CustomAnimLayer[])descriptor.baseAnimationLayers.Clone(),
                SpecialLayers = descriptor == null || descriptor.specialAnimationLayers == null
                    ? null
                    : (VRCAvatarDescriptor.CustomAnimLayer[])descriptor.specialAnimationLayers.Clone(),
            };
            if (targetAnimator != null)
                snapshot.Animators.Add((targetAnimator, targetAnimator.runtimeAnimatorController));
            return snapshot;
        }

        private static void RestoreSnapshot(BuildAssignmentSnapshot snapshot)
        {
            if (snapshot == null)
                return;
            if (snapshot.Descriptor != null)
            {
                snapshot.Descriptor.baseAnimationLayers = snapshot.BaseLayers;
                snapshot.Descriptor.specialAnimationLayers = snapshot.SpecialLayers;
                EditorUtility.SetDirty(snapshot.Descriptor);
            }
            foreach (var assignment in snapshot.Animators)
            {
                if (assignment.Animator != null)
                    assignment.Animator.runtimeAnimatorController = assignment.Controller;
            }
        }

        private static AnimatorController GetPlayableController(
            VRCAvatarDescriptor descriptor,
            AnimatorOptimizerComponent.PlayableLayer playableLayer)
        {
            if (descriptor == null)
                return null;
            var layers = GetPlayableLayers(descriptor, playableLayer, out _);
            if (layers == null)
                return null;
            var layer = layers.FirstOrDefault(candidate => candidate.type == ToVrcLayerType(playableLayer));
            return layer.animatorController as AnimatorController;
        }

        private static VRCAvatarDescriptor.CustomAnimLayer[] GetPlayableLayers(
            VRCAvatarDescriptor descriptor,
            AnimatorOptimizerComponent.PlayableLayer playableLayer,
            out bool isBase)
        {
            isBase = playableLayer <= AnimatorOptimizerComponent.PlayableLayer.FX;
            return isBase ? descriptor?.baseAnimationLayers : descriptor?.specialAnimationLayers;
        }

        private static VRCAvatarDescriptor.AnimLayerType ToVrcLayerType(
            AnimatorOptimizerComponent.PlayableLayer playableLayer)
        {
            switch (playableLayer)
            {
                case AnimatorOptimizerComponent.PlayableLayer.Base:
                    return VRCAvatarDescriptor.AnimLayerType.Base;
                case AnimatorOptimizerComponent.PlayableLayer.Additive:
                    return VRCAvatarDescriptor.AnimLayerType.Additive;
                case AnimatorOptimizerComponent.PlayableLayer.Gesture:
                    return VRCAvatarDescriptor.AnimLayerType.Gesture;
                case AnimatorOptimizerComponent.PlayableLayer.Action:
                    return VRCAvatarDescriptor.AnimLayerType.Action;
                case AnimatorOptimizerComponent.PlayableLayer.FX:
                    return VRCAvatarDescriptor.AnimLayerType.FX;
                case AnimatorOptimizerComponent.PlayableLayer.Sitting:
                    return VRCAvatarDescriptor.AnimLayerType.Sitting;
                case AnimatorOptimizerComponent.PlayableLayer.TPose:
                    return VRCAvatarDescriptor.AnimLayerType.TPose;
                default:
                    return VRCAvatarDescriptor.AnimLayerType.IKPose;
            }
        }

        private static string GetGeneratedControllerPath(GameObject avatarRoot, AnimatorController source)
        {
            var avatarName = SanitizeAssetName(avatarRoot != null ? avatarRoot.name : "Avatar");
            var controllerName = SanitizeAssetName(source != null ? source.name : "Controller");
            return $"{GeneratedRoot}/{avatarName}/{controllerName}_AnimatorOptimizer.controller";
        }

        private static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Avatar";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
            return new string(chars);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "Assets")
                return;
            folderPath = folderPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folderPath))
                return;
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
