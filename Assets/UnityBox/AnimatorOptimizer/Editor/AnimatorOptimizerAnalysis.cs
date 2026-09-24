using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    public enum AnimatorOptimizerConversionKind
    {
        Unsupported,
        ConstantMotion,
        BooleanTruthTable,
        NumericSelector,
        FiniteParameterTable,
        AapStateRegister,
    }

    [Serializable]
    public sealed class AnimatorOptimizerOptions
    {
        public string GeneratedLayerName = "(WD On) AnimatorOptimizer";
        public string WeightParameterPrefix = "AO/LayerWeight";
        public bool RequireAtLeastTwoLayers = true;
        public bool StrictCurveConflicts = true;
        public bool UpgradeBoolParametersToFloat = true;
        public bool UpgradeIntParametersToFloat = true;
        public bool AllowFloatConditionApproximation = false;
        public float FloatConditionEpsilon = 0.001f;
        public AnimatorOptimizerComponent.WriteDefaultsPolicy WriteDefaultsPolicy =
            AnimatorOptimizerComponent.WriteDefaultsPolicy.AllowCoveredStates;
        // Only affects transitions between time-independent motions; dynamic clips still
        // require exact timing because the BlendTree cannot restart them.
        public bool IgnoreTransitionTimingForStaticMotions = false;
        public AnimatorOptimizerComponent.HeuristicFlags Heuristics;
        public int ShortAnimationMaxFrames = 5;
        public float ShortAnimationMaxSeconds = 0.1f;
        public int MotionTimeSampleCount = 9;
        public const AnimatorOptimizerComponent.HeuristicFlags PracticalHeuristics =
            AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles |
            AnimatorOptimizerComponent.HeuristicFlags.ShortAnimationEndpoints;
        public const AnimatorOptimizerComponent.HeuristicFlags AggressiveHeuristics = PracticalHeuristics |
            AnimatorOptimizerComponent.HeuristicFlags.SampleMotionTime |
            AnimatorOptimizerComponent.HeuristicFlags.ComplementaryToggle |
            AnimatorOptimizerComponent.HeuristicFlags.StableInputConvergence;
        // These are resource safeguards, not semantic eligibility checks. Exact
        // decision plans are still preferred whenever the configured finite domain
        // can be enumerated; generated trees use structural sharing to reduce cost.
        public int MaxBooleanTruthTableEntries = 4096;
        public int MaxNumericSelectorSamples = 256;
        public int MaxPriorityCombinations = 4096;
        public int MaxAapDecisionNodes = 4096;
        public bool MergeLayersWithEquivalentMasks = true;
        public bool ResolveOverridePriorityWithStaticComposite = true;
        public bool MergeSynchronizedStateMachines = true;
        public bool EnableAapStateRegisterOptimization = false;
        public bool RemoveEffectivelyEmptyLayers = true;
        // A name and an initial value cannot prove absence of external writers.
        public bool NormalizeAlwaysOneParameters = false;
        public bool ExecuteLateOptimization = false;
        public bool PreserveMmdCompatibility = false;
        public bool HasKnownPlayableLayer = false;
        public AnimatorOptimizerComponent.PlayableLayer TargetPlayableLayer = AnimatorOptimizerComponent.PlayableLayer.FX;
        public bool VerboseLogging = false;

        public AnimatorOptimizerOptions Clone()
        {
            return new AnimatorOptimizerOptions
            {
                GeneratedLayerName = GeneratedLayerName,
                WeightParameterPrefix = WeightParameterPrefix,
                RequireAtLeastTwoLayers = RequireAtLeastTwoLayers,
                StrictCurveConflicts = StrictCurveConflicts,
                UpgradeBoolParametersToFloat = UpgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = UpgradeIntParametersToFloat,
                AllowFloatConditionApproximation = AllowFloatConditionApproximation,
                FloatConditionEpsilon = float.IsNaN(FloatConditionEpsilon) || float.IsInfinity(FloatConditionEpsilon)
                    ? 0.001f : Mathf.Max(0.000001f, FloatConditionEpsilon),
                WriteDefaultsPolicy = Enum.IsDefined(typeof(AnimatorOptimizerComponent.WriteDefaultsPolicy), WriteDefaultsPolicy)
                    ? WriteDefaultsPolicy : AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict,
                IgnoreTransitionTimingForStaticMotions = IgnoreTransitionTimingForStaticMotions,
                Heuristics = Heuristics & AggressiveHeuristics,
                ShortAnimationMaxFrames = Mathf.Clamp(ShortAnimationMaxFrames, 1, 10),
                ShortAnimationMaxSeconds = float.IsNaN(ShortAnimationMaxSeconds) || float.IsInfinity(ShortAnimationMaxSeconds)
                    ? 0.1f : Mathf.Clamp(ShortAnimationMaxSeconds, 0.01f, 0.25f),
                MotionTimeSampleCount = Mathf.Clamp(MotionTimeSampleCount, 2, 33),
                MaxBooleanTruthTableEntries = Mathf.Clamp(MaxBooleanTruthTableEntries, 2, 4096),
                MaxNumericSelectorSamples = Mathf.Clamp(MaxNumericSelectorSamples, 2, 256),
                MaxPriorityCombinations = Mathf.Clamp(MaxPriorityCombinations, 2, 4096),
                MaxAapDecisionNodes = Mathf.Clamp(MaxAapDecisionNodes, 64, 16384),
                MergeLayersWithEquivalentMasks = MergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = ResolveOverridePriorityWithStaticComposite,
                MergeSynchronizedStateMachines = MergeSynchronizedStateMachines,
                EnableAapStateRegisterOptimization = EnableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = RemoveEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = NormalizeAlwaysOneParameters,
                ExecuteLateOptimization = ExecuteLateOptimization,
                PreserveMmdCompatibility = PreserveMmdCompatibility,
                HasKnownPlayableLayer = HasKnownPlayableLayer,
                TargetPlayableLayer = TargetPlayableLayer,
                VerboseLogging = VerboseLogging,
            };
        }
    }

    public sealed class AnimatorOptimizerReport
    {
        private readonly List<AnimatorOptimizerLayerReport> _layers;

        internal AnimatorOptimizerReport(
            AnimatorController controller,
            IEnumerable<AnimatorOptimizerLayerReport> layers,
            bool graphIsValid = true)
        {
            Controller = controller;
            GraphIsValid = graphIsValid;
            _layers = new List<AnimatorOptimizerLayerReport>(layers ?? Enumerable.Empty<AnimatorOptimizerLayerReport>());
        }

        public AnimatorController Controller { get; }
        internal bool GraphIsValid { get; }
        public IReadOnlyList<AnimatorOptimizerLayerReport> Layers => _layers;
        public int ConvertibleLayerCount => _layers.Count(layer => layer.CanConvert);
        public IEnumerable<AnimatorOptimizerLayerReport> ConvertibleLayers => _layers.Where(layer => layer.CanConvert);
    }

    public sealed class AnimatorOptimizerLayerReport
    {
        private readonly List<string> _reasons = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _optimizations = new List<string>();
        private readonly List<string> _suggestions = new List<string>();

        internal AnimatorOptimizerLayerReport(int index, AnimatorControllerLayer layer)
        {
            Index = index;
            Name = layer == null ? string.Empty : layer.name;
            SourceLayer = layer;
            DefaultWeight = layer == null ? 0f : layer.defaultWeight;
            Mask = layer == null ? null : layer.avatarMask;
            MaskKey = BuildMaskKey(Mask);
            ConversionKind = AnimatorOptimizerConversionKind.Unsupported;
            Bindings = new HashSet<AnimatorOptimizerBindingKey>();
            BindingsAreComplete = true;
        }

        public int Index { get; }
        public string Name { get; }
        public float DefaultWeight { get; }
        public AnimatorLayerBlendingMode BlendingMode => SourceLayer == null
            ? AnimatorLayerBlendingMode.Override
            : SourceLayer.blendingMode;
        public AvatarMask Mask { get; }
        public string MaskKey { get; }
        public AnimatorOptimizerConversionKind ConversionKind { get; internal set; }
        public bool CanConvert => ConversionKind != AnimatorOptimizerConversionKind.Unsupported && _reasons.Count == 0;
        public int MotionCount { get; internal set; }
        public int AnimatedBindingCount => Bindings.Count;
        public IReadOnlyList<string> Reasons => _reasons;
        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyList<string> Optimizations => _optimizations;
        public IReadOnlyList<string> Suggestions => _suggestions;
        public IReadOnlyList<string> HeuristicAssumptions => Plan?.Assumptions ?? (IReadOnlyList<string>)Array.Empty<string>();
        public bool UsesHeuristics => HeuristicAssumptions.Count > 0;
        public IReadOnlyCollection<string> RequiredFloatParameters => Plan == null
            ? (IReadOnlyCollection<string>)Array.Empty<string>()
            : Plan.RequiredFloatParameters;

        internal AnimatorControllerLayer SourceLayer { get; }
        internal HashSet<AnimatorOptimizerBindingKey> Bindings { get; }
        internal bool BindingsAreComplete { get; set; }
        internal AnimatorOptimizerMotionPlan Plan { get; set; }
        internal AnimatorOptimizerOptions Options { get; set; }
        // Conservative controller-wide default-write scope, shared by analysis attempts.
        internal HashSet<AnimatorOptimizerBindingKey> LayerMotionBindings { get; set; }
        internal bool LayerMotionBindingsAreComplete { get; set; }
        internal HashSet<int> WriteDefaultsOffStates { get; } = new HashSet<int>();
        internal HashSet<string> AttemptReasons { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal HashSet<string> AttemptSuggestions { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal bool HasExitTime { get; set; }
        public int WriteDefaultsOffStateCount => WriteDefaultsOffStates.Count;
        public int RelaxedTransitionCount { get; internal set; }

        internal void AddReason(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason) && !_reasons.Contains(reason))
                _reasons.Add(reason);
        }

        internal void AddWarning(string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning) && !_warnings.Contains(warning))
                _warnings.Add(warning);
        }

        internal void AddOptimization(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !_optimizations.Contains(message))
                _optimizations.Add(message);
        }

        internal void AddSuggestion(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !_suggestions.Contains(message))
                _suggestions.Add(message);
        }

        private static string BuildMaskKey(AvatarMask mask)
        {
            if (mask == null)
                return "<none>";

            try
            {
                var key = new System.Text.StringBuilder();
                for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                    key.Append(mask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)part) ? '1' : '0');
                using var serialized = new SerializedObject(mask);
                var elements = serialized.FindProperty("m_Elements");
                if (elements == null || !elements.isArray || elements.arraySize != mask.transformCount)
                    return "instance:" + mask.GetInstanceID();
                key.Append('|').Append(elements.arraySize).Append('|');
                for (var index = 0; index < elements.arraySize; index++)
                {
                    var element = elements.GetArrayElementAtIndex(index);
                    var pathProperty = element.FindPropertyRelative("m_Path");
                    var weightProperty = element.FindPropertyRelative("m_Weight");
                    if (pathProperty == null || weightProperty == null ||
                        !AnimatorOptimizerAnalyzer.IsFinite(weightProperty.floatValue))
                        return "instance:" + mask.GetInstanceID();
                    var path = pathProperty.stringValue ?? string.Empty;
                    // GetTransformActive only returns weight > 0.5, losing fractional
                    // values written by other tools. Compare the actual serialized weight.
                    key.Append(path.Length).Append(':').Append(path).Append('=')
                        .Append(weightProperty.floatValue.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                }
                return key.ToString();
            }
            catch
            {
                return "instance:" + mask.GetInstanceID();
            }
        }
    }

    internal sealed class AnimatorOptimizerMotionPlan
    {
        public AnimatorOptimizerConversionKind Kind;
        public AnimatorOptimizerMotionRef ConstantMotion;
        public List<AnimatorOptimizerBranch> Branches;
        public List<string> BooleanParameters;
        public Dictionary<int, AnimatorOptimizerMotionRef> TruthTable;
        public List<AnimatorOptimizerDecisionDimension> DecisionDimensions;
        public Dictionary<int, AnimatorOptimizerMotionRef> DecisionTable;
        public string SelectorParameter;
        public AnimatorControllerParameterType SelectorParameterType;
        public List<AnimatorOptimizerSelectorSample> SelectorSamples;
        public bool SelectorIsApproximate;
        public HashSet<string> RequiredFloatParameters = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> ReferencedParameters = new HashSet<string>(StringComparer.Ordinal);
        public string StructuralKey;
        public AnimatorOptimizerAapProgram StateRegisterProgram;
        public List<AnimatorState> StateRegisterStates;
        public string StateTag;
        public List<string> Assumptions = new List<string>();
        public HashSet<string> AssumedBinaryParameters = new HashSet<string>(StringComparer.Ordinal);
    }

    internal sealed class AnimatorOptimizerBranch
    {
        public AnimatorOptimizerMotionRef Motion;
        public List<AnimatorOptimizerCondition> Conditions;
        public int Order;
    }

    internal sealed class AnimatorOptimizerMotionRef
    {
        public Motion Motion;
        public float Speed = 1f;
        public float CycleOffset;
        public bool Mirror;
        // Read-only sampled data. No temporary Unity assets are created during analysis.
        public AnimatorOptimizerClipBake Bake;

        public AnimatorOptimizerMotionRef Clone()
        {
            return new AnimatorOptimizerMotionRef
            {
                Motion = Motion,
                Speed = Speed,
                CycleOffset = CycleOffset,
                Mirror = Mirror,
                Bake = Bake,
            };
        }
    }

    internal sealed class AnimatorOptimizerCondition
    {
        public string Parameter;
        public AnimatorControllerParameterType ParameterType;
        public AnimatorConditionMode Mode;
        public float Threshold;

        public bool IsBoolean => ParameterType == AnimatorControllerParameterType.Bool;
        public bool IsInteger => ParameterType == AnimatorControllerParameterType.Int;
        public bool IsFloat => ParameterType == AnimatorControllerParameterType.Float;

        public bool Matches(float value)
        {
            if (IsBoolean)
            {
                var boolValue = value >= 0.5f;
                return Mode switch
                {
                    AnimatorConditionMode.If => boolValue,
                    AnimatorConditionMode.IfNot => !boolValue,
                    AnimatorConditionMode.Equals => (boolValue ? 1f : 0f) == Threshold,
                    AnimatorConditionMode.NotEqual => (boolValue ? 1f : 0f) != Threshold,
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
                case AnimatorConditionMode.If:
                    return value >= 0.5f;
                case AnimatorConditionMode.IfNot:
                    return value < 0.5f;
                default:
                    return false;
            }
        }
    }

    internal sealed class AnimatorOptimizerSelectorSample
    {
        public float Value;
        public AnimatorOptimizerMotionRef Motion;
    }

    internal sealed class AnimatorOptimizerDecisionDimension
    {
        public string Parameter;
        public AnimatorControllerParameterType ParameterType;
        public List<float> Values;
        public bool IsApproximate;
    }

    internal readonly struct AnimatorOptimizerBindingKey : IEquatable<AnimatorOptimizerBindingKey>
    {
        public AnimatorOptimizerBindingKey(string path, Type type, string propertyName, bool objectReference)
        {
            Path = path ?? string.Empty;
            BindingType = type;
            PropertyName = propertyName ?? string.Empty;
            ObjectReference = objectReference;
        }

        private string Path { get; }
        private Type BindingType { get; }
        private string PropertyName { get; }
        private bool ObjectReference { get; }

        public bool Equals(AnimatorOptimizerBindingKey other)
        {
            return ObjectReference == other.ObjectReference &&
                   string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                   BindingType == other.BindingType &&
                   string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AnimatorOptimizerBindingKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Path);
                hash = hash * 397 ^ (BindingType?.GetHashCode() ?? 0);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(PropertyName);
                hash = hash * 397 ^ (ObjectReference ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            return (ObjectReference ? "PPtr:" : "Curve:") + Path + ":" + BindingType?.FullName + ":" + PropertyName;
        }

        public bool IsObjectReference => ObjectReference;

        public bool IsSafeAdditiveFloatBinding => !ObjectReference &&
            ((BindingType == typeof(Transform) &&
                (string.Equals(PropertyName, "m_LocalPosition.x", StringComparison.Ordinal) ||
                 string.Equals(PropertyName, "m_LocalPosition.y", StringComparison.Ordinal) ||
                 string.Equals(PropertyName, "m_LocalPosition.z", StringComparison.Ordinal))) ||
             (BindingType == typeof(SkinnedMeshRenderer) && PropertyName.StartsWith("blendShape.", StringComparison.Ordinal)));
    }

    public static partial class AnimatorOptimizerAnalyzer
    {
        public static AnimatorOptimizerReport Analyze(
            AnimatorController controller,
            AnimatorOptimizerOptions options = null)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            var reports = new List<AnimatorOptimizerLayerReport>(layers.Length);
            if (!IsValidControllerGraph(controller))
            {
                for (var index = 0; index < layers.Length; index++)
                {
                    var invalid = new AnimatorOptimizerLayerReport(index, layers[index]) { BindingsAreComplete = false };
                    invalid.AddReason("Controller 存在循环引用或过深的状态机/Motion；保留原图。");
                    reports.Add(invalid);
                }
                return new AnimatorOptimizerReport(controller, reports, false);
            }
            var controllerBindings = new HashSet<AnimatorOptimizerBindingKey>();
            var bindingsComplete = true;
            var visitedMachines = new HashSet<int>();
            foreach (var layer in layers)
            {
                if (layer != null && layer.syncedLayerIndex >= 0)
                    bindingsComplete = false;
                bindingsComplete &= TryCollectStateMachineMotionBindings(layer?.stateMachine, controllerBindings, visitedMachines);
            }
            for (var index = 0; index < layers.Length; index++)
                reports.Add(AnalyzeLayer(controller, index, layers[index], options, controllerBindings, bindingsComplete));

            return new AnimatorOptimizerReport(controller, reports);
        }

        private static AnimatorOptimizerLayerReport AnalyzeLayer(
            AnimatorController controller,
            int index,
            AnimatorControllerLayer layer,
            AnimatorOptimizerOptions options,
            HashSet<AnimatorOptimizerBindingKey> controllerBindings,
            bool bindingsComplete)
        {
            var report = new AnimatorOptimizerLayerReport(index, layer);
            if (layer == null)
            {
                report.AddReason("Layer 为空。");
                return report;
            }

            var configuredGeneratedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? AnimatorOptimizerConverter.GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            if (IsGeneratedLayerName(layer.name, configuredGeneratedLayerName) ||
                layer.name.StartsWith(AnimatorOptimizerConverter.GeneratedAssetPrefix, StringComparison.Ordinal))
            {
                report.AddReason("这是 AnimatorOptimizer 已生成的 Layer，避免重复优化。");
                return report;
            }

            if (layer.stateMachine == null)
            {
                report.AddReason("Layer 没有关联 StateMachine。");
                return report;
            }

            if (layer.blendingMode != AnimatorLayerBlendingMode.Override &&
                layer.blendingMode != AnimatorLayerBlendingMode.Additive)
                report.AddReason("当前 Layer 混合模式不是 Override/Additive，无法由 BlendTree 表达。");
            if (layer.syncedLayerIndex >= 0)
            {
                report.BindingsAreComplete = false; // Synced override motions are not represented by the local graph.
                report.AddReason("Synced Layer/Synced Timing 依赖其它 Layer 的状态结构或时间，暂不转换。");
            }
            else if (layer.syncedLayerAffectsTiming)
                report.AddOptimization("未设置 Synced Layer 目标，残留的 Synced Timing 标记不参与求值。");
            if (layer.iKPass)
            {
                report.AddReason("Layer 开启了 IK Pass；合并后无法保留独立 IK Pass 边界。");
                report.AddSuggestion("确认本层不需要 IK 求解或 OnAnimatorIK 回调后，再在源 Layer 关闭 IK Pass；不会仅凭静态 Clip 自动关闭。");
            }
            if (index != 0 && layer.defaultWeight != 1f)
                report.AddReason("Layer 默认权重不是 1；运行时 Animator.SetLayerWeight 无法映射为静态 Direct 权重。");
            else if (index == 0 && layer.defaultWeight != 1f)
                report.AddOptimization("首层实际权重恒为 1，忽略其未生效的 Default Weight 数值。");
            var unsupportedStateMachineBehaviours = (layer.stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Where(behaviour => !IsPreservableLayerBehaviour(behaviour))
                .ToArray();
            if (unsupportedStateMachineBehaviours.Length > 0)
            {
                report.BindingsAreComplete = false;
                report.AddReason("StateMachine 包含 StateMachineBehaviour，BlendTree 不会执行这些回调。");
            }
            else if (layer.stateMachine.behaviours != null && layer.stateMachine.behaviours.Length > 0)
            {
                if (!AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(layer.stateMachine.behaviours, out _))
                    report.AddReason("无法完整读取 StateMachine 的 MMD 标记配置，保留源层而不猜测或丢弃标记。");
                else
                    report.AddWarning("StateMachine 只包含可复制的 MMD LayerControl；合并时会复制到生成 Layer。");
            }
            var states = layer.stateMachine.states ?? Array.Empty<ChildAnimatorState>();
            var nestedStateMachines = layer.stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>();
            if (states.Where(child => child.state != null).GroupBy(child => child.state).Any(group => group.Count() > 1))
            {
                report.AddReason("State 列表包含重复引用，保留源图而不猜测状态槽位。");
                CollectAllBindings(layer.stateMachine, report);
                return report;
            }
            if (states.Length == 0 && nestedStateMachines.Length == 0)
            {
                report.AddReason("StateMachine 没有 State，无法生成 Motion。");
                return report;
            }

            var analyzableStates = GetPotentiallyReachableStates(controller, layer.stateMachine, states);
            report.Options = options;
            report.LayerMotionBindings = controllerBindings;
            report.LayerMotionBindingsAreComplete = bindingsComplete;
            report.HasExitTime = HasEffectiveExitTime(layer.stateMachine, controller);
            if (nestedStateMachines.Length == 0 && analyzableStates.Count < states.Length)
                report.AddOptimization($"忽略 {states.Length - analyzableStates.Count} 个从 Default / Entry / 有效 Transition 不可达的 State（不支持外部 Animator.Play 按名称跳转）。");
            if (nestedStateMachines.Length == 0)
            {
                var impossibleTransitions = GetEffectiveAnimatorTransitions(layer.stateMachine.entryTransitions)
                    .Cast<AnimatorTransitionBase>().Concat(GetEffectiveTransitions(layer.stateMachine.anyStateTransitions))
                    .Concat(analyzableStates.SelectMany(child => GetEffectiveTransitions(child.state?.transitions)))
                    .Count(transition => !TransitionMayBeReachable(controller, transition.conditions));
                if (impossibleTransitions > 0)
                    report.AddOptimization($"检测到 {impossibleTransitions} 条条件矛盾的有效 Transition；不会把它们作为可达输出或输入维度。");
            }

            var planningReport = CreateTrialReport(report);
            var built = TryAlternative(planningReport, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildPlan(controller, layer.stateMachine, analyzableStates, options, trial, out candidate), out var plan);
            bool HasTimedOutput() => built && EnumeratePlanMotions(plan)
                .Any(motion => motion?.Bake == null && !AnimatorOptimizerMotionUtility.IsTimeIndependent(motion?.Motion));
            void TryFinitePlan(bool allowHeuristics)
            {
                var trial = CreateTrialReport(report);
                if (TryBuildFiniteGraphPlan(controller, layer.stateMachine, analyzableStates, options, trial,
                    allowHeuristics, out var candidate) &&
                    (!built || HasTimedOutput() || PlanEntryCount(candidate) < PlanEntryCount(plan)))
                {
                    // Keep diagnostics from the chosen plan only, not a discarded
                    // AAP plan or an unused heuristic trial.
                    planningReport = trial;
                    plan = candidate;
                    built = true;
                }
                else
                {
                    planningReport.AttemptReasons.UnionWith(trial.Reasons);
                    planningReport.AttemptSuggestions.UnionWith(trial.Suggestions);
                }
            }
            if (!built || !HasTimedOutput() && PlanEntryCount(plan) > 1) TryFinitePlan(false);
            if (options.Heuristics != AnimatorOptimizerComponent.HeuristicFlags.None &&
                (!built || HasTimedOutput() || plan.Kind == AnimatorOptimizerConversionKind.AapStateRegister ||
                 options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles) && PlanEntryCount(plan) > 1))
                TryFinitePlan(true);
            if (!built)
            {
                report.AttemptReasons.UnionWith(planningReport.AttemptReasons);
                report.AttemptReasons.UnionWith(planningReport.Reasons);
                report.AttemptSuggestions.UnionWith(planningReport.AttemptSuggestions);
                report.AttemptSuggestions.UnionWith(planningReport.Suggestions);
                foreach (var reason in report.AttemptReasons)
                    report.AddReason(reason);
                foreach (var suggestion in report.AttemptSuggestions)
                    report.AddSuggestion(suggestion);
                if (report.Reasons.Count == 0)
                    report.AddReason("无法证明该状态机与无历史 BlendTree 等价。");
                if (GetEffectiveAnimatorTransitions(layer.stateMachine.entryTransitions).Count > 0)
                    report.AddReason("包含 Entry Transition，且无法证明入口目标与其它 State 的可观测效果等价。");
                CollectAllBindings(layer.stateMachine, report);
                return report;
            }

            MergeTrialReport(report, planningReport);
            CollectPlanBindings(plan, report);
            if (!ValidatePlanParameters(controller, plan, options, report))
            {
                CollectAllBindings(layer.stateMachine, report);
                return report;
            }
            var relevantTagStates = nestedStateMachines.Length == 0
                ? new HashSet<AnimatorState>(analyzableStates
                    .Where(child => child.state != null)
                    .Select(child => child.state))
                : null;
            if (!TryGetUniformStateTag(layer.stateMachine, relevantTagStates, out var stateTag))
            {
                report.AddReason("可转换 State 使用了不同的 Tag；生成的单一 Root State 无法同时保留 AnimatorStateInfo.IsTag 语义。");
                return report;
            }
            plan.StateTag = stateTag;
            report.Plan = plan;
            plan.StructuralKey = BuildStructuralKey(plan);
            report.ConversionKind = plan.Kind;
            report.AddOptimization(BuildPlanWarning(plan));
            return report;
        }

        private static bool TryGetUniformStateTag(
            AnimatorStateMachine stateMachine,
            ISet<AnimatorState> relevantStates,
            out string tag)
        {
            tag = string.Empty;
            if (stateMachine == null)
                return false;
            var tags = new HashSet<string>(StringComparer.Ordinal);
            CollectStateTags(stateMachine, tags, new HashSet<int>(), relevantStates);
            if (tags.Count > 1)
                return false;
            tag = tags.FirstOrDefault() ?? string.Empty;
            return true;
        }

        private static void CollectStateTags(
            AnimatorStateMachine stateMachine,
            ISet<string> tags,
            ISet<int> visited,
            ISet<AnimatorState> relevantStates)
        {
            if (stateMachine == null || !visited.Add(stateMachine.GetInstanceID()))
                return;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null && (relevantStates == null || relevantStates.Contains(child.state)))
                    tags.Add(child.state.tag ?? string.Empty);
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                CollectStateTags(child.stateMachine, tags, visited, relevantStates);
        }

        internal static bool IsPreservableLayerBehaviour(StateMachineBehaviour behaviour)
        {
            return behaviour != null && string.Equals(
                behaviour.GetType().FullName, "nadena.dev.modular_avatar.core.ModularAvatarMMDLayerControl", StringComparison.Ordinal);
        }

        internal static bool IsVrcGlobalParameter(string parameterName)
        {
            return TryGetVrcGlobalParameterType(parameterName, out _);
        }

        internal static bool TryGetVrcGlobalParameterType(
            string parameterName,
            out AnimatorControllerParameterType type)
        {
            type = AnimatorControllerParameterType.Float;
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
                    type = AnimatorControllerParameterType.Bool;
                    return true;
                case "Viseme":
                case "GestureLeft":
                case "GestureRight":
                case "TrackingType":
                case "VRMode":
                case "AvatarVersion":
                    type = AnimatorControllerParameterType.Int;
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
                    type = AnimatorControllerParameterType.Float;
                    return true;
                default:
                    return false;
            }
        }

        private static bool ValidatePlanParameters(
            AnimatorController controller,
            AnimatorOptimizerMotionPlan plan,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report)
        {
            if (controller == null || plan == null)
                return false;

            var parameters = new HashSet<string>(StringComparer.Ordinal);
            if (plan.ConstantMotion != null)
            {
                CollectMotionParameters(
                    plan.ConstantMotion.Motion,
                    parameters,
                    new HashSet<int>());
            }
            foreach (var motion in EnumeratePlanMotions(plan).Skip(plan.ConstantMotion == null ? 0 : 1))
                CollectMotionParameters(motion?.Motion, parameters, new HashSet<int>());
            foreach (var motion in EnumeratePlanMotions(plan))
                if (!string.IsNullOrWhiteSpace(motion?.Bake?.Parameter)) parameters.Add(motion.Bake.Parameter);

            plan.ReferencedParameters = parameters;
            foreach (var parameterName in parameters)
            {
                var parameter = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                    .FirstOrDefault(candidate => string.Equals(candidate.name, parameterName, StringComparison.Ordinal));
                if (parameter == null)
                {
                    if (IsVrcGlobalParameter(parameterName))
                        continue;
                    report.AddReason($"BlendTree 引用了不存在的 Animator 参数: {parameterName}");
                    continue;
                }

                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        if (IsVrcGlobalParameter(parameterName))
                            break;
                        if (!options.UpgradeBoolParametersToFloat)
                            report.AddReason($"BlendTree 参数 '{parameterName}' 是 Bool；需要启用 Bool → Float 才能安全放入生成树。");
                        else
                            plan.RequiredFloatParameters.Add(parameterName);
                        break;
                    case AnimatorControllerParameterType.Int:
                        if (IsVrcGlobalParameter(parameterName))
                            break;
                        if (!options.UpgradeIntParametersToFloat)
                            report.AddReason($"BlendTree 参数 '{parameterName}' 是 Int；需要启用 Int → Float 才能安全放入生成树。");
                        else
                            plan.RequiredFloatParameters.Add(parameterName);
                        break;
                    case AnimatorControllerParameterType.Float:
                        if (!IsFinite(parameter.defaultFloat))
                            report.AddReason($"Float 参数 '{parameterName}' 的默认值不是有限数；保留源图。");
                        plan.RequiredFloatParameters.Add(parameterName);
                        break;
                    default:
                        report.AddReason($"BlendTree 参数 '{parameterName}' 的类型 {parameter.type} 不能作为生成 BlendTree 参数。");
                        break;
                }
            }
            return report.Reasons.Count == 0;
        }

        private static IEnumerable<AnimatorOptimizerMotionRef> EnumeratePlanMotions(
            AnimatorOptimizerMotionPlan plan)
        {
            if (plan == null)
                yield break;
            if (plan.ConstantMotion != null)
                yield return plan.ConstantMotion;
            foreach (var branch in plan.Branches ?? Enumerable.Empty<AnimatorOptimizerBranch>())
                if (branch?.Motion != null)
                    yield return branch.Motion;
            foreach (var sample in plan.SelectorSamples ?? Enumerable.Empty<AnimatorOptimizerSelectorSample>())
                if (sample?.Motion != null)
                    yield return sample.Motion;
            foreach (var motion in plan.TruthTable?.Values ?? Enumerable.Empty<AnimatorOptimizerMotionRef>())
                if (motion != null)
                    yield return motion;
            foreach (var motion in plan.DecisionTable?.Values ?? Enumerable.Empty<AnimatorOptimizerMotionRef>())
                if (motion != null)
                    yield return motion;
            foreach (var state in plan.StateRegisterStates ?? Enumerable.Empty<AnimatorState>())
                if (state != null)
                    yield return ToMotionRef(state);
        }

        private static void CollectMotionParameters(
            Motion motion,
            ISet<string> parameters,
            ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return;

            AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(motion, out var normalized);
            if (!ReferenceEquals(normalized, motion))
            {
                CollectMotionParameters(normalized, parameters, visited);
                return;
            }

            if (!(motion is BlendTree tree))
                return;

            if (tree.blendType != BlendTreeType.Direct)
            {
                if (!string.IsNullOrWhiteSpace(tree.blendParameter))
                    parameters.Add(tree.blendParameter);
                if (tree.blendType != BlendTreeType.Simple1D && !string.IsNullOrWhiteSpace(tree.blendParameterY))
                    parameters.Add(tree.blendParameterY);
            }
            foreach (var child in tree.children ?? Array.Empty<ChildMotion>())
            {
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrWhiteSpace(child.directBlendParameter))
                {
                    parameters.Add(child.directBlendParameter);
                }
                CollectMotionParameters(child.motion, parameters, visited);
            }
        }

        private static List<ChildAnimatorState> GetPotentiallyReachableStates(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> states)
        {
            var allStates = states.Where(child => child.state != null).ToList();
            if (allStates.Count == 0)
                return allStates;
            var stateSet = new HashSet<AnimatorState>(allStates.Select(child => child.state));
            var reachable = new HashSet<AnimatorState>();
            var pending = new Queue<AnimatorState>();

            void Add(AnimatorState state)
            {
                if (state != null && stateSet.Contains(state) && reachable.Add(state))
                    pending.Enqueue(state);
            }

            Add(stateMachine.defaultState);
            foreach (var transition in GetEffectiveAnimatorTransitions(stateMachine.entryTransitions))
            {
                if (transition == null || !TransitionMayBeReachable(controller, transition.conditions))
                    continue;
                Add(transition.destinationState);
            }

            // Root AnyState destinations do not depend on which reachable state
            // is visited. Inspect each edge once instead of states × AnyState edges.
            if (pending.Count > 0)
                foreach (var transition in GetEffectiveTransitions(stateMachine.anyStateTransitions))
                    if (TransitionMayBeReachable(controller, transition.conditions))
                        Add(transition.destinationState);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var transition in GetEffectiveTransitions(current.transitions))
                {
                    if (TransitionMayBeReachable(controller, transition.conditions))
                        Add(transition.destinationState);
                }
            }

            return allStates.Where(child => reachable.Contains(child.state)).ToList();
        }

        private static List<AnimatorStateTransition> GetEffectiveTransitions(
            IEnumerable<AnimatorStateTransition> transitions)
        {
            var active = (transitions ?? Enumerable.Empty<AnimatorStateTransition>())
                .Where(transition => transition != null && !transition.mute)
                .ToList();
            if (active.Any(transition => transition.solo))
                active = active.Where(transition => transition.solo).ToList();
            // Unity ignores State/AnyState transitions with neither conditions nor
            // Exit Time. Entry transitions are a different type and stay eligible.
            return active.Where(transition => transition.hasExitTime || transition.conditions.Length > 0).ToList();
        }

        private static List<AnimatorTransition> GetEffectiveAnimatorTransitions(
            IEnumerable<AnimatorTransition> transitions)
        {
            var active = (transitions ?? Enumerable.Empty<AnimatorTransition>())
                .Where(transition => transition != null && !transition.mute)
                .ToList();
            if (active.Any(transition => transition.solo))
                active = active.Where(transition => transition.solo).ToList();
            return active;
        }

        private static bool TransitionMayBeReachable(
            AnimatorController controller,
            IReadOnlyList<AnimatorCondition> conditions)
        {
            if (controller == null) return true;
            var declarations = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            return AnimatorOptimizerSemanticUtility.ConditionsMayBeReachable(conditions, name =>
            {
                // A duplicate declaration is not a well-defined proof domain.
                var matches = declarations.Where(candidate => candidate != null &&
                    string.Equals(candidate.name, name, StringComparison.Ordinal)).Take(2).ToArray();
                if (matches.Length > 1) return null;
                return matches.Length == 1 ? matches[0].type : TryGetVrcGlobalParameterType(name, out var type)
                    ? type : (AnimatorControllerParameterType?)null;
            });
        }

        private static bool IsGeneratedLayerName(string name, string configuredName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (string.Equals(name, AnimatorOptimizerConverter.GeneratedLayerName, StringComparison.Ordinal) ||
                name.StartsWith(AnimatorOptimizerConverter.GeneratedLayerName + "/", StringComparison.Ordinal))
                return true;
            return !string.IsNullOrWhiteSpace(configuredName) &&
                   (string.Equals(name, configuredName, StringComparison.Ordinal) ||
                    name.StartsWith(configuredName + "/", StringComparison.Ordinal));
        }

        private static bool TryBuildPlan(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> childStates,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (stateMachine.stateMachines != null && stateMachine.stateMachines.Length > 0)
            {
                if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                    TryBuildNestedWrapperPlan(controller, stateMachine, options, trial, out candidate), out plan))
                    return true;
                if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                    TryBuildNestedEffectEquivalentPlan(controller, stateMachine, trial, out candidate), out plan))
                    return true;
                if (!TryResolveNestedStaticDefault(stateMachine, out var nestedDefault))
                {
                    report.BindingsAreComplete = false;
                    report.AddReason("嵌套 StateMachine 含有局部状态/Transition；只有纯组织层级的静态嵌套图可以扁平化。");
                    return false;
                }

                if (!ValidateState(controller, nestedDefault, report))
                    return false;
                plan = CreateConstantPlan(nestedDefault);
                return true;
            }

            var defaultState = stateMachine.defaultState;
            if (defaultState == null || !childStates.Any(child => child.state == defaultState))
            {
                report.AddReason("StateMachine 缺少有效 Default State。");
                return false;
            }

            var allStatesAreStatic = childStates.All(child =>
                child.state != null && !GetEffectiveAapTransitions(child.state.transitions)
                    .Any(transition => TransitionMayBeReachable(controller, transition.conditions)));
            var effectiveAnyTransitions = GetEffectiveAapTransitions(stateMachine.anyStateTransitions)
                .Where(transition => TransitionMayBeReachable(controller, transition.conditions)).ToList();
            var effectiveEntryTransitions = GetEffectiveAnimatorTransitions(stateMachine.entryTransitions)
                .Where(transition => TransitionMayBeReachable(controller, transition.conditions)).ToList();
            var noAnyTransitions = effectiveAnyTransitions.Count == 0;
            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildEffectEquivalentPlan(controller, stateMachine, childStates, trial, out candidate), out plan))
            {
                report.AddOptimization("State/Transition 可能包含 Exit、Duration 或 Entry 逻辑，但所有可达 Motion 的可观测效果完全相同，已折叠为单一 Motion。");
                return true;
            }

            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildEntryDrivenPlan(controller, stateMachine, childStates, options, trial, out candidate), out plan))
            {
                report.AddOptimization("Entry 条件与 State Exit 条件在有限参数域内组成无历史函数，已编译为决策 BlendTree。");
                return true;
            }

            if (allStatesAreStatic && noAnyTransitions &&
                effectiveEntryTransitions.Count == 0)
            {
                if (!ValidateState(controller, defaultState, report))
                    return false;

                plan = CreateConstantPlan(defaultState);
                return true;
            }

            // A state-dependent graph can still be a pure function when every possible
            // source state produces the same output for each finite parameter value.
            // Prove that convergence before falling back to the more restrictive branch
            // extraction rules below. Any State transitions participate with their
            // priority over per-state transitions.
            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildStateIndependentOutputPlan(controller, stateMachine, childStates, options, trial, out candidate), out plan))
            {
                report.AddOptimization("已按原始优先级分析条件分支及无条件回退；所有源 State 对每组有限输入收敛到相同输出，已消除状态历史。");
                return true;
            }

            if (options.EnableAapStateRegisterOptimization &&
                (effectiveEntryTransitions.Count > 0 || effectiveAnyTransitions.Count > 0 ||
                 childStates.Any(child => child.state != null && GetEffectiveTransitions(child.state.transitions).Count > 0)) &&
                TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                    TryBuildAapStateRegisterPlan(controller, stateMachine, childStates, options, trial, out candidate), out plan))
            {
                report.AddWarning("AAP 按每次逻辑求值更新状态寄存器，支持混合条件并保留未命中时的历史；反馈延迟与首帧行为仍需 Unity 验证，不能视为逐帧等价证明。");
                return true;
            }

            if (effectiveAnyTransitions.Count > 0 &&
                childStates.Any(child => child.state != null && GetEffectiveAapTransitions(child.state.transitions).Count > 0))
            {
                report.AddReason("同时包含 Any State 和 State Transition；当前版本无法证明状态优先级与历史无关。");
                return false;
            }

            var transitions = new List<AnimatorStateTransition>();
            if (effectiveAnyTransitions.Count > 0)
            {
                if (effectiveAnyTransitions.Any(transition => !transition.canTransitionToSelf))
                {
                    report.AddReason("Any State 禁止自转移，且未通过逐源状态的优先级证明；不能直接用第一命中分支替代。");
                    return false;
                }
                transitions.AddRange(effectiveAnyTransitions);
            }
            else
            {
                if (childStates.Count == 2 && childStates.All(child =>
                        child.state != null && GetEffectiveAapTransitions(child.state.transitions).Count == 1))
                {
                    transitions.AddRange(childStates.Select(child => GetEffectiveAapTransitions(child.state.transitions)[0]));
                }
                else if (!TryExtractStateIndependentTransitions(childStates, options.IgnoreTransitionTimingForStaticMotions, out transitions))
                {
                    report.AddReason("State Transition 图依赖当前 State；只有两态互斥 Toggle 或所有 State 使用同一转移表时才可转换。");
                    return false;
                }
            }

            var stateDependentToggle = noAnyTransitions && childStates.Count == 2 &&
                                       childStates.All(child => child.state != null &&
                                                               GetEffectiveAapTransitions(child.state.transitions).Count == 1);
            if (stateDependentToggle && TransitionGroupsCanOverlap(transitions[0], transitions[1], controller))
            {
                report.AddReason("两态 State Transition 的条件存在重叠；原图会依赖当前 State 和 Transition 优先级，不能直接变成无状态 BlendTree。");
                return false;
            }

            if (!TryBuildBranches(controller, childStates, transitions, options, report, out var branches))
                return false;

            if (branches.Count == 0)
            {
                report.AddReason("没有可转换的参数状态分支。");
                return false;
            }

            if (branches.Any(branch => branch.Conditions == null || branch.Conditions.Count == 0))
            {
                report.AddReason("缺少状态条件；非参数驱动的状态保持依赖运行时历史，不能直接转为 BlendTree。");
                return false;
            }

            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildBooleanTruthTable(branches, options, trial, out candidate), out plan))
                return true;
            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildNumericSelector(branches, options, trial, out candidate), out plan))
                return true;
            if (TryAlternative(report, (AnimatorOptimizerLayerReport trial, out AnimatorOptimizerMotionPlan candidate) =>
                TryBuildFiniteParameterTable(controller, branches, options, trial, out candidate), out plan))
                return true;

            report.AddReason("状态条件同时涉及多个数值参数，或包含无法精确表达的逻辑组合；当前版本不会生成可能改变语义的近似树。");
            return false;
        }

        private delegate bool PlanAttempt(AnimatorOptimizerLayerReport report, out AnimatorOptimizerMotionPlan plan);

        private static int PlanEntryCount(AnimatorOptimizerMotionPlan plan) =>
            plan?.Kind == AnimatorOptimizerConversionKind.AapStateRegister ? int.MaxValue :
            plan?.TruthTable?.Count ?? plan?.DecisionTable?.Count ?? plan?.SelectorSamples?.Count ?? 1;

        private static AnimatorOptimizerLayerReport CreateTrialReport(AnimatorOptimizerLayerReport report) =>
            new AnimatorOptimizerLayerReport(report.Index, report.SourceLayer)
            {
                Options = report.Options,
                LayerMotionBindings = report.LayerMotionBindings,
                LayerMotionBindingsAreComplete = report.LayerMotionBindingsAreComplete,
                HasExitTime = report.HasExitTime,
            };

        private static bool TryAlternative(AnimatorOptimizerLayerReport report, PlanAttempt attempt,
            out AnimatorOptimizerMotionPlan plan)
        {
            var trial = CreateTrialReport(report);
            if (!attempt(trial, out plan))
            {
                report.AttemptReasons.UnionWith(trial.Reasons);
                report.AttemptReasons.UnionWith(trial.AttemptReasons);
                report.AttemptSuggestions.UnionWith(trial.Suggestions);
                report.AttemptSuggestions.UnionWith(trial.AttemptSuggestions);
                plan = null;
                return false;
            }
            MergeTrialReport(report, trial);
            return true;
        }

        private static void MergeTrialReport(AnimatorOptimizerLayerReport report, AnimatorOptimizerLayerReport trial)
        {
            foreach (var reason in trial.Reasons)
                report.AddReason(reason);
            foreach (var warning in trial.Warnings)
                report.AddWarning(warning);
            foreach (var optimization in trial.Optimizations)
                report.AddOptimization(optimization);
            foreach (var suggestion in trial.Suggestions)
                report.AddSuggestion(suggestion);
            report.WriteDefaultsOffStates.UnionWith(trial.WriteDefaultsOffStates);
            report.RelaxedTransitionCount += trial.RelaxedTransitionCount;
            report.BindingsAreComplete &= trial.BindingsAreComplete;
        }

        internal static bool IsValidControllerGraph(AnimatorController controller)
        {
            if (controller == null) return false;
            var layers = controller.layers;
            var done = new HashSet<int>();
            if (!layers.All(layer => IsValidStateMachineGraph(layer?.stateMachine, new HashSet<int>(), done)))
                return false;
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                if (layer == null || layer.syncedLayerIndex < 0) continue;
                foreach (var state in EnumerateLayerSourceStates(layers, index))
                    if (!AnimatorOptimizerMotionUtility.IsAcyclic(layer.GetOverrideMotion(state))) return false;
            }
            return true;
        }

        // Include all potential source states of a synced chain. This is a union
        // for conservative reference/writer inspection, not a playback evaluator.
        internal static IEnumerable<AnimatorState> EnumerateLayerSourceStates(
            IReadOnlyList<AnimatorControllerLayer> layers, int index)
        {
            var visitedLayers = new HashSet<int>();
            var machines = new HashSet<AnimatorStateMachine>();
            var states = new HashSet<AnimatorState>();
            while (index >= 0 && index < layers.Count && visitedLayers.Add(index))
            {
                var layer = layers[index];
                if (layer == null) yield break;
                var pending = new Stack<AnimatorStateMachine>();
                if (layer.stateMachine != null) pending.Push(layer.stateMachine);
                while (pending.Count > 0)
                {
                    var machine = pending.Pop();
                    if (!machines.Add(machine)) continue;
                    foreach (var child in machine.states)
                        if (child.state != null && states.Add(child.state)) yield return child.state;
                    foreach (var child in machine.stateMachines)
                        if (child.stateMachine != null) pending.Push(child.stateMachine);
                }
                index = layer.syncedLayerIndex;
            }
        }

        private static bool IsValidStateMachineGraph(AnimatorStateMachine machine, ISet<int> active, ISet<int> done)
        {
            if (machine == null || done.Contains(machine.GetInstanceID()))
                return true;
            if (active.Count >= 128 || !active.Add(machine.GetInstanceID()))
                return false;
            foreach (var child in machine.states ?? Array.Empty<ChildAnimatorState>())
                if (child.state != null && !AnimatorOptimizerMotionUtility.IsAcyclic(child.state.motion))
                    return false;
            foreach (var child in machine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                if (!IsValidStateMachineGraph(child.stateMachine, active, done))
                    return false;
            active.Remove(machine.GetInstanceID());
            done.Add(machine.GetInstanceID());
            return true;
        }

        private static bool TryBuildNestedWrapperPlan(
            AnimatorController controller,
            AnimatorStateMachine rootStateMachine,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            var childMachines = rootStateMachine?.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>();
            if (rootStateMachine == null || childMachines.Length != 1 ||
                (rootStateMachine.states ?? Array.Empty<ChildAnimatorState>()).Length != 0 ||
                (rootStateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>()).Length != 0 ||
                GetEffectiveTransitions(rootStateMachine.anyStateTransitions).Count != 0)
                return false;

            var childMachine = childMachines[0].stateMachine;
            if (childMachine == null ||
                (childMachine.behaviours ?? Array.Empty<StateMachineBehaviour>()).Length != 0 ||
                childMachine.states.Any(child => child.state != null &&
                    GetEffectiveTransitions(child.state.transitions).Any(transition => transition.isExit)) ||
                GetEffectiveTransitions(childMachine.anyStateTransitions).Any(transition => transition.isExit) ||
                GetEffectiveAnimatorTransitions(rootStateMachine.GetStateMachineTransitions(childMachine)).Count != 0)
                return false;

            var entries = GetEffectiveAnimatorTransitions(rootStateMachine.entryTransitions);
            if (entries.Count != 1)
                return false;
            var entry = entries[0];
            if (entry.destinationStateMachine != childMachine || entry.destinationState != null ||
                entry.isExit || (entry.conditions ?? Array.Empty<AnimatorCondition>()).Length != 0)
                return false;

            var childStates = GetPotentiallyReachableStates(
                controller,
                childMachine,
                childMachine.states ?? Array.Empty<ChildAnimatorState>());
            return TryBuildPlan(controller, childMachine, childStates, options, report, out plan);
        }

        private static bool TryBuildEntryDrivenPlan(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> childStates,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (stateMachine == null || childStates.Count == 0 ||
                GetEffectiveAnimatorTransitions(stateMachine.entryTransitions).Count == 0 ||
                GetEffectiveAapTransitions(stateMachine.anyStateTransitions).Count > 0 ||
                stateMachine.defaultState == null || childStates.All(child => child.state != stateMachine.defaultState))
                return false;

            var states = childStates.Select(child => child.state).Where(state => state != null).ToList();
            var stateSet = new HashSet<AnimatorState>(states);
            var entryTransitions = new List<NormalizedStateTransition>();
            var effectiveEntryTransitions = GetEffectiveAnimatorTransitions(stateMachine.entryTransitions)
                .Where(transition => TransitionMayBeReachable(controller, transition.conditions)).ToList();
            for (var order = 0; order < effectiveEntryTransitions.Count; order++)
            {
                var transition = effectiveEntryTransitions[order];
                if (transition == null || transition.destinationState == null ||
                    transition.destinationStateMachine != null || !stateSet.Contains(transition.destinationState) ||
                    transition.isExit)
                    return false;
                var conditions = new List<AnimatorOptimizerCondition>();
                foreach (var sourceCondition in transition.conditions ?? Array.Empty<AnimatorCondition>())
                {
                    if (!TryNormalizeCondition(controller, sourceCondition, options, report, out var condition))
                        return false;
                    conditions.Add(condition);
                }
                entryTransitions.Add(new NormalizedStateTransition
                {
                    Destination = transition.destinationState,
                    Conditions = conditions,
                    Order = order,
                });
            }

            if (entryTransitions.Count == 1 && entryTransitions[0].Conditions.Count == 0 &&
                GetEffectiveAapTransitions(stateMachine.anyStateTransitions).Count == 0 &&
                states.All(state => GetEffectiveAapTransitions(state.transitions).Count == 0))
            {
                var entryState = entryTransitions[0].Destination;
                if (!ValidateState(controller, entryState, report))
                    return false;
                plan = CreateConstantPlan(entryState);
                return true;
            }

            var exitTransitions = new Dictionary<AnimatorState, List<NormalizedStateTransition>>();
            var allConditions = new List<AnimatorOptimizerCondition>();
            foreach (var state in states)
            {
                if (!ValidateState(controller, state, report, allowTransitions: true) ||
                    !IsTimeIndependentMotion(state.motion))
                    return false;
                var exits = new List<NormalizedStateTransition>();
                foreach (var transition in GetEffectiveAapTransitions(state.transitions))
                {
                    if (!TransitionMayBeReachable(controller, transition.conditions))
                        continue;
                    if (transition == null || !transition.isExit || transition.destinationState != null ||
                        transition.destinationStateMachine != null ||
                        HasBlockingTransitionTiming(transition, report))
                        return false;
                    var conditions = new List<AnimatorOptimizerCondition>();
                    foreach (var sourceCondition in transition.conditions ?? Array.Empty<AnimatorCondition>())
                    {
                        if (!TryNormalizeCondition(controller, sourceCondition, options, report, out var condition))
                            return false;
                        conditions.Add(condition);
                        allConditions.Add(condition);
                    }
                    exits.Add(new NormalizedStateTransition
                    {
                        Source = state,
                        Conditions = conditions,
                        Order = exits.Count,
                    });
                }
                exitTransitions[state] = exits;
            }
            allConditions.AddRange(entryTransitions.SelectMany(transition => transition.Conditions));
            if (!TryBuildDecisionDimensionsFromConditions(allConditions, options, report,
                    out var dimensions, out var combinationCount))
                return false;
            if (combinationCount > 65536L / states.Count)
            {
                report.AddReason("Entry/Exit 证明的状态数 × 输入组合数超过 65536，保留源图。");
                return false;
            }
            if (dimensions.Any(dimension => dimension.IsApproximate))
                report.AddWarning("Entry/Exit 决策包含 Float 条件；AAP/BlendTree 只在阈值邻域采样，边界处属于近似语义。");

            var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
            var indices = new int[dimensions.Count];
            var outputKeys = states.ToDictionary(state => state, state => BuildMotionKey(ToMotionRef(state)));
            for (var combination = 0; combination < (int)combinationCount; combination++)
            {
                var values = BuildDecisionDimensionValues(dimensions, indices);
                var selectedEntry = entryTransitions.OrderBy(transition => transition.Order)
                    .FirstOrDefault(transition => transition.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                var desired = selectedEntry?.Destination ?? stateMachine.defaultState;
                foreach (var state in states)
                {
                    var exits = exitTransitions[state];
                    var exitsNow = exits.Any(transition => transition.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                    if (!string.Equals(outputKeys[exitsNow ? desired : state], outputKeys[desired], StringComparison.Ordinal))
                    {
                        report.AddReason($"Entry/Exit 不构成无历史函数：State '{state.name}' 在输入 " +
                            string.Join(", ", values.Select(pair => pair.Key + "=" + pair.Value.ToString("R", CultureInfo.InvariantCulture))) +
                            " 下会保持旧状态或重复重入，不能直接替换为入口选择树。");
                        report.AddSuggestion("旧 MA 响应式层的 Greater/Less 等值边界可能保留历史；同步状态机合并可保留原 Entry/Exit 逻辑，不需强制 Float 近似或 WD On。");
                        return false;
                    }
                }
                table[BuildDecisionDimensionKey(dimensions, indices)] = ToMotionRef(desired);
                IncrementDecisionDimensionIndices(dimensions, indices);
            }

            plan = BuildDecisionPlanFromTable(entryTransitions, dimensions, table, controller);
            return plan != null;
        }

        private static bool TryBuildDecisionDimensionsFromConditions(
            IReadOnlyList<AnimatorOptimizerCondition> conditions,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out List<AnimatorOptimizerDecisionDimension> dimensions,
            out long combinationCount)
        {
            dimensions = new List<AnimatorOptimizerDecisionDimension>();
            combinationCount = 1;
            foreach (var group in (conditions ?? Array.Empty<AnimatorOptimizerCondition>())
                         .GroupBy(condition => condition.Parameter, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var list = group.ToList();
                var type = list[0].ParameterType;
                if (list.Any(condition => condition.ParameterType != type))
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
                {
                    report.AddReason($"参数 '{group.Key}' 需要 {values.Count} 个条件分区端点，超过单轴上限 {options.MaxNumericSelectorSamples}。");
                    return false;
                }
                if (values.Count == 0 || combinationCount > long.MaxValue / values.Count)
                    return false;
                combinationCount *= values.Count;
                dimensions.Add(new AnimatorOptimizerDecisionDimension
                {
                    Parameter = group.Key,
                    ParameterType = type,
                    Values = values.ToList(),
                    IsApproximate = approximate,
                });
            }
            var limit = dimensions.All(dimension => dimension.ParameterType == AnimatorControllerParameterType.Bool)
                ? Math.Max(2, options.MaxBooleanTruthTableEntries)
                : Math.Max(2, options.MaxPriorityCombinations);
            if (combinationCount > limit)
            {
                report.AddReason($"Entry/Exit 条件需要 {combinationCount} 个有限输入组合，超过上限 {limit}。");
                return false;
            }
            return true;
        }

        private static AnimatorOptimizerMotionPlan BuildDecisionPlanFromTable(
            IReadOnlyList<NormalizedStateTransition> branches,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            IReadOnlyDictionary<int, AnimatorOptimizerMotionRef> table,
            AnimatorController controller)
        {
            if (dimensions.Count == 0)
                return new AnimatorOptimizerMotionPlan
                {
                    Kind = AnimatorOptimizerConversionKind.ConstantMotion,
                    ConstantMotion = table[0].Clone(),
                };
            var required = new HashSet<string>(dimensions.Select(dimension => dimension.Parameter), StringComparer.Ordinal);
            if (dimensions.All(dimension => dimension.ParameterType == AnimatorControllerParameterType.Bool))
            {
                return new AnimatorOptimizerMotionPlan
                {
                    Kind = AnimatorOptimizerConversionKind.BooleanTruthTable,
                    BooleanParameters = dimensions.Select(dimension => dimension.Parameter).ToList(),
                    TruthTable = table.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
                    RequiredFloatParameters = required,
                };
            }
            if (dimensions.Count == 1)
            {
                var dimension = dimensions[0];
                return new AnimatorOptimizerMotionPlan
                {
                    Kind = AnimatorOptimizerConversionKind.NumericSelector,
                    SelectorParameter = dimension.Parameter,
                    SelectorParameterType = dimension.ParameterType,
                    SelectorIsApproximate = dimension.IsApproximate,
                    SelectorSamples = dimension.Values.Select((value, index) => new AnimatorOptimizerSelectorSample
                    {
                        Value = value,
                        Motion = table[index].Clone(),
                    }).ToList(),
                    RequiredFloatParameters = required,
                };
            }
            return new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.FiniteParameterTable,
                DecisionDimensions = dimensions.ToList(),
                DecisionTable = table.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
                RequiredFloatParameters = required,
            };
        }

        private static bool TryBuildNestedEffectEquivalentPlan(
            AnimatorController controller,
            AnimatorStateMachine rootStateMachine,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            var states = new List<AnimatorState>();
            var stateMachines = new HashSet<AnimatorStateMachine>();
            CollectNestedStateGraph(rootStateMachine, states, stateMachines);
            if (states.Count == 0 || rootStateMachine == null ||
                rootStateMachine.defaultState == null &&
                GetEffectiveAnimatorTransitions(rootStateMachine.entryTransitions).Count == 0)
                return false;

            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (!ValidateEffectEquivalentStaticState(state, report) ||
                    !TryBuildStaticObservableMotionSignature(state.motion, out var signature))
                    return false;
                signatures.Add(signature);
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;

            foreach (var stateMachine in stateMachines)
            {
                if (!HasOnlyReadOnlyTransitionConditions(controller, stateMachine,
                    stateMachine.states.Where(child => child.state != null).Select(child => child.state)))
                {
                    report.AddReason("Transition 可能消费 Trigger 或参数类型未知；输出相同也不能删除条件副作用。");
                    return false;
                }
                if (stateMachine != rootStateMachine && stateMachine.behaviours.Length > 0 ||
                    (stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
                        .Any(behaviour => !IsPreservableLayerBehaviour(behaviour)) ||
                    !AllNestedTransitionsAreLocal(stateMachine, states, stateMachines))
                    return false;
            }

            plan = CreateConstantPlan(states[0]);
            return true;
        }

        private static void CollectNestedStateGraph(
            AnimatorStateMachine stateMachine,
            ICollection<AnimatorState> states,
            ISet<AnimatorStateMachine> stateMachines)
        {
            if (stateMachine == null || !stateMachines.Add(stateMachine))
                return;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null)
                    states.Add(child.state);
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                CollectNestedStateGraph(child.stateMachine, states, stateMachines);
        }

        private static bool AllNestedTransitionsAreLocal(
            AnimatorStateMachine stateMachine,
            IReadOnlyCollection<AnimatorState> states,
            ISet<AnimatorStateMachine> stateMachines)
        {
            foreach (var transition in GetEffectiveAnimatorTransitions(stateMachine.entryTransitions))
            {
                if (!IsLocalNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var transition in GetEffectiveTransitions(stateMachine.anyStateTransitions))
            {
                if (!IsLocalNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                foreach (var transition in GetEffectiveTransitions(child.state?.transitions))
                {
                    if (!IsLocalNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                foreach (var transition in GetEffectiveAnimatorTransitions(
                             stateMachine.GetStateMachineTransitions(child.stateMachine)))
                {
                    if (!IsLocalNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            return true;
        }

        private static bool IsLocalNestedTransition(
            AnimatorTransitionBase transition,
            IReadOnlyCollection<AnimatorState> states,
            ISet<AnimatorStateMachine> stateMachines)
        {
            if (transition == null || transition.isExit)
                return false;
            if (transition.destinationState != null)
                return states.Contains(transition.destinationState);
            if (transition.destinationStateMachine != null)
                return stateMachines.Contains(transition.destinationStateMachine);
            return false;
        }

        private static bool TryBuildFiniteParameterTable(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerBranch> branches,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (branches == null || branches.Count == 0 ||
                branches.Any(branch => branch.Conditions == null || branch.Conditions.Count == 0) ||
                branches.SelectMany(branch => branch.Conditions).Any(condition =>
                    !condition.IsBoolean && !condition.IsInteger &&
                    !condition.IsFloat))
                return false;

            var parameterNames = branches.SelectMany(branch => branch.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(parameter => parameter, StringComparer.Ordinal)
                .ToList();
            if (parameterNames.Count == 0)
                return false;

            var dimensions = new List<AnimatorOptimizerDecisionDimension>();
            foreach (var parameterName in parameterNames)
            {
                var conditions = branches.SelectMany(branch => branch.Conditions)
                    .Where(condition => string.Equals(condition.Parameter, parameterName, StringComparison.Ordinal))
                    .ToList();
                var type = conditions[0].ParameterType;
                if (conditions.Any(condition => condition.ParameterType != type))
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

                dimensions.Add(new AnimatorOptimizerDecisionDimension
                {
                    Parameter = parameterName,
                    ParameterType = type,
                    Values = values.ToList(),
                    IsApproximate = approximate,
                });
            }

            long combinationCount = 1;
            foreach (var dimension in dimensions)
            {
                if (dimension.Values.Count == 0 || dimension.Values.Count > options.MaxNumericSelectorSamples ||
                    combinationCount > long.MaxValue / dimension.Values.Count)
                    return false;
                combinationCount *= dimension.Values.Count;
            }
            if (combinationCount > Math.Max(2, options.MaxPriorityCombinations))
            {
                report.AddReason($"Bool/Int 混合条件需要 {combinationCount} 个组合，超过有限决策表上限 {options.MaxPriorityCombinations}。");
                return false;
            }
            if (dimensions.Any(dimension => dimension.IsApproximate))
                report.AddWarning("有限决策表包含 Float 条件；阈值区间由 1D BlendTree 线性插值近似，边界附近可能与 Animator 阶跃判断不同。");

            var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
            var indices = new int[dimensions.Count];
            for (var combination = 0; combination < (int)combinationCount; combination++)
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
                {
                    report.AddReason("Bool/Int 混合 Transition 没有覆盖完整有限参数域；未命中时原状态机依赖历史，不能转换为无状态决策树。");
                    return false;
                }
                table[key] = matched.Motion.Clone();

                for (var dimensionIndex = 0; dimensionIndex < indices.Length; dimensionIndex++)
                {
                    if (++indices[dimensionIndex] < dimensions[dimensionIndex].Values.Count)
                        break;
                    indices[dimensionIndex] = 0;
                }
            }

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.FiniteParameterTable,
                Branches = branches.ToList(),
                DecisionDimensions = dimensions,
                DecisionTable = table,
                RequiredFloatParameters = new HashSet<string>(parameterNames, StringComparer.Ordinal),
            };
            return true;
        }

        private static AnimatorOptimizerMotionPlan CreateConstantPlan(AnimatorState state)
        {
            return new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.ConstantMotion,
                ConstantMotion = ToMotionRef(state),
                StructuralKey = "constant:" + (state.motion == null ? "null" : state.motion.GetInstanceID()),
            };
        }

        private static string BuildStructuralKey(AnimatorOptimizerMotionPlan plan)
        {
            if (plan == null)
                return "null";

            string Sequence(IEnumerable<string> fields) => string.Concat(fields.Select(AnimatorOptimizerSynchronizedGraph.Atom));
            string Pack(params string[] fields) => Sequence(fields);
            string Dimensions(IEnumerable<AnimatorOptimizerDecisionDimension> dimensions) => Sequence(
                (dimensions ?? Enumerable.Empty<AnimatorOptimizerDecisionDimension>()).Select(dimension => Pack(
                    dimension.Parameter, dimension.ParameterType.ToString(), dimension.IsApproximate.ToString(),
                    Sequence(dimension.Values.Select(value => value.ToString("R", CultureInfo.InvariantCulture))))));
            string Table(IReadOnlyDictionary<int, AnimatorOptimizerMotionRef> table) => Sequence(
                (table ?? new Dictionary<int, AnimatorOptimizerMotionRef>()).OrderBy(pair => pair.Key)
                    .Select(pair => Pack(pair.Key.ToString(CultureInfo.InvariantCulture), BuildMotionKey(pair.Value))));
            var prefix = "plan:" + AnimatorOptimizerSynchronizedGraph.Atom(plan.StateTag);
            if (plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion)
                return prefix + Pack("constant", BuildMotionKey(plan.ConstantMotion));
            if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
                return prefix + Pack("bool", Sequence(plan.BooleanParameters ?? new List<string>()), Table(plan.TruthTable));
            if (plan.Kind == AnimatorOptimizerConversionKind.NumericSelector)
                return prefix + Pack("numeric", plan.SelectorParameter, plan.SelectorParameterType.ToString(),
                    plan.SelectorIsApproximate.ToString(), Sequence((plan.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>())
                        .Select(sample => Pack(sample.Value.ToString("R", CultureInfo.InvariantCulture), BuildMotionKey(sample.Motion)))));
            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
                return prefix + Pack("finite", Dimensions(plan.DecisionDimensions), Table(plan.DecisionTable));
            if (plan.Kind == AnimatorOptimizerConversionKind.AapStateRegister)
            {
                return prefix + Pack("aap-symbolic", plan.StateRegisterProgram.StructuralKey,
                    Sequence(plan.StateRegisterStates.Select(state => BuildMotionKey(ToMotionRef(state)))));
            }
            return prefix + Pack("unknown", AnimatorOptimizerSemanticUtility.ReferenceKey(plan));
        }

        private static string BuildMotionKey(AnimatorOptimizerMotionRef motion)
        {
            if (motion == null)
                return "null";
            if (motion.Bake != null) return motion.Bake.Key;
            // Static output equivalence is independent of redundant key times/duration.
            if (!motion.Mirror && TryBuildStaticObservableMotionSignature(motion.Motion, out var staticKey))
                return staticKey;
            return AnimatorOptimizerSynchronizedGraph.Atom(AnimatorOptimizerMotionUtility.BuildStructuralKey(motion.Motion)) + ":" +
                   motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.Mirror;
        }

        private static bool TryExtractStateIndependentTransitions(
            IReadOnlyList<ChildAnimatorState> childStates,
            bool ignoreTiming,
            out List<AnimatorStateTransition> transitions)
        {
            transitions = null;
            if (childStates.Count == 0 || childStates.Any(child => child.state == null))
                return false;

            var first = GetEffectiveTransitions(childStates[0].state.transitions).ToArray();
            if (first.Length == 0)
                return false;

            foreach (var child in childStates.Skip(1))
            {
                var current = GetEffectiveTransitions(child.state.transitions).ToArray();
                if (current.Length != first.Length)
                    return false;
                for (var index = 0; index < first.Length; index++)
                {
                    if (!AreEquivalentTransition(first[index], current[index], ignoreTiming))
                        return false;
                }
            }

            transitions = first.ToList();
            return true;
        }

        private sealed class NormalizedStateTransition
        {
            public AnimatorState Source;
            public AnimatorState Destination;
            public List<AnimatorOptimizerCondition> Conditions;
            public int Order;
            // Only meaningful for Any State transitions (Source == null).
            public bool CanTransitionToSelf = true;
        }

        private static bool TryBuildStateIndependentOutputPlan(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> childStates,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (stateMachine == null || childStates.Count < 2 ||
                childStates.Any(child => child.state == null) ||
                GetEffectiveAnimatorTransitions(stateMachine.entryTransitions)
                    .Any(transition => TransitionMayBeReachable(controller, transition.conditions)))
                return false;

            var states = childStates.Select(child => child.state).ToList();
            var stateSet = new HashSet<AnimatorState>(states);
            var transitions = new List<NormalizedStateTransition>();
            var anyTransitions = new List<NormalizedStateTransition>();
            var effectiveAnyTransitions = GetEffectiveTransitions(stateMachine.anyStateTransitions);
            for (var order = 0; order < effectiveAnyTransitions.Count; order++)
            {
                var transition = effectiveAnyTransitions[order];
                if (!TransitionMayBeReachable(controller, transition.conditions))
                    continue;
                if (transition == null || transition.destinationState == null ||
                    !stateSet.Contains(transition.destinationState) ||
                    transition.destinationStateMachine != null || transition.isExit ||
                    HasBlockingTransitionTiming(transition, report))
                    return false;
                var sourceConditions = transition.conditions ?? Array.Empty<AnimatorCondition>();
                var conditions = new List<AnimatorOptimizerCondition>();
                foreach (var sourceCondition in sourceConditions)
                {
                    if (!TryNormalizeCondition(controller, sourceCondition, options, report, out var condition))
                        return false;
                    conditions.Add(condition);
                }
                anyTransitions.Add(new NormalizedStateTransition
                {
                    Source = null,
                    Destination = transition.destinationState,
                    Conditions = conditions,
                    Order = order,
                    CanTransitionToSelf = transition.canTransitionToSelf,
                });
            }
            foreach (var state in states)
            {
                if (!ValidateState(controller, state, report, allowTransitions: true) ||
                    !IsTimeIndependentMotion(state.motion))
                    return false;

                var sourceTransitions = GetEffectiveTransitions(state.transitions);
                for (var order = 0; order < sourceTransitions.Count; order++)
                {
                    var transition = sourceTransitions[order];
                    if (!TransitionMayBeReachable(controller, transition.conditions))
                        continue;
                    if (transition == null || transition.destinationState == null ||
                        !stateSet.Contains(transition.destinationState) ||
                        transition.destinationStateMachine != null || transition.isExit ||
                        HasBlockingTransitionTiming(transition, report))
                        return false;

                    var sourceConditions = transition.conditions ?? Array.Empty<AnimatorCondition>();
                    var conditions = new List<AnimatorOptimizerCondition>();
                    foreach (var sourceCondition in sourceConditions)
                    {
                        if (!TryNormalizeCondition(controller, sourceCondition, options, report, out var condition))
                            return false;
                        conditions.Add(condition);
                    }
                    transitions.Add(new NormalizedStateTransition
                    {
                        Source = state,
                        Destination = transition.destinationState,
                        Conditions = conditions,
                        Order = order,
                    });
                }
            }

            if (transitions.Count == 0 && anyTransitions.Count == 0)
                return false;
            var allTransitions = anyTransitions.Concat(transitions).ToList();
            if (!TryBuildDecisionDimensionsFromConditions(
                allTransitions.SelectMany(transition => transition.Conditions).ToList(), options, report,
                out var dimensions, out var combinations))
                return false;

            if (combinations > 65536L / states.Count)
            {
                report.AddReason("无历史状态证明的状态数 × 输入组合数超过 65536，保留源图以避免分析阻塞。");
                return false;
            }
            var outputs = states.ToDictionary(state => state, ToMotionRef);
            var outputKeys = outputs.ToDictionary(pair => pair.Key, pair => BuildMotionKey(pair.Value));
            var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
            var indices = new int[dimensions.Count];
            for (var combination = 0; combination < (int)combinations; combination++)
            {
                var values = BuildDecisionDimensionValues(dimensions, indices);
                if (!TryResolveStateIndependentMotion(states, anyTransitions, transitions, values, outputs, outputKeys, out var motion))
                    return false;
                table[BuildDecisionDimensionKey(dimensions, indices)] = motion;
                IncrementDecisionDimensionIndices(dimensions, indices);
            }

            plan = BuildDecisionPlanFromTable(allTransitions, dimensions, table, controller);
            return plan != null;
        }

        private static bool TryBuildAapStateRegisterPlan(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> childStates,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (stateMachine == null || stateMachine.stateMachines != null && stateMachine.stateMachines.Length > 0 ||
                childStates.Count == 0 || childStates.Any(child => child.state == null))
                return false;

            // VRChat derives Gesture/FX playable masks from controller.layers[0].
            // Update has no mask, so moving a masked source into Output would
            // also change the controller's platform-level mask contract.
            if (report.Index == 0 && report.Mask != null)
            {
                report.AddReason("首层 Mask 可能兼作 VRChat Playable Mask；为避免 AAP 拆分改变遮罩作用范围，保留原层。");
                return false;
            }
            if (report.Index == 0 && report.BlendingMode == AnimatorLayerBlendingMode.Additive)
            {
                report.AddReason("首层采用 Additive；移到 AAP Output 次层后的叠加基准尚未证明一致，保留原层。");
                return false;
            }

            var states = childStates.Select(child => child.state).ToList();
            if (states.Distinct().Count() != states.Count || states.Count > 4096)
                return false;
            var stateIndices = states
                .Select((state, index) => new { state, index })
                .ToDictionary(item => item.state, item => item.index);
            var defaultStateIndex = states.IndexOf(stateMachine.defaultState);
            if (defaultStateIndex < 0)
                return false;

            foreach (var state in states)
            {
                if (!ValidateState(controller, state, report, allowTransitions: true) ||
                    !IsTimeIndependentMotion(state.motion))
                    return false;
            }

            var declarations = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            foreach (var parameter in controller.parameters)
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) || !declarations.TryAdd(parameter.name, parameter))
                {
                    report.AddReason("AAP Controller 参数声明为空或重名，无法建立唯一输入契约。");
                    return false;
                }

            List<AnimatorOptimizerFiniteGraph.Edge> ConvertEdges(IEnumerable<AnimatorTransitionBase> source, bool allowExit)
            {
                var result = new List<AnimatorOptimizerFiniteGraph.Edge>();
                foreach (var transition in source)
                {
                    // The shared contradiction proof validates all condition types
                    // before pruning, including conditions on other parameters.
                    if (!TransitionMayBeReachable(controller, transition.conditions))
                        continue;
                    if (transition.destinationStateMachine != null ||
                        transition.isExit && (!allowExit || transition.destinationState != null) ||
                        !transition.isExit && (transition.destinationState == null || !stateIndices.ContainsKey(transition.destinationState)))
                        return null;
                    if (transition is AnimatorStateTransition timed && HasBlockingTransitionTiming(timed, report))
                        return null;
                    result.Add(new AnimatorOptimizerFiniteGraph.Edge
                    {
                        Destination = transition.isExit ? -1 : stateIndices[transition.destinationState],
                        CanTransitionToSelf = transition is not AnimatorStateTransition edge || edge.canTransitionToSelf,
                        Conditions = transition.conditions ?? Array.Empty<AnimatorCondition>(),
                    });
                }
                return result;
            }
            var graph = new AnimatorOptimizerFiniteGraph
            {
                DefaultState = defaultStateIndex,
                Entries = ConvertEdges(GetEffectiveAnimatorTransitions(stateMachine.entryTransitions), false),
                AnyState = ConvertEdges(GetEffectiveTransitions(stateMachine.anyStateTransitions), false),
                States = states.Select(state => ConvertEdges(GetEffectiveTransitions(state.transitions), true)).ToList(),
            };
            if (graph.Entries == null || graph.AnyState == null || graph.States.Any(edges => edges == null))
            {
                report.AddReason("AAP 只接受本层 State 和根 Exit 路由；嵌套出口、时间过渡仍保留原状态机。");
                return false;
            }
            if (!AnimatorOptimizerAapProgram.TryCompile(graph, declarations, options, out var program, out var reason))
            {
                report.AddReason(reason);
                return false;
            }

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.AapStateRegister,
                StateRegisterProgram = program,
                StateRegisterStates = states,
                RequiredFloatParameters = new HashSet<string>(program.Axes.Select(axis => axis.Parameter), StringComparer.Ordinal),
            };
            foreach (var axis in program.AssumptionAxes)
            {
                if (axis.AssumesBinaryFloat)
                {
                    plan.AssumedBinaryParameters.Add(axis.Parameter);
                    plan.Assumptions.Add($"AAP Float '{axis.Parameter}' 按 0/1 开关处理；连续输入不在此约定内。");
                }
                if (axis.IsApproximate)
                    report.AddWarning($"AAP Float '{axis.Parameter}' 使用已允许的阈值邻域插值，不是精确阶跃。");
            }
            foreach (var assumption in plan.Assumptions) report.AddWarning("启发式：" + assumption);
            if (program.UsesRootExit)
                report.AddWarning("AAP 根 Exit 在一次逻辑更新中重新计算有序 Entry；不保留 Exit→Entry 的潜在额外帧延迟，仅属于实验性转换契约。");
            report.AddOptimization($"AAP 符号图：{states.Count} 个状态、{program.Axes.Count} 个有效输入、" +
                $"{program.DecisionNodeCount} 个共享条件节点、{program.ValueClipCount} 个赋值 Clip；不枚举全部输入组合。");
            return true;
        }

        private static Dictionary<string, float> BuildDecisionDimensionValues(
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            IReadOnlyList<int> indices)
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            for (var index = 0; index < dimensions.Count; index++)
                values[dimensions[index].Parameter] = dimensions[index].Values[indices[index]];
            return values;
        }

        private static int BuildDecisionDimensionKey(
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
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

        private static void IncrementDecisionDimensionIndices(
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            IList<int> indices)
        {
            for (var index = 0; index < indices.Count; index++)
            {
                if (++indices[index] < dimensions[index].Values.Count)
                    return;
                indices[index] = 0;
            }
        }

        private static bool TryBuildEffectEquivalentPlan(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            IReadOnlyList<ChildAnimatorState> childStates,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (stateMachine == null || childStates.Count == 0 ||
                stateMachine.stateMachines != null && stateMachine.stateMachines.Length > 0 ||
                childStates.Any(child => child.state == null))
                return false;

            var states = childStates.Select(child => child.state).ToList();
            var defaultState = stateMachine.defaultState ?? states[0];
            if (!states.Contains(defaultState))
                return false;
            if (!HasOnlyReadOnlyTransitionConditions(controller, stateMachine, states))
            {
                report.AddReason("Transition 可能消费 Trigger 或参数类型未知；输出相同也不能删除条件副作用。");
                return false;
            }

            var staticSignatures = new List<string>();
            foreach (var state in states)
            {
                if (!TryBuildStaticObservableMotionSignature(state.motion, out var staticSignature))
                    return false;
                staticSignatures.Add(staticSignature);
            }
            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (!ValidateEffectEquivalentStaticState(state, report))
                    return false;
                signatures.Add(staticSignatures[signatures.Count]);
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;

            // Entry/AnyState/State transitions may have timing and interruption details,
            // but they must remain inside this concrete State set. An Exit destination can
            // leave the current machine and therefore has no local observable output that
            // this proof can model.
            foreach (var transition in GetEffectiveAnimatorTransitions(stateMachine.entryTransitions))
            {
                if (transition == null || transition.destinationState == null ||
                    transition.destinationStateMachine != null ||
                    !states.Contains(transition.destinationState))
                    return false;
            }
            foreach (var transition in GetEffectiveTransitions(stateMachine.anyStateTransitions))
            {
                if (transition != null &&
                    (transition.destinationState == null || transition.isExit ||
                     transition.destinationStateMachine != null ||
                     !states.Contains(transition.destinationState)))
                    return false;
            }
            foreach (var state in states)
            {
                foreach (var transition in GetEffectiveAapTransitions(state.transitions))
                {
                    if (transition != null &&
                        (transition.destinationState == null || transition.isExit ||
                         transition.destinationStateMachine != null ||
                         !states.Contains(transition.destinationState)))
                        return false;
                }
            }

            plan = CreateConstantPlan(defaultState);
            return true;
        }

        private static bool HasOnlyReadOnlyTransitionConditions(AnimatorController controller,
            AnimatorStateMachine machine, IEnumerable<AnimatorState> states)
        {
            var declarations = controller.parameters;
            var edges = GetEffectiveAnimatorTransitions(machine.entryTransitions).Cast<AnimatorTransitionBase>()
                .Concat(GetEffectiveTransitions(machine.anyStateTransitions))
                .Concat(states.SelectMany(state => GetEffectiveTransitions(state.transitions)))
                .Concat(machine.stateMachines.Where(child => child.stateMachine != null).SelectMany(child =>
                    GetEffectiveAnimatorTransitions(machine.GetStateMachineTransitions(child.stateMachine))));
            return edges.Where(edge => TransitionMayBeReachable(controller, edge.conditions)).All(edge =>
                AnimatorOptimizerSemanticUtility.AreReadOnlyConditions(edge.conditions, name =>
                {
                    var matches = declarations.Where(parameter => parameter != null && parameter.name == name).Take(2).ToList();
                    if (matches.Count > 1) return null;
                    return matches.Count == 1 ? matches[0].type : TryGetVrcGlobalParameterType(name, out var type)
                        ? type : (AnimatorControllerParameterType?)null;
                }));
        }

        internal static bool TryBuildStaticObservableMotionSignature(
            Motion motion,
            out string signature)
        {
            signature = null;
            if (motion == null)
            {
                signature = "static-empty";
                return true;
            }

            while (motion is BlendTree singletonTree &&
                   AnimatorOptimizerMotionUtility.TryGetSafeSingletonChild(singletonTree, out var singleton, allowStaticSettings: true))
                motion = singleton.motion;

            if (!(motion is AnimationClip clip) || !AnimatorOptimizerMotionUtility.IsStaticClip(clip))
                return false;

            var floatParts = new List<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                // Animator curves are controller-local logic. Even a constant AAP
                // value must not be folded by this output-only equivalence proof.
                if (binding.type == typeof(Animator))
                    return false;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;
                floatParts.Add("f:" + AnimatorOptimizerSynchronizedGraph.BindingKey(binding) + ":" +
                    curve.keys[0].value.ToString("R", CultureInfo.InvariantCulture));
            }

            var objectParts = new List<string>();
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (curve == null || curve.Length == 0)
                    continue;
                if (curve.Any(key => key.value != curve[0].value))
                    return false;
                objectParts.Add("o:" + AnimatorOptimizerSynchronizedGraph.BindingKey(binding) + ":" +
                    (curve[0].value == null ? "0" : curve[0].value.GetInstanceID().ToString(CultureInfo.InvariantCulture)));
            }

            floatParts.Sort(StringComparer.Ordinal);
            objectParts.Sort(StringComparer.Ordinal);
            signature = "static:" + string.Concat(floatParts.Concat(objectParts).Select(AnimatorOptimizerSynchronizedGraph.Atom));
            return true;
        }

        private static bool ValidateEffectEquivalentStaticState(
            AnimatorState state,
            AnimatorOptimizerLayerReport report)
        {
            if (state == null)
            {
                report.AddReason("State 引用为空。");
                return false;
            }
            if ((state.behaviours ?? Array.Empty<StateMachineBehaviour>()).Length > 0)
                report.AddReason("State 包含 StateMachineBehaviour，不能证明空 Motion 没有回调副作用。");
            ValidateWriteDefaults(state, report);
            ValidatePlaybackSettings(state, report);
            if (ToMotionRef(state).Mirror)
                report.AddReason($"State '{state.name}' 的 Mirror 影响姿态，不能仅按 Clip 曲线证明输出等价。");
            if (!TryBuildStaticObservableMotionSignature(state.motion, out _))
                report.AddReason("State Motion 不是可证明为静态且可观测等价的 Clip。");
            return report.Reasons.Count == 0;
        }

        private static List<AnimatorStateTransition> GetEffectiveAapTransitions(
            IEnumerable<AnimatorStateTransition> transitions)
        {
            return GetEffectiveTransitions(transitions);
        }

        private static bool TryResolveStateIndependentMotion(
            IReadOnlyList<AnimatorState> states,
            IReadOnlyList<NormalizedStateTransition> anyTransitions,
            IReadOnlyList<NormalizedStateTransition> transitions,
            IReadOnlyDictionary<string, float> values,
            IReadOnlyDictionary<AnimatorState, AnimatorOptimizerMotionRef> outputs,
            IReadOnlyDictionary<AnimatorState, string> outputKeys,
            out AnimatorOptimizerMotionRef motion)
        {
            motion = null;
            string selectedKey = null;
            foreach (var state in states)
            {
                // Any State transitions win over the source state's own transitions; an Any
                // State transition back to the current state is skipped unless it may self-transition.
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
                if (motion == null)
                {
                    motion = output;
                    selectedKey = key;
                }
                else if (!string.Equals(selectedKey, key, StringComparison.Ordinal))
                    return false;
            }
            return motion != null;
        }

        private static bool TryResolveNestedStaticDefault(
            AnimatorStateMachine stateMachine,
            out AnimatorState state)
        {
            state = null;
            if (stateMachine == null ||
                (stateMachine.behaviours != null && stateMachine.behaviours.Length > 0) ||
                GetEffectiveAnimatorTransitions(stateMachine.entryTransitions).Count > 0 ||
                GetEffectiveTransitions(stateMachine.anyStateTransitions).Count > 0)
                return false;

            if (stateMachine.defaultState != null)
            {
                if ((stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                    .Any(child => child.stateMachine != null &&
                        HasEffectiveTransitionToStateMachine(stateMachine, child.stateMachine)))
                    return false;
                state = stateMachine.defaultState;
                return GetEffectiveTransitions(state.transitions).Count == 0;
            }

            var childMachines = stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>();
            if ((stateMachine.states ?? Array.Empty<ChildAnimatorState>()).Length != 0 || childMachines.Length != 1)
                return false;
            if (GetEffectiveAnimatorTransitions(
                    stateMachine.GetStateMachineTransitions(childMachines[0].stateMachine)).Count != 0)
                return false;
            return TryResolveNestedStaticDefault(childMachines[0].stateMachine, out state);
        }

        private static bool HasEffectiveTransitionToStateMachine(
            AnimatorStateMachine stateMachine,
            AnimatorStateMachine target)
        {
            if (stateMachine == null || target == null)
                return false;
            if (GetEffectiveAnimatorTransitions(stateMachine.entryTransitions)
                .Any(transition => transition.destinationStateMachine == target) ||
                GetEffectiveTransitions(stateMachine.anyStateTransitions)
                .Any(transition => transition.destinationStateMachine == target) ||
                (stateMachine.states ?? Array.Empty<ChildAnimatorState>())
                .Where(child => child.state != null)
                .SelectMany(child => GetEffectiveTransitions(child.state.transitions))
                .Any(transition => transition.destinationStateMachine == target) ||
                GetEffectiveAnimatorTransitions(stateMachine.GetStateMachineTransitions(target))
                .Count > 0)
                return true;
            return false;
        }

        private static bool TransitionGroupsCanOverlap(
            AnimatorStateTransition left,
            AnimatorStateTransition right,
            AnimatorController controller)
        {
            var leftConditions = left?.conditions ?? Array.Empty<AnimatorCondition>();
            var rightConditions = right?.conditions ?? Array.Empty<AnimatorCondition>();
            var parameterNames = leftConditions.Concat(rightConditions)
                .Select(condition => condition.parameter)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (parameterNames.Count == 0)
                return true;

            var parameters = parameterNames
                .Select(name => controller.parameters.FirstOrDefault(parameter => parameter.name == name))
                .ToList();
            if (parameters.Any(parameter => parameter == null))
                return true;

            if (parameters.All(parameter => parameter.type == AnimatorControllerParameterType.Bool))
            {
                foreach (var name in parameterNames)
                {
                    var conditions = leftConditions.Concat(rightConditions)
                        .Where(condition => condition.parameter == name).ToArray();
                    if (conditions.Any(condition => condition.mode != AnimatorConditionMode.If &&
                        condition.mode != AnimatorConditionMode.IfNot && condition.mode != AnimatorConditionMode.Equals &&
                        condition.mode != AnimatorConditionMode.NotEqual))
                        return true;
                    var values = new Dictionary<string, float>(StringComparer.Ordinal) { [name] = 0f };
                    if (ConditionsMatch(conditions, values))
                        continue;
                    values[name] = 1f;
                    if (!ConditionsMatch(conditions, values))
                        return false;
                }
                return true;
            }

            if (parameterNames.Count != 1)
                return true;

            var parameter = parameters[0];
            if (parameter.type != AnimatorControllerParameterType.Int &&
                parameter.type != AnimatorControllerParameterType.Float)
                return true;

            if (parameter.type == AnimatorControllerParameterType.Float)
                return NumericConditionGroupsCanOverlap(leftConditions, rightConditions);

            var thresholds = leftConditions.Concat(rightConditions).Select(condition => condition.threshold).ToList();
            var valuesToTry = new HashSet<float>();
            var epsilon = 0.001f;
            foreach (var threshold in thresholds)
            {
                if (parameter.type == AnimatorControllerParameterType.Int)
                {
                    var value = Mathf.RoundToInt(threshold);
                    valuesToTry.Add(value - 1);
                    valuesToTry.Add(value);
                    valuesToTry.Add(value + 1);
                }
                else
                {
                    valuesToTry.Add(threshold - epsilon);
                    valuesToTry.Add(threshold);
                    valuesToTry.Add(threshold + epsilon);
                }
            }

            foreach (var value in valuesToTry)
            {
                var values = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [parameterNames[0]] = value,
                };
                if (ConditionsMatch(leftConditions, values) && ConditionsMatch(rightConditions, values))
                    return true;
            }
            return false;
        }

        private static bool NumericConditionGroupsCanOverlap(
            IEnumerable<AnimatorCondition> left,
            IEnumerable<AnimatorCondition> right)
        {
            var conditions = (left ?? Enumerable.Empty<AnimatorCondition>())
                .Concat(right ?? Enumerable.Empty<AnimatorCondition>())
                .ToList();
            var excluded = new HashSet<float>();
            var hasEquality = false;
            var equality = 0f;
            var hasLower = false;
            var lower = 0f;
            var lowerInclusive = true;
            var hasUpper = false;
            var upper = 0f;
            var upperInclusive = true;

            foreach (var condition in conditions)
            {
                switch (condition.mode)
                {
                    case AnimatorConditionMode.Equals:
                        if (hasEquality && equality != condition.threshold)
                            return false;
                        hasEquality = true;
                        equality = condition.threshold;
                        break;
                    case AnimatorConditionMode.NotEqual:
                        excluded.Add(condition.threshold);
                        break;
                    case AnimatorConditionMode.Greater:
                        if (!hasLower || condition.threshold > lower)
                        {
                            hasLower = true;
                            lower = condition.threshold;
                            lowerInclusive = false;
                        }
                        else if (condition.threshold == lower)
                        {
                            lowerInclusive = false;
                        }
                        break;
                    case AnimatorConditionMode.Less:
                        if (!hasUpper || condition.threshold < upper)
                        {
                            hasUpper = true;
                            upper = condition.threshold;
                            upperInclusive = false;
                        }
                        else if (condition.threshold == upper)
                        {
                            upperInclusive = false;
                        }
                        break;
                    default:
                        // The normalizer rejects these for Float, but an unknown
                        // condition must never be treated as proven disjoint.
                        return true;
                }
            }

            if (hasEquality)
            {
                if (hasLower && (equality < lower || equality == lower && !lowerInclusive) ||
                    hasUpper && (equality > upper || equality == upper && !upperInclusive) ||
                    excluded.Contains(equality))
                    return false;
                return true;
            }

            if (hasLower && hasUpper)
            {
                if (lower < upper)
                    return true;
                if (lower > upper || !lowerInclusive || !upperInclusive)
                    return false;
                return !excluded.Contains(lower);
            }

            // A half-infinite interval cannot be removed by a finite set of
            // NotEqual constraints, so it always has a witness.
            return true;
        }

        private static bool ConditionsMatch(
            IEnumerable<AnimatorCondition> conditions,
            IReadOnlyDictionary<string, float> values)
        {
            foreach (var condition in conditions)
            {
                if (!values.TryGetValue(condition.parameter, out var value))
                    return false;
                var matches = condition.mode switch
                {
                    AnimatorConditionMode.If => value >= 0.5f,
                    AnimatorConditionMode.IfNot => value < 0.5f,
                    AnimatorConditionMode.Equals => value == condition.threshold,
                    AnimatorConditionMode.NotEqual => value != condition.threshold,
                    AnimatorConditionMode.Greater => value > condition.threshold,
                    AnimatorConditionMode.Less => value < condition.threshold,
                    _ => false,
                };
                if (!matches)
                    return false;
            }
            return true;
        }

        private static bool AreEquivalentTransition(
            AnimatorStateTransition left,
            AnimatorStateTransition right,
            bool ignoreTiming)
        {
            if (left == null || right == null)
                return left == right;
            var sameTiming = ignoreTiming ||
                             left.hasExitTime == right.hasExitTime &&
                             (!left.hasExitTime || left.exitTime == right.exitTime) &&
                             left.duration == right.duration && left.offset == right.offset &&
                             left.hasFixedDuration == right.hasFixedDuration;
            return left.destinationState == right.destinationState &&
                   left.destinationStateMachine == right.destinationStateMachine &&
                   left.isExit == right.isExit &&
                   left.canTransitionToSelf == right.canTransitionToSelf &&
                   left.interruptionSource == right.interruptionSource &&
                   left.orderedInterruption == right.orderedInterruption &&
                   sameTiming &&
                   left.mute == right.mute &&
                   left.solo == right.solo &&
                   AreEquivalentConditions(left.conditions, right.conditions);
        }

        private static bool AreEquivalentConditions(
            IReadOnlyList<AnimatorCondition> left,
            IReadOnlyList<AnimatorCondition> right)
        {
            if (left == null || right == null)
                return (left == null || left.Count == 0) && (right == null || right.Count == 0);
            if (left.Count != right.Count)
                return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].parameter, right[index].parameter, StringComparison.Ordinal) ||
                    left[index].mode != right[index].mode ||
                    left[index].threshold != right[index].threshold)
                    return false;
            }
            return true;
        }

        private static bool TryBuildBranches(
            AnimatorController controller,
            IReadOnlyList<ChildAnimatorState> states,
            IReadOnlyList<AnimatorStateTransition> transitions,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out List<AnimatorOptimizerBranch> branches)
        {
            branches = new List<AnimatorOptimizerBranch>();
            var stateSet = new HashSet<AnimatorState>(states.Select(child => child.state));
            for (var order = 0; order < transitions.Count; order++)
            {
                var transition = transitions[order];
                if (transition != null && !TransitionMayBeReachable(controller, transition.conditions))
                    continue;
                if (transition == null || transition.destinationState == null ||
                    !stateSet.Contains(transition.destinationState) || transition.destinationStateMachine != null ||
                    transition.isExit)
                {
                    report.AddReason("Transition 必须指向本层的另一个 State，不能指向 Exit/StateMachine。");
                    return false;
                }

                if (transition.mute)
                {
                    report.AddReason("Transition 已 Mute，不应参与分支提取。");
                    return false;
                }
                if (HasBlockingTransitionTiming(transition, report))
                {
                    report.AddReason("Transition 含有 Exit Time、Duration 或 Offset，存在时间语义；可开启“忽略静态 Motion 的过渡时间”后再试。");
                    return false;
                }

                var conditions = transition.conditions ?? Array.Empty<AnimatorCondition>();
                if (conditions.Length == 0)
                {
                    report.AddReason("无条件 Transition 会依赖当前 State 的历史，不能直接作为 BlendTree 分支。");
                    return false;
                }

                var normalizedConditions = new List<AnimatorOptimizerCondition>();
                foreach (var condition in conditions)
                {
                    if (!TryNormalizeCondition(controller, condition, options, report, out var normalized))
                        return false;
                    normalizedConditions.Add(normalized);
                }

                branches.Add(new AnimatorOptimizerBranch
                {
                    Motion = ToMotionRef(transition.destinationState),
                    Conditions = normalizedConditions,
                    Order = order,
                });
            }

            foreach (var child in states)
            {
                if (!ValidateState(controller, child.state, report, allowTransitions: true))
                    return false;
                if (!IsTimeIndependentMotion(child.state.motion))
                {
                    report.AddReason("参数 Transition 会改变当前 State；源 Motion 含时间变化，BlendTree 切换无法保留 State 进入时的时间重置。");
                    return false;
                }
            }

            return true;
        }

        private static bool IsTimeIndependentMotion(Motion motion)
        {
            return AnimatorOptimizerMotionUtility.IsTimeIndependent(motion);
        }

        private static bool TryNormalizeCondition(
            AnimatorController controller,
            AnimatorCondition source,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerCondition normalized)
        {
            normalized = null;
            var parameter = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .FirstOrDefault(candidate => string.Equals(candidate.name, source.parameter, StringComparison.Ordinal));
            if (parameter == null)
            {
                if (!TryGetVrcGlobalParameterType(source.parameter, out var globalType))
                {
                    report.AddReason("Transition 引用了不存在的 Animator 参数: " + source.parameter);
                    return false;
                }
                return TryNormalizeConditionForType(source, globalType, options, report, out normalized);
            }

            if (!IsUsableConditionThreshold(source, parameter.type) ||
                parameter.type == AnimatorControllerParameterType.Float && !IsFinite(parameter.defaultFloat))
            {
                report.AddReason("条件阈值/Float 默认值非有限数，或超过 Float 可精确表示的整数邻域: " + source.parameter);
                return false;
            }
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                    source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual)
                {
                    report.AddReason("Bool 条件只支持 If、IfNot、Equals、NotEqual。");
                    return false;
                }
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    source.threshold != 0f && source.threshold != 1f)
                {
                    report.AddReason("Bool Equals/NotEqual 阈值必须为 0 或 1。");
                    return false;
                }
                if (!options.UpgradeBoolParametersToFloat)
                {
                    report.AddReason($"条件使用 Bool 参数 '{source.parameter}'；需要勾选 Bool → Float 才能作为 BlendTree 参数。");
                    report.AddSuggestion("在“参数与合并（高级）”启用 Bool → Float；参数写入者与 Behaviour 引用仍会独立检查。");
                    return false;
                }

                normalized = new AnimatorOptimizerCondition
                {
                    Parameter = source.parameter,
                    ParameterType = parameter.type,
                    Mode = source.mode,
                    Threshold = source.threshold,
                };
                return true;
            }

            if (parameter.type == AnimatorControllerParameterType.Int)
            {
                if (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                    source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less)
                {
                    report.AddReason("Int 条件只支持 Equals、NotEqual、Greater、Less。");
                    return false;
                }
                if (source.threshold != Mathf.Round(source.threshold))
                {
                    report.AddReason("Int 条件阈值必须是整数。");
                    return false;
                }
                if (!options.UpgradeIntParametersToFloat)
                {
                    report.AddReason($"条件使用 Int 参数 '{source.parameter}'；需要勾选 Int → Float 才能作为 BlendTree 参数。");
                    report.AddSuggestion("在“参数与合并（高级）”启用 Int → Float；保留整数输入约定，避免连续值插值改变选择结果。");
                    return false;
                }

                normalized = new AnimatorOptimizerCondition
                {
                    Parameter = source.parameter,
                    ParameterType = parameter.type,
                    Mode = source.mode,
                    Threshold = Mathf.Round(source.threshold),
                };
                return true;
            }

            if (parameter.type != AnimatorControllerParameterType.Float)
            {
                report.AddReason("只支持 Bool、Int、Float Animator 参数条件。");
                return false;
            }
            if (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less)
            {
                report.AddReason("Float 条件只支持 Equals、NotEqual、Greater、Less。");
                return false;
            }
            if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(source.threshold,
                options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, new HashSet<float>(), out var approximate))
            {
                report.AddReason($"Float 参数 '{source.parameter}' 的阈值位于零、非正规数或溢出边缘，不能保证跨平台精确阶跃。");
                return false;
            }
            if (approximate)
                report.AddWarning($"Float 参数 '{source.parameter}' 使用显式允许的阈值邻域近似。");
            else
                report.AddOptimization($"Float 参数 '{source.parameter}' 使用相邻可表示值构造精确阶跃（有限 Float 输入）；等于阈值的情况单独证明，不假定 0/1 值域。");

            normalized = new AnimatorOptimizerCondition
            {
                Parameter = source.parameter,
                ParameterType = parameter.type,
                Mode = source.mode,
                Threshold = source.threshold,
            };
            return true;
        }

        internal static bool IsUsableConditionThreshold(AnimatorCondition condition, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrWhiteSpace(condition.parameter))
                return false;
            if (type == AnimatorControllerParameterType.Bool)
                return true;
            return !float.IsNaN(condition.threshold) && !float.IsInfinity(condition.threshold) &&
                (type != AnimatorControllerParameterType.Int || Math.Abs((double)condition.threshold) <= 16777214d);
        }

        private static bool TryBuildBooleanTruthTable(
            IReadOnlyList<AnimatorOptimizerBranch> branches,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (branches.Any(branch => branch.Conditions.Any(condition => !condition.IsBoolean)))
                return false;

            var parameters = branches
                .SelectMany(branch => branch.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (parameters.Count == 0)
                return false;
            if (parameters.Count > 30)
            {
                report.AddReason("Bool 条件参数超过 30 个，已在位移计算前安全拒绝，避免真值表整数溢出。");
                return false;
            }

            var entryCount = 1L << parameters.Count;
            if (entryCount > Math.Max(2, options.MaxBooleanTruthTableEntries))
            {
                report.AddReason($"Bool 条件真值表需要 {entryCount} 个组合，超过上限 {options.MaxBooleanTruthTableEntries}。");
                return false;
            }

            var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
            for (var mask = 0; mask < entryCount; mask++)
            {
                AnimatorOptimizerBranch matched = null;
                foreach (var branch in branches.OrderBy(branch => branch.Order))
                {
                    if (branch.Conditions.All(condition =>
                    {
                        var value = (mask & (1 << parameters.IndexOf(condition.Parameter))) != 0 ? 1f : 0f;
                        return condition.Matches(value);
                    }))
                    {
                        matched = branch;
                        break;
                    }
                }

                if (matched == null)
                {
                    report.AddReason("Bool Transition 条件没有覆盖所有参数组合；原状态机在未命中时会保留历史状态，不能无损转换。");
                    return false;
                }
                table[mask] = matched.Motion.Clone();
            }

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.BooleanTruthTable,
                Branches = branches.ToList(),
                BooleanParameters = parameters,
                TruthTable = table,
                RequiredFloatParameters = new HashSet<string>(parameters, StringComparer.Ordinal),
                StructuralKey = "bool:" + string.Join(",", parameters) + ":" +
                                string.Join("|", table.OrderBy(pair => pair.Key).Select(pair => pair.Value.Motion == null ? "null" : pair.Value.Motion.GetInstanceID().ToString())),
            };
            return true;
        }

        private static bool TryBuildNumericSelector(
            IReadOnlyList<AnimatorOptimizerBranch> branches,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (branches.Any(branch => branch.Conditions.Count != 1))
                return false;

            var parameterNames = branches
                .Select(branch => branch.Conditions[0].Parameter)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (parameterNames.Count != 1)
                return false;

            var parameter = branches[0].Conditions[0];
            if (parameter.IsBoolean)
                return false;
            if (branches.Any(branch => branch.Conditions[0].ParameterType != parameter.ParameterType))
                return false;

            var samples = BuildSelectorSamples(branches, parameter, options, report, out var approximate);
            if (samples == null || samples.Count == 0)
                return false;

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.NumericSelector,
                Branches = branches.ToList(),
                SelectorParameter = parameter.Parameter,
                SelectorParameterType = parameter.ParameterType,
                SelectorSamples = samples,
                SelectorIsApproximate = approximate,
                RequiredFloatParameters = new HashSet<string>(new[] { parameter.Parameter }, StringComparer.Ordinal),
                StructuralKey = "numeric:" + parameter.Parameter + ":" +
                                string.Join("|", samples.Select(sample => sample.Value.ToString("R") + ":" +
                                    (sample.Motion.Motion == null ? "null" : sample.Motion.Motion.GetInstanceID().ToString()))),
            };
            return true;
        }

        private static List<AnimatorOptimizerSelectorSample> BuildSelectorSamples(
            IReadOnlyList<AnimatorOptimizerBranch> branches,
            AnimatorOptimizerCondition conditionPrototype,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out bool approximate)
        {
            approximate = false;
            var values = new SortedSet<float>();
            var thresholds = branches.Select(branch => branch.Conditions[0].Threshold).ToList();
            if (conditionPrototype.IsInteger)
            {
                foreach (var threshold in thresholds)
                {
                    var value = Mathf.RoundToInt(threshold);
                    values.Add(value - 1);
                    values.Add(value);
                    values.Add(value + 1);
                }
            }
            else
            {
                foreach (var threshold in thresholds)
                {
                    if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(threshold,
                        options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var isApproximate))
                        return null;
                    approximate |= isApproximate;
                }
            }

            if (values.Count > Math.Max(3, options.MaxNumericSelectorSamples))
            {
                report.AddReason($"数值条件需要 {values.Count} 个 BlendTree 采样点，超过上限 {options.MaxNumericSelectorSamples}。");
                return null;
            }

            var samples = new List<AnimatorOptimizerSelectorSample>();
            foreach (var value in values)
            {
                var matched = branches
                    .OrderBy(branch => branch.Order)
                    .FirstOrDefault(branch => branch.Conditions[0].Matches(value));
                if (matched == null)
                {
                    if (conditionPrototype.IsFloat && options.AllowFloatConditionApproximation)
                    {
                        approximate = true;
                        matched = branches
                            .OrderBy(branch => Mathf.Abs(branch.Conditions[0].Threshold - value))
                            .ThenBy(branch => branch.Order)
                            .FirstOrDefault();
                        report.AddWarning("Float 条件在某些阈值点没有 Transition 命中；近似模式已选择最近分支，运行结果可能与原状态机在边界帧不同。");
                    }
                }
                if (matched == null)
                {
                    report.AddReason("数值 Transition 条件没有覆盖完整取值区间；原状态机未命中时依赖历史状态，不能无损转换。");
                    return null;
                }
                samples.Add(new AnimatorOptimizerSelectorSample
                {
                    Value = value,
                    Motion = matched.Motion.Clone(),
                });
            }

            return samples;
        }

        private static bool ValidateState(
            AnimatorController controller,
            AnimatorState state,
            AnimatorOptimizerLayerReport report,
            bool allowTransitions = false)
        {
            if (state == null)
            {
                report.AddReason("State 引用为空。");
                return false;
            }
            if (state.motion != null && ContainsPotentialProxyMotion(state.motion, new HashSet<int>()))
                report.AddReason("State Motion 包含 VRChat/VRCFury proxy Clip，必须保留平台特殊 Motion。");
            if (!allowTransitions && GetEffectiveTransitions(state.transitions)
                .Any(transition => TransitionMayBeReachable(controller, transition.conditions)))
                report.AddReason("State 包含 Transition，不能按 Constant Motion 处理。");
            if (state.behaviours != null && state.behaviours.Length > 0)
            {
                report.BindingsAreComplete = false;
                report.AddReason("State 包含 StateMachineBehaviour，BlendTree 不会执行这些回调。");
            }
            ValidateWriteDefaults(state, report);
            ValidatePlaybackSettings(state, report);
            if (state.motion != null && state.motion is not AnimationClip && state.motion is not BlendTree)
                report.AddReason("State Motion 不是 AnimationClip 或 BlendTree。");
            return report.Reasons.Count == 0;
        }

        // Coverage must hold on every selectable branch, not merely their union.
        private static bool ValidateWriteDefaults(
            AnimatorState state,
            AnimatorOptimizerLayerReport report)
        {
            if (state == null || state.writeDefaultValues)
                return true;

            report.WriteDefaultsOffStates.Add(state.GetInstanceID());
            var policy = report.Options?.WriteDefaultsPolicy ??
                         AnimatorOptimizerComponent.WriteDefaultsPolicy.AllowCoveredStates;
            var stateName = string.IsNullOrEmpty(state.name) ? "<unnamed>" : state.name;
            if (policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict)
            {
                report.AddReason($"State '{stateName}' 的 Write Defaults 为 Off；当前策略为 Strict，只接受 WD On 的源 State。");
                return false;
            }

            if (report.LayerMotionBindings == null || !report.LayerMotionBindingsAreComplete)
            {
                report.AddReason($"State '{stateName}' 的 Write Defaults 为 Off，且 Controller 绑定无法完整分析。");
                return false;
            }

            var stateBindings = new HashSet<AnimatorOptimizerBindingKey>();
            if (!TryCollectGuaranteedBindings(state.motion, stateBindings))
            {
                report.AddReason($"State '{stateName}' 的 Write Defaults 为 Off，且其 Motion 绑定无法完整分析。");
                return false;
            }

            var missing = report.LayerMotionBindings.Where(binding => !stateBindings.Contains(binding)).ToList();
            if (missing.Count == 0)
            {
                report.AddOptimization(report.LayerMotionBindings.Count == 0
                    ? $"State '{stateName}' 为 WD Off，但 Controller 没有动画绑定。"
                    : $"State '{stateName}' 为 WD Off，所有分支均覆盖 Controller 的 {report.LayerMotionBindings.Count} 个绑定，已自动放行。");
                return true;
            }

            var example = missing.Count > 3
                ? string.Join(", ", missing.Take(3)) + " …"
                : string.Join(", ", missing);
            if (policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn)
            {
                report.AddWarning(
                    $"State '{stateName}' 已按 WD On 处理；{missing.Count} 个绑定未被保证覆盖（{example}），可能改变默认值回写与属性保持效果。");
                return true;
            }

            report.AddReason(
                $"State '{stateName}' 为 WD Off，未保证覆盖 Controller 全部绑定（缺 {missing.Count} 个: {example}）；" +
                "若不依赖属性保持，可显式选择“视为 WD On”（可能改变效果）。");
            report.AddSuggestion("WD Off：优先补齐各分支应写入的属性；仅在接受默认值回写变化时选择“视为 WD On”。");
            return false;
        }

        private static void ValidatePlaybackSettings(AnimatorState state, AnimatorOptimizerLayerReport report)
        {
            if (state.iKOnFeet)
            {
                report.AddReason($"State '{state.name}' 开启 IK On Feet；无法证明求解器对继承姿态没有影响。");
                report.AddSuggestion("IK On Feet / IK Pass 仍保留安全检查；确认不需要 IK 后在源状态或源层关闭，不能仅根据 Clip 是静态就忽略。");
            }
            if (!IsFinite(state.speed) || !IsFinite(state.cycleOffset))
                report.AddReason($"State '{state.name}' 的 Speed / Cycle Offset 不是有限数，请修正源数据。");

            var timing = new List<string>();
            if (state.speedParameterActive) timing.Add($"Speed 参数 '{state.speedParameter}'");
            if (state.cycleOffsetParameterActive) timing.Add($"Cycle Offset 参数 '{state.cycleOffsetParameter}'");
            if (state.timeParameterActive) timing.Add($"Time 参数 '{state.timeParameter}'");
            var timeIndependent = AnimatorOptimizerMotionUtility.IsTimeIndependent(state.motion);
            if (timeIndependent)
            {
                if (state.speed != 1f) timing.Add("Speed 数值");
                if (state.cycleOffset != 0f) timing.Add("Cycle Offset 数值");
                if (timing.Count > 0)
                    report.AddOptimization($"State '{state.name}'：{string.Join("、", timing)} 不影响其时间无关 Motion，生成时统一为 Speed=1 / Offset=0；Transition 时序仍单独检查。");
            }
            else if (timing.Count > 0)
            {
                report.AddReason($"State '{state.name}' 使用 {string.Join("、", timing)}；Motion 存在时间变化、事件、根运动或特殊动画语义，不能移除这些参数。");
                report.AddSuggestion("播放参数：静态普通 Motion 会自动放行。动态动画若是误勾选，请在源 State 关闭对应参数；参数默认值不代表运行时常量。");
            }
            if (state.mirror || state.mirrorParameterActive)
            {
                if (AnimatorOptimizerMotionUtility.IsPropertyOnly(state.motion))
                    report.AddOptimization($"State '{state.name}'：Motion 仅写普通非骨骼属性，Mirror / Mirror 参数不参与这些属性的求值，已规范化。");
                else if (state.mirrorParameterActive)
                    report.AddReason($"State '{state.name}' 的 Mirror 参数可能改变姿态，无法降级为固定 ChildMotion Mirror。");
            }
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool IsFloatNeighborhoodRepresentable(float threshold, float epsilon)
        {
            var lower = threshold - epsilon;
            var upper = threshold + epsilon;
            return IsFinite(lower) && IsFinite(upper) && lower < threshold && upper > threshold;
        }

        internal static bool TryCollectGuaranteedBindings(Motion motion, HashSet<AnimatorOptimizerBindingKey> bindings)
        {
            if (motion == null)
                return true;
            if (motion is AnimationClip)
                return TryCollectMotionBindingKeys(motion, bindings, new HashSet<int>());
            if (!(motion is BlendTree tree))
                return false;
            if (tree.blendType != BlendTreeType.Simple1D)
                return true;
            HashSet<AnimatorOptimizerBindingKey> common = null;
            foreach (var child in tree.children)
            {
                var childBindings = new HashSet<AnimatorOptimizerBindingKey>();
                if (!TryCollectGuaranteedBindings(child.motion, childBindings))
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

        // Callers still require time-independent motions when opting into instant transitions.
        private static bool HasBlockingTransitionTiming(
            AnimatorStateTransition transition,
            AnimatorOptimizerLayerReport report)
        {
            if (transition == null)
                return false;
            if (!IsFinite(transition.duration) || !IsFinite(transition.offset) ||
                transition.hasExitTime && !IsFinite(transition.exitTime))
            {
                report?.AddReason("Transition 含非有限 Duration / Offset / Exit Time，请修正源数据。");
                return true;
            }
            var irrelevantOffset = transition.offset != 0f && report != null && !report.HasExitTime &&
                transition.destinationState != null &&
                AnimatorOptimizerMotionUtility.IsTimeIndependent(transition.destinationState.motion);
            if (irrelevantOffset)
                report.AddOptimization($"指向 '{transition.destinationState.name}' 的 Transition Offset 不影响静态输出，且本层无有效 Exit Time 依赖，已忽略偏移。");
            var timed = transition.hasExitTime ||
                        transition.duration != 0f ||
                        transition.offset != 0f && !irrelevantOffset;
            if (!timed)
                return false;
            if (report?.Options == null || !report.Options.IgnoreTransitionTimingForStaticMotions)
            {
                report?.AddReason("Transition 的 Exit Time / Duration 或有时间依赖的 Offset 无法由瞬时 BlendTree 切换保留。");
                report?.AddSuggestion("过渡时间：若接受静态状态间取消等待和淡入淡出，可启用“忽略静态过渡时间”；真正动态 Motion 不会因此放行。");
                return true;
            }
            report.RelaxedTransitionCount++;
            report.AddWarning("已忽略 Transition 的 Exit Time / Duration / Offset；静态 Motion 之间的切换会变为瞬时，不再有过渡淡入淡出。");
            return false;
        }

        private static bool HasEffectiveExitTime(AnimatorStateMachine machine, AnimatorController controller)
        {
            if (machine == null)
                return false;
            bool IsTimed(AnimatorStateTransition transition) => transition.hasExitTime &&
                TransitionMayBeReachable(controller, transition.conditions);
            return GetEffectiveTransitions(machine.anyStateTransitions).Any(IsTimed) ||
                machine.states.Any(child => child.state != null &&
                    GetEffectiveTransitions(child.state.transitions).Any(IsTimed)) ||
                machine.stateMachines.Any(child => HasEffectiveExitTime(child.stateMachine, controller));
        }

        private static bool TryCollectStateMachineMotionBindings(
            AnimatorStateMachine stateMachine,
            ISet<AnimatorOptimizerBindingKey> bindings,
            ISet<int> visitedMachines)
        {
            if (stateMachine == null || !visitedMachines.Add(stateMachine.GetInstanceID()))
                return true;
            var complete = true;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                complete &= TryCollectMotionBindingKeys(child.state.motion, bindings, new HashSet<int>());
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                complete &= TryCollectStateMachineMotionBindings(child.stateMachine, bindings, visitedMachines);
            return complete;
        }

        internal static bool TryCollectMotionBindingKeys(
            Motion motion,
            ISet<AnimatorOptimizerBindingKey> bindings,
            ISet<int> visited)
        {
            if (motion == null)
                return true;
            if (!visited.Add(motion.GetInstanceID()))
                return true;
            if (motion is AnimationClip clip)
            {
                if (!AnimatorOptimizerSemanticUtility.HasInspectableBindings(clip))
                    return false;
                try
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        bindings.Add(new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false));
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        bindings.Add(new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, true));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            if (motion is BlendTree tree)
            {
                var complete = true;
                foreach (var child in tree.children ?? Array.Empty<ChildMotion>())
                    complete &= TryCollectMotionBindingKeys(child.motion, bindings, visited);
                return complete;
            }
            return false;
        }

        private static bool ContainsPotentialProxyMotion(
            Motion motion,
            ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return false;
            if (motion is AnimationClip clip)
                return AnimatorOptimizerMotionUtility.IsPotentialProxyClip(clip);
            return motion is BlendTree tree &&
                (tree.children ?? Array.Empty<ChildMotion>()).Any(child =>
                    ContainsPotentialProxyMotion(child.motion, visited));
        }

        private static bool TryNormalizeConditionForType(
            AnimatorCondition source,
            AnimatorControllerParameterType parameterType,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out AnimatorOptimizerCondition normalized)
        {
            normalized = null;
            if (!IsUsableConditionThreshold(source, parameterType))
                return false;
            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                if (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                    source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual)
                    return false;
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    source.threshold != 0f && source.threshold != 1f)
                    return false;
                normalized = new AnimatorOptimizerCondition
                {
                    Parameter = source.parameter,
                    ParameterType = parameterType,
                    Mode = source.mode,
                    Threshold = source.threshold,
                };
                return true;
            }
            if (parameterType == AnimatorControllerParameterType.Int)
            {
                if (source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                    source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less ||
                    source.threshold != Mathf.Round(source.threshold))
                    return false;
                normalized = new AnimatorOptimizerCondition
                {
                    Parameter = source.parameter,
                    ParameterType = parameterType,
                    Mode = source.mode,
                    Threshold = Mathf.Round(source.threshold),
                };
                return true;
            }
            if (parameterType != AnimatorControllerParameterType.Float ||
                !AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(source.threshold,
                    options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, new HashSet<float>(), out var approximate) ||
                source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less)
                return false;
            if (approximate)
                report.AddWarning($"Float 全局参数 '{source.parameter}' 使用显式允许的阈值邻域近似。");
            normalized = new AnimatorOptimizerCondition
            {
                Parameter = source.parameter,
                ParameterType = parameterType,
                Mode = source.mode,
                Threshold = source.threshold,
            };
            return true;
        }

        internal static AnimatorOptimizerMotionRef ToMotionRef(AnimatorState state)
        {
            var independent = AnimatorOptimizerMotionUtility.IsTimeIndependent(state?.motion);
            AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(state?.motion, out var motion);
            return new AnimatorOptimizerMotionRef
            {
                Motion = motion,
                Speed = state == null || independent ? 1f : state.speed,
                CycleOffset = state == null || independent ? 0f : state.cycleOffset,
                Mirror = state != null && state.mirror && !AnimatorOptimizerMotionUtility.IsPropertyOnly(state.motion),
            };
        }

        private static string BuildPlanWarning(AnimatorOptimizerMotionPlan plan)
        {
            switch (plan.Kind)
            {
                case AnimatorOptimizerConversionKind.ConstantMotion:
                    return "该 Layer 的输出是单一可达 Motion。";
                case AnimatorOptimizerConversionKind.BooleanTruthTable:
                    return $"已枚举 {plan.TruthTable.Count} 个 Bool 参数组合，生成嵌套 1D BlendTree；Bool 逻辑保持离散。";
                case AnimatorOptimizerConversionKind.NumericSelector:
                    return "已按数值条件生成 1D BlendTree；Int 保留整数输入约定，Float 优先使用相邻可表示值保持阶跃，近似回退另列警告。";
                case AnimatorOptimizerConversionKind.FiniteParameterTable:
                    return $"已枚举 {plan.DecisionTable.Count} 个 Bool/Int/Float 条件分区组合，生成多维嵌套 1D BlendTree。";
                case AnimatorOptimizerConversionKind.AapStateRegister:
                    return plan.StateRegisterProgram?.RequiresInitialization == true
                        ? "计划生成带一次性 Entry 初始化的 AAP 更新树与状态输出树；每条更新分支持续写初始化标记。"
                        : "计划生成 AAP 状态更新树与输出树；条件未命中时保持原状态，不把阈值空档当作补集。";
                default:
                    return string.Empty;
            }
        }

        private static void CollectPlanBindings(
            AnimatorOptimizerMotionPlan plan,
            AnimatorOptimizerLayerReport report)
        {
            var visited = new HashSet<int>();
            if (plan == null)
                return;
            if (plan.ConstantMotion != null)
                CollectMotionBindings(plan.ConstantMotion.Motion, report, visited);
            if (plan.Branches != null)
            {
                foreach (var branch in plan.Branches)
                    CollectMotionBindings(branch.Motion?.Motion, report, visited);
            }
            if (plan.SelectorSamples != null)
            {
                foreach (var sample in plan.SelectorSamples)
                    CollectMotionBindings(sample.Motion?.Motion, report, visited);
            }
            if (plan.TruthTable != null)
            {
                foreach (var motion in plan.TruthTable.Values)
                    CollectMotionBindings(motion?.Motion, report, visited);
            }
            if (plan.DecisionTable != null)
            {
                foreach (var motion in plan.DecisionTable.Values)
                    CollectMotionBindings(motion?.Motion, report, visited);
            }
            if (plan.StateRegisterStates != null)
            {
                foreach (var state in plan.StateRegisterStates)
                    CollectMotionBindings(state?.motion, report, visited);
            }
        }

        private static void CollectAllBindings(
            AnimatorStateMachine stateMachine,
            AnimatorOptimizerLayerReport report)
        {
            if (stateMachine == null)
                return;
            var visited = new HashSet<int>();
            CollectAllBindings(stateMachine, report, visited);
        }

        private static void CollectAllBindings(
            AnimatorStateMachine stateMachine,
            AnimatorOptimizerLayerReport report,
            HashSet<int> visited)
        {
            if (stateMachine == null)
                return;
            if (stateMachine.behaviours != null && stateMachine.behaviours.Length > 0)
                report.BindingsAreComplete &= stateMachine.behaviours.All(IsPreservableLayerBehaviour);
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                if (child.state.behaviours != null && child.state.behaviours.Length > 0)
                    report.BindingsAreComplete = false;
                CollectMotionBindings(child.state.motion, report, visited);
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                CollectAllBindings(child.stateMachine, report, visited);
        }

        private static void CollectMotionBindings(
            Motion motion,
            AnimatorOptimizerLayerReport report,
            HashSet<int> visited)
        {
            if (motion == null)
                return;
            if (!visited.Add(motion.GetInstanceID()))
                return;
            report.MotionCount++;
            if (motion is AnimationClip clip)
            {
                if (AnimatorOptimizerMotionUtility.IsPotentialProxyClip(clip))
                {
                    report.BindingsAreComplete = false;
                    report.AddReason("Motion 是 VRChat/VRCFury proxy Clip；平台特殊 Motion 必须保持原始 Layer 和资源。");
                }
                if (!AnimatorOptimizerSemanticUtility.HasInspectableBindings(clip))
                    report.BindingsAreComplete = false;
                try
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (AnimatorOptimizerSemanticUtility.IsRotationBinding(binding))
                            report.AddReason("动画包含 Transform 旋转曲线；进入 BlendTree 的旋转混合语义尚未证明等价，保留源层。");
                        if (!AnimatorOptimizerSemanticUtility.IsFiniteCurveData(AnimationUtility.GetEditorCurve(clip, binding)))
                            report.AddReason("动画包含非有限或无效曲线数据（如 NaN 隐藏网格）；不重写其特殊求值语义。");
                        if (binding.type == typeof(Animator))
                        {
                            report.AddReason("动画包含 AAP/Animator 参数曲线；参数保存顺序暂不自动重写。");
                        }
                        report.Bindings.Add(new AnimatorOptimizerBindingKey(
                            binding.path, binding.type, binding.propertyName, false));
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        var objectCurve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        if (objectCurve == null || objectCurve.Any(key => !IsFinite(key.time)))
                            report.AddReason("对象引用动画曲线不可读或含非有限关键帧时间，保留源层。");
                        if (binding.type == typeof(Animator))
                        {
                            report.AddReason("动画包含 Animator 参数对象曲线；AAP 参数写入语义暂不自动重写。");
                        }
                        report.Bindings.Add(new AnimatorOptimizerBindingKey(
                            binding.path, binding.type, binding.propertyName, true));
                    }
                }
                catch (Exception exception)
                {
                    report.BindingsAreComplete = false;
                    report.AddReason("读取动画曲线失败，无法安全检查跨 Layer 冲突: " + exception.Message);
                }
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children ?? Array.Empty<ChildMotion>())
                    CollectMotionBindings(child.motion, report, visited);
                return;
            }
            report.BindingsAreComplete = false;
            report.AddReason("发现无法分析的 Motion 类型: " + motion.GetType().Name);
        }
    }

    /// <summary>Shared physical/virtual proof primitives; these never sample or mutate source assets.</summary>
    public static class AnimatorOptimizerSemanticUtility
    {
        private sealed class ReferenceIdentity
        {
            public readonly long Value = System.Threading.Interlocked.Increment(ref nextReferenceIdentity);
        }

        private static long nextReferenceIdentity;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ReferenceIdentity>
            referenceIdentities = new();

        // Hash codes are not object identities. Weak keys do not retain virtual graphs
        // between builds, and distinct live nodes can never alias in a memoization key.
        public static string ReferenceKey(object value) => value == null ? "null" :
            referenceIdentities.GetValue(value, _ => new ReferenceIdentity()).Value.ToString(CultureInfo.InvariantCulture);

        public static void CollectSerializedStrings(UnityEngine.Object source, ISet<string> names)
        {
            if (source == null) return;
            using var serialized = new SerializedObject(source);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.String && !string.IsNullOrWhiteSpace(property.stringValue))
                    names.Add(property.stringValue);
        }

        public static string AllocateParameterName(string prefix, ISet<string> reserved)
        {
            var name = prefix;
            var suffix = 1;
            while (reserved.Contains(name) || AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(name))
                name = prefix + "_" + suffix++;
            reserved.Add(name);
            return name;
        }

        public static bool IsFiniteOrderedAxis(IReadOnlyList<float> axis)
        {
            if (axis == null || axis.Count == 0 || axis.Count > 4096)
                return false;
            for (var index = 0; index < axis.Count; index++)
                if (!AnimatorOptimizerAnalyzer.IsFinite(axis[index]) || index > 0 && axis[index] <= axis[index - 1])
                    return false;
            return AnimatorOptimizerAnalyzer.IsFinite(axis[axis.Count - 1] - axis[0]);
        }

        // Evaluate a generated finite table, not the original transitions. Another
        // layer may add a sample inside one of its intervals. Such an interval is
        // constant only if every bracketing corner has the same observable output.
        // Nearest-neighbour lookup alone would silently turn interpolation into a step.
        public static bool TryEvaluateConstantTable<T>(IReadOnlyList<IReadOnlyList<float>> axes,
            IReadOnlyList<float> inputs, IReadOnlyDictionary<int, T> table, Func<T, T, bool> equivalent,
            out T result) where T : class
        {
            result = null;
            if (axes == null || inputs == null || table == null || equivalent == null || axes.Count != inputs.Count)
                return false;
            var keys = new List<int> { 0 };
            var stride = 1;
            for (var dimension = 0; dimension < axes.Count; dimension++)
            {
                var axis = axes[dimension];
                var value = inputs[dimension];
                if (!IsFiniteOrderedAxis(axis) || !AnimatorOptimizerAnalyzer.IsFinite(value) || stride > 4096 / axis.Count)
                    return false;
                var low = 0;
                var high = axis.Count - 1;
                if (value <= axis[low]) high = low;
                else if (value >= axis[high]) low = high;
                else
                {
                    while (high - low > 1)
                    {
                        var middle = low + (high - low) / 2;
                        if (axis[middle] <= value) low = middle;
                        else high = middle;
                    }
                    if (axis[low] == value) high = low;
                }
                var count = keys.Count;
                for (var index = 0; index < count; index++)
                {
                    var key = keys[index];
                    keys[index] = key + low * stride;
                    if (high != low) keys.Add(key + high * stride);
                }
                stride *= axis.Count;
            }
            foreach (var key in keys)
            {
                if (!table.TryGetValue(key, out var candidate) || candidate == null)
                    return false;
                if (result == null) result = candidate;
                else if (!equivalent(result, candidate)) return false;
            }
            return result != null;
        }

        // Keep both ends of each constant run. In particular never remove either
        // endpoint of a strict Float step (threshold and its immediate neighbour).
        public static List<int> RetainRunEndpoints(IReadOnlyList<string> signatures)
        {
            var retained = new List<int>();
            for (var index = 0; index < signatures.Count; index++)
                if (index == 0 || index == signatures.Count - 1 || signatures[index] == null ||
                    signatures[index - 1] != signatures[index] || signatures[index] != signatures[index + 1])
                    retained.Add(index);
            return retained;
        }

        public static string DecisionNodeKey(string parameter, IEnumerable<float> values, IEnumerable<string> children) =>
            "decision:" + AnimatorOptimizerSynchronizedGraph.Atom(parameter) +
            AnimatorOptimizerSynchronizedGraph.Atom(string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))) +
            string.Concat(children.Select(AnimatorOptimizerSynchronizedGraph.Atom));

        // No IEEE-754 single lies between a number and its immediate neighbour.
        // Keep equality separately: >t and <t are not complements. Denormal and
        // overflow edges are excluded to avoid platform flush-to-zero differences.
        // This proves finite Float inputs, not arbitrary NaN parameter values.
        public static bool TryAddFloatConditionSamples(float threshold, bool allowApproximation,
            float epsilon, ISet<float> samples, out bool approximate)
        {
            approximate = false;
            if (!AnimatorOptimizerAnalyzer.IsFinite(threshold)) return false;
            var bits = BitConverter.SingleToInt32Bits(threshold);
            var lower = threshold == 0f ? 0f : BitConverter.Int32BitsToSingle(unchecked(bits + (threshold > 0f ? -1 : 1)));
            var upper = threshold == 0f ? 0f : BitConverter.Int32BitsToSingle(unchecked(bits + (threshold > 0f ? 1 : -1)));
            const float minNormal = 1.17549435E-38f;
            if (threshold != 0f && AnimatorOptimizerAnalyzer.IsFinite(lower) &&
                AnimatorOptimizerAnalyzer.IsFinite(upper) && lower < threshold && upper > threshold &&
                Math.Abs(lower) >= minNormal && Math.Abs(upper) >= minNormal &&
                threshold - lower >= minNormal && upper - threshold >= minNormal)
            {
                samples.Add(lower);
                samples.Add(threshold);
                samples.Add(upper);
                // Unity interpolation subtracts neighbouring thresholds as floats;
                // a span that overflows could produce NaN even with equal outputs.
                return samples.Count <= 4096 && AnimatorOptimizerAnalyzer.IsFinite(samples.Max() - samples.Min());
            }
            if (!allowApproximation ||
                !AnimatorOptimizerAnalyzer.IsFloatNeighborhoodRepresentable(threshold, epsilon)) return false;
            approximate = true;
            samples.Add(threshold - epsilon);
            samples.Add(threshold);
            samples.Add(threshold + epsilon);
            return samples.Count <= 4096 && AnimatorOptimizerAnalyzer.IsFinite(samples.Max() - samples.Min());
        }

        // Prove local contradictions only. Unknown declarations, Trigger semantics and
        // malformed thresholds never become evidence for deleting a transition.
        public static bool ConditionsMayBeReachable(
            IEnumerable<AnimatorCondition> conditions,
            Func<string, AnimatorControllerParameterType?> parameterType)
        {
            if (conditions == null || parameterType == null) return true;
            var source = conditions.Take(65).ToArray();
            if (source.Length > 64) return true;
            var types = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            // Validate the ENTIRE conjunction first: a contradiction on X must
            // not erase a Trigger/unknown condition on Y later in the list.
            foreach (var condition in source)
            {
                if (string.IsNullOrWhiteSpace(condition.parameter)) return true;
                if (!types.TryGetValue(condition.parameter, out var type))
                {
                    var resolved = parameterType(condition.parameter);
                    if (!resolved.HasValue) return true;
                    type = resolved.Value;
                    types.Add(condition.parameter, type);
                }
                if (!AnimatorOptimizerFiniteGraph.IsUsable(condition, type)) return true;
            }
            foreach (var group in source.GroupBy(condition => condition.parameter, StringComparer.Ordinal))
            {
                var type = types[group.Key];
                var list = group.ToList();
                if (type == AnimatorControllerParameterType.Bool)
                {
                    if (!list.All(condition => AnimatorOptimizerFiniteGraph.Matches(condition, 0f)) &&
                        !list.All(condition => AnimatorOptimizerFiniteGraph.Matches(condition, 1f))) return false;
                    continue;
                }
                if (type != AnimatorControllerParameterType.Int && type != AnimatorControllerParameterType.Float)
                    continue;
                if (list.Any(condition => !AnimatorOptimizerAnalyzer.IsFinite(condition.threshold) ||
                    condition.mode != AnimatorConditionMode.Greater && condition.mode != AnimatorConditionMode.Less &&
                    condition.mode != AnimatorConditionMode.Equals && condition.mode != AnimatorConditionMode.NotEqual))
                    continue;
                var integer = type == AnimatorControllerParameterType.Int;
                if (integer && list.Any(condition => condition.threshold != Mathf.Round(condition.threshold) ||
                    Math.Abs((double)condition.threshold) > 16777214d))
                    continue;
                var lower = integer ? (double)int.MinValue : double.NegativeInfinity;
                var upper = integer ? (double)int.MaxValue : double.PositiveInfinity;
                double? equal = null;
                var excluded = new HashSet<double>();
                foreach (var condition in list)
                {
                    var value = (double)condition.threshold;
                    switch (condition.mode)
                    {
                        case AnimatorConditionMode.Equals:
                            if (equal.HasValue && equal.Value != value) return false;
                            equal = value;
                            break;
                        case AnimatorConditionMode.NotEqual: excluded.Add(value); break;
                        case AnimatorConditionMode.Greater: lower = Math.Max(lower, value + (integer ? 1d : 0d)); break;
                        case AnimatorConditionMode.Less: upper = Math.Min(upper, value - (integer ? 1d : 0d)); break;
                    }
                }
                if (integer ? lower > upper : lower >= upper) return false;
                if (equal.HasValue)
                {
                    var value = equal.Value;
                    if (excluded.Contains(value) || (integer ? value < lower || value > upper : value <= lower || value >= upper))
                        return false;
                }
                else if (integer && upper - lower + 1 <= excluded.Count &&
                    excluded.Count(value => value >= lower && value <= upper) == upper - lower + 1)
                    return false;
            }
            return true;
        }

        // Trigger conditions consume/reset parameter state when a transition fires.
        // Equality of output curves cannot prove that deleting that transition is safe.
        public static bool AreReadOnlyConditions(IEnumerable<AnimatorCondition> conditions,
            Func<string, AnimatorControllerParameterType?> resolveType)
        {
            if (conditions == null || resolveType == null) return false;
            return conditions.All(condition =>
            {
                if (string.IsNullOrWhiteSpace(condition.parameter)) return false;
                var type = resolveType(condition.parameter);
                return type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Int ||
                    type == AnimatorControllerParameterType.Float;
            });
        }

        public static bool HasInspectableBindings(AnimationClip clip) => clip != null &&
            !clip.humanMotion && !clip.hasRootCurves && !clip.hasMotionCurves && !clip.hasGenericRootTransform &&
            !AnimatorOptimizerMotionUtility.IsPotentialProxyClip(clip);

        public static bool IsOrdinaryClip(AnimationClip clip)
        {
            return HasInspectableBindings(clip) && !clip.legacy && clip.events.Length == 0;
        }

        public static bool IsRotationBinding(EditorCurveBinding binding) => binding.type == typeof(Transform) &&
            (binding.propertyName?.StartsWith("m_LocalRotation.", StringComparison.Ordinal) == true ||
             binding.propertyName?.StartsWith("localEulerAngles", StringComparison.Ordinal) == true ||
             binding.propertyName?.StartsWith("m_LocalEulerAngles", StringComparison.Ordinal) == true);

        public static bool IsFiniteCurveData(AnimationCurve curve)
        {
            if (curve == null) return false;
            var keys = curve.keys;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                if (!AnimatorOptimizerAnalyzer.IsFinite(key.time) || !AnimatorOptimizerAnalyzer.IsFinite(key.value) ||
                    float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent) ||
                    index > 0 && keys[index - 1].time >= key.time ||
                    (key.weightedMode & WeightedMode.In) != 0 &&
                        (!AnimatorOptimizerAnalyzer.IsFinite(key.inWeight) || key.inWeight < 0f || key.inWeight > 1f) ||
                    (key.weightedMode & WeightedMode.Out) != 0 &&
                        (!AnimatorOptimizerAnalyzer.IsFinite(key.outWeight) || key.outWeight < 0f || key.outWeight > 1f)) return false;
            }
            return keys.Length < 2 || AnimatorOptimizerAnalyzer.IsFinite(keys[keys.Length - 1].time - keys[0].time);
        }

        public static bool TryGetHighQualityCurves(AnimationClip clip, out bool value)
        {
            value = false;
            if (clip == null) return false;
            try
            {
                using var serialized = new SerializedObject(clip);
                var property = serialized.FindProperty("m_UseHighQualityCurve");
                if (property == null) return false;
                value = property.boolValue;
                return true;
            }
            catch { return false; }
        }

        public static bool IsConstantCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
                return true;
            var keys = curve.keys;
            var value = keys[0].value;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                if (!AnimatorOptimizerAnalyzer.IsFinite(key.time) || !AnimatorOptimizerAnalyzer.IsFinite(key.value) ||
                    key.value != value)
                    return false;
                if (index == 0)
                    continue; // A single-key curve is constant; unused outer tangents are irrelevant.
                var previous = keys[index - 1];
                if (previous.time >= key.time || float.IsNaN(previous.outTangent) || float.IsNaN(key.inTangent))
                    return false;
                // Unity's constant/stepped segments use infinite tangents. Equal endpoints
                // stay constant; otherwise prove a flat Hermite/Bezier segment analytically.
                if (float.IsInfinity(previous.outTangent) || float.IsInfinity(key.inTangent))
                    continue;
                if (previous.outTangent != 0f || key.inTangent != 0f)
                    return false;
                if ((previous.weightedMode & WeightedMode.Out) != 0 &&
                    (!AnimatorOptimizerAnalyzer.IsFinite(previous.outWeight) || previous.outWeight < 0f || previous.outWeight > 1f) ||
                    (key.weightedMode & WeightedMode.In) != 0 &&
                    (!AnimatorOptimizerAnalyzer.IsFinite(key.inWeight) || key.inWeight < 0f || key.inWeight > 1f))
                    return false;
            }
            return true;
        }

        public static bool IsConstantObjectCurve(ObjectReferenceKeyframe[] curve)
        {
            if (curve == null || curve.Length == 0)
                return true;
            if (curve[0].time != 0f)
                return false;
            for (var index = 0; index < curve.Length; index++)
                if (!AnimatorOptimizerAnalyzer.IsFinite(curve[index].time) || curve[index].value != curve[0].value ||
                    index > 0 && curve[index].time <= curve[index - 1].time)
                    return false;
            return true;
        }
    }

    internal enum AnimatorOptimizerMotionShape
    {
        Missing,
        AnimationClip,
        BlendTree,
        Unsupported,
    }

    internal sealed class AnimatorOptimizerMotionInfo
    {
        public Motion Motion;
        public AnimatorOptimizerMotionShape Shape;
        public bool IsTimeIndependent;
        public bool IsStatic;
        public bool HasParameterDrivenBlend;
        public int NodeCount;
        public HashSet<AnimatorOptimizerBindingKey> Bindings = new HashSet<AnimatorOptimizerBindingKey>();
        public HashSet<string> BlendParameters = new HashSet<string>(StringComparer.Ordinal);
        public string StructuralKey;
    }

    /// <summary>
    /// Shared semantic inspection for AnimationClip and BlendTree motions.
    /// A harmless singleton wrapper is equivalent to its child, while a different
    /// blend rule, parameter, threshold or child setting is not.
    /// </summary>
    internal static class AnimatorOptimizerMotionUtility
    {
        public static bool IsAcyclic(Motion motion)
        {
            return IsAcyclic(motion, new HashSet<int>(), new HashSet<int>());
        }

        private static bool IsAcyclic(Motion motion, ISet<int> active, ISet<int> done)
        {
            if (motion == null || done.Contains(motion.GetInstanceID()))
                return true;
            if (active.Count >= 128 || !active.Add(motion.GetInstanceID()))
                return false;
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                    if (!AnimatorOptimizerAnalyzer.IsFinite(child.timeScale) || !AnimatorOptimizerAnalyzer.IsFinite(child.cycleOffset) ||
                        !AnimatorOptimizerAnalyzer.IsFinite(child.threshold) ||
                        !AnimatorOptimizerAnalyzer.IsFinite(child.position.x) || !AnimatorOptimizerAnalyzer.IsFinite(child.position.y) ||
                        !IsAcyclic(child.motion, active, done))
                        return false;
            }
            else if (!(motion is AnimationClip))
                return false;
            active.Remove(motion.GetInstanceID());
            done.Add(motion.GetInstanceID());
            return true;
        }

        public static bool IsPotentialProxyClip(AnimationClip clip)
        {
            return clip != null && clip.name != null &&
                   clip.name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase);
        }

        public static AnimatorOptimizerMotionInfo Analyze(Motion motion)
        {
            if (!IsAcyclic(motion))
                return new AnimatorOptimizerMotionInfo { Motion = motion, Shape = AnimatorOptimizerMotionShape.Unsupported };
            var info = new AnimatorOptimizerMotionInfo
            {
                Motion = motion,
                StructuralKey = BuildStructuralKey(motion),
            };
            AnalyzeInto(motion, info, new HashSet<int>());
            return info;
        }

        public static string BuildStructuralKey(Motion motion)
        {
            return BuildStructuralKey(motion, new HashSet<int>());
        }

        public static bool AreStructurallyEquivalent(Motion left, Motion right)
        {
            return string.Equals(BuildStructuralKey(left), BuildStructuralKey(right), StringComparison.Ordinal);
        }

        public static bool IsTimeIndependent(Motion motion)
        {
            return IsAcyclic(motion) && IsTimeIndependentCore(motion);
        }

        // Restrict mirror elimination to property-only clips. Bone/Animator bindings,
        // imported humanoid/root motion and unknown motions are deliberately excluded.
        public static bool IsPropertyOnly(Motion motion)
        {
            if (!IsAcyclic(motion))
                return false;
            var pending = new Stack<Motion>();
            var visited = new HashSet<Motion>();
            if (motion != null) pending.Push(motion);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current)) continue;
                if (current is AnimationClip clip)
                {
                    if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip) ||
                        AnimationUtility.GetAnimationClipSettings(clip).hasAdditiveReferencePose ||
                        AnimationUtility.GetAnimationClipSettings(clip).additiveReferencePoseClip != null ||
                        AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))
                            .Any(binding => binding.type == typeof(Animator) || binding.type == typeof(Transform)))
                        return false;
                }
                else if (current is BlendTree tree)
                    foreach (var child in tree.children)
                        if (child.motion != null) pending.Push(child.motion);
            }
            return true;
        }

        private static bool IsTimeIndependentCore(Motion motion)
        {
            if (motion == null)
                return true;
            if (motion is AnimationClip clip)
                return IsTimeIndependentClip(clip);
            if (motion is BlendTree tree)
            {
                return (tree.children ?? Array.Empty<ChildMotion>()).All(child =>
                    child.motion != null && IsTimeIndependentCore(child.motion));
            }
            return false;
        }

        public static bool IsStaticClip(AnimationClip clip)
        {
            if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip))
                return false;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                return false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (binding.type == typeof(Animator) || !IsConstantCurve(curve))
                    return false;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (binding.type == typeof(Animator) || !IsConstantObjectReferenceCurve(curve))
                    return false;
            }
            return true;
        }

        private static bool IsConstantObjectReferenceCurve(ObjectReferenceKeyframe[] curve)
        {
            return AnimatorOptimizerSemanticUtility.IsConstantObjectCurve(curve);
        }

        public static bool TryNormalizeSingletonTree(Motion motion, out Motion normalized)
        {
            normalized = motion;
            if (!IsAcyclic(motion))
                return false;
            var changed = false;
            while (normalized is BlendTree tree && TryGetSafeSingletonChild(tree, out var child, allowStaticSettings: true))
            {
                normalized = child.motion;
                changed = true;
            }
            return changed;
        }

        public static bool TryGetSafeSingletonChild(BlendTree tree, out ChildMotion child, bool allowStaticSettings = false)
        {
            child = default;
            if (tree == null || tree.children == null || tree.children.Length != 1)
                return false;
            child = tree.children[0];
            if (child.motion == null ||
                (child.timeScale != 1f || child.cycleOffset != 0f) &&
                    (!allowStaticSettings || !IsTimeIndependent(child.motion)) ||
                child.mirror && (!allowStaticSettings || !IsPropertyOnly(child.motion)))
                return false;
            // Missing Direct weight parameters do not imply a weight of one.
            if (tree.blendType != BlendTreeType.Simple1D)
                return false;
            return true;
        }

        public static int CountNodes(Motion motion)
        {
            var visited = new HashSet<Motion>();
            var pending = new Stack<Motion>();
            if (motion != null)
                pending.Push(motion);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current) || current is not BlendTree tree)
                    continue;
                foreach (var child in tree.children)
                    if (child.motion != null)
                        pending.Push(child.motion);
            }
            return visited.Count;
        }

        private static bool IsTimeIndependentClip(AnimationClip clip)
        {
            return IsStaticClip(clip);
        }

        private static void AnalyzeInto(
            Motion motion,
            AnimatorOptimizerMotionInfo info,
            HashSet<int> visited)
        {
            if (motion == null)
            {
                info.Shape = AnimatorOptimizerMotionShape.Missing;
                info.IsTimeIndependent = false;
                info.IsStatic = true;
                return;
            }
            if (!visited.Add(motion.GetInstanceID()))
            {
                info.IsTimeIndependent = false;
                info.IsStatic = false;
                info.Shape = AnimatorOptimizerMotionShape.Unsupported;
                return;
            }

            info.NodeCount++;
            if (motion is AnimationClip clip)
            {
                info.Shape = AnimatorOptimizerMotionShape.AnimationClip;
                info.IsTimeIndependent = IsTimeIndependentClip(clip);
                info.IsStatic = IsStaticClip(clip);
                CollectClipBindings(clip, info.Bindings);
                return;
            }
            if (!(motion is BlendTree tree))
            {
                info.Shape = AnimatorOptimizerMotionShape.Unsupported;
                info.IsTimeIndependent = false;
                info.IsStatic = false;
                return;
            }

            info.Shape = AnimatorOptimizerMotionShape.BlendTree;
            if (tree.blendType != BlendTreeType.Direct && !string.IsNullOrWhiteSpace(tree.blendParameter))
            {
                info.HasParameterDrivenBlend = true;
                info.BlendParameters.Add(tree.blendParameter);
            }
            if (tree.blendType != BlendTreeType.Direct && tree.blendType != BlendTreeType.Simple1D &&
                !string.IsNullOrWhiteSpace(tree.blendParameterY))
            {
                info.HasParameterDrivenBlend = true;
                info.BlendParameters.Add(tree.blendParameterY);
            }

            var children = tree.children ?? Array.Empty<ChildMotion>();
            info.IsTimeIndependent = children.Length > 0;
            info.IsStatic = children.Length > 0;
            foreach (var child in children)
            {
                if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrWhiteSpace(child.directBlendParameter))
                {
                    info.HasParameterDrivenBlend = true;
                    info.BlendParameters.Add(child.directBlendParameter);
                }
                var childInfo = Analyze(child.motion);
                info.IsTimeIndependent &= childInfo.IsTimeIndependent;
                info.IsStatic &= childInfo.IsStatic;
                info.NodeCount += childInfo.NodeCount;
                info.Bindings.UnionWith(childInfo.Bindings);
                info.BlendParameters.UnionWith(childInfo.BlendParameters);
                info.HasParameterDrivenBlend |= childInfo.HasParameterDrivenBlend;
            }
        }

        private static void CollectClipBindings(
            AnimationClip clip,
            ISet<AnimatorOptimizerBindingKey> bindings)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                bindings.Add(new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false));
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                bindings.Add(new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, true));
        }

        private static bool IsConstantCurve(AnimationCurve curve)
        {
            return AnimatorOptimizerSemanticUtility.IsConstantCurve(curve);
        }

        private static string BuildStructuralKey(Motion motion, HashSet<int> active)
        {
            if (motion == null)
                return "null";
            var instanceId = motion.GetInstanceID();
            if (active.Count >= 128 || !active.Add(instanceId))
                return "cycle:" + instanceId.ToString(CultureInfo.InvariantCulture);
            if (motion is AnimationClip clip)
            {
                active.Remove(instanceId);
                return BuildClipStructuralKey(clip);
            }
            if (!(motion is BlendTree tree))
            {
                active.Remove(instanceId);
                return "unsupported:" + instanceId.ToString(CultureInfo.InvariantCulture);
            }
            if (TryGetSafeSingletonChild(tree, out var singleton))
            {
                var singletonKey = BuildStructuralKey(singleton.motion, active);
                active.Remove(instanceId);
                return singletonKey;
            }

            var key = string.Concat(new[]
            {
                "tree",
                tree.blendType.ToString(),
                tree.blendParameter ?? string.Empty,
                tree.blendParameterY ?? string.Empty,
                tree.minThreshold.ToString("R", CultureInfo.InvariantCulture),
                tree.maxThreshold.ToString("R", CultureInfo.InvariantCulture),
                tree.useAutomaticThresholds.ToString(),
                GetNormalizedBlendValuesKey(tree),
                string.Concat((tree.children ?? Array.Empty<ChildMotion>()).Select(child =>
                    string.Concat(new[]
                    {
                        BuildStructuralKey(child.motion, active),
                        child.threshold.ToString("R", CultureInfo.InvariantCulture),
                        child.timeScale.ToString("R", CultureInfo.InvariantCulture),
                        child.cycleOffset.ToString("R", CultureInfo.InvariantCulture),
                        child.mirror.ToString(),
                        child.directBlendParameter ?? string.Empty,
                        child.position.x.ToString("R", CultureInfo.InvariantCulture),
                        child.position.y.ToString("R", CultureInfo.InvariantCulture),
                        }.Select(AnimatorOptimizerSynchronizedGraph.Atom))))
                    }.Select(AnimatorOptimizerSynchronizedGraph.Atom));
            active.Remove(instanceId);
            return key;
        }

        private static string GetNormalizedBlendValuesKey(BlendTree tree)
        {
            if (tree == null)
                return "normalized:null";
            try
            {
                var serializedObject = new SerializedObject(tree);
                var property = serializedObject.FindProperty("m_NormalizedBlendValues");
                return property == null ? "normalized:unknown:" + tree.GetInstanceID() : "normalized:" + property.boolValue;
            }
            catch
            {
                return "normalized:unknown:" + tree.GetInstanceID();
            }
        }

        private static string BuildClipStructuralKey(AnimationClip clip)
        {
            if (clip == null)
                return "clip:null";
            // Imported humanoid/root data is not fully represented by editor curves.
            // Equality of those curves cannot prove equality of the imported clip.
            if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip))
                return "clip:opaque:" + clip.GetInstanceID().ToString(CultureInfo.InvariantCulture);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(clip, out var highQualityCurves))
                return "clip:opaque:" + clip.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            var floatCurves = AnimationUtility.GetCurveBindings(clip)
                .OrderBy(BindingKeyText, StringComparer.Ordinal)
                .Select(binding => "f:" + BindingKeyText(binding) + ":" +
                    CurveKeyText(AnimationUtility.GetEditorCurve(clip, binding)));
            var objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .OrderBy(BindingKeyText, StringComparer.Ordinal)
                .Select(binding => "o:" + BindingKeyText(binding) + ":" +
                    ObjectCurveKeyText(AnimationUtility.GetObjectReferenceCurve(clip, binding)));
            var events = (clip.events ?? Array.Empty<AnimationEvent>())
                .Select(animationEvent => string.Join(",", new[]
                {
                    animationEvent.time.ToString("R", CultureInfo.InvariantCulture),
                    animationEvent.functionName ?? string.Empty,
                    animationEvent.stringParameter ?? string.Empty,
                    animationEvent.floatParameter.ToString("R", CultureInfo.InvariantCulture),
                    animationEvent.intParameter.ToString(CultureInfo.InvariantCulture),
                    animationEvent.objectReferenceParameter == null
                        ? "0"
                        : animationEvent.objectReferenceParameter.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                }));

                 return "clip|" + clip.legacy + "|hq:" + highQualityCurves + "|" +
                     clip.frameRate.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + clip.wrapMode + "|" + settings.loopTime + "|" + settings.loopBlend +
                     "|" + settings.loopBlendOrientation + "|" + settings.loopBlendPositionY +
                     "|" + settings.loopBlendPositionXZ + "|" + settings.keepOriginalOrientation +
                     "|" + settings.keepOriginalPositionY + "|" + settings.keepOriginalPositionXZ +
                     "|" + settings.heightFromFeet +
                   "|" + settings.hasAdditiveReferencePose +
                   "|" + settings.additiveReferencePoseTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + (settings.additiveReferencePoseClip == null
                       ? "0"
                       : settings.additiveReferencePoseClip.GetInstanceID().ToString(CultureInfo.InvariantCulture)) +
                   "|" + settings.startTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + settings.stopTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + settings.orientationOffsetY.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + settings.level.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + settings.cycleOffset.ToString("R", CultureInfo.InvariantCulture) +
                   "|bounds:" + clip.localBounds.center.x.ToString("R", CultureInfo.InvariantCulture) +
                   "," + clip.localBounds.center.y.ToString("R", CultureInfo.InvariantCulture) +
                   "," + clip.localBounds.center.z.ToString("R", CultureInfo.InvariantCulture) +
                   "," + clip.localBounds.extents.x.ToString("R", CultureInfo.InvariantCulture) +
                   "," + clip.localBounds.extents.y.ToString("R", CultureInfo.InvariantCulture) +
                   "," + clip.localBounds.extents.z.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + string.Concat(floatCurves.Concat(objectCurves).Select(AnimatorOptimizerSynchronizedGraph.Atom)) +
                   "|events:" + string.Join(";", events);
        }

        private static string BindingKeyText(EditorCurveBinding binding)
        {
            return AnimatorOptimizerSynchronizedGraph.BindingKey(binding);
        }

        private static string CurveKeyText(AnimationCurve curve)
        {
            if (curve == null)
                return "null";
            return curve.preWrapMode + ":" + curve.postWrapMode + ":" + string.Join(";", curve.keys.Select(key =>
                string.Join(",", new[]
                {
                    key.time.ToString("R", CultureInfo.InvariantCulture),
                    key.value.ToString("R", CultureInfo.InvariantCulture),
                    key.inTangent.ToString("R", CultureInfo.InvariantCulture),
                    key.outTangent.ToString("R", CultureInfo.InvariantCulture),
                    key.inWeight.ToString("R", CultureInfo.InvariantCulture),
                    key.outWeight.ToString("R", CultureInfo.InvariantCulture),
                    key.weightedMode.ToString(),
                })));
        }

        private static string ObjectCurveKeyText(ObjectReferenceKeyframe[] curve)
        {
            if (curve == null)
                return "null";
            return string.Join(";", curve.Select(key => string.Join(",", new[]
            {
                key.time.ToString("R", CultureInfo.InvariantCulture),
                key.value == null ? "0" : key.value.GetInstanceID().ToString(CultureInfo.InvariantCulture),
            })));
        }
    }
}
