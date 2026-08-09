#if NDMF_AVAILABLE
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(UnityBox.AnimatorOptimizer.AnimatorOptimizerNdmfPlugin))]

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerNdmfPlugin : Plugin<AnimatorOptimizerNdmfPlugin>
    {
        public override string QualifiedName => "top.sealoong.unitybox.animator-optimizer";
        public override string DisplayName => "Animator Optimizer";

        protected override void Configure()
        {
            // NDMF removes IEditorOnly components during Resolving. Capture before that pass,
            // then perform the actual optimization late on the virtual controller.
            InPhase(BuildPhase.Resolving)
                .Run("AnimatorOptimizer/CaptureConfig", context =>
                {
                    AnimatorOptimizerBuildState.Capture(context.AvatarRootObject, "NDMF before RemoveEditorOnly");
                    AnimatorOptimizerBuildState.MarkNdmfCaptured(context.AvatarRootObject);
                })
                .BeforePass("nadena.dev.ndmf.system.RemoveEditorOnly");

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence => sequence.Run(
                    "AnimatorOptimizer/OptimizeVirtualFX",
                    context => AnimatorOptimizerNdmfProcessor.Process(context, false)));

            InPhase(BuildPhase.PlatformFinish)
                .AfterPlugin("top.sealoong.unitybox.avatar-security-system")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence => sequence.Run(
                    "AnimatorOptimizer/OptimizeVirtualFXLate",
                    context => AnimatorOptimizerNdmfProcessor.Process(context, true)));
        }
    }

    internal static class AnimatorOptimizerNdmfProcessor
    {
        private const string GeneratedLayerName = AnimatorOptimizerConverter.GeneratedLayerName;
        private const string GeneratedAssetPrefix = AnimatorOptimizerConverter.GeneratedAssetPrefix;
        private const string WeightParameterPrefix = "AO/LayerWeight";
        private static int virtualPriorityCounter;

        private sealed class VirtualMotionRef
        {
            public VirtualMotion Motion;
            public float Speed = 1f;
            public float CycleOffset;
            public bool Mirror;
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
                    if (Mode == AnimatorConditionMode.If ||
                        (Mode == AnimatorConditionMode.Equals && Mathf.Approximately(Threshold, 1f)) ||
                        (Mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(Threshold, 0f)))
                        return actual;
                    return !actual;
                }

                switch (Mode)
                {
                    case AnimatorConditionMode.Equals:
                        return Mathf.Approximately(value, Threshold);
                    case AnimatorConditionMode.NotEqual:
                        return !Mathf.Approximately(value, Threshold);
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
            public List<VirtualDecisionDimension> DecisionDimensions;
            public Dictionary<int, VirtualMotionRef> DecisionTable;
            public HashSet<string> ReferencedParameters = new HashSet<string>(StringComparer.Ordinal);
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
            public List<VirtualCandidate> Candidates;
            public int FirstIndex => Candidates.Min(candidate => candidate.Index);
        }

        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            public BindingKey(EditorCurveBinding binding)
            {
                Path = binding.path ?? string.Empty;
                TypeName = binding.type == null ? string.Empty : binding.type.FullName ?? binding.type.Name;
                PropertyName = binding.propertyName ?? string.Empty;
                IsPPtr = binding.isPPtrCurve;
            }

            private string Path { get; }
            private string TypeName { get; }
            private string PropertyName { get; }
            public bool IsPPtr { get; }

            public bool Equals(BindingKey other)
            {
                return IsPPtr == other.IsPPtr &&
                       string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                       string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
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
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(TypeName);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(PropertyName);
                    hash = hash * 397 ^ (IsPPtr ? 1 : 0);
                    return hash;
                }
            }
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
            var animatorServices = context.Extension<AnimatorServicesContext>();
            if (animatorServices == null)
                return;

            object controllerKey = config.TargetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer
                ? (object)ToVrcLayerType(config.PlayableLayer)
                : config.Controller;
            if (controllerKey == null ||
                !animatorServices.ControllerContext.Controllers.TryGetValue(controllerKey, out var controller) ||
                controller == null)
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF could not find the configured virtual AnimatorController.");
                return;
            }

            var options = config.CreateOptions();
            if (options.EnableAapStateRegisterOptimization)
            {
                // The AAP tree writes real Animator FloatCurves. This virtual processor
                // cannot safely synthesize those curves, so leave the physical fallback
                // eligible instead of marking NDMF as processed and silently skipping it.
                Debug.LogWarning("[AnimatorOptimizer] AAP StateRegister is handled by the physical VRCSDK fallback; NDMF virtual processing is skipped.");
                return;
            }

            // Mark the virtual path before any remaining eligibility checks. Even when no
            // Layer is profitable to merge, the VRCSDK fallback must not process the
            // physical source controller a second time.
            AnimatorOptimizerBuildState.MarkNdmfProcessed(context.AvatarRootObject);

            if (controller.Layers.Any(layer => IsGeneratedLayerName(layer.Name, options.GeneratedLayerName)))
                return;

            var layers = controller.Layers.ToList();
            if (options.NormalizeAlwaysOneParameters)
                NormalizeVirtualAlwaysOneParameters(controller);
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

            var candidates = layers
                .Select((layer, index) => TryBuildPlan(controller, layer, options, out var plan)
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

            var candidateLayerSet = new HashSet<VirtualLayer>(
                candidates.Where(candidate => completeBindings.Contains(candidate.Layer))
                    .Select(candidate => candidate.Layer));
            var selected = candidates
                .Where(candidate => completeBindings.Contains(candidate.Layer))
                .Where(candidate => !layers.Any(otherLayer =>
                    otherLayer != candidate.Layer &&
                    !candidateLayerSet.Contains(otherLayer) &&
                    ((!completeBindings.Contains(otherLayer) && candidate.Bindings.Count > 0) ||
                     (bindingsByLayer.TryGetValue(otherLayer, out var otherBindings) &&
                      candidate.Bindings.Intersect(otherBindings).Any()))))
                .ToList();

            if (!config.OptimizeAllConvertibleLayers)
            {
                var requested = new HashSet<int>(config.LayerIndices ?? new List<int>());
                selected = selected.Where(candidate => requested.Contains(candidate.Index)).ToList();
            }
            if (options.PreserveMmdCompatibility)
                selected = selected.Where(candidate => candidate.Index != 0).ToList();

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
                .GroupBy(candidate => candidate.Layer.BlendingMode + "|" + candidate.MaskKey, StringComparer.Ordinal)
                .Select(group => new VirtualCandidateGroup
                {
                    BlendingMode = group.First().Layer.BlendingMode,
                    MaskKey = group.First().MaskKey,
                    Mask = group.First().Mask,
                    Candidates = group.OrderBy(candidate => candidate.Index).ToList(),
                })
                .OrderBy(group => group.FirstIndex)
                .ToList();
            foreach (var group in groups)
            {
                if (group.Candidates.Count > 1 && !CanPreserveVirtualLayerBehaviours(group.Candidates))
                {
                    Debug.LogWarning($"[AnimatorOptimizer] NDMF skipped {group.BlendingMode}/{group.MaskKey}: StateMachine Behaviour semantics differ.");
                    CleanupVirtualController(controller, options);
                    return;
                }
                if (group.Candidates.Count > 1 && !CanMergeVirtualMotionSet(group.Candidates))
                {
                    Debug.LogWarning($"[AnimatorOptimizer] NDMF skipped {group.BlendingMode}/{group.MaskKey}: dynamic Motion timing cannot be proven synchronized.");
                    CleanupVirtualController(controller, options);
                    return;
                }
            }
            if (HasInterleavedConflictingGroups(groups))
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF skipped interleaved BlendMode/Mask groups with shared bindings.");
                CleanupVirtualController(controller, options);
                return;
            }
            var safeGroups = new List<VirtualCandidateGroup>();
            foreach (var group in groups)
            {
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

            selected = safeGroups.SelectMany(group => group.Candidates).ToList();
            if (selected.Count < 2)
            {
                CleanupVirtualController(controller, options);
                return;
            }

            var upgraded = UpgradeParameters(controller, selected.Select(candidate => candidate.Plan), options);
            if (upgraded == null)
            {
                Debug.LogWarning("[AnimatorOptimizer] NDMF skipped virtual conversion because a Bool/Int parameter has an unknown writer or Animator curve.");
                CleanupVirtualController(controller, options);
                return;
            }
            if (options.AllowFloatConditionApproximation &&
                selected.SelectMany(candidate => EnumerateVirtualPlanConditions(candidate.Plan))
                    .Any(condition => condition.IsFloat))
                Debug.LogWarning("[AnimatorOptimizer] NDMF virtual conversion uses Float Transition approximation; threshold boundaries may differ from Animator step semantics.");
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
                    rootTree.Children = group.Candidates.Select(candidate => new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = BuildPlanMotion(controller, candidate.Plan, emptyClip),
                        DirectBlendParameter = weightParameter,
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

            var selectedSet = new HashSet<VirtualLayer>(selected.Select(candidate => candidate.Layer));
            var outputLayers = new List<VirtualLayer>(layers.Count - selected.Count + generatedLayers.Count);
            for (var index = 0; index < layers.Count; index++)
            {
                if (generatedAtIndex.TryGetValue(index, out var generatedAtFirst))
                    outputLayers.AddRange(generatedAtFirst);
                if (!selectedSet.Contains(layers[index]))
                    outputLayers.Add(layers[index]);
            }
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
            var originalLayers = layers.ToList();
            if (options.RemoveEffectivelyEmptyLayers)
            {
                layers = layers
                    .Select((layer, index) => new { layer, index })
                    .Where(item => item.index == 0 ||
                        item.layer != null &&
                        (string.Equals(item.layer.Name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                         string.Equals(item.layer.Name, "MMD Dummy Layer 2", StringComparison.Ordinal)) ||
                        item.layer != null && (IsGeneratedLayerName(item.layer.Name, options.GeneratedLayerName) ||
                            item.layer.Name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal)) ||
                        !IsEffectivelyEmptyVirtualLayer(item.layer))
                    .Select(item => item.layer)
                    .ToList();
            }

            if (options.PreserveMmdCompatibility)
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
                layer.SyncedLayerAffectsTiming || layer.StateMachine == null)
                return false;
            var stateMachine = layer.StateMachine;
            if (stateMachine.Behaviours.Count > 0 || stateMachine.EntryTransitions.Count > 0 ||
                stateMachine.AnyStateTransitions.Count > 0 || stateMachine.StateMachines.Count > 0)
                return false;
            return stateMachine.States.All(child => child.State == null ||
                child.State.Behaviours.Count == 0 &&
                (child.State.Transitions == null || child.State.Transitions.Count == 0) &&
                !child.State.WriteDefaultValues && !child.State.IKOnFeet && !child.State.Mirror &&
                Mathf.Approximately(child.State.Speed, 1f) &&
                Mathf.Approximately(child.State.CycleOffset, 0f) &&
                child.State.SpeedParameter == null && child.State.CycleOffsetParameter == null &&
                child.State.MirrorParameter == null && child.State.TimeParameter == null &&
                IsEffectivelyEmptyVirtualMotion(child.State.Motion, new HashSet<int>()));
        }

        private static bool IsEffectivelyEmptyVirtualMotion(
            VirtualMotion motion,
            HashSet<int> visited)
        {
            if (motion == null)
                return true;
            if (motion is VirtualClip clip)
                return !clip.IsMarkerClip && !IsPotentialProxyVirtualClip(clip) && !clip.GetFloatCurveBindings().Any() &&
                       !clip.GetObjectCurveBindings().Any();
            if (!(motion is VirtualBlendTree tree) || !visited.Add(motion.GetHashCode()))
                return false;
            return tree.Children.All(child => IsEffectivelyEmptyVirtualMotion(child.Motion, visited));
        }

        private static bool IsPotentialProxyVirtualClip(VirtualClip clip)
        {
            return clip != null && clip.Name != null &&
                   clip.Name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureVirtualMmdCompatibilityLayers(
            VirtualAnimatorController controller,
            List<VirtualLayer> layers)
        {
            var originalLayers = layers.ToList();
            var dummy1 = layers.FirstOrDefault(layer => layer != null &&
                string.Equals(layer.Name, "MMD Dummy Layer 1", StringComparison.Ordinal));
            var dummy2 = layers.FirstOrDefault(layer => layer != null &&
                string.Equals(layer.Name, "MMD Dummy Layer 2", StringComparison.Ordinal));
            layers.RemoveAll(layer => layer != null &&
                (string.Equals(layer.Name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                 string.Equals(layer.Name, "MMD Dummy Layer 2", StringComparison.Ordinal)));
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

            foreach (var layer in layers.ToList())
            {
                var oldLayer = originalLayers.FirstOrDefault(candidate => candidate == layer);
                if (oldLayer == null || oldLayer.SyncedLayerIndex < 0)
                    continue;
                var oldTarget = originalLayers.FirstOrDefault(candidate =>
                    candidate != null && candidate.VirtualLayerIndex == oldLayer.SyncedLayerIndex);
                if (oldTarget == null || !layers.Contains(oldTarget))
                {
                    layer.SyncedLayerIndex = -1;
                    layer.SyncedLayerAffectsTiming = false;
                }
            }
            controller.Layers = layers;
        }

        private static string GetMaskKey(VirtualAvatarMask mask)
        {
            if (mask == null)
                return "<none>";
            return string.Join(";", mask.Elements
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
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
            if (!TryBuildVirtualPriorityDimensions(candidates, options, out var dimensions, out _))
                return false;
            if (candidates.Any(candidate => candidate.Layer.BlendingMode == AnimatorLayerBlendingMode.Additive))
            {
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr))
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
                    if (motion?.Motion != null &&
                        (!(motion.Motion is VirtualClip clip) || clip.IsMarkerClip ||
                         IsPotentialProxyVirtualClip(clip) || !IsStaticVirtualClip(clip)))
                        return false;
                }
            }
            return true;
        }

        private static bool CanMergeStaticVirtualCandidates(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorLayerBlendingMode blendingMode)
        {
            if (candidates == null || candidates.Count < 2 ||
                candidates.Any(candidate => candidate.Plan == null || !candidate.Plan.IsConstant))
                return false;

            var clips = candidates.Select(candidate => candidate.Plan.ConstantMotion?.Motion).ToList();
            if (clips.Any(motion => !(motion is VirtualClip)) ||
                clips.Cast<VirtualClip>().Any(clip => clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip) ||
                    !IsStaticVirtualClip(clip)))
                return false;

            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr))
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
            if (candidates.All(candidate => candidate.Plan != null && candidate.Plan.IsConstant))
                return TryBuildVirtualLayerComposite(candidates, candidates[0].Layer.BlendingMode, out _, out _);
            return candidates.All(candidate => EnumerateVirtualPlanMotions(candidate.Plan).All(motion =>
                motion != null && motion.Motion != null && IsTimeIndependentMotion(motion.Motion)));
        }

        private static bool TryBuildVirtualLayerComposite(
            IReadOnlyList<VirtualCandidate> candidates,
            AnimatorLayerBlendingMode blendingMode,
            out VirtualClip composite,
            out VirtualMotionRef compositeSettings)
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
            if (clips.Any(clip => clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip) ||
                    clip.Settings.hasAdditiveReferencePose ||
                    clip.AdditiveReferencePoseClip != null))
                return false;
            var first = clips[0];
            var firstSettings = first.Settings;
            foreach (var clip in clips.Skip(1))
            {
                if (clip.FrameRate != first.FrameRate || clip.WrapMode != first.WrapMode ||
                    !AreCompatibleVirtualClipSettings(firstSettings, clip.Settings))
                    return false;
            }

            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
                    {
                        if (candidates[leftIndex].Bindings.Intersect(candidates[rightIndex].Bindings)
                            .Any(binding => binding.IsPPtr))
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

            composite = VirtualClip.Create(GeneratedAssetPrefix + "TimedComposite_" + virtualPriorityCounter++);
            composite.FrameRate = first.FrameRate;
            composite.WrapMode = first.WrapMode;
            composite.Settings = firstSettings;
            foreach (var curve in floatCurves.Values)
                composite.SetFloatCurve(curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                composite.SetObjectCurve(curve.Binding, curve.Curve);
            compositeSettings = candidates[0].Plan.ConstantMotion;
            return true;
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
                   Mathf.Approximately(left.startTime, right.startTime) &&
                   Mathf.Approximately(left.stopTime, right.stopTime) &&
                   Mathf.Approximately(left.orientationOffsetY, right.orientationOffsetY) &&
                   Mathf.Approximately(left.level, right.level) &&
                   Mathf.Approximately(left.cycleOffset, right.cycleOffset);
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
                if (!Mathf.Approximately(leftKeys[index].time, rightKeys[index].time) ||
                    leftKeys[index].weightedMode != rightKeys[index].weightedMode ||
                    !Mathf.Approximately(leftKeys[index].inWeight, rightKeys[index].inWeight) ||
                    !Mathf.Approximately(leftKeys[index].outWeight, rightKeys[index].outWeight))
                    return false;
                var key = leftKeys[index];
                key.value += rightKeys[index].value;
                key.inTangent += rightKeys[index].inTangent;
                key.outTangent += rightKeys[index].outTangent;
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
                foreach (var candidate in candidates)
                {
                    if (EvaluateVirtualPlan(candidate.Plan, values) == null && !candidate.Plan.IsConstant)
                        return false;
                }
                return true;
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
                    if (plan.SelectorParameterType != AnimatorControllerParameterType.Int)
                        return FailVirtualDimensions(out dimensions, out combinations);
                    foreach (var sample in plan.SelectorSamples ?? Enumerable.Empty<(float Value, VirtualMotionRef Motion)>())
                        AddVirtualPriorityValue(valuesByParameter, plan.SelectorParameter, sample.Value);
                }
                    else if (plan.DecisionDimensions != null)
                    {
                        foreach (var dimension in plan.DecisionDimensions)
                        AddVirtualPriorityValue(valuesByParameter, dimension.Parameter, dimension.Values.ToArray());
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
                if (dimension.Values.Count == 0 || combinations > long.MaxValue / dimension.Values.Count)
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
                new Dictionary<string, float>(StringComparer.Ordinal), emptyClip, additive);
        }

        private static VirtualMotion BuildVirtualPriorityNode(
            VirtualAnimatorController controller,
            IReadOnlyList<VirtualCandidate> candidates,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values,
            VirtualClip emptyClip,
            bool additive)
        {
            if (depth >= dimensions.Count)
                return CreateVirtualPriorityClip(candidates, values, emptyClip, additive);

            var dimension = dimensions[depth];
            var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "PriorityTree_" + virtualPriorityCounter++);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = dimension.Parameter;
            tree.UseAutomaticThresholds = false;
            var children = new List<VirtualBlendTree.VirtualChildMotion>();
            foreach (var value in dimension.Values)
            {
                values[dimension.Parameter] = value;
                children.Add(new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = BuildVirtualPriorityNode(controller, candidates, dimensions, depth + 1, values, emptyClip, additive),
                    Threshold = value,
                });
            }
            values.Remove(dimension.Parameter);
            tree.Children = children.ToImmutableList();
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
                var mask = 0;
                for (var index = 0; index < plan.BooleanParameters.Count; index++)
                {
                    if (values.TryGetValue(plan.BooleanParameters[index], out var value) && value >= 0.5f)
                        mask |= 1 << index;
                }
                return plan.TruthTable.TryGetValue(mask, out var motion) ? motion : null;
            }
            if (values.TryGetValue(plan.SelectorParameter, out var selectorValue))
            {
                var branch = plan.Branches?.OrderBy(item => item.Order)
                    .FirstOrDefault(item => item.Conditions.All(condition => condition.Matches(selectorValue)));
                if (branch != null)
                    return branch.Motion;
                return plan.SelectorSamples?.OrderBy(sample => Mathf.Abs(sample.Value - selectorValue))
                    .FirstOrDefault().Motion;
            }
            if (plan.DecisionDimensions != null)
            {
                var key = 0;
                var stride = 1;
                foreach (var dimension in plan.DecisionDimensions)
                {
                    if (!values.TryGetValue(dimension.Parameter, out var value))
                        return null;
                    var index = dimension.Values.FindIndex(candidate => Mathf.Approximately(candidate, value));
                    if (index < 0)
                        return null;
                    key += index * stride;
                    stride *= dimension.Values.Count;
                }
                return plan.DecisionTable.TryGetValue(key, out var decisionMotion) ? decisionMotion : null;
            }
            return null;
        }

        private static VirtualClip CreateVirtualPriorityClip(
            IReadOnlyList<VirtualCandidate> candidates,
            IReadOnlyDictionary<string, float> values,
            VirtualClip emptyClip,
            bool additive)
        {
            var floatCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<BindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            foreach (var candidate in candidates.OrderBy(item => item.Index))
            {
                var motion = EvaluateVirtualPlan(candidate.Plan, values);
                if (motion?.Motion == null)
                    continue;
                if (!(motion.Motion is VirtualClip clip) || clip.IsMarkerClip)
                    throw new InvalidOperationException("NDMF virtual priority composite requires editable VirtualClip leaves.");
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

            var result = VirtualClip.Create(GeneratedAssetPrefix + "PriorityClip_" + virtualPriorityCounter++);
            foreach (var curve in floatCurves.Values)
                result.SetFloatCurve(curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                result.SetObjectCurve(curve.Binding, curve.Curve);
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
            return AnimationCurve.Constant(0f, 1f / 60f, leftValue + rightValue);
        }

        private static bool IsStaticVirtualClip(VirtualClip clip)
        {
            if (clip == null || IsPotentialProxyVirtualClip(clip))
                return false;
            var settings = clip.Settings;
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                return false;
            foreach (var binding in clip.GetFloatCurveBindings())
            {
                var curve = clip.GetFloatCurve(binding);
                if (!IsConstantVirtualCurve(curve))
                    return false;
            }
            foreach (var binding in clip.GetObjectCurveBindings())
            {
                var curve = clip.GetObjectCurve(binding);
                if (curve == null || curve.Length == 0)
                    continue;
                var firstValue = curve[0].value;
                if (curve.Any(key => key.value != firstValue))
                    return false;
            }
            return true;
        }

        private static bool TryBuildPlan(
            VirtualAnimatorController controller,
            VirtualLayer layer,
            AnimatorOptimizerOptions options,
            out VirtualPlan plan)
        {
            plan = null;
            if (!TryBuildPlanCore(controller, layer, options, out var builtPlan))
                return false;
            if (!ValidateVirtualPlanParameters(controller, builtPlan, options))
                return false;
            plan = builtPlan;
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
                layer.SyncedLayerIndex >= 0 || layer.SyncedLayerAffectsTiming ||
                !Mathf.Approximately(layer.DefaultWeight, 1f))
                return false;

            var stateMachine = layer.StateMachine;
            if (stateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) ||
                stateMachine.DefaultState == null && stateMachine.StateMachines.Count == 0)
                return false;

            if (stateMachine.StateMachines.Count > 0)
            {
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

            if (stateMachine.States.Any(child => child.State == null))
                return false;

            var states = stateMachine.States.Select(child => child.State).ToList();
            if (TryBuildVirtualEntryDrivenPlan(controller, stateMachine, states, options, out plan))
                return true;
            if (TryBuildVirtualEffectEquivalentPlan(stateMachine, states, out plan))
                return true;
            var allStatic = states.All(state => state.Transitions.Count == 0) &&
                            stateMachine.AnyStateTransitions.Count == 0 &&
                            stateMachine.EntryTransitions.Count == 0;
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

            if (stateMachine.AnyStateTransitions.Count > 0 && states.Any(state => state.Transitions.Count > 0))
                return false;

            var transitions = stateMachine.AnyStateTransitions.ToList();
            if (transitions.Count == 0)
            {
                if (states.Count != 2 || states.Any(state => state.Transitions.Count != 1))
                    return false;
                transitions = states.Select(state => state.Transitions[0]).ToList();
            }

            var branches = new List<VirtualBranch>();
            for (var order = 0; order < transitions.Count; order++)
            {
                var transition = transitions[order];
                if (transition == null || transition.DestinationState == null || transition.DestinationStateMachine != null ||
                    transition.IsExit || transition.CanTransitionToSelf || transition.Mute || transition.Solo ||
                    transition.ExitTime.HasValue || !Mathf.Approximately(transition.Duration, 0f) ||
                    !Mathf.Approximately(transition.Offset, 0f) || transition.Conditions.Count == 0)
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
            plan.ReferencedParameters = parameters;
            foreach (var parameterName in parameters)
            {
                if (IsVrcGlobalParameter(parameterName))
                    continue;
                if (!controller.Parameters.TryGetValue(parameterName, out var parameter))
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
                    if (!string.IsNullOrWhiteSpace(tree.BlendParameterY))
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

        private static bool IsVrcGlobalParameter(string parameterName)
        {
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
                stateMachine.EntryTransitions.Count == 0 || stateMachine.AnyStateTransitions.Count > 0 ||
                stateMachine.DefaultState == null || !states.Contains(stateMachine.DefaultState))
                return false;

            var stateSet = new HashSet<VirtualState>(states);
            var entries = new List<VirtualNormalizedTransition>();
            for (var order = 0; order < stateMachine.EntryTransitions.Count; order++)
            {
                var transition = stateMachine.EntryTransitions[order];
                if (transition == null || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null || transition.IsExit ||
                    transition.Mute || transition.Solo || !stateSet.Contains(transition.DestinationState))
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
                stateMachine.AnyStateTransitions.Count == 0 &&
                states.All(state => state.Transitions.Count == 0))
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
                foreach (var transition in state.Transitions)
                {
                    if (transition == null || !transition.IsExit || transition.DestinationState != null ||
                        transition.DestinationStateMachine != null || transition.CanTransitionToSelf ||
                        transition.Mute || transition.Solo || transition.ExitTime.HasValue ||
                        !Mathf.Approximately(transition.Duration, 0f) ||
                        !Mathf.Approximately(transition.Offset, 0f))
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

            var table = new Dictionary<int, VirtualMotionRef>();
            var indices = new int[dimensions.Count];
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
                    if (exitsNow != (desired != state))
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
                var values = new SortedSet<float>();
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
                else if (type == AnimatorControllerParameterType.Float && options.AllowFloatConditionApproximation)
                {
                    var epsilon = Mathf.Max(0.000001f, options.FloatConditionEpsilon);
                    foreach (var condition in list)
                    {
                        values.Add(condition.Threshold - epsilon);
                        values.Add(condition.Threshold);
                        values.Add(condition.Threshold + epsilon);
                    }
                }
                else
                {
                    return false;
                }
                if (values.Count == 0 || combinations > long.MaxValue / values.Count)
                    return false;
                combinations *= values.Count;
                dimensions.Add(new VirtualDecisionDimension
                {
                    Parameter = group.Key,
                    Type = type,
                    Values = values.ToList(),
                });
            }
            var allBoolean = dimensions.All(dimension => dimension.Type == AnimatorControllerParameterType.Bool);
            var limit = allBoolean ? Math.Max(2, options.MaxBooleanTruthTableEntries) :
                Math.Max(2, options.MaxPriorityCombinations);
            return dimensions.Count > 0 && combinations <= limit;
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
            if (dimensions == null || dimensions.Count == 0 || table == null)
                return null;
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
                rootStateMachine.DefaultState == null && rootStateMachine.EntryTransitions.Count == 0)
                return false;

            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (state.Behaviours.Count > 0 || !state.WriteDefaultValues || state.IKOnFeet ||
                    state.Mirror || state.SpeedParameter != null || state.CycleOffsetParameter != null ||
                    state.MirrorParameter != null || state.TimeParameter != null ||
                    !TryBuildVirtualStaticObservableMotionSignature(state.Motion, out var signature))
                    return false;
                signatures.Add(signature);
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;

            foreach (var stateMachine in stateMachines)
            {
                if (stateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) ||
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
            foreach (var transition in stateMachine.EntryTransitions)
            {
                if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var transition in stateMachine.AnyStateTransitions)
            {
                if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var child in stateMachine.States)
            {
                foreach (var transition in child.State?.Transitions ??
                         ImmutableList<VirtualStateTransition>.Empty)
                {
                    if (!IsLocalVirtualNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            foreach (var child in stateMachine.StateMachines)
            {
                if (!stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                    continue;
                foreach (var transition in transitions)
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
                stateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) || stateMachine.EntryTransitions.Count > 0 &&
                stateMachine.EntryTransitions.Any(transition =>
                    transition == null || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null) ||
                stateMachine.AnyStateTransitions.Any(transition =>
                    transition == null || transition.IsExit || transition.DestinationState == null ||
                    transition.DestinationStateMachine != null))
                return false;

            var stateSet = new HashSet<VirtualState>(states);
            if (stateMachine.EntryTransitions.Any(transition =>
                    !stateSet.Contains(transition.DestinationState)) ||
                stateMachine.AnyStateTransitions.Any(transition =>
                    !stateSet.Contains(transition.DestinationState)))
                return false;
            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (!TryBuildVirtualStaticObservableMotionSignature(state.Motion, out var signature) ||
                    state.Behaviours.Count > 0 || !state.WriteDefaultValues || state.IKOnFeet ||
                    state.Mirror || state.SpeedParameter != null || state.CycleOffsetParameter != null ||
                    state.MirrorParameter != null || state.TimeParameter != null ||
                    !stateSet.Contains(state))
                    return false;
                signatures.Add(signature);
                if (state.Transitions.Any(transition =>
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

        private static bool TryBuildVirtualStaticObservableMotionSignature(
            VirtualMotion motion,
            out string signature)
        {
            signature = null;
            if (motion == null)
            {
                signature = "static-empty";
                return true;
            }
            if (!(motion is VirtualClip clip) || clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip) ||
                clip.Settings.hasAdditiveReferencePose || clip.AdditiveReferencePoseClip != null)
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
                if (curve.Any(key => key.value != curve[0].value))
                    return false;
                objectParts.Add("o:" + BuildVirtualBindingKey(binding) + ":" +
                    string.Join(";", curve.Select(key =>
                        key.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        (key.value == null ? "0" : key.value.GetInstanceID().ToString()))));
            }
            floatParts.Sort(StringComparer.Ordinal);
            objectParts.Sort(StringComparer.Ordinal);
            signature = "static:" + string.Join(";", floatParts.Concat(objectParts));
            return true;
        }

        private static string BuildVirtualBindingKey(EditorCurveBinding binding)
        {
            return string.Join("/", new[]
            {
                binding.path ?? string.Empty,
                binding.type == null ? string.Empty : binding.type.FullName ?? binding.type.Name,
                binding.propertyName ?? string.Empty,
                binding.isPPtrCurve.ToString(),
            });
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
                    !(condition.IsFloat && options.AllowFloatConditionApproximation)))
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
                else if (type == AnimatorControllerParameterType.Float && options.AllowFloatConditionApproximation)
                {
                    var epsilon = Mathf.Max(0.000001f, options.FloatConditionEpsilon);
                    foreach (var condition in conditions)
                    {
                        values.Add(condition.Threshold - epsilon);
                        values.Add(condition.Threshold);
                        values.Add(condition.Threshold + epsilon);
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
                });
            }

            long combinations = 1;
            foreach (var dimension in dimensions)
            {
                if (dimension.Values.Count == 0 || combinations > long.MaxValue / dimension.Values.Count)
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
                return false;
            if (motion is VirtualClip clip)
            {
                if (clip.IsMarkerClip || IsPotentialProxyVirtualClip(clip))
                    return false;
                var settings = clip.Settings;
                if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                    return false;
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var curve = clip.GetFloatCurve(binding);
                    if (!IsConstantVirtualCurve(curve))
                        return false;
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    var curve = clip.GetObjectCurve(binding);
                    if (curve != null && curve.Length > 1)
                    {
                        var first = curve[0].value;
                        if (curve.Any(key => key.value != first))
                            return false;
                    }

                }
                return true;
            }
            if (motion is VirtualBlendTree tree)
                return tree.Children.All(child => IsTimeIndependentMotion(child.Motion));
            return false;
        }

        private static bool IsConstantVirtualCurve(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0)
                return true;
            var keys = curve.keys;
            var value = keys[0].value;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                if (!Mathf.Approximately(key.value, value) ||
                    !Mathf.Approximately(key.inTangent, 0f) ||
                    !Mathf.Approximately(key.outTangent, 0f))
                    return false;
                if (index > 0 && !Mathf.Approximately(
                        curve.Evaluate((keys[index - 1].time + key.time) * 0.5f), value))
                    return false;
            }
            return true;
        }

        private static bool TryResolveVirtualNestedStaticDefault(
            VirtualStateMachine stateMachine,
            out VirtualState state)
        {
            state = null;
            if (stateMachine == null || stateMachine.Behaviours.Count > 0 ||
                stateMachine.EntryTransitions.Count > 0 || stateMachine.AnyStateTransitions.Count > 0)
                return false;

            if (stateMachine.DefaultState != null)
            {
                if (stateMachine.StateMachines.Count > 0)
                    return false;
                state = stateMachine.DefaultState;
                return state.Transitions.Count == 0;
            }

            if (stateMachine.States.Count != 0 || stateMachine.StateMachines.Count != 1)
                return false;
            var child = stateMachine.StateMachines[0];
            if (stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions) && transitions.Count != 0)
                return false;
            return TryResolveVirtualNestedStaticDefault(child.StateMachine, out state);
        }

        private static bool TryNormalizeCondition(
            VirtualAnimatorController controller,
            AnimatorCondition source,
            AnimatorOptimizerOptions options,
            out VirtualCondition condition)
        {
            condition = null;
            var hasDeclaredParameter = controller.Parameters.TryGetValue(source.parameter, out var parameter);
            if (!hasDeclaredParameter && !TryGetVirtualGlobalParameterType(source.parameter, out parameter))
                return false;
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (hasDeclaredParameter && !options.UpgradeBoolParametersToFloat ||
                    (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                     source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual))
                    return false;
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    !Mathf.Approximately(source.threshold, 0f) && !Mathf.Approximately(source.threshold, 1f))
                    return false;
            }
            else if (parameter.type == AnimatorControllerParameterType.Int)
            {
                if (hasDeclaredParameter && !options.UpgradeIntParametersToFloat ||
                    (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                     source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less) ||
                    !Mathf.Approximately(source.threshold, Mathf.Round(source.threshold)))
                    return false;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float)
            {
                if (!options.AllowFloatConditionApproximation ||
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
            string parameterName,
            out AnimatorControllerParameter parameter)
        {
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
            if (parameters.Count == 0 || (1L << parameters.Count) > Math.Max(2, options.MaxBooleanTruthTableEntries))
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
            var values = new SortedSet<float>();
            var epsilon = Mathf.Max(0.000001f, options.FloatConditionEpsilon);
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
                    values.Add(threshold - epsilon);
                    values.Add(threshold);
                    values.Add(threshold + epsilon);
                }
            }
            if (values.Count > Math.Max(3, options.MaxNumericSelectorSamples))
                return false;

            var samples = new List<(float Value, VirtualMotionRef Motion)>();
            foreach (var value in values)
            {
                var branch = branches.OrderBy(item => item.Order)
                    .FirstOrDefault(item => item.Conditions[0].Matches(value));
                if (branch == null && !prototype.IsInt && options.AllowFloatConditionApproximation)
                {
                    branch = branches
                        .OrderBy(item => Mathf.Abs(item.Conditions[0].Threshold - value))
                        .ThenBy(item => item.Order)
                        .FirstOrDefault();
                }
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
                var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "NumericSelector_" + plan.SelectorParameter);
                tree.BlendType = BlendTreeType.Simple1D;
                tree.BlendParameter = plan.SelectorParameter;
                tree.UseAutomaticThresholds = false;
                tree.Children = plan.SelectorSamples.Select(sample => new VirtualBlendTree.VirtualChildMotion
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
            return BuildTruthTree(controller, plan, emptyClip, 0, 0,
                new Dictionary<string, VirtualTruthNode>(StringComparer.Ordinal)).Motion;
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
            return BuildVirtualFiniteDecisionNode(controller, plan, emptyClip, depth, key, stride, memo).Motion;
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

            var signature = string.Join("|", childNodes.Select(node => node.Signature));
            var memoKey = depth + ":" + dimension.Parameter + ":" +
                string.Join(",", dimension.Values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) +
                ":" + signature;
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;

            var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "FiniteTree_" + depth + "_" + key);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = dimension.Parameter;
            tree.UseAutomaticThresholds = false;
            tree.Children = dimension.Values.Select((value, index) => new VirtualBlendTree.VirtualChildMotion
            {
                Motion = childNodes[index].Motion,
                Threshold = value,
                TimeScale = childNodes[index].LeafSettings == null ? 1f : childNodes[index].LeafSettings.Speed,
                CycleOffset = childNodes[index].LeafSettings == null ? 0f : childNodes[index].LeafSettings.CycleOffset,
                Mirror = childNodes[index].LeafSettings != null && childNodes[index].LeafSettings.Mirror,
            }).ToImmutableList();
            var result = new VirtualTruthNode
            {
                Motion = tree,
                Signature = signature,
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
                var leafSignature = falseSignature + "|" + trueSignature;
                var leafMemoKey = depth + ":" + plan.BooleanParameters[depth] + ":" + leafSignature;
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
                    Signature = leafSignature,
                };
                memo[leafMemoKey] = leafResult;
                return leafResult;
            }

            var falseTree = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask, memo);
            var trueTree = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask | (1 << depth), memo);
            if (string.Equals(falseTree.Signature, trueTree.Signature, StringComparison.Ordinal))
                return falseTree;

            var signature = falseTree.Signature + "|" + trueTree.Signature;
            var memoKey = depth + ":" + plan.BooleanParameters[depth] + ":" + signature;
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
                Signature = signature,
            };
            memo[memoKey] = result;
            return result;
        }

        private static string BuildVirtualMotionSignature(VirtualMotionRef motion)
        {
            if (motion == null)
                return "null";
            return (motion.Motion == null ? "null" : motion.Motion.GetHashCode().ToString()) + ":" +
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
            if (state == null || state.Motion == null ||
                ContainsPotentialProxyVirtualMotion(state.Motion, new HashSet<VirtualMotion>()) ||
                state.Behaviours.Count > 0 || !state.WriteDefaultValues ||
                state.IKOnFeet || state.SpeedParameter != null || state.CycleOffsetParameter != null ||
                state.MirrorParameter != null || state.TimeParameter != null)
                return false;
            if (!allowTransitions && state.Transitions.Count > 0)
                return false;
            return state.Motion is VirtualClip || state.Motion is VirtualBlendTree;
        }

        private static bool ContainsPotentialProxyVirtualMotion(
            VirtualMotion motion,
            ISet<VirtualMotion> visited)
        {
            if (motion == null || !visited.Add(motion))
                return false;
            if (motion is VirtualClip clip)
                return IsPotentialProxyVirtualClip(clip);
            return motion is VirtualBlendTree tree && tree.Children.Any(child =>
                ContainsPotentialProxyVirtualMotion(child.Motion, visited));
        }

        private static VirtualMotionRef ToMotionRef(VirtualState state)
        {
            return new VirtualMotionRef
            {
                Motion = state?.Motion,
                Speed = state == null ? 1f : state.Speed,
                CycleOffset = state == null ? 0f : state.CycleOffset,
                Mirror = state != null && state.Mirror,
            };
        }

        private static bool TryCollectLayerBindings(VirtualLayer layer, HashSet<BindingKey> bindings)
        {
            if (layer == null || layer.StateMachine == null ||
                layer.StateMachine.Behaviours.Any(behaviour => !IsPreservableLayerBehaviour(behaviour)))
                return false;

            if (layer.StateMachine.StateMachines.Count > 0)
            {
                if (!TryResolveVirtualNestedStaticDefault(layer.StateMachine, out var nestedDefault) ||
                    nestedDefault == null || nestedDefault.Behaviours.Count > 0)
                    return false;
                return TryCollectMotionBindings(nestedDefault.Motion, bindings);
            }

            foreach (var child in layer.StateMachine.States)
            {
                if (child.State == null || child.State.Behaviours.Count > 0 ||
                    !TryCollectMotionBindings(child.State.Motion, bindings))
                    return false;
            }
            return true;
        }

        private static bool IsPreservableLayerBehaviour(StateMachineBehaviour behaviour)
        {
            return behaviour != null && string.Equals(
                behaviour.GetType().Name, "ModularAvatarMMDLayerControl", StringComparison.Ordinal);
        }

        private static bool CanPreserveVirtualLayerBehaviours(
            IReadOnlyList<VirtualCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return true;
            var keys = candidates.Select(candidate =>
                    candidate.Layer.StateMachine.Behaviours
                        .Select(BuildVirtualBehaviourKey)
                        .ToList())
                .ToList();
            return keys.All(list => list.Count == 0) ||
                   keys.Skip(1).All(list => list.SequenceEqual(keys[0], StringComparer.Ordinal));
        }

        private static string BuildVirtualBehaviourKey(StateMachineBehaviour behaviour)
        {
            if (!IsPreservableLayerBehaviour(behaviour))
                return "unsupported:" + (behaviour == null ? "null" : behaviour.GetType().FullName);
            try
            {
                return behaviour.GetType().FullName + "|" + EditorJsonUtility.ToJson(behaviour);
            }
            catch
            {
                return behaviour.GetType().FullName + "|unreadable";
            }
        }

        private static ImmutableList<StateMachineBehaviour> CloneVirtualLayerBehaviours(
            IReadOnlyList<VirtualCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0 || !CanPreserveVirtualLayerBehaviours(candidates))
                return ImmutableList<StateMachineBehaviour>.Empty;
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
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    if (binding.type == typeof(Animator))
                        return false;
                    bindings.Add(new BindingKey(binding));
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    if (binding.type == typeof(Animator))
                        return false;
                    bindings.Add(new BindingKey(binding));
                }
                return true;
            }
            if (motion is VirtualBlendTree tree)
                return tree.Children.All(child => TryCollectMotionBindings(child.Motion, bindings));
            return false;
        }

        private static List<string> UpgradeParameters(
            VirtualAnimatorController controller,
            IEnumerable<VirtualPlan> plans,
            AnimatorOptimizerOptions options)
        {
            var names = plans.SelectMany(plan => plan.BooleanParameters ?? Enumerable.Empty<string>())
                .Concat(plans.Where(plan => !string.IsNullOrWhiteSpace(plan.SelectorParameter))
                    .Select(plan => plan.SelectorParameter))
                .Concat(plans.SelectMany(plan => plan.DecisionDimensions ?? Enumerable.Empty<VirtualDecisionDimension>())
                    .Select(dimension => dimension.Parameter))
                .Concat(plans.SelectMany(plan => plan.ReferencedParameters ?? new HashSet<string>(StringComparer.Ordinal))
                    .Where(name => !IsVrcGlobalParameter(name)))
                .Distinct(StringComparer.Ordinal).ToList();
            var animated = CollectVirtualAnimatedAnimatorParameterNames(controller);
            if (names.Any(name => VirtualParameterNeedsUpgrade(controller, name, options) &&
                (animated.Contains(name) || HasPotentialVirtualBehaviourReference(controller, name))))
                return null;
            var upgraded = new List<string>();
            var bools = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (IsVrcGlobalParameter(name))
                    continue;
                if (!controller.Parameters.TryGetValue(name, out var parameter))
                    continue;
                if (parameter.type == AnimatorControllerParameterType.Bool && options.UpgradeBoolParametersToFloat)
                {
                    controller.SetParameter(name, new AnimatorControllerParameter
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
                    controller.SetParameter(name, new AnimatorControllerParameter
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

        private static bool VirtualParameterNeedsUpgrade(
            VirtualAnimatorController controller,
            string name,
            AnimatorOptimizerOptions options)
        {
            if (controller == null || !controller.Parameters.TryGetValue(name, out var parameter))
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
                VirtualStateMachineHasPotentialBehaviourReference(layer.StateMachine, parameterName));
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
                return false;
            try
            {
                var serializedObject = new SerializedObject(behaviour);
                var iterator = serializedObject.GetIterator();
                var enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
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
                    if (condition.mode == AnimatorConditionMode.If)
                    {
                        condition.mode = AnimatorConditionMode.Greater;
                        condition.threshold = 0.5f;
                    }
                    else if (condition.mode == AnimatorConditionMode.IfNot)
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
            var name = WeightParameterPrefix;
            var suffix = 1;
            while (controller.Parameters.ContainsKey(name))
                name = WeightParameterPrefix + "_" + suffix++;
            controller.SetParameter(name, new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            return name;
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
            foreach (var layer in controller.Layers)
                CollectVirtualAnimatedAnimatorParameterNames(layer.StateMachine, names, new HashSet<VirtualMotion>());
            return names;
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
                foreach (var binding in clip.GetFloatCurveBindings())
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
                VirtualStateMachineHasParameterDriver(layer.StateMachine, parameterName));
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
