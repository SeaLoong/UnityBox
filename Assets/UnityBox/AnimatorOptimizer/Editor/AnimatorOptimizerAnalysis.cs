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
        // These are resource safeguards, not semantic eligibility checks. Exact
        // decision plans are still preferred whenever the configured finite domain
        // can be enumerated; generated trees use structural sharing to reduce cost.
        public int MaxBooleanTruthTableEntries = 4096;
        public int MaxNumericSelectorSamples = 256;
        public int MaxPriorityCombinations = 4096;
        public bool MergeLayersWithEquivalentMasks = true;
        public bool ResolveOverridePriorityWithStaticComposite = true;
        public bool EnableAapStateRegisterOptimization = false;
        public bool RemoveEffectivelyEmptyLayers = true;
        public bool NormalizeAlwaysOneParameters = true;
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
                FloatConditionEpsilon = FloatConditionEpsilon,
                MaxBooleanTruthTableEntries = MaxBooleanTruthTableEntries,
                MaxNumericSelectorSamples = MaxNumericSelectorSamples,
                MaxPriorityCombinations = MaxPriorityCombinations,
                MergeLayersWithEquivalentMasks = MergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = ResolveOverridePriorityWithStaticComposite,
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
            IEnumerable<AnimatorOptimizerLayerReport> layers)
        {
            Controller = controller;
            _layers = new List<AnimatorOptimizerLayerReport>(layers ?? Enumerable.Empty<AnimatorOptimizerLayerReport>());
        }

        public AnimatorController Controller { get; }
        public IReadOnlyList<AnimatorOptimizerLayerReport> Layers => _layers;
        public int ConvertibleLayerCount => _layers.Count(layer => layer.CanConvert);
        public IEnumerable<AnimatorOptimizerLayerReport> ConvertibleLayers => _layers.Where(layer => layer.CanConvert);
    }

    public sealed class AnimatorOptimizerLayerReport
    {
        private readonly List<string> _reasons = new List<string>();
        private readonly List<string> _warnings = new List<string>();

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
        public IReadOnlyCollection<string> RequiredFloatParameters => Plan == null
            ? (IReadOnlyCollection<string>)Array.Empty<string>()
            : Plan.RequiredFloatParameters;

        internal AnimatorControllerLayer SourceLayer { get; }
        internal HashSet<AnimatorOptimizerBindingKey> Bindings { get; }
        internal bool BindingsAreComplete { get; set; }
        internal AnimatorOptimizerMotionPlan Plan { get; set; }

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

        private static string BuildMaskKey(AvatarMask mask)
        {
            if (mask == null)
                return "<none>";

            try
            {
                var serializedObject = new SerializedObject(mask);
                var iterator = serializedObject.GetIterator();
                var key = new System.Text.StringBuilder();
                var enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyPath == "m_Name" || iterator.propertyPath == "m_ObjectHideFlags" ||
                        iterator.propertyPath == "m_Script" || iterator.propertyPath == "m_EditorClassIdentifier")
                        continue;
                    key.Append(iterator.propertyPath).Append(':').Append(iterator.propertyType).Append('=');
                    switch (iterator.propertyType)
                    {
                        case SerializedPropertyType.Boolean:
                            key.Append(iterator.boolValue);
                            break;
                        case SerializedPropertyType.Integer:
                        case SerializedPropertyType.Enum:
                            key.Append(iterator.intValue);
                            break;
                        case SerializedPropertyType.Float:
                            key.Append(iterator.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                            break;
                        case SerializedPropertyType.String:
                            key.Append(iterator.stringValue);
                            break;
                        case SerializedPropertyType.ObjectReference:
                            key.Append(iterator.objectReferenceValue == null
                                ? 0
                                : iterator.objectReferenceValue.GetInstanceID());
                            break;
                    }
                    key.Append(';');
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
        public HashSet<string> RequiredFloatParameters = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> ReferencedParameters = new HashSet<string>(StringComparer.Ordinal);
        public string StructuralKey;
        public List<AnimatorState> StateRegisterStates;
        public List<string> StateRegisterBooleanParameters;
        public List<AnimatorOptimizerDecisionDimension> StateRegisterDecisionDimensions;
        public Dictionary<int, Dictionary<int, int>> StateRegisterNextStates;
        public Dictionary<int, int> StateRegisterEntryStates;
        public int StateRegisterDefaultState;
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

        public AnimatorOptimizerMotionRef Clone()
        {
            return new AnimatorOptimizerMotionRef
            {
                Motion = Motion,
                Speed = Speed,
                CycleOffset = CycleOffset,
                Mirror = Mirror,
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
                var expected = Mode == AnimatorConditionMode.If ||
                              (Mode == AnimatorConditionMode.Equals && Mathf.Approximately(Threshold, 1f)) ||
                              (Mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(Threshold, 0f));
                return Mode == AnimatorConditionMode.IfNot ||
                       (Mode == AnimatorConditionMode.Equals && Mathf.Approximately(Threshold, 0f))
                    ? !boolValue
                    : expected == boolValue;
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
            TypeName = type == null ? string.Empty : type.FullName ?? type.Name;
            PropertyName = propertyName ?? string.Empty;
            ObjectReference = objectReference;
        }

        private string Path { get; }
        private string TypeName { get; }
        private string PropertyName { get; }
        private bool ObjectReference { get; }

        public bool Equals(AnimatorOptimizerBindingKey other)
        {
            return ObjectReference == other.ObjectReference &&
                   string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                   string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
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
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(TypeName);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(PropertyName);
                hash = hash * 397 ^ (ObjectReference ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            return (ObjectReference ? "PPtr:" : "Curve:") + Path + ":" + TypeName + ":" + PropertyName;
        }

        public bool IsObjectReference => ObjectReference;
    }

    public static class AnimatorOptimizerAnalyzer
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
            for (var index = 0; index < layers.Length; index++)
                reports.Add(AnalyzeLayer(controller, index, layers[index], options));

            return new AnimatorOptimizerReport(controller, reports);
        }

        private static AnimatorOptimizerLayerReport AnalyzeLayer(
            AnimatorController controller,
            int index,
            AnimatorControllerLayer layer,
            AnimatorOptimizerOptions options)
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
            if (layer.syncedLayerIndex >= 0 || layer.syncedLayerAffectsTiming)
                report.AddReason("Synced Layer/Synced Timing 依赖其它 Layer 的状态结构或时间，暂不转换。");
            if (layer.iKPass)
                report.AddReason("Layer 开启了 IK Pass；合并后无法保留独立 IK Pass 边界。");
            if (!Mathf.Approximately(layer.defaultWeight, 1f))
                report.AddReason("Layer 默认权重不是 1；运行时 Animator.SetLayerWeight 无法映射为静态 Direct 权重。");
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
                report.AddWarning("StateMachine 只包含可复制的 MMD LayerControl；合并时会复制到生成 Layer。");
            }
            var states = layer.stateMachine.states ?? Array.Empty<ChildAnimatorState>();
            var nestedStateMachines = layer.stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>();
            if (states.Length == 0 && nestedStateMachines.Length == 0)
            {
                report.AddReason("StateMachine 没有 State，无法生成 Motion。");
                return report;
            }

            var analyzableStates = GetPotentiallyReachableStates(controller, layer.stateMachine, states);

            if (!TryBuildPlan(controller, layer.stateMachine, analyzableStates, options, report, out var plan))
            {
                if (layer.stateMachine.entryTransitions != null && layer.stateMachine.entryTransitions.Length > 0)
                    report.AddReason("包含 Entry Transition，且无法证明入口目标与其它 State 的可观测效果等价。");
                CollectAllBindings(layer.stateMachine, report);
                return report;
            }

            CollectPlanBindings(plan, report);
            if (!ValidatePlanParameters(controller, plan, options, report))
            {
                CollectAllBindings(layer.stateMachine, report);
                return report;
            }
            report.Plan = plan;
            plan.StructuralKey = BuildStructuralKey(plan);
            report.ConversionKind = plan.Kind;
            report.AddWarning(BuildPlanWarning(plan));
            return report;
        }

        internal static bool IsPreservableLayerBehaviour(StateMachineBehaviour behaviour)
        {
            return behaviour != null && string.Equals(
                behaviour.GetType().Name, "ModularAvatarMMDLayerControl", StringComparison.Ordinal);
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
                if (!string.IsNullOrWhiteSpace(tree.blendParameterY))
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
            foreach (var transition in stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>())
            {
                if (transition == null || !TransitionMayBeReachable(controller, transition.conditions))
                    continue;
                Add(transition.destinationState);
            }

            var anyTransitions = GetEffectiveTransitions(stateMachine.anyStateTransitions);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var transition in anyTransitions)
                {
                    if (TransitionMayBeReachable(controller, transition.conditions))
                        Add(transition.destinationState);
                }
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
                return active.Where(transition => transition.solo).ToList();
            return active;
        }

        private static bool TransitionMayBeReachable(
            AnimatorController controller,
            IReadOnlyList<AnimatorCondition> conditions)
        {
            // Unknown parameters, Triggers, time conditions and complicated numeric
            // intersections remain possible. Only reject a condition set when a Bool
            // contradiction can be proven locally.
            var boolRequirements = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var condition in conditions ?? Array.Empty<AnimatorCondition>())
            {
                var parameter = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                    .FirstOrDefault(candidate => string.Equals(candidate.name, condition.parameter, StringComparison.Ordinal));
                if (parameter == null || parameter.type != AnimatorControllerParameterType.Bool)
                    continue;
                if (condition.mode != AnimatorConditionMode.If && condition.mode != AnimatorConditionMode.IfNot &&
                    condition.mode != AnimatorConditionMode.Equals && condition.mode != AnimatorConditionMode.NotEqual)
                    continue;
                var isBoolLike = condition.mode == AnimatorConditionMode.If ||
                                 condition.mode == AnimatorConditionMode.IfNot ||
                                 Mathf.Approximately(condition.threshold, 0f) ||
                                 Mathf.Approximately(condition.threshold, 1f);
                if (!isBoolLike)
                    continue;
                var expected = condition.mode == AnimatorConditionMode.If ||
                               condition.mode == AnimatorConditionMode.Equals && Mathf.Approximately(condition.threshold, 1f) ||
                               condition.mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(condition.threshold, 0f);
                if (condition.mode == AnimatorConditionMode.IfNot ||
                    condition.mode == AnimatorConditionMode.Equals && Mathf.Approximately(condition.threshold, 0f) ||
                    condition.mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(condition.threshold, 1f))
                    expected = false;
                if (boolRequirements.TryGetValue(condition.parameter, out var previous) && previous != expected)
                    return false;
                boolRequirements[condition.parameter] = expected;
            }
            return true;
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
                if (TryBuildNestedEffectEquivalentPlan(stateMachine, report, out plan))
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
                child.state != null && GetEffectiveAapTransitions(child.state.transitions).Count == 0);
            var effectiveAnyTransitions = GetEffectiveAapTransitions(stateMachine.anyStateTransitions);
            var noAnyTransitions = effectiveAnyTransitions.Count == 0;
            if (TryBuildEffectEquivalentPlan(controller, stateMachine, childStates, report, out plan))
            {
                report.AddWarning("State/Transition 可能包含 Exit、Duration 或 Entry 逻辑，但所有可达 Motion 的可观测效果完全相同，已折叠为单一 Motion。");
                return true;
            }

            if (TryBuildEntryDrivenPlan(controller, stateMachine, childStates, options, report, out plan))
            {
                report.AddWarning("Entry 条件与 State Exit 条件在有限参数域内组成无历史函数，已编译为决策 BlendTree。");
                return true;
            }

            if (allStatesAreStatic && noAnyTransitions &&
                (stateMachine.entryTransitions == null || stateMachine.entryTransitions.Length == 0))
            {
                if (!ValidateState(controller, defaultState, report))
                    return false;

                plan = CreateConstantPlan(defaultState);
                return true;
            }

            // A state-dependent graph can still be a pure function when every possible
            // source state produces the same output for each finite parameter value.
            // Prove that convergence before falling back to the more restrictive branch
            // extraction rules below.
            if (noAnyTransitions &&
                TryBuildStateIndependentOutputPlan(controller, stateMachine, childStates, options, report, out plan))
            {
                report.AddWarning("状态依赖 Transition 在有限参数域下对所有源 State 收敛到同一 Motion，已消除 State 历史并生成 BlendTree。");
                return true;
            }

            if (options.EnableAapStateRegisterOptimization &&
                childStates.Any(child => child.state != null &&
                    child.state.transitions != null && child.state.transitions.Length > 0) &&
                TryBuildAapStateRegisterPlan(controller, stateMachine, childStates, options, report, out plan))
            {
                report.AddWarning("已用 AAP StateRegister 保留 Bool Transition 的 State 历史，并把输出 Motion 转为 1D BlendTree。");
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
                transitions.AddRange(effectiveAnyTransitions);
            }
            else
            {
                if (childStates.Count == 2 && childStates.All(child =>
                        child.state != null && GetEffectiveAapTransitions(child.state.transitions).Count == 1))
                {
                    transitions.AddRange(childStates.Select(child => GetEffectiveAapTransitions(child.state.transitions)[0]));
                }
                else if (!TryExtractStateIndependentTransitions(childStates, out transitions))
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

            if (TryBuildBooleanTruthTable(branches, options, report, out plan))
                return true;
            if (TryBuildNumericSelector(branches, options, report, out plan))
                return true;
            if (TryBuildFiniteParameterTable(controller, branches, options, report, out plan))
                return true;

            report.AddReason("状态条件同时涉及多个数值参数，或包含无法精确表达的逻辑组合；当前版本不会生成可能改变语义的近似树。");
            return false;
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
                stateMachine.entryTransitions == null || stateMachine.entryTransitions.Length == 0 ||
                stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Length > 0 ||
                stateMachine.defaultState == null || childStates.All(child => child.state != stateMachine.defaultState))
                return false;

            var states = childStates.Select(child => child.state).Where(state => state != null).ToList();
            var stateSet = new HashSet<AnimatorState>(states);
            var entryTransitions = new List<NormalizedStateTransition>();
            for (var order = 0; order < stateMachine.entryTransitions.Length; order++)
            {
                var transition = stateMachine.entryTransitions[order];
                if (transition == null || transition.destinationState == null ||
                    transition.destinationStateMachine != null || !stateSet.Contains(transition.destinationState) ||
                    transition.mute || transition.solo)
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
                stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Length == 0 &&
                states.All(state => state.transitions == null || state.transitions.Length == 0))
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
                    if (transition == null || !transition.isExit || transition.destinationState != null ||
                        transition.destinationStateMachine != null || transition.canTransitionToSelf ||
                        transition.hasExitTime || !Mathf.Approximately(transition.duration, 0f) ||
                        !Mathf.Approximately(transition.offset, 0f))
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
            if (dimensions.Any(dimension => dimension.IsApproximate))
                report.AddWarning("Entry/Exit 决策包含 Float 条件；AAP/BlendTree 只在阈值邻域采样，边界处属于近似语义。");

            var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
            var indices = new int[dimensions.Count];
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
                    if (exitsNow != (desired != state))
                        return false;
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
                else if (type == AnimatorControllerParameterType.Float && options.AllowFloatConditionApproximation)
                {
                    approximate = true;
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
            return dimensions.Count > 0;
        }

        private static AnimatorOptimizerMotionPlan BuildDecisionPlanFromTable(
            IReadOnlyList<NormalizedStateTransition> branches,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            IReadOnlyDictionary<int, AnimatorOptimizerMotionRef> table,
            AnimatorController controller)
        {
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
                (rootStateMachine.entryTransitions == null || rootStateMachine.entryTransitions.Length == 0))
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
                if ((stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
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
            foreach (var transition in stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>())
            {
                if (!IsLocalNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var transition in stateMachine.anyStateTransitions ?? Array.Empty<AnimatorStateTransition>())
            {
                if (!IsLocalNestedTransition(transition, states, stateMachines))
                    return false;
            }
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                foreach (var transition in child.state?.transitions ?? Array.Empty<AnimatorStateTransition>())
                {
                    if (!IsLocalNestedTransition(transition, states, stateMachines))
                        return false;
                }
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                foreach (var transition in stateMachine.GetStateMachineTransitions(child.stateMachine) ??
                         Array.Empty<AnimatorTransition>())
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
                    !(condition.IsFloat && options.AllowFloatConditionApproximation)))
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
                else if (type == AnimatorControllerParameterType.Float && options.AllowFloatConditionApproximation)
                {
                    approximate = true;
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
                if (dimension.Values.Count == 0 || combinationCount > long.MaxValue / dimension.Values.Count)
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
            if (plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion)
                return "constant:" + BuildMotionKey(plan.ConstantMotion);
            if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
            {
                return "bool:" + string.Join(",", plan.BooleanParameters ?? new List<string>()) + ":" +
                       string.Join("|", (plan.TruthTable ?? new Dictionary<int, AnimatorOptimizerMotionRef>())
                           .OrderBy(pair => pair.Key)
                           .Select(pair => pair.Key + "=" + BuildMotionKey(pair.Value)));
            }
            if (plan.Kind == AnimatorOptimizerConversionKind.NumericSelector)
            {
                return "numeric:" + plan.SelectorParameter + ":" + plan.SelectorParameterType + ":" +
                       string.Join("|", (plan.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>())
                           .Select(sample => sample.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                             "=" + BuildMotionKey(sample.Motion)));
            }
            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
            {
                return "finite:" + string.Join("|", plan.DecisionDimensions.Select(dimension =>
                           dimension.Parameter + "=" + string.Join(",", dimension.Values.Select(value =>
                               value.ToString("R", CultureInfo.InvariantCulture))))) + ":" +
                       string.Join("|", plan.DecisionTable.OrderBy(pair => pair.Key)
                           .Select(pair => pair.Key + "=" + BuildMotionKey(pair.Value)));
            }
            if (plan.Kind == AnimatorOptimizerConversionKind.AapStateRegister)
            {
                var dimensions = plan.StateRegisterDecisionDimensions == null
                    ? string.Join(",", plan.StateRegisterBooleanParameters ?? new List<string>())
                    : string.Join("|", plan.StateRegisterDecisionDimensions.Select(dimension =>
                        dimension.Parameter + "=" + string.Join(",", dimension.Values.Select(value =>
                            value.ToString("R", CultureInfo.InvariantCulture)))));
                return "aap-state:" + dimensions + ":" +
                       "entry=" + string.Join(",", (plan.StateRegisterEntryStates ?? new Dictionary<int, int>())
                           .OrderBy(pair => pair.Key)
                           .Select(pair => pair.Key + ">" + pair.Value)) + ":" +
                       string.Join("|", (plan.StateRegisterNextStates ?? new Dictionary<int, Dictionary<int, int>>())
                           .OrderBy(pair => pair.Key)
                           .Select(pair => pair.Key + "=" + string.Join(",", pair.Value.OrderBy(item => item.Key)
                               .Select(item => item.Key + ">" + item.Value))));
            }
            return plan.Kind.ToString();
        }

        private static string BuildMotionKey(AnimatorOptimizerMotionRef motion)
        {
            if (motion == null)
                return "null";
            return AnimatorOptimizerMotionUtility.BuildStructuralKey(motion.Motion) + ":" +
                   motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.Mirror;
        }

        private static bool TryExtractStateIndependentTransitions(
            IReadOnlyList<ChildAnimatorState> childStates,
            out List<AnimatorStateTransition> transitions)
        {
            transitions = null;
            if (childStates.Count == 0 || childStates.Any(child => child.state == null))
                return false;

            var first = childStates[0].state.transitions ?? Array.Empty<AnimatorStateTransition>();
            if (first.Length == 0)
                return false;

            foreach (var child in childStates.Skip(1))
            {
                var current = child.state.transitions ?? Array.Empty<AnimatorStateTransition>();
                if (current.Length != first.Length)
                    return false;
                for (var index = 0; index < first.Length; index++)
                {
                    if (!AreEquivalentTransition(first[index], current[index]))
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
                stateMachine.entryTransitions != null && stateMachine.entryTransitions.Length > 0)
                return false;

            var states = childStates.Select(child => child.state).ToList();
            var stateSet = new HashSet<AnimatorState>(states);
            var transitions = new List<NormalizedStateTransition>();
            foreach (var state in states)
            {
                if (!ValidateState(controller, state, report, allowTransitions: true) ||
                    !IsTimeIndependentMotion(state.motion))
                    return false;

                var sourceTransitions = state.transitions ?? Array.Empty<AnimatorStateTransition>();
                for (var order = 0; order < sourceTransitions.Length; order++)
                {
                    var transition = sourceTransitions[order];
                    if (transition == null || transition.destinationState == null ||
                        !stateSet.Contains(transition.destinationState) ||
                        transition.destinationStateMachine != null || transition.isExit ||
                        transition.canTransitionToSelf || transition.mute || transition.solo ||
                        transition.hasExitTime || !Mathf.Approximately(transition.duration, 0f) ||
                        !Mathf.Approximately(transition.offset, 0f))
                        return false;

                    var sourceConditions = transition.conditions ?? Array.Empty<AnimatorCondition>();
                    if (sourceConditions.Length == 0)
                        return false;
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

            if (transitions.Count == 0)
                return false;
            var parameters = transitions.SelectMany(transition => transition.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (!TryBuildFiniteStateSamples(transitions, parameters, options, report, out var samples))
                return false;

            if (parameters.All(parameter => IsBooleanParameter(controller, parameter)))
            {
                var table = new Dictionary<int, AnimatorOptimizerMotionRef>();
                var entryCount = 1 << parameters.Count;
                if (entryCount > Math.Max(2, options.MaxBooleanTruthTableEntries))
                    return false;
                for (var mask = 0; mask < entryCount; mask++)
                {
                    var values = parameters.ToDictionary(
                        parameter => parameter,
                        parameter => (mask & (1 << parameters.IndexOf(parameter))) != 0 ? 1f : 0f,
                        StringComparer.Ordinal);
                    if (!TryResolveStateIndependentMotion(states, transitions, values, out var motion))
                        return false;
                    table[mask] = motion;
                }

                plan = new AnimatorOptimizerMotionPlan
                {
                    Kind = AnimatorOptimizerConversionKind.BooleanTruthTable,
                    Branches = transitions.Select(transition => new AnimatorOptimizerBranch
                    {
                        Motion = ToMotionRef(transition.Destination),
                        Conditions = transition.Conditions,
                        Order = transition.Order,
                    }).ToList(),
                    BooleanParameters = parameters,
                    TruthTable = table,
                    RequiredFloatParameters = new HashSet<string>(parameters, StringComparer.Ordinal),
                };
                return true;
            }

            if (parameters.Count != 1)
                return false;
            var samplesByValue = new List<AnimatorOptimizerSelectorSample>();
            foreach (var value in samples)
            {
                var values = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [parameters[0]] = value,
                };
                if (!TryResolveStateIndependentMotion(states, transitions, values, out var motion))
                    return false;
                samplesByValue.Add(new AnimatorOptimizerSelectorSample
                {
                    Value = value,
                    Motion = motion,
                });
            }

            var parameterType = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .First(parameter => string.Equals(parameter.name, parameters[0], StringComparison.Ordinal)).type;
            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.NumericSelector,
                Branches = transitions.Select(transition => new AnimatorOptimizerBranch
                {
                    Motion = ToMotionRef(transition.Destination),
                    Conditions = transition.Conditions,
                    Order = transition.Order,
                }).ToList(),
                SelectorParameter = parameters[0],
                SelectorParameterType = parameterType,
                SelectorSamples = samplesByValue,
                RequiredFloatParameters = new HashSet<string>(parameters, StringComparer.Ordinal),
            };
            return true;
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

            var states = childStates.Select(child => child.state).ToList();
            var stateIndices = states
                .Select((state, index) => new { state, index })
                .ToDictionary(item => item.state, item => item.index);
            if (states.Count > Math.Max(1, options.MaxBooleanTruthTableEntries))
                return false;
            var defaultStateIndex = states.IndexOf(stateMachine.defaultState);
            if (defaultStateIndex < 0)
                return false;

            foreach (var state in states)
            {
                if (!ValidateState(controller, state, report, allowTransitions: true) ||
                    !IsTimeIndependentMotion(state.motion))
                    return false;
            }

            var anyTransitions = new List<NormalizedStateTransition>();
            var entryTransitions = new List<NormalizedStateTransition>();
            var sourceTransitions = new Dictionary<AnimatorState, List<NormalizedStateTransition>>();
            foreach (var state in states)
                sourceTransitions[state] = new List<NormalizedStateTransition>();

            var rawEntryTransitions = stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>();
            for (var order = 0; order < rawEntryTransitions.Length; order++)
            {
                var transition = rawEntryTransitions[order];
                if (transition == null || transition.destinationState == null ||
                    !stateIndices.ContainsKey(transition.destinationState) ||
                    transition.destinationStateMachine != null || transition.isExit ||
                    transition.mute || transition.solo)
                    return false;
                var conditions = new List<AnimatorOptimizerCondition>();
                foreach (var sourceCondition in transition.conditions ?? Array.Empty<AnimatorCondition>())
                {
                    if (!TryNormalizeAapCondition(controller, sourceCondition, options, report, out var condition))
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

            var unconditionalEntries = entryTransitions
                .Where(transition => transition.Conditions.Count == 0)
                .OrderBy(transition => transition.Order)
                .ToList();
            if (unconditionalEntries.Count > 0)
            {
                if (unconditionalEntries.Count != 1 || entryTransitions.Any(transition =>
                        transition != unconditionalEntries[0] && transition.Order < unconditionalEntries[0].Order))
                    return false;
                defaultStateIndex = stateIndices[unconditionalEntries[0].Destination];
                entryTransitions.Clear();
            }

            var rawAnyTransitions = GetEffectiveAapTransitions(stateMachine.anyStateTransitions);
            for (var order = 0; order < rawAnyTransitions.Count; order++)
            {
                if (!TryNormalizeAapStateTransition(controller, rawAnyTransitions[order], stateIndices,
                        null, order, options, report, out var normalized))
                    return false;
                anyTransitions.Add(normalized);
            }

            foreach (var state in states)
            {
                var rawTransitions = GetEffectiveAapTransitions(state.transitions);
                for (var order = 0; order < rawTransitions.Count; order++)
                {
                    if (!TryNormalizeAapStateTransition(controller, rawTransitions[order], stateIndices,
                            state, order, options, report, out var normalized))
                        return false;
                    sourceTransitions[state].Add(normalized);
                }
            }

            var allTransitions = entryTransitions
                .Concat(anyTransitions)
                .Concat(sourceTransitions.Values.SelectMany(list => list))
                .ToList();
            var parameters = allTransitions
                .SelectMany(transition => transition.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(parameter => parameter, StringComparer.Ordinal)
                .ToList();
            if (parameters.Count == 0 ||
                !TryBuildAapDecisionDimensions(controller, allTransitions, options, report,
                    out var dimensions, out var entryCount))
                return false;

            var nextStates = new Dictionary<int, Dictionary<int, int>>();
            var entryStates = new Dictionary<int, int>();
            var entryIndices = new int[dimensions.Count];
            for (var combination = 0; combination < (int)entryCount; combination++)
            {
                var values = BuildDecisionDimensionValues(dimensions, entryIndices);
                var transition = entryTransitions.OrderBy(item => item.Order)
                    .FirstOrDefault(item => item.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                entryStates[BuildDecisionDimensionKey(dimensions, entryIndices)] = transition == null
                    ? defaultStateIndex
                    : stateIndices[transition.Destination];
                IncrementDecisionDimensionIndices(dimensions, entryIndices);
            }
            for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
            {
                var table = new Dictionary<int, int>();
                var indices = new int[dimensions.Count];
                for (var combination = 0; combination < (int)entryCount; combination++)
                {
                    var values = BuildDecisionDimensionValues(dimensions, indices);
                    var transition = anyTransitions.OrderBy(item => item.Order)
                        .FirstOrDefault(item => item.Conditions.All(condition =>
                            values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                    if (transition == null)
                    {
                        transition = sourceTransitions[states[stateIndex]].OrderBy(item => item.Order)
                            .FirstOrDefault(item => item.Conditions.All(condition =>
                                values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                    }
                    var key = BuildDecisionDimensionKey(dimensions, indices);
                    table[key] = transition == null
                        ? stateIndex
                        : stateIndices[transition.Destination];
                    IncrementDecisionDimensionIndices(dimensions, indices);
                }
                nextStates[stateIndex] = table;
            }

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.AapStateRegister,
                StateRegisterStates = states,
                StateRegisterBooleanParameters = dimensions.All(dimension => dimension.ParameterType == AnimatorControllerParameterType.Bool)
                    ? dimensions.Select(dimension => dimension.Parameter).ToList()
                    : null,
                StateRegisterDecisionDimensions = dimensions,
                StateRegisterNextStates = nextStates,
                StateRegisterEntryStates = entryTransitions.Count > 0 ? entryStates : null,
                StateRegisterDefaultState = defaultStateIndex,
                RequiredFloatParameters = new HashSet<string>(parameters, StringComparer.Ordinal),
            };
            return true;
        }

        private static bool TryBuildAapDecisionDimensions(
            AnimatorController controller,
            IReadOnlyList<NormalizedStateTransition> transitions,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out List<AnimatorOptimizerDecisionDimension> dimensions,
            out long combinationCount)
        {
            dimensions = new List<AnimatorOptimizerDecisionDimension>();
            combinationCount = 1;
            var parameterNames = transitions.SelectMany(transition => transition.Conditions)
                .Select(condition => condition.Parameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(parameter => parameter, StringComparer.Ordinal)
                .ToList();
            foreach (var parameterName in parameterNames)
            {
                var conditions = transitions.SelectMany(transition => transition.Conditions)
                    .Where(condition => string.Equals(condition.Parameter, parameterName, StringComparison.Ordinal))
                    .ToList();
                if (conditions.Count == 0)
                    return false;
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
                else if (type == AnimatorControllerParameterType.Float && options.AllowFloatConditionApproximation)
                {
                    approximate = true;
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

                if (values.Count == 0)
                    return false;
                if (combinationCount > long.MaxValue / values.Count)
                    return false;
                combinationCount *= values.Count;
                dimensions.Add(new AnimatorOptimizerDecisionDimension
                {
                    Parameter = parameterName,
                    ParameterType = type,
                    Values = values.ToList(),
                    IsApproximate = approximate,
                });
            }

            var allBoolean = dimensions.All(dimension => dimension.ParameterType == AnimatorControllerParameterType.Bool);
            var limit = allBoolean
                ? Math.Max(2, options.MaxBooleanTruthTableEntries)
                : Math.Max(2, options.MaxPriorityCombinations);
            if (combinationCount > limit)
            {
                report.AddReason($"AAP StateRegister 需要 {combinationCount} 个有限输入组合，超过上限 {limit}。");
                return false;
            }
            if (dimensions.Any(dimension => dimension.IsApproximate))
                report.AddWarning("AAP StateRegister 包含 Float 条件；输入阈值使用有限采样，边界附近属于近似语义。");
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

            var staticSignatures = new List<string>();
            var allMotionsHaveStaticObservableOutput = true;
            foreach (var state in states)
            {
                if (!TryBuildStaticObservableMotionSignature(state.motion, out var staticSignature))
                {
                    allMotionsHaveStaticObservableOutput = false;
                    break;
                }
                staticSignatures.Add(staticSignature);
            }
            var signatures = new List<string>();
            foreach (var state in states)
            {
                if (allMotionsHaveStaticObservableOutput)
                {
                    if (!ValidateEffectEquivalentStaticState(state, report))
                        return false;
                }
                else if (!ValidateState(controller, state, report, allowTransitions: true))
                    return false;
                signatures.Add(allMotionsHaveStaticObservableOutput
                    ? staticSignatures[signatures.Count]
                    : BuildMotionKey(ToMotionRef(state)));
            }
            if (signatures.Distinct(StringComparer.Ordinal).Count() != 1)
                return false;

            // Entry/AnyState/State transitions may have timing and interruption details,
            // but they must remain inside this concrete State set. An Exit destination can
            // leave the current machine and therefore has no local observable output that
            // this proof can model.
            foreach (var transition in stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>())
            {
                if (transition == null || transition.destinationState == null ||
                    transition.destinationStateMachine != null ||
                    !states.Contains(transition.destinationState))
                    return false;
            }
            foreach (var transition in stateMachine.anyStateTransitions ?? Array.Empty<AnimatorStateTransition>())
            {
                if (transition != null &&
                    (transition.destinationState == null || transition.isExit ||
                     transition.destinationStateMachine != null ||
                     !states.Contains(transition.destinationState)))
                    return false;
            }
            foreach (var state in states)
            {
                foreach (var transition in state.transitions ?? Array.Empty<AnimatorStateTransition>())
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

        private static bool TryBuildStaticObservableMotionSignature(
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
                   AnimatorOptimizerMotionUtility.TryGetSafeSingletonChild(singletonTree, out var singleton))
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
                floatParts.Add("f:" + new AnimatorOptimizerBindingKey(
                    binding.path, binding.type, binding.propertyName, false) + ":" +
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
                objectParts.Add("o:" + new AnimatorOptimizerBindingKey(
                    binding.path, binding.type, binding.propertyName, true) + ":" +
                    string.Join(";", curve.Select(key => string.Join(",", new[]
                    {
                        key.time.ToString("R", CultureInfo.InvariantCulture),
                        key.value == null ? "0" : key.value.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                    }))));
            }

            floatParts.Sort(StringComparer.Ordinal);
            objectParts.Sort(StringComparer.Ordinal);
            signature = "static:" + string.Join(";", floatParts.Concat(objectParts));
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
            if (!state.writeDefaultValues)
                report.AddReason("源 State 的 Write Defaults 必须为 On，才能证明空 Motion 的可观测输出一致。");
            if (state.iKOnFeet)
                report.AddReason("State 开启了 IK On Feet；即使静态 Motion 相同也可能改变可观测姿态。");
            if (state.mirror || state.speedParameterActive || state.cycleOffsetParameterActive ||
                state.mirrorParameterActive || state.timeParameterActive)
                report.AddReason("State 使用 Mirror 或动态播放参数，不能把静态 Motion 的 State 设置差异折叠。");
            if (!TryBuildStaticObservableMotionSignature(state.motion, out _))
                report.AddReason("State Motion 不是可证明为静态且可观测等价的 Clip。");
            return report.Reasons.Count == 0;
        }

        private static bool TryNormalizeAapStateTransition(
            AnimatorController controller,
            AnimatorStateTransition transition,
            IReadOnlyDictionary<AnimatorState, int> stateIndices,
            AnimatorState source,
            int order,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out NormalizedStateTransition normalized)
        {
            normalized = null;
            if (transition == null || transition.destinationState == null ||
                !stateIndices.ContainsKey(transition.destinationState) ||
                transition.destinationStateMachine != null || transition.isExit ||
                transition.hasExitTime || !Mathf.Approximately(transition.duration, 0f) ||
                !Mathf.Approximately(transition.offset, 0f))
                return false;

            var conditions = transition.conditions ?? Array.Empty<AnimatorCondition>();
            if (conditions.Length == 0)
                return false;
            var normalizedConditions = new List<AnimatorOptimizerCondition>();
            foreach (var condition in conditions)
            {
                if (!TryNormalizeAapCondition(controller, condition, options, report, out var normalizedCondition))
                    return false;
                normalizedConditions.Add(normalizedCondition);
            }
            normalized = new NormalizedStateTransition
            {
                Source = source,
                Destination = transition.destinationState,
                Conditions = normalizedConditions,
                Order = order,
            };
            return true;
        }

        private static List<AnimatorStateTransition> GetEffectiveAapTransitions(
            IEnumerable<AnimatorStateTransition> transitions)
        {
            var active = (transitions ?? Enumerable.Empty<AnimatorStateTransition>())
                .Where(transition => transition != null && !transition.mute)
                .ToList();
            if (active.Any(transition => transition.solo))
                active = active.Where(transition => transition.solo).ToList();
            return active;
        }

        private static bool TryNormalizeAapCondition(
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
                    return false;
                return TryNormalizeConditionForType(source, globalType, options, report, out normalized);
            }

            if (parameter.type == AnimatorControllerParameterType.Bool)
                return TryNormalizeCondition(controller, source, options, report, out normalized) && normalized.IsBoolean;

            // A regular Layer conversion may already have upgraded the original Bool
            // parameter and rewritten If/IfNot to Greater/Less 0.5. Accept exactly that
            // canonical form so the build pipeline can process regular and AAP layers in
            // two passes without losing the Boolean state machine plan.
            var isTrue = source.mode == AnimatorConditionMode.Greater && Mathf.Approximately(source.threshold, 0.5f) ||
                         source.mode == AnimatorConditionMode.Equals && Mathf.Approximately(source.threshold, 1f) ||
                         source.mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(source.threshold, 0f);
            var isFalse = source.mode == AnimatorConditionMode.Less && Mathf.Approximately(source.threshold, 0.5f) ||
                          source.mode == AnimatorConditionMode.Equals && Mathf.Approximately(source.threshold, 0f) ||
                          source.mode == AnimatorConditionMode.NotEqual && Mathf.Approximately(source.threshold, 1f);
            if (parameter.type == AnimatorControllerParameterType.Float && (isTrue || isFalse))
            {
                normalized = new AnimatorOptimizerCondition
                {
                    Parameter = source.parameter,
                    ParameterType = AnimatorControllerParameterType.Bool,
                    Mode = isTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    Threshold = 0f,
                };
                return true;
            }
            return TryNormalizeCondition(controller, source, options, report, out normalized);
        }

        private static bool TryBuildFiniteStateSamples(
            IReadOnlyList<NormalizedStateTransition> transitions,
            IReadOnlyList<string> parameters,
            AnimatorOptimizerOptions options,
            AnimatorOptimizerLayerReport report,
            out List<float> samples)
        {
            samples = new List<float>();
            if (parameters.Count == 0)
                return false;
            if (parameters.Count > 1)
            {
                var booleanConditions = transitions.SelectMany(transition => transition.Conditions)
                    .All(condition => condition.IsBoolean);
                var booleanEntryCount = 1L << parameters.Count;
                if (!booleanConditions || booleanEntryCount > Math.Max(2, options.MaxBooleanTruthTableEntries))
                    return false;
                samples.AddRange(Enumerable.Range(0, (int)booleanEntryCount).Select(value => (float)value));
                return true;
            }

            var parameter = parameters[0];
            var conditions = transitions.SelectMany(transition => transition.Conditions)
                .Where(condition => string.Equals(condition.Parameter, parameter, StringComparison.Ordinal))
                .ToList();
            if (conditions.Count == 0)
                return false;

            if (conditions.All(condition => condition.IsBoolean))
            {
                samples.Add(0f);
                samples.Add(1f);
                return true;
            }

            if (conditions.Any(condition => condition.IsBoolean))
                return false;
            var prototype = conditions[0];
            if (conditions.Any(condition => condition.ParameterType != prototype.ParameterType))
                return false;
            if (!prototype.IsInteger && !prototype.IsFloat)
                return false;

            var values = new SortedSet<float>();
            var epsilon = Mathf.Max(0.000001f, options.FloatConditionEpsilon);
            foreach (var condition in conditions)
            {
                if (prototype.IsInteger)
                {
                    var threshold = Mathf.RoundToInt(condition.Threshold);
                    values.Add(threshold - 1);
                    values.Add(threshold);
                    values.Add(threshold + 1);
                }
                else
                {
                    if (!options.AllowFloatConditionApproximation)
                        return false;
                    values.Add(condition.Threshold - epsilon);
                    values.Add(condition.Threshold);
                    values.Add(condition.Threshold + epsilon);
                }
            }
            if (values.Count > Math.Max(3, options.MaxNumericSelectorSamples))
            {
                report.AddReason($"状态依赖图需要 {values.Count} 个有限参数采样点，超过上限 {options.MaxNumericSelectorSamples}。");
                return false;
            }
            samples.AddRange(values);
            return true;
        }

        private static bool TryResolveStateIndependentMotion(
            IReadOnlyList<AnimatorState> states,
            IReadOnlyList<NormalizedStateTransition> transitions,
            IReadOnlyDictionary<string, float> values,
            out AnimatorOptimizerMotionRef motion)
        {
            motion = null;
            foreach (var state in states)
            {
                var output = ToMotionRef(state);
                var transition = transitions
                    .Where(candidate => candidate.Source == state)
                    .OrderBy(candidate => candidate.Order)
                    .FirstOrDefault(candidate => candidate.Conditions.All(condition =>
                        values.TryGetValue(condition.Parameter, out var value) && condition.Matches(value)));
                if (transition != null)
                    output = ToMotionRef(transition.Destination);

                if (motion == null)
                    motion = output;
                else if (!string.Equals(BuildMotionKey(motion), BuildMotionKey(output), StringComparison.Ordinal))
                    return false;
            }
            return motion != null;
        }

        private static bool IsBooleanParameter(AnimatorController controller, string parameterName)
        {
            var parameter = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .FirstOrDefault(candidate => string.Equals(candidate.name, parameterName, StringComparison.Ordinal));
            return parameter != null && parameter.type == AnimatorControllerParameterType.Bool;
        }

        private static bool TryResolveNestedStaticDefault(
            AnimatorStateMachine stateMachine,
            out AnimatorState state)
        {
            state = null;
            if (stateMachine == null ||
                (stateMachine.behaviours != null && stateMachine.behaviours.Length > 0) ||
                (stateMachine.entryTransitions != null && stateMachine.entryTransitions.Length > 0) ||
                (stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Length > 0))
                return false;

            if (stateMachine.defaultState != null)
            {
                if ((stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>()).Length > 0)
                    return false;
                state = stateMachine.defaultState;
                return state.transitions == null || state.transitions.Length == 0;
            }

            var childMachines = stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>();
            if ((stateMachine.states ?? Array.Empty<ChildAnimatorState>()).Length != 0 || childMachines.Length != 1)
                return false;
            if (stateMachine.GetStateMachineTransitions(childMachines[0].stateMachine).Length != 0)
                return false;
            return TryResolveNestedStaticDefault(childMachines[0].stateMachine, out state);
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
                var count = 1 << parameterNames.Count;
                for (var mask = 0; mask < count; mask++)
                {
                    var values = parameterNames.ToDictionary(
                        name => name,
                        name => (mask & (1 << parameterNames.IndexOf(name))) != 0 ? 1f : 0f,
                        StringComparer.Ordinal);
                    if (ConditionsMatch(leftConditions, values) && ConditionsMatch(rightConditions, values))
                        return true;
                }
                return false;
            }

            if (parameterNames.Count != 1)
                return true;

            var parameter = parameters[0];
            if (parameter.type != AnimatorControllerParameterType.Int &&
                parameter.type != AnimatorControllerParameterType.Float)
                return true;

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
                    AnimatorConditionMode.Equals => Mathf.Approximately(value, condition.threshold),
                    AnimatorConditionMode.NotEqual => !Mathf.Approximately(value, condition.threshold),
                    AnimatorConditionMode.Greater => value > condition.threshold,
                    AnimatorConditionMode.Less => value < condition.threshold,
                    _ => false,
                };
                if (!matches)
                    return false;
            }
            return true;
        }

        private static bool AreEquivalentTransition(AnimatorStateTransition left, AnimatorStateTransition right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.destinationState == null || right.destinationState == null)
                return left.destinationState == right.destinationState &&
                       left.destinationStateMachine == right.destinationStateMachine &&
                       AreEquivalentConditions(left.conditions, right.conditions);
            return left.destinationState.name == right.destinationState.name &&
                   left.destinationState.motion == right.destinationState.motion &&
                   left.hasExitTime == right.hasExitTime &&
                   Mathf.Approximately(left.exitTime, right.exitTime) &&
                   Mathf.Approximately(left.duration, right.duration) &&
                   Mathf.Approximately(left.offset, right.offset) &&
                   left.mute == right.mute &&
                   left.solo == right.solo &&
                   left.canTransitionToSelf == right.canTransitionToSelf &&
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
                    !Mathf.Approximately(left[index].threshold, right[index].threshold))
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
                if (transition == null || transition.destinationState == null ||
                    !stateSet.Contains(transition.destinationState) || transition.destinationStateMachine != null ||
                    transition.isExit || transition.canTransitionToSelf)
                {
                    report.AddReason("Transition 必须指向本层的另一个 State，不能指向 Exit/StateMachine/自身。");
                    return false;
                }

                if (transition.mute || transition.solo || transition.hasExitTime ||
                    !Mathf.Approximately(transition.duration, 0f) || !Mathf.Approximately(transition.offset, 0f))
                {
                    report.AddReason("Transition 含有 Mute/Solo、Exit Time、Duration 或 Offset，存在时间/优先级语义，暂不转换。");
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

            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                    source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual)
                {
                    report.AddReason("Bool 条件只支持 If、IfNot、Equals、NotEqual。");
                    return false;
                }
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    !Mathf.Approximately(source.threshold, 0f) && !Mathf.Approximately(source.threshold, 1f))
                {
                    report.AddReason("Bool Equals/NotEqual 阈值必须为 0 或 1。");
                    return false;
                }
                if (!options.UpgradeBoolParametersToFloat)
                {
                    report.AddReason($"条件使用 Bool 参数 '{source.parameter}'；需要勾选 Bool → Float 才能作为 BlendTree 参数。");
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
                if (!Mathf.Approximately(source.threshold, Mathf.Round(source.threshold)))
                {
                    report.AddReason("Int 条件阈值必须是整数。");
                    return false;
                }
                if (!options.UpgradeIntParametersToFloat)
                {
                    report.AddReason($"条件使用 Int 参数 '{source.parameter}'；需要勾选 Int → Float 才能作为 BlendTree 参数。");
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
            if (!options.AllowFloatConditionApproximation)
            {
                report.AddReason($"Float 参数 '{source.parameter}' 的 Transition 是阶跃条件；请勾选 Float 条件近似后再转换。");
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

            var samples = BuildSelectorSamples(branches, parameter, options, report);
            if (samples == null || samples.Count == 0)
                return false;

            plan = new AnimatorOptimizerMotionPlan
            {
                Kind = AnimatorOptimizerConversionKind.NumericSelector,
                Branches = branches.ToList(),
                SelectorParameter = parameter.Parameter,
                SelectorParameterType = parameter.ParameterType,
                SelectorSamples = samples,
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
            AnimatorOptimizerLayerReport report)
        {
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
                var epsilon = Mathf.Max(0.000001f, options.FloatConditionEpsilon);
                foreach (var threshold in thresholds)
                {
                    values.Add(threshold - epsilon);
                    values.Add(threshold);
                    values.Add(threshold + epsilon);
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
            if (state.motion == null)
                report.AddReason("State 没有 Motion。");
            if (state.motion != null && ContainsPotentialProxyMotion(state.motion, new HashSet<int>()))
                report.AddReason("State Motion 包含 VRChat/VRCFury proxy Clip，必须保留平台特殊 Motion。");
            if (!allowTransitions && state.transitions != null && state.transitions.Length > 0)
                report.AddReason("State 包含 Transition，不能按 Constant Motion 处理。");
            if (state.behaviours != null && state.behaviours.Length > 0)
            {
                report.BindingsAreComplete = false;
                report.AddReason("State 包含 StateMachineBehaviour，BlendTree 不会执行这些回调。");
            }
            if (!state.writeDefaultValues)
                report.AddReason("源 State 的 Write Defaults 必须为 On，才能放进生成的 WD On Direct BlendTree。");
            if (state.iKOnFeet)
                report.AddReason("State 开启了 IK On Feet；ChildMotion 无法保留 State 级别的 IK 设置。");
            if (state.speedParameterActive || state.cycleOffsetParameterActive || state.mirrorParameterActive ||
                state.timeParameterActive)
            {
                report.AddReason("State 使用动态 Speed/Cycle Offset/Mirror/Time 参数，暂不能等价降级为 ChildMotion。");
            }
            if (state.motion != null && state.motion is not AnimationClip && state.motion is not BlendTree)
                report.AddReason("State Motion 不是 AnimationClip 或 BlendTree。");
            return report.Reasons.Count == 0;
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
            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                if (source.mode != AnimatorConditionMode.If && source.mode != AnimatorConditionMode.IfNot &&
                    source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual)
                    return false;
                if ((source.mode == AnimatorConditionMode.Equals || source.mode == AnimatorConditionMode.NotEqual) &&
                    !Mathf.Approximately(source.threshold, 0f) && !Mathf.Approximately(source.threshold, 1f))
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
                    !Mathf.Approximately(source.threshold, Mathf.Round(source.threshold)))
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
            if (!options.AllowFloatConditionApproximation ||
                source.mode != AnimatorConditionMode.Equals && source.mode != AnimatorConditionMode.NotEqual &&
                source.mode != AnimatorConditionMode.Greater && source.mode != AnimatorConditionMode.Less)
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

        private static AnimatorOptimizerMotionRef ToMotionRef(AnimatorState state)
        {
            return new AnimatorOptimizerMotionRef
            {
                Motion = state?.motion,
                Speed = state == null ? 1f : state.speed,
                CycleOffset = state == null ? 0f : state.cycleOffset,
                Mirror = state != null && state.mirror,
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
                    return "已按数值条件生成 1D BlendTree；Int 取值为整数时精确，Float 条件使用阈值附近近似。";
                case AnimatorOptimizerConversionKind.FiniteParameterTable:
                    return $"已枚举 {plan.DecisionTable.Count} 个 Bool/Int 参数组合，生成多维嵌套 1D BlendTree。";
                case AnimatorOptimizerConversionKind.AapStateRegister:
                    return plan.StateRegisterEntryStates == null
                        ? "已生成 AAP StateRegister 更新树和 State 输出 1D BlendTree；支持有限 Bool/Int 以及可选 Float 条件。"
                        : "已生成带首帧 Entry 初始化反馈的 AAP StateRegister，并用 State 输出 1D BlendTree 保留状态历史。";
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
                report.BindingsAreComplete = false;
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
                try
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Animator))
                        {
                            report.BindingsAreComplete = false;
                            report.AddReason("动画包含 AAP/Animator 参数曲线；参数保存顺序暂不自动重写。");
                        }
                        report.Bindings.Add(new AnimatorOptimizerBindingKey(
                            binding.path, binding.type, binding.propertyName, false));
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        if (binding.type == typeof(Animator))
                        {
                            report.BindingsAreComplete = false;
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
        public static bool IsPotentialProxyClip(AnimationClip clip)
        {
            return clip != null && clip.name != null &&
                   clip.name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase);
        }

        public static AnimatorOptimizerMotionInfo Analyze(Motion motion)
        {
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
            if (motion == null)
                return false;
            if (motion is AnimationClip clip)
                return IsTimeIndependentClip(clip);
            if (motion is BlendTree tree)
            {
                return (tree.children ?? Array.Empty<ChildMotion>()).All(child =>
                    child.motion != null && IsTimeIndependent(child.motion));
            }
            return false;
        }

        public static bool IsStaticClip(AnimationClip clip)
        {
            if (clip == null || IsPotentialProxyClip(clip) || (clip.events != null && clip.events.Length > 0))
                return false;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                return false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (!IsConstantCurve(curve))
                    return false;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (curve == null || curve.Length == 0)
                    continue;
                var firstValue = curve[0].value;
                if (curve.Any(key => key.value != firstValue))
                    return false;
            }
            return true;
        }

        public static bool TryNormalizeSingletonTree(Motion motion, out Motion normalized)
        {
            normalized = motion;
            var changed = false;
            while (normalized is BlendTree tree && TryGetSafeSingletonChild(tree, out var child))
            {
                normalized = child.motion;
                changed = true;
            }
            return changed;
        }

        public static bool TryGetSafeSingletonChild(BlendTree tree, out ChildMotion child)
        {
            child = default;
            if (tree == null || tree.children == null || tree.children.Length != 1)
                return false;
            child = tree.children[0];
            if (child.motion == null || !Mathf.Approximately(child.timeScale, 1f) ||
                !Mathf.Approximately(child.cycleOffset, 0f) || child.mirror)
                return false;
            // An explicit Direct weight is variable and cannot be discarded.
            if (tree.blendType == BlendTreeType.Direct &&
                !string.IsNullOrWhiteSpace(child.directBlendParameter))
                return false;
            return true;
        }

        public static int CountNodes(Motion motion)
        {
            if (motion == null)
                return 0;
            if (motion is BlendTree tree)
                return 1 + (tree.children ?? Array.Empty<ChildMotion>()).Sum(child => CountNodes(child.motion));
            return 1;
        }

        private static bool IsTimeIndependentClip(AnimationClip clip)
        {
            if (clip == null || IsPotentialProxyClip(clip) || (clip.events != null && clip.events.Length > 0))
                return false;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                return false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (!IsConstantCurve(curve))
                    return false;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (curve != null && curve.Length > 1)
                {
                    var first = curve[0].value;
                    if (curve.Any(key => key.value != first))
                        return false;
                }
            }
            return true;
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
            if (!string.IsNullOrWhiteSpace(tree.blendParameter))
            {
                info.HasParameterDrivenBlend = true;
                info.BlendParameters.Add(tree.blendParameter);
            }
            if (!string.IsNullOrWhiteSpace(tree.blendParameterY))
            {
                info.HasParameterDrivenBlend = true;
                info.BlendParameters.Add(tree.blendParameterY);
            }

            var children = tree.children ?? Array.Empty<ChildMotion>();
            info.IsTimeIndependent = children.Length > 0;
            info.IsStatic = children.Length > 0;
            foreach (var child in children)
            {
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
                if (index > 0)
                {
                    var midpoint = (keys[index - 1].time + key.time) * 0.5f;
                    if (!Mathf.Approximately(curve.Evaluate(midpoint), value))
                        return false;
                }
            }
            return true;
        }

        private static string BuildStructuralKey(Motion motion, HashSet<int> active)
        {
            if (motion == null)
                return "null";
            var instanceId = motion.GetInstanceID();
            if (!active.Add(instanceId))
                return "cycle:" + instanceId.ToString(CultureInfo.InvariantCulture);
            if (motion is AnimationClip clip)
            {
                active.Remove(instanceId);
                return BuildClipStructuralKey(clip);
            }
            if (!(motion is BlendTree tree))
            {
                active.Remove(instanceId);
                return "unsupported:" + motion.GetType().FullName;
            }
            if (TryGetSafeSingletonChild(tree, out var singleton))
            {
                var singletonKey = BuildStructuralKey(singleton.motion, active);
                active.Remove(instanceId);
                return singletonKey;
            }

            var key = string.Join("|", new[]
            {
                "tree",
                tree.blendType.ToString(),
                tree.blendParameter ?? string.Empty,
                tree.blendParameterY ?? string.Empty,
                tree.minThreshold.ToString("R", CultureInfo.InvariantCulture),
                tree.maxThreshold.ToString("R", CultureInfo.InvariantCulture),
                tree.useAutomaticThresholds.ToString(),
                GetNormalizedBlendValuesKey(tree),
                string.Join(";", (tree.children ?? Array.Empty<ChildMotion>()).Select(child =>
                    string.Join(",", new[]
                    {
                        BuildStructuralKey(child.motion, active),
                        child.threshold.ToString("R", CultureInfo.InvariantCulture),
                        child.timeScale.ToString("R", CultureInfo.InvariantCulture),
                        child.cycleOffset.ToString("R", CultureInfo.InvariantCulture),
                        child.mirror.ToString(),
                        child.directBlendParameter ?? string.Empty,
                        child.position.x.ToString("R", CultureInfo.InvariantCulture),
                        child.position.y.ToString("R", CultureInfo.InvariantCulture),
                    })))
            });
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
                return "normalized:" + (property == null || property.boolValue);
            }
            catch
            {
                return "normalized:unknown";
            }
        }

        private static string BuildClipStructuralKey(AnimationClip clip)
        {
            if (clip == null)
                return "clip:null";

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var floatCurves = AnimationUtility.GetCurveBindings(clip)
                .OrderBy(binding => binding.path, StringComparer.Ordinal)
                .ThenBy(binding => binding.type == null ? string.Empty : binding.type.FullName, StringComparer.Ordinal)
                .ThenBy(binding => binding.propertyName, StringComparer.Ordinal)
                .Select(binding => "f:" + BindingKeyText(binding) + ":" +
                    CurveKeyText(AnimationUtility.GetEditorCurve(clip, binding)));
            var objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .OrderBy(binding => binding.path, StringComparer.Ordinal)
                .ThenBy(binding => binding.type == null ? string.Empty : binding.type.FullName, StringComparer.Ordinal)
                .ThenBy(binding => binding.propertyName, StringComparer.Ordinal)
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

            return "clip|" + clip.legacy + "|" + clip.frameRate.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + clip.wrapMode + "|" + settings.loopTime + "|" + settings.loopBlend +
                   "|" + settings.hasAdditiveReferencePose +
                   "|" + settings.additiveReferencePoseTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + (settings.additiveReferencePoseClip == null
                       ? "0"
                       : settings.additiveReferencePoseClip.GetInstanceID().ToString(CultureInfo.InvariantCulture)) +
                   "|" + settings.startTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + settings.stopTime.ToString("R", CultureInfo.InvariantCulture) +
                   "|" + string.Join(";", floatCurves.Concat(objectCurves)) +
                   "|events:" + string.Join(";", events);
        }

        private static string BindingKeyText(EditorCurveBinding binding)
        {
            return string.Join("/", new[]
            {
                binding.path ?? string.Empty,
                binding.type == null ? string.Empty : binding.type.FullName ?? binding.type.Name,
                binding.propertyName ?? string.Empty,
                binding.isPPtrCurve.ToString(),
            });
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
