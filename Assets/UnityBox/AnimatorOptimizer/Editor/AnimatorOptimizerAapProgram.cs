using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    /// <summary>
    /// Immutable, asset-free AAP state-register program. Conditions are ANDed within
    /// each edge; ordered edges are first-match choices, not an unordered OR/sum.
    /// A reduced ordered multi-valued decision diagram avoids enumerating the full
    /// Cartesian input domain. Update and visible Output evaluate one logical transition from the same
    /// parameter snapshot; the animated register is published for the next evaluation.
    /// </summary>
    public sealed class AnimatorOptimizerAapProgram
    {
        public sealed class Axis
        {
            internal Axis(string parameter, AnimatorControllerParameterType type, IEnumerable<float> values,
                bool approximate, bool binary)
            {
                Parameter = parameter;
                SourceType = type;
                Values = Array.AsReadOnly(values.ToArray());
                IsApproximate = approximate;
                AssumesBinaryFloat = binary;
            }

            public string Parameter { get; }
            public AnimatorControllerParameterType SourceType { get; }
            public IReadOnlyList<float> Values { get; }
            public bool IsApproximate { get; }
            public bool AssumesBinaryFloat { get; }
        }

        public sealed class Node
        {
            internal Node(int axis, int value, int[] children)
            {
                AxisIndex = axis;
                Value = value;
                Children = Array.AsReadOnly(children);
            }

            public bool IsLeaf => AxisIndex < 0;
            public int AxisIndex { get; }
            public int Value { get; }
            public IReadOnlyList<int> Children { get; }
        }

        private AnimatorOptimizerAapProgram(IReadOnlyList<Axis> axes, IReadOnlyList<Axis> assumptionAxes, IReadOnlyList<Node> nodes,
            int[] roots, int entryRoot, int defaultState, bool hasEntry, bool hasExit, bool useVrChatParameterSemantics)
        {
            Axes = Array.AsReadOnly(axes.ToArray());
            AssumptionAxes = Array.AsReadOnly(assumptionAxes.ToArray());
            Nodes = Array.AsReadOnly(nodes.ToArray());
            StateRoots = Array.AsReadOnly(roots);
            EntryRoot = entryRoot;
            DefaultState = defaultState;
            RequiresInitialization = hasEntry;
            UsesRootExit = hasExit;
            UseVrChatParameterSemantics = useVrChatParameterSemantics;
            DecisionNodeCount = nodes.Count(node => !node.IsLeaf);
            ValueClipCount = nodes.Count(node => node.IsLeaf);
            EstimatedChildReferenceCount = nodes.Sum(node => node.Children.Count) * 2 + roots.Length * 3 + (hasEntry ? 4 : 0);
            var key = new System.Text.StringBuilder("aap-dag:" + (useVrChatParameterSemantics ? "vrchat:" : "unity:"));
            key.Append(defaultState).Append(':').Append(hasEntry).Append(':').Append(entryRoot).Append('|');
            foreach (var axis in axes)
            {
                key.Append(AnimatorOptimizerSynchronizedGraph.Atom(axis.Parameter)).Append((int)axis.SourceType)
                    .Append(':').Append(axis.IsApproximate).Append(':').Append(axis.AssumesBinaryFloat).Append(':');
                foreach (var value in axis.Values)
                    key.Append(BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture));
                key.Append('|');
            }
            foreach (var node in nodes)
                key.Append(node.AxisIndex).Append(':').Append(node.Value).Append(':')
                    .Append(string.Join(",", node.Children)).Append('|');
            key.Append(string.Join(",", roots)).Append("|contracts:");
            foreach (var axis in AssumptionAxes)
                key.Append(AnimatorOptimizerSynchronizedGraph.Atom(axis.Parameter))
                    .Append(':').Append(axis.AssumesBinaryFloat).Append(':').Append(axis.IsApproximate).Append('|');
            StructuralKey = key.ToString();
        }

        public IReadOnlyList<Axis> Axes { get; }
        // A domain assumption may be the very reason an axis disappears. Keep
        // its provenance separately from the nodes that survive reduction.
        public IReadOnlyList<Axis> AssumptionAxes { get; }
        // Nodes are topologically ordered: every child has a smaller index.
        public IReadOnlyList<Node> Nodes { get; }
        public IReadOnlyList<int> StateRoots { get; }
        public int EntryRoot { get; }
        public int DefaultState { get; }
        public int StateCount => StateRoots.Count;
        public bool RequiresInitialization { get; }
        public bool UsesRootExit { get; }
        public bool UseVrChatParameterSemantics { get; }
        public int DecisionNodeCount { get; }
        public int ValueClipCount { get; }
        public int EstimatedMotionAssetCount => Nodes.Count + DecisionNodeCount + StateCount + 2 + (RequiresInitialization ? 2 : 0);
        public int EstimatedChildReferenceCount { get; }
        public string StructuralKey { get; }

        public static bool TryCompile(AnimatorOptimizerFiniteGraph graph,
            IReadOnlyDictionary<string, AnimatorControllerParameter> declarations, AnimatorOptimizerOptions options,
            out AnimatorOptimizerAapProgram program, out string reason)
        {
            program = null;
            reason = null;
            if (graph == null || declarations == null || options == null || graph.States == null ||
                graph.Entries == null || graph.AnyState == null || graph.States.Count == 0 || graph.States.Count > 4096 ||
                graph.States.Any(state => state == null) || graph.DefaultState < 0 || graph.DefaultState >= graph.States.Count)
            {
                reason = "AAP 状态图缺少有效默认状态，或状态数量超过 4096。";
                return false;
            }
            var edges = graph.Entries.Concat(graph.AnyState).Concat(graph.States.SelectMany(state => state)).Take(8193).ToArray();
            if (edges.Length > 8192 || edges.Any(edge => edge == null || edge.Conditions == null ||
                    edge.Conditions.Length > 64 || edge.Destination < -1 || edge.Destination >= graph.States.Count) ||
                edges.Sum(edge => edge.Conditions.Length) > 65536 ||
                graph.Entries.Concat(graph.AnyState).Any(edge => edge.Destination < 0))
            {
                reason = "AAP 转移目标无效，或超过转移/条件预算；Entry 和 AnyState 不能指向根 Exit。";
                return false;
            }
            var resolved = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            foreach (var condition in edges.SelectMany(edge => edge.Conditions))
            {
                if (string.IsNullOrWhiteSpace(condition.parameter))
                {
                    reason = "AAP 条件引用了空参数名。";
                    return false;
                }
                if (!resolved.TryGetValue(condition.parameter, out var parameter))
                {
                    declarations.TryGetValue(condition.parameter, out parameter);
                    if (parameter == null && AnimatorOptimizerAnalyzer.TryGetVrcGlobalParameterType(condition.parameter, options.UseVrChatParameterSemantics, out var type))
                        parameter = new AnimatorControllerParameter { name = condition.parameter, type = type };
                    resolved.Add(condition.parameter, parameter);
                }
                // Validate every edge BEFORE dead-branch reduction. A consumed
                // Trigger must never disappear because its visible result is equal.
                if (!AnimatorOptimizerFiniteGraph.IsUsable(condition, parameter))
                {
                    reason = $"AAP 条件 '{condition.parameter}' 的类型/阈值无效；Trigger 消费不能替换为参数运算。";
                    return false;
                }
            }
            List<AnimatorOptimizerFiniteGraph.Edge> Reachable(IEnumerable<AnimatorOptimizerFiniteGraph.Edge> source) => source
                .Where(edge => AnimatorOptimizerSemanticUtility.ConditionsMayBeReachable(edge.Conditions, name => resolved[name].type)).ToList();
            graph = new AnimatorOptimizerFiniteGraph
            {
                DefaultState = graph.DefaultState,
                Entries = Reachable(graph.Entries),
                AnyState = Reachable(graph.AnyState),
                States = graph.States.Select(state => Reachable(state)).ToList(),
            };
            edges = graph.Entries.Concat(graph.AnyState).Concat(graph.States.SelectMany(state => state)).ToArray();
            if (edges.SelectMany(edge => edge.Conditions).Select(condition => condition.parameter).Distinct(StringComparer.Ordinal).Count() > 64)
            {
                reason = "AAP 符号图最多支持 64 个条件输入，避免过深的 BlendTree。";
                return false;
            }
            options = options.Clone();
            var axes = new List<Axis>();
            foreach (var group in edges.SelectMany(edge => edge.Conditions)
                .GroupBy(condition => condition.parameter, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var parameter = resolved[group.Key];
                var values = new SortedSet<float>();
                var approximate = false;
                var binary = parameter.type == AnimatorControllerParameterType.Float &&
                    options.Heuristics.HasFlag(AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles) &&
                    !AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(group.Key, options.UseVrChatParameterSemantics) &&
                    (parameter.defaultFloat == 0f || parameter.defaultFloat == 1f) &&
                    group.All(condition => condition.threshold >= 0f && condition.threshold <= 1f &&
                        AnimatorOptimizerFiniteGraph.Matches(condition, 0f) != AnimatorOptimizerFiniteGraph.Matches(condition, 1f));
                if (parameter.type == AnimatorControllerParameterType.Bool || binary)
                {
                    values.Add(0f);
                    values.Add(1f);
                }
                else if (parameter.type == AnimatorControllerParameterType.Int)
                {
                    foreach (var condition in group)
                    {
                        values.Add(condition.threshold - 1f);
                        values.Add(condition.threshold);
                        values.Add(condition.threshold + 1f);
                    }
                }
                else
                {
                    foreach (var condition in group)
                    {
                        if (!AnimatorOptimizerSemanticUtility.TryAddFloatConditionSamples(condition.threshold,
                            options.AllowFloatConditionApproximation, options.FloatConditionEpsilon, values, out var fallback))
                        {
                            reason = $"AAP Float '{group.Key}' 的阈值不能构造稳定阶跃；零/非正规数边界需要显式近似或适用的 0/1 假设。";
                            return false;
                        }
                        approximate |= fallback;
                    }
                }
                if (values.Count > options.MaxNumericSelectorSamples || !AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(values.ToArray()))
                {
                    reason = $"AAP 参数 '{group.Key}' 超过单轴采样预算或包含不稳定阈值跨度。";
                    return false;
                }
                axes.Add(new Axis(group.Key, parameter.type, values, approximate, binary));
            }
            try
            {
                program = new Compiler(axes, options.MaxAapDecisionNodes, options.UseVrChatParameterSemantics).Compile(graph);
                foreach (var axis in program.Axes)
                {
                    if (AnimatorOptimizerAnalyzer.TryGetVrcGlobalParameterType(axis.Parameter, options.UseVrChatParameterSemantics, out var platformType))
                    {
                        if (platformType == AnimatorControllerParameterType.Float && axis.SourceType == AnimatorControllerParameterType.Float) continue;
                        reason = $"平台输入 '{axis.Parameter}' 使用 {axis.SourceType} 参数槽；保留原生条件状态机。";
                        program = null;
                        return false;
                    }
                    if (axis.SourceType == AnimatorControllerParameterType.Bool && !options.UpgradeBoolParametersToFloat ||
                        axis.SourceType == AnimatorControllerParameterType.Int && !options.UpgradeIntParametersToFloat)
                    {
                        reason = $"AAP 输入 '{axis.Parameter}' 需要启用对应的 Bool/Int → Float 映射。";
                        program = null;
                        return false;
                    }
                }
                return true;
            }
            catch (BudgetExceededException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private sealed class BudgetExceededException : Exception
        {
            public BudgetExceededException(string message) : base(message) { }
        }

        private sealed class Compiler
        {
            private readonly IReadOnlyList<Axis> axes;
            private readonly Dictionary<string, int> axisByName;
            private readonly int maxNodes;
            private readonly bool useVrChatParameterSemantics;
            private readonly List<Node> nodes = new List<Node>();
            private readonly Dictionary<int, int> leaves = new Dictionary<int, int>();
            private readonly Dictionary<string, int> uniqueNodes = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<(int Condition, int Yes, int No), int> selections = new Dictionary<(int, int, int), int>();
            private int workRemaining = 2097152;
            private int workingChildren;

            public Compiler(IReadOnlyList<Axis> axes, int maxNodes, bool useVrChatParameterSemantics)
            {
                this.axes = axes;
                this.maxNodes = maxNodes;
                this.useVrChatParameterSemantics = useVrChatParameterSemantics;
                axisByName = axes.Select((axis, index) => (axis, index))
                    .ToDictionary(item => item.axis.Parameter, item => item.index, StringComparer.Ordinal);
            }

            private void Work(int amount = 1)
            {
                if ((workRemaining -= amount) < 0)
                    throw new BudgetExceededException("AAP 符号运算超过工作量预算，已在创建资源前保留原图。");
            }

            private int Leaf(int value)
            {
                if (leaves.TryGetValue(value, out var id)) return id;
                id = nodes.Count;
                nodes.Add(new Node(-1, value, Array.Empty<int>()));
                leaves.Add(value, id);
                return id;
            }

            private int Branch(int axis, int[] children)
            {
                Work(children.Length);
                if (children.All(child => child == children[0])) return children[0];
                var key = axis.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", children);
                if (uniqueNodes.TryGetValue(key, out var id)) return id;
                workingChildren += children.Length;
                if (nodes.Count >= Math.Min(131072, maxNodes * 8 + 8192) || workingChildren > 1048576)
                    throw new BudgetExceededException("AAP 中间决策图超过内存预算，已在创建资源前保留原图。");
                id = nodes.Count;
                nodes.Add(new Node(axis, 0, children));
                uniqueNodes.Add(key, id);
                return id;
            }

            private int Select(int condition, int yes, int no)
            {
                Work();
                if (yes == no) return yes;
                if (nodes[condition].IsLeaf) return nodes[condition].Value == 0 ? no : yes;
                var key = (condition, yes, no);
                if (selections.TryGetValue(key, out var previous)) return previous;
                int Top(int id) => nodes[id].IsLeaf ? int.MaxValue : nodes[id].AxisIndex;
                var axis = Math.Min(Top(condition), Math.Min(Top(yes), Top(no)));
                int Child(int id, int index) => Top(id) == axis ? nodes[id].Children[index] : id;
                var children = new int[axes[axis].Values.Count];
                for (var index = 0; index < children.Length; index++)
                    children[index] = Select(Child(condition, index), Child(yes, index), Child(no, index));
                var result = Branch(axis, children);
                if (selections.Count >= 262144)
                    throw new BudgetExceededException("AAP 条件合成缓存超过预算，已在创建资源前保留原图。");
                selections.Add(key, result);
                return result;
            }

            private int Conjunction(IReadOnlyList<AnimatorCondition> conditions)
            {
                var root = Leaf(1);
                var zero = Leaf(0);
                foreach (var group in conditions.GroupBy(condition => axisByName[condition.parameter]).OrderByDescending(group => group.Key))
                {
                    Work(group.Count() * axes[group.Key].Values.Count);
                    var child = root;
                    root = Branch(group.Key, axes[group.Key].Values.Select(value =>
                        group.All(condition => AnimatorOptimizerFiniteGraph.Matches(condition, value)) ? child : zero).ToArray());
                }
                return root;
            }

            public AnimatorOptimizerAapProgram Compile(AnimatorOptimizerFiniteGraph graph)
            {
                var edgeConditions = new Dictionary<AnimatorOptimizerFiniteGraph.Edge, int>();
                foreach (var edge in graph.Entries.Concat(graph.AnyState).Concat(graph.States.SelectMany(state => state)).Distinct())
                    edgeConditions.Add(edge, Conjunction(edge.Conditions));
                var entry = Leaf(graph.DefaultState);
                for (var index = graph.Entries.Count - 1; index >= 0; index--)
                {
                    var edge = graph.Entries[index];
                    entry = Select(edgeConditions[edge], Leaf(edge.Destination), entry);
                }
                var roots = new int[graph.States.Count];
                for (var state = 0; state < roots.Length; state++)
                {
                    var root = Leaf(state); // No matching edge preserves state, including equality gaps.
                    var local = graph.States[state];
                    for (var index = local.Count - 1; index >= 0; index--)
                    {
                        var edge = local[index];
                        root = Select(edgeConditions[edge], edge.Destination < 0 ? entry : Leaf(edge.Destination), root);
                    }
                    // AnyState wins. A disabled self edge is skipped, not converted
                    // to a hold branch that would suppress later AnyState/local edges.
                    for (var index = graph.AnyState.Count - 1; index >= 0; index--)
                    {
                        var edge = graph.AnyState[index];
                        if (edge.Destination == state && !edge.CanTransitionToSelf) continue;
                        root = Select(edgeConditions[edge], Leaf(edge.Destination), root);
                    }
                    roots[state] = root;
                }

                var reachable = new HashSet<int>();
                void Visit(int id)
                {
                    if (!reachable.Add(id)) return;
                    foreach (var child in nodes[id].Children) Visit(child);
                }
                foreach (var root in roots) Visit(root);
                if (graph.Entries.Count > 0) Visit(entry);
                var ordered = reachable.OrderBy(id => id).ToArray();
                if (ordered.Count(id => !nodes[id].IsLeaf) > maxNodes || ordered.Sum(id => (long)nodes[id].Children.Count) > 262144)
                    throw new BudgetExceededException($"AAP 共享决策图超过 {maxNodes} 个节点或 262144 条子引用；未创建任何资源。");
                var remap = ordered.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
                var usedAxes = ordered.Where(id => !nodes[id].IsLeaf).Select(id => nodes[id].AxisIndex).Distinct().OrderBy(id => id).ToArray();
                var axisRemap = usedAxes.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
                var compact = ordered.Select(id => new Node(nodes[id].IsLeaf ? -1 : axisRemap[nodes[id].AxisIndex],
                    nodes[id].Value, nodes[id].Children.Select(child => remap[child]).ToArray())).ToArray();
                return new AnimatorOptimizerAapProgram(usedAxes.Select(id => axes[id]).ToArray(),
                    axes.Where(axis => axis.AssumesBinaryFloat || axis.IsApproximate).ToArray(), compact,
                    roots.Select(root => remap[root]).ToArray(), graph.Entries.Count > 0 ? remap[entry] : -1,
                    graph.DefaultState, graph.Entries.Count > 0, graph.States.SelectMany(state => state).Any(edge => edge.Destination < 0), useVrChatParameterSemantics);
            }
        }
    }
}