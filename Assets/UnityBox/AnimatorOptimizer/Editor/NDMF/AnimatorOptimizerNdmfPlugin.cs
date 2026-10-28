#if NDMF_AVAILABLE
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

[assembly: ExportsPlugin(typeof(UnityBox.AnimatorOptimizer.AnimatorOptimizerNdmfPlugin))]

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerNdmfPlugin : Plugin<AnimatorOptimizerNdmfPlugin>
    {
        public override string QualifiedName => "top.sealoong.unitybox.animator-optimizer";
        public override string DisplayName => "Animator Optimizer";

        protected override void Configure()
        {
            // Capture and strip our SDK-independent authoring component on the build clone,
            // then perform the actual optimization late on the virtual controller.
            InPhase(BuildPhase.Resolving)
                .Run("AnimatorOptimizer/CaptureConfig", context =>
                {
                    // A new NDMF invocation must not inherit processed flags from a previous
                    // (possibly failed) build. Preserve an early SDK snapshot only when the
                    // configuration components have already been stripped in this build.
                    if (context.AvatarRootObject != null && context.AvatarRootObject
                        .GetComponentsInChildren<AnimatorOptimizerComponent>(true).Length > 0)
                        AnimatorOptimizerBuildState.Release(context.AvatarRootObject);
                    AnimatorOptimizerBuildState.Capture(context.AvatarRootObject, "NDMF before RemoveEditorOnly");
                    AnimatorOptimizerBuildState.MarkNdmfCaptured(context.AvatarRootObject);
                    AnimatorOptimizerNdmfProcessor.CaptureSourceLayerSelection(context);
                    AnimatorOptimizerPlatform.StripConfigurationComponents(context.AvatarRootObject);
                })
                .BeforePass("nadena.dev.ndmf.system.RemoveEditorOnly");

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithCompatibleExtensions(new[] { typeof(AnimatorServicesContext), typeof(VirtualControllerContext) }, sequence => sequence.Run(
                    "AnimatorOptimizer/OptimizeVirtualFX",
                    context => AnimatorOptimizerNdmfProcessor.Process(context, false)));

            InPhase(BuildPhase.PlatformFinish)
                .AfterPlugin("top.sealoong.unitybox.avatar-security-system")
                .WithCompatibleExtensions(new[] { typeof(AnimatorServicesContext), typeof(VirtualControllerContext) }, sequence => sequence.Run(
                    "AnimatorOptimizer/OptimizeVirtualFXLate",
                    context => AnimatorOptimizerNdmfProcessor.Process(context, true)));
        }
    }

    internal static partial class AnimatorOptimizerNdmfProcessor
    {
        private const string GeneratedLayerName = AnimatorOptimizerConverter.GeneratedLayerName;
        private const string GeneratedAssetPrefix = AnimatorOptimizerConverter.GeneratedAssetPrefix;
        private const string WeightParameterPrefix = "AO/LayerWeight";
        private static int virtualPriorityCounter;
        // NDMF 1.11 retains the source Unity object in this protected property.
        // Fail closed if a future API removes it; guessing by name/index is unsafe.
        private static readonly PropertyInfo VirtualOriginalObjectProperty = typeof(VirtualNode).GetProperty(
            "OriginalObject",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo VirtualMaskObjectField = typeof(VirtualAvatarMask).GetField(
            "_mask", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class VirtualMotionRef
        {
            public VirtualMotion Motion;
            public float Speed = 1f;
            public float CycleOffset;
            public bool Mirror;
            public AnimatorOptimizerClipBake Bake;
        }

        private sealed class PriorityDimension
        {
            public string Parameter;
            public List<float> Values;
        }

        private sealed class VirtualDecisionDimension
        {
            public string Parameter;
            public AnimatorControllerParameterType Type;
            public List<float> Values;
            public bool IsApproximate;
        }

        private sealed class VirtualCondition
        {
            public string Parameter;
            public AnimatorControllerParameterType Type;
            public AnimatorConditionMode Mode;
            public float Threshold;

            public bool IsBool => Type == AnimatorControllerParameterType.Bool;
            public bool IsInt => Type == AnimatorControllerParameterType.Int;
            public bool IsFloat => Type == AnimatorControllerParameterType.Float;

            public bool Matches(float value)
            {
                if (IsBool)
                {
                    var actual = value >= 0.5f;
                    return Mode switch
                    {
                        AnimatorConditionMode.If => actual,
                        AnimatorConditionMode.IfNot => !actual,
                        AnimatorConditionMode.Equals => (actual ? 1f : 0f) == Threshold,
                        AnimatorConditionMode.NotEqual => (actual ? 1f : 0f) != Threshold,
                        _ => false,
                    };
                }

                switch (Mode)
                {
                    case AnimatorConditionMode.Equals:
                        return value == Threshold;
                    case AnimatorConditionMode.NotEqual:
                        return value != Threshold;
                    case AnimatorConditionMode.Greater:
                        return value > Threshold;
                    case AnimatorConditionMode.Less:
                        return value < Threshold;
                    default:
                        return false;
                }
            }
        }

        private sealed class VirtualBranch
        {
            public VirtualMotionRef Motion;
            public List<VirtualCondition> Conditions;
            public int Order;
        }

        private sealed class VirtualNormalizedTransition
        {
            public VirtualState Source;
            public VirtualState Destination;
            public List<VirtualCondition> Conditions;
            public int Order;
            // Only meaningful for Any State transitions (Source == null).
            public bool CanTransitionToSelf = true;
        }

        private sealed class VirtualPlan
        {
            public bool IsConstant;
            public VirtualMotionRef ConstantMotion;
            public List<VirtualBranch> Branches;
            public List<string> BooleanParameters;
            public Dictionary<int, VirtualMotionRef> TruthTable;
            public string SelectorParameter;
            public AnimatorControllerParameterType SelectorParameterType;
            public List<(float Value, VirtualMotionRef Motion)> SelectorSamples;
            public bool SelectorIsApproximate;
            public List<VirtualDecisionDimension> DecisionDimensions;
            public Dictionary<int, VirtualMotionRef> DecisionTable;
            public HashSet<string> ReferencedParameters = new HashSet<string>(StringComparer.Ordinal);
            public string StateTag;
            public List<string> Assumptions = new List<string>();
            public HashSet<string> AssumedBinaryParameters = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class VirtualCandidate
        {
            public VirtualLayer Layer;
            public int Index;
            public VirtualPlan Plan;
            public HashSet<BindingKey> Bindings;
            public VirtualAvatarMask Mask;
            public string MaskKey;
        }

        private sealed class VirtualCandidateGroup
        {
            public AnimatorLayerBlendingMode BlendingMode;
            public string MaskKey;
            public VirtualAvatarMask Mask;
            public string StateTag;
            public List<VirtualCandidate> Candidates;
            public List<VirtualCandidate> OriginalCandidates;
            public int FirstIndex => OriginalCandidates.Min(candidate => candidate.Index);
        }

        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            public BindingKey(EditorCurveBinding binding)
            {
                Path = binding.path ?? string.Empty;
                BindingType = binding.type;
                PropertyName = binding.propertyName ?? string.Empty;
                IsPPtr = binding.isPPtrCurve;
            }

            private string Path { get; }
            private Type BindingType { get; }
            private string PropertyName { get; }
            public bool IsPPtr { get; }

            public bool IsSafeAdditiveFloatBinding => !IsPPtr &&
                ((BindingType == typeof(Transform) &&
                    (string.Equals(PropertyName, "m_LocalPosition.x", StringComparison.Ordinal) ||
                     string.Equals(PropertyName, "m_LocalPosition.y", StringComparison.Ordinal) ||
                     string.Equals(PropertyName, "m_LocalPosition.z", StringComparison.Ordinal))) ||
                 (BindingType == typeof(SkinnedMeshRenderer) && PropertyName.StartsWith("blendShape.", StringComparison.Ordinal)));

            public bool Equals(BindingKey other)
            {
                return IsPPtr == other.IsPPtr &&
                       string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                       BindingType == other.BindingType &&
                       string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is BindingKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Path);
                    hash = hash * 397 ^ (BindingType?.GetHashCode() ?? 0);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(PropertyName);
                    hash = hash * 397 ^ (IsPPtr ? 1 : 0);
                    return hash;
                }
            }
        }

        private sealed class SourceLayerSelection
        {
            public bool IsCaptured;
            public bool HasActivePlayableController;
            public bool IsValid;
            public List<AnimatorStateMachine> StateMachines = new List<AnimatorStateMachine>();
            public SourceLayerSelection() { }
        }

        internal static void CaptureSourceLayerSelection(BuildContext context)
        {
            var selection = context.GetState<SourceLayerSelection>();
            selection.IsCaptured = true;
            selection.HasActivePlayableController = false;
            selection.IsValid = false;
            selection.StateMachines.Clear();
            var config = AnimatorOptimizerBuildState.Get(context.AvatarRootObject);
            if (config == null) return;
            AnimatorController source;
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer)
            {
                var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                var type = ToVrcLayerType(config.PlayableLayer);
                source = descriptor == null || !descriptor.customizeAnimationLayers ? null :
                    (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                    .Concat(descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                    .Where(layer => layer.type == type && !layer.isDefault)
                    .Select(layer => layer.animatorController as AnimatorController).FirstOrDefault();
            }
            else
            {
                var target = config.ResolveAnimator(context.AvatarRootObject);
                source = config.TargetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController
                    ? config.Controller as AnimatorController
                    : target != null ? target.runtimeAnimatorController as AnimatorController : null;
            }
            selection.HasActivePlayableController = source != null;
            if (source == null || config.OptimizeAllConvertibleLayers) return;
            var layers = source.layers;
            foreach (var index in (config.LayerIndices ?? new List<int>()).Distinct())
            {
                if (index < 0 || index >= layers.Length || layers[index].stateMachine == null) return;
                selection.StateMachines.Add(layers[index].stateMachine);
            }
            selection.IsValid = true;
        }

        private static object ResolveVirtualControllerKey(AnimatorOptimizerConfigData config, GameObject root)
        {
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer)
                return ToVrcLayerType(config.PlayableLayer);
            var target = config.ResolveAnimator(root);
            // An explicit source different from the assigned controller is materialized
            // by the SDK fallback; optimizing the assigned virtual controller would edit
            // the wrong source. NDMF keys Animator controllers by component, not asset.
            if (target == null || config.TargetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController &&
                target.runtimeAnimatorController != config.Controller)
                return null;
            return target;
        }

        private static bool TryResolveRequestedVirtualLayers(
            IEnumerable<VirtualLayer> layers,
            IReadOnlyList<AnimatorStateMachine> sourceStateMachines,
            out HashSet<VirtualLayer> requested)
        {
            requested = new HashSet<VirtualLayer>();
            var bySource = layers.Where(layer => layer != null && layer.StateMachine != null)
                .Select(layer => (Layer: layer, Source: GetVirtualOriginalObject<AnimatorStateMachine>(layer.StateMachine)))
                .Where(item => item.Source != null).ToLookup(item => item.Source, item => item.Layer);
            foreach (var stateMachine in sourceStateMachines)
            {
                var matches = bySource[stateMachine].ToList();
                if (matches.Count != 1 || !requested.Add(matches[0])) return false;
            }
            return true;
        }

        public static void Process(BuildContext context, bool latePhase)
        {
            if (context == null || context.AvatarRootObject == null)
                return;

            var config = AnimatorOptimizerBuildState.Get(context.AvatarRootObject);
            if (config == null)
                return;
            if (config.ExecuteLateOptimization != latePhase)
                return;
            var selection = context.GetState<SourceLayerSelection>();
            if (!selection.IsCaptured) CaptureSourceLayerSelection(context);
            if (config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer &&
                !selection.HasActivePlayableController)
            {
                // NDMF's VRChat bindings enable customization when services activate. An
                // inactive source must not become eligible merely because that occurred.
                AnimatorOptimizerBuildState.MarkNdmfProcessed(context.AvatarRootObject);
                Debug.LogWarning("[AnimatorOptimizer] The configured Playable Layer had no active custom controller before NDMF; preserving it.");
                return;
            }

            // Compatible extensions remain live when another plugin owns them. When no
            // plugin needs animator services, only activate them after the target checks,
            // and release exactly the contexts acquired here before returning to NDMF's
            // statically scheduled passes. An Avatar without AO must have no side effects.
            var animatorServices = GetActiveExtension<AnimatorServicesContext>(context);
            var ownsAnimatorServices = animatorServices == null;
            var ownsControllers = GetActiveExtension<VirtualControllerContext>(context) == null;
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var restoreDisabledDescriptor = ownsControllers && descriptor != null &&
                !descriptor.customizeAnimationLayers;
            var baseLayers = restoreDisabledDescriptor ? descriptor.baseAnimationLayers?.ToArray() : null;
            var specialLayers = restoreDisabledDescriptor ? descriptor.specialAnimationLayers?.ToArray() : null;
            try
            {
                if (ownsAnimatorServices)
                    animatorServices = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                ProcessVirtual(context, config, animatorServices);
            }
            finally
            {
                try
                {
                    if (ownsAnimatorServices)
                        context.DeactivateExtensionContext<AnimatorServicesContext>();
                }
                finally
                {
                    try
                    {
                        if (ownsControllers)
                            context.DeactivateExtensionContext<VirtualControllerContext>();
                    }
                    finally
                    {
                        // Optimizing a child Animator must not enable unrelated inactive
                        // Avatar Playable Layers as a side effect of NDMF's platform adapter.
                        if (restoreDisabledDescriptor && descriptor != null)
                        {
                            descriptor.baseAnimationLayers = baseLayers;
                            descriptor.specialAnimationLayers = specialLayers;
                            descriptor.customizeAnimationLayers = false;
                        }
                    }
                }
            }
        }

        private static T GetActiveExtension<T>(BuildContext context) where T : class, IExtensionContext
        {
            // NDMF 1.9+ exposes no TryGetExtension; this accessor throws only when the
            // requested context is absent. Activation errors are never caught here.
            try { return context.Extension<T>(); }
            catch (Exception) { return null; }
        }

        private static void ProcessVirtual(BuildContext context, AnimatorOptimizerConfigData config,
            AnimatorServicesContext animatorServices)
        {
            var controllerKey = ResolveVirtualControllerKey(config, context.AvatarRootObject);
            if (controllerKey == null ||
                !animatorServices.ControllerContext.Controllers.TryGetValue(controllerKey, out var controller) ||
                controller == null)
            {
                // A manual selection on an assigned/playable controller refers to the
                // pre-NDMF source. Without a virtual identity mapping the SDK fallback
                // must not reinterpret those indices on an already transformed graph.
                // ExplicitController keeps its original asset as the physical source.
                if (!config.OptimizeAllConvertibleLayers &&
                    config.TargetMode != AnimatorOptimizerComponent.TargetMode.ExplicitController)
                    AnimatorOptimizerBuildState.MarkNdmfProcessed(context.AvatarRootObject);
                Debug.LogWarning("[AnimatorOptimizer] NDMF could not find the configured virtual AnimatorController; manual selections on assigned/playable controllers will be preserved.");
                return;
            }

            var options = config.CreateOptions().Clone();
            // Only AvatarPlayableLayer targets (or explicitly opted-in Animator targets)
            // inherit VRChat builtins; merely participating in an Avatar build is insufficient.
            // Coalescing is a separate whole-controller pass. Explicit index mode
            // must not silently modify unrequested legacy reactive layers.
            options.MergeSynchronizedStateMachines &= config.OptimizeAllConvertibleLayers;
            if (options.EnableAapStateRegisterOptimization)
            {
                if (!config.OptimizeAllConvertibleLayers)
                {
                    // SDK fallback cannot map source indices across NDMF layer reordering.
                    AnimatorOptimizerBuildState.MarkNdmfProcessed(context.AvatarRootObject);
                    Debug.LogWarning("[AnimatorOptimizer] AAP with manual Layer indices is skipped during NDMF builds because source Layer identity cannot be preserved through the physical fallback.");
                    return;
                }
                // The symbolic AAP materializer currently targets physical controllers.
                // VirtualClip can write FloatCurves, but this adapter does not yet
                // implement the same register/layer contract. Keep fallback eligible.
                Debug.LogWarning("[AnimatorOptimizer] AAP StateRegister is handled by the physical VRCSDK fallback; NDMF virtual processing is skipped.");
                return;
            }

            // Mark the virtual path before any remaining eligibility checks. Even when no
            // Layer is profitable to merge, the VRCSDK fallback must not process the
            // physical source controller a second time.
            AnimatorOptimizerBuildState.MarkNdmfProcessed(context.AvatarRootObject);

            if (controller.Layers.Any(layer => layer != null && IsGeneratedLayerName(layer.Name, options.GeneratedLayerName)))
                return;

            var layers = controller.Layers.ToList();
            if (layers.Any(layer => layer == null || layer.SyncedLayerMotionOverrides == null || layer.SyncedLayerBehaviourOverrides == null ||
                !IsValidVirtualGraph(layer.StateMachine, new HashSet<VirtualNode>(), new HashSet<VirtualNode>()) ||
                layer.SyncedLayerMotionOverrides.Values.Any(motion => !IsValidVirtualGraph(motion, new HashSet<VirtualNode>(), new HashSet<VirtualNode>()))))
            {
                Debug.LogWarning("[AnimatorOptimizer] Virtual graph contains cyclic or excessively deep references; preserving the controller.");
                return;
            }
            var referenceIssue = FindInvalidVirtualLayerReferenceIssue(layers, options);
            if (referenceIssue != null)
            {
                Debug.LogWarning("[AnimatorOptimizer] Preserving virtual controller: " + referenceIssue);
                return;
            }
            // Resolve source selections before any normalization or cleanup mutates the
            // graph, so an unmappable request really leaves the controller untouched.
            HashSet<VirtualLayer> requestedLayers = null;
            if (!config.OptimizeAllConvertibleLayers)
            {
                var sourceSelection = context.GetState<SourceLayerSelection>();
                if (!sourceSelection.IsValid || !TryResolveRequestedVirtualLayers(
                        layers, sourceSelection.StateMachines, out requestedLayers))
                {
                    Debug.LogWarning("[AnimatorOptimizer] Manual Layer selection cannot be mapped unambiguously after NDMF transformations; preserving the virtual controller.");
                    return;
                }
            }
            if (options.NormalizeAlwaysOneParameters)
            {
                Debug.LogWarning("[AnimatorOptimizer] AlwaysOne aliasing assumes named parameters remain one at runtime; external writers cannot be excluded by static analysis.");
                NormalizeVirtualAlwaysOneParameters(controller);
            }
            var bindingsByLayer = new Dictionary<VirtualLayer, HashSet<BindingKey>>();
            var completeBindings = new HashSet<VirtualLayer>();
            foreach (var layer in layers)
            {
                var bindings = new HashSet<BindingKey>();
                if (TryCollectLayerBindings(layer, bindings))
                {
                    bindingsByLayer[layer] = bindings;
                    completeBindings.Add(layer);
                }
            }

            var analysisScope = BeginLayerScope(controller, options);
            var candidates = layers
                .Select((layer, index) => TryBuildPlan(controller, layer, options, out var plan, analysisScope)
                    ? new VirtualCandidate
                    {
                        Layer = layer,
                        Index = index,
                        Plan = plan,
                        Bindings = bindingsByLayer.TryGetValue(layer, out var b) ? b : new HashSet<BindingKey>(),
                        Mask = layer.AvatarMask,
                        MaskKey = GetMaskKey(layer.AvatarMask),
                    }
                    : null)
                .Where(candidate => candidate != null)
                .ToList();

            var syncedTargets = layers.Where(layer => layer != null && layer.SyncedLayerIndex >= 0)
                .Select(layer => layer.SyncedLayerIndex).ToHashSet();
            if (options.HasKnownPlayableLayer)
                syncedTargets.UnionWith(EnumerateVirtualLayerControls(layers)
                    .Where(control => ToVrcLayerType(control.playable) == ToVrcLayerType(options.TargetPlayableLayer))
                    .Select(control => control.layer));
            var selected = candidates
                .Where(candidate => completeBindings.Contains(candidate.Layer))
                .Where(candidate => !syncedTargets.Contains(candidate.Layer.VirtualLayerIndex))
                .ToList();

            if (requestedLayers != null)
                selected = selected.Where(candidate => requestedLayers.Contains(candidate.Layer)).ToList();
            if (options.PreserveMmdCompatibility)
                selected = selected.Where(candidate => candidate.Index != 0).ToList();

            var hasKnownPlayableLayer = config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer;
            if (hasKnownPlayableLayer || EnumerateVirtualLayerControls(layers).Any())
            {
                var layerControlIssue = FindVirtualLayerControlIssue(
                    layers,
                    selected.Select(candidate => candidate.Layer).ToHashSet(),
                    config.PlayableLayer,
                    hasKnownPlayableLayer);
                if (layerControlIssue != null)
                {
                    Debug.LogWarning("[AnimatorOptimizer] NDMF skipped virtual Layer merge: " + layerControlIssue);
                    CleanupVirtualController(controller, options);
                    return;
                }
            }

            if (!config.MergeLayersWithEquivalentMasks && selected.Any(candidate => candidate.MaskKey != "<none>"))
            {
                CleanupVirtualController(controller, options);
                return;
            }
            if (selected.Count < 2)
            {
                CleanupVirtualController(controller, options);
                return;
            }

            var groups = selected
                .GroupBy(candidate => new
                {
                    candidate.Layer.BlendingMode,
                    candidate.MaskKey,
                    StateTag = candidate.Plan.StateTag ?? string.Empty,
                })
                .Select(group => new VirtualCandidateGroup
                {
                    BlendingMode = group.First().Layer.BlendingMode,
                    MaskKey = group.First().MaskKey,
                    Mask = group.First().Mask,
                    StateTag = group.Key.StateTag,
                    Candidates = group.OrderBy(candidate => candidate.Index).ToList(),
                    OriginalCandidates = group.OrderBy(candidate => candidate.Index).ToList(),
                })
                .OrderBy(group => group.FirstIndex)
                .ToList();
            groups = groups.SelectMany(group => SplitVirtualCandidateGroup(group, layers, bindingsByLayer,
                completeBindings, options)).ToList();
            var safeGroups = new List<VirtualCandidateGroup>();
            foreach (var group in groups)
            {
                if (HasVirtualParameterOrderConflict(group, layers) ||
                    !CanPreserveVirtualLayerBehaviours(group.Candidates) || !CanMergeVirtualMotionSet(group.Candidates) ||
                    HasInterleavedConflictingGroups(safeGroups.Concat(new[] { group }).ToList()))
                {
                    Debug.LogWarning($"[AnimatorOptimizer] NDMF preserved {group.BlendingMode}/{group.MaskKey}: incompatible timing, behaviours or interleaved bindings.");
                    continue;
                }
                var hasCurveOverlap = group.Candidates.Any(left => group.Candidates.Any(right =>
                    left != right && left.Bindings.Intersect(right.Bindings).Any()));
                var canResolveOverlap = !hasCurveOverlap || CanResolveVirtualPriority(group.Candidates, options);
                if (hasCurveOverlap && !canResolveOverlap && options.StrictCurveConflicts)
                {
                    Debug.LogWarning($"[AnimatorOptimizer] NDMF skipped unsafe group {group.BlendingMode}/{group.MaskKey}.");
                    continue;
                }
                safeGroups.Add(group);
            }

            // Removing one group turns it into retained layers for the remaining groups.
            if (options.StrictCurveConflicts)
            {
                bool removed;
                do
                {
                    var merging = safeGroups.SelectMany(group => group.OriginalCandidates)
                        .Select(candidate => candidate.Layer).ToHashSet();
                    removed = safeGroups.RemoveAll(group => layers.Select((layer, index) => (layer, index)).Any(item =>
                        !merging.Contains(item.layer) && group.Candidates.Any(candidate => candidate.Bindings.Count > 0) &&
                        (!completeBindings.Contains(item.layer) ||
                         item.index > group.FirstIndex && item.index < group.OriginalCandidates.Max(candidate => candidate.Index) &&
                         group.Candidates.SelectMany(candidate => candidate.Bindings).Intersect(bindingsByLayer[item.layer]).Any()))) > 0;
                } while (removed);
            }

            foreach (var group in safeGroups)
                group.Candidates = DeduplicateVirtualCandidates(group.Candidates, group.BlendingMode);

            selected = safeGroups.SelectMany(group => group.Candidates).ToList();
            if (safeGroups.Sum(group => group.OriginalCandidates.Count) < 2)
            {
                CleanupVirtualController(controller, options);
                return;
            }

            var removedLayers = safeGroups.SelectMany(group => group.OriginalCandidates)
                .Select(candidate => candidate.Layer).ToHashSet();
            var upgraded = UpgradeParameters(controller, selected.Select(candidate => candidate.Plan), options, removedLayers);
            if (upgraded == null)
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF skipped virtual conversion because a Bool/Int parameter has an unknown writer, Animator curve, or retained condition that cannot safely become Float.");
                CleanupVirtualController(controller, options);
                return;
            }
            if (options.AllowFloatConditionApproximation)
                Debug.LogWarning("[AnimatorOptimizer] Float approximation fallback is enabled; exact representable boundaries are preferred, but unsupported edges may use interpolation.");
            foreach (var candidate in safeGroups.SelectMany(group => group.OriginalCandidates))
                foreach (var assumption in candidate.Plan.Assumptions)
                    Debug.LogWarning($"[AnimatorOptimizer] 启发式 [{candidate.Index}] {candidate.Layer.Name}: {assumption}");
            MaterializeVirtualBakedMotions(selected);
            var emptyClip = VirtualClip.Create(GeneratedAssetPrefix + "Empty");
            var weightParameter = AddWeightParameter(controller, options);
            var generatedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            var generatedLayers = new List<VirtualLayer>();
            var generatedAtIndex = new Dictionary<int, List<VirtualLayer>>();
            var groupNumber = 0;
            foreach (var group in safeGroups)
            {
                var rootTree = VirtualBlendTree.Create(GeneratedAssetPrefix + "Root_" + groupNumber);
                rootTree.BlendType = BlendTreeType.Direct;
                rootTree.BlendParameter = weightParameter;
                rootTree.UseAutomaticThresholds = false;
                rootTree.NormalizedBlendValues = false;
                var hasCurveOverlap = group.Candidates.Any(left => group.Candidates.Any(right =>
                    left != right && left.Bindings.Intersect(right.Bindings).Any()));
                var canResolveOverlap = !hasCurveOverlap || CanResolveVirtualPriority(group.Candidates, options);
                var canMergeStatic = CanMergeStaticVirtualCandidates(group.Candidates, group.BlendingMode);
                if (TryBuildVirtualLayerComposite(group.Candidates, group.BlendingMode,
                        out var timedComposite, out var compositeSettings))
                {
                    rootTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = timedComposite,
                        DirectBlendParameter = weightParameter,
                        TimeScale = compositeSettings == null ? 1f : compositeSettings.Speed,
                        CycleOffset = compositeSettings == null ? 0f : compositeSettings.CycleOffset,
                        Mirror = compositeSettings != null && compositeSettings.Mirror,
                    });
                }
                else if (canMergeStatic || (hasCurveOverlap && canResolveOverlap && options.ResolveOverridePriorityWithStaticComposite))
                {
                    rootTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = BuildVirtualPriorityTree(controller, group.Candidates, emptyClip, options,
                            group.BlendingMode == AnimatorLayerBlendingMode.Additive),
                        DirectBlendParameter = weightParameter,
                    });
                }
                else
                {
                    if (hasCurveOverlap && options.StrictCurveConflicts)
                        throw new InvalidOperationException("NDMF generation cannot preserve the analyzed property priority; refusing a Direct blend fallback.");
                    rootTree.Children = group.Candidates.Select(candidate => new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = BuildPlanMotion(controller, candidate.Plan, emptyClip),
                        DirectBlendParameter = weightParameter,
                        TimeScale = candidate.Plan.IsConstant && candidate.Plan.ConstantMotion != null
                            ? candidate.Plan.ConstantMotion.Speed
                            : 1f,
                        CycleOffset = candidate.Plan.IsConstant && candidate.Plan.ConstantMotion != null
                            ? candidate.Plan.ConstantMotion.CycleOffset
                            : 0f,
                        Mirror = candidate.Plan.IsConstant && candidate.Plan.ConstantMotion != null &&
                                 candidate.Plan.ConstantMotion.Mirror,
                    }).ToImmutableList();
                }

                var generatedLayer = controller.AddLayer(new LayerPriority(0),
                    safeGroups.Count == 1 ? generatedLayerName : generatedLayerName + "/" + group.BlendingMode + "_" + groupNumber);
                generatedLayer.DefaultWeight = 1f;
                generatedLayer.BlendingMode = group.BlendingMode;
                generatedLayer.AvatarMask = group.Mask;
                generatedLayer.IKPass = false;
                generatedLayer.SyncedLayerIndex = -1;
                generatedLayer.SyncedLayerAffectsTiming = false;
                generatedLayer.StateMachine.Behaviours = CloneVirtualLayerBehaviours(group.Candidates);

                var state = generatedLayer.StateMachine.AddState(
                    GeneratedAssetPrefix + "Root (WD On) " + groupNumber,
                    rootTree,
                    new Vector3(320f, 80f, 0f));
                state.Tag = group.StateTag ?? string.Empty;
                state.WriteDefaultValues = true;
                generatedLayer.StateMachine.DefaultState = state;
                generatedLayers.Add(generatedLayer);
                if (!generatedAtIndex.TryGetValue(group.FirstIndex, out var generatedAtFirst))
                {
                    generatedAtFirst = new List<VirtualLayer>();
                    generatedAtIndex.Add(group.FirstIndex, generatedAtFirst);
                }
                generatedAtFirst.Add(generatedLayer);
                groupNumber++;
            }

            var selectedSet = new HashSet<VirtualLayer>(safeGroups
                .SelectMany(group => group.OriginalCandidates)
                .Select(candidate => candidate.Layer));
            var outputLayers = new List<VirtualLayer>(layers.Count - selected.Count + generatedLayers.Count);
            for (var index = 0; index < layers.Count; index++)
            {
                if (generatedAtIndex.TryGetValue(index, out var generatedAtFirst))
                    outputLayers.AddRange(generatedAtFirst);
                if (!selectedSet.Contains(layers[index]))
                    outputLayers.Add(layers[index]);
            }
            var layerControlRewriteIssue = RewriteVirtualLayerIndexReferences(
                layers,
                outputLayers,
                config.PlayableLayer,
                hasKnownPlayableLayer);
            if (layerControlRewriteIssue != null)
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF skipped virtual Layer merge: " + layerControlRewriteIssue);
                controller.Layers = layers;
                CleanupVirtualController(controller, options);
                return;
            }
            RewriteVirtualSyncedLayerReferences(layers, outputLayers);
            controller.Layers = outputLayers;
            CleanupVirtualController(controller, options);
            controller.NormalizeFirstLayerWeights();
            if (config.VerboseLogging)
                Debug.Log($"[AnimatorOptimizer] NDMF optimized {selected.Count} virtual Layers; upgraded parameters: {string.Join(", ", upgraded)}.");

        }

        private static void CleanupVirtualController(
            VirtualAnimatorController controller,
            AnimatorOptimizerOptions options)
        {
            if (controller == null)
                return;

            var layers = controller.Layers.ToList();
            var referenceIssue = FindInvalidVirtualLayerReferenceIssue(layers, options);
            if (referenceIssue != null)
            {
                Debug.LogWarning("[AnimatorOptimizer] Skipped cleanup: " + referenceIssue);
                return;
            }
            var originalLayers = layers.ToList();
            var controls = EnumerateVirtualLayerControls(layers).ToList();
            var unknownControlContext = !options.HasKnownPlayableLayer && controls.Count > 0;
            var protectedIndices = layers.Where(layer => layer != null && layer.SyncedLayerIndex >= 0)
                .Select(layer => layer.SyncedLayerIndex).ToHashSet();
            foreach (var control in controls)
                if (!options.HasKnownPlayableLayer || ToVrcLayerType(control.playable) == ToVrcLayerType(options.TargetPlayableLayer))
                    protectedIndices.Add(control.layer);
            if (options.MergeSynchronizedStateMachines && !unknownControlContext)
                MergeSynchronizedVirtualLayers(controller, layers, protectedIndices, options);
            if (options.RemoveEffectivelyEmptyLayers && !unknownControlContext)
            {
                layers = layers
                    .Select((layer, index) => new { layer, index })
                    .Where(item => item.index == 0 ||
                        item.layer != null && protectedIndices.Contains(item.layer.VirtualLayerIndex) ||
                        item.layer != null &&
                        (string.Equals(item.layer.Name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                         string.Equals(item.layer.Name, "MMD Dummy Layer 2", StringComparison.Ordinal)) ||
                        item.layer != null && (IsGeneratedLayerName(item.layer.Name, options.GeneratedLayerName) ||
                            item.layer.Name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal)) ||
                        !IsEffectivelyEmptyVirtualLayer(item.layer))
                    .Select(item => item.layer)
                    .ToList();
            }

            if (options.PreserveMmdCompatibility && !unknownControlContext)
            {
                EnsureVirtualMmdCompatibilityLayers(controller, layers);
                return;
            }
            if (!originalLayers.SequenceEqual(layers))
                controller.Layers = layers;
        }

        private static bool IsEffectivelyEmptyVirtualLayer(VirtualLayer layer)
        {
            if (layer == null || layer.IKPass || layer.SyncedLayerIndex >= 0 ||
                layer.StateMachine == null)
                return false;
            var stateMachine = layer.StateMachine;
            if (stateMachine.Behaviours.Count > 0 || GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count > 0 ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Count > 0 || stateMachine.StateMachines.Count > 0)
                return false;
            return stateMachine.States.All(child => child.State != null &&
                child.State.Behaviours.Count == 0 &&
                GetEffectiveVirtualTransitions(child.State.Transitions).Count == 0 &&
                !child.State.WriteDefaultValues && !child.State.IKOnFeet &&
                IsFinite(child.State.Speed) && IsFinite(child.State.CycleOffset) &&
                (!(child.State.Mirror || child.State.MirrorParameter != null) || IsPropertyOnlyVirtualMotion(child.State.Motion)) &&
                IsEffectivelyEmptyVirtualMotion(child.State.Motion, new HashSet<VirtualMotion>()));
        }

        private static bool IsEffectivelyEmptyVirtualMotion(
            VirtualMotion motion,
            HashSet<VirtualMotion> visited)
        {
            if (motion == null)
                return false; // WD Off null Motion is not equivalent to an empty clip.
            if (motion is VirtualClip clip)
                  return IsOrdinaryVirtualClip(clip) && !clip.Settings.hasAdditiveReferencePose &&
                      clip.Settings.additiveReferencePoseClip == null && clip.AdditiveReferencePoseClip == null &&
                       !HasVirtualAnimationEvents(clip) && !clip.GetFloatCurveBindings().Any() &&
                       !clip.GetObjectCurveBindings().Any();
            if (!(motion is VirtualBlendTree tree) || !visited.Add(motion))
                return false;
            return tree.Children.All(child => IsEffectivelyEmptyVirtualMotion(child.Motion, new HashSet<VirtualMotion>(visited)));
        }

        private static bool IsPotentialProxyVirtualClip(VirtualClip clip)
        {
            return clip != null && clip.Name != null &&
                   clip.Name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase);
        }

        private static List<T> GetEffectiveVirtualTransitions<T>(IEnumerable<T> transitions)
            where T : VirtualTransitionBase
        {
            var active = (transitions ?? Enumerable.Empty<T>())
                .Where(transition => transition != null && !transition.Mute)
                .ToList();
            if (active.Any(transition => transition.Solo))
                active = active.Where(transition => transition.Solo).ToList();
            active = active.Where(transition => transition is not VirtualStateTransition stateTransition ||
                stateTransition.ExitTime.HasValue || stateTransition.Conditions.Count > 0).ToList();
            // Solo/mute selection precedes satisfiability: an impossible solo edge
            // must not accidentally reactivate non-solo edges.
            var controller = currentLayerScope?.Controller;
            if (controller != null)
                active = active.Where(transition => AnimatorOptimizerSemanticUtility.ConditionsMayBeReachable(
                    transition.Conditions, name =>
                        controller.Parameters.TryGetValue(name, out var declaration) ? declaration.type :
                        TryGetVirtualGlobalParameterType(name, currentLayerScope?.Options?.UseVrChatParameterSemantics == true, out var global) ? global.type :
                        (AnimatorControllerParameterType?)null)).ToList();
            return active;
        }

        private static bool HasVirtualAnimationEvents(VirtualClip clip)
        {
            if (!TryGetVirtualOriginalObject(clip, out AnimationClip original))
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF could not inspect VirtualClip origin; preserving the Layer to avoid dropping AnimationEvent semantics.");
                return true;
            }
            return original != null && original.events != null && original.events.Length > 0;
        }

        private static void EnsureVirtualMmdCompatibilityLayers(
            VirtualAnimatorController controller,
            List<VirtualLayer> layers)
        {
            var dummy1 = layers.Skip(1).FirstOrDefault(layer => IsReusableVirtualMmdDummyLayer(layer) &&
                string.Equals(layer.Name, "MMD Dummy Layer 1", StringComparison.Ordinal));
            var dummy2 = layers.Skip(1).FirstOrDefault(layer => IsReusableVirtualMmdDummyLayer(layer) &&
                string.Equals(layer.Name, "MMD Dummy Layer 2", StringComparison.Ordinal));
            if (dummy1 != null) layers.Remove(dummy1);
            if (dummy2 != null) layers.Remove(dummy2);
            if (layers.Count == 0)
            {
                var baseLayer = controller.AddLayer(new LayerPriority(0), "Base Layer");
                baseLayer.DefaultWeight = 1f;
                layers.Add(baseLayer);
            }
            dummy1 ??= controller.AddLayer(new LayerPriority(0), "MMD Dummy Layer 1");
            dummy2 ??= controller.AddLayer(new LayerPriority(0), "MMD Dummy Layer 2");
            dummy1.DefaultWeight = 0f;
            dummy2.DefaultWeight = 0f;
            layers.Insert(Math.Min(1, layers.Count), dummy1);
            layers.Insert(Math.Min(2, layers.Count), dummy2);
            // Only insert/move layers; virtual identities and override references
            // remain unchanged. NDMF performs physical remapping at commit.
            controller.Layers = layers;
        }

        private static bool IsReusableVirtualMmdDummyLayer(VirtualLayer layer) => layer?.StateMachine != null &&
            layer.OriginalPhysicalLayerIndex != 0 && layer.DefaultWeight == 0f && layer.AvatarMask == null &&
            layer.BlendingMode == AnimatorLayerBlendingMode.Override && layer.SyncedLayerIndex < 0 && !layer.IKPass &&
            layer.StateMachine.Behaviours.Count == 0 && layer.StateMachine.States.Count == 0 &&
            layer.StateMachine.StateMachines.Count == 0 && layer.StateMachine.EntryTransitions.Count == 0 &&
            layer.StateMachine.AnyStateTransitions.Count == 0;

        private static string FindVirtualLayerControlIssue(
            IReadOnlyList<VirtualLayer> layers,
            ISet<VirtualLayer> selected,
            AnimatorOptimizerComponent.PlayableLayer targetPlayableLayer,
            bool hasKnownPlayableLayer)
        {
            if (!hasKnownPlayableLayer && EnumerateVirtualLayerControls(layers).Any())
                return "LayerControl target Playable context is unknown; preserving virtual layers.";
            var targetType = ToVrcLayerType(targetPlayableLayer);
            foreach (var owner in layers)
            {
                foreach (var control in EnumerateVirtualLayerControls(owner))
                {
                    if (hasKnownPlayableLayer && ToVrcLayerType(control.playable) != targetType)
                        continue;
                    var target = layers.FirstOrDefault(layer =>
                        layer != null && layer.VirtualLayerIndex == control.layer);
                    if (target == null)
                        return $"LayerControl from '{owner?.Name}' references unknown virtual index {control.layer}.";
                    if (target != null && selected.Contains(target))
                    {
                        return $"VRCAnimatorLayerControl from '{owner?.Name}' targets selected Layer '{target.Name}' (index {control.layer}).";
                    }
                }
            }
            return null;
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateVirtualLayerControls(
            IEnumerable<VirtualLayer> layers)
        {
            foreach (var layer in layers ?? Enumerable.Empty<VirtualLayer>())
                foreach (var control in EnumerateVirtualLayerControls(layer)) yield return control;
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateVirtualLayerControls(VirtualLayer layer)
        {
            foreach (var control in EnumerateVirtualLayerControls(layer?.StateMachine)) yield return control;
            if (layer != null)
                foreach (var control in layer.SyncedLayerBehaviourOverrides.Values.SelectMany(behaviours => behaviours)
                    .OfType<VRC_AnimatorLayerControl>()) yield return control;
        }

        private static string FindInvalidVirtualLayerReferenceIssue(IReadOnlyList<VirtualLayer> layers, AnimatorOptimizerOptions options)
        {
            if (layers.Any(layer => layer == null) || layers.Select(layer => layer.VirtualLayerIndex).Distinct().Count() != layers.Count)
                return "Missing layer or duplicate virtual identity.";
            if (layers.Any(layer => layer.SyncedLayerMotionOverrides == null || layer.SyncedLayerBehaviourOverrides == null ||
                layer.SyncedLayerBehaviourOverrides.Values.Any(behaviours => behaviours == null)))
                return "Synced Layer override collections are missing.";
            var byId = layers.ToDictionary(layer => layer.VirtualLayerIndex);
            foreach (var layer in layers)
            {
                var chain = new HashSet<int> { layer.VirtualLayerIndex };
                var index = layer.SyncedLayerIndex;
                var sourceStates = new HashSet<VirtualState>();
                var hasOverrides = layer.SyncedLayerMotionOverrides.Count > 0 || layer.SyncedLayerBehaviourOverrides.Count > 0;
                while (index >= 0)
                {
                    if (!byId.TryGetValue(index, out var target) || !chain.Add(index))
                        return $"Synced Layer '{layer.Name}' has a missing or cyclic target ({index}).";
                    if (hasOverrides && target.StateMachine != null)
                        sourceStates.UnionWith(target.StateMachine.AllStates());
                    index = target.SyncedLayerIndex;
                }
                if (index != -1) return $"Synced Layer '{layer.Name}' has an invalid negative target ({index}).";
                if (layer.SyncedLayerIndex >= 0 && layer.SyncedLayerMotionOverrides.Keys
                    .Concat(layer.SyncedLayerBehaviourOverrides.Keys).Any(state => state == null || !sourceStates.Contains(state)))
                    return $"Synced Layer '{layer.Name}' contains an override for a state outside its source graph.";
            }
            if (options.HasKnownPlayableLayer)
                foreach (var control in EnumerateVirtualLayerControls(layers))
                    if (ToVrcLayerType(control.playable) == ToVrcLayerType(options.TargetPlayableLayer) && !byId.ContainsKey(control.layer))
                        return $"LayerControl references a missing virtual target ({control.layer}).";
            return null;
        }

        private static string RewriteVirtualLayerIndexReferences(
            IReadOnlyList<VirtualLayer> oldLayers,
            IReadOnlyList<VirtualLayer> newLayers,
            AnimatorOptimizerComponent.PlayableLayer targetPlayableLayer,
            bool hasKnownPlayableLayer)
        {
            if (oldLayers == null || newLayers == null)
                return null;

            // NDMF commits virtual indices to physical indices; never rewrite them here.
            var retained = new HashSet<VirtualLayer>(newLayers);
            var targetType = ToVrcLayerType(targetPlayableLayer);
            foreach (var owner in newLayers)
            {
                foreach (var control in EnumerateVirtualLayerControls(owner))
                {
                    if (hasKnownPlayableLayer && ToVrcLayerType(control.playable) != targetType)
                        continue;
                    var oldTarget = oldLayers.FirstOrDefault(layer =>
                        layer != null && layer.VirtualLayerIndex == control.layer);
                    if (oldTarget == null || !retained.Contains(oldTarget))
                    {
                        return $"VRCAnimatorLayerControl from '{owner?.Name}' has invalid or removed target index {control.layer}.";
                    }
                }
            }
            return null;
        }

        private static void RewriteVirtualSyncedLayerReferences(
            IReadOnlyList<VirtualLayer> oldLayers,
            IReadOnlyList<VirtualLayer> newLayers)
        {
            if (oldLayers == null || newLayers == null)
                return;
            var retained = new HashSet<VirtualLayer>(newLayers);
            foreach (var layer in newLayers)
            {
                if (layer == null || layer.SyncedLayerIndex < 0)
                    continue;
                var target = oldLayers.FirstOrDefault(candidate =>
                    candidate != null && candidate.VirtualLayerIndex == layer.SyncedLayerIndex);
                if (target == null || !retained.Contains(target))
                    throw new InvalidOperationException("Synced Layer target was removed; refusing to disable synchronization implicitly.");
            }
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateVirtualLayerControls(
            VirtualStateMachine stateMachine)
        {
            if (stateMachine == null)
                yield break;
            foreach (var behaviour in stateMachine.Behaviours)
            {
                if (behaviour is VRC_AnimatorLayerControl control)
                    yield return control;
            }
            foreach (var child in stateMachine.States)
            {
                if (child.State == null)
                    continue;
                foreach (var behaviour in child.State.Behaviours)
                {
                    if (behaviour is VRC_AnimatorLayerControl control)
                        yield return control;
                }
            }
            foreach (var child in stateMachine.StateMachines)
            {
                foreach (var control in EnumerateVirtualLayerControls(child.StateMachine))
                    yield return control;
            }
        }

        private static VRCAvatarDescriptor.AnimLayerType ToVrcLayerType(
            VRC_AnimatorLayerControl.BlendableLayer playable)
        {
            switch (playable)
            {
                case VRC_AnimatorLayerControl.BlendableLayer.Action:
                    return VRCAvatarDescriptor.AnimLayerType.Action;
                case VRC_AnimatorLayerControl.BlendableLayer.Additive:
                    return VRCAvatarDescriptor.AnimLayerType.Additive;
                case VRC_AnimatorLayerControl.BlendableLayer.Gesture:
                    return VRCAvatarDescriptor.AnimLayerType.Gesture;
                case VRC_AnimatorLayerControl.BlendableLayer.FX:
                    return VRCAvatarDescriptor.AnimLayerType.FX;
                default:
                    return VRCAvatarDescriptor.AnimLayerType.FX;
            }
        }

        private static string GetMaskKey(VirtualAvatarMask mask)
        {
            if (mask == null)
                return "<none>";
            try
            {
                // NDMF commits this private clone, not OriginalObject (which may
                // have changed since cloning). Unknown versions fall back to identity.
                var concreteMask = VirtualMaskObjectField?.GetValue(mask) as AvatarMask;
                if (concreteMask != null && mask.Elements != null && mask.Elements.All(pair => IsFinite(pair.Value)))
                {
                    var humanoidParts = Enumerable.Range(0, (int)AvatarMaskBodyPart.LastBodyPart)
                        .Select(part => "human:" + part + "=" + concreteMask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)part));
                    return string.Join(";", humanoidParts.Concat(mask.Elements
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => "transform:" + pair.Key.Length + ":" + pair.Key + "=" +
                            pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                }
            }
            catch
            {
                // Missing metadata is not proof that all humanoid mask bits are false.
            }
            return "unknown-mask:" + AnimatorOptimizerSemanticUtility.ReferenceKey(mask);
        }

        private static T GetVirtualOriginalObject<T>(VirtualNode node)
            where T : UnityEngine.Object
        {
            return TryGetVirtualOriginalObject(node, out T value) ? value : null;
        }

        private static bool TryGetVirtualOriginalObject<T>(
            VirtualNode node,
            out T original)
            where T : UnityEngine.Object => TryReadVirtualOriginalObject(node, VirtualOriginalObjectProperty, out original);

        private static bool TryReadVirtualOriginalObject<T>(
            VirtualNode node,
            PropertyInfo originProperty,
            out T original)
            where T : UnityEngine.Object
        {
            original = null;
            if (node == null)
                return true;
            if (originProperty == null || !typeof(UnityEngine.Object).IsAssignableFrom(originProperty.PropertyType))
                return false;
            try
            {
                var value = originProperty.GetValue(node);
                if (value != null && !(value is T))
                    return false;
                original = value as T;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGeneratedLayerName(string name, string configuredName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (string.Equals(name, GeneratedLayerName, StringComparison.Ordinal) ||
                name.StartsWith(GeneratedLayerName + "/", StringComparison.Ordinal))
                return true;
            return !string.IsNullOrWhiteSpace(configuredName) &&
                   (string.Equals(name, configuredName, StringComparison.Ordinal) ||
                    name.StartsWith(configuredName + "/", StringComparison.Ordinal));
        }

        private static bool HasInterleavedConflictingGroups(IReadOnlyList<VirtualCandidateGroup> groups)
        {
            for (var leftIndex = 0; leftIndex < groups.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < groups.Count; rightIndex++)
                {
                    var left = groups[leftIndex];
                    var right = groups[rightIndex];
                    if (!left.Candidates.SelectMany(candidate => candidate.Bindings)
                        .Intersect(right.Candidates.SelectMany(candidate => candidate.Bindings)).Any())
                        continue;
                    if (GroupsInterleave(left.Candidates.Select(candidate => candidate.Index),
                            right.Candidates.Select(candidate => candidate.Index)))
                        return true;
                }
            }
            return false;
        }

        private static bool GroupsInterleave(IEnumerable<int> leftIndices, IEnumerable<int> rightIndices)
        {
            var left = leftIndices.OrderBy(index => index).ToList();
            var right = rightIndices.OrderBy(index => index).ToList();
            return HasIndexBetween(left, right) || HasIndexBetween(right, left);
        }

        private static bool HasIndexBetween(IReadOnlyList<int> outer, IReadOnlyList<int> inner)
        {
            if (outer.Count < 2)
                return false;
            for (var index = 0; index < inner.Count; index++)
            {
                if (inner[index] > outer[0] && inner[index] < outer[outer.Count - 1])
                    return true;
            }
            return false;
        }

        private static bool CanResolveVirtualPriority(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorOptimizerOptions options)
        {
            if (!options.ResolveOverridePriorityWithStaticComposite)
                return false;
            var lowerBindings = new HashSet<BindingKey>();
            foreach (var candidate in candidates.OrderBy(item => item.Index))
            {
                var overlap = lowerBindings.Intersect(candidate.Bindings).ToList();
                foreach (var motion in EnumerateVirtualPlanMotions(candidate.Plan))
                {
                    var guaranteed = new HashSet<BindingKey>();
                    if (motion != null && motion.Mirror ||
                        !TryCollectVirtualGuaranteedBindings(motion?.Motion, guaranteed) ||
                        overlap.Any(binding => !guaranteed.Contains(binding)))
                        return false;
                }
                lowerBindings.UnionWith(candidate.Bindings);
            }
            if (!TryBuildVirtualPriorityDimensions(candidates, options, out var dimensions, out _))
                return false;
            if (candidates.Any(candidate => candidate.Layer.BlendingMode == AnimatorLayerBlendingMode.Additive))
            {
                if (candidates.SelectMany(candidate => candidate.Bindings).Any(binding => binding.IsPPtr))
                    return false;
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr || !binding.IsSafeAdditiveFloatBinding))
                            return false;
                    }
                }
            }
            if (!ValidateVirtualPriorityCombinations(candidates, dimensions, 0,
                    new Dictionary<string, float>(StringComparer.Ordinal)))
                return false;
            foreach (var candidate in candidates)
            {
                foreach (var motion in EnumerateVirtualPlanMotions(candidate.Plan))
                {
                    if (motion?.Bake != null)
                    {
                        if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                        continue;
                    }
                    if (motion?.Motion != null &&
                        (!(motion.Motion is VirtualClip clip) || clip.IsMarkerClip ||
                         IsPotentialProxyVirtualClip(clip) || !IsStaticVirtualClip(clip)))
                        return false;
                }
            }
            var references = candidates.SelectMany(candidate => EnumerateVirtualPlanMotions(candidate.Plan)).ToList();
            if (references.Any(motion => motion?.Bake != null))
                return AreSampledVirtualMotionsCompatible(references);
            var staticClips = references
                .Select(motion => motion?.Motion)
                .OfType<VirtualClip>()
                .ToList();
            if (staticClips.Select(clip => clip.Legacy).Distinct().Count() > 1)
                return false;
            if (staticClips.Skip(1).Any(clip => !AreCompatibleVirtualClipMetadata(staticClips[0], clip)))
                return false;
            return true;
        }

        private static bool CanMergeStaticVirtualCandidates(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorLayerBlendingMode blendingMode)
        {
            if (candidates == null || candidates.Count < 2 ||
                candidates.Any(candidate => candidate.Plan == null || !candidate.Plan.IsConstant ||
                    candidate.Plan.ConstantMotion == null || candidate.Plan.ConstantMotion.Mirror))
                return false;

            var clips = candidates.Select(candidate => candidate.Plan.ConstantMotion?.Motion).ToList();
            if (clips.Any(motion => !(motion is VirtualClip)) ||
                clips.Cast<VirtualClip>().Any(clip => clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip) ||
                    !IsStaticVirtualClip(clip)))
                return false;
            if (clips.Cast<VirtualClip>().Select(clip => clip.Legacy).Distinct().Count() > 1)
                return false;
            if (clips.Cast<VirtualClip>().Skip(1).Any(clip => !AreCompatibleVirtualClipMetadata(
                    clips.Cast<VirtualClip>().First(), clip)))
                return false;

            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                if (candidates.SelectMany(candidate => candidate.Bindings).Any(binding => binding.IsPPtr) ||
                    !AreStaticVirtualAdditiveValuesFinite(candidates.OrderBy(candidate => candidate.Index)
                        .Select(candidate => candidate.Plan.ConstantMotion)))
                    return false;
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr || !binding.IsSafeAdditiveFloatBinding))
                            return false;
                    }
                }
            }
            return true;
        }

        private static bool CanMergeVirtualMotionSet(IReadOnlyList<VirtualCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
                return true;
            if (candidates.Any(candidate => candidate.Plan == null ||
                candidate.Layer.BlendingMode == AnimatorLayerBlendingMode.Additive && candidate.Bindings.Any(binding => binding.IsPPtr)))
                return false;
            if (candidates[0].Layer.BlendingMode == AnimatorLayerBlendingMode.Additive &&
                candidates.All(candidate => candidate.Plan.IsConstant &&
                    (candidate.Plan.ConstantMotion?.Bake != null
                        ? candidate.Plan.ConstantMotion.Bake.Parameter == null && candidate.Plan.ConstantMotion.Bake.Frames.Length == 1
                        : candidate.Plan.ConstantMotion?.Motion is VirtualClip clip && IsStaticVirtualClip(clip))) &&
                !AreStaticVirtualAdditiveValuesFinite(candidates.OrderBy(candidate => candidate.Index)
                    .Select(candidate => candidate.Plan.ConstantMotion)))
                return false;
            if (!HasVirtualBindingOverlap(candidates) && candidates.All(CanRetainVirtualPlanAsDirectChild) &&
                candidates.All(candidate => EnumerateVirtualPlanMotions(candidate.Plan).All(motion =>
                    IsTimeIndependentVirtualRef(motion))))
                return true;
            if (candidates.All(candidate => EnumerateVirtualPlanMotions(candidate.Plan).All(motion =>
                motion != null && motion.Motion != null && IsTimeIndependentVirtualRef(motion))))
                return true;
            if (candidates.All(candidate => candidate.Plan != null && candidate.Plan.IsConstant))
                return CanMergeStaticVirtualCandidates(candidates, candidates[0].Layer.BlendingMode) ||
                    TryBuildVirtualLayerComposite(candidates, candidates[0].Layer.BlendingMode, out _, out _, validateOnly: true);
            return false;
        }

        private static bool HasVirtualBindingOverlap(IReadOnlyList<VirtualCandidate> candidates)
        {
            for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                {
                    if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings).Any())
                        return true;
                }
            }
            return false;
        }

        private static bool CanRetainVirtualPlanAsDirectChild(VirtualCandidate candidate)
        {
            return candidate?.Plan != null && EnumerateVirtualPlanMotions(candidate.Plan).All(motion =>
                motion?.Motion == null ||
                !ContainsVirtualAnimationEvents(motion.Motion, new HashSet<VirtualMotion>()) &&
                !ContainsVirtualAdditiveReferencePose(motion.Motion, new HashSet<VirtualMotion>()));
        }

        private static bool ContainsVirtualAdditiveReferencePose(
            VirtualMotion motion,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return false;
            if (motion is VirtualClip clip)
            {
                var settings = clip.Settings;
                return settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                       clip.AdditiveReferencePoseClip != null;
            }
            return motion is VirtualBlendTree tree && tree.Children.Any(child =>
                ContainsVirtualAdditiveReferencePose(child.Motion, visited));
        }

        private static List<VirtualCandidate> DeduplicateVirtualCandidates(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorLayerBlendingMode blendingMode)
        {
            if (candidates == null || candidates.Count < 2 ||
                blendingMode == AnimatorLayerBlendingMode.Additive)
                return (candidates ?? Array.Empty<VirtualCandidate>()).ToList();

            var unique = new List<VirtualCandidate>();
            var allKeys = candidates.Select(candidate => BuildVirtualPlanEquivalenceKey(candidate.Plan)).ToList();
            if (allKeys.All(key => key != null && key == allKeys[0]))
                return new List<VirtualCandidate> { candidates.OrderBy(candidate => candidate.Index).First() };
            if (HasVirtualBindingOverlap(candidates))
                return candidates.ToList();
            var keys = new Dictionary<VirtualCandidate, string>();
            foreach (var candidate in candidates.OrderBy(item => item.Index))
            {
                var key = BuildVirtualPlanEquivalenceKey(candidate.Plan);
                if (key == null || unique.Any(existing =>
                        keys.TryGetValue(existing, out var existingKey) &&
                        string.Equals(existingKey, key, StringComparison.Ordinal)))
                {
                    if (key == null)
                        unique.Add(candidate);
                    continue;
                }

                keys[candidate] = key;
                unique.Add(candidate);
            }
            return unique;
        }

        private static string BuildVirtualPlanEquivalenceKey(VirtualPlan plan)
        {
            if (plan == null)
                return null;

            if (plan.IsConstant)
            {
                var constantKey = BuildVirtualMotionRefEquivalenceKey(plan.ConstantMotion);
                return constantKey == null ? null : "constant:" + constantKey;
            }

            if (plan.BooleanParameters != null && plan.TruthTable != null)
            {
                var tableKeys = plan.TruthTable.OrderBy(pair => pair.Key)
                    .Select(pair => BuildVirtualMotionRefEquivalenceKey(pair.Value))
                    .ToList();
                if (tableKeys.Any(key => key == null))
                    return null;
                return "bool:" + string.Concat(plan.BooleanParameters.Select(AnimatorOptimizerSynchronizedGraph.Atom)) + ":" +
                       string.Join("|", plan.TruthTable.OrderBy(pair => pair.Key)
                           .Zip(tableKeys, (pair, key) => pair.Key + "=" + AnimatorOptimizerSynchronizedGraph.Atom(key)));
            }

            if (!string.IsNullOrWhiteSpace(plan.SelectorParameter) && plan.SelectorSamples != null)
            {
                var sampleKeys = plan.SelectorSamples
                    .Select(sample => BuildVirtualMotionRefEquivalenceKey(sample.Motion))
                    .ToList();
                if (sampleKeys.Any(key => key == null))
                    return null;
                  return "selector:" + AnimatorOptimizerSynchronizedGraph.Atom(plan.SelectorParameter) + ":" +
                      plan.SelectorParameterType + ":" + plan.SelectorIsApproximate + ":" +
                       string.Join("|", plan.SelectorSamples.Zip(sampleKeys, (sample, key) =>
                           sample.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "=" +
                           AnimatorOptimizerSynchronizedGraph.Atom(key)));
            }

            if (plan.DecisionDimensions != null && plan.DecisionTable != null)
            {
                var decisionKeys = plan.DecisionTable.OrderBy(pair => pair.Key)
                    .Select(pair => BuildVirtualMotionRefEquivalenceKey(pair.Value))
                    .ToList();
                if (decisionKeys.Any(key => key == null))
                    return null;
                return "finite:" + string.Concat(plan.DecisionDimensions.Select(dimension =>
                           AnimatorOptimizerSynchronizedGraph.Atom(AnimatorOptimizerSynchronizedGraph.Atom(dimension.Parameter) +
                               dimension.Type + ":" + dimension.IsApproximate + ":" + string.Join(",", dimension.Values.Select(value =>
                                   value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))))) + ":" +
                       string.Join("|", plan.DecisionTable.OrderBy(pair => pair.Key)
                           .Zip(decisionKeys, (pair, key) => pair.Key + "=" + AnimatorOptimizerSynchronizedGraph.Atom(key)));
            }

            return null;
        }

        private static string BuildVirtualMotionRefEquivalenceKey(VirtualMotionRef motion)
        {
            if (motion == null)
                return "null";
            if (motion.Bake != null) return motion.Bake.Key;
            var motionKey = BuildVirtualMotionEquivalenceKey(motion.Motion, new HashSet<VirtualMotion>());
            return motionKey == null
                ? null
                : AnimatorOptimizerSynchronizedGraph.Atom(motionKey) + ":" + motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                  ":" + motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                  ":" + motion.Mirror;
        }

        private static string BuildVirtualMotionEquivalenceKey(
            VirtualMotion motion,
            ISet<VirtualMotion> active)
        {
            if (motion == null)
                return "null";
            if (!active.Add(motion))
                return null;

            if (motion is VirtualClip clip)
            {
                if (!IsOrdinaryVirtualClip(clip) || HasVirtualAnimationEvents(clip))
                    return null;
                var floatCurves = clip.GetFloatCurveBindings()
                    .OrderBy(binding => BuildVirtualBindingKey(binding), StringComparer.Ordinal)
                    .Select(binding => "f:" + BuildVirtualBindingKey(binding) + ":" +
                        BuildVirtualCurveKey(clip.GetFloatCurve(binding)));
                var objectCurves = clip.GetObjectCurveBindings()
                    .OrderBy(binding => BuildVirtualBindingKey(binding), StringComparer.Ordinal)
                    .Select(binding => "o:" + BuildVirtualBindingKey(binding) + ":" +
                        BuildVirtualObjectCurveKey(clip.GetObjectCurve(binding)));
                var settings = clip.Settings;
                if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                    clip.AdditiveReferencePoseClip != null)
                {
                    active.Remove(motion);
                    return null;
                }
                var bounds = clip.LocalBounds;
                var key = "clip:" + clip.Legacy + ":" +
                       clip.FrameRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       clip.WrapMode + ":hq:" + clip.UseHighQualityCurves + ":" +
                       settings.loopTime + ":" + settings.loopBlend + ":" +
                       settings.loopBlendOrientation + ":" +
                       settings.loopBlendPositionY + ":" +
                       settings.loopBlendPositionXZ + ":" + settings.keepOriginalOrientation + ":" +
                       settings.keepOriginalPositionY + ":" + settings.keepOriginalPositionXZ + ":" +
                       settings.heightFromFeet + ":" +
                       settings.startTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       settings.stopTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       settings.orientationOffsetY.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       settings.level.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       settings.cycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":bounds:" +
                       bounds.center.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                       bounds.center.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                       bounds.center.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                       bounds.extents.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                       bounds.extents.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                       bounds.extents.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       string.Concat(floatCurves.Concat(objectCurves).Select(AnimatorOptimizerSynchronizedGraph.Atom));
                active.Remove(motion);
                return key;
            }

            if (motion is VirtualBlendTree tree)
            {
                var children = new List<string>();
                foreach (var child in tree.Children)
                {
                    var childKey = BuildVirtualMotionEquivalenceKey(child.Motion, active);
                    if (childKey == null)
                        return null;
                    children.Add(string.Concat(new[]
                    {
                        childKey,
                        child.Threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        child.Position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        child.Position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        child.TimeScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        child.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        child.Mirror.ToString(),
                        child.DirectBlendParameter ?? string.Empty,
                    }.Select(AnimatorOptimizerSynchronizedGraph.Atom)));
                }
                active.Remove(motion);
                  return "tree:" + tree.BlendType + ":" + AnimatorOptimizerSynchronizedGraph.Atom(tree.BlendParameter) +
                      AnimatorOptimizerSynchronizedGraph.Atom(tree.BlendParameterY) + ":" +
                       tree.MinThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       tree.MaxThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                       tree.UseAutomaticThresholds + ":" + tree.NormalizedBlendValues + ":" +
                       string.Concat(children.Select(AnimatorOptimizerSynchronizedGraph.Atom));
            }

            active.Remove(motion);
            return null;
        }

        private static string BuildVirtualCurveKey(AnimationCurve curve)
        {
            if (curve == null)
                return "null";
            return curve.preWrapMode + ":" + curve.postWrapMode + ":" + string.Join(";", curve.keys.Select(key =>
                string.Join(",", new[]
                {
                    key.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.inTangent.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.outTangent.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.inWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.outWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    key.weightedMode.ToString(),
                })));
        }

        private static string BuildVirtualObjectCurveKey(ObjectReferenceKeyframe[] curve)
        {
            if (curve == null)
                return "null";
            return string.Join(";", curve.Select(key => key.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                (key.value == null ? "0" : key.value.GetInstanceID().ToString())));
        }

        private static bool TryBuildVirtualLayerComposite(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorLayerBlendingMode blendingMode,
            out VirtualClip composite,
            out VirtualMotionRef compositeSettings,
            bool validateOnly = false)
        {
            composite = null;
            compositeSettings = null;
            if (candidates == null || candidates.Count < 2 ||
                candidates.Any(candidate => candidate.Plan == null || !candidate.Plan.IsConstant ||
                    candidate.Plan.ConstantMotion?.Motion is not VirtualClip))
                return false;

            var clips = candidates
                .OrderBy(candidate => candidate.Index)
                .Select(candidate => (VirtualClip)candidate.Plan.ConstantMotion.Motion)
                .ToList();
            if (candidates.Any(candidate => candidate.Plan.ConstantMotion.Mirror))
                return false;
                if (clips.Any(clip => !IsOrdinaryVirtualClip(clip) || HasVirtualAnimationEvents(clip) ||
                    clip.Settings.hasAdditiveReferencePose ||
                    clip.Settings.additiveReferencePoseClip != null || clip.AdditiveReferencePoseClip != null))
                return false;
            var first = clips[0];
            var firstSettings = first.Settings;
            var firstObservedLength = GetVirtualObservedLength(first);
            var firstMotionSettings = candidates
                .OrderBy(candidate => candidate.Index)
                .First()
                .Plan
                .ConstantMotion;
            foreach (var clip in clips.Skip(1))
            {
                if (clip.FrameRate != first.FrameRate || clip.WrapMode != first.WrapMode ||
                    clip.Legacy != first.Legacy ||
                    clip.UseHighQualityCurves != first.UseHighQualityCurves ||
                    !AreVirtualObservedLengthsCompatible(firstObservedLength, GetVirtualObservedLength(clip)) ||
                    !AreCompatibleVirtualClipSettings(firstSettings, clip.Settings))
                    return false;
            }
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Index).Skip(1))
            {
                var motionSettings = candidate.Plan.ConstantMotion;
                if (motionSettings == null || firstMotionSettings == null ||
                    motionSettings.Speed != firstMotionSettings.Speed ||
                    motionSettings.CycleOffset != firstMotionSettings.CycleOffset ||
                    motionSettings.Mirror != firstMotionSettings.Mirror)
                    return false;
            }

            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                if (candidates.SelectMany(candidate => candidate.Bindings).Any(binding => binding.IsPPtr))
                    return false;
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr || !binding.IsSafeAdditiveFloatBinding))
                            return false;
                    }
                }
            }

            var floatCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            foreach (var clip in clips)
            {
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var key = new BindingKey(binding);
                    var curve = clip.GetFloatCurve(binding);
                    if (floatCurves.TryGetValue(key, out var existing))
                    {
                        if (blendingMode == AnimatorLayerBlendingMode.Additive)
                        {
                            if (!TryAddVirtualCompatibleCurves(existing.Curve, curve, out var added))
                                return false;
                            floatCurves[key] = (binding, added);
                        }
                        else
                        {
                            floatCurves[key] = (binding, CloneVirtualCurve(curve));
                        }
                    }
                    else
                    {
                        floatCurves[key] = (binding, CloneVirtualCurve(curve));
                    }
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    var key = new BindingKey(binding);
                    if (blendingMode == AnimatorLayerBlendingMode.Additive && objectCurves.ContainsKey(key))
                        return false;
                    var curve = clip.GetObjectCurve(binding);
                    objectCurves[key] = (binding, curve == null ? Array.Empty<ObjectReferenceKeyframe>() : curve.ToArray());
                }
            }

            if (validateOnly) return true;
            composite = VirtualClip.Create(GeneratedAssetPrefix + "TimedComposite_" + virtualPriorityCounter++);
            composite.Legacy = first.Legacy;
            composite.UseHighQualityCurves = first.UseHighQualityCurves;
            composite.FrameRate = first.FrameRate;
            composite.WrapMode = first.WrapMode;
            composite.LocalBounds = GetCombinedVirtualLocalBounds(clips);
            composite.Settings = firstSettings;
            foreach (var curve in floatCurves.Values)
                composite.SetFloatCurve(curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                composite.SetObjectCurve(curve.Binding, curve.Curve);
            compositeSettings = candidates[0].Plan.ConstantMotion;
            return true;
        }

        private static bool AreCompatibleVirtualClipMetadata(VirtualClip left, VirtualClip right)
        {
            if (left == null || right == null || left.Legacy != right.Legacy ||
                left.FrameRate != right.FrameRate || left.WrapMode != right.WrapMode ||
                left.UseHighQualityCurves != right.UseHighQualityCurves)
                return false;
            return AreCompatibleVirtualClipSettings(left.Settings, right.Settings);
        }

        private static Bounds GetCombinedVirtualLocalBounds(IEnumerable<VirtualClip> clips)
        {
            var sourceClips = clips?.Where(clip => clip != null).ToList() ?? new List<VirtualClip>();
            if (sourceClips.Count == 0)
                return default(Bounds);
            var bounds = sourceClips[0].LocalBounds;
            for (var index = 1; index < sourceClips.Count; index++)
                bounds.Encapsulate(sourceClips[index].LocalBounds);
            return bounds;
        }

        private static float? GetVirtualObservedLength(VirtualClip clip)
        {
            if (clip == null)
                return null;
            var times = clip.GetFloatCurveBindings()
                .SelectMany(binding => clip.GetFloatCurve(binding)?.keys ?? Array.Empty<Keyframe>())
                .Select(key => key.time)
                .Concat(clip.GetObjectCurveBindings()
                    .SelectMany(binding => clip.GetObjectCurve(binding) ?? Array.Empty<ObjectReferenceKeyframe>())
                    .Select(key => key.time))
                .ToList();
            return times.Count == 0 ? (float?)null : times.Max();
        }

        private static bool AreVirtualObservedLengthsCompatible(float? left, float? right)
        {
            if (left.HasValue != right.HasValue)
                return false;
            return !left.HasValue || left.Value == right.Value;
        }

        private static bool AreCompatibleVirtualClipSettings(
            AnimationClipSettings left,
            AnimationClipSettings right)
        {
            return left.loopTime == right.loopTime &&
                   left.loopBlend == right.loopBlend &&
                   left.loopBlendOrientation == right.loopBlendOrientation &&
                   left.loopBlendPositionY == right.loopBlendPositionY &&
                   left.loopBlendPositionXZ == right.loopBlendPositionXZ &&
                   left.keepOriginalOrientation == right.keepOriginalOrientation &&
                   left.keepOriginalPositionY == right.keepOriginalPositionY &&
                   left.keepOriginalPositionXZ == right.keepOriginalPositionXZ &&
                   left.heightFromFeet == right.heightFromFeet &&
                   left.hasAdditiveReferencePose == right.hasAdditiveReferencePose &&
                   left.additiveReferencePoseClip == right.additiveReferencePoseClip &&
                   left.additiveReferencePoseTime == right.additiveReferencePoseTime &&
                   left.startTime == right.startTime &&
                   left.stopTime == right.stopTime &&
                   left.orientationOffsetY == right.orientationOffsetY &&
                   left.level == right.level &&
                   left.cycleOffset == right.cycleOffset;
        }

        private static bool TryAddVirtualCompatibleCurves(
            AnimationCurve left,
            AnimationCurve right,
            out AnimationCurve result)
        {
            result = null;
            if (left == null || right == null || left.preWrapMode != right.preWrapMode ||
                left.postWrapMode != right.postWrapMode || left.keys.Length != right.keys.Length)
                return false;
            var leftKeys = left.keys;
            var rightKeys = right.keys;
            var keys = new Keyframe[leftKeys.Length];
            for (var index = 0; index < keys.Length; index++)
            {
                if (leftKeys[index].time != rightKeys[index].time ||
                    leftKeys[index].weightedMode != rightKeys[index].weightedMode ||
                    leftKeys[index].inWeight != rightKeys[index].inWeight ||
                    leftKeys[index].outWeight != rightKeys[index].outWeight ||
                    float.IsNaN(leftKeys[index].time) || float.IsNaN(rightKeys[index].time) ||
                    float.IsInfinity(leftKeys[index].time) || float.IsInfinity(rightKeys[index].time) ||
                    float.IsNaN(leftKeys[index].value) || float.IsNaN(rightKeys[index].value) ||
                    float.IsInfinity(leftKeys[index].value) || float.IsInfinity(rightKeys[index].value) ||
                    float.IsNaN(leftKeys[index].inWeight) || float.IsNaN(rightKeys[index].inWeight) ||
                    float.IsNaN(leftKeys[index].outWeight) || float.IsNaN(rightKeys[index].outWeight) ||
                    float.IsInfinity(leftKeys[index].inWeight) || float.IsInfinity(rightKeys[index].inWeight) ||
                    float.IsInfinity(leftKeys[index].outWeight) || float.IsInfinity(rightKeys[index].outWeight) ||
                    float.IsNaN(leftKeys[index].inTangent) || float.IsNaN(rightKeys[index].inTangent) ||
                    float.IsNaN(leftKeys[index].outTangent) || float.IsNaN(rightKeys[index].outTangent) ||
                    float.IsInfinity(leftKeys[index].inTangent) || float.IsInfinity(rightKeys[index].inTangent) ||
                    float.IsInfinity(leftKeys[index].outTangent) || float.IsInfinity(rightKeys[index].outTangent))
                    return false;
                var key = leftKeys[index];
                key.value += rightKeys[index].value;
                key.inTangent += rightKeys[index].inTangent;
                key.outTangent += rightKeys[index].outTangent;
                if (!IsFinite(key.value) || !IsFinite(key.inTangent) || !IsFinite(key.outTangent))
                    return false;
                keys[index] = key;
            }
            result = new AnimationCurve(keys)
            {
                preWrapMode = left.preWrapMode,
                postWrapMode = left.postWrapMode,
            };
            return true;
        }

        private static bool ValidateVirtualPriorityCombinations(
            IReadOnlyList<VirtualCandidate> candidates,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values)
        {
            if (depth >= dimensions.Count)
            {
                var motions = candidates.OrderBy(candidate => candidate.Index)
                    .Select(candidate => EvaluateVirtualPlan(candidate.Plan, values)).ToList();
                return motions.All(motion => motion != null) &&
                    (candidates.Count == 0 || candidates[0].Layer.BlendingMode != AnimatorLayerBlendingMode.Additive ||
                     AreStaticVirtualAdditiveValuesFinite(motions));
            }

            var dimension = dimensions[depth];
            foreach (var value in dimension.Values)
            {
                values[dimension.Parameter] = value;
                if (!ValidateVirtualPriorityCombinations(candidates, dimensions, depth + 1, values))
                    return false;
            }
            values.Remove(dimension.Parameter);
            return true;
        }

        private static bool AreStaticVirtualAdditiveValuesFinite(IEnumerable<VirtualMotionRef> motions)
        {
            var sums = new Dictionary<BindingKey, float>();
            foreach (var motion in motions)
            {
                if (motion?.Motion == null) continue;
                if (motion.Bake != null)
                {
                    if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                    for (var index = 0; index < motion.Bake.FloatBindings.Length; index++)
                    {
                        var key = new BindingKey(motion.Bake.FloatBindings[index]);
                        sums.TryGetValue(key, out var previous);
                        var sum = previous + motion.Bake.Frames[0].Floats[index];
                        if (!IsFinite(sum)) return false;
                        sums[key] = sum;
                    }
                    continue;
                }
                if (motion.Motion is not VirtualClip clip || !IsStaticVirtualClip(clip)) return false;
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var curve = clip.GetFloatCurve(binding);
                    if (curve == null || curve.length == 0) continue;
                    var key = new BindingKey(binding);
                    sums.TryGetValue(key, out var previous);
                    var sum = previous + curve.keys[0].value;
                    if (!IsFinite(sum)) return false;
                    sums[key] = sum;
                }
            }
            return true;
        }

        private static IEnumerable<VirtualMotionRef> EnumerateVirtualPlanMotions(VirtualPlan plan)
        {
            if (plan == null)
                yield break;
            if (plan.IsConstant)
                yield return plan.ConstantMotion;
            if (plan.TruthTable != null)
            {
                foreach (var motion in plan.TruthTable.Values)
                    yield return motion;
            }
            if (plan.SelectorSamples != null)
            {
                foreach (var sample in plan.SelectorSamples)
                    yield return sample.Motion;
            }
            if (plan.DecisionTable != null)
            {
                foreach (var motion in plan.DecisionTable.Values)
                    yield return motion;
            }
        }

        private static IEnumerable<VirtualCondition> EnumerateVirtualPlanConditions(VirtualPlan plan)
        {
            if (plan == null)
                yield break;
            foreach (var branch in plan.Branches ?? Enumerable.Empty<VirtualBranch>())
            {
                foreach (var condition in branch.Conditions ?? Enumerable.Empty<VirtualCondition>())
                    yield return condition;
            }
        }

        private static bool TryBuildVirtualPriorityDimensions(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorOptimizerOptions options,
            out List<PriorityDimension> dimensions,
            out long combinations)
        {
            var valuesByParameter = new Dictionary<string, SortedSet<float>>(StringComparer.Ordinal);
            var binary = candidates.SelectMany(candidate => candidate.Plan?.AssumedBinaryParameters ?? new HashSet<string>()).ToHashSet();
            if (candidates.Any(candidate => candidate.Plan != null &&
                (!string.IsNullOrWhiteSpace(candidate.Plan.SelectorParameter) && binary.Contains(candidate.Plan.SelectorParameter) ||
                 (candidate.Plan.DecisionDimensions ?? new List<VirtualDecisionDimension>()).Any(axis =>
                    binary.Contains(axis.Parameter) && axis.Type != AnimatorControllerParameterType.Bool))))
                return FailVirtualDimensions(out dimensions, out combinations);
            foreach (var candidate in candidates)
            {
                var plan = candidate.Plan;
                if (plan == null)
                    return FailVirtualDimensions(out dimensions, out combinations);
                if (plan.BooleanParameters != null)
                {
                    foreach (var parameter in plan.BooleanParameters)
                        AddVirtualPriorityValue(valuesByParameter, parameter, 0f, 1f);
                }
                else if (!string.IsNullOrWhiteSpace(plan.SelectorParameter))
                {
                    if (plan.SelectorIsApproximate ||
                        plan.SelectorParameterType != AnimatorControllerParameterType.Int &&
                        plan.SelectorParameterType != AnimatorControllerParameterType.Float)
                        return FailVirtualDimensions(out dimensions, out combinations);
                    foreach (var sample in plan.SelectorSamples ?? Enumerable.Empty<(float Value, VirtualMotionRef Motion)>())
                        AddVirtualPriorityValue(valuesByParameter, plan.SelectorParameter, sample.Value);
                }
                else if (plan.DecisionDimensions != null)
                {
                    foreach (var dimension in plan.DecisionDimensions)
                    {
                        if (dimension.IsApproximate)
                            return FailVirtualDimensions(out dimensions, out combinations);
                        AddVirtualPriorityValue(valuesByParameter, dimension.Parameter, dimension.Values.ToArray());
                    }
                }
                else if (!plan.IsConstant)
                {
                    return FailVirtualDimensions(out dimensions, out combinations);
                }
            }

            dimensions = valuesByParameter
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PriorityDimension
                {
                    Parameter = pair.Key,
                    Values = pair.Value.ToList(),
                })
                .ToList();
            combinations = 1;
            foreach (var dimension in dimensions)
            {
                if (!AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(dimension.Values) ||
                    dimension.Values.Count > options.MaxNumericSelectorSamples ||
                    combinations > long.MaxValue / dimension.Values.Count)
                    return false;
                combinations *= dimension.Values.Count;
            }
            return combinations <= Math.Max(1, options.MaxPriorityCombinations);
        }

        private static bool FailVirtualDimensions(
            out List<PriorityDimension> dimensions,
            out long combinations)
        {
            dimensions = null;
            combinations = 0;
            return false;
        }

        private static void AddVirtualPriorityValue(
            IDictionary<string, SortedSet<float>> valuesByParameter,
            string parameter,
            params float[] values)
        {
            if (!valuesByParameter.TryGetValue(parameter, out var parameterValues))
            {
                parameterValues = new SortedSet<float>();
                valuesByParameter.Add(parameter, parameterValues);
            }
            foreach (var value in values)
                parameterValues.Add(value);
        }

        private static VirtualMotion BuildVirtualPriorityTree(
            VirtualAnimatorController controller,
            IReadOnlyList<VirtualCandidate> candidates,
            VirtualClip emptyClip,
            AnimatorOptimizerOptions options,
            bool additive)
        {
            if (!TryBuildVirtualPriorityDimensions(candidates, options, out var dimensions, out _))
                throw new InvalidOperationException("NDMF virtual Override priority cannot be represented with the configured finite domain.");
            return BuildVirtualPriorityNode(controller, candidates, dimensions, 0,
                new Dictionary<string, float>(StringComparer.Ordinal), emptyClip, additive,
                new Dictionary<string, VirtualMotion>(StringComparer.Ordinal));
        }

        private static VirtualMotion BuildVirtualPriorityNode(
            VirtualAnimatorController controller,
            IReadOnlyList<VirtualCandidate> candidates,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values,
            VirtualClip emptyClip,
            bool additive,
            IDictionary<string, VirtualMotion> memo)
        {
            if (depth >= dimensions.Count)
                return CreateVirtualPriorityClip(candidates, values, emptyClip, additive, memo);

            var dimension = dimensions[depth];
            var children = new List<VirtualBlendTree.VirtualChildMotion>();
            foreach (var value in dimension.Values)
            {
                values[dimension.Parameter] = value;
                children.Add(new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = BuildVirtualPriorityNode(controller, candidates, dimensions, depth + 1, values, emptyClip, additive, memo),
                    Threshold = value,
                });
            }
            values.Remove(dimension.Parameter);

            var signatures = children.Select(child => BuildVirtualMotionSignature(new VirtualMotionRef { Motion = child.Motion })).ToList();
            if (signatures.Count > 0 && signatures.All(signature =>
                    string.Equals(signature, signatures[0], StringComparison.Ordinal)))
                return children[0].Motion;

            var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(signatures);
            var memoKey = AnimatorOptimizerSemanticUtility.DecisionNodeKey(dimension.Parameter,
                retained.Select(index => dimension.Values[index]), retained.Select(index => signatures[index]));
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;
            var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "PriorityTree_" + virtualPriorityCounter++);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = dimension.Parameter;
            tree.UseAutomaticThresholds = false;
            tree.Children = retained.Select(index => children[index]).ToImmutableList();
            memo[memoKey] = tree;
            return tree;
        }

        private static VirtualMotionRef EvaluateVirtualPlan(
            VirtualPlan plan,
            IReadOnlyDictionary<string, float> values)
        {
            if (plan == null)
                return null;
            if (plan.IsConstant)
                return plan.ConstantMotion;
            if (plan.BooleanParameters != null)
            {
                if (plan.BooleanParameters.Count > 30)
                    return null;
                var mask = 0;
                for (var index = 0; index < plan.BooleanParameters.Count; index++)
                {
                    if (values.TryGetValue(plan.BooleanParameters[index], out var value) && value >= 0.5f)
                        mask |= 1 << index;
                }
                return plan.TruthTable.TryGetValue(mask, out var motion) ? motion : null;
            }
            if (!string.IsNullOrWhiteSpace(plan.SelectorParameter) && values.TryGetValue(plan.SelectorParameter, out var selectorValue))
            {
                if (plan.SelectorIsApproximate || plan.SelectorSamples == null) return null;
                return AnimatorOptimizerSemanticUtility.TryEvaluateConstantTable(
                    new IReadOnlyList<float>[] { plan.SelectorSamples.Select(sample => sample.Value).ToArray() },
                    new[] { selectorValue }, plan.SelectorSamples.Select((sample, index) => (sample, index))
                        .ToDictionary(item => item.index, item => item.sample.Motion),
                    AreVirtualPriorityMotionsEquivalent, out var selectorMotion) ? selectorMotion : null;
            }
            if (plan.DecisionDimensions != null)
            {
                var inputs = new List<float>();
                foreach (var dimension in plan.DecisionDimensions)
                {
                    if (dimension.IsApproximate || !values.TryGetValue(dimension.Parameter, out var value)) return null;
                    inputs.Add(value);
                }
                return AnimatorOptimizerSemanticUtility.TryEvaluateConstantTable(
                    plan.DecisionDimensions.Select(dimension => (IReadOnlyList<float>)dimension.Values).ToArray(),
                    inputs, plan.DecisionTable, AreVirtualPriorityMotionsEquivalent, out var decisionMotion) ? decisionMotion : null;
            }
            return null;
        }

        private static bool AreVirtualPriorityMotionsEquivalent(VirtualMotionRef left, VirtualMotionRef right) =>
            left != null && right != null && string.Equals(BuildVirtualMotionSignature(left), BuildVirtualMotionSignature(right), StringComparison.Ordinal);

        private static VirtualClip CreateVirtualPriorityClip(
            IReadOnlyList<VirtualCandidate> candidates,
            IReadOnlyDictionary<string, float> values,
            VirtualClip emptyClip,
            bool additive,
            IDictionary<string, VirtualMotion> memo = null)
        {
            var floatCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            VirtualClip legacySource = null;
            var sourceClips = new List<VirtualClip>();
            foreach (var candidate in candidates.OrderBy(item => item.Index))
            {
                var motion = EvaluateVirtualPlan(candidate.Plan, values);
                if (motion?.Motion == null)
                    continue;
                if (!(motion.Motion is VirtualClip clip) || clip.IsMarkerClip)
                    throw new InvalidOperationException("NDMF virtual priority composite requires editable VirtualClip leaves.");
                legacySource ??= clip;
                sourceClips.Add(clip);
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var curve = clip.GetFloatCurve(binding);
                    var key = new BindingKey(binding);
                    var cloned = CloneVirtualCurve(curve);
                    if (additive && floatCurves.TryGetValue(key, out var existingFloat))
                        floatCurves[key] = (binding, AddStaticVirtualCurves(existingFloat.Curve, cloned));
                    else
                        floatCurves[key] = (binding, cloned);
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    var curve = clip.GetObjectCurve(binding);
                    objectCurves[new BindingKey(binding)] = (binding,
                        curve == null ? Array.Empty<ObjectReferenceKeyframe>() : curve.ToArray());
                }
            }
            if (floatCurves.Count == 0 && objectCurves.Count == 0)
                return emptyClip;

            var outputKey = "output:" + string.Concat(floatCurves.Values.Select(item =>
                AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding) +
                AnimatorOptimizerSynchronizedGraph.Atom(item.Curve.length == 0 ? "empty" :
                    item.Curve.keys[0].value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(objectCurves.Values.Select(item => AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding) +
                    AnimatorOptimizerSynchronizedGraph.Atom(item.Curve.Length == 0 ? "empty" :
                        item.Curve[0].value == null ? "null" : item.Curve[0].value.GetInstanceID().ToString())))
                .OrderBy(key => key, StringComparer.Ordinal).Select(AnimatorOptimizerSynchronizedGraph.Atom));
            var bounds = GetCombinedVirtualLocalBounds(sourceClips);
            outputKey += AnimatorOptimizerSynchronizedGraph.Atom(string.Join(",", new[] {
                bounds.center.x, bounds.center.y, bounds.center.z, bounds.extents.x, bounds.extents.y, bounds.extents.z
            }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
            if (memo != null && memo.TryGetValue(outputKey, out var cachedOutput)) return (VirtualClip)cachedOutput;

            var result = VirtualClip.Create(GeneratedAssetPrefix + "PriorityClip_" + virtualPriorityCounter++);
            if (legacySource != null)
            {
                result.Legacy = legacySource.Legacy;
                result.FrameRate = legacySource.FrameRate;
                result.WrapMode = legacySource.WrapMode;
                result.UseHighQualityCurves = legacySource.UseHighQualityCurves;
                result.LocalBounds = GetCombinedVirtualLocalBounds(sourceClips);
                result.Settings = legacySource.Settings;
            }
            foreach (var curve in floatCurves.Values)
                result.SetFloatCurve(curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                result.SetObjectCurve(curve.Binding, curve.Curve);
            if (memo != null) memo[outputKey] = result;
            return result;
        }

        private static AnimationCurve CloneVirtualCurve(AnimationCurve source)
        {
            if (source == null)
                return new AnimationCurve();
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private static AnimationCurve AddStaticVirtualCurves(AnimationCurve left, AnimationCurve right)
        {
            var leftValue = left == null || left.keys.Length == 0 ? 0f : left.keys[0].value;
            var rightValue = right == null || right.keys.Length == 0 ? 0f : right.keys[0].value;
            var sum = leftValue + rightValue;
            if (!IsFinite(sum))
                throw new InvalidOperationException("Static additive curves overflow; refusing non-finite animation output.");
            return AnimationCurve.Constant(0f, 1f / 60f, sum);
        }

        private static bool IsStaticVirtualClip(VirtualClip clip)
        {
            if (!IsOrdinaryVirtualClip(clip))
                return false;
            var settings = clip.Settings;
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                clip.AdditiveReferencePoseClip != null)
                return false;
            foreach (var binding in clip.GetFloatCurveBindings())
            {
                var curve = clip.GetFloatCurve(binding);
                if (binding.type == typeof(Animator) || !IsConstantVirtualCurve(curve))
                    return false;
            }
            foreach (var binding in clip.GetObjectCurveBindings())
            {
                var curve = clip.GetObjectCurve(binding);
                if (binding.type == typeof(Animator) || !IsConstantVirtualObjectCurve(curve))
                    return false;
            }
            return true;
        }

        private static bool IsConstantVirtualObjectCurve(ObjectReferenceKeyframe[] curve)
        {
            return AnimatorOptimizerSemanticUtility.IsConstantObjectCurve(curve);
        }

        private static bool TryBuildPlan(
            VirtualAnimatorController controller,
            VirtualLayer layer,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan,
            VirtualLayerScope scope = null)
        {
            plan = null;
            var previousScope = currentLayerScope;
            currentLayerScope = scope ?? BeginLayerScope(controller, options);
            var previousExitTime = currentLayerScope.HasExitTime;
            currentLayerScope.HasExitTime = HasEffectiveVirtualExitTime(layer?.StateMachine);
            try
            {
                var built = TryBuildPlanCore(controller, layer, options, out var builtPlan);
                bool HasTimedOutput() => built && EnumerateVirtualPlanMotions(builtPlan)
                    .Any(motion => motion?.Bake == null && !IsTimeIndependentMotion(motion?.Motion));
                void TryFinitePlan(bool allowHeuristics)
                {
                    if (!TryBuildFiniteVirtualGraphPlan(controller, layer, options, allowHeuristics, out var candidate) ||
                        built && !HasTimedOutput() && VirtualPlanEntryCount(candidate) >= VirtualPlanEntryCount(builtPlan)) return;
                    builtPlan = candidate;
                    built = true;
                }
                if (!built || !HasTimedOutput() && VirtualPlanEntryCount(builtPlan) > 1) TryFinitePlan(false);
                if (options.Heuristics != AnimatorOptimizerComponent.HeuristicFlags.None &&
                    (!built || HasTimedOutput() || options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles) &&
                     VirtualPlanEntryCount(builtPlan) > 1)) TryFinitePlan(true);
                if (!built) return false;
                // Bindings can be fully known while their AAP evaluation order must
                // remain untouched (notably MA ActiveSelf proxies and RC v2 trees).
                if (EnumerateVirtualPlanMotions(builtPlan).Any(motion =>
                    ContainsVirtualAnimatorCurves(motion?.Motion, new HashSet<VirtualMotion>()) ||
                    ContainsUnsupportedVirtualCurveData(motion?.Motion, new HashSet<VirtualMotion>())))
                    return false;
                if (!ValidateVirtualPlanParameters(controller, builtPlan, options))
                    return false;
                if (!TryGetUniformVirtualStateTag(layer?.StateMachine, out var stateTag))
                    return false;
                builtPlan.StateTag = stateTag;
                plan = builtPlan;
                if (options.VerboseLogging)
                {
                    foreach (var state in layer.StateMachine.AllStates().Where(state => state != null &&
                        (state.SpeedParameter != null || state.CycleOffsetParameter != null || state.TimeParameter != null) &&
                        IsTimeIndependentMotion(state.Motion)))
                        Debug.Log($"[AnimatorOptimizer] NDMF '{layer.Name}/{state.Name}': time-independent output; playback parameters need not be copied into the generated tree. Transition timing is checked separately.");
                }
                return true;
            }
            finally
            {
                currentLayerScope.HasExitTime = previousExitTime;
                currentLayerScope = previousScope;
            }
        }

        private static bool TryGetUniformVirtualStateTag(
            VirtualStateMachine stateMachine,
            out string tag)
        {
            tag = string.Empty;
            if (stateMachine == null)
                return false;
            var tagStates = stateMachine.StateMachines.Count == 0
                ? GetPotentiallyReachableVirtualStates(stateMachine)
                : stateMachine.AllStates().ToList();
            var tags = tagStates
                .Select(state => state?.Tag ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (tags.Count > 1)
                return false;
            tag = tags.FirstOrDefault() ?? string.Empty;
            return true;
        }

        private static bool TryBuildPlanCore(
            VirtualAnimatorController controller,
            VirtualLayer layer,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (layer == null || IsGeneratedLayerName(layer.Name, options.GeneratedLayerName) ||
                layer.Name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal) ||
                layer.StateMachine == null ||
                (layer.BlendingMode != AnimatorLayerBlendingMode.Override &&
                 layer.BlendingMode != AnimatorLayerBlendingMode.Additive) || layer.IKPass ||
                layer.SyncedLayerIndex >= 0 ||
                controller.Layers.FirstOrDefault() != layer && layer.OriginalPhysicalLayerIndex != 0 && layer.DefaultWeight != 1f)
                return false;

            var stateMachine = layer.StateMachine;
            if (!AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(stateMachine.Behaviours, out _) ||
                stateMachine.DefaultState == null && stateMachine.StateMachines.Count == 0)
                return false;

            if (stateMachine.StateMachines.Count > 0)
            {
                if (TryBuildVirtualNestedWrapperPlan(controller, layer, stateMachine, options, out plan))
                    return true;
                if (TryBuildVirtualNestedEffectEquivalentPlan(stateMachine, out plan))
                    return true;
                if (!TryResolveVirtualNestedStaticDefault(stateMachine, out var nestedDefault) ||
                    !ValidateState(nestedDefault))
                    return false;
                plan = new VirtualPlan
                {
                    IsConstant = true,
                    ConstantMotion = ToMotionRef(nestedDefault),
                };
                return true;
            }

            if (stateMachine.States.Any(child => child.State == null) ||
                stateMachine.States.GroupBy(child => child.State).Any(group => group.Count() > 1))
                return false;

            var states = GetPotentiallyReachableVirtualStates(stateMachine);
            if (TryBuildVirtualEntryDrivenPlan(controller, stateMachine, states, options, out plan))
                return true;
            if (TryBuildVirtualEffectEquivalentPlan(stateMachine, states, out plan))
                return true;
            var allStatic = states.All(state => GetEffectiveVirtualTransitions(state.Transitions).Count == 0) &&
                            GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Count == 0 &&
                            GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count == 0;
            if (allStatic)
            {
                if (!ValidateState(states.FirstOrDefault(state => state == stateMachine.DefaultState)))
                    return false;
                plan = new VirtualPlan
                {
                    IsConstant = true,
                    ConstantMotion = ToMotionRef(stateMachine.DefaultState),
                };
                return true;
            }

            var effectiveAnyTransitions = GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions);
            var hasStateTransitions = states.Any(state => GetEffectiveVirtualTransitions(state.Transitions).Count > 0);
            if (TryBuildVirtualStateIndependentOutputPlan(controller, stateMachine, states, options, out plan))
                return true;
            if (hasStateTransitions || effectiveAnyTransitions.Any(transition => !transition.CanTransitionToSelf))
                return false;

            var transitions = effectiveAnyTransitions.ToList();
            if (transitions.Count == 0)
            {
                if (states.Count == 2 && states.All(state => GetEffectiveVirtualTransitions(state.Transitions).Count == 1))
                    transitions = states.Select(state => GetEffectiveVirtualTransitions(state.Transitions)[0]).ToList();
                else if (!TryExtractVirtualStateIndependentTransitions(states, options.IgnoreTransitionTimingForStaticMotions, out transitions))
                    return false;
            }

            var branches = new List<VirtualBranch>();
            for (var order = 0; order < transitions.Count; order++)
            {
                var transition = transitions[order];
                if (transition == null || transition.DestinationState == null || transition.DestinationStateMachine != null ||
                    transition.IsExit || transition.Mute ||
                    HasBlockingVirtualTransitionTiming(transition) || transition.Conditions.Count == 0)
                    return false;

                var conditions = new List<VirtualCondition>();
                foreach (var source in transition.Conditions)
                {
                    if (!TryNormalizeCondition(controller, source, options, out var condition))
                        return false;
                    conditions.Add(condition);
                }
                branches.Add(new VirtualBranch
                {
                    Motion = ToMotionRef(transition.DestinationState),
                    Conditions = conditions,
                    Order = order,
                });
            }

            foreach (var state in states)
            {
                if (!ValidateState(state, allowTransitions: true) || !IsTimeIndependentMotion(state.Motion))
                    return false;
            }

            if (TryBuildBooleanTruthTable(branches, options, out plan))
                return true;
            if (TryBuildNumericSelector(branches, options, out plan))
                return true;
            if (TryBuildVirtualFiniteParameterTable(controller, branches, options, out plan))
                return true;
            return false;
        }

        private static bool TryBuildVirtualNestedWrapperPlan(
            VirtualAnimatorController controller,
            VirtualLayer layer,
            VirtualStateMachine rootStateMachine,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (rootStateMachine == null || rootStateMachine.StateMachines.Count != 1 ||
                rootStateMachine.States.Count != 0 || rootStateMachine.Behaviours.Count != 0 ||
                GetEffectiveVirtualTransitions(rootStateMachine.AnyStateTransitions).Count != 0)
                return false;

            var childMachine = rootStateMachine.StateMachines[0].StateMachine;
            if (childMachine == null || childMachine.Behaviours.Count != 0 ||
                childMachine.States.Any(child => child.State != null &&
                    GetEffectiveVirtualTransitions(child.State.Transitions).Any(transition => transition.IsExit)) ||
                GetEffectiveVirtualTransitions(childMachine.AnyStateTransitions).Any(transition => transition.IsExit) ||
                rootStateMachine.StateMachineTransitions.TryGetValue(childMachine, out var crossTransitions) &&
                GetEffectiveVirtualTransitions(crossTransitions).Count != 0)
                return false;

            var entries = GetEffectiveVirtualTransitions(rootStateMachine.EntryTransitions);
            if (entries.Count != 1)
                return false;
            var entry = entries[0];
            if (entry.DestinationStateMachine != childMachine || entry.DestinationState != null ||
                entry.IsExit || entry.Conditions.Count != 0)
                return false;

            var originalStateMachine = layer.StateMachine;
            try
            {
                layer.StateMachine = childMachine;
                return TryBuildPlanCore(controller, layer, options, out plan);
            }
            finally
            {
                layer.StateMachine = originalStateMachine;
            }
        }

        private static List<VirtualState> GetPotentiallyReachableVirtualStates(VirtualStateMachine machine)
        {
            var states = machine.States.Select(child => child.State).Where(state => state != null).ToList();
            var available = states.ToHashSet();
            var reached = new HashSet<VirtualState>();
            var pending = new Queue<VirtualState>();
            void Add(VirtualState state)
            {
                if (state != null && available.Contains(state) && reached.Add(state)) pending.Enqueue(state);
            }
            Add(machine.DefaultState);
            foreach (var transition in GetEffectiveVirtualTransitions(machine.EntryTransitions))
                Add(transition.DestinationState);
            if (pending.Count > 0)
                foreach (var transition in GetEffectiveVirtualTransitions(machine.AnyStateTransitions))
                    Add(transition.DestinationState);
            while (pending.Count > 0)
            {
                var state = pending.Dequeue();
                foreach (var transition in GetEffectiveVirtualTransitions(state.Transitions)) Add(transition.DestinationState);
            }
            return states.Where(reached.Contains).ToList();
        }

        private static bool TryExtractVirtualStateIndependentTransitions(
            IReadOnlyList<VirtualState> states,
            bool ignoreTiming,
            out List<VirtualStateTransition> transitions)
        {
            transitions = null;
            if (states == null || states.Count == 0 || states.Any(state => state == null))
                return false;
            var first = GetEffectiveVirtualTransitions(states[0].Transitions);
            if (first.Count == 0)
                return false;
            foreach (var state in states.Skip(1))
            {
                var current = GetEffectiveVirtualTransitions(state.Transitions);
                if (current.Count != first.Count)
                    return false;
                for (var index = 0; index < first.Count; index++)
                {
                    if (!AreEquivalentVirtualTransitions(first[index], current[index], ignoreTiming))
                        return false;
                }
            }
            transitions = first.ToList();
            return true;
        }

        private static bool AreEquivalentVirtualTransitions(
            VirtualStateTransition left,
            VirtualStateTransition right,
            bool ignoreTiming)
        {
            if (left == null || right == null)
                return left == right;
            if (left.DestinationState != right.DestinationState ||
                left.DestinationStateMachine != right.DestinationStateMachine ||
                left.IsExit != right.IsExit || left.Mute != right.Mute || left.Solo != right.Solo ||
                left.CanTransitionToSelf != right.CanTransitionToSelf ||
                left.InterruptionSource != right.InterruptionSource || left.OrderedInterruption != right.OrderedInterruption)
                return false;
            if (!ignoreTiming &&
                (left.ExitTime.HasValue != right.ExitTime.HasValue ||
                 left.ExitTime.HasValue && left.ExitTime.Value != right.ExitTime.Value ||
                 left.Duration != right.Duration || left.Offset != right.Offset ||
                 left.HasFixedDuration != right.HasFixedDuration))
                return false;
            if (left.Conditions.Count != right.Conditions.Count)
                return false;
            for (var index = 0; index < left.Conditions.Count; index++)
            {
                if (!string.Equals(left.Conditions[index].parameter, right.Conditions[index].parameter, StringComparison.Ordinal) ||
                    left.Conditions[index].mode != right.Conditions[index].mode ||
                    left.Conditions[index].threshold != right.Conditions[index].threshold)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Virtual mirror of the physical state-independent output proof: a graph whose
        /// one-step result under every finite input is the same Motion regardless of the
        /// current State has no observable history and can become a decision tree.
        /// </summary>
        private static bool TryBuildVirtualStateIndependentOutputPlan(
            VirtualAnimatorController controller,
            VirtualStateMachine stateMachine,
            IReadOnlyList<VirtualState> states,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (controller == null || stateMachine == null || states == null || states.Count < 2 ||
                states.Any(state => state == null) ||
                GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count > 0)
                return false;

            var stateSet = new HashSet<VirtualState>(states);
            var anyTransitions = new List<VirtualNormalizedTransition>();
            var effectiveAny = GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions);
            for (var order = 0; order < effectiveAny.Count; order++)
            {
                var transition = effectiveAny[order];
                if (transition == null || transition.DestinationState == null ||
                    !stateSet.Contains(transition.DestinationState) || transition.DestinationStateMachine != null ||
                    transition.IsExit ||
                    HasBlockingVirtualTransitionTiming(transition) ||
                    !TryNormalizeVirtualConditions(controller, transition.Conditions, options, out var conditions))
                    return false;
                anyTransitions.Add(new VirtualNormalizedTransition
                {
                    Destination = transition.DestinationState,
                    Conditions = conditions,
                    Order = order,
                    CanTransitionToSelf = transition.CanTransitionToSelf,
                });
            }

            var transitions = new List<VirtualNormalizedTransition>();
            foreach (var state in states)
            {
                if (!ValidateState(state, allowTransitions: true) || !IsTimeIndependentMotion(state.Motion))
                    return false;
                var sourceTransitions = GetEffectiveVirtualTransitions(state.Transitions);
                for (var order = 0; order < sourceTransitions.Count; order++)
                {
                    var transition = sourceTransitions[order];
                    if (transition == null || transition.DestinationState == null ||
                        !stateSet.Contains(transition.DestinationState) || transition.DestinationStateMachine != null ||
                        transition.IsExit ||
                        HasBlockingVirtualTransitionTiming(transition) ||
                        !TryNormalizeVirtualConditions(controller, transition.Conditions, options, out var conditions))
                        return false;
                    transitions.Add(new VirtualNormalizedTransition
                    {
                        Source = state,
                        Destination = transition.DestinationState,
                        Conditions = conditions,
                        Order = order,
                    });
                }
            }
            if (transitions.Count == 0 && anyTransitions.Count == 0)
                return false;

            var allTransitions = anyTransitions.Concat(transitions).ToList();
            var allConditions = allTransitions.SelectMany(transition => transition.Conditions).ToList();
            if (!TryBuildVirtualDecisionDimensions(allConditions, options, out var dimensions, out var combinations))
                return false;

            if (combinations > 65536L / states.Count)
                return false;
            var outputs = states.ToDictionary(state => state, ToMotionRef);
            var outputKeys = outputs.ToDictionary(pair => pair.Key, pair =>
                !pair.Value.Mirror && TryBuildVirtualStaticObservableMotionSignature(pair.Value.Motion, out var signature)
                    ? signature : BuildVirtualMotionRefEquivalenceKey(pair.Value));
            var table = new Dictionary<int, VirtualMotionRef>();
            var indices = new int[dimensions.Count];
            for (var combination = 0; combination < (int)combinations; combination++)
            {
                var values = BuildVirtualDecisionValues(dimensions, indices);
                if (!TryResolveVirtualStateIndependentMotion(states, anyTransitions, transitions, values, outputs, outputKeys, out var motion))
                    return false;
                table[BuildVirtualDecisionKey(dimensions, indices)] = motion;
                IncrementVirtualDecisionIndices(dimensions, indices);
            }

            plan = BuildVirtualDecisionPlanFromTable(dimensions, table);
            return plan != null;
        }

        private static bool TryResolveVirtualStateIndependentMotion(
            IReadOnlyList<VirtualState> states,
            IReadOnlyList<VirtualNormalizedTransition> anyTransitions,
            IReadOnlyList<VirtualNormalizedTransition> transitions,
            IReadOnlyDictionary<string, float> values,
            IReadOnlyDictionary<VirtualState, VirtualMotionRef> outputs,
            IReadOnlyDictionary<VirtualState, string> outputKeys,
            out VirtualMotionRef motion)
        {
            motion = null;
            string motionKey = null;
            foreach (var state in states)
            {
                var transition = anyTransitions
                    .OrderBy(candidate => candidate.Order)
                    .Where(candidate => candidate.Destination != state || candidate.CanTransitionToSelf)
                    .FirstOrDefault(candidate => candidate.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                if (transition == null)
                {
                    transition = transitions
                        .Where(candidate => candidate.Source == state)
                        .OrderBy(candidate => candidate.Order)
                        .FirstOrDefault(candidate => candidate.Conditions.All(condition =>
                            values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                }
                var destination = transition?.Destination ?? state;
                var output = outputs[destination];
                var key = outputKeys[destination];
                if (key == null)
                    return false;
                if (motion == null)
                {
                    motion = output;
                    motionKey = key;
                }
                else if (!string.Equals(motionKey, key, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return motion != null;
        }

        private static bool ValidateVirtualPlanParameters(
            VirtualAnimatorController controller,
            VirtualPlan plan,
            AnimatorOptimizerOptions options)
        {
            if (controller == null || plan == null)
                return false;
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var motion in EnumerateVirtualPlanMotions(plan))
                CollectVirtualMotionParameters(motion?.Motion, parameters, new HashSet<VirtualMotion>());
            foreach (var motion in EnumerateVirtualPlanMotions(plan))
                if (!string.IsNullOrWhiteSpace(motion?.Bake?.Parameter)) parameters.Add(motion.Bake.Parameter);
            plan.ReferencedParameters = parameters;
            foreach (var parameterName in parameters)
            {
                if (IsVrcGlobalParameter(parameterName, options.UseVrChatParameterSemantics))
                    continue;
                if (!controller.Parameters.TryGetValue(parameterName, out var parameter))
                    return false;
                if (parameter.type == AnimatorControllerParameterType.Float && !IsFinite(parameter.defaultFloat))
                    return false;
                if (parameter.type == AnimatorControllerParameterType.Bool && !options.UpgradeBoolParametersToFloat ||
                    parameter.type == AnimatorControllerParameterType.Int && !options.UpgradeIntParametersToFloat)
                    return false;
                if (parameter.type == AnimatorControllerParameterType.Bool ||
                    parameter.type == AnimatorControllerParameterType.Int ||
                    parameter.type == AnimatorControllerParameterType.Float)
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private static void CollectVirtualMotionParameters(
            VirtualMotion motion,
            ISet<string> parameters,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return;
            if (motion is VirtualBlendTree tree)
            {
                if (tree.BlendType != BlendTreeType.Direct)
                {
                    if (!string.IsNullOrWhiteSpace(tree.BlendParameter))
                        parameters.Add(tree.BlendParameter);
                    if (tree.BlendType != BlendTreeType.Simple1D && !string.IsNullOrWhiteSpace(tree.BlendParameterY))
                        parameters.Add(tree.BlendParameterY);
                }
                foreach (var child in tree.Children)
                {
                    if (tree.BlendType == BlendTreeType.Direct &&
                        !string.IsNullOrWhiteSpace(child.DirectBlendParameter))
                        parameters.Add(child.DirectBlendParameter);
                    CollectVirtualMotionParameters(child.Motion, parameters, visited);
                }
            }
        }

        private static bool IsVrcGlobalParameter(string parameterName, bool useVrChatParameterSemantics)
        {
            if (!useVrChatParameterSemantics) return false;
            switch (parameterName)
            {
                case "IsLocal":
                case "PreviewMode":
                case "Viseme":
                case "Voice":
                case "GestureLeft":
                case "GestureRight":
                case "GestureLeftWeight":
                case "GestureRightWeight":
                case "AngularY":
                case "VelocityX":
                case "VelocityY":
                case "VelocityZ":
                case "VelocityMagnitude":
                case "Upright":
                case "Grounded":
                case "Seated":
                case "AFK":
                case "TrackingType":
                case "VRMode":
                case "MuteSelf":
                case "InStation":
                case "Earmuffs":
                case "IsOnFriendsList":
                case "AvatarVersion":
                case "IsAnimatorEnabled":
                case "ScaleModified":
                case "ScaleFactor":
                case "ScaleFactorInverse":
                case "EyeHeightAsMeters":
                case "EyeHeightAsPercent":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildVirtualEntryDrivenPlan(
            VirtualAnimatorController controller,
            VirtualStateMachine stateMachine,
            IReadOnlyList<VirtualState> states,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (controller == null || stateMachine == null || states == null || states.Count == 0 ||
                GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count == 0 ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Count > 0 ||
                stateMachine.DefaultState == null || !states.Contains(stateMachine.DefaultState))
                return false;

            var stateSet = new HashSet<VirtualState>(states);
            var entries = new List<VirtualNormalizedTransition>();
            var effectiveEntries = GetEffectiveVirtualTransitions(stateMachine.EntryTransitions);
            for (var order = 0; order < effectiveEntries.Count; order++)
            {
                var transition = effectiveEntries[order];
                if (transition == null || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null || transition.IsExit ||
                    !stateSet.Contains(transition.DestinationState))
                    return false;
                if (!TryNormalizeVirtualConditions(controller, transition.Conditions, options, out var conditions))
                    return false;
                entries.Add(new VirtualNormalizedTransition
                {
                    Destination = transition.DestinationState,
                    Conditions = conditions,
                    Order = order,
                });
            }

            if (entries.Count == 1 && entries[0].Conditions.Count == 0 &&
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Count == 0 &&
                states.All(state => GetEffectiveVirtualTransitions(state.Transitions).Count == 0))
            {
                if (!ValidateState(entries[0].Destination))
                    return false;
                plan = new VirtualPlan
                {
                    IsConstant = true,
                    ConstantMotion = ToMotionRef(entries[0].Destination),
                };
                return true;
            }

            var exits = new Dictionary<VirtualState, List<VirtualNormalizedTransition>>();
            var allConditions = new List<VirtualCondition>();
            foreach (var state in states)
            {
                if (!ValidateState(state, allowTransitions: true) || !IsTimeIndependentMotion(state.Motion))
                    return false;
                var stateExits = new List<VirtualNormalizedTransition>();
                foreach (var transition in GetEffectiveVirtualTransitions(state.Transitions))
                {
                    if (transition == null || !transition.IsExit || transition.DestinationState != null ||
                        transition.DestinationStateMachine != null ||
                        HasBlockingVirtualTransitionTiming(transition))
                        return false;
                    if (!TryNormalizeVirtualConditions(controller, transition.Conditions, options, out var conditions))
                        return false;
                    allConditions.AddRange(conditions);
                    stateExits.Add(new VirtualNormalizedTransition
                    {
                        Source = state,
                        Conditions = conditions,
                        Order = stateExits.Count,
                    });
                }
                exits[state] = stateExits;
            }
            allConditions.AddRange(entries.SelectMany(entry => entry.Conditions));
            if (!TryBuildVirtualDecisionDimensions(allConditions, options, out var dimensions, out var combinations))
                return false;

            if (combinations > 65536L / states.Count)
                return false;
            var table = new Dictionary<int, VirtualMotionRef>();
            var indices = new int[dimensions.Count];
            var outputKeys = states.ToDictionary(state => state, state =>
                TryBuildVirtualStaticObservableMotionSignature(state.Motion, out var signature)
                    ? signature : BuildVirtualMotionRefEquivalenceKey(ToMotionRef(state)));
            for (var combination = 0; combination < (int)combinations; combination++)
            {
                var values = BuildVirtualDecisionValues(dimensions, indices);
                var entry = entries.OrderBy(item => item.Order).FirstOrDefault(item =>
                    item.Conditions.All(condition => values.TryGetValue(condition.Parameter, out var value) &&
                        condition.Matches(value)));
                var desired = entry?.Destination ?? stateMachine.DefaultState;
                foreach (var state in states)
                {
                    var exitsNow = exits[state].Any(item => item.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                    if (outputKeys[desired] == null || outputKeys[exitsNow ? desired : state] != outputKeys[desired])
                        return false;
                }
                table[BuildVirtualDecisionKey(dimensions, indices)] = ToMotionRef(desired);
                IncrementVirtualDecisionIndices(dimensions, indices);
            }

            plan = BuildVirtualDecisionPlanFromTable(dimensions, table);
            return plan != null;
        }

        private static bool TryNormalizeVirtualConditions(
            VirtualAnimatorController controller,
            IEnumerable<AnimatorCondition> sourceConditions,
            AnimatorOptimizerOptions options,
            out List<VirtualCondition> conditions)
        {
            conditions = new List<VirtualCondition>();
            foreach (var source in sourceConditions ?? Enumerable.Empty<AnimatorCondition>())
            {
                if (!TryNormalizeCondition(controller, source, options, out var condition))
                    return false;
                conditions.Add(condition);
            }
            return true;
        }

        private static bool TryBuildVirtualDecisionDimensions(
            IReadOnlyList<VirtualCondition> conditions,
            AnimatorOptimizerOptions options,
            out List<VirtualDecisionDimension> dimensions,
            out long combinations)
        {
            dimensions = new List<VirtualDecisionDimension>();
            combinations = 1;
            foreach (var group in (conditions ?? Array.Empty<VirtualCondition>())
                         .GroupBy(condition => condition.Parameter, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var list = group.ToList();
                var type = list[0].Type;
                if (list.Any(condition => condition.Type != type)) return false;
                var values = new SortedSet<float>();
                var approximate = false;
                if (type == AnimatorControllerParameterType.Bool)
                {
                    values.Add(0f);
                    values.Add(1f);
                }
                else if (type == AnimatorControllerParameterType.Int)
                {
                    foreach (var condition in list)
                    {
                        var threshold = Mathf.RoundToInt(condition.Threshold);
                        values.Add(threshold - 1);
                        values.Add(threshold);
                        values.Add(threshold + 1);
                    }
                }
                else if (type == AnimatorControllerParameterType.Float)
                {
                    foreach (var condition in list)
                    {
                        if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(condition.Threshold,
                            options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var isApproximate))
                            return false;
                        approximate |= isApproximate;
                    }
                }
                else
                {
                    return false;
                }
                if (values.Count > options.MaxNumericSelectorSamples && type != AnimatorControllerParameterType.Bool)
                    return false;
                if (values.Count == 0 || combinations > long.MaxValue / values.Count)
                    return false;
                combinations *= values.Count;
                dimensions.Add(new VirtualDecisionDimension
                {
                    Parameter = group.Key,
                    Type = type,
                    Values = values.ToList(),
                    IsApproximate = approximate,
                });
            }
            var allBoolean = dimensions.All(dimension => dimension.Type == AnimatorControllerParameterType.Bool);
            var limit = allBoolean ? Math.Max(2, options.MaxBooleanTruthTableEntries) :
                Math.Max(2, options.MaxPriorityCombinations);
            return combinations <= limit;
        }

        private static Dictionary<string, float> BuildVirtualDecisionValues(
            IReadOnlyList<VirtualDecisionDimension> dimensions,
            IReadOnlyList<int> indices)
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            for (var index = 0; index < dimensions.Count; index++)
                values[dimensions[index].Parameter] = dimensions[index].Values[indices[index]];
            return values;
        }

        private static int BuildVirtualDecisionKey(
            IReadOnlyList<VirtualDecisionDimension> dimensions,
            IReadOnlyList<int> indices)
        {
            var key = 0;
            var stride = 1;
            for (var index = 0; index < dimensions.Count; index++)
            {
                key += indices[index] * stride;
                stride *= dimensions[index].Values.Count;
            }
            return key;
        }

        private static void IncrementVirtualDecisionIndices(
            IReadOnlyList<VirtualDecisionDimension> dimensions,
            IList<int> indices)
        {
            for (var index = 0; index < indices.Count; index++)
            {
                if (++indices[index] < dimensions[index].Values.Count)
                    return;
                indices[index] = 0;
            }
        }

        private static VirtualPlan BuildVirtualDecisionPlanFromTable(
            IReadOnlyList<VirtualDecisionDimension> dimensions,
            IReadOnlyDictionary<int, VirtualMotionRef> table)
        {
            if (dimensions == null || table == null)
                return null;
            if (dimensions.Count == 0)
                return new VirtualPlan { IsConstant = true, ConstantMotion = table[0] };
            if (dimensions.All(dimension => dimension.Type == AnimatorControllerParameterType.Bool))
            {
                return new VirtualPlan
                {
                    Branches = new List<VirtualBranch>(),
                    BooleanParameters = dimensions.Select(dimension => dimension.Parameter).ToList(),
                    TruthTable = table.ToDictionary(pair => pair.Key, pair => pair.Value),
                };
            }
            if (dimensions.Count == 1)
            {
                var dimension = dimensions[0];
                return new VirtualPlan
                {
                    Branches = new List<VirtualBranch>(),
                    SelectorParameter = dimension.Parameter,
                    SelectorParameterType = dimension.Type,
                    SelectorIsApproximate = dimension.IsApproximate,
                    SelectorSamples = dimension.Values.Select((value, index) =>
                        (value, table[index])).ToList(),
                };
            }
            return new VirtualPlan
            {
                Branches = new List<VirtualBranch>(),
                DecisionDimensions = dimensions.ToList(),
                DecisionTable = table.ToDictionary(pair => pair.Key, pair => pair.Value),
            };
        }

        private static bool TryBuildVirtualNestedEffectEquivalentPlan(
            VirtualStateMachine rootStateMachine,
            out VirtualPlan plan)
        {
            plan = null;
            var states = new List<VirtualState>();
            var stateMachines = new HashSet<VirtualStateMachine>();
            CollectVirtualNestedStateGraph(rootStateMachine, states, stateMachines);
            if (states.Count == 0 || rootStateMachine == null ||
                rootStateMachine.DefaultState == null &&
                GetEffectiveVirtualTransitions(rootStateMachine.EntryTransitions).Count == 0)
                return false;

            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (!ValidateState(state, allowTransitions: true) || ToMotionRef(state).Mirror ||
                    !TryBuildVirtualStaticObservableMotionSignature(state.Motion, out var signature))
                    return false;
                signatures.Add(signature);
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;

            foreach (var stateMachine in stateMachines)
            {
                if (stateMachine != rootStateMachine && stateMachine.Behaviours.Count > 0 ||
                    stateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) ||
                    !HasOnlyReadOnlyVirtualTransitionConditions(stateMachine,
                        stateMachine.States.Where(child => child.State != null).Select(child => child.State)) ||
                    !AllVirtualNestedTransitionsAreLocal(stateMachine, states, stateMachines))
                    return false;
            }
            if (rootStateMachine.DefaultState != null &&
                !states.Contains(rootStateMachine.DefaultState))
                return false;

            plan = new VirtualPlan
            {
                IsConstant = true,
                ConstantMotion = ToMotionRef(rootStateMachine.DefaultState ?? states[0]),
            };
            return true;
        }

        private static void CollectVirtualNestedStateGraph(
            VirtualStateMachine stateMachine,
            ICollection<VirtualState> states,
            ISet<VirtualStateMachine> stateMachines)
        {
            if (stateMachine == null || !stateMachines.Add(stateMachine))
                return;
            foreach (var child in stateMachine.States)
            {
                if (child.State != null)
                    states.Add(child.State);
            }
            foreach (var child in stateMachine.StateMachines)
                CollectVirtualNestedStateGraph(child.StateMachine, states, stateMachines);
        }

        private static bool AllVirtualNestedTransitionsAreLocal(
            VirtualStateMachine stateMachine,
            IReadOnlyCollection<VirtualState> states,
            ISet<VirtualStateMachine> stateMachines)
        {
            foreach (var transition in GetEffectiveVirtualTransitions(stateMachine.EntryTransitions))
            {
                if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var transition in GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions))
            {
                if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var child in stateMachine.States)
            {
                foreach (var transition in GetEffectiveVirtualTransitions(child.State?.Transitions))
                {
                    if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            foreach (var child in stateMachine.StateMachines)
            {
                if (!stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                    continue;
                foreach (var transition in GetEffectiveVirtualTransitions(transitions))
                {
                    if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            return true;
        }

        private static bool IsLocalVirtualNestedTransition(
            VirtualTransitionBase transition,
            IReadOnlyCollection<VirtualState> states,
            ISet<VirtualStateMachine> stateMachines)
        {
            if (transition == null || transition.IsExit)
                return false;
            if (transition.DestinationState != null)
                return states.Contains(transition.DestinationState);
            if (transition.DestinationStateMachine != null)
                return stateMachines.Contains(transition.DestinationStateMachine);
            return false;
        }

        private static bool TryBuildVirtualEffectEquivalentPlan(
            VirtualStateMachine stateMachine,
            IReadOnlyList<VirtualState> states,
            out VirtualPlan plan)
        {
            plan = null;
            if (stateMachine == null || states == null || states.Count == 0 ||
                !HasOnlyReadOnlyVirtualTransitionConditions(stateMachine, states) ||
                stateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) ||
                GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count > 0 &&
                GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Any(transition =>
                    transition == null || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null) ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Any(transition =>
                    transition == null || transition.IsExit || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null))
                return false;

            var stateSet = new HashSet<VirtualState>(states);
            if (GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Any(transition =>
                    !stateSet.Contains(transition.DestinationState)) ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Any(transition =>
                    !stateSet.Contains(transition.DestinationState)))
                return false;
            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (!TryBuildVirtualStaticObservableMotionSignature(state.Motion, out var signature) ||
                    !ValidateState(state, allowTransitions: true) || ToMotionRef(state).Mirror ||
                    !stateSet.Contains(state))
                    return false;
                signatures.Add(signature);
                if (GetEffectiveVirtualTransitions(state.Transitions).Any(transition =>
                        transition == null || transition.IsExit || transition.DestinationState == null ||
                        transition.DestinationStateMachine != null || !stateSet.Contains(transition.DestinationState)))
                    return false;
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;
            if (stateMachine.DefaultState == null || !stateSet.Contains(stateMachine.DefaultState))
                return false;

            plan = new VirtualPlan
            {
                IsConstant = true,
                ConstantMotion = ToMotionRef(stateMachine.DefaultState ?? states[0]),
            };
            return true;
        }

        private static bool HasOnlyReadOnlyVirtualTransitionConditions(VirtualStateMachine machine,
            IEnumerable<VirtualState> states)
        {
            var controller = currentLayerScope?.Controller;
            if (controller == null) return false;
            var edges = GetEffectiveVirtualTransitions(machine.EntryTransitions).Cast<VirtualTransitionBase>()
                .Concat(GetEffectiveVirtualTransitions(machine.AnyStateTransitions))
                .Concat(states.Where(state => state != null).SelectMany(state => GetEffectiveVirtualTransitions(state.Transitions)))
                .Concat(machine.StateMachineTransitions.Values.SelectMany(transitions => GetEffectiveVirtualTransitions(transitions)));
            return edges.All(edge => AnimatorOptimizerSemanticUtility.AreReadOnlyConditions(edge.Conditions, name =>
                controller.Parameters.TryGetValue(name, out var parameter) ? parameter?.type :
                    TryGetVirtualGlobalParameterType(name, currentLayerScope?.Options?.UseVrChatParameterSemantics == true, out var global) ? global.type : (AnimatorControllerParameterType?)null));
        }

        private static bool TryBuildVirtualStaticObservableMotionSignature(
            VirtualMotion motion,
            out string signature)
        {
            signature = null;
            motion = NormalizeVirtualSingleton(motion);
            if (motion == null)
            {
                signature = "static-empty";
                return true;
            }
            if (!(motion is VirtualClip clip) || !IsStaticVirtualClip(clip))
                return false;

            var floatParts = new List<string>();
            foreach (var binding in clip.GetFloatCurveBindings())
            {
                if (binding.type == typeof(Animator))
                    return false;
                var curve = clip.GetFloatCurve(binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;
                if (!IsConstantVirtualCurve(curve))
                    return false;
                floatParts.Add("f:" + BuildVirtualBindingKey(binding) + ":" +
                    curve.keys[0].value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            var objectParts = new List<string>();
            foreach (var binding in clip.GetObjectCurveBindings())
            {
                var curve = clip.GetObjectCurve(binding);
                if (curve == null || curve.Length == 0)
                    continue;
                if (!IsConstantVirtualObjectCurve(curve))
                    return false;
                objectParts.Add("o:" + BuildVirtualBindingKey(binding) + ":" +
                    (curve[0].value == null ? "0" : curve[0].value.GetInstanceID().ToString()));
            }
            floatParts.Sort(StringComparer.Ordinal);
            objectParts.Sort(StringComparer.Ordinal);
            signature = "static:" + string.Concat(floatParts.Concat(objectParts).Select(AnimatorOptimizerSynchronizedGraph.Atom));
            return true;
        }

        private static string BuildVirtualBindingKey(EditorCurveBinding binding)
        {
            return AnimatorOptimizerSynchronizedGraph.BindingKey(binding);
        }

        private static bool TryBuildVirtualFiniteParameterTable(
            VirtualAnimatorController controller,
            IReadOnlyList<VirtualBranch> branches,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (branches == null || branches.Count == 0 ||
                branches.Any(branch => branch.Conditions == null || branch.Conditions.Count == 0) ||
                branches.SelectMany(branch => branch.Conditions).Any(condition =>
                    !condition.IsBool && !condition.IsInt &&
                    !condition.IsFloat))
                return false;

            var names = branches.SelectMany(branch => branch.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(parameter => parameter, StringComparer.Ordinal)
                .ToList();
            var dimensions = new List<VirtualDecisionDimension>();
            foreach (var name in names)
            {
                var conditions = branches.SelectMany(branch => branch.Conditions)
                    .Where(condition => string.Equals(condition.Parameter, name, StringComparison.Ordinal))
                    .ToList();
                var type = conditions[0].Type;
                if (conditions.Any(condition => condition.Type != type))
                    return false;
                var values = new SortedSet<float>();
                var approximate = false;
                if (type == AnimatorControllerParameterType.Bool)
                {
                    values.Add(0f);
                    values.Add(1f);
                }
                else if (type == AnimatorControllerParameterType.Int)
                {
                    foreach (var condition in conditions)
                    {
                        var threshold = Mathf.RoundToInt(condition.Threshold);
                        values.Add(threshold - 1);
                        values.Add(threshold);
                        values.Add(threshold + 1);
                    }
                }
                else if (type == AnimatorControllerParameterType.Float)
                {
                    foreach (var condition in conditions)
                    {
                        if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(condition.Threshold,
                            options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var isApproximate))
                            return false;
                        approximate |= isApproximate;
                    }
                }
                else
                {
                    return false;
                }
                dimensions.Add(new VirtualDecisionDimension
                {
                    Parameter = name,
                    Type = type,
                    Values = values.ToList(),
                    IsApproximate = approximate,
                });
            }

            long combinations = 1;
            foreach (var dimension in dimensions)
            {
                if (dimension.Values.Count == 0 || dimension.Values.Count > options.MaxNumericSelectorSamples ||
                    combinations > long.MaxValue / dimension.Values.Count)
                    return false;
                combinations *= dimension.Values.Count;
            }
            if (combinations > Math.Max(2, options.MaxPriorityCombinations))
                return false;

            var table = new Dictionary<int, VirtualMotionRef>();
            var indices = new int[dimensions.Count];
            for (var combination = 0; combination < (int)combinations; combination++)
            {
                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                var key = 0;
                var stride = 1;
                for (var dimensionIndex = 0; dimensionIndex < dimensions.Count; dimensionIndex++)
                {
                    var dimension = dimensions[dimensionIndex];
                    values[dimension.Parameter] = dimension.Values[indices[dimensionIndex]];
                    key += indices[dimensionIndex] * stride;
                    stride *= dimension.Values.Count;
                }
                var matched = branches.OrderBy(branch => branch.Order)
                    .FirstOrDefault(branch => branch.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                if (matched == null)
                    return false;
                table[key] = matched.Motion;
                for (var dimensionIndex = 0; dimensionIndex < indices.Length; dimensionIndex++)
                {
                    if (++indices[dimensionIndex] < dimensions[dimensionIndex].Values.Count)
                        break;
                    indices[dimensionIndex] = 0;
                }
            }

            plan = new VirtualPlan
            {
                Branches = branches.ToList(),
                DecisionDimensions = dimensions,
                DecisionTable = table,
            };
            return true;
        }

        private static bool IsTimeIndependentMotion(VirtualMotion motion)
        {
            if (motion == null)
                return true;
            if (motion is VirtualClip clip)
                return IsStaticVirtualClip(clip);
            if (motion is VirtualBlendTree tree)
                return tree.Children.All(child => IsTimeIndependentMotion(child.Motion));
            return false;
        }

        private static bool IsOrdinaryVirtualClip(VirtualClip clip)
        {
            return clip != null && !clip.IsMarkerClip && !clip.Legacy && !IsPotentialProxyVirtualClip(clip) &&
                TryGetVirtualOriginalObject(clip, out AnimationClip original) &&
                AnimatorOptimizerSemanticUtility.IsOrdinaryClip(original);
        }

        private static bool HasInspectableVirtualBindings(VirtualClip clip) => clip != null && !clip.IsMarkerClip &&
            !IsPotentialProxyVirtualClip(clip) && TryGetVirtualOriginalObject(clip, out AnimationClip original) &&
            AnimatorOptimizerSemanticUtility.HasInspectableBindings(original);

        private static bool IsPropertyOnlyVirtualMotion(VirtualMotion motion)
        {
            if (motion == null) return true;
            if (motion is VirtualClip clip)
                return IsOrdinaryVirtualClip(clip) && !clip.Settings.hasAdditiveReferencePose &&
                    clip.Settings.additiveReferencePoseClip == null && clip.AdditiveReferencePoseClip == null &&
                    !clip.GetFloatCurveBindings().Concat(clip.GetObjectCurveBindings())
                        .Any(binding => binding.type == typeof(Animator) || binding.type == typeof(Transform));
            return motion is VirtualBlendTree tree && tree.Children.All(child => IsPropertyOnlyVirtualMotion(child.Motion));
        }

        private static VirtualMotion NormalizeVirtualSingleton(VirtualMotion motion)
        {
            var visited = new HashSet<VirtualMotion>();
            while (motion is VirtualBlendTree tree && tree.BlendType == BlendTreeType.Simple1D &&
                tree.Children.Count == 1 && visited.Add(motion))
            {
                var child = tree.Children[0];
                if (child.Motion == null ||
                    (child.TimeScale != 1f || child.CycleOffset != 0f) && !IsTimeIndependentMotion(child.Motion) ||
                    child.Mirror && !IsPropertyOnlyVirtualMotion(child.Motion))
                    break;
                motion = child.Motion;
            }
            return motion;
        }

        private static bool IsConstantVirtualCurve(AnimationCurve curve)
        {
            return AnimatorOptimizerSemanticUtility.IsConstantCurve(curve);
        }

        private static bool TryResolveVirtualNestedStaticDefault(
            VirtualStateMachine stateMachine,
            out VirtualState state)
        {
            state = null;
            if (stateMachine == null || stateMachine.Behaviours.Count > 0 ||
                GetEffectiveVirtualTransitions(stateMachine.EntryTransitions).Count > 0 ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions).Count > 0)
                return false;

            if (stateMachine.DefaultState != null)
            {
                if (stateMachine.StateMachines.Any(child =>
                        child.StateMachine != null &&
                        HasEffectiveTransitionToVirtualStateMachine(stateMachine, child.StateMachine)))
                    return false;
                state = stateMachine.DefaultState;
                return GetEffectiveVirtualTransitions(state.Transitions).Count == 0;
            }

            if (stateMachine.States.Count != 0 || stateMachine.StateMachines.Count != 1)
                return false;
            var child = stateMachine.StateMachines[0];
            if (stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions) &&
                GetEffectiveVirtualTransitions(transitions).Count != 0)
                return false;
            return TryResolveVirtualNestedStaticDefault(child.StateMachine, out state);
        }

        private static bool HasEffectiveTransitionToVirtualStateMachine(
            VirtualStateMachine stateMachine,
            VirtualStateMachine target)
        {
            if (stateMachine == null || target == null)
                return false;
            if (GetEffectiveVirtualTransitions(stateMachine.EntryTransitions)
                .Any(transition => transition.DestinationStateMachine == target) ||
                GetEffectiveVirtualTransitions(stateMachine.AnyStateTransitions)
                .Any(transition => transition.DestinationStateMachine == target) ||
                stateMachine.States
                .Where(child => child.State != null)
                .SelectMany(child => GetEffectiveVirtualTransitions(child.State.Transitions))
                .Any(transition => transition.DestinationStateMachine == target) ||
                stateMachine.StateMachineTransitions.TryGetValue(target, out var transitions) &&
                GetEffectiveVirtualTransitions(transitions).Count > 0)
                return true;
            return false;
        }

        private static bool TryNormalizeCondition(
            VirtualAnimatorController controller,
            AnimatorCondition source,
            AnimatorOptimizerOptions options,
            out VirtualCondition condition)
        {
            condition = null;
            if (string.IsNullOrWhiteSpace(source.parameter) || float.IsNaN(source.threshold) || float.IsInfinity(source.threshold))
                return false;
            var hasDeclaredParameter = controller.Parameters.TryGetValue(source.parameter, out var parameter);
            if (!hasDeclaredParameter && !TryGetVirtualGlobalParameterType(source.parameter, options.UseVrChatParameterSemantics, out parameter))
                return false;
            if (parameter.type == AnimatorControllerParameterType.Float && !IsFinite(parameter.defaultFloat))
                return false;
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (hasDeclaredParameter && !options.UpgradeBoolParametersToFloat && !IsVrcGlobalParameter(source.parameter, options.UseVrChatParameterSemantics) ||
                    (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                     source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual))
                    return false;
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    source.threshold != 0f && source.threshold != 1f)
                    return false;
            }
            else if (parameter.type == AnimatorControllerParameterType.Int)
            {
                if (Math.Abs((double)source.threshold) > 16777214d)
                    return false;
                if (hasDeclaredParameter && !options.UpgradeIntParametersToFloat && !IsVrcGlobalParameter(source.parameter, options.UseVrChatParameterSemantics) ||
                    (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                     source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less) ||
                    source.threshold != Mathf.Round(source.threshold))
                    return false;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float)
            {
                if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(source.threshold,
                        options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, new HashSet<float>(), out _) ||
                    (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                     source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less))
                    return false;
            }
            else
            {
                return false;
            }

            condition = new VirtualCondition
            {
                Parameter = source.parameter,
                Type = parameter.type,
                Mode = source.mode,
                Threshold = parameter.type == AnimatorControllerParameterType.Int ? Mathf.Round(source.threshold) : source.threshold,
            };
            return true;
        }

        private static bool TryGetVirtualGlobalParameterType(
            string parameterName, bool useVrChatParameterSemantics,
            out AnimatorControllerParameter parameter)
        {
            parameter = null;
            if (!useVrChatParameterSemantics) return false;
            parameter = new AnimatorControllerParameter
            {
                name = parameterName,
                type = AnimatorControllerParameterType.Float,
            };
            switch (parameterName)
            {
                case "IsLocal":
                case "PreviewMode":
                case "Grounded":
                case "Seated":
                case "AFK":
                case "MuteSelf":
                case "InStation":
                case "Earmuffs":
                case "IsOnFriendsList":
                case "IsAnimatorEnabled":
                case "ScaleModified":
                    parameter.type = AnimatorControllerParameterType.Bool;
                    return true;
                case "Viseme":
                case "GestureLeft":
                case "GestureRight":
                case "TrackingType":
                case "VRMode":
                case "AvatarVersion":
                    parameter.type = AnimatorControllerParameterType.Int;
                    return true;
                case "Voice":
                case "GestureLeftWeight":
                case "GestureRightWeight":
                case "AngularY":
                case "VelocityX":
                case "VelocityY":
                case "VelocityZ":
                case "VelocityMagnitude":
                case "Upright":
                case "ScaleFactor":
                case "ScaleFactorInverse":
                case "EyeHeightAsMeters":
                case "EyeHeightAsPercent":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildBooleanTruthTable(
            IReadOnlyList<VirtualBranch> branches,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (branches.Any(branch => branch.Conditions.Any(condition => !condition.IsBool)))
                return false;
            var parameters = branches.SelectMany(branch => branch.Conditions)
                .Select(condition => condition.Parameter).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (parameters.Count == 0 || parameters.Count > 30 ||
                (1L << parameters.Count) > Math.Max(2, options.MaxBooleanTruthTableEntries))
                return false;

            var table = new Dictionary<int, VirtualMotionRef>();
            var count = 1 << parameters.Count;
            for (var mask = 0; mask < count; mask++)
            {
                var branch = branches.OrderBy(item => item.Order).FirstOrDefault(item => item.Conditions.All(condition =>
                {
                    var value = (mask & (1 << parameters.IndexOf(condition.Parameter))) != 0 ? 1f : 0f;
                    return condition.Matches(value);
                }));
                if (branch == null)
                    return false;
                table[mask] = branch.Motion;
            }

            plan = new VirtualPlan
            {
                Branches = branches.ToList(),
                BooleanParameters = parameters,
                TruthTable = table,
            };
            return true;
        }

        private static bool TryBuildNumericSelector(
            IReadOnlyList<VirtualBranch> branches,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (branches.Any(branch => branch.Conditions.Count != 1))
                return false;
            var parameters = branches.Select(branch => branch.Conditions[0].Parameter)
                .Distinct(StringComparer.Ordinal).ToList();
            if (parameters.Count != 1 || branches.Any(branch => branch.Conditions[0].IsBool))
                return false;

            var prototype = branches[0].Conditions[0];
            if (branches.Any(branch => branch.Conditions[0].Type != prototype.Type)) return false;
            var values = new SortedSet<float>();
            var approximate = false;
            foreach (var branch in branches)
            {
                var threshold = branch.Conditions[0].Threshold;
                if (prototype.IsInt)
                {
                    var value = Mathf.RoundToInt(threshold);
                    values.Add(value - 1);
                    values.Add(value);
                    values.Add(value + 1);
                }
                else
                {
                    if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(threshold,
                        options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var isApproximate))
                        return false;
                    approximate |= isApproximate;
                }
            }
            if (values.Count > Math.Max(3, options.MaxNumericSelectorSamples))
                return false;

            var samples = new List<(float Value, VirtualMotionRef Motion)>();
            foreach (var value in values)
            {
                var branch = branches.OrderBy(item => item.Order)
                    .FirstOrDefault(item => item.Conditions[0].Matches(value));
                // Approximation may interpolate a threshold, but cannot discard
                // state history when an input value has no matching transition.
                if (branch == null)
                    return false;
                samples.Add((value, branch.Motion));
            }

            plan = new VirtualPlan
            {
                Branches = branches.ToList(),
                SelectorParameter = prototype.Parameter,
                SelectorParameterType = prototype.Type,
                SelectorSamples = samples,
                SelectorIsApproximate = approximate,
            };
            return true;
        }

        private static VirtualMotion BuildPlanMotion(
            VirtualAnimatorController controller,
            VirtualPlan plan,
            VirtualClip emptyClip)
        {
            if (plan.IsConstant)
                return plan.ConstantMotion.Motion ?? emptyClip;
            if (!string.IsNullOrWhiteSpace(plan.SelectorParameter))
            {
                var signatures = plan.SelectorSamples.Select(sample => BuildVirtualMotionSignature(sample.Motion)).ToList();
                if (signatures.Count > 0 && signatures.All(signature => signature == signatures[0]))
                    return PreserveTruthNodeSettings(new VirtualTruthNode {
                        Motion = plan.SelectorSamples[0].Motion.Motion ?? emptyClip,
                        LeafSettings = plan.SelectorSamples[0].Motion,
                    }, plan.SelectorParameter);
                var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(signatures);
                var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "NumericSelector_" + plan.SelectorParameter);
                tree.BlendType = BlendTreeType.Simple1D;
                tree.BlendParameter = plan.SelectorParameter;
                tree.UseAutomaticThresholds = false;
                tree.Children = retained.Select(index => plan.SelectorSamples[index]).Select(sample => new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = sample.Motion.Motion ?? emptyClip,
                    Threshold = sample.Value,
                    TimeScale = sample.Motion.Speed,
                    CycleOffset = sample.Motion.CycleOffset,
                    Mirror = sample.Motion.Mirror,
                }).ToImmutableList();
                return tree;
            }
            if (plan.DecisionDimensions != null)
                return BuildVirtualFiniteDecisionTree(controller, plan, emptyClip, 0, 0, 1,
                    new Dictionary<string, VirtualTruthNode>(StringComparer.Ordinal));
            if (plan.BooleanParameters == null || plan.BooleanParameters.Count > 30)
                throw new InvalidOperationException("Bool 决策树参数超过 30 位，拒绝生成可能溢出的虚拟真值树。");
            return PreserveTruthNodeSettings(BuildTruthTree(controller, plan, emptyClip, 0, 0,
                new Dictionary<string, VirtualTruthNode>(StringComparer.Ordinal)), plan.BooleanParameters.FirstOrDefault());
        }

        private static VirtualMotion BuildVirtualFiniteDecisionTree(
            VirtualAnimatorController controller,
            VirtualPlan plan,
            VirtualClip emptyClip,
            int depth,
            int key,
            int stride,
            IDictionary<string, VirtualTruthNode> memo)
        {
            return PreserveTruthNodeSettings(BuildVirtualFiniteDecisionNode(controller, plan, emptyClip, depth, key, stride, memo),
                plan.DecisionDimensions.FirstOrDefault()?.Parameter);
        }

        private static VirtualMotion PreserveTruthNodeSettings(VirtualTruthNode node, string parameter)
        {
            if (node.LeafSettings == null || node.LeafSettings.Speed == 1f &&
                node.LeafSettings.CycleOffset == 0f && !node.LeafSettings.Mirror)
                return node.Motion;
            var wrapper = VirtualBlendTree.Create(GeneratedAssetPrefix + "LeafSettings");
            wrapper.BlendType = BlendTreeType.Simple1D;
            wrapper.BlendParameter = parameter;
            wrapper.UseAutomaticThresholds = false;
            wrapper.Children = ImmutableList.Create(ToVirtualTruthChild(0f, node));
            return wrapper;
        }

        private static VirtualTruthNode BuildVirtualFiniteDecisionNode(
            VirtualAnimatorController controller,
            VirtualPlan plan,
            VirtualClip emptyClip,
            int depth,
            int key,
            int stride,
            IDictionary<string, VirtualTruthNode> memo)
        {
            if (depth >= plan.DecisionDimensions.Count)
            {
                plan.DecisionTable.TryGetValue(key, out var leaf);
                return new VirtualTruthNode
                {
                    Motion = leaf?.Motion ?? emptyClip,
                    LeafSettings = leaf,
                    Signature = BuildVirtualMotionSignature(leaf),
                };
            }

            var dimension = plan.DecisionDimensions[depth];
            var childStride = stride * dimension.Values.Count;
            var childNodes = dimension.Values.Select((value, index) =>
                BuildVirtualFiniteDecisionNode(controller, plan, emptyClip, depth + 1,
                    key + index * stride, childStride, memo)).ToList();
            if (childNodes.All(node => string.Equals(node.Signature, childNodes[0].Signature, StringComparison.Ordinal)))
                return childNodes[0];

            var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(childNodes.Select(node => node.Signature).ToList());
            var memoKey = AnimatorOptimizerSemanticUtility.DecisionNodeKey(dimension.Parameter,
                retained.Select(index => dimension.Values[index]), retained.Select(index => childNodes[index].Signature));
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;

            var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "FiniteTree_" + depth + "_" + key);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = dimension.Parameter;
            tree.UseAutomaticThresholds = false;
            tree.Children = retained.Select(index => new VirtualBlendTree.VirtualChildMotion
            {
                Motion = childNodes[index].Motion,
                Threshold = dimension.Values[index],
                TimeScale = childNodes[index].LeafSettings == null ? 1f : childNodes[index].LeafSettings.Speed,
                CycleOffset = childNodes[index].LeafSettings == null ? 0f : childNodes[index].LeafSettings.CycleOffset,
                Mirror = childNodes[index].LeafSettings != null && childNodes[index].LeafSettings.Mirror,
            }).ToImmutableList();
            var result = new VirtualTruthNode
            {
                Motion = tree,
                Signature = memoKey,
            };
            memo[memoKey] = result;
            return result;
        }

        private sealed class VirtualTruthNode
        {
            public VirtualMotion Motion;
            public VirtualMotionRef LeafSettings;
            public string Signature;
        }

        private static VirtualTruthNode BuildTruthTree(
            VirtualAnimatorController controller,
            VirtualPlan plan,
            VirtualClip emptyClip,
            int depth,
            int mask,
            IDictionary<string, VirtualTruthNode> memo)
        {
            if (depth >= plan.BooleanParameters.Count)
            {
                plan.TruthTable.TryGetValue(mask, out var value);
                return new VirtualTruthNode { Motion = value?.Motion ?? emptyClip,
                    LeafSettings = value, Signature = BuildVirtualMotionSignature(value) };
            }
            if (depth + 1 == plan.BooleanParameters.Count)
            {
                plan.TruthTable.TryGetValue(mask, out var falseMotion);
                plan.TruthTable.TryGetValue(mask | (1 << depth), out var trueMotion);
                var falseSignature = BuildVirtualMotionSignature(falseMotion);
                var trueSignature = BuildVirtualMotionSignature(trueMotion);
                if (string.Equals(falseSignature, trueSignature, StringComparison.Ordinal))
                {
                    return new VirtualTruthNode
                    {
                        Motion = falseMotion?.Motion ?? emptyClip,
                        LeafSettings = falseMotion,
                        Signature = falseSignature,
                    };
                }
                var leafParameter = plan.BooleanParameters[depth];
                var leafMemoKey = "bool:" + leafParameter.Length + ":" + leafParameter + ":" +
                    falseSignature.Length + ":" + falseSignature + ":" + trueSignature;
                if (memo.TryGetValue(leafMemoKey, out var leafCached))
                    return leafCached;
                var leafTree = VirtualBlendTree.Create(GeneratedAssetPrefix + "BoolTree_" + depth + "_" + mask);
                leafTree.BlendType = BlendTreeType.Simple1D;
                leafTree.BlendParameter = plan.BooleanParameters[depth];
                leafTree.UseAutomaticThresholds = false;
                leafTree.Children = ImmutableList.Create(
                    ToVirtualChild(0f, falseMotion, emptyClip),
                    ToVirtualChild(1f, trueMotion, emptyClip));
                var leafResult = new VirtualTruthNode
                {
                    Motion = leafTree,
                    Signature = leafMemoKey,
                };
                memo[leafMemoKey] = leafResult;
                return leafResult;
            }

            var falseTree = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask, memo);
            var trueTree = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask | (1 << depth), memo);
            if (string.Equals(falseTree.Signature, trueTree.Signature, StringComparison.Ordinal))
                return falseTree;

            var parameter = plan.BooleanParameters[depth];
            var memoKey = "bool:" + parameter.Length + ":" + parameter + ":" +
                falseTree.Signature.Length + ":" + falseTree.Signature + ":" + trueTree.Signature;
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;

            var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "BoolTree_" + depth + "_" + mask);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = plan.BooleanParameters[depth];
            tree.UseAutomaticThresholds = false;

            tree.Children = ImmutableList.Create(
                ToVirtualTruthChild(0f, falseTree),
                ToVirtualTruthChild(1f, trueTree));
            var result = new VirtualTruthNode
            {
                Motion = tree,
                Signature = memoKey,
            };
            memo[memoKey] = result;
            return result;
        }

        private static string BuildVirtualMotionSignature(VirtualMotionRef motion)
        {
            if (motion == null)
                return "null";
            if (motion.Bake != null) return motion.Bake.Key;
            if (motion.Speed == 1f && motion.CycleOffset == 0f && !motion.Mirror &&
                IsPropertyOnlyVirtualMotion(motion.Motion) &&
                TryBuildVirtualStaticObservableMotionSignature(motion.Motion, out var staticKey))
                return staticKey;
            return AnimatorOptimizerSemanticUtility.ReferenceKey(motion.Motion) + ":" +
                   motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.Mirror;
        }

        private static VirtualBlendTree.VirtualChildMotion ToVirtualTruthChild(
            float threshold,
            VirtualTruthNode node)
        {
            return new VirtualBlendTree.VirtualChildMotion
            {
                Motion = node?.Motion,
                Threshold = threshold,
                TimeScale = node?.LeafSettings == null ? 1f : node.LeafSettings.Speed,
                CycleOffset = node?.LeafSettings == null ? 0f : node.LeafSettings.CycleOffset,
                Mirror = node?.LeafSettings != null && node.LeafSettings.Mirror,
            };
        }

        private static VirtualBlendTree.VirtualChildMotion ToVirtualChild(
            float threshold,
            VirtualMotionRef motion,
            VirtualClip emptyClip)
        {
            return new VirtualBlendTree.VirtualChildMotion
            {
                Motion = motion?.Motion ?? emptyClip,
                Threshold = threshold,
                TimeScale = motion == null ? 1f : motion.Speed,
                CycleOffset = motion == null ? 0f : motion.CycleOffset,
                Mirror = motion != null && motion.Mirror,
            };
        }

        private static bool ValidateState(VirtualState state, bool allowTransitions = false)
        {
            if (state == null ||
                !IsFinite(state.Speed) || !IsFinite(state.CycleOffset) ||
                state.Motion != null && ContainsPotentialProxyVirtualMotion(state.Motion, new HashSet<VirtualMotion>()) ||
                state.Motion != null && ContainsVirtualAnimationEvents(state.Motion, new HashSet<VirtualMotion>()) ||
                state.Behaviours.Count > 0 || !IsWriteDefaultsAcceptable(state) ||
                state.IKOnFeet ||
                (state.SpeedParameter != null || state.CycleOffsetParameter != null || state.TimeParameter != null) &&
                    !IsTimeIndependentMotion(state.Motion) ||
                state.MirrorParameter != null && !IsPropertyOnlyVirtualMotion(state.Motion))
                return false;
            if (!allowTransitions && GetEffectiveVirtualTransitions(state.Transitions).Count > 0)
                return false;
            return state.Motion == null || state.Motion is VirtualClip || state.Motion is VirtualBlendTree;
        }

        // The scope is restored after each candidate, including failed analysis.
        private sealed class VirtualLayerScope
        {
            public AnimatorOptimizerOptions Options;
            public VirtualAnimatorController Controller;
            public HashSet<BindingKey> LayerBindings;
            public bool LayerBindingsComplete;
            public bool HasExitTime;
            public readonly Dictionary<VirtualState, bool> WriteDefaultsDecisions = new Dictionary<VirtualState, bool>();
        }

        [ThreadStatic]
        private static VirtualLayerScope currentLayerScope;

        private static VirtualLayerScope BeginLayerScope(VirtualAnimatorController controller, AnimatorOptimizerOptions options)
        {
            var scope = new VirtualLayerScope
            {
                Options = options,
                Controller = controller,
                LayerBindings = new HashSet<BindingKey>(),
            };
            scope.LayerBindingsComplete = true;
            var visited = new HashSet<VirtualStateMachine>();
            foreach (var layer in controller.Layers)
            {
                if (layer != null && layer.SyncedLayerIndex >= 0)
                    scope.LayerBindingsComplete = false;
                scope.LayerBindingsComplete &= TryCollectStateMachineMotionBindings(
                    layer?.StateMachine, scope.LayerBindings, visited);
            }
            return scope;
        }

        private static bool TryCollectStateMachineMotionBindings(
            VirtualStateMachine stateMachine,
            HashSet<BindingKey> bindings,
            ISet<VirtualStateMachine> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine))
                return true;
            var complete = true;
            foreach (var child in stateMachine.States)
            {
                if (child.State != null)
                    complete &= TryCollectMotionBindingKeys(child.State.Motion, bindings);
            }
            foreach (var child in stateMachine.StateMachines)
                complete &= TryCollectStateMachineMotionBindings(child.StateMachine, bindings, visited);
            return complete;
        }

        // WD coverage includes controller-local parameter bindings.
        private static bool TryCollectMotionBindingKeys(VirtualMotion motion, HashSet<BindingKey> bindings)
        {
            if (motion == null)
                return true;
            if (motion is VirtualClip clip)
            {
                if (!HasInspectableVirtualBindings(clip))
                    return false;
                foreach (var binding in clip.GetFloatCurveBindings())
                    bindings.Add(new BindingKey(binding));
                foreach (var binding in clip.GetObjectCurveBindings())
                    bindings.Add(new BindingKey(binding));
                return true;
            }
            if (motion is VirtualBlendTree tree)
                return tree.Children.All(child => TryCollectMotionBindingKeys(child.Motion, bindings));
            return false;
        }

        private static bool IsWriteDefaultsAcceptable(VirtualState state)
        {
            if (state == null || state.WriteDefaultValues)
                return true;
            var scope = currentLayerScope;
            var policy = scope?.Options?.WriteDefaultsPolicy ??
                         AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict;
            if (policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict || scope == null)
                return false;
            if (scope.WriteDefaultsDecisions.TryGetValue(state, out var cached))
                return cached;

            var accepted = false;
            if (scope.LayerBindingsComplete)
            {
                var stateBindings = new HashSet<BindingKey>();
                if (TryCollectVirtualGuaranteedBindings(state.Motion, stateBindings))
                {
                    var covered = scope.LayerBindings.All(stateBindings.Contains);
                    accepted = covered ||
                               policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn;
                    if (accepted && !covered && scope.Options.VerboseLogging)
                    {
                        Debug.Log($"[AnimatorOptimizer] NDMF treats WD Off state '{state.Name}' as WD On; " +
                                  $"{scope.LayerBindings.Count(binding => !stateBindings.Contains(binding))} controller binding(s) are not guaranteed covered; default-write effects may change.");
                    }
                }
            }
            scope.WriteDefaultsDecisions[state] = accepted;
            return accepted;
        }

        private static bool TryCollectVirtualGuaranteedBindings(VirtualMotion motion, HashSet<BindingKey> bindings)
        {
            if (motion == null)
                return true;
            if (motion is VirtualClip)
                return TryCollectMotionBindingKeys(motion, bindings);
            if (!(motion is VirtualBlendTree tree))
                return false;
            if (tree.BlendType != BlendTreeType.Simple1D)
                return true;
            HashSet<BindingKey> common = null;
            foreach (var child in tree.Children)
            {
                var childBindings = new HashSet<BindingKey>();
                if (!TryCollectVirtualGuaranteedBindings(child.Motion, childBindings))
                    return false;
                if (common == null)
                    common = childBindings;
                else
                    common.IntersectWith(childBindings);
            }
            if (common != null)
                bindings.UnionWith(common);
            return true;
        }

        private static bool IsValidVirtualGraph(VirtualNode node, ISet<VirtualNode> active, ISet<VirtualNode> done)
        {
            if (node == null || done.Contains(node))
                return true;
            if (active.Count >= 128 || !active.Add(node))
                return false;
            if (node is VirtualStateMachine machine)
            {
                foreach (var child in machine.States)
                    if (child.State != null &&
                        (!IsFinite(child.State.Speed) || !IsFinite(child.State.CycleOffset)) ||
                        !IsValidVirtualGraph(child.State?.Motion, active, done))
                        return false;
                foreach (var child in machine.StateMachines)
                    if (!IsValidVirtualGraph(child.StateMachine, active, done))
                        return false;
            }
            else if (node is VirtualBlendTree tree)
            {
                foreach (var child in tree.Children)
                    if (!IsFinite(child.TimeScale) || !IsFinite(child.CycleOffset) ||
                        !IsFinite(child.Threshold) || !IsFinite(child.Position.x) || !IsFinite(child.Position.y) ||
                        !IsValidVirtualGraph(child.Motion, active, done))
                        return false;
            }
            active.Remove(node);
            done.Add(node);
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFloatNeighborhoodRepresentable(float threshold, float epsilon)
        {
            var lower = threshold - epsilon;
            var upper = threshold + epsilon;
            return IsFinite(lower) && IsFinite(upper) && lower < threshold && upper > threshold;
        }

        private static bool HasBlockingVirtualTransitionTiming(VirtualStateTransition transition)
        {
            if (transition == null)
                return false;
            if (!IsFinite(transition.Duration) || !IsFinite(transition.Offset) ||
                transition.ExitTime.HasValue && !IsFinite(transition.ExitTime.Value))
                return true;
            var irrelevantOffset = currentLayerScope != null && !currentLayerScope.HasExitTime &&
                transition.DestinationState != null && IsTimeIndependentMotion(transition.DestinationState.Motion);
            var timed = transition.ExitTime.HasValue ||
                        transition.Duration != 0f || transition.Offset != 0f && !irrelevantOffset;
            if (!timed)
                return false;
            return !(currentLayerScope?.Options?.IgnoreTransitionTimingForStaticMotions ?? false);
        }

        private static bool HasEffectiveVirtualExitTime(VirtualStateMachine machine)
        {
            return machine != null &&
                (GetEffectiveVirtualTransitions(machine.AnyStateTransitions).Any(transition => transition.ExitTime.HasValue) ||
                 machine.States.Any(child => child.State != null &&
                    GetEffectiveVirtualTransitions(child.State.Transitions).Any(transition => transition.ExitTime.HasValue)) ||
                 machine.StateMachines.Any(child => HasEffectiveVirtualExitTime(child.StateMachine)));
        }

        private static bool ContainsVirtualAnimationEvents(
            VirtualMotion motion,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return false;
            if (motion is VirtualClip clip)
                return HasVirtualAnimationEvents(clip);
            return motion is VirtualBlendTree tree && tree.Children.Any(child =>
                ContainsVirtualAnimationEvents(child.Motion, visited));
        }

        private static bool ContainsPotentialProxyVirtualMotion(
            VirtualMotion motion,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return false;
            if (motion is VirtualClip clip)
                return clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip);
            return motion is VirtualBlendTree tree && tree.Children.Any(child =>
                ContainsPotentialProxyVirtualMotion(child.Motion, visited));
        }

        private static VirtualMotionRef ToMotionRef(VirtualState state)
        {
            var independent = IsTimeIndependentMotion(state?.Motion);
            return new VirtualMotionRef
            {
                Motion = NormalizeVirtualSingleton(state?.Motion),
                Speed = state == null || independent ? 1f : state.Speed,
                CycleOffset = state == null || independent ? 0f : state.CycleOffset,
                Mirror = state != null && state.Mirror && !IsPropertyOnlyVirtualMotion(state.Motion),
            };
        }

        private static bool TryCollectLayerBindings(VirtualLayer layer, HashSet<BindingKey> bindings)
        {
            if (layer == null || layer.StateMachine == null || layer.SyncedLayerIndex >= 0 ||
                layer.StateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)))
                return false;

            return TryCollectVirtualMachineBindings(layer.StateMachine, bindings);
        }

        private static bool TryCollectVirtualMachineBindings(VirtualStateMachine machine, HashSet<BindingKey> bindings)
        {
            if (machine == null || machine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)))
                return false;
            foreach (var child in machine.States)
            {
                if (child.State == null || child.State.Behaviours.Count > 0 ||
                    !TryCollectMotionBindings(child.State.Motion, bindings))
                    return false;
            }
            return machine.StateMachines.All(child => TryCollectVirtualMachineBindings(child.StateMachine, bindings));
        }

        private static bool IsPreservableLayerBehaviour(StateMachineBehaviour behaviour)
        {
            return behaviour != null && string.Equals(
                behaviour.GetType().FullName, "nadena.dev.modular_avatar.core.ModularAvatarMMDLayerControl", StringComparison.Ordinal);
        }

        private static bool CanPreserveVirtualLayerBehaviours(
            IReadOnlyList<VirtualCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return true;
            var keys = candidates.Select(candidate =>
                    AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(candidate.Layer.StateMachine.Behaviours, out var key) ? key : null)
                .ToList();
            return keys.All(key => key != null && string.Equals(key, keys[0], StringComparison.Ordinal));
        }

        private static ImmutableList<StateMachineBehaviour> CloneVirtualLayerBehaviours(
            IReadOnlyList<VirtualCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return ImmutableList<StateMachineBehaviour>.Empty;
            if (!CanPreserveVirtualLayerBehaviours(candidates))
                throw new InvalidOperationException("Cannot copy source layer markers without losing Behaviour data.");
            var source = candidates[0].Layer.StateMachine.Behaviours;
            var result = ImmutableList<StateMachineBehaviour>.Empty;
            foreach (var behaviour in source)
            {
                if (!IsPreservableLayerBehaviour(behaviour))
                    continue;
                var clone = UnityEngine.Object.Instantiate(behaviour);
                result = result.Add(clone);
            }
            return result;
        }

        private static bool TryCollectMotionBindings(VirtualMotion motion, HashSet<BindingKey> bindings)
        {
            if (motion == null)
                return true;
            if (motion is VirtualClip clip)
            {
                if (!HasInspectableVirtualBindings(clip)) return false;
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    bindings.Add(new BindingKey(binding));
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    bindings.Add(new BindingKey(binding));
                }
                return true;
            }
            if (motion is VirtualBlendTree tree)
                return tree.Children.All(child => TryCollectMotionBindings(child.Motion, bindings));
            return false;
        }

        private static bool ContainsVirtualAnimatorCurves(VirtualMotion motion, ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion)) return false;
            if (motion is VirtualClip clip)
                return clip.GetFloatCurveBindings().Concat(clip.GetObjectCurveBindings())
                    .Any(binding => binding.type == typeof(Animator));
            return motion is VirtualBlendTree tree && tree.Children.Any(child =>
                ContainsVirtualAnimatorCurves(child.Motion, visited));
        }

        private static bool ContainsUnsupportedVirtualCurveData(VirtualMotion motion, ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion)) return false;
            if (motion is VirtualClip clip)
                return clip.GetFloatCurveBindings().Any(binding => AnimatorOptimizerSemanticUtility.IsRotationBinding(binding) ||
                    !AnimatorOptimizerSemanticUtility.IsFiniteCurveData(clip.GetFloatCurve(binding))) ||
                    clip.GetObjectCurveBindings().Any(binding => clip.GetObjectCurve(binding) == null ||
                        clip.GetObjectCurve(binding).Any(key => !IsFinite(key.time)));
            return motion is VirtualBlendTree tree && tree.Children.Any(child => ContainsUnsupportedVirtualCurveData(child.Motion, visited));
        }

        private static List<string> UpgradeParameters(
            VirtualAnimatorController controller,
            IEnumerable<VirtualPlan> plans,
            AnimatorOptimizerOptions options,
            ISet<VirtualLayer> removedLayers)
        {
            var names = plans.SelectMany(plan => plan.BooleanParameters ?? Enumerable.Empty<string>())
                .Concat(plans.Where(plan => !string.IsNullOrWhiteSpace(plan.SelectorParameter))
                    .Select(plan => plan.SelectorParameter))
                .Concat(plans.SelectMany(plan => plan.DecisionDimensions ?? Enumerable.Empty<VirtualDecisionDimension>())
                    .Select(dimension => dimension.Parameter))
                .Concat(plans.SelectMany(plan => plan.ReferencedParameters ?? new HashSet<string>(StringComparer.Ordinal))
                    .Where(name => !IsVrcGlobalParameter(name, options.UseVrChatParameterSemantics)))
                .Distinct(StringComparer.Ordinal).ToList();
            var animated = CollectVirtualAnimatedAnimatorParameterNames(controller);
            if (names.Any(name => VirtualParameterNeedsUpgrade(controller, name, options) &&
                (animated.Contains(name) || HasPotentialVirtualBehaviourReference(controller, name))))
                return null;
            var retainedConditions = controller.Layers.Where(layer => !removedLayers.Contains(layer))
                .SelectMany(layer => EnumerateVirtualTransitionConditions(layer.StateMachine, new HashSet<VirtualStateMachine>()))
                .ToLookup(condition => condition.parameter, StringComparer.Ordinal);
            if (names.Any(name => VirtualParameterNeedsUpgrade(controller, name, options) &&
                retainedConditions[name].Any(condition =>
                    !CanPreserveVirtualConditionAfterFloatUpgrade(condition, controller.Parameters[name].type))))
                return null;
            var upgraded = new List<string>();
            var bools = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (IsVrcGlobalParameter(name, options.UseVrChatParameterSemantics))
                    continue;
                if (!controller.Parameters.TryGetValue(name, out var parameter))
                    continue;
                if (parameter.type == AnimatorControllerParameterType.Bool && options.UpgradeBoolParametersToFloat)
                {
                    controller.Parameters = controller.Parameters.SetItem(name, new AnimatorControllerParameter
                    {
                        name = name,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = parameter.defaultBool ? 1f : 0f,
                    });
                    upgraded.Add(name);
                    bools.Add(name);
                }
                else if (parameter.type == AnimatorControllerParameterType.Int && options.UpgradeIntParametersToFloat)
                {
                    controller.Parameters = controller.Parameters.SetItem(name, new AnimatorControllerParameter
                    {
                        name = name,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = parameter.defaultInt,
                    });
                    upgraded.Add(name);
                }
            }
            if (bools.Count > 0)
                RewriteVirtualConditions(controller, bools);
            return upgraded;
        }

        private static bool CanPreserveVirtualConditionAfterFloatUpgrade(
            AnimatorCondition condition, AnimatorControllerParameterType type)
        {
            if (type == AnimatorControllerParameterType.Bool)
                return condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot ||
                    (condition.mode == AnimatorConditionMode.Equals || condition.mode == AnimatorConditionMode.NotEqual) &&
                        (condition.threshold == 0f || condition.threshold == 1f);
            if (type == AnimatorControllerParameterType.Int)
                return (condition.mode == AnimatorConditionMode.Greater || condition.mode == AnimatorConditionMode.Less) &&
                    IsFinite(condition.threshold) && Math.Abs((double)condition.threshold) <= 16777214d &&
                    condition.threshold == Mathf.Round(condition.threshold);
            return false;
        }

        private static IEnumerable<AnimatorCondition> EnumerateVirtualTransitionConditions(
            VirtualStateMachine stateMachine, ISet<VirtualStateMachine> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine)) yield break;
            var transitions = stateMachine.AnyStateTransitions.Cast<VirtualTransitionBase>()
                .Concat(stateMachine.EntryTransitions)
                .Concat(stateMachine.States.Where(child => child.State != null)
                    .SelectMany(child => child.State.Transitions));
            foreach (var child in stateMachine.StateMachines)
            {
                if (child.StateMachine == null) continue;
                if (stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var outgoing))
                    transitions = transitions.Concat(outgoing);
                foreach (var condition in EnumerateVirtualTransitionConditions(child.StateMachine, visited))
                    yield return condition;
            }
            foreach (var transition in transitions.Where(transition => transition != null))
                foreach (var condition in transition.Conditions)
                    yield return condition;
        }

        private static bool VirtualParameterNeedsUpgrade(
            VirtualAnimatorController controller,
            string name,
            AnimatorOptimizerOptions options)
        {
            if (controller == null || IsVrcGlobalParameter(name, options.UseVrChatParameterSemantics) || !controller.Parameters.TryGetValue(name, out var parameter))
                return false;
            return parameter.type == AnimatorControllerParameterType.Bool && options.UpgradeBoolParametersToFloat ||
                   parameter.type == AnimatorControllerParameterType.Int && options.UpgradeIntParametersToFloat;
        }

        private static bool HasPotentialVirtualBehaviourReference(
            VirtualAnimatorController controller,
            string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
                return false;
            return controller.Layers.Any(layer =>
                VirtualStateMachineHasPotentialBehaviourReference(layer.StateMachine, parameterName) ||
                layer.SyncedLayerBehaviourOverrides.Values.SelectMany(list => list)
                    .Any(behaviour => BehaviourMayReferenceVirtualParameter(behaviour, parameterName)));
        }

        private static bool VirtualStateMachineHasPotentialBehaviourReference(
            VirtualStateMachine stateMachine,
            string parameterName)
        {
            if (stateMachine == null)
                return false;
            if (stateMachine.Behaviours.Any(behaviour =>
                    BehaviourMayReferenceVirtualParameter(behaviour, parameterName)))
                return true;
            foreach (var child in stateMachine.States)
            {
                if (child.State != null && child.State.Behaviours.Any(behaviour =>
                        BehaviourMayReferenceVirtualParameter(behaviour, parameterName)))
                    return true;
            }
            return stateMachine.StateMachines.Any(child =>
                VirtualStateMachineHasPotentialBehaviourReference(child.StateMachine, parameterName));
        }

        private static bool BehaviourMayReferenceVirtualParameter(
            StateMachineBehaviour behaviour,
            string parameterName)
        {
            if (behaviour == null)
                return true;
            try
            {
                var serializedObject = new SerializedObject(behaviour);
                var iterator = serializedObject.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.String &&
                        string.Equals(iterator.stringValue, parameterName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                return true;
            }
            return false;
        }

        private static void RewriteVirtualConditions(VirtualAnimatorController controller, HashSet<string> bools)
        {
            foreach (var layer in controller.Layers)
                RewriteVirtualStateMachine(layer.StateMachine, bools);
        }

        private static void RewriteVirtualStateMachine(VirtualStateMachine stateMachine, HashSet<string> bools)
        {
            if (stateMachine == null)
                return;
            foreach (var transition in stateMachine.AnyStateTransitions)
                RewriteVirtualTransition(transition, bools);
            foreach (var transition in stateMachine.EntryTransitions)
                RewriteVirtualTransition(transition, bools);
            foreach (var state in stateMachine.States)
            {
                if (state.State == null)
                    continue;
                foreach (var transition in state.State.Transitions)
                    RewriteVirtualTransition(transition, bools);
            }
            foreach (var child in stateMachine.StateMachines)
            {
                RewriteVirtualStateMachine(child.StateMachine, bools);
                if (stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                {
                    foreach (var transition in transitions)
                        RewriteVirtualTransition(transition, bools);
                }
            }
        }

        private static void RewriteVirtualTransition(VirtualTransitionBase transition, HashSet<string> bools)
        {
            if (transition == null)
                return;
            transition.Conditions = transition.Conditions.Select(condition =>
            {
                if (bools.Contains(condition.parameter))
                {
                    if (condition.mode == AnimatorConditionMode.If ||
                        condition.mode == AnimatorConditionMode.Equals && condition.threshold == 1f ||
                        condition.mode == AnimatorConditionMode.NotEqual && condition.threshold == 0f)
                    {
                        condition.mode = AnimatorConditionMode.Greater;
                        condition.threshold = 0.5f;
                    }
                    else if (condition.mode == AnimatorConditionMode.IfNot ||
                             condition.mode == AnimatorConditionMode.Equals && condition.threshold == 0f ||
                             condition.mode == AnimatorConditionMode.NotEqual && condition.threshold == 1f)
                    {
                        condition.mode = AnimatorConditionMode.Less;
                        condition.threshold = 0.5f;
                    }
                }
                return condition;
            }).ToImmutableList();
        }

        private static string AddWeightParameter(
            VirtualAnimatorController controller,
            AnimatorOptimizerOptions options)
        {
            if (options != null && options.NormalizeAlwaysOneParameters &&
                TryFindVirtualAlwaysOne(controller, out var existingAlwaysOne))
                return existingAlwaysOne;
            var prefix = string.IsNullOrWhiteSpace(options?.WeightParameterPrefix)
                ? WeightParameterPrefix : options.WeightParameterPrefix.Trim();
            var name = AnimatorOptimizerSemanticUtility.AllocateParameterName(prefix, CollectVirtualReservedParameterNames(controller), options?.UseVrChatParameterSemantics == true);
            controller.Parameters = controller.Parameters.SetItem(name, new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            return name;
        }

        private static HashSet<string> CollectVirtualReservedParameterNames(VirtualAnimatorController controller)
        {
            var names = new HashSet<string>(controller.Parameters.Keys, StringComparer.Ordinal);
            void Add(string name) { if (!string.IsNullOrWhiteSpace(name)) names.Add(name); }
            foreach (var node in controller.AllReachableNodes())
            {
                if (node is VirtualBlendTree tree)
                {
                    Add(tree.BlendParameter);
                    Add(tree.BlendParameterY);
                    foreach (var child in tree.Children) Add(child.DirectBlendParameter);
                }
                else if (node is VirtualClip clip)
                {
                    foreach (var binding in clip.GetFloatCurveBindings().Concat(clip.GetObjectCurveBindings()))
                        if (binding.type == typeof(Animator)) Add(binding.propertyName);
                }
                else if (node is VirtualState state)
                {
                    Add(state.SpeedParameter); Add(state.CycleOffsetParameter); Add(state.TimeParameter); Add(state.MirrorParameter);
                    foreach (var behaviour in state.Behaviours) AnimatorOptimizerSemanticUtility.CollectSerializedStrings(behaviour, names);
                }
                else if (node is VirtualTransitionBase transition)
                    foreach (var condition in transition.Conditions) Add(condition.parameter);
                else if (node is VirtualStateMachine machine)
                    foreach (var behaviour in machine.Behaviours) AnimatorOptimizerSemanticUtility.CollectSerializedStrings(behaviour, names);
                else if (node is VirtualLayer layer)
                    foreach (var behaviour in layer.SyncedLayerBehaviourOverrides.Values.SelectMany(list => list))
                        AnimatorOptimizerSemanticUtility.CollectSerializedStrings(behaviour, names);
            }
            return names;
        }

        private static bool TryFindVirtualAlwaysOne(
            VirtualAnimatorController controller,
            out string name)
        {
            name = null;
            var animated = CollectVirtualAnimatedAnimatorParameterNames(controller);
            var candidate = controller.Parameters
                .Where(pair => pair.Value.type == AnimatorControllerParameterType.Float &&
                    Mathf.Approximately(pair.Value.defaultFloat, 1f) &&
                    IsAlwaysOneName(pair.Key) &&
                    !animated.Contains(pair.Key) &&
                    !HasPotentialVirtualParameterDriver(controller, pair.Key) &&
                    !HasPotentialVirtualBehaviourReference(controller, pair.Key))
                .OrderBy(pair => IsCanonicalAlwaysOneName(pair.Key) ? 0 : 1)
                .ThenBy(pair => pair.Key.Length)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            name = candidate;
            return true;
        }

        private static HashSet<string> CollectVirtualAnimatedAnimatorParameterNames(
            VirtualAnimatorController controller)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (controller == null)
                return names;
            var layers = controller.Layers.ToList();
            var visited = new HashSet<VirtualMotion>();
            for (var index = 0; index < layers.Count; index++)
                CollectVirtualAnimatedAnimatorParameterNames(layers, index, names, visited);
            return names;
        }

        private static void CollectVirtualAnimatedAnimatorParameterNames(IReadOnlyList<VirtualLayer> layers, int index,
            ISet<string> names, ISet<VirtualMotion> visited)
        {
            var layer = layers[index];
            var chain = new HashSet<VirtualLayer>();
            while (layer != null && chain.Add(layer))
            {
                CollectVirtualAnimatedAnimatorParameterNames(layer.StateMachine, names, visited);
                foreach (var motion in layer.SyncedLayerMotionOverrides.Values)
                    CollectVirtualAnimatedAnimatorParameterNames(motion, names, visited);
                layer = layer.SyncedLayerIndex < 0 ? null : layers.FirstOrDefault(candidate =>
                    candidate?.VirtualLayerIndex == layer.SyncedLayerIndex);
            }
        }

        private static void CollectVirtualAnimatedAnimatorParameterNames(
            VirtualStateMachine stateMachine,
            ISet<string> names,
            ISet<VirtualMotion> visited)
        {
            if (stateMachine == null)
                return;
            foreach (var child in stateMachine.States)
            {
                if (child.State != null)
                    CollectVirtualAnimatedAnimatorParameterNames(child.State.Motion, names, visited);
            }
            foreach (var child in stateMachine.StateMachines)
                CollectVirtualAnimatedAnimatorParameterNames(child.StateMachine, names, visited);
        }

        private static void CollectVirtualAnimatedAnimatorParameterNames(
            VirtualMotion motion,
            ISet<string> names,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return;
            if (motion is VirtualClip clip)
            {
                foreach (var binding in clip.GetFloatCurveBindings().Concat(clip.GetObjectCurveBindings()))
                {
                    if (binding.type == typeof(Animator))
                        names.Add(binding.propertyName);
                }
                return;
            }
            if (motion is VirtualBlendTree tree)
            {
                foreach (var child in tree.Children)
                    CollectVirtualAnimatedAnimatorParameterNames(child.Motion, names, visited);
            }
        }

        private static bool HasPotentialVirtualParameterDriver(
            VirtualAnimatorController controller,
            string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
                return false;
            return controller.Layers.Any(layer =>
                VirtualStateMachineHasParameterDriver(layer.StateMachine, parameterName) ||
                layer.SyncedLayerBehaviourOverrides.Values.SelectMany(list => list)
                    .Any(behaviour => VirtualBehaviourReferencesParameter(behaviour, parameterName)));
        }

        private static bool VirtualStateMachineHasParameterDriver(
            VirtualStateMachine stateMachine,
            string parameterName)
        {
            if (stateMachine == null)
                return false;
            if (stateMachine.Behaviours.Any(behaviour =>
                    VirtualBehaviourReferencesParameter(behaviour, parameterName)))
                return true;
            foreach (var child in stateMachine.States)
            {
                if (child.State != null && child.State.Behaviours.Any(behaviour =>
                        VirtualBehaviourReferencesParameter(behaviour, parameterName)))
                    return true;
            }
            return stateMachine.StateMachines.Any(child =>
                VirtualStateMachineHasParameterDriver(child.StateMachine, parameterName));
        }

        private static bool VirtualBehaviourReferencesParameter(
            StateMachineBehaviour behaviour,
            string parameterName)
        {
            if (behaviour == null ||
                behaviour.GetType().Name.IndexOf("ParameterDriver", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            try
            {
                var serializedObject = new SerializedObject(behaviour);
                var parameters = serializedObject.FindProperty("parameters");
                if (parameters == null || !parameters.isArray)
                    return true;
                for (var index = 0; index < parameters.arraySize; index++)
                {
                    var name = parameters.GetArrayElementAtIndex(index).FindPropertyRelative("name");
                    if (name != null && string.Equals(name.stringValue, parameterName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                return true;
            }
            return false;
        }

        private static void NormalizeVirtualAlwaysOneParameters(VirtualAnimatorController controller)
        {
            if (!TryFindVirtualAlwaysOne(controller, out var canonical))
                return;
            var aliases = controller.Parameters.Keys
                .Where(name => !string.Equals(name, canonical, StringComparison.Ordinal) &&
                    controller.Parameters[name].type == AnimatorControllerParameterType.Float &&
                    Mathf.Approximately(controller.Parameters[name].defaultFloat, 1f) &&
                    IsAlwaysOneName(name) &&
                    !CollectVirtualAnimatedAnimatorParameterNames(controller).Contains(name) &&
                    !HasPotentialVirtualParameterDriver(controller, name) &&
                    !HasPotentialVirtualBehaviourReference(controller, name))
                .ToDictionary(name => name, name => canonical, StringComparer.Ordinal);
            if (aliases.Count == 0)
                return;
            foreach (var layer in controller.Layers)
                RewriteVirtualAlwaysOneMotion(layer.StateMachine, aliases);
        }

        private static void RewriteVirtualAlwaysOneMotion(
            VirtualStateMachine stateMachine,
            IReadOnlyDictionary<string, string> aliases)
        {
            if (stateMachine == null)
                return;
            foreach (var state in stateMachine.States)
            {
                if (state.State != null)
                    RewriteVirtualAlwaysOneMotion(state.State.Motion, aliases);
            }
            foreach (var child in stateMachine.StateMachines)
                RewriteVirtualAlwaysOneMotion(child.StateMachine, aliases);
        }

        private static void RewriteVirtualAlwaysOneMotion(
            VirtualMotion motion,
            IReadOnlyDictionary<string, string> aliases)
        {
            if (!(motion is VirtualBlendTree tree))
                return;
            var canonical = string.Empty;
            var children = tree.Children.ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (tree.BlendType == BlendTreeType.Direct &&
                    !string.IsNullOrWhiteSpace(child.DirectBlendParameter) &&
                    aliases.TryGetValue(child.DirectBlendParameter, out canonical))
                {
                    child.DirectBlendParameter = canonical;
                    children[index] = child;
                }
                RewriteVirtualAlwaysOneMotion(child.Motion, aliases);
            }
            tree.Children = children.ToImmutableList();
        }

        private static bool IsAlwaysOneName(string name)
        {
            var compact = new string((name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return compact.Contains("alwaysone", StringComparison.Ordinal) ||
                     compact.Contains("always1", StringComparison.Ordinal) ||
                     compact.EndsWith("modularavatarinternalone", StringComparison.Ordinal) ||
                     compact == "vrcfdbtalwaysone";
        }

        private static bool IsCanonicalAlwaysOneName(string name)
        {
            var compact = new string((name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                 return compact == "alwaysone" || compact.EndsWith("internalalwaysone", StringComparison.Ordinal) ||
                     compact.EndsWith("modularavatarinternalone", StringComparison.Ordinal) ||
                     compact == "vrcfdbtalwaysone";
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
    }
}
#endif
