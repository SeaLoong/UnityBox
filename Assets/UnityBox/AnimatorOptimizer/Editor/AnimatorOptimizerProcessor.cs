using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerConfigData
    {
        public AnimatorOptimizerComponent.TargetMode TargetMode;
        public AnimatorOptimizerComponent.PlayableLayer PlayableLayer;
        public Animator Animator;
        public RuntimeAnimatorController Controller;
        public string AnimatorPath;
        public List<string> AnimatorPathSegments;
        public int AnimatorComponentIndex;
        public bool OptimizeAllConvertibleLayers;
        public List<int> LayerIndices = new List<int>();
        public bool UseVrChatParameterSemantics;
        public bool UpgradeBoolParametersToFloat;
        public bool UpgradeIntParametersToFloat;
        public bool AllowFloatConditionApproximation;
        public AnimatorOptimizerComponent.WriteDefaultsPolicy WriteDefaultsPolicy =
            AnimatorOptimizerComponent.WriteDefaultsPolicy.AllowCoveredStates;
        public bool IgnoreTransitionTimingForStaticMotions;
        public AnimatorOptimizerComponent.HeuristicFlags Heuristics;
        public int ShortAnimationMaxFrames = 5;
        public float ShortAnimationMaxSeconds = 0.1f;
        public int MotionTimeSampleCount = 9;
        public bool StrictCurveConflicts;
        public bool MergeLayersWithEquivalentMasks;
        public bool ResolveOverridePriorityWithStaticComposite;
        public bool MergeSynchronizedStateMachines = true;
        public bool EnableAapStateRegisterOptimization;
        public bool RemoveEffectivelyEmptyLayers;
        public bool NormalizeAlwaysOneParameters;
        public bool ExecuteLateOptimization;
        public bool PreserveMmdCompatibility;
        public int MaxPriorityCombinations;
        public int MaxAapDecisionNodes = 4096;
        public bool VerboseLogging;
        public string GeneratedLayerName;
        public string WeightParameterPrefix;
        public string SourceObjectName;

        public static AnimatorOptimizerConfigData FromAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return null;
            var component = avatarRoot.GetComponentsInChildren<AnimatorOptimizerComponent>(true)
                .FirstOrDefault(candidate => candidate.enabled);
            return FromComponent(component, avatarRoot);
        }

        public static AnimatorOptimizerConfigData FromComponent(
            AnimatorOptimizerComponent component,
            GameObject avatarRoot = null,
            bool requireEnabled = true)
        {
            if (component == null || requireEnabled && !component.enabled)
                return null;

            avatarRoot ??= AnimatorOptimizerPlatform.FindAvatarRoot(component);
            var animator = component.animator;
            if (component.targetMode != AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer && animator == null)
                animator = FindDefaultAnimator(avatarRoot);

            return new AnimatorOptimizerConfigData
            {
                TargetMode = component.targetMode,
                PlayableLayer = component.playableLayer,
                Animator = animator,
                Controller = component.controller != null
                    ? component.controller
                    : animator != null ? animator.runtimeAnimatorController : null,
                AnimatorPath = GetRelativePath(avatarRoot, animator != null ? animator.gameObject : null),
                AnimatorPathSegments = GetPathSegments(avatarRoot, animator != null ? animator.gameObject : null),
                AnimatorComponentIndex = GetComponentIndex(animator),
                OptimizeAllConvertibleLayers = component.optimizeAllConvertibleLayers,
                LayerIndices = component.layerIndices != null
                    ? new List<int>(component.layerIndices)
                    : new List<int>(),
                UseVrChatParameterSemantics = component.targetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer || component.useVrChatParameterSemantics,
                UpgradeBoolParametersToFloat = component.upgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = component.upgradeIntParametersToFloat,
                AllowFloatConditionApproximation = component.allowFloatConditionApproximation,
                WriteDefaultsPolicy = component.writeDefaultsPolicy,
                IgnoreTransitionTimingForStaticMotions = component.ignoreTransitionTimingForStaticMotions,
                Heuristics = component.heuristics,
                ShortAnimationMaxFrames = component.shortAnimationMaxFrames,
                ShortAnimationMaxSeconds = component.shortAnimationMaxSeconds,
                MotionTimeSampleCount = component.motionTimeSampleCount,
                StrictCurveConflicts = component.strictCurveConflicts,
                MergeLayersWithEquivalentMasks = component.mergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = component.resolveOverridePriorityWithStaticComposite,
                MergeSynchronizedStateMachines = component.mergeSynchronizedStateMachines,
                EnableAapStateRegisterOptimization = component.enableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = component.removeEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = component.normalizeAlwaysOneParameters,
                ExecuteLateOptimization = component.executeLateOptimization,
                PreserveMmdCompatibility = component.preserveMmdCompatibility,
                MaxPriorityCombinations = component.maxPriorityCombinations,
                MaxAapDecisionNodes = component.maxAapDecisionNodes,
                VerboseLogging = component.verboseLogging,
                GeneratedLayerName = component.generatedLayerName,
                WeightParameterPrefix = component.weightParameterPrefix,
                SourceObjectName = component.gameObject != null ? component.gameObject.name : string.Empty,
            };
        }

        /// <summary>Editor-side configuration snapshot; build processors additionally enforce the selection scope.</summary>
        public static AnimatorOptimizerOptions CreateOptions(AnimatorOptimizerComponent component)
        {
            return FromComponent(component, null, requireEnabled: false)?.CreateOptions() ?? new AnimatorOptimizerOptions();
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
                UseVrChatParameterSemantics = UseVrChatParameterSemantics,
                UpgradeBoolParametersToFloat = UpgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = UpgradeIntParametersToFloat,
                AllowFloatConditionApproximation = AllowFloatConditionApproximation,
                WriteDefaultsPolicy = WriteDefaultsPolicy,
                IgnoreTransitionTimingForStaticMotions = IgnoreTransitionTimingForStaticMotions,
                Heuristics = Heuristics,
                ShortAnimationMaxFrames = ShortAnimationMaxFrames,
                ShortAnimationMaxSeconds = ShortAnimationMaxSeconds,
                MotionTimeSampleCount = MotionTimeSampleCount,
                MergeLayersWithEquivalentMasks = MergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = ResolveOverridePriorityWithStaticComposite,
                MergeSynchronizedStateMachines = MergeSynchronizedStateMachines,
                EnableAapStateRegisterOptimization = EnableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = RemoveEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = NormalizeAlwaysOneParameters,
                ExecuteLateOptimization = ExecuteLateOptimization,
                PreserveMmdCompatibility = PreserveMmdCompatibility,
                HasKnownPlayableLayer = TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer,
                TargetPlayableLayer = PlayableLayer,
                MaxPriorityCombinations = MaxPriorityCombinations,
                MaxAapDecisionNodes = MaxAapDecisionNodes,
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
            // A valid live reference survives renames/reparenting and is safer than a name lookup.
            if (Animator != null && Animator.transform.IsChildOf(avatarRoot.transform))
                return Animator;
            if (AnimatorPathSegments == null)
                return null;

            var target = avatarRoot.transform;
            foreach (var segment in AnimatorPathSegments)
            {
                Transform match = null;
                for (var i = 0; i < target.childCount; i++)
                {
                    var child = target.GetChild(i);
                    if (child.name != segment) continue;
                    // Ambiguous clone paths must not silently optimize a different object.
                    if (match != null) return null;
                    match = child;
                }
                if (match == null) return null;
                target = match;
            }
            var animators = target.GetComponents<Animator>();
            return AnimatorComponentIndex >= 0 && AnimatorComponentIndex < animators.Length
                ? animators[AnimatorComponentIndex] : null;
        }

        internal static Animator FindDefaultAnimator(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            var rootAnimator = avatarRoot.GetComponent<Animator>();
            if (rootAnimator != null) return rootAnimator;
            var candidates = avatarRoot.GetComponentsInChildren<Animator>(true);
            return candidates.Length == 1 ? candidates[0] : null;
        }

        private static List<string> GetPathSegments(GameObject root, GameObject target)
        {
            if (root == null || target == null || !target.transform.IsChildOf(root.transform))
                return null;
            var segments = new List<string>();
            for (var current = target.transform; current != root.transform; current = current.parent)
                segments.Add(current.name);
            segments.Reverse();
            return segments;
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

        public static void Capture(GameObject avatarRoot, string stage) => Capture(avatarRoot, stage, false);

        public static void Capture(GameObject avatarRoot, string stage, bool useVrChatParameterSemantics)
        {
            if (avatarRoot == null)
                return;
            var config = AnimatorOptimizerConfigData.FromAvatar(avatarRoot);
            if (config == null)
            {
                // Preserve a snapshot after EditorOnly stripping, but honor an explicitly disabled component.
                if (avatarRoot.GetComponentsInChildren<AnimatorOptimizerComponent>(true).Length > 0)
                    Release(avatarRoot);
                return;
            }
            config.UseVrChatParameterSemantics |= useVrChatParameterSemantics;
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

    public static class AnimatorOptimizerProcessor
    {
        private const string GeneratedRoot = "Assets/UnityBox/AnimatorOptimizer/Generated";
        private static readonly List<Action> Snapshots = new List<Action>();
        private static readonly HashSet<string> GeneratedControllerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool ProcessAvatar(GameObject avatarRoot, AnimatorOptimizerConfigData config)
        {
            if (avatarRoot == null || config == null)
                return true;

            var target = ResolveControllerTarget(avatarRoot, config);
            var source = config.TargetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController
                ? config.Controller as AnimatorController : target?.Controller;
            if (config.TargetMode != AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer && target == null)
            {
                Debug.LogError("[AnimatorOptimizer] No unambiguous target Animator exists inside this Avatar. Assign the intended Animator explicitly.");
                return false;
            }
            if (source == null)
            {
                Debug.LogWarning($"[AnimatorOptimizer] No AnimatorController found for '{avatarRoot.name}', skipping.");
                return true;
            }

            var options = config.CreateOptions();
            options.MergeSynchronizedStateMachines &= config.OptimizeAllConvertibleLayers;
            var report = AnimatorOptimizerAnalyzer.Analyze(source, options);
            if (!report.GraphIsValid)
            {
                Debug.LogWarning($"[AnimatorOptimizer] Preserved '{source.name}': invalid controller graph.");
                return true;
            }
            var selected = config.OptimizeAllConvertibleLayers
                ? AnimatorOptimizerConverter.GetRecommendedSelection(report, options).ToList()
                : (config.LayerIndices ?? new List<int>()).Distinct().OrderBy(index => index).ToList();
            if (config.PreserveMmdCompatibility)
                selected = selected.Where(index => index != 0).ToList();
            var aapCount = selected.Count(index => index >= 0 && index < report.Layers.Count &&
                report.Layers[index].ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister);
            var cleanupOnlyRequest = config.RemoveEffectivelyEmptyLayers ||
                                     config.MergeSynchronizedStateMachines ||
                                     config.NormalizeAlwaysOneParameters ||
                                     config.PreserveMmdCompatibility;
            if (selected.Count < 2 && aapCount == 0 && !cleanupOnlyRequest)
            {
                if (config.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] '{source.name}' has fewer than two convertible Layers, skipping.");
                return true;
            }

            var safetyOptions = options.Clone();
            if (selected.Count < 2 && aapCount == 0) safetyOptions.RequireAtLeastTwoLayers = false;
            var issues = selected.Count == 0 ? Array.Empty<string>() :
                AnimatorOptimizerConverter.GetSelectionIssues(report, selected, safetyOptions);
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
                selected.Clear();
                aapCount = 0;
            }

            var assignmentSnapshot = target?.CaptureRestore();
            if (assignmentSnapshot != null)
                Snapshots.Add(assignmentSnapshot);

            var copyPath = string.Empty;
            try
            {
                copyPath = GetGeneratedControllerPath(avatarRoot, source);
                EnsureFolder(Path.GetDirectoryName(copyPath)?.Replace('\\', '/'));
                copyPath = AssetDatabase.GenerateUniqueAssetPath(copyPath);
                var generated = CreateBuildCopy(source, copyPath);
                GeneratedControllerPaths.Add(copyPath);

                // A single batch preserves the pre-upgrade input types and original
                // layer identities even with several AAP layers or duplicate names.
                if (selected.Count >= 2 || aapCount > 0)
                    AnimatorOptimizerConverter.ConvertInPlace(generated, selected, options);
                var bindingRoot = target.BindingRoot ?? avatarRoot.transform;
                if (selected.Count < 2 && aapCount == 0)
                    options.RequireAtLeastTwoLayers = false;
                var cleanupMessages = AnimatorOptimizerConverter.CleanupControllerInPlace(
                    generated, bindingRoot, options);
                target.Assign(generated);
                EditorUtility.SetDirty(avatarRoot);
                if (config.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] Optimized '{source.name}' into '{copyPath}'. " +
                              string.Join(" ", cleanupMessages));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorOptimizer] Failed to optimize '{source.name}': {exception.Message}\n{exception.StackTrace}");
                RestoreSnapshot(assignmentSnapshot);
                Snapshots.Remove(assignmentSnapshot);
                if (!string.IsNullOrWhiteSpace(copyPath) &&
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(copyPath) != null)
                {
                    AssetDatabase.DeleteAsset(copyPath);
                    GeneratedControllerPaths.Remove(copyPath);
                }
                return false;
            }
        }

        private static AnimatorController CreateBuildCopy(AnimatorController source, string path)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.IsMainAsset(source) &&
                sourcePath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
            {
                if (!AssetDatabase.CopyAsset(sourcePath, path))
                    throw new InvalidOperationException("Unable to copy AnimatorController: " + sourcePath);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return AnimatorOptimizerConverter.LoadCopiedController(source, path);
            }

            // NDMF and other preprocessors can provide an unsaved controller. Reuse
            // the converter's graph isolation rather than editing that shared graph.
            var copy = new AnimatorController();
            EditorUtility.CopySerialized(source, copy);
            copy.name = source.name;
            AssetDatabase.CreateAsset(copy, path);
            AnimatorOptimizerConverter.EnsureOwnedMutableGraph(copy);
            var transientAssets = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            foreach (var owned in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                using (var serialized = new SerializedObject(owned))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var reference = property.objectReferenceValue;
                        if (reference is not AnimationClip && reference is not AvatarMask) continue;
                        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(reference))) continue;
                        if (!transientAssets.TryGetValue(reference, out var clone))
                        {
                            clone = UnityEngine.Object.Instantiate(reference);
                            clone.name = reference.name;
                            AssetDatabase.AddObjectToAsset(clone, copy);
                            transientAssets.Add(reference, clone);
                        }
                        property.objectReferenceValue = clone;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
            return copy;
        }

        public static void RestoreAndCleanup()
        {
            for (var index = Snapshots.Count - 1; index >= 0; index--)
                RestoreSnapshot(Snapshots[index]);
            Snapshots.Clear();

            foreach (var path in GeneratedControllerPaths.ToList())
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
            GeneratedControllerPaths.Clear();
            AssetDatabase.SaveAssets();
        }

        private static IAnimatorOptimizerControllerTarget ResolveControllerTarget(
            GameObject avatarRoot, AnimatorOptimizerConfigData config)
        {
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer)
                return AnimatorOptimizerPlatform.Adapter?.ResolvePlayableTarget(avatarRoot, config.PlayableLayer);
            var animator = config.ResolveAnimator(avatarRoot);
            return animator == null ? null : new AnimatorTarget(animator);
        }

        private sealed class AnimatorTarget : IAnimatorOptimizerControllerTarget
        {
            private readonly Animator animator;
            public AnimatorTarget(Animator animator) { this.animator = animator; }
            public AnimatorController Controller => animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            public Transform BindingRoot => animator != null ? animator.transform : null;
            public void Assign(AnimatorController controller)
            {
                if (animator == null) throw new InvalidOperationException("Target Animator was removed before assignment.");
                animator.runtimeAnimatorController = controller;
            }
            public Action CaptureRestore()
            {
                var original = animator.runtimeAnimatorController;
                return () => { if (animator != null) animator.runtimeAnimatorController = original; };
            }
        }

        private static void RestoreSnapshot(Action snapshot) => snapshot?.Invoke();

        private static string GetGeneratedControllerPath(GameObject avatarRoot, AnimatorController source)
        {
            var avatarName = SanitizeAssetName(avatarRoot != null ? avatarRoot.name : "Avatar");
            var controllerName = SanitizeAssetName(source != null ? source.name : "Controller");
            return $"{GeneratedRoot}/{avatarName}/{controllerName}_AnimatorOptimizer.controller";
        }

        internal static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Avatar";
            // A single portable path segment: no dot segments or platform-dependent separators.
            var sanitized = new string(value.Select(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_')
                .Take(80).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Avatar" : sanitized;
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
