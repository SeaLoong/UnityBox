using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDKBase;
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

    public static class AnimatorOptimizerConverter
    {
        public const string GeneratedLayerName = "(WD On) AnimatorOptimizer";
        public const string GeneratedAssetPrefix = "AnimatorOptimizer_";
        private static int priorityClipCounter;

        private sealed class LayerGroup
        {
            public AnimatorLayerBlendingMode BlendingMode;
            public string MaskKey;
            public AvatarMask Mask;
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
            if (options.PreserveMmdCompatibility && selected.Contains(0))
                issues.Add("启用 MMD 兼容时不能转换 Controller 的首层；首层必须保留为兼容基准层。");
            issues.AddRange(GetLayerIndexReferenceIssues(report.Controller, selected, options));
            var includesAapStateRegister = selected.Any(index =>
                report.Layers[index].ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister);
            if (options.RequireAtLeastTwoLayers && selected.Count < 2 && !includesAapStateRegister)
                issues.Add("至少选择两个可转换 Layer，合并后才会减少 Animator Layer 数量。");

            var selectedReports = selected.Select(index => report.Layers[index]).ToList();
            if (includesAapStateRegister && selectedReports.Count != 1)
                issues.Add("AAP StateRegister 当前必须单独转换一个源 Layer；请不要与普通合并选择混用。");
            if (includesAapStateRegister && selectedReports.Any(layer =>
                    layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister))
                issues.Add("AAP StateRegister 不能与普通 BlendTree 合并计划混在同一次转换中。");
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

            var groups = BuildLayerGroups(selectedReports);
            if (!options.MergeLayersWithEquivalentMasks && groups.Any(group => !string.Equals(group.MaskKey, "<none>", StringComparison.Ordinal)))
            {
                issues.Add("当前选择包含 AvatarMask，但已关闭“合并等价 Mask”；请开启该选项或拆分选择。");
            }
            if (HasInterleavedConflictingGroups(groups))
                issues.Add("不同 BlendMode/Mask 组在 Layer 顺序上交错且控制相同属性；合并会改变组间 Override/Additive 顺序，请分组处理。");
            foreach (var group in groups.Where(group => group.Reports.Count > 1))
            {
                if (!CanPreserveLayerBehaviours(group.Reports))
                    issues.Add($"Layer 组 ({group.BlendingMode}/{group.MaskKey}) 的 StateMachine Behaviour 不完全一致，合并会丢失 MMD/其它回调语义。");
            }

            issues.AddRange(GetParameterSafetyIssues(report, selected));

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
                for (var leftIndex = 0; leftIndex < group.Reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < group.Reports.Count; rightIndex++)
                    {
                        var left = group.Reports[leftIndex];
                        var right = group.Reports[rightIndex];
                        var overlap = left.Bindings.Intersect(right.Bindings).Take(3).ToList();
                        if (overlap.Count > 0 && !CanResolveLayerOverlap(group.Reports, options))
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
                    if (selected.Contains(layer.Index) || groupBindings.Count == 0)
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
                    if (overlap.Count > 0)
                    {
                        issues.Add(
                            $"待合并 Layer 组 ({group.BlendingMode}/{group.MaskKey}) 与未合并 Layer {layer.Index} ({layer.Name}) 存在动画绑定冲突；" +
                            "示例: " + overlap[0]);
                    }
                }
            }

            return DistinctIssues(issues);
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
                    !AppliesToTargetPlayable(control, options) || selected.Contains(control.layer)))
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
                   behaviour is VRC_AnimatorLayerControl;
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
                    if (selected.Contains(control.layer))
                    {
                        issues.Add($"VRCAnimatorLayerControl 引用了待合并 Layer {control.layer}；合并后无法保留该 Layer 的独立权重控制。");
                    }
                }
            }

            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            for (var index = 0; index < layers.Length; index++)
            {
                if (layers[index] != null && selected.Contains(layers[index].syncedLayerIndex))
                    issues.Add($"未选中 Layer {index} 的 Synced Layer 引用了待合并 Layer {layers[index].syncedLayerIndex}。");
            }
            return DistinctIssues(issues);
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateLayerControls(
            AnimatorController controller)
        {
            var visited = new HashSet<int>();
            foreach (var layer in controller?.layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                foreach (var control in EnumerateLayerControls(layer?.stateMachine, visited))
                    yield return control;
            }
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateLayerControls(
            AnimatorStateMachine stateMachine,
            ISet<int> visited)
        {
            if (stateMachine == null || !visited.Add(stateMachine.GetInstanceID()))
                yield break;
            foreach (var behaviour in stateMachine.behaviours ?? Array.Empty<StateMachineBehaviour>())
            {
                if (behaviour is VRC_AnimatorLayerControl control)
                    yield return control;
            }
            foreach (var child in stateMachine.states ?? Array.Empty<ChildAnimatorState>())
            {
                if (child.state == null)
                    continue;
                foreach (var behaviour in child.state.behaviours ?? Array.Empty<StateMachineBehaviour>())
                {
                    if (behaviour is VRC_AnimatorLayerControl control)
                        yield return control;
                }
            }
            foreach (var child in stateMachine.stateMachines ?? Array.Empty<ChildAnimatorStateMachine>())
            {
                foreach (var control in EnumerateLayerControls(child.stateMachine, visited))
                    yield return control;
            }
        }

        private static bool AppliesToTargetPlayable(
            VRC_AnimatorLayerControl control,
            AnimatorOptimizerOptions options)
        {
            if (control == null || options == null || !options.HasKnownPlayableLayer)
                return false;
            switch (options.TargetPlayableLayer)
            {
                case AnimatorOptimizerComponent.PlayableLayer.FX:
                    return control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX;
                case AnimatorOptimizerComponent.PlayableLayer.Gesture:
                    return control.playable == VRC_AnimatorLayerControl.BlendableLayer.Gesture;
                case AnimatorOptimizerComponent.PlayableLayer.Action:
                    return control.playable == VRC_AnimatorLayerControl.BlendableLayer.Action;
                case AnimatorOptimizerComponent.PlayableLayer.Additive:
                    return control.playable == VRC_AnimatorLayerControl.BlendableLayer.Additive;
                default:
                    return false;
            }
        }

        internal static IReadOnlyList<string> GetParameterSafetyIssues(
            AnimatorOptimizerReport report,
            IEnumerable<int> selectedLayerIndices)
        {
            if (report == null)
                return Array.Empty<string>();
            var selected = NormalizeSelection(report, selectedLayerIndices, new List<string>());
            var requiredParameters = new HashSet<string>(
                selected.SelectMany(index => report.Layers[index].RequiredFloatParameters),
                StringComparer.Ordinal);
            var animatedParameters = CollectAnimatedAnimatorParameterNames(report.Controller);
            var issues = new List<string>();
            foreach (var parameter in requiredParameters)
            {
                if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(parameter))
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
            }
            return DistinctIssues(issues);
        }

        public static AnimatorOptimizerConversionResult ConvertInPlace(
            AnimatorController controller,
            IEnumerable<int> selectedLayerIndices,
            AnimatorOptimizerOptions options = null)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var issues = GetSelectionIssues(report, selectedLayerIndices, options);
            if (issues.Count > 0)
                throw new InvalidOperationException("AnimatorOptimizer 转换被阻止:\n- " + string.Join("\n- ", issues));

            var selected = NormalizeSelection(report, selectedLayerIndices, new List<string>());
            var selectedReports = selected.Select(index => report.Layers[index]).ToList();
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(controllerPath))
                throw new InvalidOperationException("AnimatorController 必须是 Assets/ 下已保存的资源。");

            var generatedLayerName = string.IsNullOrWhiteSpace(options.GeneratedLayerName)
                ? GeneratedLayerName
                : options.GeneratedLayerName.Trim();
            if ((controller.layers ?? Array.Empty<AnimatorControllerLayer>()).Any(layer =>
                    IsGeneratedLayerName(layer.name, generatedLayerName) ||
                    layer.name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Controller 已包含 AnimatorOptimizer 生成层，请先恢复源资源。");
            }

            if (selectedReports.Count == 1 &&
                selectedReports[0].ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister)
                return ConvertAapStateRegisterLayer(controller, selectedReports[0], options);

            Undo.RegisterCompleteObjectUndo(controller, "Animator Optimizer");
            var alwaysOneWarnings = new List<string>();
            if (options.NormalizeAlwaysOneParameters)
                NormalizeAlwaysOneParameters(controller, alwaysOneWarnings);
            var upgradedParameters = UpgradeParameters(controller, selectedReports, options);
            var warnings = new List<string>(alwaysOneWarnings);
            if (!options.StrictCurveConflicts)
                warnings.Add("已关闭严格曲线冲突检查；请确认没有依赖 Layer 顺序的重复动画绑定。");

            var emptyClip = CreateEmptyClip(controller);
            var weightParameter = EnsureWeightParameter(controller, options.WeightParameterPrefix,
                options.NormalizeAlwaysOneParameters);
            var groups = BuildLayerGroups(selectedReports);
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
            if (options.PreserveMmdCompatibility && layerIndex == 0)
                throw new InvalidOperationException("启用 MMD 兼容时不能转换 Controller 的首层。");
            var layerReferenceIssues = GetLayerIndexReferenceIssues(controller, new[] { layerIndex }, options);
            if (layerReferenceIssues.Count > 0)
                throw new InvalidOperationException("AAP StateRegister 转换被阻止:\n- " +
                    string.Join("\n- ", layerReferenceIssues));
            var layer = report.Layers[layerIndex];
            if (!layer.CanConvert || layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister)
                throw new InvalidOperationException($"Layer {layerIndex} 不是可转换的 AAP StateRegister Layer。");
            return ConvertAapStateRegisterLayer(controller, layer, options);
        }

        private static AnimatorOptimizerConversionResult ConvertAapStateRegisterLayer(
            AnimatorController controller,
            AnimatorOptimizerLayerReport report,
            AnimatorOptimizerOptions options)
        {
            var plan = report.Plan;
            if (plan == null || plan.Kind != AnimatorOptimizerConversionKind.AapStateRegister ||
                plan.StateRegisterStates == null || plan.StateRegisterStates.Count == 0)
                throw new InvalidOperationException("AAP StateRegister 计划不完整。");

            Undo.RegisterCompleteObjectUndo(controller, "Animator Optimizer AAP StateRegister");
            var upgradedParameters = UpgradeParameters(controller, new[] { report }, options);
            var alwaysOneWarnings = new List<string>();
            if (options.NormalizeAlwaysOneParameters)
                NormalizeAlwaysOneParameters(controller, alwaysOneWarnings);
            var stateRegisterParameter = EnsureFloatParameterWithDefault(
                controller,
                "AO/StateRegister/" + report.Index,
                plan.StateRegisterDefaultState);
            var nextStates = plan.StateRegisterNextStates.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<int, int>)pair.Value);
            var initializedParameter = plan.StateRegisterEntryStates == null
                ? null
                : EnsureFloatParameterWithDefault(
                    controller,
                    "AO/StateRegisterInitialized/" + report.Index,
                    0f);
            var updateTree = initializedParameter != null
                ? BuildAapStateRegisterUpdateTreeWithInitialization(
                    controller,
                    stateRegisterParameter,
                    initializedParameter,
                    plan.StateRegisterDecisionDimensions,
                    plan.StateRegisterStates.Count,
                    nextStates,
                    plan.StateRegisterEntryStates,
                    GeneratedAssetPrefix + "StateRegister_" + report.Index)
                : plan.StateRegisterDecisionDimensions != null
                ? BuildAapFiniteStateRegisterUpdateTree(
                    controller,
                    stateRegisterParameter,
                    plan.StateRegisterDecisionDimensions,
                    plan.StateRegisterStates.Count,
                    nextStates,
                    GeneratedAssetPrefix + "StateRegister_" + report.Index)
                : BuildAapStateRegisterUpdateTree(
                    controller,
                    stateRegisterParameter,
                    plan.StateRegisterBooleanParameters,
                    plan.StateRegisterStates.Count,
                    nextStates,
                    GeneratedAssetPrefix + "StateRegister_" + report.Index);
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
            var updateLayer = new AnimatorControllerLayer
            {
                name = generatedLayerName + "/AAPStateRegister_" + report.Index + "/Update",
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false,
                avatarMask = null,
                stateMachine = updateStateMachine,
            };
            var outputLayer = new AnimatorControllerLayer
            {
                name = generatedLayerName + "/AAPStateRegister_" + report.Index + "/Output",
                defaultWeight = 1f,
                blendingMode = report.BlendingMode,
                iKPass = false,
                avatarMask = report.Mask,
                stateMachine = outputStateMachine,
            };

            var oldLayers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            var newLayers = new List<AnimatorControllerLayer>(oldLayers.Length + 1);
            for (var index = 0; index < oldLayers.Length; index++)
            {
                if (index == report.Index)
                {
                    newLayers.Add(updateLayer);
                    newLayers.Add(outputLayer);
                }
                else
                {
                    newLayers.Add(oldLayers[index]);
                }
            }
            RewriteLayerIndexReferences(oldLayers, newLayers.ToArray(), options);
            controller.layers = newLayers.ToArray();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var cleanupWarnings = CleanupControllerInPlace(controller, null, options);
            AssetDatabase.SaveAssets();
            return new AnimatorOptimizerConversionResult(
                controller,
                new[] { updateLayer, outputLayer },
                new[] { report.Index },
                upgradedParameters,
                new[]
                {
                    initializedParameter == null
                        ? $"Layer {report.Index} 使用 AAP StateRegister；生成 Update/Output 两个 WD On Layer，StateRegister={stateRegisterParameter}。"
                        : $"Layer {report.Index} 使用 AAP StateRegister Entry 初始化反馈；首帧写入 {initializedParameter}=1，StateRegister={stateRegisterParameter}。",
                }.Concat(alwaysOneWarnings).Concat(cleanupWarnings));
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
            var oldLayersForReferences = (controller.layers ?? Array.Empty<AnimatorControllerLayer>()).ToArray();
            var hasUnknownLayerControls = !options.HasKnownPlayableLayer &&
                EnumerateLayerControlsFromLayers(oldLayersForReferences).Any();
            if (hasUnknownLayerControls && options.PreserveMmdCompatibility)
                throw new InvalidOperationException("启用 MMD 兼容时必须知道目标 Playable Layer，才能安全重排 VRCAnimatorLayerControl。");
            var protectedLayerIndices = GetProtectedLayerIndices(oldLayersForReferences, options);
            if (options.NormalizeAlwaysOneParameters)
                NormalizeAlwaysOneParameters(controller, messages);
            if (options.RemoveEffectivelyEmptyLayers && !hasUnknownLayerControls)
            {
                var oldLayers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
                var kept = new List<AnimatorControllerLayer>(oldLayers.Length);
                for (var index = 0; index < oldLayers.Length; index++)
                {
                    var layer = oldLayers[index];
                    var preserveSlot = index == 0 || layer != null &&
                        (string.Equals(layer.name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                         string.Equals(layer.name, "MMD Dummy Layer 2", StringComparison.Ordinal));
                    var generated = layer != null &&
                        (IsGeneratedLayerName(layer.name, options.GeneratedLayerName) ||
                         layer.name.StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal));
                    if (!preserveSlot && !generated && !protectedLayerIndices.Contains(index) &&
                        IsEffectivelyEmptyLayer(layer, bindingRoot))
                    {
                        messages.Add($"删除无可观测输出的空 Layer {index} ({layer?.name ?? "<null>"})。");
                        continue;
                    }
                    kept.Add(layer);
                }
                if (kept.Count != oldLayers.Length)
                    controller.layers = kept.ToArray();
            }

            var normalizedEmptyMotions = NormalizeEffectivelyEmptyMotions(controller, bindingRoot);

            if (options.PreserveMmdCompatibility && !options.HasKnownPlayableLayer &&
                EnumerateLayerControls(controller).Any())
                throw new InvalidOperationException("启用 MMD 兼容时必须知道目标 Playable Layer，才能安全重映射 VRCAnimatorLayerControl。");
            if (options.PreserveMmdCompatibility)
                EnsureMmdCompatibilityLayers(controller);
            RewriteLayerIndexReferences(
                oldLayersForReferences,
                controller.layers ?? Array.Empty<AnimatorControllerLayer>(),
                options);
            if (messages.Count > 0 || normalizedEmptyMotions || options.PreserveMmdCompatibility)
                EditorUtility.SetDirty(controller);
            return messages;
        }

        private static HashSet<int> GetProtectedLayerIndices(
            IReadOnlyList<AnimatorControllerLayer> layers,
            AnimatorOptimizerOptions options)
        {
            var protectedIndices = new HashSet<int>();
            var controls = EnumerateLayerControlsFromLayers(layers).ToList();
            foreach (var control in controls)
            {
                if (!options.HasKnownPlayableLayer || AppliesToTargetPlayable(control, options))
                    protectedIndices.Add(control.layer);
            }
            return protectedIndices;
        }

        private static IEnumerable<VRC_AnimatorLayerControl> EnumerateLayerControlsFromLayers(
            IReadOnlyList<AnimatorControllerLayer> layers)
        {
            var visited = new HashSet<int>();
            foreach (var layer in layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                foreach (var control in EnumerateLayerControls(layer?.stateMachine, visited))
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
                var oldStateMachine = oldLayers[oldIndex]?.stateMachine;
                if (oldStateMachine == null)
                    continue;
                var newIndex = newLayers
                    .Select((layer, index) => new { layer, index })
                    .FirstOrDefault(item => item.layer?.stateMachine == oldStateMachine)?.index ?? -1;
                if (newIndex >= 0)
                    oldToNew[oldIndex] = newIndex;
            }

            foreach (var layer in newLayers)
            {
                var stateMachine = layer?.stateMachine;
                if (stateMachine == null)
                    continue;
                var oldIndex = oldLayers
                    .Select((candidate, index) => new { candidate, index })
                    .FirstOrDefault(item => item.candidate?.stateMachine == stateMachine)?.index ?? -1;
                if (oldIndex >= 0 && layer.syncedLayerIndex >= 0)
                {
                    if (oldToNew.TryGetValue(layer.syncedLayerIndex, out var newSyncedIndex))
                        layer.syncedLayerIndex = newSyncedIndex;
                    else
                    {
                        layer.syncedLayerIndex = -1;
                        layer.syncedLayerAffectsTiming = false;
                    }
                }

                foreach (var control in EnumerateLayerControls(stateMachine, new HashSet<int>()))
                {
                    if (!options.HasKnownPlayableLayer || !AppliesToTargetPlayable(control, options))
                        continue;
                    if (oldToNew.TryGetValue(control.layer, out var newIndex))
                        control.layer = newIndex;
                }
            }
        }

        public static bool IsEffectivelyEmptyClip(AnimationClip clip, Transform bindingRoot = null)
        {
            if (clip == null || AnimatorOptimizerMotionUtility.IsPotentialProxyClip(clip) ||
                (clip.events != null && clip.events.Length > 0))
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
                layer.syncedLayerIndex >= 0 || layer.syncedLayerAffectsTiming)
                return false;
            return IsEffectivelyEmptyStateMachine(layer.stateMachine, bindingRoot);
        }

        private static bool NormalizeEffectivelyEmptyMotions(
            AnimatorController controller,
            Transform bindingRoot)
        {
            if (controller == null)
                return false;
            AnimationClip canonical = FindCanonicalEmptyClip(controller);
            var changed = false;
            foreach (var layer in controller.layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                if (layer == null || layer.stateMachine == null ||
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
                    child.state.iKOnFeet || child.state.mirror ||
                    child.state.speedParameterActive || child.state.cycleOffsetParameterActive ||
                    child.state.mirrorParameterActive || child.state.timeParameterActive)
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
                return stateTransition.hasExitTime || !Mathf.Approximately(stateTransition.duration, 0f) ||
                       !Mathf.Approximately(stateTransition.offset, 0f);
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
                        state.motion = canonical;
                        changed = true;
                    }
                }
                else if (state.motion is BlendTree tree &&
                         IsEffectivelyEmptyMotion(tree, bindingRoot, new HashSet<int>()))
                {
                    canonical ??= CreateCanonicalEmptyClip(controller);
                    state.motion = canonical;
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
                    clip.name, GeneratedAssetPrefix + "CanonicalEmpty", StringComparison.Ordinal));
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
                    child.state.writeDefaultValues || child.state.iKOnFeet || child.state.mirror ||
                    !Mathf.Approximately(child.state.speed, 1f) ||
                    !Mathf.Approximately(child.state.cycleOffset, 0f) ||
                    child.state.speedParameterActive || child.state.cycleOffsetParameterActive ||
                    child.state.mirrorParameterActive || child.state.timeParameterActive ||
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
                return true;
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

        private static void EnsureMmdCompatibilityLayers(AnimatorController controller)
        {
            var oldLayers = (controller.layers ?? Array.Empty<AnimatorControllerLayer>()).ToArray();
            var layers = oldLayers.ToList();
            var dummy1 = layers.FirstOrDefault(layer => layer != null &&
                string.Equals(layer.name, "MMD Dummy Layer 1", StringComparison.Ordinal));
            var dummy2 = layers.FirstOrDefault(layer => layer != null &&
                string.Equals(layer.name, "MMD Dummy Layer 2", StringComparison.Ordinal));
            layers.RemoveAll(layer => layer != null &&
                (string.Equals(layer.name, "MMD Dummy Layer 1", StringComparison.Ordinal) ||
                 string.Equals(layer.name, "MMD Dummy Layer 2", StringComparison.Ordinal)));
            if (layers.Count == 0)
                layers.Add(CreateMmdDummyLayer(controller, "Base Layer", 1f));
            dummy1 ??= CreateMmdDummyLayer(controller, "MMD Dummy Layer 1", 0f);
            dummy2 ??= CreateMmdDummyLayer(controller, "MMD Dummy Layer 2", 0f);
            dummy1.defaultWeight = 0f;
            dummy2.defaultWeight = 0f;
            layers.Insert(Math.Min(1, layers.Count), dummy1);
            layers.Insert(Math.Min(2, layers.Count), dummy2);
            for (var index = 0; index < layers.Count; index++)
            {
                var layer = layers[index];
                if (layer == null)
                    continue;
                var oldIndex = Array.FindIndex(oldLayers, old => old != null && old.stateMachine != null &&
                    old.stateMachine == layer.stateMachine);
                if (oldIndex < 0 || oldLayers[oldIndex].syncedLayerIndex < 0 ||
                    oldLayers[oldIndex].syncedLayerIndex >= oldLayers.Length)
                    continue;
                var oldTarget = oldLayers[oldIndex].syncedLayerIndex;
                var oldTargetLayer = oldLayers[oldTarget];
                if (oldTargetLayer == null || oldTargetLayer.stateMachine == null)
                {
                    layer.syncedLayerIndex = -1;
                    layer.syncedLayerAffectsTiming = false;
                    layers[index] = layer;
                    continue;
                }
                var targetStateMachine = oldTargetLayer.stateMachine;
                var newTarget = layers.FindIndex(candidate => candidate != null && candidate.stateMachine != null &&
                    candidate.stateMachine == targetStateMachine);
                layer.syncedLayerIndex = newTarget;
                if (newTarget < 0)
                    layer.syncedLayerAffectsTiming = false;
                layers[index] = layer;
            }
            controller.layers = layers.ToArray();
        }

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
                stateMachine = stateMachine,
            };
        }

        private static void NormalizeAlwaysOneParameters(
            AnimatorController controller,
            ICollection<string> messages)
        {
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
            foreach (var layer in controller?.layers ?? Array.Empty<AnimatorControllerLayer>())
            {
                CollectAnimatedAnimatorParameterNames(layer?.stateMachine, names, visited);
            }
            return names;
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
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
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
            var children = tree.children ?? Array.Empty<ChildMotion>();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrWhiteSpace(child.directBlendParameter) &&
                    aliases.TryGetValue(child.directBlendParameter, out canonical))
                    child.directBlendParameter = canonical;
                children[index] = child;
                RewriteAlwaysOneMotion(child.motion, aliases);
            }
            tree.children = children;
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
                var motion = state.motion;
                AnimatorOptimizerMotionUtility.TryNormalizeSingletonTree(motion, out motion);
                return new ChildMotion
                {
                    motion = motion,
                    threshold = index,
                    timeScale = state.speed,
                    cycleOffset = state.cycleOffset,
                    mirror = state.mirror,
                };
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        private static BlendTree BuildAapStateRegisterUpdateTree(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<string> inputParameters,
            int stateCount,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> nextStates,
            string name)
        {
            var stateTrees = new Motion[stateCount];
            for (var stateIndex = 0; stateIndex < stateCount; stateIndex++)
            {
                var values = nextStates[stateIndex].ToDictionary(
                    pair => pair.Key,
                    pair => (float)pair.Value);
                stateTrees[stateIndex] = BuildAapBooleanValueNode(
                    controller,
                    stateRegisterParameter,
                    inputParameters,
                    values,
                    0,
                    0,
                    name + "_State_" + stateIndex);
            }

            var root = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = stateRegisterParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            root.children = stateTrees.Select((motion, stateIndex) => new ChildMotion
            {
                motion = motion,
                threshold = stateIndex,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, root);
            return root;
        }

        private static BlendTree BuildAapFiniteStateRegisterUpdateTree(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            int stateCount,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> nextStates,
            string name)
        {
            if (dimensions == null || dimensions.Count == 0)
                throw new InvalidOperationException("AAP 有限 StateRegister 缺少输入维度。");
            var stateTrees = new Motion[stateCount];
            for (var stateIndex = 0; stateIndex < stateCount; stateIndex++)
            {
                var values = nextStates[stateIndex].ToDictionary(
                    pair => pair.Key,
                    pair => (float)pair.Value);
                stateTrees[stateIndex] = BuildAapFiniteValueNode(
                    controller,
                    stateRegisterParameter,
                    dimensions,
                    values,
                    0,
                    0,
                    1,
                    name + "_State_" + stateIndex);
            }

            var root = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = stateRegisterParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            root.children = stateTrees.Select((motion, stateIndex) => new ChildMotion
            {
                motion = motion,
                threshold = stateIndex,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, root);
            return root;
        }

        private static BlendTree BuildAapStateRegisterUpdateTreeWithInitialization(
            AnimatorController controller,
            string stateRegisterParameter,
            string initializedParameter,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            int stateCount,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> nextStates,
            IReadOnlyDictionary<int, int> entryStates,
            string name)
        {
            if (dimensions == null || dimensions.Count == 0 || entryStates == null)
                throw new InvalidOperationException("AAP StateRegister Entry 初始化计划不完整。");

            var normalTree = BuildAapFiniteStateRegisterUpdateTree(
                controller,
                stateRegisterParameter,
                dimensions,
                stateCount,
                nextStates,
                name + "_Normal");
            var entryValues = entryStates.ToDictionary(pair => pair.Key, pair => (float)pair.Value);
            var entryTree = BuildAapFiniteValueNode(
                controller,
                stateRegisterParameter,
                dimensions,
                entryValues,
                0,
                0,
                1,
                name + "_Entry",
                initializedParameter);
            var root = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = initializedParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            root.children = new[]
            {
                new ChildMotion
                {
                    motion = entryTree,
                    threshold = 0f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
                new ChildMotion
                {
                    motion = normalTree,
                    threshold = 1f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
            };
            AddSubAsset(controller, root);
            return root;
        }

        private static Motion BuildAapFiniteValueNode(
            AnimatorController controller,
            string outputParameter,
            IReadOnlyList<AnimatorOptimizerDecisionDimension> dimensions,
            IReadOnlyDictionary<int, float> values,
            int depth,
            int key,
            int stride,
            string name,
            string initializedParameter = null)
        {
            if (depth >= dimensions.Count)
                return CreateAapValueClip(controller, outputParameter, values[key], name + "_Value",
                    initializedParameter);

            var dimension = dimensions[depth];
            var tree = new BlendTree
            {
                name = name + "_Node_" + depth + "_" + key,
                blendType = BlendTreeType.Simple1D,
                blendParameter = dimension.Parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var childStride = stride * dimension.Values.Count;
            tree.children = dimension.Values.Select((value, index) => new ChildMotion
            {
                motion = BuildAapFiniteValueNode(
                    controller,
                    outputParameter,
                    dimensions,
                    values,
                    depth + 1,
                    key + index * stride,
                    childStride,
                    name,
                    initializedParameter),
                threshold = value,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        private static Motion BuildAapBooleanValueNode(
            AnimatorController controller,
            string outputParameter,
            IReadOnlyList<string> inputParameters,
            IReadOnlyDictionary<int, float> values,
            int depth,
            int mask,
            string name)
        {
            if (depth >= inputParameters.Count)
            {
                var value = values[mask];
                return CreateAapValueClip(controller, outputParameter, value,
                    name + "_Value_" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            var tree = new BlendTree
            {
                name = name + "_Node_" + depth + "_" + mask,
                blendType = BlendTreeType.Simple1D,
                blendParameter = inputParameters[depth],
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = new[]
            {
                new ChildMotion
                {
                    motion = BuildAapBooleanValueNode(controller, outputParameter, inputParameters,
                        values, depth + 1, mask, name),
                    threshold = 0f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
                new ChildMotion
                {
                    motion = BuildAapBooleanValueNode(controller, outputParameter, inputParameters,
                        values, depth + 1, mask | (1 << depth), name),
                    threshold = 1f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
            };
            AddSubAsset(controller, tree);
            return tree;
        }

        private static AnimationClip CreateAapValueClip(
            AnimatorController controller,
            string parameterName,
            float value,
            string name,
            string initializedParameter = null)
        {
            var clip = new AnimationClip
            {
                name = name,
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), parameterName);
            AnimationUtility.SetEditorCurve(clip, binding,
                AnimationCurve.Constant(0f, 1f / 60f, value));
            if (!string.IsNullOrWhiteSpace(initializedParameter))
            {
                var initializedBinding = EditorCurveBinding.FloatCurve(
                    string.Empty, typeof(Animator), initializedParameter);
                AnimationUtility.SetEditorCurve(clip, initializedBinding,
                    AnimationCurve.Constant(0f, 1f / 60f, 1f));
            }
            AddSubAsset(controller, clip);
            return clip;
        }

        private static string EnsureFloatParameterWithDefault(
            AnimatorController controller,
            string prefix,
            float defaultValue)
        {
            prefix = string.IsNullOrWhiteSpace(prefix) ? "AO/StateRegister" : prefix.Trim();
            var name = prefix;
            var suffix = 1;
            while ((controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .Any(parameter => string.Equals(parameter.name, name, StringComparison.Ordinal)))
                name = prefix + "_" + suffix++;
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
                var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath);
                if (copy == null)
                    throw new InvalidOperationException("复制后无法重新载入 AnimatorController: " + outputPath);
                ConvertInPlace(copy, selectedLayerIndices, options);
                return copy;
            }
            catch
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                    AssetDatabase.DeleteAsset(outputPath);
                throw;
            }
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
                .GroupBy(report => report.BlendingMode + "|" + report.MaskKey, StringComparer.Ordinal)
                .Select(group => new LayerGroup
                {
                    BlendingMode = group.First().BlendingMode,
                    MaskKey = group.First().MaskKey,
                    Mask = group.First().Mask,
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
                    (report.SourceLayer?.stateMachine?.behaviours ?? Array.Empty<StateMachineBehaviour>())
                    .Select(BuildBehaviourKey)
                    .ToList())
                .ToList();
            return behaviourKeys.All(keys => keys.Count == 0) ||
                   behaviourKeys.Skip(1).All(keys => keys.SequenceEqual(behaviourKeys[0], StringComparer.Ordinal));
        }

        private static string BuildBehaviourKey(StateMachineBehaviour behaviour)
        {
            if (!AnimatorOptimizerAnalyzer.IsPreservableLayerBehaviour(behaviour))
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

        private static StateMachineBehaviour[] ClonePreservableLayerBehaviours(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports)
        {
            if (reports == null || reports.Count == 0 || !CanPreserveLayerBehaviours(reports))
                return Array.Empty<StateMachineBehaviour>();
            var source = reports[0].SourceLayer?.stateMachine?.behaviours ?? Array.Empty<StateMachineBehaviour>();
            return source.Select(behaviour =>
            {
                try
                {
                    var clone = UnityEngine.Object.Instantiate(behaviour);
                    AddSubAsset(controller, clone);
                    return clone;
                }
                catch
                {
                    return null;
                }
            }).Where(behaviour => behaviour != null).ToArray();
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
                    !unique.Any(existing => ArePlansStructurallyEquivalent(existing.Plan, report.Plan) &&
                        string.Equals(existing.MaskKey, report.MaskKey, StringComparison.Ordinal)))
                    unique.Add(report);
            }
            return unique;
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
                       (left.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Count ==
                       (right.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Count &&
                       (left.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>()).Zip(
                           right.SelectorSamples ?? new List<AnimatorOptimizerSelectorSample>(),
                           (a, b) => Mathf.Approximately(a.Value, b.Value) &&
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
            return Mathf.Approximately(left.Speed, right.Speed) &&
                   Mathf.Approximately(left.CycleOffset, right.CycleOffset) &&
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
            if (blendingMode == AnimatorLayerBlendingMode.Additive)
            {
                for (var leftIndex = 0; leftIndex < reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < reports.Count; rightIndex++)
                    {
                        if (reports[leftIndex].Bindings.Intersect(reports[rightIndex].Bindings)
                            .Any(binding => binding.IsObjectReference))
                            return false;
                    }
                }
            }
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
            var unique = GetUniqueReports(reports);
            if (unique.Count <= 1)
                return true;
            if (reports.All(report => report.Plan != null &&
                    EnumeratePlanMotions(report.Plan).All(motion =>
                        motion != null && motion.Motion != null &&
                        AnimatorOptimizerMotionUtility.IsTimeIndependent(motion.Motion))))
                return true;
            if (reports.All(report => report.Plan != null &&
                    report.Plan.Kind == AnimatorOptimizerConversionKind.ConstantMotion))
            {
                var motions = reports.Select(report => report.Plan.ConstantMotion).ToList();
                if (motions.Any(motion => motion == null || motion.Motion == null || motion.Mirror))
                    return false;
                if (motions.All(motion => AnimatorOptimizerMotionUtility.IsTimeIndependent(motion.Motion)))
                    return true;
                return TryGetCompatibleTimedClips(reports, out _);
            }
            return false;
        }

        private static bool TryFlattenConstantDirectTree(
            BuiltMotion built,
            out IReadOnlyList<ChildMotion> flattenedChildren)
        {
            flattenedChildren = null;
            if (built == null || built.Motion is not BlendTree tree ||
                tree.blendType != BlendTreeType.Direct ||
                built.HasStateSettings &&
                (!Mathf.Approximately(built.Speed, 1f) ||
                 !Mathf.Approximately(built.CycleOffset, 0f) || built.Mirror) ||
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
            if (AnimatorOptimizerMotionUtility.IsPotentialProxyClip(firstClip) ||
                firstClip.events != null && firstClip.events.Length > 0)
                return false;
            var firstSettings = AnimationUtility.GetAnimationClipSettings(firstClip);
            if (firstSettings.hasAdditiveReferencePose || firstSettings.additiveReferencePoseClip != null)
                return false;
            foreach (var reference in references.Skip(1))
            {
                var clip = (AnimationClip)reference.Motion;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (AnimatorOptimizerMotionUtility.IsPotentialProxyClip(clip) ||
                    clip.events != null && clip.events.Length > 0 ||
                    settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip != null ||
                    !Mathf.Approximately(clip.frameRate, firstClip.frameRate) ||
                    !Mathf.Approximately(clip.length, firstClip.length) ||
                    clip.wrapMode != firstClip.wrapMode ||
                    !AreCompatibleClipSettings(firstSettings, settings) ||
                    !Mathf.Approximately(reference.Speed, first.Speed) ||
                    !Mathf.Approximately(reference.CycleOffset, first.CycleOffset) ||
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
                     Mathf.Approximately(left.additiveReferencePoseTime, right.additiveReferencePoseTime) &&
                     left.additiveReferencePoseClip == right.additiveReferencePoseClip &&
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
                for (var leftIndex = 0; leftIndex < reports.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < reports.Count; rightIndex++)
                    {
                        if (reports[leftIndex].Bindings.Intersect(reports[rightIndex].Bindings)
                            .Any(binding => binding.IsObjectReference))
                            return false;
                    }
                }
            }

            if (clips.Cast<AnimationClip>().All(AnimatorOptimizerMotionUtility.IsStaticClip))
            {
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
                legacy = false,
                frameRate = clips[0].frameRate,
                wrapMode = clips[0].wrapMode,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clips[0]);
            AnimationUtility.SetAnimationClipSettings(result, settings);
            foreach (var curve in floatCurves.Values)
                AnimationUtility.SetEditorCurve(result, curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                AnimationUtility.SetObjectReferenceCurve(result, curve.Binding, curve.Curve);
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
                if (!Mathf.Approximately(leftKeys[index].time, rightKeys[index].time) ||
                    leftKeys[index].weightedMode != rightKeys[index].weightedMode ||
                    !Mathf.Approximately(leftKeys[index].inWeight, rightKeys[index].inWeight) ||
                    !Mathf.Approximately(leftKeys[index].outWeight, rightKeys[index].outWeight) ||
                    float.IsInfinity(leftKeys[index].inTangent) || float.IsInfinity(leftKeys[index].outTangent) ||
                    float.IsInfinity(rightKeys[index].inTangent) || float.IsInfinity(rightKeys[index].outTangent))
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

        private static bool ValidatePriorityCombinations(
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values)
        {
            if (depth >= dimensions.Count)
            {
                foreach (var report in reports)
                {
                    if (EvaluatePlan(report.Plan, values) == null && report.Plan.Kind != AnimatorOptimizerConversionKind.ConstantMotion)
                        return false;
                }
                return true;
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
                    if (plan.SelectorParameterType != AnimatorControllerParameterType.Int)
                        return FailPriorityDimensions(out dimensions, out combinationCount);
                    foreach (var sample in plan.SelectorSamples ?? Enumerable.Empty<AnimatorOptimizerSelectorSample>())
                        AddPriorityValue(valuesByParameter, plan.SelectorParameter, sample.Value);
                }
                else if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
                {
                    foreach (var dimension in plan.DecisionDimensions ?? Enumerable.Empty<AnimatorOptimizerDecisionDimension>())
                        AddPriorityValue(valuesByParameter, dimension.Parameter, dimension.Values.ToArray());
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
                if (dimension.Values.Count == 0 || combinationCount > long.MaxValue / dimension.Values.Count)
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
                new Dictionary<string, float>(StringComparer.Ordinal), emptyClip, blendingMode == AnimatorLayerBlendingMode.Additive);
        }

        private static Motion BuildPriorityNode(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyList<PriorityDimension> dimensions,
            int depth,
            Dictionary<string, float> values,
            AnimationClip emptyClip,
            bool additive)
        {
            if (depth >= dimensions.Count)
                return CreatePriorityCompositeClip(controller, reports, values, emptyClip, additive);

            var dimension = dimensions[depth];
            var tree = new BlendTree
            {
                name = GeneratedAssetPrefix + "PriorityTree_" + depth + "_" + priorityClipCounter++,
                blendType = BlendTreeType.Simple1D,
                blendParameter = dimension.Parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, tree);

            tree.children = dimension.Values.Select(value =>
            {
                values[dimension.Parameter] = value;
                return new ChildMotion
                {
                    motion = BuildPriorityNode(controller, reports, dimensions, depth + 1, values, emptyClip, additive),
                    threshold = value,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                };
            }).ToArray();
            values.Remove(dimension.Parameter);
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
                var branch = plan.Branches?
                    .OrderBy(item => item.Order)
                    .FirstOrDefault(item => item.Conditions.All(condition => condition.Matches(selectorValue)));
                if (branch != null)
                    return branch.Motion;

                return plan.SelectorSamples?
                    .OrderBy(sample => Mathf.Abs(sample.Value - selectorValue))
                    .FirstOrDefault()?.Motion;
            }
            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
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

        private static AnimationClip CreatePriorityCompositeClip(
            AnimatorController controller,
            IReadOnlyList<AnimatorOptimizerLayerReport> reports,
            IReadOnlyDictionary<string, float> values,
            AnimationClip emptyClip,
            bool additive)
        {
            var floatCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, AnimationCurve Curve)>();
            var objectCurves = new Dictionary<AnimatorOptimizerBindingKey, (EditorCurveBinding Binding, ObjectReferenceKeyframe[] Curve)>();
            AnimationClip settingsSource = null;

            foreach (var report in reports.OrderBy(item => item.Index))
            {
                var motion = EvaluatePlan(report.Plan, values);
                if (motion?.Motion == null)
                    continue;
                if (!(motion.Motion is AnimationClip clip))
                    throw new InvalidOperationException("Priority composite 只能合并 AnimationClip Motion。");
                settingsSource = clip;

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

            var composite = new AnimationClip
            {
                name = GeneratedAssetPrefix + "PriorityClip_" + priorityClipCounter++,
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            if (settingsSource != null)
            {
                composite.frameRate = settingsSource.frameRate;
                var settings = AnimationUtility.GetAnimationClipSettings(settingsSource);
                AnimationUtility.SetAnimationClipSettings(composite, settings);
            }

            foreach (var curve in floatCurves.Values)
                AnimationUtility.SetEditorCurve(composite, curve.Binding, curve.Curve);
            foreach (var curve in objectCurves.Values)
                AnimationUtility.SetObjectReferenceCurve(composite, curve.Binding, curve.Curve);

            AddSubAsset(controller, composite);
            return composite;
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
            return AnimationCurve.Constant(0f, 1f / 60f, leftValue + rightValue);
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
                var tree = new BlendTree
                {
                    name = GeneratedAssetPrefix + "NumericSelector_" + plan.SelectorParameter,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = plan.SelectorParameter,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                AddSubAsset(controller, tree);
                tree.children = plan.SelectorSamples.Select(sample => ToChildMotion(sample.Value, sample.Motion, emptyClip)).ToArray();
                return new BuiltMotion { Motion = tree };
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.BooleanTruthTable)
            {
                var built = BuildTruthTree(controller, plan, emptyClip, 0, 0,
                    new Dictionary<string, BuiltTruthNode>(StringComparer.Ordinal));
                return new BuiltMotion { Motion = built.Motion };
            }

            if (plan.Kind == AnimatorOptimizerConversionKind.FiniteParameterTable)
            {
                var built = BuildFiniteDecisionTree(controller, plan, emptyClip, 0, 0, 1,
                    new Dictionary<string, BuiltTruthNode>(StringComparer.Ordinal));
                return new BuiltMotion { Motion = built.Motion };
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
                children.Add(new ChildMotion
                {
                    motion = child.Motion,
                    threshold = dimension.Values[index],
                    timeScale = child.LeafSettings == null ? 1f : child.LeafSettings.Speed,
                    cycleOffset = child.LeafSettings == null ? 0f : child.LeafSettings.CycleOffset,
                    mirror = child.LeafSettings != null && child.LeafSettings.Mirror,
                });
            }

            var nodeSignature = string.Join("|", children.Select(child =>
                BuildMotionSignature(new AnimatorOptimizerMotionRef
                {
                    Motion = child.motion,
                    Speed = child.timeScale,
                    CycleOffset = child.cycleOffset,
                    Mirror = child.mirror,
                })));
            if (children.All(child => string.Equals(
                    BuildMotionSignature(new AnimatorOptimizerMotionRef
                    {
                        Motion = child.motion,
                        Speed = child.timeScale,
                        CycleOffset = child.cycleOffset,
                        Mirror = child.mirror,
                    }),
                    BuildMotionSignature(new AnimatorOptimizerMotionRef
                    {
                        Motion = children[0].motion,
                        Speed = children[0].timeScale,
                        CycleOffset = children[0].cycleOffset,
                        Mirror = children[0].mirror,
                    }), StringComparison.Ordinal)))
                return firstNode;

            var memoKey = depth + ":" + dimension.Parameter + ":" +
                string.Join(",", dimension.Values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) +
                ":" + nodeSignature;
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
            tree.children = children.ToArray();
            AddSubAsset(controller, tree);
            var result = new BuiltTruthNode
            {
                Motion = tree,
                Signature = nodeSignature,
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

            var nodeSignature = falseNode.Signature + "|" + trueNode.Signature;
            var memoKey = depth + ":" + plan.BooleanParameters[depth] + ":" + nodeSignature;
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
                Signature = nodeSignature,
            };
            memo[memoKey] = result;
            return result;
        }

        private static string BuildMotionSignature(AnimatorOptimizerMotionRef motion)
        {
            if (motion == null)
                return "null";
            return (motion.Motion == null ? "null" : motion.Motion.GetInstanceID().ToString()) + ":" +
                   motion.Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.CycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   motion.Mirror;
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
                if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(parameter.name))
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
                transition.conditions = transition.conditions
                    .Select(condition => RewriteCondition(condition, boolParameters))
                    .ToArray();
            }
            return transitions;
        }

        private static AnimatorCondition RewriteCondition(
            AnimatorCondition condition,
            HashSet<string> boolParameters)
        {
            if (!boolParameters.Contains(condition.parameter))
                return condition;
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
            return condition;
        }

        private static bool HasPotentialParameterDriverFor(
            AnimatorController controller,
            string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
                return false;

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
            bool normalizeAlwaysOne)
        {
            if (normalizeAlwaysOne)
            {
                var existingAlwaysOne = FindCanonicalAlwaysOneParameter(controller);
                if (!string.IsNullOrWhiteSpace(existingAlwaysOne))
                    return existingAlwaysOne;
            }
            prefix = string.IsNullOrWhiteSpace(prefix) ? "AO/LayerWeight" : prefix.Trim();
            var name = prefix;
            var suffix = 1;
            while ((controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .Any(parameter => string.Equals(parameter.name, name, StringComparison.Ordinal)))
                name = prefix + "_" + suffix++;

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            return name;
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
