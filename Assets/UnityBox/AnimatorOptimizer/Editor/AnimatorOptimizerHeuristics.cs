using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    // Shared, asset-free representation used by both the physical and NDMF adapters.
    // Destination -1 denotes a root Exit, which re-evaluates the ordered root Entry list.
    public sealed class AnimatorOptimizerFiniteGraph
    {
        public sealed class Edge
        {
            public int Destination;
            public bool CanTransitionToSelf = true;
            public AnimatorCondition[] Conditions = Array.Empty<AnimatorCondition>();
        }

        public sealed class Axis
        {
            public string Parameter;
            public AnimatorControllerParameterType Type;
            public List<float> Values;
            public bool IsApproximate;
        }

        public sealed class Result
        {
            public List<Axis> Axes = new List<Axis>();
            public Dictionary<int, int> Outputs = new Dictionary<int, int>();
            public List<string> Assumptions = new List<string>();
            public HashSet<string> BinaryParameters = new HashSet<string>(StringComparer.Ordinal);
            public int PrunedConditions;
            public int PrunedEdges;
            public int RemovedAxes;
        }

        public int DefaultState;
        public List<Edge> Entries = new List<Edge>();
        public List<Edge> AnyState = new List<Edge>();
        public List<List<Edge>> States = new List<List<Edge>>();

        public bool TryCompile(IReadOnlyList<string> outputKeys,
            IReadOnlyDictionary<string, AnimatorControllerParameter> declarations,
            AnimatorOptimizerOptions options, out Result result)
        {
            result = null;
            if (outputKeys == null || States == null || Entries == null || AnyState == null ||
                States.Any(state => state == null) || States.Count == 0 || States.Count > 256 ||
                outputKeys.Count != States.Count || outputKeys.Any(key => key == null) ||
                DefaultState < 0 || DefaultState >= States.Count || declarations == null || options == null)
                return false;
            options = options.Clone();
            var edges = Entries.Concat(AnyState).Concat(States.SelectMany(state => state)).ToList();
            if (edges.Count > 8192 || edges.Any(edge => edge == null || edge.Conditions == null ||
                edge.Conditions.Length > 64 || edge.Destination < -1 || edge.Destination >= States.Count) ||
                Entries.Concat(AnyState).Any(edge => edge.Destination < 0) ||
                edges.Sum(edge => edge.Conditions.Length) > 65536) return false;

            AnimatorControllerParameter Resolve(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                if (declarations.TryGetValue(name, out var value)) return value;
                return AnimatorOptimizerAnalyzer.TryGetVrcGlobalParameterType(name, out var type)
                    ? new AnimatorControllerParameter { name = name, type = type } : null;
            }
            AnimatorControllerParameterType? TypeOf(string name) => Resolve(name)?.type;

            // Validate before simplifying: Trigger consumption is never a lossy preset.
            if (edges.SelectMany(edge => edge.Conditions).Any(condition => !IsUsable(condition, Resolve(condition.parameter))))
                return false;
            var built = new Result();
            var implicationBudget = 262144;
            bool Dominates(Edge previous, Edge current, AnimatorCondition[] conditions, bool anyState)
            {
                if (anyState && !previous.CanTransitionToSelf &&
                    (current.CanTransitionToSelf || previous.Destination != current.Destination)) return false;
                foreach (var required in previous.Conditions)
                {
                    var implied = false;
                    foreach (var actual in conditions)
                    {
                        if (--implicationBudget < 0) return false;
                        if (!Implies(actual, required, TypeOf(actual.parameter))) continue;
                        implied = true;
                        break;
                    }
                    if (!implied) return false;
                }
                return true;
            }
            List<Edge> Prepare(IEnumerable<Edge> source, bool anyState)
            {
                var list = new List<Edge>();
                foreach (var edge in source)
                {
                    if (!AnimatorOptimizerSemanticUtility.ConditionsMayBeReachable(edge.Conditions, TypeOf))
                    {
                        built.PrunedEdges++;
                        continue;
                    }
                    var conditions = SimplifyConjunction(edge.Conditions, TypeOf);
                    built.PrunedConditions += edge.Conditions.Length - conditions.Length;
                    // A later self-enabled AnyState edge can still suppress local
                    // transitions when the earlier self-disabled edge is skipped.
                    // Exhausting the pruning budget only skips this simplification.
                    if (implicationBudget > 0 && list.Any(previous => Dominates(previous, edge, conditions, anyState)))
                    {
                        built.PrunedEdges++;
                        continue;
                    }
                    list.Add(new Edge { Destination = edge.Destination,
                        CanTransitionToSelf = edge.CanTransitionToSelf, Conditions = conditions });
                }
                return list;
            }
            var entries = Prepare(Entries, false);
            var any = Prepare(AnyState, true);
            var stateEdges = States.Select(state => Prepare(state, false)).ToList();
            var allConditions = entries.Concat(any).Concat(stateEdges.SelectMany(state => state))
                .SelectMany(edge => edge.Conditions).ToList();
            long combinations = 1;
            foreach (var group in allConditions.GroupBy(condition => condition.parameter, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var parameter = Resolve(group.Key);
                var conditions = group.ToList();
                var type = parameter.type;
                var values = new SortedSet<float>();
                var approximate = false;
                var binary = type == AnimatorControllerParameterType.Float &&
                    options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles) &&
                    !AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(group.Key) &&
                    (parameter.defaultFloat == 0f || parameter.defaultFloat == 1f) &&
                    conditions.All(condition => condition.threshold >= 0f && condition.threshold <= 1f &&
                        Matches(condition, 0f) != Matches(condition, 1f));
                if (type == AnimatorControllerParameterType.Bool || binary)
                {
                    values.Add(0f);
                    values.Add(1f);
                    if (binary)
                    {
                        built.BinaryParameters.Add(group.Key);
                        built.Assumptions.Add($"Float '{group.Key}' 按 0/1 开关处理；连续值、OSC 中间值和阈值处历史不在此假设内。");
                    }
                    type = AnimatorControllerParameterType.Bool;
                }
                else if (type == AnimatorControllerParameterType.Int)
                {
                    foreach (var condition in conditions)
                    {
                        values.Add(condition.threshold - 1f);
                        values.Add(condition.threshold);
                        values.Add(condition.threshold + 1f);
                    }
                }
                else
                {
                    foreach (var condition in conditions)
                    {
                        if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(condition.threshold,
                            options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var fallback))
                            return false;
                        approximate |= fallback;
                    }
                }
                if (!AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(values.ToArray()) ||
                    values.Count > options.MaxNumericSelectorSamples || combinations > 4096 / values.Count)
                    return false;
                combinations *= values.Count;
                built.Axes.Add(new Axis { Parameter = group.Key, Type = type, Values = values.ToList(), IsApproximate = approximate });
                if (approximate) built.Assumptions.Add($"'{group.Key}' 的不支持阈值采用显式允许的邻域插值近似。");
            }
            var budget = built.Axes.All(axis => axis.Type == AnimatorControllerParameterType.Bool)
                ? options.MaxBooleanTruthTableEntries : options.MaxPriorityCombinations;
            if (combinations > budget || combinations > 65536L / States.Count ||
                combinations * (allConditions.Count + edges.Count + States.Count) > 4194304L) return false;

            var valuesByParameter = new Dictionary<string, float>(StringComparer.Ordinal);
            bool Active(Edge edge) => edge.Conditions.All(condition =>
                valuesByParameter.TryGetValue(condition.parameter, out var value) && Matches(condition, value));
            var usedSettling = false;
            var usedComplement = false;
            var complement = FindComplementaryToggle(entries, any, stateEdges, options);
            for (var key = 0; key < (int)combinations; key++)
            {
                var digits = key;
                foreach (var axis in built.Axes)
                {
                    valuesByParameter[axis.Parameter] = axis.Values[digits % axis.Values.Count];
                    digits /= axis.Values.Count;
                }
                var entry = entries.FirstOrDefault(Active)?.Destination ?? DefaultState;
                var activeAny = any.Where(Active).ToList();
                var next = new int[States.Count];
                for (var state = 0; state < next.Length; state++)
                {
                    var edge = activeAny.FirstOrDefault(candidate =>
                        candidate.Destination != state || candidate.CanTransitionToSelf) ??
                        stateEdges[state].FirstOrDefault(Active);
                    next[state] = edge == null ? state : edge.Destination < 0 ? entry : edge.Destination;
                }
                var representative = next[0];
                if (!next.All(state => outputKeys[state] == outputKeys[representative]))
                {
                    if (options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.StableInputConvergence) &&
                        TryResolveSettledOutput(next, outputKeys, out representative)) usedSettling = true;
                    else if (complement != null)
                    {
                        representative = Active(complement) ? complement.Destination : DefaultState;
                        usedComplement = true;
                    }
                    else return false;
                }
                if (entries.Any(edge => edge.Conditions.Length != 0 || edge.Destination != DefaultState) &&
                    outputKeys[entry] != outputKeys[representative])
                {
                    // A routed Entry must not expose a different initial output.
                    // Skipping that initial stage is an explicit settling assumption.
                    if (!options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.StableInputConvergence)) return false;
                    usedSettling = true;
                }
                built.Outputs.Add(key, representative);
            }
            if (usedSettling) built.Assumptions.Add("假定输入在状态收敛期间保持不变，直接输出最终结果；中间状态的可见帧及响应延迟会被消除。");
            if (usedComplement) built.Assumptions.Add("简单双态开关以默认态的出发条件为准，返回条件按补集补齐；空档、重叠和等值处的历史行为会改变。");
            RemoveUnusedAxes(built, outputKeys);
            result = built;
            return true;
        }

        private Edge FindComplementaryToggle(List<Edge> entries, List<Edge> any,
            List<List<Edge>> states, AnimatorOptimizerOptions options)
        {
            if (!options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.ComplementaryToggle) ||
                states.Count != 2 || any.Count != 0 ||
                entries.Any(edge => edge.Conditions.Length != 0 || edge.Destination != DefaultState) ||
                states.Any(edges => edges.Count != 1 || edges[0].Conditions.Length != 1)) return null;
            var primary = states[DefaultState][0];
            var returning = states[1 - DefaultState][0];
            if (primary.Destination != 1 - DefaultState ||
                returning.Destination != DefaultState && returning.Destination != -1) return null;
            var a = primary.Conditions[0];
            var b = returning.Conditions[0];
            if (a.parameter != b.parameter) return null;
            var opposite = a.mode == AnimatorConditionMode.Greater && b.mode == AnimatorConditionMode.Less ||
                a.mode == AnimatorConditionMode.Less && b.mode == AnimatorConditionMode.Greater ||
                a.mode == AnimatorConditionMode.Equals && b.mode == AnimatorConditionMode.NotEqual ||
                a.mode == AnimatorConditionMode.NotEqual && b.mode == AnimatorConditionMode.Equals ||
                a.mode == AnimatorConditionMode.If && b.mode == AnimatorConditionMode.IfNot ||
                a.mode == AnimatorConditionMode.IfNot && b.mode == AnimatorConditionMode.If;
            return opposite ? primary : null;
        }

        private static bool TryResolveSettledOutput(int[] next, IReadOnlyList<string> outputKeys, out int representative)
        {
            representative = -1;
            var resolved = Enumerable.Repeat(-1, next.Length).ToArray();
            for (var start = 0; start < next.Length; start++)
            {
                if (resolved[start] >= 0) continue;
                var path = new List<int>();
                var positions = new Dictionary<int, int>();
                var current = start;
                while (resolved[current] < 0 && !positions.ContainsKey(current))
                {
                    positions.Add(current, path.Count);
                    path.Add(current);
                    current = next[current];
                }
                var target = resolved[current];
                if (target < 0)
                {
                    target = current;
                    // Same-output cycles may be collapsed; oscillating outputs may not.
                    if (path.Skip(positions[current]).Any(state => outputKeys[state] != outputKeys[target])) return false;
                }
                foreach (var state in path) resolved[state] = target;
            }
            representative = resolved[0];
            var key = outputKeys[representative];
            return resolved.All(state => outputKeys[state] == key);
        }

        private static void RemoveUnusedAxes(Result result, IReadOnlyList<string> outputKeys)
        {
            var stride = 1;
            for (var axis = 0; axis < result.Axes.Count;)
            {
                var count = result.Axes[axis].Values.Count;
                var block = stride * count;
                var independent = result.Outputs.All(pair => outputKeys[pair.Value] ==
                    outputKeys[result.Outputs[pair.Key - pair.Key / stride % count * stride]]);
                if (!independent) { stride = block; axis++; continue; }
                result.Outputs = result.Outputs.Where(pair => pair.Key / stride % count == 0)
                    .ToDictionary(pair => pair.Key % stride + pair.Key / block * stride, pair => pair.Value);
                result.Axes.RemoveAt(axis);
                result.RemovedAxes++;
            }
        }

        internal static bool IsUsable(AnimatorCondition condition, AnimatorControllerParameter parameter)
        {
            return parameter != null &&
                (parameter.type != AnimatorControllerParameterType.Float || AnimatorOptimizerAnalyzer.IsFinite(parameter.defaultFloat)) &&
                IsUsable(condition, parameter.type);
        }

        internal static bool IsUsable(AnimatorCondition condition, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrWhiteSpace(condition.parameter)) return false;
            if (type == AnimatorControllerParameterType.Bool)
                return condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot ||
                    (condition.mode == AnimatorConditionMode.Equals || condition.mode == AnimatorConditionMode.NotEqual) &&
                    (condition.threshold == 0f || condition.threshold == 1f);
            if (type != AnimatorControllerParameterType.Int && type != AnimatorControllerParameterType.Float)
                return false;
            if (!AnimatorOptimizerAnalyzer.IsFinite(condition.threshold) ||
                type == AnimatorControllerParameterType.Int &&
                    (condition.threshold != Mathf.Round(condition.threshold) || Math.Abs((double)condition.threshold) > 16777214d)) return false;
            return condition.mode == AnimatorConditionMode.Greater || condition.mode == AnimatorConditionMode.Less ||
                condition.mode == AnimatorConditionMode.Equals || condition.mode == AnimatorConditionMode.NotEqual;
        }

        public static bool Matches(AnimatorCondition condition, float value) => condition.mode switch
        {
            AnimatorConditionMode.If => value >= 0.5f,
            AnimatorConditionMode.IfNot => value < 0.5f,
            AnimatorConditionMode.Equals => value == condition.threshold,
            AnimatorConditionMode.NotEqual => value != condition.threshold,
            AnimatorConditionMode.Greater => value > condition.threshold,
            AnimatorConditionMode.Less => value < condition.threshold,
            _ => false,
        };

        public static AnimatorCondition[] SimplifyConjunction(IReadOnlyList<AnimatorCondition> source,
            Func<string, AnimatorControllerParameterType?> typeOf)
        {
            if (source == null || typeOf == null || source.Count > 64) return source?.ToArray() ?? Array.Empty<AnimatorCondition>();
            var retained = new List<AnimatorCondition>();
            for (var index = 0; index < source.Count; index++)
            {
                var type = typeOf(source[index].parameter);
                var redundant = false;
                for (var other = 0; other < source.Count && !redundant; other++)
                    redundant = other != index && Implies(source[other], source[index], type) &&
                        (other < index || !Implies(source[index], source[other], type));
                if (!redundant) retained.Add(source[index]);
            }
            return retained.ToArray();
        }

        private static bool Implies(AnimatorCondition actual, AnimatorCondition required, AnimatorControllerParameterType? type)
        {
            if (actual.parameter != required.parameter) return false;
            if (type == AnimatorControllerParameterType.Bool)
                return (!Matches(actual, 0f) || Matches(required, 0f)) && (!Matches(actual, 1f) || Matches(required, 1f));
            if (type != AnimatorControllerParameterType.Int && type != AnimatorControllerParameterType.Float) return false;
            if (actual.mode == required.mode && actual.threshold == required.threshold) return true;
            if (actual.mode == AnimatorConditionMode.Equals) return Matches(required, actual.threshold);
            if (actual.mode == AnimatorConditionMode.Greater)
                return required.mode == AnimatorConditionMode.Greater && actual.threshold >= required.threshold ||
                    required.mode == AnimatorConditionMode.NotEqual && actual.threshold >= required.threshold;
            if (actual.mode == AnimatorConditionMode.Less)
                return required.mode == AnimatorConditionMode.Less && actual.threshold <= required.threshold ||
                    required.mode == AnimatorConditionMode.NotEqual && actual.threshold <= required.threshold;
            return false;
        }
    }

    public sealed class AnimatorOptimizerClipBake
    {
        public sealed class Frame
        {
            public float Threshold;
            public float[] Floats;
            public UnityEngine.Object[] Objects;
        }
        public string Key;
        public string Name;
        public string Parameter;
        public float FrameRate;
        public Bounds Bounds;
        public bool HighQuality;
        public EditorCurveBinding[] FloatBindings;
        public EditorCurveBinding[] ObjectBindings;
        public Frame[] Frames;

        public static bool TryCreate(string name, float length, float frameRate, Bounds bounds,
            bool highQuality, AnimationClipSettings settings,
            IReadOnlyList<(EditorCurveBinding Binding, AnimationCurve Curve)> floats,
            IReadOnlyList<(EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)> objects,
            float speed, float cycleOffset, bool speedParameter, bool cycleParameter, string timeParameter,
            AnimatorOptimizerOptions options, out AnimatorOptimizerClipBake bake, out string assumption)
        {
            bake = null;
            assumption = null;
            if (options == null || floats == null || objects == null) return false;
            options = options.Clone();
            if (!AnimatorOptimizerAnalyzer.IsFinite(length) || length < 0f || !AnimatorOptimizerAnalyzer.IsFinite(frameRate) || frameRate <= 0f ||
                settings == null || settings.loopTime || settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                settings.startTime != 0f || !AnimatorOptimizerAnalyzer.IsFinite(settings.stopTime) || settings.stopTime < 0f ||
                settings.stopTime != 0f && settings.stopTime != length ||
                new[] { bounds.center.x, bounds.center.y, bounds.center.z, bounds.extents.x, bounds.extents.y, bounds.extents.z }
                    .Any(value => !AnimatorOptimizerAnalyzer.IsFinite(value)) ||
                !AnimatorOptimizerAnalyzer.IsFinite(speed) || !AnimatorOptimizerAnalyzer.IsFinite(cycleOffset) || speedParameter ||
                floats.Count + objects.Count == 0 || floats.Count + objects.Count > 4096) return false;
            if (floats.Any(item => item.Binding.type == null || item.Binding.type == typeof(Animator) || item.Binding.type == typeof(Transform) ||
                    item.Binding.isPPtrCurve || !AnimatorOptimizerSemanticUtility.IsFiniteCurveData(item.Curve) || item.Curve.length == 0 ||
                    item.Curve.keys.Any(key => key.time < 0f || key.time > length)) ||
                objects.Any(item => item.Binding.type == null || item.Binding.type == typeof(Animator) || item.Binding.type == typeof(Transform) ||
                    !item.Binding.isPPtrCurve || item.Curve == null || item.Curve.Length == 0 || item.Curve[0].time != 0f ||
                    item.Curve.Where((key, index) => !AnimatorOptimizerAnalyzer.IsFinite(key.time) || key.time > length ||
                        index > 0 && key.time <= item.Curve[index - 1].time).Any())) return false;
            floats = floats.OrderBy(item => AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding), StringComparer.Ordinal).ToArray();
            objects = objects.OrderBy(item => AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding), StringComparer.Ordinal).ToArray();
            var times = new SortedSet<float>();
            if (timeParameter != null)
            {
                if (!options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.SampleMotionTime) ||
                    string.IsNullOrWhiteSpace(timeParameter) || length <= 0f || speed != 1f ||
                    objects.Any(item => !AnimatorOptimizerSemanticUtility.IsConstantObjectCurve(item.Curve)) ||
                    floats.Any(item => (item.Binding.isDiscreteCurve || item.Binding.isSerializeReferenceCurve) &&
                        !AnimatorOptimizerSemanticUtility.IsConstantCurve(item.Curve))) return false;
                // Cycle offset does not affect Motion Time; keep sampling keyed to the time parameter.
                for (var index = 0; index < options.MotionTimeSampleCount; index++)
                    times.Add(index / (float)(options.MotionTimeSampleCount - 1));
                foreach (var key in floats.SelectMany(item => item.Curve.keys))
                {
                    if (key.time < 0f || key.time > length) return false;
                    times.Add(key.time / length);
                    if (times.Count > 65) return false;
                }
                assumption = $"Motion Time '{name}' 按 '{timeParameter}' 重建 {times.Count} 点采样树；保留关键时间，缓动/阶跃区间为线性近似，输入按 0~1 钳制。";
            }
            else
            {
                if (!options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.ShortAnimationEndpoints) ||
                    cycleParameter || cycleOffset != 0f) return false;
                if (speed != 0f && (Math.Abs(speed) < 0.9f ||
                    length > options.ShortAnimationMaxFrames / frameRate ||
                    length / Math.Abs(speed) > options.ShortAnimationMaxSeconds)) return false;
                times.Add(speed > 0f ? 1f : 0f);
                assumption = speed == 0f ? $"零速动画 '{name}' 固定为起始属性；不保留外部按 normalizedTime 跳转。" :
                    $"短动画 '{name}' 取{(speed > 0f ? "末" : "首")}端点，移除 {length.ToString("0.####", CultureInfo.InvariantCulture)} 秒源动画过程。";
            }
            if ((long)times.Count * (floats.Count + objects.Count) > 65536 ||
                times.Zip(times.Skip(1), (left, right) => right - left).Any(span => span < 1.17549435E-38f)) return false;
            var frames = new List<Frame>();
            foreach (var normalized in times)
            {
                var time = normalized * length;
                var values = floats.Select(item => item.Curve.Evaluate(time)).ToArray();
                if (values.Any(value => !AnimatorOptimizerAnalyzer.IsFinite(value))) return false;
                var objectValues = objects.Select(item =>
                {
                    var value = item.Curve[0].value;
                    foreach (var key in item.Curve)
                    {
                        if (key.time > time) break;
                        value = key.value;
                    }
                    return value;
                }).ToArray();
                if (frames.Count > 0 && values.Where((value, index) =>
                    !AnimatorOptimizerAnalyzer.IsFinite(value - frames[frames.Count - 1].Floats[index])).Any()) return false;
                frames.Add(new Frame { Threshold = normalized, Floats = values, Objects = objectValues });
            }
            bake = new AnimatorOptimizerClipBake
            {
                Name = name, Parameter = timeParameter, FrameRate = frameRate, Bounds = bounds, HighQuality = highQuality,
                FloatBindings = floats.Select(item => item.Binding).ToArray(),
                ObjectBindings = objects.Select(item => item.Binding).ToArray(), Frames = frames.ToArray(),
            };
            // Compare sampled outputs rather than source asset identity. Distinct
            // short clips can have the same endpoint under the enabled assumption.
            var keyBuilder = new System.Text.StringBuilder("sampled:");
            keyBuilder.Append(AnimatorOptimizerSynchronizedGraph.Atom(timeParameter)).Append(highQuality);
            void Number(float value) => keyBuilder.Append(BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture));
            Number(frameRate);
            foreach (var value in new[] { bounds.center.x, bounds.center.y, bounds.center.z, bounds.extents.x, bounds.extents.y, bounds.extents.z }) Number(value);
            foreach (var binding in bake.FloatBindings.Concat(bake.ObjectBindings))
                keyBuilder.Append(AnimatorOptimizerSynchronizedGraph.Atom(AnimatorOptimizerSynchronizedGraph.BindingKey(binding)));
            foreach (var frame in frames)
            {
                keyBuilder.Append('|');
                if (timeParameter != null) Number(frame.Threshold);
                foreach (var value in frame.Floats) Number(value);
                foreach (var value in frame.Objects) keyBuilder.Append(':').Append(value == null ? 0 : value.GetInstanceID());
            }
            bake.Key = keyBuilder.ToString();
            return true;
        }
    }

    public static partial class AnimatorOptimizerAnalyzer
    {
        private static bool TryBuildFiniteGraphPlan(AnimatorController controller, AnimatorStateMachine machine,
            IReadOnlyList<ChildAnimatorState> children, AnimatorOptimizerOptions options, AnimatorOptimizerLayerReport report,
            bool allowHeuristics, out AnimatorOptimizerMotionPlan plan)
        {
            plan = null;
            if (machine == null || machine.stateMachines.Length != 0 || children.Count == 0 || children.Count > 256 ||
                children.Any(child => child.state == null)) return false;
            var states = children.Select(child => child.state).ToList();
            if (states.Distinct().Count() != states.Count) return false;
            var indices = states.Select((state, index) => (state, index)).ToDictionary(item => item.state, item => item.index);
            if (machine.defaultState == null || !indices.TryGetValue(machine.defaultState, out var defaultIndex)) return false;
            var declarations = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            foreach (var parameter in controller.parameters)
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) || !declarations.TryAdd(parameter.name, parameter)) return false;
            var effective = options.Clone();
            if (!allowHeuristics) effective.Heuristics = AnimatorOptimizerComponent.HeuristicFlags.None;
            var outputs = new List<AnimatorOptimizerMotionRef>();
            var assumptions = new List<string>();
            var sampledFrames = 0;
            long sampledValues = 0;
            foreach (var state in states)
            {
                if (state.behaviours.Length != 0 || state.iKOnFeet || !IsFinite(state.speed) || !IsFinite(state.cycleOffset) ||
                    !ValidateWriteDefaults(state, report)) return false;
                var output = ToMotionRef(state);
                if (!AnimatorOptimizerMotionUtility.IsTimeIndependent(state.motion))
                {
                    if (output.Motion is not AnimationClip clip || !AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip) ||
                        clip.isLooping || clip.wrapMode == WrapMode.Loop || clip.wrapMode == WrapMode.PingPong ||
                        !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(clip, out var hq)) return false;
                    if (state.timeParameterActive &&
                        (!declarations.TryGetValue(state.timeParameter ?? string.Empty, out var timeParameter) ||
                         timeParameter.type != AnimatorControllerParameterType.Float || !IsFinite(timeParameter.defaultFloat))) return false;
                    if (!AnimatorOptimizerClipBake.TryCreate(clip.name,
                        clip.length, clip.frameRate, clip.localBounds, hq, AnimationUtility.GetAnimationClipSettings(clip),
                        AnimationUtility.GetCurveBindings(clip).Select(binding => (binding, AnimationUtility.GetEditorCurve(clip, binding))).ToArray(),
                        AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(binding => (binding, AnimationUtility.GetObjectReferenceCurve(clip, binding))).ToArray(),
                        state.speed, state.cycleOffset, state.speedParameterActive, state.cycleOffsetParameterActive,
                        state.timeParameterActive ? state.timeParameter ?? string.Empty : null, effective, out var bake, out var assumption)) return false;
                    sampledFrames += bake.Frames.Length;
                    sampledValues += (long)bake.Frames.Length * (bake.FloatBindings.Length + bake.ObjectBindings.Length);
                    if (sampledFrames > 1024 || sampledValues > 262144) return false;
                    output = new AnimatorOptimizerMotionRef { Motion = clip, Bake = bake };
                    assumptions.Add(assumption);
                }
                else if (!ValidateState(controller, state, report, allowTransitions: true) || output.Mirror) return false;
                outputs.Add(output);
            }
            var graph = new AnimatorOptimizerFiniteGraph { DefaultState = defaultIndex };
            List<AnimatorOptimizerFiniteGraph.Edge> ConvertEdges(IEnumerable<AnimatorTransitionBase> source, bool entry)
            {
                var result = new List<AnimatorOptimizerFiniteGraph.Edge>();
                foreach (var transition in source)
                {
                    if (!TransitionMayBeReachable(controller, transition.conditions)) continue;
                    if (transition.destinationStateMachine != null || entry && transition.isExit ||
                        transition.isExit && transition.destinationState != null ||
                        !transition.isExit && (transition.destinationState == null || !indices.ContainsKey(transition.destinationState))) return null;
                    if (transition is AnimatorStateTransition timed)
                    {
                        var irrelevantOffset = !report.HasExitTime && timed.destinationState != null &&
                            AnimatorOptimizerMotionUtility.IsTimeIndependent(timed.destinationState.motion);
                        var needsRelaxation = timed.hasExitTime || timed.duration != 0f || timed.offset != 0f && !irrelevantOffset;
                        if (!IsFinite(timed.duration) || !IsFinite(timed.offset) || timed.hasExitTime && !IsFinite(timed.exitTime) ||
                            needsRelaxation && !effective.IgnoreTransitionTimingForStaticMotions) return null;
                        if (needsRelaxation)
                            assumptions.Add("按已启用的选项忽略过渡时间；采样/静态输出间切换改为瞬时。");
                    }
                    result.Add(new AnimatorOptimizerFiniteGraph.Edge
                    {
                        Destination = transition.isExit ? -1 : indices[transition.destinationState],
                        CanTransitionToSelf = transition is not AnimatorStateTransition stateTransition || stateTransition.canTransitionToSelf,
                        Conditions = transition.conditions,
                    });
                }
                return result;
            }
            graph.Entries = ConvertEdges(GetEffectiveAnimatorTransitions(machine.entryTransitions), true);
            graph.AnyState = ConvertEdges(GetEffectiveTransitions(machine.anyStateTransitions), false);
            graph.States = states.Select(state => ConvertEdges(GetEffectiveTransitions(state.transitions), false)).ToList();
            if (graph.Entries == null || graph.AnyState == null || graph.States.Any(edges => edges == null) ||
                !graph.TryCompile(outputs.Select(BuildMotionKey).ToArray(), declarations, effective, out var compiled)) return false;
            var dimensions = compiled.Axes.Select(axis => new AnimatorOptimizerDecisionDimension
            {
                Parameter = axis.Parameter, ParameterType = axis.Type, Values = axis.Values, IsApproximate = axis.IsApproximate,
            }).ToList();
            if (dimensions.Any(axis => declarations.TryGetValue(axis.Parameter, out var parameter) &&
                !IsVrcGlobalParameter(axis.Parameter) &&
                (parameter.type == AnimatorControllerParameterType.Bool && !options.UpgradeBoolParametersToFloat ||
                 parameter.type == AnimatorControllerParameterType.Int && !options.UpgradeIntParametersToFloat))) return false;
            plan = BuildDecisionPlanFromTable(null, dimensions,
                compiled.Outputs.ToDictionary(pair => pair.Key, pair => outputs[pair.Value]), controller);
            plan.Assumptions = assumptions.Concat(compiled.Assumptions).Distinct(StringComparer.Ordinal).ToList();
            plan.AssumedBinaryParameters = compiled.BinaryParameters;
            foreach (var assumption in plan.Assumptions) report.AddWarning("启发式：" + assumption);
            report.AddOptimization($"统一有限状态图：按输出比较 Entry / Exit / Any State / 普通转移；" +
                $"消除 {compiled.PrunedConditions} 个冗余条件、{compiled.PrunedEdges} 条被覆盖分支、{compiled.RemovedAxes} 个无关输入维度。");
            return true;
        }
    }
}