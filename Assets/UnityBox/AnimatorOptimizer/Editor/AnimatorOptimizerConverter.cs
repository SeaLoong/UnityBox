using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerConversionResult
    {
        internal AnimatorOptimizerConversionResult(
            AnimatorController controller,
            IEnumerable<AnimatorControllerLayer> generatedLayers,
            IEnumerable<int> mergedLayerIndices,
            IEnumerable<string> upgradedParameters,
            IEnumerable<string> warnings)
        {
            Controller = controller;
            GeneratedLayers = new List<AnimatorControllerLayer>(generatedLayers ?? Enumerable.Empty<AnimatorControllerLayer>()).AsReadOnly();
            MergedLayerIndices = new List<int>(mergedLayerIndices ?? Enumerable.Empty<int>()).AsReadOnly();
            UpgradedParameters = new List<string>(upgradedParameters ?? Enumerable.Empty<string>()).AsReadOnly();
            Warnings = new List<string>(warnings ?? Enumerable.Empty<string>()).AsReadOnly();
        }

        public AnimatorController Controller { get; }
        public IReadOnlyList<AnimatorControllerLayer> GeneratedLayers { get; }
        public AnimatorControllerLayer GeneratedLayer => GeneratedLayers.FirstOrDefault();
        public IReadOnlyList<int> MergedLayerIndices { get; }
        public IReadOnlyList<string> UpgradedParameters { get; }
        public IReadOnlyList<string> Warnings { get; }
        public int MergedLayerCount => MergedLayerIndices.Count;
    }

    public static partial class AnimatorOptimizerConverter
    {
        public const string GeneratedLayerName = "(WD On) AnimatorOptimizer";
        public const string GeneratedAssetPrefix = "AnimatorOptimizer_";
        private static int priorityClipCounter;

        private sealed class LayerGroup
        {
            public AnimatorLayerBlendingMode BlendingMode;
            public string MaskKey;
            public AvatarMask Mask;
            public string StateTag;
            public List<AnimatorOptimizerLayerReport> Reports;

            public int FirstIndex => Reports.Min(report => report.Index);
        }

        private sealed class PriorityDimension
        {
            public string Parameter;
            public List<float> Values;
        }

        public static IReadOnlyList<string> GetSelectionIssues(
            AnimatorOptimizerReport report,
            IEnumerable<int> selectedLayerIndices,
            AnimatorOptimizerOptions options = null)
        {
            var issues = new List<string>();
            if (report == null)
            {
                issues.Add("还没有 AnimatorController 分析结果。");
                return issues;
            }
            if (!report.GraphIsValid)
            {
                issues.Add("Controller 图结构无效，不能安全分析或转换。");
                return issues;
            }

            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var generatedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            if (report.Layers.Any(layer =>
                    IsGeneratedLayerName(layer.Name, generatedLayerName) ||
                    layer.Name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal)))
            {
                issues.Add("Controller 已包含 AnimatorOptimizer 生成层；请先恢复源 Controller，避免重复优化。");
            }

            var selected = NormalizeSelection(report, selectedLayerIndices, issues);
            if (selected.Count == 0)
                issues.Add("请选择至少一个可转换 Layer。");
            if (options.PreserveMmdCompatibility && selected.Contains(0))
                issues.Add("启用 MMD 兼容时不能转换 Controller 的首层；首层必须保留为兼容基准层。");
            issues.AddRange(GetLayerIndexReferenceIssues(report.Controller, selected, options));
            var includesAapStateRegister = selected.Any(index =>
                report.Layers[index].ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister);
            if (options.RequireAtLeastTwoLayers && selected.Count < 2 && !includesAapStateRegister)
                issues.Add("至少选择两个可转换 Layer，合并后才会减少 Animator Layer 数量。");

            var selectedReports = selected.Select(index => report.Layers[index]).ToList();
            foreach (var layer in selectedReports)
            {
                if (!layer.CanConvert)
                {
                    foreach (var reason in layer.Reasons)
                        issues.Add($"Layer {layer.Index} ({layer.Name}): {reason}");
                }
                if (!layer.BindingsAreComplete)
                    issues.Add($"Layer {layer.Index} ({layer.Name}) 的动画绑定无法完整分析，不能保证转换后无冲突。");
            }
            var aapPrograms = selectedReports.Where(layer => layer.ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister)
                .Select(layer => layer.Plan?.StateRegisterProgram).Where(program => program != null).ToList();
            var aapAssets = aapPrograms.Sum(program => (long)program.EstimatedMotionAssetCount);
            var aapChildren = aapPrograms.Sum(program => (long)program.EstimatedChildReferenceCount);
            if (aapAssets > 65536 || aapChildren > 1048576)
                issues.Add($"AAP 批量计划预计生成 {aapAssets} 个 Motion 资源、至多 {aapChildren} 条子引用，超过整批资源保护上限；请缩小选集或节点预算。");

            // AAP layers stay at their original slots; they are NOT members of a
            // regular merge group. Treat them as retained barriers for that group.
            var regularReports = selectedReports.Where(layer => layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister).ToList();
            var regularIndices = regularReports.Select(layer => layer.Index).ToHashSet();
            var groups = BuildLayerGroups(regularReports);
            if (!options.MergeLayersWithEquivalentMasks && groups.Any(group => !string.Equals(group.MaskKey, "<none>", StringComparison.Ordinal)))
            {
                issues.Add("当前选择包含 AvatarMask，但已关闭“合并等价 Mask”；请开启该选项或拆分选择。");
            }
            if (HasInterleavedConflictingGroups(groups))
                issues.Add("不同 BlendMode/Mask 组在 Layer 顺序上交错且控制相同属性；合并会改变组间 Override/Additive 顺序，请分组处理。");
            foreach (var group in groups)
            {
                if (!CanPreserveLayerBehaviours(group.Reports))
                    issues.Add($"Layer 组 ({group.BlendingMode}/{group.MaskKey}) 的 StateMachine Behaviour 不完全一致，合并会丢失 MMD/其它回调语义。");
            }

            issues.AddRange(GetParameterSafetyIssues(report, selected, options));
            var sourceLayers = report.Layers.Select(layer => layer.SourceLayer).ToArray();
            foreach (var group in groups)
            {
                foreach (var candidate in group.Reports)
                {
                    for (var index = group.FirstIndex + 1; index < candidate.Index; index++)
                    {
                        var written = new HashSet<string>(StringComparer.Ordinal);
                        CollectAnimatedAnimatorParameterNames(sourceLayers, index, written, new HashSet<int>());
                        if (candidate.RequiredFloatParameters.Concat(candidate.Plan?.ReferencedParameters ?? new HashSet<string>())
                            .Any(written.Contains))
                            issues.Add($"Layer {candidate.Index} 读取 Layer {index} 的 AAP 输出；合并不能跨过参数写入层，避免改变 MA 响应式反馈顺序。");
                    }
                }
            }

            foreach (var group in groups)
            {
                if (group.Reports.Count > 1 && !CanMergeLayerMotionSet(group.Reports))
                {
                    issues.Add(
                        $"Layer 组 ({group.BlendingMode}/{group.MaskKey}) 包含不同动画时间基准；" +
                        "Direct BlendTree 会改变动态 Clip 的播放时间，只有同时间轴 Clip 或时间无关 Motion 才能合并。");
                }
            }

            if (!options.StrictCurveConflicts || selected.Count == 0)
                return DistinctIssues(issues);

            foreach (var group in groups)
            {
                var resolvesOverlap = !HasAnyCurveOverlap(group.Reports) || CanResolveLayerOverlap(group.Reports, options);
                if (resolvesOverlap) continue;
                for (var leftIndex = 0; leftIndex < group.Reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < group.Reports.Count; rightIndex++)
                    {
                        var left = group.Reports[leftIndex];
                        var right = group.Reports[rightIndex];
                        var overlap = left.Bindings.Intersect(right.Bindings).Take(3).ToList();
                        if (overlap.Count > 0)
                        {
                            issues.Add(
                                $"Layer {left.Index} ({left.Name}) 与 Layer {right.Index} ({right.Name}) 存在重复动画绑定；" +
                                "当前输出包含时间/连续参数或组合数过大，无法安全模拟原 Layer 覆盖/叠加优先级。示例: " + overlap[0]);
                        }
                    }
                }
            }

            foreach (var group in groups)
            {
                var groupBindings = new HashSet<AnimatorOptimizerBindingKey>(
                    group.Reports.SelectMany(layer => layer.Bindings));
                foreach (var layer in report.Layers)
                {
                    if (regularIndices.Contains(layer.Index) || groupBindings.Count == 0)
                        continue;

                    if (!layer.BindingsAreComplete)
                    {
                        if (CanRetainUnselectedLayerBehaviour(report.Controller, layer, selected, options))
                            continue;
                        issues.Add(
                            $"未合并 Layer {layer.Index} ({layer.Name}) 的动画绑定无法完整分析；" +
                            "为保证多个 Layer 的覆盖顺序，当前选择不能安全合并。");
                        continue;
                    }

                    var overlap = groupBindings.Intersect(layer.Bindings).Take(1).ToList();
                    if (overlap.Count > 0 && layer.Index > group.FirstIndex &&
                        layer.Index < group.Reports.Max(candidate => candidate.Index))
                    {
                        issues.Add(
                            $"待合并 Layer 组 ({group.BlendingMode}/{group.MaskKey}) 与未合并 Layer {layer.Index} ({layer.Name}) 存在动画绑定冲突；" +
                            "示例: " + overlap[0]);
                    }
                }
            }

            return DistinctIssues(issues);
        }

        public static IReadOnlyList<int> GetRecommendedSelection(AnimatorOptimizerReport report,
            AnimatorOptimizerOptions options = null)
        {
            if (report == null || !report.GraphIsValid)
                return Array.Empty<int>();
            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var candidates = report.ConvertibleLayers.Where(layer =>
                (!options.PreserveMmdCompatibility || layer.Index != 0)).Select(layer => layer.Index).ToList();
            if (GetSelectionIssues(report, candidates, options).Count == 0)
                return candidates;

            var relaxedCount = options.Clone();
            relaxedCount.RequireAtLeastTwoLayers = false;
            var selected = new List<int>();
            foreach (var group in BuildLayerGroups(candidates.Select(index => report.Layers[index])
                .Where(layer => layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister).ToList()))
            {
                var trial = selected.Concat(group.Reports.Select(layer => layer.Index)).Distinct().OrderBy(index => index).ToList();
                if (GetSelectionIssues(report, trial, relaxedCount).Count == 0)
                {
                    selected = trial;
                    continue;
                }
                foreach (var layer in group.Reports)
                {
                    trial = selected.Concat(new[] { layer.Index }).OrderBy(index => index).ToList();
                    if (GetSelectionIssues(report, trial, relaxedCount).Count == 0)
                        selected = trial;
                }
            }
            foreach (var layer in report.ConvertibleLayers.Where(layer =>
                layer.ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister &&
                (!options.PreserveMmdCompatibility || layer.Index != 0)))
            {
                var trial = selected.Concat(new[] { layer.Index }).OrderBy(index => index).ToList();
                if (GetSelectionIssues(report, trial, relaxedCount).Count == 0)
                    selected = trial;
            }
            return GetSelectionIssues(report, selected, options).Count == 0 ? selected : Array.Empty<int>();
        }

        private static bool CanRetainUnselectedLayerBehaviour(
            AnimatorController controller,
            AnimatorOptimizerLayerReport report,
            IReadOnlyCollection<int> selected,
            AnimatorOptimizerOptions options)
        {
            if (controller == null || report?.SourceLayer?.stateMachine == null ||
                options == null || !options.HasKnownPlayableLayer)
                return false;

            var controls = EnumerateLayerControls(report.SourceLayer.stateMachine, new HashSet<int>()).ToList();
            if (controls.Count == 0 || controls.Any(control =>
                    !AppliesToTargetPlayable(control, options) || selected.Contains(control.LayerIndex)))
                return false;
            if (StateMachineHasUnsupportedBehaviour(report.SourceLayer.stateMachine) ||
                StateMachineHasAnimatorParameterCurve(report.SourceLayer.stateMachine, new HashSet<int>()))
                return false;
            return true;
        }

        private static bool StateMachineHasUnsupportedBehaviour(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
                return false;
            if ((stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Any(behaviour => !IsRetainableUnselectedLayerBehaviour(behaviour)))
                return true;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null && (child.state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                    .Any(behaviour => !IsRetainableUnselectedLayerBehaviour(behaviour)))
                    return true;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .Any(child => StateMachineHasUnsupportedBehaviour(child.stateMachine));
        }

        private static bool IsRetainableUnselectedLayerBehaviour(StateMachineBehaviour behaviour)
        {
            return AnimatorOptimizerAnalyzer.IsPreservableLayerBehaviour(behaviour) ||
                   AnimatorOptimizerPlatform.GetLayerControl(behaviour) != null;
        }

        private static bool StateMachineHasAnimatorParameterCurve(
            AnimatorStateMachine stateMachine,
            ISet<int> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine.GetInstanceID()))
                return false;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null && MotionHasAnimatorParameterCurve(child.state.motion, new HashSet<int>()))
                    return true;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .Any(child => StateMachineHasAnimatorParameterCurve(child.stateMachine, visited));
        }

        private static bool MotionHasAnimatorParameterCurve(Motion motion, ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return false;
            if (motion is AnimationClip clip)
                return AnimationUtility.GetCurveBindings(clip).Any(binding => binding.type == typeof(Animator)) ||
                       AnimationUtility.GetObjectReferenceCurveBindings(clip)
                           .Any(binding => binding.type == typeof(Animator));
            return motion is BlendTree tree && (tree.children ?? Array.Empty<ChildMotion>())
                .Any(child => MotionHasAnimatorParameterCurve(child.motion, visited));
        }

        private static IReadOnlyList<string> GetLayerIndexReferenceIssues(
            AnimatorController controller,
            IReadOnlyCollection<int> selected,
            AnimatorOptimizerOptions options)
        {
            var issues = new List<string>();
            if (controller == null || selected == null || selected.Count == 0)
                return issues;

            var controls = EnumerateLayerControls(controller).ToList();
            if (controls.Count > 0 && !options.HasKnownPlayableLayer)
            {
                issues.Add("Controller 包含 VRCAnimatorLayerControl，但当前目标 Playable Layer 未知；为避免 Layer 合并后索引漂移，转换已阻止。");
            }
            else
            {
                foreach (var control in controls.Where(control => AppliesToTargetPlayable(control, options)))
                {
                    if (selected.Contains(control.LayerIndex))
                    {
                        issues.Add($"VRCAnimatorLayerControl 引用了待合并 Layer {control.LayerIndex}；合并后无法保留该 Layer 的独立权重控制。");
                    }
                }
            }

            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            issues.AddRange(GetMalformedLayerReferenceIssues(layers, options));
            for (var index = 0; index < layers.Length; index++)
            {
                if (layers[index] != null && selected.Contains(layers[index].syncedLayerIndex))
                    issues.Add($"未选中 Layer {index} 的 Synced Layer 引用了待合并 Layer {layers[index].syncedLayerIndex}。");
            }
            return DistinctIssues(issues);
        }

        private static List<string> GetMalformedLayerReferenceIssues(IReadOnlyList<AnimatorControllerLayer> layers,
            AnimatorOptimizerOptions options)
        {
            var issues = new List<string>();
            for (var index = 0; index < layers.Count; index++)
            {
                var target = layers[index]?.syncedLayerIndex ?? -1;
                if (target >= layers.Count || target < -1 || target == index)
                    issues.Add($"Layer {index} 的 Synced Layer 引用 {target} 无效；保留源索引，不自动改成其它层。");
                var chain = new HashSet<int> { index };
                while (target >= 0 && target < layers.Count)
                {
                    if (!chain.Add(target))
                    {
                        issues.Add($"Layer {index} 的 Synced Layer 引用形成循环，不能安全重排。");
                        break;
                    }
                    target = layers[target]?.syncedLayerIndex ?? -1;
                }
            }
            foreach (var control in EnumerateLayerControlsFromLayers(layers))
                if (AppliesToTargetPlayable(control, options) && (control.LayerIndex < 0 || control.LayerIndex >= layers.Count))
                    issues.Add($"VRCAnimatorLayerControl 引用了不存在的 Layer {control.LayerIndex}，不能安全重排。");
            return issues;
        }

        private static IEnumerable<IAnimatorOptimizerLayerControl> EnumerateLayerControls(
            AnimatorController controller)
        {
            return EnumerateLayerControlsFromLayers(controller?.layers ?? Array.Empty<AnimatorControllerLayer>());
        }

        private static IEnumerable<IAnimatorOptimizerLayerControl> EnumerateLayerControls(
            AnimatorStateMachine stateMachine,
            ISet<int> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine.GetInstanceID()))
                yield break;
            foreach (var behaviour in stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
            {
                var control = AnimatorOptimizerPlatform.GetLayerControl(behaviour);
                if (control != null) yield return control;
            }
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                foreach (var behaviour in child.state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                {
                    var control = AnimatorOptimizerPlatform.GetLayerControl(behaviour);
                    if (control != null) yield return control;
                }
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                foreach (var control in EnumerateLayerControls(child.stateMachine, visited))
                    yield return control;
            }
        }

        private static bool AppliesToTargetPlayable(
            IAnimatorOptimizerLayerControl control,
            AnimatorOptimizerOptions options)
        {
            return control != null && options != null && options.HasKnownPlayableLayer &&
                control.PlayableLayer == options.TargetPlayableLayer;
        }

        internal static IReadOnlyList<string> GetParameterSafetyIssues(
            AnimatorOptimizerReport report,
            IEnumerable<int> selectedLayerIndices,
            AnimatorOptimizerOptions options)
        {
            if (report == null)
                return Array.Empty<string>();
            var selected = NormalizeSelection(report, selectedLayerIndices, new List<string>());
            var requiredParameters = new HashSet<string>(
                selected.SelectMany(index => report.Layers[index].RequiredFloatParameters),
                StringComparer.Ordinal);
            var animatedParameters = CollectAnimatedAnimatorParameterNames(report.Controller);
            var retainedConditions = report.Layers.Where(layer => !selected.Contains(layer.Index))
                .SelectMany(layer => EnumerateTransitionConditions(layer.SourceLayer?.stateMachine,
                    new HashSet<AnimatorStateMachine>())).ToLookup(condition => condition.parameter, StringComparer.Ordinal);
            var issues = new List<string>();
            foreach (var parameter in requiredParameters)
            {
                var declaration = report.Controller.parameters.FirstOrDefault(candidate => candidate.name == parameter);
                if (declaration != null && declaration.type == AnimatorControllerParameterType.Float)
                    continue;
                if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(parameter, options.UseVrChatParameterSemantics))
                {
                    continue;
                }
                else if (HasPotentialParameterDriverFor(report.Controller, parameter))
                {
                    issues.Add($"参数 '{parameter}' 被 ParameterDriver/类似 StateMachineBehaviour 使用，不能安全升级为 Float。");
                }
                else if (animatedParameters.Contains(parameter))
                {
                    issues.Add($"参数 '{parameter}' 被 Animator FloatCurve/AAP 写入，不能安全升级为 Float。");
                }
                else if (HasPotentialBehaviourReferenceFor(report.Controller, parameter))
                {
                    issues.Add($"参数 '{parameter}' 被其它 StateMachineBehaviour 引用，无法证明升级为 Float 不改变语义。");
                }
                if (declaration != null && retainedConditions[parameter].Any(condition =>
                        !CanPreserveConditionAfterFloatUpgrade(condition, declaration.type)))
                {
                    issues.Add($"参数 '{parameter}' 仍被保留层的条件使用，无法安全升级为 Float；" +
                        "Int Equals/NotEqual 或无效条件不能原样保留，请同时转换使用这些条件的层。");
                }
            }
            return DistinctIssues(issues);
        }

        private static bool CanPreserveConditionAfterFloatUpgrade(
            AnimatorCondition condition, AnimatorControllerParameterType type)
        {
            if (type == AnimatorControllerParameterType.Bool)
                return condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot ||
                    (condition.mode == AnimatorConditionMode.Equals || condition.mode == AnimatorConditionMode.NotEqual) &&
                        (condition.threshold == 0f || condition.threshold == 1f);
            if (type == AnimatorControllerParameterType.Int)
                return (condition.mode == AnimatorConditionMode.Greater || condition.mode == AnimatorConditionMode.Less) &&
                    AnimatorOptimizerAnalyzer.IsUsableConditionThreshold(condition, type) &&
                    condition.threshold == Mathf.Round(condition.threshold);
            return false;
        }

        private static IEnumerable<AnimatorCondition> EnumerateTransitionConditions(
            AnimatorStateMachine stateMachine, ISet<AnimatorStateMachine> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine))
                yield break;
            var transitions = (stateMachine.anyStateTransitions ?? Array.Empty<AnimatorStateTransition>())
                .Cast<AnimatorTransitionBase>().Concat(stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>())
                .Concat((stateMachine.states ?? Array.Empty<ChildAnimatorState>())
                    .Where(child => child.state != null).SelectMany(child => child.state.transitions ?? Array.Empty<AnimatorStateTransition>()));
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                if (child.stateMachine == null) continue;
                transitions = transitions.Concat(stateMachine.GetStateMachineTransitions(child.stateMachine) ?? Array.Empty<AnimatorTransition>());
                foreach (var condition in EnumerateTransitionConditions(child.stateMachine, visited))
                    yield return condition;
            }
            foreach (var transition in transitions.Where(transition => transition != null))
                foreach (var condition in transition.conditions ?? Array.Empty<AnimatorCondition>())
                    yield return condition;
        }

        public static AnimatorOptimizerConversionResult ConvertInPlace(
            AnimatorController controller,
            IEnumerable<int> selectedLayerIndices,
            AnimatorOptimizerOptions options = null)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var requestedSelection = selectedLayerIndices?.ToArray() ?? Array.Empty<int>();
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = GetSelectionIssues(report, requestedSelection, options);
            if (issues.Count > 0)
                throw new InvalidOperationException("AnimatorOptimizer 转换被阻止:\n- " + string.Join("\n- ", issues));

            var selected = NormalizeSelection(report, requestedSelection, new List<string>());
            var selectedReports = selected.Select(index => report.Layers[index]).ToList();
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(controllerPath) || !controllerPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AnimatorController 必须是 Assets/ 下已保存的资源。");
            Undo.RegisterCompleteObjectUndo(controller, "Animator Optimizer");
            if (EnsureOwnedMutableGraph(controller))
            {
                report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
                selectedReports = selected.Select(index => report.Layers[index]).ToList();
                issues = GetSelectionIssues(report, selected, options);
                if (issues.Count > 0)
                    throw new InvalidOperationException("复制外部图后选集不再有效:\n- " + string.Join("\n- ", issues));
            }

            var generatedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            if ((controller.layers ?? Array.Empty<AnimatorControllerLayer>()).Any(layer =>
                    IsGeneratedLayerName(layer.name, generatedLayerName) ||
                    layer.name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Controller 已包含 AnimatorOptimizer 生成层，请先恢复源资源。");
            }

            var alwaysOneWarnings = new List<string>();
            if (options.NormalizeAlwaysOneParameters)
                NormalizeAlwaysOneParameters(controller, alwaysOneWarnings);
            // Upgrade once against the SAME pre-upgrade plans. Re-analysis between
            // phases would mistake previously discrete Bool/Int inputs for Float.
            var upgradedParameters = UpgradeParameters(controller, selectedReports, options);
            var warnings = new List<string>(alwaysOneWarnings.Concat(selectedReports.SelectMany(layer => layer.Warnings)));
            foreach (var layer in selectedReports.Where(layer => layer.UsesHeuristics))
                Debug.LogWarning($"[AnimatorOptimizer] 启发式 [{layer.Index}] {layer.Name}:\n- " + string.Join("\n- ", layer.HeuristicAssumptions));
            MaterializeBakedMotions(controller, selectedReports);
            if (!options.StrictCurveConflicts)
                warnings.Add("已关闭严格曲线冲突检查；请确认没有依赖 Layer 顺序的重复动画绑定。");

            var regularReports = selectedReports.Where(layer => layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister).ToList();
            var emptyClip = regularReports.Count == 0 ? null : CreateEmptyClip(controller);
            var weightParameter = regularReports.Count == 0 ? null : EnsureWeightParameter(controller, options.WeightParameterPrefix,
                options.NormalizeAlwaysOneParameters, options.UseVrChatParameterSemantics);
            var groups = BuildLayerGroups(regularReports);
            var generatedLayers = new List<AnimatorControllerLayer>();
            var generatedAtIndex = new Dictionary<int, List<AnimatorControllerLayer>>();
            var groupNumber = 0;
            foreach (var group in groups.OrderBy(group => group.FirstIndex))
            {
                var rootTree = new BlendTree
                {
                    name = GeneratedAssetPrefix + "Root_" + groupNumber,
                    blendType = BlendTreeType.Direct,
                    blendParameter = weightParameter,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                AddSubAsset(controller, rootTree);
                SetNormalizedBlendValues(rootTree, false);

                var rootChildren = new List<ChildMotion>();
                var hasOverlap = HasAnyCurveOverlap(group.Reports);
                if (TryBuildLayerComposite(controller, group.Reports, emptyClip, group.BlendingMode,
                    out var staticComposite, out var compositeSettings))
                {
                    rootChildren.Add(new ChildMotion
                    {
                        motion = staticComposite,
                        directBlendParameter = weightParameter,
                        threshold = group.FirstIndex,
                        timeScale = compositeSettings == null ? 1f : compositeSettings.Speed,
                        cycleOffset = compositeSettings == null ? 0f : compositeSettings.CycleOffset,
                        mirror = compositeSettings != null && compositeSettings.Mirror,
                    });
                    warnings.Add($"Group {groupNumber} ({group.BlendingMode}/{group.MaskKey}) 已将同时间轴 Layer Motion 合并为单一 Clip。");
                }
                else if (hasOverlap && options.ResolveOverridePriorityWithStaticComposite &&
                         CanResolveLayerOverlap(group.Reports, options))
                {
                    rootChildren.Add(new ChildMotion
                    {
                        motion = BuildPriorityCompositeTree(controller, group.Reports, emptyClip, options, warnings, group.BlendingMode),
                        directBlendParameter = weightParameter,
                        threshold = group.FirstIndex,
                        timeScale = 1f,
                        cycleOffset = 0f,
                        mirror = false,
                    });
                    warnings.Add($"Group {groupNumber} ({group.BlendingMode}/{group.MaskKey}) 使用有限参数复合模拟原 Layer 优先级。");
                }
                else
                {
                    if (hasOverlap && options.StrictCurveConflicts)
                        throw new InvalidOperationException("生成阶段无法保留已分析的属性优先级，拒绝退回普通 Direct 混合。请重新分析源 Controller。");
                    foreach (var layerReport in GetUniqueReports(group.Reports))
                    {
                        var built = BuildPlanMotion(controller, layerReport.Plan, emptyClip);
                        if (TryFlattenConstantDirectTree(built, out var flattenedChildren))
                        {
                            rootChildren.AddRange(flattenedChildren);
                            warnings.Add($"Layer {layerReport.Index} ({layerReport.Name}) 的未归一化 Direct BlendTree 已提升到合并根节点。");
                            continue;
                        }
                        rootChildren.Add(new ChildMotion
                        {
                            motion = built.Motion ?? emptyClip,
                            directBlendParameter = weightParameter,
                            threshold = layerReport.Index,
                            timeScale = built.HasStateSettings ? built.Speed : 1f,
                            cycleOffset = built.HasStateSettings ? built.CycleOffset : 0f,
                            mirror = built.HasStateSettings && built.Mirror,
                        });
                        warnings.AddRange(layerReport.Warnings);
                    }
                }
                rootTree.children = rootChildren.ToArray();

                var stateMachine = new AnimatorStateMachine
                {
                    name = GeneratedAssetPrefix + "StateMachine_" + groupNumber,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                stateMachine.behaviours = ClonePreservableLayerBehaviours(controller, group.Reports);
                var state = stateMachine.AddState(
                    GeneratedAssetPrefix + "Root (WD On) " + groupNumber,
                    new Vector3(320f, 80f, 0f));
                state.motion = rootTree;
                state.tag = group.StateTag ?? string.Empty;
                state.writeDefaultValues = true;
                stateMachine.defaultState = state;
                AddSubAsset(controller, stateMachine);

                var generatedLayer = new AnimatorControllerLayer
                {
                    name = groups.Count == 1 ? generatedLayerName : generatedLayerName + "/" + group.BlendingMode + "_" + groupNumber,
                    defaultWeight = 1f,
                    blendingMode = group.BlendingMode,
                    iKPass = false,
                    avatarMask = group.Mask,
                    syncedLayerIndex = -1,
                    stateMachine = stateMachine,
                };
                generatedLayers.Add(generatedLayer);
                if (!generatedAtIndex.TryGetValue(group.FirstIndex, out var generatedAtFirst))
                {
                    generatedAtFirst = new List<AnimatorControllerLayer>();
                    generatedAtIndex.Add(group.FirstIndex, generatedAtFirst);
                }
                generatedAtFirst.Add(generatedLayer);
                groupNumber++;
            }

            foreach (var aap in selectedReports.Where(layer => layer.ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister))
            {
                var layers = BuildAapStateRegisterLayers(controller, aap, options, out var message);
                generatedAtIndex.Add(aap.Index, layers.ToList());
                generatedLayers.AddRange(layers);
                warnings.Add(message);
            }
            generatedLayers = generatedAtIndex.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).ToList();

            var oldLayers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            var selectedSet = new HashSet<int>(selected);
            var newLayers = new List<AnimatorControllerLayer>(oldLayers.Length - selected.Count + generatedLayers.Count);
            for (var index = 0; index < oldLayers.Length; index++)
            {
                if (generatedAtIndex.TryGetValue(index, out var generatedAtFirst))
                    newLayers.AddRange(generatedAtFirst);
                if (!selectedSet.Contains(index))
                    newLayers.Add(oldLayers[index]);
            }

            RewriteLayerIndexReferences(oldLayers, newLayers.ToArray(), options);
            controller.layers = newLayers.ToArray();
            var cleanupWarnings = CleanupControllerInPlace(controller, null, options);
            warnings.AddRange(cleanupWarnings);
            EditorUtility.SetDirty(controller);
            // Persist the grouped layer layout before returning the conversion result.
            AssetDatabase.SaveAssets();

            return new AnimatorOptimizerConversionResult(
                controller,
                generatedLayers,
                selected,
                upgradedParameters,
                warnings.Distinct(StringComparer.Ordinal));
        }

        public static AnimatorOptimizerConversionResult ConvertAapStateRegisterInPlace(
            AnimatorController controller,
            int layerIndex,
            AnimatorOptimizerOptions options = null)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            if (layerIndex < 0 || layerIndex >= report.Layers.Count)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            var layer = report.Layers[layerIndex];
            if (!layer.CanConvert || layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister)
                throw new InvalidOperationException($"Layer {layerIndex} 不是可转换的 AAP StateRegister Layer。");
            return ConvertInPlace(controller, new[] { layerIndex }, options);
        }

        private static IReadOnlyList<AnimatorControllerLayer> BuildAapStateRegisterLayers(
            AnimatorController controller,
            AnimatorOptimizerLayerReport report,
            AnimatorOptimizerOptions options,
            out string message)
        {
            var plan = report.Plan;
            if (plan == null || plan.Kind != AnimatorOptimizerConversionKind.AapStateRegister ||
                plan.StateRegisterStates == null || plan.StateRegisterProgram == null ||
                plan.StateRegisterStates.Count != plan.StateRegisterProgram.StateCount)
                throw new InvalidOperationException("AAP StateRegister 计划不完整。");

            var stateRegisterParameter = EnsureFloatParameterWithDefault(
                controller,
                "AO/StateRegister/" + report.Index,
                plan.StateRegisterProgram.DefaultState, options.UseVrChatParameterSemantics);
            var initializedParameter = !plan.StateRegisterProgram.RequiresInitialization
                ? null
                : EnsureFloatParameterWithDefault(
                    controller,
                    "AO/StateRegisterInitialized/" + report.Index,
                    0f, options.UseVrChatParameterSemantics);
            var updateTree = AnimatorOptimizerAapBuilder.BuildStateRegisterUpdate(controller, stateRegisterParameter,
                plan.StateRegisterProgram, initializedParameter, GeneratedAssetPrefix + "StateRegister_" + report.Index);
            var outputTree = BuildAapStateOutputTree(controller, stateRegisterParameter, plan.StateRegisterStates,
                GeneratedAssetPrefix + "StateOutput_" + report.Index);

            var generatedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            var updateStateMachine = CreateSingleStateMachine(
                controller,
                GeneratedAssetPrefix + "StateRegisterStateMachine_" + report.Index,
                GeneratedAssetPrefix + "StateRegister (WD On) " + report.Index,
                updateTree);
            var outputStateMachine = CreateSingleStateMachine(
                controller,
                GeneratedAssetPrefix + "StateOutputStateMachine_" + report.Index,
                GeneratedAssetPrefix + "StateOutput (WD On) " + report.Index,
                outputTree);
            updateStateMachine.defaultState.tag = plan.StateTag ?? string.Empty;
            outputStateMachine.defaultState.tag = plan.StateTag ?? string.Empty;
            outputStateMachine.behaviours = ClonePreservableLayerBehaviours(controller, new[] { report });
            var updateLayer = new AnimatorControllerLayer
            {
                name = generatedLayerName + "/AAPStateRegister_" + report.Index + "/Update",
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false,
                avatarMask = null,
                syncedLayerIndex = -1,
                stateMachine = updateStateMachine,
            };
            var outputLayer = new AnimatorControllerLayer
            {
                name = generatedLayerName + "/AAPStateRegister_" + report.Index + "/Output",
                defaultWeight = 1f,
                blendingMode = report.BlendingMode,
                iKPass = false,
                avatarMask = report.Mask,
                syncedLayerIndex = -1,
                stateMachine = outputStateMachine,
            };

            message = $"Layer {report.Index} 在原位置生成 AAP Update/Output 两层；" +
                $"{plan.StateRegisterProgram.DecisionNodeCount} 个共享条件节点，StateRegister={stateRegisterParameter}。" +
                (initializedParameter == null ? string.Empty : $" Entry 只初始化一次；全部分支持续写 {initializedParameter}=1。");
            // Assignment, reference remapping and cleanup are performed once after
            // all regular/AAP replacements have been prepared from the source plan.
            return new[] { updateLayer, outputLayer };
        }

        public static IReadOnlyList<string> CleanupControllerInPlace(
            AnimatorController controller,
            Transform bindingRoot,
            AnimatorOptimizerOptions options = null)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var messages = new List<string>();
            var sourceLayers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            var invalidReferences = GetMalformedLayerReferenceIssues(sourceLayers, options);
            if (invalidReferences.Count > 0)
                return invalidReferences.Select(issue => "未执行清理：" + issue).ToArray();
            var hasUnknownLayerControls = !options.HasKnownPlayableLayer &&
                EnumerateLayerControlsFromLayers(sourceLayers).Any();
            if (hasUnknownLayerControls && options.PreserveMmdCompatibility)
                throw new InvalidOperationException("启用 MMD 兼容时必须知道目标 Playable Layer，才能安全重排 VRCAnimatorLayerControl。");
            Undo.RegisterCompleteObjectUndo(controller, "Animator Optimizer Cleanup");
            EnsureOwnedMutableGraph(controller);
            var oldLayersForReferences = (controller.layers ?? Array.Empty<AnimatorControllerLayer>()).ToArray();
            // Keep these wrappers until the final assignment. controller.layers
            // reconstructs wrappers, and stateMachine is not a unique layer identity.
            var workingLayers = oldLayersForReferences.ToList();
            var protectedLayerIndices = GetProtectedLayerIndices(oldLayersForReferences, options);
            var protectedMachines = protectedLayerIndices.Where(index => index >= 0 && index < oldLayersForReferences.Length)
                .Select(index => oldLayersForReferences[index]?.stateMachine).ToHashSet();
            if (options.MergeSynchronizedStateMachines && !hasUnknownLayerControls)
                MergeSynchronizedLayersInPlace(controller, workingLayers, protectedLayerIndices, messages, options);
            if (options.NormalizeAlwaysOneParameters)
                NormalizeAlwaysOneParameters(controller, messages);
            if (options.RemoveEffectivelyEmptyLayers && !hasUnknownLayerControls)
            {
                var kept = new List<AnimatorControllerLayer>(workingLayers.Count);
                for (var index = 0; index < workingLayers.Count; index++)
                {
                    var layer = workingLayers[index];
                    var preserveSlot = index == 0 || layer != null &&
                        (string.Equals(layer.name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                         string.Equals(layer.name, "MMD Dummy Layer 2", StringComparison.Ordinal));
                    var generated = layer != null &&
                        (IsGeneratedLayerName(layer.name, options.GeneratedLayerName) ||
                         layer.name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal));
                    if (!preserveSlot && !generated && !protectedMachines.Contains(layer?.stateMachine) &&
                        IsEffectivelyEmptyLayer(layer, bindingRoot))
                    {
                        messages.Add($"删除无可观测输出的空 Layer {index} ({layer?.name ?? "<null>"})。");
                        continue;
                    }
                    kept.Add(layer);
                }
                workingLayers = kept;
            }

            var normalizedEmptyMotions = NormalizeEffectivelyEmptyMotions(controller, bindingRoot, workingLayers, oldLayersForReferences);

            if (options.PreserveMmdCompatibility && !options.HasKnownPlayableLayer &&
                EnumerateLayerControls(controller).Any())
                throw new InvalidOperationException("启用 MMD 兼容时必须知道目标 Playable Layer，才能安全重映射 VRCAnimatorLayerControl。");
            if (options.PreserveMmdCompatibility)
                EnsureMmdCompatibilityLayers(controller, workingLayers);
            var finalLayers = workingLayers.ToArray();
            RewriteLayerIndexReferences(oldLayersForReferences, finalLayers, options);
            controller.layers = finalLayers;
            if (messages.Count > 0 || normalizedEmptyMotions || options.PreserveMmdCompatibility)
                EditorUtility.SetDirty(controller);
            return messages;
        }

        private static HashSet<int> GetProtectedLayerIndices(
            IReadOnlyList<AnimatorControllerLayer> layers,
            AnimatorOptimizerOptions options)
        {
            var protectedIndices = new HashSet<int>();
            foreach (var layer in layers)
                if (layer != null && layer.syncedLayerIndex >= 0)
                    protectedIndices.Add(layer.syncedLayerIndex);
            var controls = EnumerateLayerControlsFromLayers(layers).ToList();
            foreach (var control in controls)
            {
                if (!options.HasKnownPlayableLayer || AppliesToTargetPlayable(control, options))
                    protectedIndices.Add(control.LayerIndex);
            }
            return protectedIndices;
        }

        private static IEnumerable<IAnimatorOptimizerLayerControl> EnumerateLayerControlsFromLayers(
            IReadOnlyList<AnimatorControllerLayer> layers)
        {
            var visited = new HashSet<int>();
            foreach (var layer in layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                foreach (var control in EnumerateLayerControls(layer?.stateMachine, visited))
                    yield return control;
                if (layer == null || layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Count) continue;
                foreach (var state in AnimatorOptimizerAnalyzer.EnumerateLayerSourceStates(layers, layer.syncedLayerIndex))
                    foreach (var control in (layer.GetOverrideBehaviours(state) ?? Array.Empty<StateMachineBehaviour>()).Select(AnimatorOptimizerPlatform.GetLayerControl).Where(control => control != null))
                        yield return control;
            }
        }

        private static void RewriteLayerIndexReferences(
            IReadOnlyList<AnimatorControllerLayer> oldLayers,
            IReadOnlyList<AnimatorControllerLayer> newLayers,
            AnimatorOptimizerOptions options)
        {
            if (oldLayers == null || newLayers == null)
                return;
            var oldToNew = new Dictionary<int, int>();
            for (var oldIndex = 0; oldIndex < oldLayers.Count; oldIndex++)
            {
                if (oldLayers[oldIndex] == null) continue;
                for (var newIndex = 0; newIndex < newLayers.Count; newIndex++)
                    if (ReferenceEquals(oldLayers[oldIndex], newLayers[newIndex]))
                    {
                        oldToNew.Add(oldIndex, newIndex);
                        break;
                    }
            }

            // Capture override behaviours using OLD synced indices before remapping.
            var controls = EnumerateLayerControlsFromLayers(oldLayers).GroupBy(control => control.Behaviour).Select(group => group.First()).ToList();
            var syncedUpdates = new List<(AnimatorControllerLayer Layer, int Index)>();
            var controlUpdates = new List<(IAnimatorOptimizerLayerControl Control, int Index)>();
            foreach (var layer in newLayers)
            {
                if (layer != null && layer.syncedLayerIndex >= 0)
                {
                    if (oldToNew.TryGetValue(layer.syncedLayerIndex, out var newSyncedIndex))
                        syncedUpdates.Add((layer, newSyncedIndex));
                    else
                        throw new InvalidOperationException("待保留 Synced Layer 的源层丢失，拒绝猜测索引映射。");
                }
            }
            foreach (var control in controls)
            {
                if (!options.HasKnownPlayableLayer || !AppliesToTargetPlayable(control, options)) continue;
                if (oldToNew.TryGetValue(control.LayerIndex, out var newIndex))
                    controlUpdates.Add((control, newIndex));
                else throw new InvalidOperationException("LayerControl 的目标层丢失，拒绝猜测索引映射。");
            }
            // Validate the complete map before modifying any reference.
            foreach (var update in syncedUpdates) update.Layer.syncedLayerIndex = update.Index;
            foreach (var update in controlUpdates)
            {
                if (update.Control.LayerIndex == update.Index) continue;
                Undo.RecordObject(update.Control.Behaviour, "Animator Optimizer Layer References");
                update.Control.LayerIndex = update.Index;
                EditorUtility.SetDirty(update.Control.Behaviour);
            }
        }

        public static bool IsEffectivelyEmptyClip(AnimationClip clip, Transform bindingRoot = null)
        {
            if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip))
                return false;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null)
                return false;
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (floatBindings.Length == 0 && objectBindings.Length == 0)
                return true;
            foreach (var binding in floatBindings)
            {
                if (!IsEffectivelyEmptyBinding(binding, bindingRoot))
                    return false;
            }
            foreach (var binding in objectBindings)
            {
                if (!IsEffectivelyEmptyBinding(binding, bindingRoot))
                    return false;
            }
            return true;
        }

        private static bool IsEffectivelyEmptyLayer(
            AnimatorControllerLayer layer,
            Transform bindingRoot)
        {
            if (layer == null || layer.stateMachine == null || layer.iKPass ||
                layer.syncedLayerIndex >= 0)
                return false;
            return IsEffectivelyEmptyStateMachine(layer.stateMachine, bindingRoot);
        }

        private static bool NormalizeEffectivelyEmptyMotions(
            AnimatorController controller,
            Transform bindingRoot,
            IReadOnlyList<AnimatorControllerLayer> layers,
            IReadOnlyList<AnimatorControllerLayer> sourceLayers)
        {
            if (controller == null)
                return false;
            // An empty motion can still provide timing for a synced override, or
            // for another machine sharing this State. Keep those time bases intact.
            var protectedStates = sourceLayers.SelectMany(layer =>
                    EnumerateSynchronizedStates(layer?.stateMachine, new HashSet<AnimatorStateMachine>()))
                .Where(state => state != null).GroupBy(state => state).Where(group => group.Count() > 1)
                .Select(group => group.Key).ToHashSet();
            for (var index = 0; index < sourceLayers.Count; index++)
                if (sourceLayers[index]?.syncedLayerIndex >= 0)
                    protectedStates.UnionWith(AnimatorOptimizerAnalyzer.EnumerateLayerSourceStates(sourceLayers, index));
            AnimationClip canonical = FindCanonicalEmptyClip(controller);
            var changed = false;
            foreach (var layer in layers)
            {
                if (layer == null || layer.stateMachine == null ||
                    EnumerateSynchronizedStates(layer.stateMachine, new HashSet<AnimatorStateMachine>()).Any(protectedStates.Contains) ||
                    !CanNormalizeEmptyStateMachine(layer.stateMachine))
                    continue;
                changed |= ReplaceEmptyMotions(layer.stateMachine, bindingRoot, controller, ref canonical);
            }
            return changed;
        }

        private static bool CanNormalizeEmptyStateMachine(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null || stateMachine.behaviours != null && stateMachine.behaviours.Length > 0 ||
                HasTimeSensitiveTransitions(stateMachine))
                return false;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null || child.state.behaviours != null && child.state.behaviours.Length > 0 ||
                    child.state.iKOnFeet ||
                    (child.state.mirror || child.state.mirrorParameterActive) &&
                        !AnimatorOptimizerMotionUtility.IsPropertyOnly(child.state.motion) ||
                    (child.state.speedParameterActive || child.state.cycleOffsetParameterActive || child.state.timeParameterActive) &&
                        !AnimatorOptimizerMotionUtility.IsTimeIndependent(child.state.motion))
                    return false;
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                if (child.stateMachine == null ||
                    stateMachine.GetStateMachineTransitions(child.stateMachine).Length > 0)
                    return false;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .All(child => CanNormalizeEmptyStateMachine(child.stateMachine));
        }

        private static bool HasTimeSensitiveTransitions(AnimatorStateMachine stateMachine)
        {
            if (stateMachine.entryTransitions != null && stateMachine.entryTransitions.Any(HasTimeSensitiveTransition))
                return true;
            if (stateMachine.anyStateTransitions != null && stateMachine.anyStateTransitions.Any(HasTimeSensitiveTransition))
                return true;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null && child.state.transitions != null &&
                    child.state.transitions.Any(HasTimeSensitiveTransition))
                    return true;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .Any(child => stateMachine.GetStateMachineTransitions(child.stateMachine).Length > 0 ||
                    HasTimeSensitiveTransitions(child.stateMachine));
        }

        private static bool HasTimeSensitiveTransition(AnimatorTransitionBase transition)
        {
            if (transition == null)
                return false;
            if (transition is AnimatorStateTransition stateTransition)
                return stateTransition.hasExitTime || stateTransition.duration != 0f ||
                       stateTransition.offset != 0f;
            return false;
        }

        private static bool ReplaceEmptyMotions(
            AnimatorStateMachine stateMachine,
            Transform bindingRoot,
            AnimatorController controller,
            ref AnimationClip canonical)
        {
            if (stateMachine == null)
                return false;
            var changed = false;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                var state = child.state;
                if (state == null)
                    continue;
                if (state.motion is AnimationClip clip && IsEffectivelyEmptyClip(clip, bindingRoot))
                {
                    canonical ??= CreateCanonicalEmptyClip(controller);
                    if (!ReferenceEquals(state.motion, canonical))
                    {
                        Undo.RecordObject(state, "Animator Optimizer Empty Motion");
                        state.motion = canonical;
                        EditorUtility.SetDirty(state);
                        changed = true;
                    }
                }
                else if (state.motion is BlendTree tree &&
                         IsEffectivelyEmptyMotion(tree, bindingRoot, new HashSet<int>()))
                {
                    canonical ??= CreateCanonicalEmptyClip(controller);
                    Undo.RecordObject(state, "Animator Optimizer Empty Motion");
                    state.motion = canonical;
                    EditorUtility.SetDirty(state);
                    changed = true;
                }
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                changed |= ReplaceEmptyMotions(child.stateMachine, bindingRoot, controller, ref canonical);
            return changed;
        }

        private static AnimationClip CreateCanonicalEmptyClip(AnimatorController controller)
        {
            var clip = new AnimationClip
            {
                name = GeneratedAssetPrefix + "CanonicalEmpty",
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, clip);
            return clip;
        }

        private static AnimationClip FindCanonicalEmptyClip(AnimatorController controller)
        {
            if (controller == null)
                return null;
            var path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => string.Equals(
                    clip.name, GeneratedAssetPrefix + "CanonicalEmpty", StringComparison.Ordinal) &&
                    IsEffectivelyEmptyClip(clip));
        }

        private static bool IsEffectivelyEmptyStateMachine(
            AnimatorStateMachine stateMachine,
            Transform bindingRoot)
        {
            if (stateMachine == null ||
                (stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>()).Length > 0 ||
                (stateMachine.entryTransitions ?? Array.Empty<AnimatorTransition>()).Length > 0 ||
                (stateMachine.anyStateTransitions ?? Array.Empty<AnimatorStateTransition>()).Length > 0)
                return false;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null || (child.state.behaviours ?? Array.Empty<StateMachineBehaviour>()).Length > 0 ||
                    (child.state.transitions ?? Array.Empty<AnimatorStateTransition>()).Length > 0 ||
                    child.state.writeDefaultValues || child.state.iKOnFeet ||
                    !AnimatorOptimizerAnalyzer.IsFinite(child.state.speed) ||
                    !AnimatorOptimizerAnalyzer.IsFinite(child.state.cycleOffset) ||
                    (child.state.mirror || child.state.mirrorParameterActive) &&
                        !AnimatorOptimizerMotionUtility.IsPropertyOnly(child.state.motion) ||
                    !IsEffectivelyEmptyMotion(child.state.motion, bindingRoot, new HashSet<int>()))
                    return false;
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                if (child.stateMachine == null ||
                    stateMachine.GetStateMachineTransitions(child.stateMachine).Length > 0)
                    return false;
                if (!IsEffectivelyEmptyStateMachine(child.stateMachine, bindingRoot))
                    return false;
            }
            return true;
        }

        private static bool IsEffectivelyEmptyMotion(
            Motion motion,
            Transform bindingRoot,
            HashSet<int> visited)
        {
            if (motion == null)
                return false; // WD Off null Motion can reset preceding layers; it is not an empty clip.
            if (!visited.Add(motion.GetInstanceID()))
                return false;
            if (motion is AnimationClip clip)
                return IsEffectivelyEmptyClip(clip, bindingRoot);
            if (motion is BlendTree tree)
                return (tree.children ?? Array.Empty<ChildMotion>()).All(child =>
                    IsEffectivelyEmptyMotion(child.motion, bindingRoot, new HashSet<int>(visited)));
            return false;
        }

        private static bool IsEffectivelyEmptyBinding(
            EditorCurveBinding binding,
            Transform bindingRoot)
        {
            if (binding.type == typeof(Animator))
                return false; // AAP is observable controller logic, never an empty property.
            if (bindingRoot == null)
                return false;
            var target = string.IsNullOrEmpty(binding.path)
                ? bindingRoot.gameObject
                : bindingRoot.Find(binding.path)?.gameObject;
            if (target == null)
                return true;
            if (binding.type == typeof(GameObject))
                return false;
            if (typeof(Component).IsAssignableFrom(binding.type))
                return target.GetComponent(binding.type) == null;
            return false;
        }

        private static void EnsureMmdCompatibilityLayers(AnimatorController controller, List<AnimatorControllerLayer> layers)
        {
            var dummy1 = layers.Skip(1).FirstOrDefault(layer => IsReusableMmdDummyLayer(layer) &&
                string.Equals(layer.name, "MMD Dummy Layer 1", StringComparison.Ordinal));
            var dummy2 = layers.Skip(1).FirstOrDefault(layer => IsReusableMmdDummyLayer(layer) &&
                string.Equals(layer.name, "MMD Dummy Layer 2", StringComparison.Ordinal));
            if (dummy1 != null) layers.Remove(dummy1);
            if (dummy2 != null) layers.Remove(dummy2);
            if (layers.Count == 0)
                layers.Add(CreateMmdDummyLayer(controller, "Base Layer", 1f));
            dummy1 ??= CreateMmdDummyLayer(controller, "MMD Dummy Layer 1", 0f);
            dummy2 ??= CreateMmdDummyLayer(controller, "MMD Dummy Layer 2", 0f);
            dummy1.defaultWeight = 0f;
            dummy2.defaultWeight = 0f;
            layers.Insert(Math.Min(1, layers.Count), dummy1);
            layers.Insert(Math.Min(2, layers.Count), dummy2);
        }

        private static bool IsReusableMmdDummyLayer(AnimatorControllerLayer layer) => layer?.stateMachine != null &&
            layer.defaultWeight == 0f && layer.avatarMask == null && layer.blendingMode == AnimatorLayerBlendingMode.Override &&
            layer.syncedLayerIndex < 0 && !layer.iKPass && layer.stateMachine.behaviours.Length == 0 &&
            layer.stateMachine.states.Length == 0 && layer.stateMachine.stateMachines.Length == 0 &&
            layer.stateMachine.entryTransitions.Length == 0 && layer.stateMachine.anyStateTransitions.Length == 0;

        private static AnimatorControllerLayer CreateMmdDummyLayer(
            AnimatorController controller,
            string name,
            float weight)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = name + " StateMachine",
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, stateMachine);
            return new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = weight,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false,
                syncedLayerIndex = -1,
                stateMachine = stateMachine,
            };
        }

        private static void NormalizeAlwaysOneParameters(
            AnimatorController controller,
            ICollection<string> messages)
        {
            messages?.Add("已显式假定 AlwaysOne 候选在运行时恒为 1；名称和默认值不能证明没有外部写入。若此约定不成立，请关闭统一 AlwaysOne 参数。");
            var parameters = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            var animated = CollectAnimatedAnimatorParameterNames(controller);
            var candidates = parameters
                .Where(parameter => parameter.type == AnimatorControllerParameterType.Float &&
                    Mathf.Approximately(parameter.defaultFloat, 1f) &&
                    IsAlwaysOneName(parameter.name) &&
                    !animated.Contains(parameter.name) &&
                    !HasPotentialParameterDriverFor(controller, parameter.name) &&
                    !HasPotentialBehaviourReferenceFor(controller, parameter.name))
                .OrderBy(parameter => IsCanonicalAlwaysOneName(parameter.name) ? 0 : 1)
                .ThenBy(parameter => parameter.name.Length)
                .ThenBy(parameter => parameter.name, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count < 2)
                return;
            var canonical = candidates[0].name;
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var candidate in candidates.Skip(1))
                aliases[candidate.name] = canonical;
            foreach (var layer in controller.layers ?? Array.Empty<AnimatorControllerLayer>())
                RewriteAlwaysOneMotion(layer?.stateMachine, aliases);
            messages?.Add($"将 {aliases.Count} 个 AlwaysOne 参数的 Direct BlendTree 引用统一到 '{canonical}'。");
        }

        private static HashSet<string> CollectAnimatedAnimatorParameterNames(AnimatorController controller)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<int>();
            var layers = controller?.layers ?? Array.Empty<AnimatorControllerLayer>();
            for (var index = 0; index < layers.Length; index++)
                CollectAnimatedAnimatorParameterNames(layers, index, names, visited);
            return names;
        }

        private static void CollectAnimatedAnimatorParameterNames(IReadOnlyList<AnimatorControllerLayer> layers,
            int index, ISet<string> names, ISet<int> visited)
        {
            var states = AnimatorOptimizerAnalyzer.EnumerateLayerSourceStates(layers, index).ToList();
            foreach (var state in states) CollectAnimatedAnimatorParameterNames(state.motion, names, visited);
            var chain = new HashSet<int>();
            while (index >= 0 && index < layers.Count && chain.Add(index))
            {
                var layer = layers[index];
                if (layer == null || layer.syncedLayerIndex < 0) break;
                foreach (var state in states)
                    CollectAnimatedAnimatorParameterNames(layer.GetOverrideMotion(state), names, visited);
                index = layer.syncedLayerIndex;
            }
        }

        private static IEnumerable<StateMachineBehaviour> EnumerateSyncedOverrideBehaviours(AnimatorController controller)
        {
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                if (layer == null || layer.syncedLayerIndex < 0) continue;
                foreach (var state in AnimatorOptimizerAnalyzer.EnumerateLayerSourceStates(layers, index))
                    foreach (var behaviour in layer.GetOverrideBehaviours(state) ?? Array.Empty<StateMachineBehaviour>())
                        yield return behaviour;
            }
        }

        private static void CollectAnimatedAnimatorParameterNames(
            AnimatorStateMachine stateMachine,
            ISet<string> names,
            ISet<int> visited)
        {
            if (stateMachine == null)
                return;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null)
                    CollectAnimatedAnimatorParameterNames(child.state.motion, names, visited);
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                CollectAnimatedAnimatorParameterNames(child.stateMachine, names, visited);
        }

        private static void CollectAnimatedAnimatorParameterNames(
            Motion motion,
            ISet<string> names,
            ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return;
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    if (binding.type == typeof(Animator))
                        names.Add(binding.propertyName);
                }
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children ?? Array.Empty<ChildMotion>())
                    CollectAnimatedAnimatorParameterNames(child.motion, names, visited);
            }
        }

        private static void RewriteAlwaysOneMotion(
            AnimatorStateMachine stateMachine,
            IReadOnlyDictionary<string, string> aliases)
        {
            if (stateMachine == null)
                return;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null)
                    RewriteAlwaysOneMotion(child.state.motion, aliases);
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                RewriteAlwaysOneMotion(child.stateMachine, aliases);
        }

        private static void RewriteAlwaysOneMotion(
            Motion motion,
            IReadOnlyDictionary<string, string> aliases)
        {
            if (!(motion is BlendTree tree))
                return;
            var canonical = string.Empty;
            var changed = false;
            var children = tree.children ?? Array.Empty<ChildMotion>();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrWhiteSpace(child.directBlendParameter) &&
                    aliases.TryGetValue(child.directBlendParameter, out canonical))
                {
                    child.directBlendParameter = canonical;
                    changed = true;
                }
                children[index] = child;
                RewriteAlwaysOneMotion(child.motion, aliases);
            }
            if (changed)
            {
                Undo.RecordObject(tree, "Animator Optimizer AlwaysOne");
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
        }

        private static bool IsAlwaysOneName(string name)
        {
            var compact = new string((name ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToLowerInvariant();
            return compact.Contains("alwaysone", StringComparison.Ordinal) ||
                     compact.Contains("always1", StringComparison.Ordinal) ||
                     compact.EndsWith("modularavatarinternalone", StringComparison.Ordinal) ||
                     compact == "vrcfdbtalwaysone";
        }

        private static bool IsCanonicalAlwaysOneName(string name)
        {
            var compact = new string((name ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToLowerInvariant();
                 return compact == "alwaysone" || compact.EndsWith("internalalwaysone", StringComparison.Ordinal) ||
                     compact.EndsWith("modularavatarinternalone", StringComparison.Ordinal) ||
                     compact == "vrcfdbtalwaysone";
        }

        private static AnimatorStateMachine CreateSingleStateMachine(
            AnimatorController controller,
            string name,
            string stateName,
            Motion motion)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = name,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var state = stateMachine.AddState(stateName, new Vector3(320f, 80f, 0f));
            state.motion = motion;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            AddSubAsset(controller, stateMachine);
            return stateMachine;
        }

        private static BlendTree BuildAapStateOutputTree(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<AnimatorState> states,
            string name)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = stateRegisterParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = states.Select((state, index) =>
            {
                var settings = AnimatorOptimizerAnalyzer.ToMotionRef(state);
                var motion = settings.Motion;
                AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(motion, out motion);
                return new ChildMotion
                {
                    motion = motion,
                    threshold = index,
                    timeScale = settings.Speed,
                    cycleOffset = settings.CycleOffset,
                    mirror = settings.Mirror,
                };
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        private static string EnsureFloatParameterWithDefault(
            AnimatorController controller,
            string prefix,
            float defaultValue, bool useVrChatParameterSemantics)
        {
            prefix = string.IsNullOrWhiteSpace(prefix) ? "AO/StateRegister" : prefix.Trim();
            var name = AnimatorOptimizerSemanticUtility.AllocateParameterName(prefix, CollectReservedParameterNames(controller), useVrChatParameterSemantics);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue,
            });
            return name;
        }

        public static AnimatorController CreateOptimizedCopy(
            AnimatorController source,
            string outputPath,
            IEnumerable<int> selectedLayerIndices,
            AnimatorOptimizerOptions options = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("输出路径不能为空。", nameof(outputPath));

            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new InvalidOperationException("源 AnimatorController 必须是 Assets/ 下已保存的资源。");

            outputPath = outputPath.Replace('\\', '/');
            if (outputPath.Split('/').Any(segment => segment == "." || segment == ".." || string.IsNullOrWhiteSpace(segment)))
                throw new InvalidOperationException("输出路径不能包含空目录、. 或 ..。");
            if (!outputPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !outputPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("输出路径必须位于 Assets/ 下，并以 .controller 结尾。");
            }
            if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("输出路径不能覆盖源 Controller。");

            EnsureFolder(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
            if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                throw new InvalidOperationException("输出 Controller 已存在，请先删除或换一个路径。\n" + outputPath);
            if (!AssetDatabase.CopyAsset(sourcePath, outputPath))
                throw new InvalidOperationException("复制 AnimatorController 失败: " + sourcePath);

            try
            {
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
                var copy = LoadCopiedController(source, outputPath);
                var selection = selectedLayerIndices?.ToList() ?? new List<int>();
                if (selection.Count == 0 && GetSynchronizedLayerGroups(copy, options).Count > 0)
                {
                    CleanupControllerInPlace(copy, null, options);
                    AssetDatabase.SaveAssets();
                }
                else
                    ConvertInPlace(copy, selection, options);
                return copy;
            }
            catch
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                    AssetDatabase.DeleteAsset(outputPath);
                throw;
            }
        }

        internal static AnimatorController LoadCopiedController(AnimatorController source, string copyPath)
        {
            // CopyAsset duplicates the whole serialized file, which may contain several
            // controllers (including equal names). File-local identity survives the copy;
            // LoadAssetAtPath<T> only chooses the first matching type in that container.
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long sourceId))
                throw new InvalidOperationException("Unable to identify the source AnimatorController sub-asset.");
            var matches = AssetDatabase.LoadAllAssetsAtPath(copyPath).OfType<AnimatorController>()
                .Where(candidate => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate,
                    out string _, out long candidateId) && candidateId == sourceId).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Unable to uniquely locate the copied AnimatorController by local file ID: " + copyPath);
            return matches[0];
        }

        // Controller copies still reference external trees, states and behaviours.
        internal static bool EnsureOwnedMutableGraph(AnimatorController controller)
        {
            if (!AnimatorOptimizerAnalyzer.IsValidControllerGraph(controller))
                throw new InvalidOperationException("Controller 图结构无效，不能修改。");
            var path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AnimatorController 必须先保存到 Assets/；Package 资源需要先复制再修改。");
            var owned = new Dictionary<Object, Object>();
            var pending = new Queue<Object>();
            var changed = false;

            Object Own(Object source)
            {
                if (source == null)
                    return null;
                if (owned.TryGetValue(source, out var previous))
                    return previous;
                var destination = source;
                if (source != controller && !string.Equals(AssetDatabase.GetAssetPath(source), path, StringComparison.OrdinalIgnoreCase))
                {
                    destination = CreateMutableGraphNode(source);
                    EditorUtility.CopySerialized(source, destination);
                    destination.name = source.name;
                    AddSubAsset(controller, destination);
                    changed = true;
                }
                owned.Add(source, destination);
                pending.Enqueue(destination);
                return destination;
            }

            Own(controller);
            while (pending.Count > 0)
            {
                var destination = pending.Dequeue();
                Undo.RecordObject(destination, "Animator Optimizer Own Graph");
                using (var serialized = new SerializedObject(destination))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        var reference = property.objectReferenceValue;
                        if (reference is not AnimatorStateMachine && reference is not AnimatorState &&
                            reference is not AnimatorTransitionBase && reference is not BlendTree &&
                            reference is not StateMachineBehaviour)
                            continue;
                        var replacement = Own(reference);
                        if (replacement != reference)
                            property.objectReferenceValue = replacement;
                    }
                    if (serialized.ApplyModifiedPropertiesWithoutUndo())
                        EditorUtility.SetDirty(destination);
                }
            }
            return changed;
        }

        private static Object CreateMutableGraphNode(Object source)
        {
            // Animator graph objects carry editor-native strong references. Unity's
            // generic Instantiate path cannot clone them safely (it asserts on
            // kStrongPPtrMask in 2022.3). Construct their native backing objects first;
            // CopySerialized and the ownership map then copy/remap the graph.
            return source switch
            {
                AnimatorStateMachine _ => new AnimatorStateMachine(),
                AnimatorState _ => new AnimatorState(),
                AnimatorStateTransition _ => new AnimatorStateTransition(),
                AnimatorTransition _ => new AnimatorTransition(),
                BlendTree _ => new BlendTree(),
                // Behaviours are ordinary ScriptableObjects; retain Instantiate's
                // serialized initialization/OnEnable ordering for custom scripts.
                StateMachineBehaviour behaviour => Object.Instantiate(behaviour),
                _ => throw new InvalidOperationException("Unsupported mutable Animator graph object: " + source.GetType().FullName),
            };
        }

        private sealed class BuiltMotion
        {
            public Motion Motion;
            public bool HasStateSettings;
            public float Speed = 1f;
            public float CycleOffset;
            public bool Mirror;
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

        private static List<LayerGroup> BuildLayerGroups(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            return reports
                .GroupBy(report => new
                {
                    report.BlendingMode,
                    report.MaskKey,
                    StateTag = report.Plan?.StateTag ?? string.Empty,
                })
                .Select(group => new LayerGroup
                {
                    BlendingMode = group.First().BlendingMode,
                    MaskKey = group.First().MaskKey,
                    Mask = group.First().Mask,
                    StateTag = group.Key.StateTag,
                    Reports = group.OrderBy(report => report.Index).ToList(),
                })
                .OrderBy(group => group.FirstIndex)
                .ToList();
        }

        private static bool CanPreserveLayerBehaviours(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            if (reports == null || reports.Count == 0)
                return true;
            var behaviourKeys = reports.Select(report =>
                    AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(
                        report.SourceLayer?.stateMachine?.behaviours ?? Array.Empty<StateMachineBehaviour>(), out var key) ? key : null)
                .ToList();
            return behaviourKeys.All(key => key != null && string.Equals(key, behaviourKeys[0], StringComparison.Ordinal));
        }

        private static StateMachineBehaviour[] ClonePreservableLayerBehaviours(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            if (reports == null || reports.Count == 0)
                return Array.Empty<StateMachineBehaviour>();
            if (!CanPreserveLayerBehaviours(reports))
                throw new InvalidOperationException("无法完整复制源层标记，拒绝丢弃 Behaviour。");
            var source = reports[0].SourceLayer?.stateMachine?.behaviours ?? Array.Empty<StateMachineBehaviour>();
            return source.Select(behaviour =>
            {
                var clone = UnityEngine.Object.Instantiate(behaviour);
                AddSubAsset(controller, clone);
                return clone;
            }).ToArray();
        }

        private static bool HasInterleavedConflictingGroups(IReadOnlyList<LayerGroup> groups)
        {
            for (var leftIndex = 0; leftIndex < groups.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < groups.Count; rightIndex++)
                {
                    var left = groups[leftIndex];
                    var right = groups[rightIndex];
                    if (!left.Reports.SelectMany(report => report.Bindings)
                        .Intersect(right.Reports.SelectMany(report => report.Bindings)).Any())
                        continue;
                    if (GroupsInterleave(left.Reports.Select(report => report.Index), right.Reports.Select(report => report.Index)))
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

        private static bool HasAnyCurveOverlap(IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            for (var leftIndex = 0; leftIndex < reports.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < reports.Count; rightIndex++)
                {
                    if (reports[leftIndex].Bindings.Intersect(reports[rightIndex].Bindings).Any())
                        return true;
                }
            }
            return false;
        }

        private static List<AnimatorOptimizerLayerReport> GetUniqueReports(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            var unique = new List<AnimatorOptimizerLayerReport>();
            foreach (var report in reports.OrderBy(item => item.Index))
            {
                if (report.BlendingMode == AnimatorLayerBlendingMode.Additive ||
                    !CanDeduplicatePlan(report.Plan) ||
                    !unique.Any(existing => CanDeduplicatePlan(existing.Plan) &&
                        CanDeduplicatePlan(report.Plan) &&
                        ArePlansStructurallyEquivalent(existing.Plan, report.Plan) &&
                        string.Equals(existing.MaskKey, report.MaskKey, StringComparison.Ordinal)))
                    unique.Add(report);
            }
            return unique;
        }

        private static bool CanDeduplicatePlan(AnimatorOptimizerMotionPlan plan)
        {
            return EnumeratePlanMotions(plan).All(motion =>
                motion?.Motion == null ||
                !MotionHasAnimationEvents(motion.Motion, new HashSet<int>()) &&
                !MotionHasAdditiveReferencePose(motion.Motion, new HashSet<int>()));
        }

        private static bool MotionHasAnimationEvents(Motion motion, ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return false;
            if (motion is AnimationClip clip)
                return clip.events != null && clip.events.Length > 0;
            return motion is BlendTree tree && (tree.children ?? Array.Empty<ChildMotion>())
                .Any(child => MotionHasAnimationEvents(child.motion, visited));
        }

        private static bool MotionHasAdditiveReferencePose(Motion motion, ISet<int> visited)
        {
            if (motion == null || !visited.Add(motion.GetInstanceID()))
                return false;
            if (motion is AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                return settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null;
            }
            return motion is BlendTree tree && (tree.children ?? Array.Empty<ChildMotion>())
                .Any(child => MotionHasAdditiveReferencePose(child.motion, visited));
        }

        private static bool ArePlansStructurallyEquivalent(
            AnimatorOptimizerMotionPlan left,
            AnimatorOptimizerMotionPlan right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Kind != right.Kind)
                return false;
            if (left.Kind == AnimatorOptimizerConversionKind.ConstantMotion)
                return AreMotionRefsStructurallyEquivalent(left.ConstantMotion, right.ConstantMotion);
            if (left.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
                return SequenceEqual(left.BooleanParameters, right.BooleanParameters) &&
                       AreMotionRefTablesStructurallyEquivalent(left.TruthTable, right.TruthTable);
            if (left.Kind == AnimatorOptimizerConversionKind.NumericSelector)
                return string.Equals(left.SelectorParameter, right.SelectorParameter, StringComparison.Ordinal) &&
                       left.SelectorParameterType == right.SelectorParameterType &&
                       left.SelectorIsApproximate == right.SelectorIsApproximate &&
                       (left.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Count ==
                       (right.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Count &&
                       (left.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Zip(
                           right.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>(),
                           (a, b) => a.Value == b.Value &&
                               AreMotionRefsStructurallyEquivalent(a.Motion, b.Motion)).All(value => value);
            if (left.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
                return AreDecisionDimensionsEquivalent(left.DecisionDimensions, right.DecisionDimensions) &&
                       AreMotionRefTablesStructurallyEquivalent(left.DecisionTable, right.DecisionTable);
            return string.Equals(left.StructuralKey, right.StructuralKey, StringComparison.Ordinal);
        }

        private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            return (left ?? new List<string>()).SequenceEqual(right ?? new List<string>(), StringComparer.Ordinal);
        }

        private static bool AreMotionRefTablesStructurallyEquivalent(
            IReadOnlyDictionary<int, AnimatorOptimizerMotionRef> left,
            IReadOnlyDictionary<int, AnimatorOptimizerMotionRef> right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            return left.Count == right.Count && left.All(pair =>
                right.TryGetValue(pair.Key, out var other) &&
                AreMotionRefsStructurallyEquivalent(pair.Value, other));
        }

        private static bool AreDecisionDimensionsEquivalent(
            IReadOnlyList<AnimatorOptimizerDecisionDimension> left,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            return left.Count == right.Count && left.Zip(right, (a, b) =>
                string.Equals(a.Parameter, b.Parameter, StringComparison.Ordinal) &&
                a.ParameterType == b.ParameterType &&
                a.IsApproximate == b.IsApproximate &&
                a.Values.SequenceEqual(b.Values)).All(value => value);
        }

        private static bool AreMotionRefsStructurallyEquivalent(
            AnimatorOptimizerMotionRef left,
            AnimatorOptimizerMotionRef right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            if (left.Bake != null || right.Bake != null)
                return left.Bake != null && right.Bake != null && left.Bake.Key == right.Bake.Key;
            return left.Speed == right.Speed &&
                   left.CycleOffset == right.CycleOffset &&
                   left.Mirror == right.Mirror &&
                   AnimatorOptimizerMotionUtility.AreStructurallyEquivalent(left.Motion, right.Motion);
        }

        private static bool CanResolveLayerOverlap(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            AnimatorOptimizerOptions options)
        {
            if (!options.ResolveOverridePriorityWithStaticComposite || reports.Count == 0)
                return false;
            if (reports.Select(report => report.MaskKey).Distinct(StringComparer.Ordinal).Count() > 1)
                return false;
            var blendingMode = reports[0].BlendingMode;
            if (reports.Any(report => report.BlendingMode != blendingMode))
                return false;
            var lowerBindings = new HashSet<AnimatorOptimizerBindingKey>();
            foreach (var layer in reports.OrderBy(item => item.Index))
            {
                var overlap = lowerBindings.Intersect(layer.Bindings).ToList();
                foreach (var motion in EnumeratePlanMotions(layer.Plan))
                {
                    var guaranteed = new HashSet<AnimatorOptimizerBindingKey>();
                    if (!AnimatorOptimizerAnalyzer.TryCollectGuaranteedBindings(motion?.Motion, guaranteed) ||
                        overlap.Any(binding => !guaranteed.Contains(binding)))
                        return false;
                }
                lowerBindings.UnionWith(layer.Bindings);
            }
            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                if (reports.SelectMany(report => report.Bindings).Any(binding => binding.IsObjectReference))
                    return false;
                for (var leftIndex = 0; leftIndex < reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < reports.Count; rightIndex++)
                    {
                        if (reports[leftIndex].Bindings.Intersect(reports[rightIndex].Bindings)
                            .Any(binding => binding.IsObjectReference || !binding.IsSafeAdditiveFloatBinding))
                            return false;
                    }
                }
            }
            var references = reports.SelectMany(report => EnumeratePlanMotions(report.Plan)).ToList();
            var hasSamples = references.Any(motion => motion?.Bake != null);
            if (hasSamples && !AreSampledStaticMotionsCompatible(references)) return false;
            var staticClips = references
                .Select(motion => motion?.Motion)
                .OfType<AnimationClip>()
                .ToList();
            if (!hasSamples && staticClips.Select(clip => clip.legacy).Distinct().Count() > 1)
                return false;
            if (!hasSamples && staticClips.Skip(1).Any(clip => !AreCompatibleClipMetadata(staticClips[0], clip)))
                return false;
            if (!TryBuildPriorityDimensions(reports, options, out var dimensions, out _))
                return false;

            if (!ValidatePriorityCombinations(reports, dimensions, 0,
                    new Dictionary<string, float>(StringComparer.Ordinal)))
                return false;

            foreach (var report in reports)
            {
                foreach (var motion in EnumeratePlanMotions(report.Plan))
                {
                    if (motion != null && motion.Mirror)
                        return false;
                    if (motion?.Bake != null)
                    {
                        if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                        continue;
                    }
                    if (motion?.Motion != null &&
                        (!(motion.Motion is AnimationClip clip) || !IsStaticClip(clip)))
                        return false;
                }
            }
            return true;
        }

        private static bool CanMergeLayerMotionSet(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            if (reports.Any(report => report.BlendingMode == AnimatorLayerBlendingMode.Additive &&
                                      report.Bindings.Any(binding => binding.IsObjectReference)))
                return false;
            if (reports.Count > 0 && reports[0].BlendingMode == AnimatorLayerBlendingMode.Additive &&
                reports.All(report => report.Plan?.Kind == AnimatorOptimizerConversionKind.ConstantMotion &&
                    (report.Plan.ConstantMotion?.Bake != null
                        ? report.Plan.ConstantMotion.Bake.Parameter == null && report.Plan.ConstantMotion.Bake.Frames.Length == 1
                        : report.Plan.ConstantMotion?.Motion is AnimationClip clip && IsStaticClip(clip))) &&
                !AreStaticAdditiveValuesFinite(reports.OrderBy(report => report.Index).Select(report => report.Plan.ConstantMotion)))
                return false;
            var unique = GetUniqueReports(reports);
            if (unique.Count <= 1)
                return true;
            if (!HasAnyCurveOverlap(reports) && reports.All(report => CanRetainPlanAsDirectChild(report.Plan)) &&
                reports.All(report => EnumeratePlanMotions(report.Plan).All(motion =>
                    IsTimeIndependentMotionRef(motion))))
                return true;
            if (reports.All(report => report.Plan != null &&
                    EnumeratePlanMotions(report.Plan).All(motion =>
                        motion != null && motion.Motion != null &&
                        IsTimeIndependentMotionRef(motion))))
                return true;
            if (reports.All(report => report.Plan != null &&
                    report.Plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion))
            {
                var motions = reports.Select(report => report.Plan.ConstantMotion).ToList();
                if (motions.Any(motion => motion == null || motion.Motion == null || motion.Mirror))
                    return false;
                if (motions.All(IsTimeIndependentMotionRef))
                    return true;
                if (reports[0].BlendingMode == AnimatorLayerBlendingMode.Additive && HasAnyCurveOverlap(reports))
                    return false;
                return TryGetCompatibleTimedClips(reports, out _);
            }
            return false;
        }

        private static bool IsTimeIndependentMotionRef(AnimatorOptimizerMotionRef motion) =>
            motion?.Bake != null || AnimatorOptimizerMotionUtility.IsTimeIndependent(motion?.Motion);

        private static bool AreSampledStaticMotionsCompatible(IEnumerable<AnimatorOptimizerMotionRef> motions)
        {
            bool? precision = null;
            foreach (var motion in motions)
            {
                if (motion == null || motion.Mirror) return false;
                bool current;
                if (motion.Bake != null)
                {
                    if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                    current = motion.Bake.HighQuality;
                }
                else
                {
                    if (motion.Motion == null) continue;
                    if (motion.Motion is not AnimationClip clip || !IsStaticClip(clip) ||
                        !AnimatorOptimizerMotionUtility.IsPropertyOnly(clip) ||
                        !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(clip, out current)) return false;
                }
                // Pure static properties have no observable time base, loop or
                // frame-rate dependence. Precision and binding semantics still do.
                if (precision.HasValue && precision.Value != current) return false;
                precision = current;
            }
            return true;
        }

        private static bool CanRetainPlanAsDirectChild(AnimatorOptimizerMotionPlan plan)
        {
            return plan != null && EnumeratePlanMotions(plan).All(motion =>
                motion?.Motion == null ||
                !MotionHasAnimationEvents(motion.Motion, new HashSet<int>()) &&
                !MotionHasAdditiveReferencePose(motion.Motion, new HashSet<int>()));
        }

        private static bool TryFlattenConstantDirectTree(
            BuiltMotion built,
            out IReadOnlyList<ChildMotion> flattenedChildren)
        {
            flattenedChildren = null;
            if (built == null || built.Motion is not BlendTree tree ||
                tree.blendType != BlendTreeType.Direct ||
                built.HasStateSettings &&
                (built.Speed != 1f || built.CycleOffset != 0f || built.Mirror) ||
                !TryGetNormalizedBlendValues(tree, out var normalized) || normalized)
                return false;

            var children = tree.children ?? Array.Empty<ChildMotion>();
            if (children.Length == 0 || children.Any(child => child.motion == null ||
                    string.IsNullOrWhiteSpace(child.directBlendParameter)))
                return false;

            flattenedChildren = children.Select(child => new ChildMotion
            {
                motion = child.motion,
                threshold = child.threshold,
                position = child.position,
                directBlendParameter = child.directBlendParameter,
                timeScale = child.timeScale,
                cycleOffset = child.cycleOffset,
                mirror = child.mirror,
            }).ToList();
            return true;
        }

        private static bool TryGetNormalizedBlendValues(BlendTree tree, out bool normalized)
        {
            normalized = false;
            if (tree == null)
                return false;
            var serializedObject = new SerializedObject(tree);
            var property = serializedObject.FindProperty("m_NormalizedBlendValues");
            if (property == null)
                return false;
            normalized = property.boolValue;
            return true;
        }

        private static bool TryGetCompatibleTimedClips(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            out List<AnimationClip> clips)
        {
            clips = null;
            if (reports == null || reports.Count == 0 || reports.Any(report =>
                    report.Plan == null || report.Plan.Kind != AnimatorOptimizerConversionKind.ConstantMotion ||
                    report.Plan.ConstantMotion == null || report.Plan.ConstantMotion.Motion is not AnimationClip))
                return false;

            var references = reports.Select(report => report.Plan.ConstantMotion).ToList();
            var first = references[0];
            var firstClip = (AnimationClip)first.Motion;
            if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(firstClip) ||
                !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(firstClip, out var firstHighQuality))
                return false;
            var firstSettings = AnimationUtility.GetAnimationClipSettings(firstClip);
            if (firstSettings.hasAdditiveReferencePose || firstSettings.additiveReferencePoseClip != null)
                return false;
            foreach (var reference in references.Skip(1))
            {
                var clip = (AnimationClip)reference.Motion;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (!AnimatorOptimizerSemanticUtility.IsOrdinaryClip(clip) ||
                    clip.legacy != firstClip.legacy ||
                    settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                    clip.frameRate != firstClip.frameRate ||
                    clip.length != firstClip.length ||
                    clip.wrapMode != firstClip.wrapMode ||
                    !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(clip, out var highQuality) || highQuality != firstHighQuality ||
                    !AreCompatibleClipSettings(firstSettings, settings) ||
                    reference.Speed != first.Speed ||
                    reference.CycleOffset != first.CycleOffset ||
                    reference.Mirror != first.Mirror)
                    return false;
            }
            clips = references.Select(reference => (AnimationClip)reference.Motion).ToList();
            return true;
        }

        private static bool AreCompatibleClipSettings(
            AnimationClipSettings left,
            AnimationClipSettings right)
        {
            return left.loopTime == right.loopTime &&
                   left.loopBlend == right.loopBlend &&
                     left.hasAdditiveReferencePose == right.hasAdditiveReferencePose &&
                     left.additiveReferencePoseTime == right.additiveReferencePoseTime &&
                     left.additiveReferencePoseClip == right.additiveReferencePoseClip &&
                   left.loopBlendOrientation == right.loopBlendOrientation &&
                   left.loopBlendPositionY == right.loopBlendPositionY &&
                   left.loopBlendPositionXZ == right.loopBlendPositionXZ &&
                   left.keepOriginalOrientation == right.keepOriginalOrientation &&
                   left.keepOriginalPositionY == right.keepOriginalPositionY &&
                   left.keepOriginalPositionXZ == right.keepOriginalPositionXZ &&
                   left.heightFromFeet == right.heightFromFeet &&
                   left.startTime == right.startTime &&
                   left.stopTime == right.stopTime &&
                   left.orientationOffsetY == right.orientationOffsetY &&
                   left.level == right.level &&
                   left.cycleOffset == right.cycleOffset;
        }

        private static bool AreCompatibleClipMetadata(AnimationClip left, AnimationClip right)
        {
            if (left == null || right == null || left.legacy != right.legacy ||
                left.frameRate != right.frameRate || left.wrapMode != right.wrapMode ||
                !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(left, out var leftHighQuality) ||
                !AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(right, out var rightHighQuality) || leftHighQuality != rightHighQuality)
                return false;
            var leftSettings = AnimationUtility.GetAnimationClipSettings(left);
            var rightSettings = AnimationUtility.GetAnimationClipSettings(right);
            return AreCompatibleClipSettings(leftSettings, rightSettings);
        }

        private static Bounds GetCombinedLocalBounds(IEnumerable<AnimationClip> clips)
        {
            var sourceClips = clips?.Where(clip => clip != null).ToList() ?? new List<AnimationClip>();
            if (sourceClips.Count == 0)
                return default(Bounds);
            var bounds = sourceClips[0].localBounds;
            for (var index = 1; index < sourceClips.Count; index++)
                bounds.Encapsulate(sourceClips[index].localBounds);
            return bounds;
        }

        private static bool TryBuildLayerComposite(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            AnimationClip emptyClip,
            AnimatorLayerBlendingMode blendingMode,
            out AnimationClip composite,
            out AnimatorOptimizerMotionRef compositeSettings)
        {
            composite = null;
            compositeSettings = null;
            if (reports == null || reports.Count < 2 ||
                reports.Any(report => report.Plan == null ||
                    report.Plan.Kind != AnimatorOptimizerConversionKind.ConstantMotion))
                return false;

            var clips = reports.Select(report => report.Plan.ConstantMotion?.Motion).ToList();
            if (clips.Any(motion => !(motion is AnimationClip)) ||
                reports.Any(report => report.Plan.ConstantMotion == null || report.Plan.ConstantMotion.Mirror))
                return false;

            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                if (reports.SelectMany(report => report.Bindings).Any(binding => binding.IsObjectReference))
                    return false;
                for (var leftIndex = 0; leftIndex < reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < reports.Count; rightIndex++)
                    {
                        if (reports[leftIndex].Bindings.Intersect(reports[rightIndex].Bindings)
                            .Any(binding => binding.IsObjectReference || !binding.IsSafeAdditiveFloatBinding))
                            return false;
                    }
                }
            }

            if (clips.Cast<AnimationClip>().All(AnimatorOptimizerMotionUtility.IsStaticClip))
            {
                if (clips.Cast<AnimationClip>().Select(clip => clip.legacy).Distinct().Count() > 1)
                    return false;
                var staticClips = clips.Cast<AnimationClip>().ToList();
                if (staticClips.Skip(1).Any(clip => !AreCompatibleClipMetadata(staticClips[0], clip)))
                    return false;
                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                composite = CreatePriorityCompositeClip(
                    controller,
                    reports,
                    values,
                    emptyClip,
                    blendingMode == AnimatorLayerBlendingMode.Additive);
                compositeSettings = reports[0].Plan.ConstantMotion?.Clone();
            }
            else if (TryGetCompatibleTimedClips(reports, out var timedClips))
            {
                composite = CreateTimedCompositeClip(
                    controller,
                    reports,
                    timedClips,
                    blendingMode);
                compositeSettings = reports[0].Plan.ConstantMotion?.Clone();
            }
            if (composite == null)
                compositeSettings = null;
            return composite != null;
        }

        private static AnimationClip CreateTimedCompositeClip(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyList<AnimationClip> clips,
            AnimatorLayerBlendingMode blendingMode)
        {
            var floatCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                var clip = clips[clipIndex];
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (floatCurves.TryGetValue(key, out var existing))
                    {
                        if (blendingMode == AnimatorLayerBlendingMode.Additive)
                        {
                            if (!TryAddCompatibleCurves(existing.Curve, curve, out var added))
                                return null;
                            floatCurves[key] = (binding, added);
                        }
                        else
                        {
                            floatCurves[key] = (binding, CloneCurve(curve));
                        }
                    }
                    else
                    {
                        floatCurves[key] = (binding, CloneCurve(curve));
                    }
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, true);
                    if (blendingMode == AnimatorLayerBlendingMode.Additive && objectCurves.ContainsKey(key))
                        return null;
                    var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    objectCurves[key] = (binding, curve == null ? Array.Empty<ObjectReferenceKeyframe>() : curve.ToArray());
                }
            }

            var result = new AnimationClip
            {
                name = GeneratedAssetPrefix + "TimedComposite_" + priorityClipCounter++,
                legacy = clips[0].legacy,
                frameRate = clips[0].frameRate,
                wrapMode = clips[0].wrapMode,
                localBounds = GetCombinedLocalBounds(clips),
                hideFlags = HideFlags.HideInHierarchy,
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clips[0]);
            AnimationUtility.SetAnimationClipSettings(result, settings);
            foreach (var curve in floatCurves.Values)
                AnimationUtility.SetEditorCurve(result, curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                AnimationUtility.SetObjectReferenceCurve(result, curve.Binding, curve.Curve);
            SetHighQualityCurves(result, GetHighQualityCurves(clips[0]));
            AddSubAsset(controller, result);
            return result;
        }

        private static bool TryAddCompatibleCurves(
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
                    float.IsInfinity(leftKeys[index].inTangent) || float.IsInfinity(leftKeys[index].outTangent) ||
                    float.IsInfinity(rightKeys[index].inTangent) || float.IsInfinity(rightKeys[index].outTangent))
                    return false;
                var key = leftKeys[index];
                key.value += rightKeys[index].value;
                key.inTangent += rightKeys[index].inTangent;
                key.outTangent += rightKeys[index].outTangent;
                if (!AnimatorOptimizerAnalyzer.IsFinite(key.value) ||
                    !AnimatorOptimizerAnalyzer.IsFinite(key.inTangent) ||
                    !AnimatorOptimizerAnalyzer.IsFinite(key.outTangent))
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

        private static bool ValidatePriorityCombinations(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values)
        {
            if (depth >= dimensions.Count)
            {
                var motions = reports.OrderBy(report => report.Index).Select(report => EvaluatePlan(report.Plan, values)).ToList();
                return motions.All(motion => motion != null) &&
                    (reports.Count == 0 || reports[0].BlendingMode != AnimatorLayerBlendingMode.Additive ||
                     AreStaticAdditiveValuesFinite(motions));
            }

            var dimension = dimensions[depth];
            foreach (var value in dimension.Values)
            {
                values[dimension.Parameter] = value;
                if (!ValidatePriorityCombinations(reports, dimensions, depth + 1, values))
                    return false;
            }
            values.Remove(dimension.Parameter);
            return true;
        }

        private static bool AreStaticAdditiveValuesFinite(IEnumerable<AnimatorOptimizerMotionRef> motions)
        {
            var sums = new Dictionary<AnimatorOptimizerBindingKey, float>();
            foreach (var motion in motions)
            {
                if (motion?.Motion == null) continue;
                if (motion.Bake != null)
                {
                    if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                    for (var index = 0; index < motion.Bake.FloatBindings.Length; index++)
                    {
                        var binding = motion.Bake.FloatBindings[index];
                        var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false);
                        sums.TryGetValue(key, out var previous);
                        var sum = previous + motion.Bake.Frames[0].Floats[index];
                        if (!AnimatorOptimizerAnalyzer.IsFinite(sum)) return false;
                        sums[key] = sum;
                    }
                    continue;
                }
                if (motion.Motion is not AnimationClip clip || !IsStaticClip(clip)) return false;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;
                    var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false);
                    sums.TryGetValue(key, out var previous);
                    var sum = previous + curve.keys[0].value;
                    if (!AnimatorOptimizerAnalyzer.IsFinite(sum)) return false;
                    sums[key] = sum;
                }
            }
            return true;
        }

        private static IEnumerable<AnimatorOptimizerMotionRef> EnumeratePlanMotions(
            AnimatorOptimizerMotionPlan plan)
        {
            if (plan == null)
                yield break;
            if (plan.ConstantMotion != null)
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

        private static bool TryBuildPriorityDimensions(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            AnimatorOptimizerOptions options,
            out List<PriorityDimension> dimensions,
            out long combinationCount)
        {
            var valuesByParameter = new Dictionary<string, SortedSet<float>>(StringComparer.Ordinal);
            // An assumed Boolean Float cannot be composed with a continuous use of
            // that same parameter by silently thresholding the latter's sample grid.
            var binary = reports.SelectMany(report => report.Plan?.AssumedBinaryParameters ?? new HashSet<string>()).ToHashSet();
            if (reports.Any(report => report.Plan != null &&
                (!string.IsNullOrWhiteSpace(report.Plan.SelectorParameter) && binary.Contains(report.Plan.SelectorParameter) ||
                 (report.Plan.DecisionDimensions ?? new List<AnimatorOptimizerDecisionDimension>()).Any(axis =>
                    binary.Contains(axis.Parameter) && axis.ParameterType != AnimatorControllerParameterType.Bool))))
                return FailPriorityDimensions(out dimensions, out combinationCount);
            foreach (var report in reports)
            {
                var plan = report.Plan;
                if (plan == null)
                    return FailPriorityDimensions(out dimensions, out combinationCount);

                if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
                {
                    foreach (var parameter in plan.BooleanParameters ?? Enumerable.Empty<string>())
                        AddPriorityValue(valuesByParameter, parameter, 0f, 1f);
                }
                else if (plan.Kind == AnimatorOptimizerConversionKind.NumericSelector)
                {
                    if (plan.SelectorIsApproximate ||
                        plan.SelectorParameterType != AnimatorControllerParameterType.Int &&
                        plan.SelectorParameterType != AnimatorControllerParameterType.Float)
                        return FailPriorityDimensions(out dimensions, out combinationCount);
                    foreach (var sample in plan.SelectorSamples ?? Enumerable.Empty<AnimatorOptimizerSelectorSample>())
                        AddPriorityValue(valuesByParameter, plan.SelectorParameter, sample.Value);
                }
                else if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
                {
                    foreach (var dimension in plan.DecisionDimensions ?? Enumerable.Empty<AnimatorOptimizerDecisionDimension>())
                    {
                        if (dimension.IsApproximate)
                            return FailPriorityDimensions(out dimensions, out combinationCount);
                        AddPriorityValue(valuesByParameter, dimension.Parameter, dimension.Values.ToArray());
                    }
                }
                else if (plan.Kind != AnimatorOptimizerConversionKind.ConstantMotion)
                {
                    return FailPriorityDimensions(out dimensions, out combinationCount);
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

            combinationCount = 1;
            foreach (var dimension in dimensions)
            {
                if (!AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(dimension.Values) ||
                    dimension.Values.Count > options.MaxNumericSelectorSamples ||
                    combinationCount > long.MaxValue / dimension.Values.Count)
                    return false;
                combinationCount *= dimension.Values.Count;
            }

            if (combinationCount > Math.Max(1, options.MaxPriorityCombinations))
                return false;
            return true;
        }

        private static bool FailPriorityDimensions(
            out List<PriorityDimension> dimensions,
            out long combinationCount)
        {
            dimensions = null;
            combinationCount = 0;
            return false;
        }

        private static void AddPriorityValue(
            IDictionary<string, SortedSet<float>> valuesByParameter,
            string parameter,
            params float[] inputValues)
        {
            if (!valuesByParameter.TryGetValue(parameter, out var parameterValues))
            {
                parameterValues = new SortedSet<float>();
                valuesByParameter.Add(parameter, parameterValues);
            }
            foreach (var value in inputValues)
                valuesByParameter[parameter].Add(value);
        }

        private static Motion BuildPriorityCompositeTree(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            AnimationClip emptyClip,
            AnimatorOptimizerOptions options,
            List<string> warnings,
            AnimatorLayerBlendingMode blendingMode)
        {
            if (!TryBuildPriorityDimensions(reports, options, out var dimensions, out var combinationCount))
                throw new InvalidOperationException("当前 Layer 的 Override 冲突不能被有限参数复合树表示。");

            warnings.Add($"Priority composite combinations: {combinationCount}.");
            return BuildPriorityNode(controller, reports, dimensions, 0,
                new Dictionary<string, float>(StringComparer.Ordinal), emptyClip,
                blendingMode == AnimatorLayerBlendingMode.Additive,
                new Dictionary<string, Motion>(StringComparer.Ordinal));
        }

        private static Motion BuildPriorityNode(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values,
            AnimationClip emptyClip,
            bool additive,
            IDictionary<string, Motion> memo)
        {
            if (depth >= dimensions.Count)
                return CreatePriorityCompositeClip(controller, reports, values, emptyClip, additive, memo);

            var dimension = dimensions[depth];
            var children = dimension.Values.Select(value =>
            {
                values[dimension.Parameter] = value;
                return new ChildMotion
                {
                    motion = BuildPriorityNode(controller, reports, dimensions, depth + 1, values, emptyClip, additive, memo),
                    threshold = value,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                };
            }).ToArray();
            values.Remove(dimension.Parameter);

            var signatures = children.Select(child => BuildMotionSignature(new AnimatorOptimizerMotionRef { Motion = child.motion })).ToList();
            if (signatures.Count > 0 && signatures.All(signature =>
                    string.Equals(signature, signatures[0], StringComparison.Ordinal)))
                return children[0].motion;

            var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(signatures);
            var memoKey = AnimatorOptimizerSemanticUtility.DecisionNodeKey(dimension.Parameter,
                retained.Select(index => dimension.Values[index]), retained.Select(index => signatures[index]));
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;
            var tree = new BlendTree
            {
                name = GeneratedAssetPrefix + "PriorityTree_" + depth + "_" + priorityClipCounter++,
                blendType = BlendTreeType.Simple1D,
                blendParameter = dimension.Parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
                children = retained.Select(index => children[index]).ToArray(),
            };
            AddSubAsset(controller, tree);
            memo[memoKey] = tree;
            return tree;
        }

        private static AnimatorOptimizerMotionRef EvaluatePlan(
            AnimatorOptimizerMotionPlan plan,
            IReadOnlyDictionary<string, float> values)
        {
            if (plan == null)
                return null;
            if (plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion)
                return plan.ConstantMotion;

            if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
            {
                if (plan.BooleanParameters == null || plan.BooleanParameters.Count > 30)
                    return null;
                var mask = 0;
                for (var index = 0; index < plan.BooleanParameters.Count; index++)
                {
                    if (values.TryGetValue(plan.BooleanParameters[index], out var value) && value >= 0.5f)
                        mask |= 1 << index;
                }
                return plan.TruthTable.TryGetValue(mask, out var motion) ? motion : null;
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.NumericSelector &&
                values.TryGetValue(plan.SelectorParameter, out var selectorValue))
            {
                if (plan.SelectorIsApproximate || plan.SelectorSamples == null) return null;
                return AnimatorOptimizerSemanticUtility.TryEvaluateConstantTable(
                    new IReadOnlyList<float>[] { plan.SelectorSamples.Select(sample => sample.Value).ToArray() },
                    new[] { selectorValue }, plan.SelectorSamples.Select((sample, index) => (sample, index))
                        .ToDictionary(item => item.index, item => item.sample.Motion),
                    ArePriorityMotionsEquivalent, out var selectorMotion) ? selectorMotion : null;
            }
            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
            {
                var inputs = new List<float>();
                foreach (var dimension in plan.DecisionDimensions)
                {
                    if (dimension.IsApproximate || !values.TryGetValue(dimension.Parameter, out var value)) return null;
                    inputs.Add(value);
                }
                return AnimatorOptimizerSemanticUtility.TryEvaluateConstantTable(
                    plan.DecisionDimensions.Select(dimension => (IReadOnlyList<float>)dimension.Values).ToArray(),
                    inputs, plan.DecisionTable, ArePriorityMotionsEquivalent, out var decisionMotion) ? decisionMotion : null;
            }
            return null;
        }

        private static bool ArePriorityMotionsEquivalent(AnimatorOptimizerMotionRef left, AnimatorOptimizerMotionRef right) =>
            left != null && right != null && string.Equals(BuildMotionSignature(left), BuildMotionSignature(right), StringComparison.Ordinal);

        private static AnimationClip CreatePriorityCompositeClip(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyDictionary<string, float> values,
            AnimationClip emptyClip,
            bool additive,
            IDictionary<string, Motion> memo = null)
        {
            var floatCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            AnimationClip settingsSource = null;
            var sourceClips = new List<AnimationClip>();

            foreach (var report in reports.OrderBy(item => item.Index))
            {
                var motion = EvaluatePlan(report.Plan, values);
                if (motion?.Motion == null)
                    continue;
                if (!(motion.Motion is AnimationClip clip))
                    throw new InvalidOperationException("Priority composite 只能合并 AnimationClip Motion。");
                settingsSource = clip;
                sourceClips.Add(clip);

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, false);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var cloned = CloneCurve(curve);
                    if (additive && floatCurves.TryGetValue(key, out var existingFloat))
                        floatCurves[key] = (binding, AddStaticCurves(existingFloat.Curve, cloned));
                    else
                        floatCurves[key] = (binding, cloned);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var key = new AnimatorOptimizerBindingKey(binding.path, binding.type, binding.propertyName, true);
                    var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    objectCurves[key] = (binding, curve == null ? Array.Empty<ObjectReferenceKeyframe>() : curve.ToArray());
                }
            }

            if (floatCurves.Count == 0 && objectCurves.Count == 0)
                return emptyClip;

            // Metadata is checked for the entire group before entering this path.
            // Cache composed output (not input values) so shadowed lower-layer
            // branches do not create a separate clip for every input combination.
            var outputKey = "output:" + string.Concat(floatCurves.Values.Select(item =>
                AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding) +
                AnimatorOptimizerSynchronizedGraph.Atom(item.Curve.length == 0 ? "empty" :
                    item.Curve.keys[0].value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(objectCurves.Values.Select(item => AnimatorOptimizerSynchronizedGraph.BindingKey(item.Binding) +
                    AnimatorOptimizerSynchronizedGraph.Atom(item.Curve.Length == 0 ? "empty" :
                        item.Curve[0].value == null ? "null" : item.Curve[0].value.GetInstanceID().ToString())))
                .OrderBy(key => key, StringComparer.Ordinal).Select(AnimatorOptimizerSynchronizedGraph.Atom));
            var bounds = GetCombinedLocalBounds(sourceClips);
            outputKey += AnimatorOptimizerSynchronizedGraph.Atom(string.Join(",", new[] {
                bounds.center.x, bounds.center.y, bounds.center.z, bounds.extents.x, bounds.extents.y, bounds.extents.z
            }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
            if (memo != null && memo.TryGetValue(outputKey, out var cachedOutput))
                return (AnimationClip)cachedOutput;

            var composite = new AnimationClip
            {
                name = GeneratedAssetPrefix + "PriorityClip_" + priorityClipCounter++,
                legacy = settingsSource != null && settingsSource.legacy,
                hideFlags = HideFlags.HideInHierarchy,
            };
            if (settingsSource != null)
            {
                composite.frameRate = settingsSource.frameRate;
                composite.wrapMode = settingsSource.wrapMode;
                composite.localBounds = GetCombinedLocalBounds(sourceClips);
                var settings = AnimationUtility.GetAnimationClipSettings(settingsSource);
                AnimationUtility.SetAnimationClipSettings(composite, settings);
            }

            foreach (var curve in floatCurves.Values)
                AnimationUtility.SetEditorCurve(composite, curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                AnimationUtility.SetObjectReferenceCurve(composite, curve.Binding, curve.Curve);

            if (settingsSource != null) SetHighQualityCurves(composite, GetHighQualityCurves(settingsSource));
            AddSubAsset(controller, composite);
            if (memo != null) memo[outputKey] = composite;
            return composite;
        }

        private static bool GetHighQualityCurves(AnimationClip clip)
        {
            if (AnimatorOptimizerSemanticUtility.TryGetHighQualityCurves(clip, out var value)) return value;
            throw new InvalidOperationException("无法读取动画精度设置，不能按默认 false 猜测源 Clip 元数据。");
        }

        private static void SetHighQualityCurves(AnimationClip clip, bool value)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            using var serializedObject = new SerializedObject(clip);
            var property = serializedObject.FindProperty("m_UseHighQualityCurve");
            if (property == null)
                throw new InvalidOperationException("当前 Unity 无法写入动画精度设置，拒绝生成不完整的复合 Clip。");
            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return new AnimationCurve();
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private static AnimationCurve AddStaticCurves(AnimationCurve left, AnimationCurve right)
        {
            var leftValue = left == null || left.keys.Length == 0 ? 0f : left.keys[0].value;
            var rightValue = right == null || right.keys.Length == 0 ? 0f : right.keys[0].value;
            var sum = leftValue + rightValue;
            if (!AnimatorOptimizerAnalyzer.IsFinite(sum))
                throw new InvalidOperationException("静态叠加曲线溢出，拒绝生成非有限动画值。");
            return AnimationCurve.Constant(0f, 1f / 60f, sum);
        }

        private static bool IsStaticClip(AnimationClip clip)
        {
            return AnimatorOptimizerMotionUtility.IsStaticClip(clip);
        }

        private static BuiltMotion BuildPlanMotion(
            AnimatorController controller,
            AnimatorOptimizerMotionPlan plan,
            AnimationClip emptyClip)
        {
            if (plan == null)
                throw new InvalidOperationException("缺少 AnimatorOptimizer 转换计划。");

            if (plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion)
            {
                return FromMotionRef(plan.ConstantMotion, emptyClip);
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.NumericSelector)
            {
                var signatures = plan.SelectorSamples.Select(sample => BuildMotionSignature(sample.Motion)).ToList();
                if (signatures.Count > 0 && signatures.All(signature => signature == signatures[0]))
                    return FromMotionRef(plan.SelectorSamples[0].Motion, emptyClip);
                var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(signatures);
                var tree = new BlendTree
                {
                    name = GeneratedAssetPrefix + "NumericSelector_" + plan.SelectorParameter,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = plan.SelectorParameter,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                AddSubAsset(controller, tree);
                tree.children = retained.Select(index => plan.SelectorSamples[index])
                    .Select(sample => ToChildMotion(sample.Value, sample.Motion, emptyClip)).ToArray();
                return new BuiltMotion { Motion = tree };
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
            {
                if (plan.BooleanParameters == null || plan.BooleanParameters.Count > 30)
                    throw new InvalidOperationException("Bool 决策树参数超过 30 位，拒绝生成可能溢出的真值树。");
                var built = BuildTruthTree(controller, plan, emptyClip, 0, 0,
                    new Dictionary<string, BuiltTruthNode>(StringComparer.Ordinal));
                return built.LeafSettings != null ? FromMotionRef(built.LeafSettings, emptyClip) :
                    new BuiltMotion { Motion = built.Motion };
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
            {
                var built = BuildFiniteDecisionTree(controller, plan, emptyClip, 0, 0, 1,
                    new Dictionary<string, BuiltTruthNode>(StringComparer.Ordinal));
                return built.LeafSettings != null ? FromMotionRef(built.LeafSettings, emptyClip) :
                    new BuiltMotion { Motion = built.Motion };
            }

            throw new InvalidOperationException("不支持的 AnimatorOptimizer 转换类型: " + plan.Kind);
        }

        private sealed class BuiltTruthNode
        {
            public Motion Motion;
            public AnimatorOptimizerMotionRef LeafSettings;
            public string Signature;
        }

        private static BuiltTruthNode BuildFiniteDecisionTree(
            AnimatorController controller,
            AnimatorOptimizerMotionPlan plan,
            AnimationClip emptyClip,
            int depth,
            int key,
            int stride,
            IDictionary<string, BuiltTruthNode> memo)
        {
            if (depth >= plan.DecisionDimensions.Count)
            {
                plan.DecisionTable.TryGetValue(key, out var leaf);
                return new BuiltTruthNode
                {
                    Motion = leaf?.Motion ?? emptyClip,
                    LeafSettings = leaf,
                    Signature = BuildMotionSignature(leaf),
                };
            }

            var dimension = plan.DecisionDimensions[depth];
            var children = new List<ChildMotion>();
            var signatures = new List<string>();
            BuiltTruthNode firstNode = null;
            var childStride = stride * dimension.Values.Count;
            for (var index = 0; index < dimension.Values.Count; index++)
            {
                var child = BuildFiniteDecisionTree(
                    controller,
                    plan,
                    emptyClip,
                    depth + 1,
                    key + index * stride,
                    childStride,
                    memo);
                firstNode ??= child;
                signatures.Add(child.Signature);
                children.Add(new ChildMotion
                {
                    motion = child.Motion,
                    threshold = dimension.Values[index],
                    timeScale = child.LeafSettings == null ? 1f : child.LeafSettings.Speed,
                    cycleOffset = child.LeafSettings == null ? 0f : child.LeafSettings.CycleOffset,
                    mirror = child.LeafSettings != null && child.LeafSettings.Mirror,
                });
            }

            if (signatures.All(signature => signature == signatures[0]))
                return firstNode;

            var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(signatures);
            var memoKey = AnimatorOptimizerSemanticUtility.DecisionNodeKey(dimension.Parameter,
                retained.Select(index => dimension.Values[index]), retained.Select(index => signatures[index]));
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;

            var tree = new BlendTree
            {
                name = GeneratedAssetPrefix + "FiniteTree_" + depth + "_" + key,
                blendType = BlendTreeType.Simple1D,
                blendParameter = dimension.Parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = retained.Select(index => children[index]).ToArray();
            AddSubAsset(controller, tree);
            var result = new BuiltTruthNode
            {
                Motion = tree,
                Signature = memoKey,
            };
            memo[memoKey] = result;
            return result;
        }

        private static BuiltTruthNode BuildTruthTree(
            AnimatorController controller,
            AnimatorOptimizerMotionPlan plan,
            AnimationClip emptyClip,
            int depth,
            int mask,
            IDictionary<string, BuiltTruthNode> memo)
        {
            if (depth >= plan.BooleanParameters.Count)
            {
                plan.TruthTable.TryGetValue(mask, out var leaf);
                return new BuiltTruthNode
                {
                    Motion = leaf?.Motion ?? emptyClip,
                    LeafSettings = leaf,
                    Signature = BuildMotionSignature(leaf),
                };
            }

            var falseNode = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask, memo);
            var trueNode = BuildTruthTree(controller, plan, emptyClip, depth + 1, mask | (1 << depth), memo);
            if (string.Equals(falseNode.Signature, trueNode.Signature, StringComparison.Ordinal))
                return falseNode;

            var parameter = plan.BooleanParameters[depth];
            var memoKey = "bool:" + parameter.Length + ":" + parameter + ":" +
                falseNode.Signature.Length + ":" + falseNode.Signature + ":" + trueNode.Signature;
            if (memo.TryGetValue(memoKey, out var cached))
                return cached;

            var tree = new BlendTree
            {
                name = GeneratedAssetPrefix + "BoolTree_" + depth + "_" + mask,
                blendType = BlendTreeType.Simple1D,
                blendParameter = plan.BooleanParameters[depth],
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, tree);

            tree.children = new[]
            {
                ToChildMotion(0f, falseNode, emptyClip),
                ToChildMotion(1f, trueNode, emptyClip),
            };
            var result = new BuiltTruthNode
            {
                Motion = tree,
                Signature = memoKey,
            };
            memo[memoKey] = result;
            return result;
        }

        private static string BuildMotionSignature(AnimatorOptimizerMotionRef motion)
        {
            if (motion == null)
                return "null";
            if (motion.Bake != null) return motion.Bake.Key;
            if (motion.Speed == 1f && motion.CycleOffset == 0f && !motion.Mirror &&
                AnimatorOptimizerMotionUtility.IsPropertyOnly(motion.Motion) &&
                AnimatorOptimizerAnalyzer.TryBuildStaticObservableMotionSignature(motion.Motion, out var staticKey))
                return staticKey;
            return (motion.Motion == null ? "null" : motion.Motion.GetInstanceID().ToString()) + ":" +
                   motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.Mirror;
        }

        private static void MaterializeBakedMotions(AnimatorController controller,
            IEnumerable<AnimatorOptimizerLayerReport> reports)
        {
            var cache = new Dictionary<string, Motion>(StringComparer.Ordinal);
            foreach (var reference in reports.SelectMany(report => EnumeratePlanMotions(report.Plan)).Distinct())
            {
                var bake = reference?.Bake;
                if (bake == null) continue;
                if (!cache.TryGetValue(bake.Key, out var motion))
                {
                    var frames = new List<Motion>();
                    foreach (var frame in bake.Frames)
                    {
                        var clip = new AnimationClip
                        {
                            name = GeneratedAssetPrefix + "Sample_" + bake.Name + "_" + frames.Count,
                            frameRate = bake.FrameRate, localBounds = bake.Bounds, legacy = false,
                            wrapMode = WrapMode.ClampForever, hideFlags = HideFlags.HideInHierarchy,
                        };
                        for (var index = 0; index < bake.FloatBindings.Length; index++)
                            AnimationUtility.SetEditorCurve(clip, bake.FloatBindings[index],
                                AnimationCurve.Constant(0f, 1f / 60f, frame.Floats[index]));
                        for (var index = 0; index < bake.ObjectBindings.Length; index++)
                            AnimationUtility.SetObjectReferenceCurve(clip, bake.ObjectBindings[index],
                                new[] { new ObjectReferenceKeyframe { time = 0f, value = frame.Objects[index] } });
                        // SetEditorCurve can reset this flag; restore it last.
                        SetHighQualityCurves(clip, bake.HighQuality);
                        AddSubAsset(controller, clip);
                        frames.Add(clip);
                    }
                    motion = frames[0];
                    if (bake.Parameter != null)
                    {
                        var tree = new BlendTree
                        {
                            name = GeneratedAssetPrefix + "MotionTime_" + bake.Name,
                            blendType = BlendTreeType.Simple1D, blendParameter = bake.Parameter,
                            useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
                            children = frames.Select((frame, index) => new ChildMotion
                            {
                                motion = frame, threshold = bake.Frames[index].Threshold, timeScale = 1f,
                            }).ToArray(),
                        };
                        AddSubAsset(controller, tree);
                        motion = tree;
                    }
                    cache.Add(bake.Key, motion);
                }
                reference.Motion = motion;
                // Keep the read-only provenance: selection and generation must use
                // the same sampled-property and precision checks after baking.
                reference.Speed = 1f;
                reference.CycleOffset = 0f;
                reference.Mirror = false;
            }
        }

        private static ChildMotion ToChildMotion(
            float threshold,
            BuiltTruthNode node,
            AnimationClip emptyClip)
        {
            var motion = node?.Motion ?? emptyClip;
            var settings = node?.LeafSettings;
            return new ChildMotion
            {
                motion = motion,
                threshold = threshold,
                timeScale = settings == null ? 1f : settings.Speed,
                cycleOffset = settings == null ? 0f : settings.CycleOffset,
                mirror = settings != null && settings.Mirror,
            };
        }

        private static ChildMotion ToChildMotion(
            float threshold,
            AnimatorOptimizerMotionRef motion,
            AnimationClip emptyClip)
        {
            var normalizedMotion = motion?.Motion;
            AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(normalizedMotion, out normalizedMotion);
            return new ChildMotion
            {
                motion = normalizedMotion ?? emptyClip,
                threshold = threshold,
                timeScale = motion == null ? 1f : motion.Speed,
                cycleOffset = motion == null ? 0f : motion.CycleOffset,
                mirror = motion != null && motion.Mirror,
            };
        }

        private static BuiltMotion FromMotionRef(AnimatorOptimizerMotionRef motion, AnimationClip emptyClip)
        {
            var normalizedMotion = motion?.Motion;
            AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(normalizedMotion, out normalizedMotion);
            return new BuiltMotion
            {
                Motion = normalizedMotion ?? emptyClip,
                HasStateSettings = motion != null,
                Speed = motion == null ? 1f : motion.Speed,
                CycleOffset = motion == null ? 0f : motion.CycleOffset,
                Mirror = motion != null && motion.Mirror,
            };
        }

        private static List<string> UpgradeParameters(
            AnimatorController controller,
            IEnumerable<AnimatorOptimizerLayerReport> reports,
            AnimatorOptimizerOptions options)
        {
            var required = new HashSet<string>(
                reports.SelectMany(report => report.RequiredFloatParameters),
                StringComparer.Ordinal);
            if (required.Count == 0)
                return new List<string>();

            var parameters = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            var upgraded = new List<string>();
            var boolUpgraded = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (!required.Contains(parameter.name))
                    continue;
                if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(parameter.name, options.UseVrChatParameterSemantics))
                    continue;

                if (parameter.type == AnimatorControllerParameterType.Bool && options.UpgradeBoolParametersToFloat)
                {
                    parameter.type = AnimatorControllerParameterType.Float;
                    parameter.defaultFloat = parameter.defaultBool ? 1f : 0f;
                    boolUpgraded.Add(parameter.name);
                    upgraded.Add(parameter.name);
                }
                else if (parameter.type == AnimatorControllerParameterType.Int && options.UpgradeIntParametersToFloat)
                {
                    parameter.type = AnimatorControllerParameterType.Float;
                    parameter.defaultFloat = parameter.defaultInt;
                    upgraded.Add(parameter.name);
                }
            }

            if (upgraded.Count == 0)
                return upgraded;

            controller.parameters = parameters;
            RewriteBoolConditions(controller, boolUpgraded);
            EditorUtility.SetDirty(controller);
            return upgraded;
        }

        private static void RewriteBoolConditions(
            AnimatorController controller,
            HashSet<string> boolParameters)
        {
            foreach (var layer in controller.layers ?? Array.Empty<AnimatorControllerLayer>())
                RewriteStateMachineConditions(layer.stateMachine, boolParameters);
        }

        private static void RewriteStateMachineConditions(
            AnimatorStateMachine stateMachine,
            HashSet<string> boolParameters)
        {
            if (stateMachine == null)
                return;

            stateMachine.anyStateTransitions = RewriteConditions(stateMachine.anyStateTransitions, boolParameters);
            stateMachine.entryTransitions = RewriteConditions(stateMachine.entryTransitions, boolParameters);
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                child.state.transitions = RewriteConditions(child.state.transitions, boolParameters);
            }

            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                var transitions = stateMachine.GetStateMachineTransitions(child.stateMachine);
                stateMachine.SetStateMachineTransitions(child.stateMachine, RewriteConditions(transitions, boolParameters));
                RewriteStateMachineConditions(child.stateMachine, boolParameters);
            }
        }

        private static T[] RewriteConditions<T>(T[] transitions, HashSet<string> boolParameters)
            where T : AnimatorTransitionBase
        {
            if (transitions == null)
                return Array.Empty<T>();
            foreach (var transition in transitions)
            {
                if (transition == null)
                    continue;
                var conditions = transition.conditions;
                if (!conditions.Any(condition => boolParameters.Contains(condition.parameter)))
                    continue;
                Undo.RecordObject(transition, "Animator Optimizer Bool Conditions");
                transition.conditions = conditions.Select(condition => RewriteCondition(condition, boolParameters)).ToArray();
                EditorUtility.SetDirty(transition);
            }
            return transitions;
        }

        private static AnimatorCondition RewriteCondition(
            AnimatorCondition condition,
            HashSet<string> boolParameters)
        {
            if (!boolParameters.Contains(condition.parameter))
                return condition;
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
            return condition;
        }

        private static bool HasPotentialParameterDriverFor(
            AnimatorController controller,
            string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
                return false;
            if (EnumerateSyncedOverrideBehaviours(controller).Any(behaviour => BehaviourReferencesParameter(behaviour, parameterName)))
                return true;

            foreach (var layer in controller.layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                if (StateMachineHasParameterDriver(layer.stateMachine, parameterName))
                    return true;
            }
            return false;
        }

        private static bool HasPotentialBehaviourReferenceFor(
            AnimatorController controller,
            string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
                return false;
            if (EnumerateSyncedOverrideBehaviours(controller).Any(behaviour => BehaviourMayReferenceParameter(behaviour, parameterName)))
                return true;
            foreach (var layer in controller.layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                if (StateMachineHasPotentialBehaviourReference(layer.stateMachine, parameterName))
                    return true;
            }
            return false;
        }

        private static bool StateMachineHasPotentialBehaviourReference(
            AnimatorStateMachine stateMachine,
            string parameterName)
        {
            if (stateMachine == null)
                return false;
            if ((stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Any(behaviour => BehaviourMayReferenceParameter(behaviour, parameterName)))
                return true;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state != null && (child.state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                    .Any(behaviour => BehaviourMayReferenceParameter(behaviour, parameterName)))
                    return true;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .Any(child => StateMachineHasPotentialBehaviourReference(child.stateMachine, parameterName));
        }

        private static bool BehaviourMayReferenceParameter(
            StateMachineBehaviour behaviour,
            string parameterName)
        {
            if (behaviour == null)
                return true; // Missing scripts are not evidence that a parameter has no reader/writer.
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

        private static bool StateMachineHasParameterDriver(
            AnimatorStateMachine stateMachine,
            string parameterName)
        {
            if (stateMachine == null)
                return false;
            if ((stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
                .Any(behaviour => BehaviourReferencesParameter(behaviour, parameterName)))
                return true;
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                if ((child.state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                    .Any(behaviour => BehaviourReferencesParameter(behaviour, parameterName)))
                    return true;
            }
            return (stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
                .Any(child => StateMachineHasParameterDriver(child.stateMachine, parameterName));
        }

        private static bool BehaviourReferencesParameter(
            StateMachineBehaviour behaviour,
            string parameterName)
        {
            if (behaviour == null || behaviour.GetType().Name.IndexOf("ParameterDriver", StringComparison.OrdinalIgnoreCase) < 0)
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

        private static List<int> NormalizeSelection(
            AnimatorOptimizerReport report,
            IEnumerable<int> selectedLayerIndices,
            List<string> issues)
        {
            var result = new List<int>();
            foreach (var index in new SortedSet<int>(selectedLayerIndices ?? Enumerable.Empty<int>()))
            {
                if (index < 0 || index >= report.Layers.Count)
                {
                    issues?.Add("选择了不存在的 Layer 索引: " + index);
                    continue;
                }
                if (!report.Layers[index].CanConvert)
                {
                    issues?.Add($"Layer {index} ({report.Layers[index].Name}) 不可转换。");
                    continue;
                }
                result.Add(index);
            }
            return result;
        }

        private static List<string> DistinctIssues(IEnumerable<string> issues)
        {
            return (issues ?? Enumerable.Empty<string>())
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string EnsureWeightParameter(
            AnimatorController controller,
            string prefix,
            bool normalizeAlwaysOne, bool useVrChatParameterSemantics)
        {
            if (normalizeAlwaysOne)
            {
                var existingAlwaysOne = FindCanonicalAlwaysOneParameter(controller);
                if (!string.IsNullOrWhiteSpace(existingAlwaysOne))
                    return existingAlwaysOne;
            }
            prefix = string.IsNullOrWhiteSpace(prefix) ? "AO/LayerWeight" : prefix.Trim();
            var name = AnimatorOptimizerSemanticUtility.AllocateParameterName(prefix, CollectReservedParameterNames(controller), useVrChatParameterSemantics);

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            return name;
        }

        // A missing declaration is not an unused name: clips, transitions or a
        // Behaviour may already refer to it. Includes synced override assets via
        // controller serialization; never follows scene objects or material assets.
        private static HashSet<string> CollectReservedParameterNames(AnimatorController controller)
        {
            var names = new HashSet<string>(controller.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            var visited = new HashSet<Object>();
            var pending = new Queue<Object>();
            pending.Enqueue(controller);
            while (pending.Count > 0)
            {
                var source = pending.Dequeue();
                if (source == null || !visited.Add(source)) continue;
                if (source is AnimationClip clip)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                        if (binding.type == typeof(Animator)) names.Add(binding.propertyName);
                    continue;
                }
                using var serialized = new SerializedObject(source);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.String && !string.IsNullOrWhiteSpace(property.stringValue))
                        names.Add(property.stringValue);
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var reference = property.objectReferenceValue;
                    if (reference is AnimatorStateMachine || reference is AnimatorState || reference is AnimatorTransitionBase ||
                        reference is BlendTree || reference is AnimationClip || reference is StateMachineBehaviour)
                        pending.Enqueue(reference);
                }
            }
            return names;
        }

        private static string FindCanonicalAlwaysOneParameter(AnimatorController controller)
        {
            var animated = CollectAnimatedAnimatorParameterNames(controller);
            return (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .Where(parameter => parameter.type == AnimatorControllerParameterType.Float &&
                    Mathf.Approximately(parameter.defaultFloat, 1f) &&
                    IsAlwaysOneName(parameter.name) &&
                    !animated.Contains(parameter.name) &&
                    !HasPotentialParameterDriverFor(controller, parameter.name) &&
                    !HasPotentialBehaviourReferenceFor(controller, parameter.name))
                .OrderBy(parameter => IsCanonicalAlwaysOneName(parameter.name) ? 0 : 1)
                .ThenBy(parameter => parameter.name.Length)
                .ThenBy(parameter => parameter.name, StringComparer.Ordinal)
                .Select(parameter => parameter.name)
                .FirstOrDefault();
        }

        private static AnimationClip CreateEmptyClip(AnimatorController controller)
        {
            var clip = new AnimationClip
            {
                name = GeneratedAssetPrefix + "Empty",
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, clip);
            return clip;
        }

        private static void AddSubAsset(AnimatorController controller, Object asset)
        {
            if (controller == null || asset == null)
                return;
            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(controller)))
                throw new InvalidOperationException("无法向未保存的 AnimatorController 添加子资源。");
            AssetDatabase.AddObjectToAsset(asset, controller);
        }

        private static void SetNormalizedBlendValues(BlendTree tree, bool normalized)
        {
            var serializedObject = new SerializedObject(tree);
            var property = serializedObject.FindProperty("m_NormalizedBlendValues");
            if (property == null)
                throw new InvalidOperationException("当前 Unity BlendTree 缺少 m_NormalizedBlendValues 字段。");
            property.boolValue = normalized;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
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
