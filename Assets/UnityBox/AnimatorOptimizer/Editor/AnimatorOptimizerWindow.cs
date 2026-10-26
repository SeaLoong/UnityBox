using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    public sealed class AnimatorOptimizerWindow : EditorWindow
    {
        [SerializeField] private RuntimeAnimatorController controllerObject;
        [SerializeField] private AnimatorOptimizerOptions options = new AnimatorOptimizerOptions();
        [SerializeField] private string outputPath;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private int filter;
        [SerializeField] private int tab;
        [SerializeField] private List<AnimatorStateMachine> selectedMachines = new List<AnimatorStateMachine>();
        [SerializeField] private List<string> selectedLayerNames = new List<string>();
        [SerializeField] private List<int> selectedLayerIndices = new List<int>();
        [SerializeField] private List<int> selectedLayerMatchCounts = new List<int>();
        [SerializeField] private string selectionLayoutKey;
        private readonly HashSet<int> selected = new HashSet<int>();
        private readonly HashSet<int> expanded = new HashSet<int>();
        private AnimatorOptimizerReport report;
        private AnimatorController analyzedController;
        private IReadOnlyList<string> issues = Array.Empty<string>();
        private Vector2 scroll;
        private Vector2 optionsScroll;
        private Vector2 detailScroll;
        private int estimatedGeneratedLayers = -1;
        private IReadOnlyList<IReadOnlyList<int>> synchronizedGroups = Array.Empty<IReadOnlyList<int>>();
        [SerializeField] private bool diagnosticsSelectedOnly;
        private string error;
        private string selectionNotice;
        private string operationError;
        private string resultMessage;
        private bool pending;
        private bool selectRecommended;
        private bool issuesDirty;
        private bool disposed;

        private AnimatorController Controller => controllerObject as AnimatorController;
        private bool CanConvert => Controller != null && report != null && !pending && !issuesDirty &&
            (selected.Count > 0 || synchronizedGroups.Count > 0) && issues.Count == 0 && !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Tools/UnityBox/Animator Optimizer")]
        public static AnimatorOptimizerWindow ShowWindow()
        {
            var window = GetWindow<AnimatorOptimizerWindow>("Animator Optimizer");
            window.minSize = new Vector2(680f, 480f);
            if (window.Controller == null)
                window.UseSelection();
            return window;
        }

        public static AnimatorOptimizerWindow Open(AnimatorController controller)
        {
            var window = ShowWindow();
            window.SetTarget(controller, null);
            return window;
        }

        public void SetTarget(AnimatorController controller, AnimatorOptimizerOptions configuration)
        {
            controllerObject = controller;
            options = configuration?.Clone() ?? new AnimatorOptimizerOptions();
            selected.Clear();
            selectedMachines.Clear();
            selectedLayerNames.Clear();
            selectedLayerIndices.Clear();
            selectedLayerMatchCounts.Clear();
            selectionLayoutKey = null;
            selectionNotice = null;
            expanded.Clear();
            report = null;
            synchronizedGroups = Array.Empty<IReadOnlyList<int>>();
            estimatedGeneratedLayers = -1;
            issues = Array.Empty<string>();
            resultMessage = null;
            operationError = null;
            if (controller != null)
            {
                var name = AnimatorOptimizerProcessor.SanitizeAssetName(controller.name);
                outputPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/{name}_Optimized.controller");
            }
            QueueAnalysis(true);
        }

        private void OnEnable()
        {
            disposed = false;
            minSize = new Vector2(680f, 480f);
            tab = Mathf.Clamp(tab, 0, 2);
            filter = Mathf.Clamp(filter, 0, 7);
            search ??= string.Empty;
            options ??= new AnimatorOptimizerOptions();
            selectedMachines ??= new List<AnimatorStateMachine>();
            selectedLayerNames ??= new List<string>();
            selectedLayerIndices ??= new List<int>();
            selectedLayerMatchCounts ??= new List<int>();
            Undo.undoRedoPerformed += Invalidate;
            Undo.postprocessModifications += OnModifications;
            EditorApplication.projectChanged += Invalidate;
            QueueAnalysis(false);
        }

        private void OnDisable()
        {
            disposed = true;
            Undo.undoRedoPerformed -= Invalidate;
            Undo.postprocessModifications -= OnModifications;
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.delayCall -= Analyze;
            EditorApplication.delayCall -= RefreshIssues;
            pending = false;
            issuesDirty = false;
        }

        private void Invalidate() => QueueAnalysis(false);

        private UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (Controller != null && modifications.Any(change => change.currentValue?.target is AnimatorController ||
                change.currentValue?.target is AnimatorState || change.currentValue?.target is AnimatorStateMachine ||
                change.currentValue?.target is AnimatorTransitionBase || change.currentValue?.target is Motion ||
                change.currentValue?.target is AvatarMask || change.currentValue?.target is StateMachineBehaviour))
                Invalidate();
            return modifications;
        }

        private void QueueAnalysis(bool recommend)
        {
            selectRecommended |= recommend;
            if (!pending)
                EditorApplication.delayCall += Analyze;
            pending = true;
            Repaint();
        }

        private void Analyze()
        {
            pending = false;
            if (disposed || this == null)
                return;
            error = null;
            selectionNotice = null;
            try
            {
                analyzedController = Controller;
                report = Controller == null ? null : AnimatorOptimizerAnalyzer.Analyze(Controller, options);
                synchronizedGroups = Controller == null ? Array.Empty<IReadOnlyList<int>>() :
                    AnimatorOptimizerConverter.GetSynchronizedLayerGroups(Controller, options);
                if (selectionLayoutKey != BuildSelectionLayoutKey()) expanded.Clear();
                selected.Clear();
                if (report != null)
                {
                    if (selectRecommended)
                        selected.UnionWith(AnimatorOptimizerConverter.GetRecommendedSelection(report, options));
                    else
                        RestoreSelection();
                }
                RememberSelection();
                RefreshIssues();
            }
            catch (Exception exception)
            {
                report = null;
                synchronizedGroups = Array.Empty<IReadOnlyList<int>>();
                selected.Clear();
                estimatedGeneratedLayers = -1;
                issues = Array.Empty<string>();
                error = exception.Message;
            }
            selectRecommended = false;
            Repaint();
        }

        private void SelectionChanged()
        {
            selectionNotice = null;
            RememberSelection();
            if (!issuesDirty)
                EditorApplication.delayCall += RefreshIssues;
            issuesDirty = true;
        }

        private void RememberSelection()
        {
            var layers = report?.Layers.Where(layer => selected.Contains(layer.Index)).ToList() ??
                new List<AnimatorOptimizerLayerReport>();
            selectedMachines = layers.Select(layer => layer.SourceLayer?.stateMachine).ToList();
            selectedLayerNames = layers.Select(layer => layer.Name).ToList();
            selectedLayerIndices = layers.Select(layer => layer.Index).ToList();
            selectedLayerMatchCounts = layers.Select(layer => report.Layers.Count(other =>
                other.SourceLayer?.stateMachine == layer.SourceLayer?.stateMachine && other.Name == layer.Name)).ToList();
            selectionLayoutKey = BuildSelectionLayoutKey();
        }

        private string BuildSelectionLayoutKey() => report == null ? null : string.Concat(report.Layers.Select(layer =>
            AnimatorOptimizerSynchronizedGraph.Atom(layer.Name) +
            AnimatorOptimizerSynchronizedGraph.Atom(layer.SourceLayer?.stateMachine == null ? "null" :
                layer.SourceLayer.stateMachine.GetInstanceID().ToString()) +
            AnimatorOptimizerSynchronizedGraph.Atom(layer.MaskKey) + ":" + layer.BlendingMode + ":" +
            layer.DefaultWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
            layer.SourceLayer?.syncedLayerIndex + ":" + layer.SourceLayer?.iKPass + ";"));

        private void RestoreSelection()
        {
            if (selectionLayoutKey != null && selectionLayoutKey == BuildSelectionLayoutKey())
                selected.UnionWith(report.ConvertibleLayers.Where(layer => selectedLayerIndices.Contains(layer.Index))
                    .Select(layer => layer.Index));
            else
            {
                for (var index = 0; index < selectedMachines.Count; index++)
                {
                    if (index < selectedLayerMatchCounts.Count && selectedLayerMatchCounts[index] != 1) continue;
                    var name = index < selectedLayerNames.Count ? selectedLayerNames[index] : null;
                    var matches = report.Layers.Where(layer => layer.SourceLayer?.stateMachine == selectedMachines[index] &&
                        (name == null || string.Equals(layer.Name, name, StringComparison.Ordinal))).ToList();
                    // Shared machines are not layer identities. If a changed layout
                    // is ambiguous, drop the bookmark instead of selecting every owner.
                    if (matches.Count == 1 && matches[0].CanConvert) selected.Add(matches[0].Index);
                }
            }
            if (selected.Count < selectedMachines.Count)
                selectionNotice = "部分原选择已失效或无法唯一定位，未自动扩大选集；请检查层列表。";
        }

        private void RefreshIssues()
        {
            if (disposed || this == null)
                return;
            estimatedGeneratedLayers = -1;
            try
            {
                issues = report == null || selected.Count == 0 ? Array.Empty<string>() :
                    AnimatorOptimizerConverter.GetSelectionIssues(report, selected, options);
                if (report != null && selected.Count > 0 && issues.Count == 0)
                {
                    var layers = report.Layers.Where(layer => selected.Contains(layer.Index)).ToList();
                    estimatedGeneratedLayers = layers.Count(layer => layer.ConversionKind == AnimatorOptimizerConversionKind.AapStateRegister) * 2 +
                        layers.Where(layer => layer.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister)
                            .GroupBy(layer => new { layer.BlendingMode, layer.MaskKey, layer.Plan.StateTag }).Count();
                }
            }
            catch (Exception exception)
            {
                issues = new[] { exception.Message };
            }
            issuesDirty = false;
            Repaint();
        }

        private void OnGUI()
        {
            AnimatorOptimizerEditorStyles.EnsureSkin();
            using var labels = new AnimatorOptimizerEditorStyles.LabelWidthScope(Mathf.Clamp(position.width * 0.36f, 130f, 230f));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(position.width < 540f ? "AO" : "ANIMATOR OPTIMIZER", EditorStyles.boldLabel, GUILayout.MinWidth(0f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("使用当前选择", EditorStyles.toolbarButton))
                    UseSelection();
                using (new EditorGUI.DisabledScope(Controller == null || pending))
                    if (GUILayout.Button("重新分析", EditorStyles.toolbarButton))
                        QueueAnalysis(false);
            }
            EditorGUI.BeginChangeCheck();
            var next = (RuntimeAnimatorController)EditorGUILayout.ObjectField("Controller", controllerObject,
                typeof(RuntimeAnimatorController), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetTarget(next as AnimatorController, null);
                controllerObject = next;
            }
            tab = GUILayout.Toolbar(tab, new[] { "分析与选择", "优化选项", "诊断与结果" }, GUILayout.Height(26f));
            EditorGUILayout.Space(4f);
            var shownTab = tab;
            if (shownTab == 0 && report != null)
                DrawAnalysisToolbar();
            var bodyScroll = shownTab == 0 ? scroll : shownTab == 1 ? optionsScroll : detailScroll;
            bodyScroll = EditorGUILayout.BeginScrollView(bodyScroll, GUILayout.ExpandHeight(true), GUILayout.MinHeight(40f));
            DrawNotices();
            if (shownTab == 0)
                DrawAnalysis();
            else if (shownTab == 1)
                DrawOptions();
            else
                DrawDiagnostics();
            EditorGUILayout.EndScrollView();
            if (shownTab == 0) scroll = bodyScroll;
            else if (shownTab == 1) optionsScroll = bodyScroll;
            else detailScroll = bodyScroll;
            DrawFooter();
        }

        private void DrawNotices()
        {
            if (controllerObject != null && Controller == null)
                EditorGUILayout.HelpBox("暂不支持 AnimatorOverrideController。请使用实际 AnimatorController，避免丢失 Override 映射。", MessageType.Warning);
            if (pending)
                EditorGUILayout.LabelField("等待重新分析… 保留手动选择，转换暂不可用。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(selectionNotice)) EditorGUILayout.HelpBox(selectionNotice, MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(operationError)) EditorGUILayout.HelpBox(operationError, MessageType.Error);
        }

        private void DrawAnalysisToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = GUILayout.TextField(search, AnimatorOptimizerEditorStyles.SearchField, GUILayout.MinWidth(40f));
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    search = string.Empty;
                    GUI.FocusControl(null);
                }
                filter = EditorGUILayout.Popup(filter, new[] { "全部", "可转换", "不可转换", "已选择", "自动简化", "有处理建议", "风险提示", "启发式计划" },
                    EditorStyles.toolbarPopup, GUILayout.Width(110f));
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(pending))
                {
                    if (GUILayout.Button("推荐选择", EditorStyles.toolbarButton))
                        QueueAnalysis(true);
                    if (GUILayout.Button(new GUIContent(position.width < 540f ? "选中筛选" : "选择筛选结果", "只添加当前筛选结果中可转换的层；仍会重新验证整个选集。"), EditorStyles.toolbarButton))
                    {
                        selected.UnionWith(FilteredLayers().Where(layer => layer.CanConvert).Select(layer => layer.Index));
                        SelectionChanged();
                    }
                    if (GUILayout.Button("清空", EditorStyles.toolbarButton))
                    {
                        selected.Clear();
                        SelectionChanged();
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(position.width < 540f ? "展开" : "展开筛选项", EditorStyles.toolbarButton))
                    expanded.UnionWith(FilteredLayers().Select(layer => layer.Index));
                if (GUILayout.Button("收起", EditorStyles.toolbarButton))
                    expanded.Clear();
            }
        }

        private void DrawAnalysis()
        {
            if (report == null)
            {
                EditorGUILayout.HelpBox("选择 Controller，或选中带 Animator 的对象后点击“使用当前选择”。", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Metric("总层数", report.Layers.Count);
                    Metric("可转换", report.ConvertibleLayerCount);
                    if (position.width >= 600f)
                    {
                        Metric("已选择", selected.Count);
                        Metric("有处理建议", report.Layers.Count(layer => layer.Suggestions.Count > 0));
                    }
                }
                if (position.width < 600f)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Metric("已选择", selected.Count);
                        Metric("有处理建议", report.Layers.Count(layer => layer.Suggestions.Count > 0));
                    }
                }
            }
            EditorGUILayout.LabelField("自动简化项属于计划预览，不会改写源 State；可转换不代表任意选集都能安全合并。", EditorStyles.wordWrappedMiniLabel);
            var heuristicCount = report.ConvertibleLayers.Count(layer => layer.UsesHeuristics);
            if (heuristicCount > 0)
                EditorGUILayout.LabelField($"{heuristicCount} 层采用启发式（黄色标记）；展开可查看实际假设，不代表逐帧等价。", EditorStyles.wordWrappedMiniLabel);
            if (synchronizedGroups.Count > 0)
                EditorGUILayout.HelpBox($"另有 {synchronizedGroups.Count} 组同步状态机可保留历史合并，预计减少 {synchronizedGroups.Sum(group => group.Count - 1)} 层。此独立步骤作用于整个 Controller，无需勾选不可转换层。", MessageType.Info);
            var visible = FilteredLayers().ToList();
            foreach (var layer in visible)
                DrawLayer(layer);
            if (visible.Count == 0)
                EditorGUILayout.LabelField("没有匹配的 Layer。", EditorStyles.centeredGreyMiniLabel);
        }

        private IEnumerable<AnimatorOptimizerLayerReport> FilteredLayers()
        {
            return report.Layers.Where(layer =>
                (filter == 0 || filter == 1 && layer.CanConvert || filter == 2 && !layer.CanConvert ||
                 filter == 3 && selected.Contains(layer.Index) || filter == 4 && layer.Optimizations.Count > 0 ||
                 filter == 5 && layer.Suggestions.Count > 0 || filter == 6 && layer.Warnings.Count > 0 ||
                 filter == 7 && layer.UsesHeuristics) &&
                (string.IsNullOrWhiteSpace(search) || (layer.Index + " " + layer.Name + " " +
                    string.Join(" ", layer.Reasons.Concat(layer.Warnings).Concat(layer.Optimizations).Concat(layer.Suggestions)))
                    .IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void DrawLayer(AnimatorOptimizerLayerReport layer)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var kind = layer.CanConvert ? layer.ConversionKind : AnimatorOptimizerConversionKind.Unsupported;
                var badgeText = (layer.UsesHeuristics ? "启发式 · " : string.Empty) + AnimatorOptimizerEditorStyles.KindLabel(kind);
                var badgeColor = layer.UsesHeuristics ? AnimatorOptimizerEditorStyles.WarningColor : AnimatorOptimizerEditorStyles.KindColor(kind);
                var compact = position.width < 540f;
                var header = EditorGUILayout.GetControlRect(false, AnimatorOptimizerEditorStyles.BadgeHeight, GUILayout.MinWidth(0f));
                var toggleRect = new Rect(header.x, header.y, 18f, header.height);
                var pingRect = new Rect(header.xMax - 44f, header.y, 44f, header.height);
                var badgeWidth = compact ? 0f : AnimatorOptimizerEditorStyles.Badge.CalcSize(new GUIContent(badgeText)).x + 6f;
                var foldoutRect = new Rect(header.x + 22f, header.y, Mathf.Max(0f, header.width - 72f - badgeWidth), header.height);
                using (new EditorGUI.DisabledScope(!layer.CanConvert || pending))
                {
                    var value = EditorGUI.Toggle(toggleRect, selected.Contains(layer.Index));
                    if (value != selected.Contains(layer.Index))
                    {
                        if (value) selected.Add(layer.Index); else selected.Remove(layer.Index);
                        SelectionChanged();
                    }
                }
                var title = $"[{layer.Index}]  {layer.Name}";
                var open = EditorGUI.Foldout(foldoutRect, expanded.Contains(layer.Index), new GUIContent(
                    AnimatorOptimizerEditorStyles.Ellipsize(title, AnimatorOptimizerEditorStyles.ClippedFoldout, foldoutRect.width - 14f), title),
                    true, AnimatorOptimizerEditorStyles.ClippedFoldout);
                if (open) expanded.Add(layer.Index); else expanded.Remove(layer.Index);
                if (!compact)
                    AnimatorOptimizerEditorStyles.DrawBadge(new Rect(pingRect.x - badgeWidth, header.y, badgeWidth - 6f, header.height),
                        badgeText, badgeColor);
                if (GUI.Button(pingRect, "定位", EditorStyles.miniButton)) EditorGUIUtility.PingObject(layer.SourceLayer?.stateMachine);
                if (compact)
                    AnimatorOptimizerEditorStyles.DrawBadge(badgeText, badgeColor);
                EditorGUILayout.LabelField($"{layer.BlendingMode}  ·  {layer.MotionCount} Motion  ·  {layer.AnimatedBindingCount} 绑定  ·  " +
                    (layer.Mask == null ? "无 Mask" : layer.Mask.name), EditorStyles.wordWrappedMiniLabel);
                if (expanded.Contains(layer.Index))
                {
                    DrawLayerDiagnostics(layer);
                    if (layer.RequiredFloatParameters.Count > 0)
                        EditorGUILayout.LabelField("Float 输入：" + string.Join(", ", layer.RequiredFloatParameters),
                            AnimatorOptimizerEditorStyles.Detail);
                }
                else if (!layer.CanConvert && layer.Reasons.Count > 0)
                    EditorGUILayout.LabelField(layer.Reasons[0], EditorStyles.wordWrappedMiniLabel);
                else if (layer.Optimizations.Count > 0)
                    EditorGUILayout.LabelField($"{layer.Optimizations.Count} 项自动简化 / 证明记录" +
                        (layer.Warnings.Count > 0 ? $"  ·  {layer.Warnings.Count} 项风险或兼容提示" : "  ·  无额外风险提示"),
                        EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawLayerDiagnostics(AnimatorOptimizerLayerReport layer)
        {
            DrawMessages("阻止转换", layer.Reasons, AnimatorOptimizerEditorStyles.DetailError);
            DrawMessages(layer.CanConvert ? "自动简化 · 计划预览" : "已识别的可简化部分 · 整层仍未通过",
                layer.Optimizations, AnimatorOptimizerEditorStyles.DetailSuccess);
            DrawMessages("实际采用的启发式假设", layer.HeuristicAssumptions, AnimatorOptimizerEditorStyles.DetailWarning);
            DrawMessages("风险与兼容提示", layer.Warnings.Where(warning => !layer.UsesHeuristics ||
                !warning.StartsWith("启发式：", StringComparison.Ordinal)).ToList(), AnimatorOptimizerEditorStyles.DetailWarning);
            DrawMessages("建议处理方式", layer.Suggestions, AnimatorOptimizerEditorStyles.Detail);
            if (layer.Suggestions.Count > 0 && GUILayout.Button("查看优化选项（不自动更改配置）", EditorStyles.miniButton))
                tab = 1;
        }

        private static void DrawMessages(string title, IReadOnlyList<string> messages, GUIStyle style)
        {
            if (messages.Count == 0) return;
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            foreach (var message in messages)
                EditorGUILayout.LabelField("• " + message, style);
        }

        private static void Metric(string label, int value)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(value.ToString(), AnimatorOptimizerEditorStyles.SummaryValue);
                GUILayout.Label(label, AnimatorOptimizerEditorStyles.SummaryLabel);
            }
        }

        private void DrawOptions()
        {
            GUIContent ToggleLabel(string label) => AnimatorOptimizerEditorStyles.FieldLabel(label, minimumFieldWidth: 24f);
            GUIContent FieldLabel(string label) => AnimatorOptimizerEditorStyles.FieldLabel(label, minimumFieldWidth: 110f);
            var before = AnimatorOptimizerEditorStyles.BuildOptionsKey(options);
            EditorGUILayout.LabelField("先选优化方式，再按需要启用 AAP。改变选项不会自动扩大手动选层。", EditorStyles.wordWrappedMiniLabel);
            options.Heuristics = AnimatorOptimizerEditorStyles.DrawHeuristics(options.Heuristics, "Window", out var details);
            if (details && (options.Heuristics & AnimatorOptimizerComponent.HeuristicFlags.ShortAnimationEndpoints) != 0)
            {
                options.ShortAnimationMaxFrames = EditorGUILayout.IntSlider(FieldLabel("短动画最大帧数"), options.ShortAnimationMaxFrames, 1, 10);
                options.ShortAnimationMaxSeconds = EditorGUILayout.Slider(FieldLabel("短动画最大播放秒数"), options.ShortAnimationMaxSeconds, 0.01f, 0.25f);
            }
            if (details && (options.Heuristics & AnimatorOptimizerComponent.HeuristicFlags.SampleMotionTime) != 0)
                options.MotionTimeSampleCount = EditorGUILayout.IntSlider(FieldLabel("Motion Time 基础采样点数"), options.MotionTimeSampleCount, 2, 33);
            EditorGUILayout.Space(8f);
            options.MergeSynchronizedStateMachines = EditorGUILayout.Toggle(ToggleLabel("合并同图状态机"), options.MergeSynchronizedStateMachines);
            options.RemoveEffectivelyEmptyLayers = EditorGUILayout.Toggle(ToggleLabel("清理无效果空层"), options.RemoveEffectivelyEmptyLayers);
            options.EnableAapStateRegisterOptimization = EditorGUILayout.Toggle(ToggleLabel("AAP 状态运算（实验性）"), options.EnableAapStateRegisterOptimization);
            if (options.EnableAapStateRegisterOptimization) AnimatorOptimizerEditorStyles.DrawAapSummary();
            EditorGUILayout.Space(8f);
            if (AnimatorOptimizerEditorStyles.Section("Window.Parameters", "参数与合并（高级）", false))
            {
                options.UpgradeBoolParametersToFloat = EditorGUILayout.Toggle(ToggleLabel("Bool → Float"), options.UpgradeBoolParametersToFloat);
                options.UpgradeIntParametersToFloat = EditorGUILayout.Toggle(ToggleLabel("Int → Float"), options.UpgradeIntParametersToFloat);
                EditorGUILayout.LabelField("外部 Unity 代码需同步调整 SetBool / SetInteger 调用；VRChat Expression 参数沿用 0/1 或整数输入约定。", EditorStyles.wordWrappedMiniLabel);
                options.WriteDefaultsPolicy = (AnimatorOptimizerComponent.WriteDefaultsPolicy)EditorGUILayout.EnumPopup(FieldLabel("Write Defaults 策略"), options.WriteDefaultsPolicy);
                options.StrictCurveConflicts = EditorGUILayout.Toggle(ToggleLabel("严格冲突检查"), options.StrictCurveConflicts);
                options.MergeLayersWithEquivalentMasks = EditorGUILayout.Toggle(ToggleLabel("合并等价 Mask"), options.MergeLayersWithEquivalentMasks);
                options.ResolveOverridePriorityWithStaticComposite = EditorGUILayout.Toggle(ToggleLabel("静态曲线优先级合成"), options.ResolveOverridePriorityWithStaticComposite);
                options.RequireAtLeastTwoLayers = EditorGUILayout.Toggle(AnimatorOptimizerEditorStyles.FieldLabel("普通转换至少选两层", "关闭后可单独转换一个普通状态机；AAP 不受此数量限制。", 24f), options.RequireAtLeastTwoLayers);
                EditorGUILayout.LabelField("同图合并是全 Controller 清理步骤，保留 Entry/Exit 与 WD；Avatar 组件仅在自动选层模式执行。", EditorStyles.wordWrappedMiniLabel);
            }
            if (AnimatorOptimizerEditorStyles.Section("Window.Relaxation", "额外放宽（可能改变效果）", false))
            {
                options.NormalizeAlwaysOneParameters = EditorGUILayout.Toggle(ToggleLabel("假定 AlwaysOne 恒为 1"), options.NormalizeAlwaysOneParameters);
                options.AllowFloatConditionApproximation = EditorGUILayout.Toggle(ToggleLabel("Float 近似回退"), options.AllowFloatConditionApproximation);
                if (options.AllowFloatConditionApproximation)
                    options.FloatConditionEpsilon = EditorGUILayout.FloatField(FieldLabel("阈值近似半宽"), options.FloatConditionEpsilon);
                options.IgnoreTransitionTimingForStaticMotions = EditorGUILayout.Toggle(ToggleLabel("忽略静态过渡时间"), options.IgnoreTransitionTimingForStaticMotions);
                EditorGUILayout.LabelField("分别假定权重恒为 1、允许阈值邻域插值、取消静态状态间的等待/淡入淡出；不会绕过 Trigger 或 Behaviour 保护。", EditorStyles.wordWrappedMiniLabel);
            }
            if (!options.StrictCurveConflicts || options.NormalizeAlwaysOneParameters || options.AllowFloatConditionApproximation ||
                options.IgnoreTransitionTimingForStaticMotions || options.WriteDefaultsPolicy == AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn)
                EditorGUILayout.HelpBox("额外放宽设置已启用；可能改变默认值、过渡或覆盖顺序。请查看逐层诊断后保存副本。", MessageType.Warning);
            if (AnimatorOptimizerEditorStyles.Section("Window.Context", "上下文、预算与输出（高级）", false))
            {
                options.HasKnownPlayableLayer = EditorGUILayout.Toggle(ToggleLabel("指定 Playable 上下文"), options.HasKnownPlayableLayer);
                if (options.HasKnownPlayableLayer)
                    options.TargetPlayableLayer = (AnimatorOptimizerComponent.PlayableLayer)EditorGUILayout.EnumPopup(FieldLabel("Playable Layer"), options.TargetPlayableLayer);
                options.PreserveMmdCompatibility = EditorGUILayout.Toggle(ToggleLabel("MMD 兼容槽位"), options.PreserveMmdCompatibility);
                options.MaxPriorityCombinations = EditorGUILayout.IntSlider(FieldLabel("有限组合预算"), options.MaxPriorityCombinations, 16, 4096);
                if (options.EnableAapStateRegisterOptimization)
                    options.MaxAapDecisionNodes = EditorGUILayout.IntSlider(FieldLabel("AAP 共享条件节点预算"), options.MaxAapDecisionNodes, 64, 16384);
                options.GeneratedLayerName = EditorGUILayout.TextField(FieldLabel("生成层名称"), options.GeneratedLayerName);
                options.WeightParameterPrefix = EditorGUILayout.TextField(FieldLabel("权重参数前缀"), options.WeightParameterPrefix);
                EditorGUILayout.LabelField("执行时机由 Avatar 组件控制；离线副本操作立即执行。", EditorStyles.wordWrappedMiniLabel);
            }
            // Opening a foldout is not an option edit and must not enqueue analysis.
            if (before != AnimatorOptimizerEditorStyles.BuildOptionsKey(options))
            {
                options = options.Clone();
                QueueAnalysis(false);
            }
            using (new EditorGUI.DisabledScope(Controller == null || pending))
                if (GUILayout.Button("按当前选项重新推荐选层", EditorStyles.miniButton)) QueueAnalysis(true);
        }

        private void DrawDiagnostics()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                diagnosticsSelectedOnly = GUILayout.Toggle(diagnosticsSelectedOnly, "仅所选层", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(report == null))
                    if (GUILayout.Button("复制完整诊断", EditorStyles.toolbarButton))
                        EditorGUIUtility.systemCopyBuffer = BuildDiagnosticsText();
            }
            if (!string.IsNullOrEmpty(resultMessage))
                EditorGUILayout.HelpBox(resultMessage, MessageType.Info);
            if (selected.Count == 0 && synchronizedGroups.Count == 0)
                EditorGUILayout.HelpBox("尚未选择 Layer。可先使用“推荐选择”，或按层展开查看不可转换原因。", MessageType.Info);
            foreach (var issue in issues)
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            foreach (var group in synchronizedGroups)
                EditorGUILayout.HelpBox($"同步合并 [{string.Join(", ", group)}] → 1 层：保留原状态机，不是无历史 BlendTree 转换。", MessageType.Info);
            if (report != null)
            {
                foreach (var layer in report.Layers.Where(layer => !diagnosticsSelectedOnly || selected.Contains(layer.Index)))
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            AnimatorOptimizerEditorStyles.ClippedText($"[{layer.Index}] {layer.Name}", AnimatorOptimizerEditorStyles.ClippedTitle);
                            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
                                EditorGUIUtility.PingObject(layer.SourceLayer?.stateMachine);
                        }
                        DrawLayerDiagnostics(layer);
                    }
                }
            }
        }

        private string BuildDiagnosticsText()
        {
            if (report == null) return string.Empty;
            return $"Controller: {AssetDatabase.GetAssetPath(report.Controller)}\n静态分析预览；未执行 Unity 运行验证。\n" +
                "Options: " + JsonUtility.ToJson(options) + "\n" +
                string.Join("\n\n", report.Layers.Select(layer =>
                    $"[{layer.Index}] {layer.Name}: {(layer.CanConvert ? layer.ConversionKind.ToString() : "Blocked")}\n" +
                    string.Join("\n", layer.Reasons.Select(message => "[阻止] " + message)
                        .Concat(layer.Optimizations.Select(message => "[自动简化] " + message))
                        .Concat(layer.Warnings.Select(message => "[提示] " + message))
                        .Concat(layer.Suggestions.Select(message => "[建议] " + message))))) +
                "\n\n选集检查:\n" + string.Join("\n", issues) +
                "\n\n同步状态机全图合并:\n" + string.Join("\n", synchronizedGroups.Select(group =>
                    "[" + string.Join(", ", group) + "] → 1 层（保留历史）"));
        }

        private void DrawFooter()
        {
            AnimatorOptimizerEditorStyles.Separator();
            var estimate = string.Empty;
            if (!pending && !issuesDirty && estimatedGeneratedLayers >= 0)
                estimate = $"所选 {selected.Count} → {estimatedGeneratedLayers} 层 · 净变化 {estimatedGeneratedLayers - selected.Count:+0;-0;0} 层";
            using (new EditorGUILayout.HorizontalScope())
            {
                var status = pending || issuesDirty ? "分析更新中…" : !string.IsNullOrEmpty(error) ? "分析失败，请查看上方错误" : selected.Count == 0 ?
                    synchronizedGroups.Count > 0 ? "仅执行同步状态机合并 / 清理" : "未选择 Layer" :
                    issues.Count == 0 ? estimate + " · 静态检查通过" : $"{issues.Count} 项阻止转换，请查看诊断";
                AnimatorOptimizerEditorStyles.ClippedText(status, tooltip: status +
                    "\n层数估计不含后续同步合并、空层清理及 MMD 槽位，不代表运行耗时；未做 Unity 运行验证。");
                if (issues.Count > 0 && GUILayout.Button("查看原因", EditorStyles.miniButton, GUILayout.Width(68f)))
                    tab = 2;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("副本路径", GUILayout.Width(58f));
                outputPath = EditorGUILayout.TextField(outputPath ?? string.Empty, GUILayout.MinWidth(0f));
                if (GUILayout.Button("浏览…", GUILayout.Width(60f)))
                {
                    var path = EditorUtility.SaveFilePanelInProject("保存优化副本", Controller == null ? "Optimized" : Controller.name + "_Optimized",
                        "controller", "选择新的资源路径；不覆盖现有资源。", "Assets");
                    if (!string.IsNullOrEmpty(path)) outputPath = path;
                }
            }
            using (new EditorGUI.DisabledScope(!CanConvert))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(position.width < 420f ? "保存副本" : "保存优化副本（推荐）", "在新路径保存，不替换源 Controller。"), GUILayout.MinWidth(0f), GUILayout.Height(30f)))
                    Convert(false);
                if (GUILayout.Button("原地修改…", GUILayout.Width(position.width < 420f ? 96f : 130f), GUILayout.Height(30f)))
                    Convert(true);
            }
        }

        private bool HasSemanticChanges() => !options.StrictCurveConflicts || options.AllowFloatConditionApproximation ||
            options.Heuristics != AnimatorOptimizerComponent.HeuristicFlags.None ||
            options.NormalizeAlwaysOneParameters ||
            options.IgnoreTransitionTimingForStaticMotions || options.EnableAapStateRegisterOptimization ||
            options.WriteDefaultsPolicy == AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn;

        private void Convert(bool inPlace)
        {
            if (!CanConvert || Controller != analyzedController)
                return;
            if ((inPlace || HasSemanticChanges()) && !EditorUtility.DisplayDialog("确认优化",
                (inPlace ? "将修改源 Controller；请先备份或提交版本控制，不应依赖 Undo 完整恢复所有子资源。\n\n" : "将保存新副本。\n\n") +
                (HasSemanticChanges() ? "已启用可能改变语义的选项，请先检查诊断中的警告。\n" +
                    string.Join("\n", report.Layers.Where(layer => selected.Contains(layer.Index))
                        .SelectMany(layer => layer.HeuristicAssumptions.Select(assumption => $"[{layer.Index}] {assumption}")).Take(4)) : "继续吗？"), "继续", "取消"))
                return;
            try
            {
                if (inPlace)
                {
                    if (selected.Count == 0)
                    {
                        resultMessage = string.Join("\n", AnimatorOptimizerConverter.CleanupControllerInPlace(Controller, null, options));
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        var result = AnimatorOptimizerConverter.ConvertInPlace(Controller, selected.ToArray(), options);
                        resultMessage = $"已转换 {result.MergedLayerCount} 层，生成 {result.GeneratedLayers.Count} 层。\n" + string.Join("\n", result.Warnings);
                    }
                    QueueAnalysis(false);
                }
                else
                {
                    var copy = AnimatorOptimizerConverter.CreateOptimizedCopy(Controller, outputPath, selected.ToArray(), options);
                    resultMessage = "优化副本已保存：" + AssetDatabase.GetAssetPath(copy) + "\n源 Controller 未替换。";
                    EditorGUIUtility.PingObject(copy);
                    outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
                }
                operationError = null;
                tab = 2;
            }
            catch (Exception exception)
            {
                operationError = exception.Message;
                tab = 2;
                if (inPlace)
                    QueueAnalysis(false);
            }
        }

        private void UseSelection()
        {
            if (Selection.activeObject is AnimatorController controller)
                SetTarget(controller, null);
            else if (Selection.activeGameObject != null)
            {
                var component = Selection.activeGameObject.GetComponentInParent<AnimatorOptimizerComponent>();
                if (component != null)
                    SetTarget(AnimatorOptimizerEditor.ResolveController(component), AnimatorOptimizerConfigData.CreateOptions(component));
                else
                {
                    var animator = Selection.activeGameObject.GetComponent<Animator>();
                    if (animator != null)
                        SetTarget(animator.runtimeAnimatorController as AnimatorController, null);
                }
            }
        }
    }
}
