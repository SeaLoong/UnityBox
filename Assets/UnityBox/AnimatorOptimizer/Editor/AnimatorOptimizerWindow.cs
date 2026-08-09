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
        private Animator _animator;
        private RuntimeAnimatorController _controllerObject;
        private AnimatorController _controller;
        private AnimatorOptimizerReport _report;
        private AnimatorOptimizerOptions _options = new AnimatorOptimizerOptions();
        private readonly HashSet<int> _selectedLayers = new HashSet<int>();
        private Vector2 _scroll;
        private string _outputPath;
        private bool _showReasons = true;

        [MenuItem("Tools/UnityBox/Animator Optimizer")]
        public static AnimatorOptimizerWindow ShowWindow()
        {
            var window = GetWindow<AnimatorOptimizerWindow>("Animator Optimizer");
            window.minSize = new Vector2(760f, 520f);
            window.TryUseSelection();
            window.Repaint();
            return window;
        }

        public static AnimatorOptimizerWindow Open(AnimatorController controller)
        {
            var window = ShowWindow();
            window.SetTarget(controller, null);
            return window;
        }

        public void SetTarget(AnimatorController controller, AnimatorOptimizerOptions options)
        {
            _controller = controller;
            _controllerObject = controller;
            _animator = null;
            if (options != null)
                _options = options.Clone();
            _selectedLayers.Clear();
            UpdateDefaultOutputPath();
            Analyze();
            Repaint();
        }

        private void OnEnable()
        {
            TryUseSelection();
        }

        private void OnSelectionChange()
        {
            if (_controller == null)
                TryUseSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animator Optimizer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "将可证明为参数函数的状态机转换为 BlendTree；Bool 逻辑使用真值表，同一 Int/Float 参数使用 1D 选择树，" +
                "静态 Clip/BlendTree Motion 会先规范化并按属性合并。含时间、历史或 Behaviour 的部分会保留；AAP 运算图可显式生成。",
                MessageType.Info);

            DrawTargetFields();
            DrawOptions();
            DrawActions();
            if (_report == null)
                return;

            EditorGUILayout.Space(6f);
            DrawSummary();
            DrawIssues();
            DrawLayers();
        }

        private void DrawTargetFields()
        {
            EditorGUI.BeginChangeCheck();
            _animator = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("Animator (optional)", "拖入 Avatar 上的 Animator；会使用其 Runtime Animator Controller。"),
                _animator,
                typeof(Animator),
                true);
            _controllerObject = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                new GUIContent("Animator Controller", "拖入 AnimatorController 资源，或由上面的 Animator 自动解析。"),
                _controllerObject,
                typeof(RuntimeAnimatorController),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _controller = ResolveController();
                _selectedLayers.Clear();
                UpdateDefaultOutputPath();
                Analyze();
            }
        }

        private void DrawOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Conversion Options", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _options.UpgradeBoolParametersToFloat = EditorGUILayout.ToggleLeft(
                    new GUIContent("Upgrade Bool → Float", "本地 Animator Float 可作为 BlendTree 参数；VRChat Bool 会映射为 0/1。"),
                    _options.UpgradeBoolParametersToFloat);
                _options.UpgradeIntParametersToFloat = EditorGUILayout.ToggleLeft(
                    new GUIContent("Upgrade Int → Float", "VRChat Int 映射到不受 0~1 限制的本地 Float，保留整数阈值。"),
                    _options.UpgradeIntParametersToFloat);
                _options.AllowFloatConditionApproximation = EditorGUILayout.ToggleLeft(
                    new GUIContent("Approximate Float Conditions", "Transition 阶跃条件在 BlendTree 中会在阈值附近线性插值。"),
                    _options.AllowFloatConditionApproximation);
                _options.StrictCurveConflicts = EditorGUILayout.ToggleLeft(
                    new GUIContent("Strict Curve Conflicts", "推荐开启；关闭后可能改变原 Layer 覆盖优先级。"),
                    _options.StrictCurveConflicts);
                _options.MergeLayersWithEquivalentMasks = EditorGUILayout.ToggleLeft(
                    new GUIContent("Merge Equivalent Masks", "允许内容相同的 AvatarMask Layer 一起合并，并让生成层继承该 Mask。"),
                    _options.MergeLayersWithEquivalentMasks);
                _options.ResolveOverridePriorityWithStaticComposite = EditorGUILayout.ToggleLeft(
                    new GUIContent("Resolve Override Priority", "对静态 Clip 的重复曲线枚举 Bool/Int 输入并生成复合 Clip。"),
                    _options.ResolveOverridePriorityWithStaticComposite);
                _options.EnableAapStateRegisterOptimization = EditorGUILayout.ToggleLeft(
                    new GUIContent("AAP StateRegister Optimization",
                        "实验性：为无时间 Bool 状态机生成 AAP StateRegister 更新树和输出 BlendTree。"),
                    _options.EnableAapStateRegisterOptimization);
                _options.RemoveEffectivelyEmptyLayers = EditorGUILayout.ToggleLeft(
                    new GUIContent("Remove Effectively Empty Layers",
                        "删除不含可观测动画输出的非首层；1/2 帧空 Clip 只有在没有时间观察者时才视为空。"),
                    _options.RemoveEffectivelyEmptyLayers);
                _options.NormalizeAlwaysOneParameters = EditorGUILayout.ToggleLeft(
                    new GUIContent("Normalize AlwaysOne Parameters",
                        "统一不同插件生成的默认值为 1 的 AlwaysOne Direct 权重参数。"),
                    _options.NormalizeAlwaysOneParameters);
                _options.ExecuteLateOptimization = EditorGUILayout.ToggleLeft(
                    new GUIContent("Execute Optimization As Late As Possible",
                        "NDMF 使用 PlatformFinish，尽量等待 VRCFury/ASS 等构建处理完成。"),
                    _options.ExecuteLateOptimization);
                _options.PreserveMmdCompatibility = EditorGUILayout.ToggleLeft(
                    new GUIContent("Preserve MMD Compatibility Layers",
                        "保留首层并在索引 1、2 插入权重为 0 的 MMD Dummy Layer。"),
                    _options.PreserveMmdCompatibility);
                if (_options.ResolveOverridePriorityWithStaticComposite)
                    _options.MaxPriorityCombinations = EditorGUILayout.IntSlider(
                        "Max Priority Combinations", _options.MaxPriorityCombinations, 16, 4096);
                _options.GeneratedLayerName = EditorGUILayout.TextField("Generated Layer", _options.GeneratedLayerName);
                _options.WeightParameterPrefix = EditorGUILayout.TextField("Weight Parameter Prefix", _options.WeightParameterPrefix);
                if (EditorGUI.EndChangeCheck() && _controller != null)
                    Analyze();
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_controller == null))
                {
                    if (GUILayout.Button("Analyze", GUILayout.Height(28f)))
                        Analyze();
                }
                using (new EditorGUI.DisabledScope(_report == null))
                {
                    if (GUILayout.Button("Select Safe", GUILayout.Height(28f)))
                    {
                        SelectSafeLayers();
                        Repaint();
                    }
                    if (GUILayout.Button("Select All Convertible", GUILayout.Height(28f)))
                    {
                        SelectAllConvertible();
                        Repaint();
                    }
                    if (GUILayout.Button("Clear", GUILayout.Height(28f)))
                    {
                        _selectedLayers.Clear();
                        Repaint();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CanConvertSelection()))
                {
                    if (GUILayout.Button("Convert In Place", GUILayout.Height(30f)))
                        ConvertInPlace();
                    if (GUILayout.Button("Save Optimized Copy", GUILayout.Height(30f)))
                        SaveCopy();
                }
            }

            if (_controller != null)
            {
                _outputPath = EditorGUILayout.TextField("Copy Output", _outputPath);
                if (GUILayout.Button("Browse Output…", GUILayout.Width(150f)))
                    BrowseOutputPath();
            }
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Layers", _report.Layers.Count.ToString(), GUILayout.Width(120f));
                EditorGUILayout.LabelField("Convertible", _report.ConvertibleLayerCount.ToString(), GUILayout.Width(150f));
                EditorGUILayout.LabelField("Selected", _selectedLayers.Count.ToString(), GUILayout.Width(120f));
                EditorGUILayout.LabelField("Issues", GetSelectionIssues().Count.ToString(), GUILayout.Width(120f));
            }
        }

        private void DrawIssues()
        {
            var issues = GetSelectionIssues();
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前选择通过安全检查。转换会保留源 Motion，并生成一个 WD On Direct BlendTree。",
                    MessageType.None);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Blocking Issues", EditorStyles.boldLabel);
                foreach (var issue in issues.Take(24))
                    EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedMiniLabel);
                if (issues.Count > 24)
                    EditorGUILayout.LabelField($"还有 {issues.Count - 24} 个问题未显示。", EditorStyles.miniLabel);
            }
        }

        private void DrawLayers()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Use", EditorStyles.toolbarButton, GUILayout.Width(42f));
                GUILayout.Label("Index", EditorStyles.toolbarButton, GUILayout.Width(48f));
                GUILayout.Label("Layer", EditorStyles.toolbarButton, GUILayout.Width(245f));
                GUILayout.Label("Mode", EditorStyles.toolbarButton, GUILayout.Width(150f));
                GUILayout.Label("Motions / Bindings", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
                _showReasons = GUILayout.Toggle(_showReasons, "Reasons", EditorStyles.toolbarButton, GUILayout.Width(90f));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(240f), GUILayout.ExpandHeight(true));
            foreach (var layer in _report.Layers)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!layer.CanConvert))
                        {
                            var next = EditorGUILayout.Toggle(_selectedLayers.Contains(layer.Index), GUILayout.Width(42f));
                            if (next)
                                _selectedLayers.Add(layer.Index);
                            else
                                _selectedLayers.Remove(layer.Index);
                        }
                        EditorGUILayout.LabelField(layer.Index.ToString(), GUILayout.Width(48f));
                        EditorGUILayout.LabelField(layer.Name, layer.CanConvert ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(245f));
                        EditorGUILayout.LabelField(layer.CanConvert ? layer.ConversionKind.ToString() : "Unsupported", GUILayout.Width(150f));
                        var maskLabel = layer.Mask == null ? "No Mask" : "Masked";
                        EditorGUILayout.LabelField($"{layer.MotionCount} / {layer.AnimatedBindingCount} · {maskLabel}", EditorStyles.miniLabel);
                    }
                    foreach (var warning in layer.Warnings)
                        EditorGUILayout.LabelField("⚠ " + warning, EditorStyles.wordWrappedMiniLabel);
                    if (_showReasons && !layer.CanConvert)
                    {
                        foreach (var reason in layer.Reasons)
                            EditorGUILayout.LabelField("✕ " + reason, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Analyze()
        {
            _controller = ResolveController();
            if (_controller == null)
            {
                _report = null;
                _selectedLayers.Clear();
                return;
            }

            try
            {
                _report = AnimatorOptimizerAnalyzer.Analyze(_controller, _options);
                _selectedLayers.Clear();
                SelectSafeLayers();
                Debug.Log($"[AnimatorOptimizer] Analyzed '{_controller.name}': " +
                          $"layers={_report.Layers.Count}, convertible={_report.ConvertibleLayerCount}, selected={_selectedLayers.Count}.");
            }
            catch (Exception exception)
            {
                _report = null;
                _selectedLayers.Clear();
                Debug.LogError("[AnimatorOptimizer] Analyze failed: " + exception);
                EditorUtility.DisplayDialog("Analyze Failed", exception.Message, "OK");
            }
        }

        private void ConvertInPlace()
        {
            if (!CanConvertSelection() || !EditorUtility.DisplayDialog(
                    "Convert Animator Controller",
                    "这会修改源 AnimatorController。建议先提交版本控制；Unity Undo 可撤销对象修改。继续吗？",
                    "Convert",
                    "Cancel"))
                return;

            try
            {
                var result = AnimatorOptimizerConverter.ConvertInPlace(_controller, _selectedLayers, _options);
                Selection.activeObject = _controller;
                Analyze();
                Debug.Log($"[AnimatorOptimizer] Converted {result.MergedLayerCount} Layers in '{_controller.name}'.");
                EditorUtility.DisplayDialog("Conversion Complete", "已生成 AnimatorOptimizer 合并层。", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError("[AnimatorOptimizer] Convert failed: " + exception);
                EditorUtility.DisplayDialog("Convert Failed", exception.Message, "OK");
            }
        }

        private void SaveCopy()
        {
            if (!CanConvertSelection())
                return;
            var path = (_outputPath ?? string.Empty).Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                if (!EditorUtility.DisplayDialog("Overwrite Controller?", "输出 Controller 已存在，覆盖吗？", "Overwrite", "Cancel"))
                    return;
                AssetDatabase.DeleteAsset(path);
            }

            try
            {
                var copy = AnimatorOptimizerConverter.CreateOptimizedCopy(_controller, path, _selectedLayers, _options);
                Selection.activeObject = copy;
                EditorGUIUtility.PingObject(copy);
                EditorUtility.DisplayDialog("Copy Created", "已生成优化 Controller:\n" + path, "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError("[AnimatorOptimizer] Copy failed: " + exception);
                EditorUtility.DisplayDialog("Copy Failed", exception.Message, "OK");
            }
        }

        private void BrowseOutputPath()
        {
            var defaultName = string.IsNullOrWhiteSpace(_outputPath)
                ? _controller.name + "_AnimatorOptimizer.controller"
                : Path.GetFileName(_outputPath);
            var folder = Path.GetDirectoryName(_outputPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(folder) || !folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                folder = "Assets/UnityBox/AnimatorOptimizer/Generated";
            var path = EditorUtility.SaveFilePanelInProject(
                "Save optimized AnimatorController",
                defaultName,
                "controller",
                "请选择输出 Controller 路径",
                folder);
            if (!string.IsNullOrWhiteSpace(path))
                _outputPath = path.Replace('\\', '/');
        }

        private AnimatorController ResolveController()
        {
            if (_animator != null && _animator.runtimeAnimatorController is AnimatorController animatorController)
                return animatorController;
            return _controllerObject as AnimatorController;
        }

        private List<string> GetSelectionIssues()
        {
            return _report == null
                ? new List<string> { "还没有 AnimatorController 分析结果。" }
                : AnimatorOptimizerConverter.GetSelectionIssues(_report, _selectedLayers, _options).ToList();
        }

        private bool CanConvertSelection()
        {
            return _controller != null && _report != null && GetSelectionIssues().Count == 0;
        }

        private void SelectAllConvertible()
        {
            _selectedLayers.Clear();
            if (_report == null)
                return;
            foreach (var layer in _report.ConvertibleLayers)
                _selectedLayers.Add(layer.Index);
        }

        private void SelectSafeLayers()
        {
            _selectedLayers.Clear();
            if (_report == null)
                return;
            foreach (var layer in _report.ConvertibleLayers)
            {
                var conflicts = _report.Layers.Any(other =>
                    other.Index != layer.Index &&
                    ((!other.BindingsAreComplete && layer.Bindings.Count > 0) ||
                     (other.BindingsAreComplete && layer.Bindings.Intersect(other.Bindings).Any())));
                if (!conflicts)
                    _selectedLayers.Add(layer.Index);
            }
            if (_selectedLayers.Count < 2)
                _selectedLayers.Clear();
        }

        private void TryUseSelection()
        {
            if (_controller != null)
                return;
            if (Selection.activeObject is AnimatorController selectedController)
                SetTarget(selectedController, null);
            else if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent<Animator>(out var animator))
            {
                _animator = animator;
                _controller = ResolveController();
                UpdateDefaultOutputPath();
                Analyze();
            }
        }

        private void UpdateDefaultOutputPath()
        {
            if (_controller == null)
            {
                _outputPath = string.Empty;
                return;
            }
            var safeName = _controller.name;
            foreach (var character in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(character, '_');
            _outputPath = $"Assets/UnityBox/AnimatorOptimizer/Generated/{safeName}_AnimatorOptimizer.controller";
        }
    }
}
