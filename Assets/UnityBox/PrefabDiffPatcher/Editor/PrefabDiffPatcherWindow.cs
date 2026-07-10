using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityBox.PrefabDiffPatcher
{
    public class PrefabDiffPatcherWindow : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/UnityBox/Generated/PrefabDiffPatcher";
        private const float TreeIndentWidth = 18f;
        private static readonly GUIContent SourceAContent = new("A (current/customized)", "当前保留为基底的对象。若它是预制件实例/资源，生成结果会尽量保持基于它。\nPatch 的目标是把 B 的选中差异应用到 A。\n");
        private static readonly GUIContent SourceBContent = new("B (patch donor / FT)", "提供差异内容的对象。选中某个路径后，生成结果会优先使用 B 的原始对象/子树。\n");

        private GameObject _sourceA;
        private GameObject _sourceB;
        private string _outputPath = DefaultOutputFolder + "/PatchedAvatar.prefab";
        private DiffNode _diffRoot;
        private Vector2 _detailScroll;
        private TreeViewState _treeViewState;
        private PrefabDiffTreeView _treeView;
        private readonly Dictionary<string, bool> _selectionByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _componentSelectionById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _foldoutByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TreeSelectionAggregate> _selectionAggregateByPath = new(StringComparer.Ordinal);

        [MenuItem("Tools/UnityBox/Prefab Diff Patcher")]
        public static void ShowWindow()
        {
            GetWindow<PrefabDiffPatcherWindow>("Prefab Diff Patcher");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Prefab Diff Patcher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "用途：把 B（例如新生成的 FT 模型）里选中的对象差异应用到 A（当前已做过大量改动的 Avatar）上。\n" +
                "选择某个对象路径后，会以该路径在 B 中的原始对象/子树作为基底，再把 A 中该子树独有的对象/组件补回去；若同一位置双方都有内容，则以 B 为准，并尽量自动修复对象/组件引用。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _sourceA = (GameObject)EditorGUILayout.ObjectField(SourceAContent, _sourceA, typeof(GameObject), true);
            _sourceB = (GameObject)EditorGUILayout.ObjectField(SourceBContent, _sourceB, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                EnsureDefaultOutputPath();
                _diffRoot = null;
                _treeView = null;
                _detailScroll = Vector2.zero;
                _selectionByPath.Clear();
                _componentSelectionById.Clear();
                _foldoutByPath.Clear();
            }

            DrawOutputPath();
            DrawTopButtons();

            if (_diffRoot != null)
            {
                EditorGUILayout.Space(6);
                DrawSelectionToolbar();
                DrawDiffSummary();
                DrawDiffTree();
                DrawApplyButton();
            }
        }

        private void DrawOutputPath()
        {
            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Prefab", _outputPath);
            if (GUILayout.Button("Browse…", GUILayout.MaxWidth(80)))
            {
                var defaultName = string.IsNullOrWhiteSpace(Path.GetFileName(_outputPath))
                    ? BuildDefaultPrefabName()
                    : Path.GetFileName(_outputPath);
                var chosen = EditorUtility.SaveFilePanelInProject(
                    "Save patched prefab",
                    defaultName,
                    "prefab",
                    "请选择输出 Prefab 路径",
                    GetExistingFolderOrFallback(_outputPath));
                if (!string.IsNullOrEmpty(chosen))
                    _outputPath = chosen.Replace('\\', '/');
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTopButtons()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_sourceA == null || _sourceB == null))
            {
                if (GUILayout.Button("Analyze Diff", GUILayout.Height(28)))
                {
                    Analyze();
                }
            }

            using (new EditorGUI.DisabledScope(_diffRoot == null))
            {
                if (GUILayout.Button("Refresh", GUILayout.Height(28)))
                    Analyze();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectionToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Width(100)))
            {
                SelectAllChanges();
                Repaint();
            }

            if (GUILayout.Button("Objects Only", GUILayout.Width(110)))
            {
                _selectionByPath.Clear();
                _componentSelectionById.Clear();
                SetObjectSelectionsRecursive(_diffRoot, true);
                Repaint();
            }

            if (GUILayout.Button("Components Only", GUILayout.Width(125)))
            {
                _selectionByPath.Clear();
                _componentSelectionById.Clear();
                SelectAllComponents();
                Repaint();
            }

            if (GUILayout.Button("Expand All", GUILayout.Width(100)))
            {
                _treeView?.ExpandAll();
                Repaint();
            }

            if (GUILayout.Button("Collapse All", GUILayout.Width(100)))
            {
                _treeView?.CollapseAll();
                Repaint();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(80)))
            {
                _selectionByPath.Clear();
                _componentSelectionById.Clear();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "勾选单位是“对象路径”。应用时会按对象子树处理：\n" +
                "- 对象、子对象、组件三者的选择彼此独立\n" +
                "- 选择对象：仅表示该路径的“对象本体”使用 B 原始对象为优先基底\n" +
                "- 选择组件：仅表示该组件差异按 B 优先应用\n" +
                "- 分析完成后默认全选所有变化\n" +
                "- 勾选父对象时会默认联动勾选子对象和组件，之后仍可手动取消局部选择\n" +
                "- 父节点支持三态勾选：全选 / 半选 / 未选\n" +
                "- 顶部快速选择按钮会直接递归修改整棵树的选择状态\n" +
                "- 未选择的子对象/组件会尽量保持 A 的内容不变\n" +
                "- 若某节点只存在于 B，则只有它自身或其后代被选中时，才会被引入结果中",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffSummary()
        {
            var changedNodes = EnumerateChangedNodes(_diffRoot).ToList();
            var selectedNodes = GetSelectedPaths().ToList();
            var selectedComponents = GetSelectedComponentSelections().ToList();
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Changed Nodes: {changedNodes.Count}", EditorStyles.boldLabel, GUILayout.Width(170));
            EditorGUILayout.LabelField($"Selected Objects: {selectedNodes.Count}", EditorStyles.boldLabel, GUILayout.Width(180));
            EditorGUILayout.LabelField($"Selected Components: {selectedComponents.Count}", EditorStyles.boldLabel, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDiffTree()
        {
            _selectionAggregateByPath.Clear();
            BuildSelectionStateCache(_diffRoot);
            EnsureTreeView();

            using (new EditorGUILayout.HorizontalScope(GUILayout.MinHeight(420f)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(420f), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    DrawTreeHeader();
                    var treeRect = GUILayoutUtility.GetRect(0f, 100000f, 320f, 460f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    _treeView?.OnGUI(treeRect);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(Mathf.Max(320f, position.width * 0.38f)), GUILayout.ExpandHeight(true)))
                {
                    DrawSelectedNodeDetails();
                }
            }
        }

        private void DrawApplyButton()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!GetSelectedPaths().Any() && !GetSelectedComponentSelections().Any()))
            {
                if (GUILayout.Button("Apply Patch And Generate Prefab", GUILayout.Height(34)))
                {
                    ApplyPatch();
                }
            }
        }

        private void DrawTreeHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Apply", EditorStyles.toolbarButton, GUILayout.Width(52f));
                GUILayout.Label("Name", EditorStyles.toolbarButton, GUILayout.Width(230f));
                GUILayout.Label("Status", EditorStyles.toolbarButton, GUILayout.Width(74f));
                GUILayout.Label("Summary", EditorStyles.toolbarButton, GUILayout.Width(140f));
                GUILayout.Label("Path", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
            }
        }

        private void DrawSelectedNodeDetails()
        {
            var node = _treeView?.SelectedNode ?? _diffRoot;
            EditorGUILayout.LabelField("Selection Details", EditorStyles.boldLabel);

            if (node == null)
            {
                EditorGUILayout.HelpBox("请先分析 Diff。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(node.Path == string.Empty ? "<Root>" : node.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Path", string.IsNullOrEmpty(node.Path) ? "<Root>" : node.Path, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Status", $"{node.StatusLabel} · {node.DetailSummary}", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);

            if (!string.IsNullOrEmpty(node.Path))
            {
                bool objectSelected = _selectionByPath.TryGetValue(node.Path, out var selected) && selected;
                bool nextObjectSelected = EditorGUILayout.ToggleLeft("Use B object shell for this node only", objectSelected);
                if (nextObjectSelected != objectSelected)
                {
                    _selectionByPath[node.Path] = nextObjectSelected;
                    Repaint();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Subtree", GUILayout.Width(110f)))
                    {
                        SetNodeSelectionRecursive(node, true);
                        Repaint();
                    }

                    if (GUILayout.Button("Clear Subtree", GUILayout.Width(110f)))
                    {
                        SetNodeSelectionRecursive(node, false);
                        Repaint();
                    }
                }

                EditorGUILayout.Space(4f);
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));

            if (node.ObjectChanges.Count > 0)
            {
                EditorGUILayout.LabelField("Object Changes", EditorStyles.boldLabel);
                foreach (var change in node.ObjectChanges)
                    EditorGUILayout.LabelField("• " + change, CreateTintedStyle(EditorStyles.wordWrappedMiniLabel, GetNodeTextColor(node.Kind)));
                EditorGUILayout.Space(6f);
            }

            if (node.ComponentDiffs.Count > 0)
            {
                EditorGUILayout.LabelField("Component Changes", EditorStyles.boldLabel);
                foreach (var component in node.ComponentDiffs)
                    DrawComponentDetails(node, component);
            }

            if (node.ObjectChanges.Count == 0 && node.ComponentDiffs.Count == 0)
                EditorGUILayout.HelpBox("当前节点本体没有直接差异；它可能只是包含有变化的子节点。", MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawComponentDetails(DiffNode node, ComponentDiff component)
        {
            var selectionId = PrefabDiffUtility.MakeComponentSelectionId(node.Path, component.Key);
            var foldoutKey = "component:" + selectionId;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool expanded = GetFoldout(foldoutKey, component.PropertyChanges.Count <= 8);
                    bool nextExpanded = EditorGUILayout.Foldout(expanded, GUIContent.none, false);
                    if (nextExpanded != expanded)
                        _foldoutByPath[foldoutKey] = nextExpanded;

                    bool canSelect = component.IsSelectable;
                    using (new EditorGUI.DisabledScope(!canSelect))
                    {
                        bool current = _componentSelectionById.TryGetValue(selectionId, out var selected) && selected;
                        bool next = EditorGUILayout.Toggle(current, GUILayout.Width(18f));
                        if (next != current)
                        {
                            _componentSelectionById[selectionId] = next;
                            Repaint();
                        }
                    }

                    GUILayout.Label(component.KindLabel, CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(component.Kind)), GUILayout.Width(18f));
                    EditorGUILayout.LabelField(component.TypeName, CreateTintedStyle(EditorStyles.boldLabel, GetNodeTextColor(component.Kind)), GUILayout.Width(190f));
                    EditorGUILayout.LabelField(component.Key, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                }

                if (!GetFoldout(foldoutKey, component.PropertyChanges.Count <= 8))
                    return;

                if (component.PropertyChanges.Count == 0)
                {
                    EditorGUILayout.LabelField("无属性级变化（新增/删除组件）。", EditorStyles.miniLabel);
                    return;
                }

                foreach (var propertyChange in component.PropertyChanges.Take(40))
                {
                    EditorGUILayout.LabelField(propertyChange.Path, EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField("A: " + propertyChange.Before, CreateTintedStyle(EditorStyles.wordWrappedMiniLabel, new Color(0.88f, 0.45f, 0.45f)));
                    EditorGUILayout.LabelField("B: " + propertyChange.After, CreateTintedStyle(EditorStyles.wordWrappedMiniLabel, new Color(0.4f, 0.78f, 0.45f)));
                    EditorGUILayout.Space(3f);
                }

                if (component.PropertyChanges.Count > 40)
                    EditorGUILayout.HelpBox($"其余 {component.PropertyChanges.Count - 40} 项属性变化已折叠省略。", MessageType.None);
            }
        }

        private void EnsureTreeView()
        {
            _treeViewState ??= new TreeViewState();
            _treeView ??= new PrefabDiffTreeView(
                _treeViewState,
                () => _diffRoot,
                GetSelectionAggregate,
                OnTreeNodeToggle,
                Repaint);
            _treeView.SyncModel(resetExpansion: false);
        }

        private void OnTreeNodeToggle(DiffNode node, bool selected)
        {
            SetNodeSelectionRecursive(node, selected);
            Repaint();
        }

        private void DrawNode(DiffNode node)
        {
            if (node == null || (!node.HasAnyChanges && node.Path != string.Empty))
                return;

            using (new EditorGUILayout.VerticalScope())
            {
                var headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(headerRect, GetNodeBackgroundColor(node.Kind, node.Path == string.Empty ? 0.14f : 0.08f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(node.Depth * TreeIndentWidth);

                    if (node.Children.Count > 0)
                    {
                        bool expanded = GetFoldout(node.Path, node.Depth <= 1);
                        bool nextExpanded = EditorGUILayout.Foldout(expanded, GUIContent.none, false);
                        if (nextExpanded != expanded)
                            _foldoutByPath[node.Path] = nextExpanded;
                    }
                    else
                    {
                        GUILayout.Space(16f);
                    }

                    bool isChanged = node.Path == string.Empty || node.HasAnyChanges;
                    var aggregate = GetSelectionAggregate(node.Path);
                    using (new EditorGUI.DisabledScope(!isChanged))
                    {
                        bool displayValue = aggregate.State == TreeSelectionState.All;
                        EditorGUI.showMixedValue = aggregate.State == TreeSelectionState.Partial;
                        bool next = EditorGUILayout.Toggle(displayValue, GUILayout.Width(18));
                        EditorGUI.showMixedValue = false;
                        if (next != displayValue || aggregate.State == TreeSelectionState.Partial)
                        {
                            SetNodeSelectionRecursive(node, next);
                            Repaint();
                        }
                    }

                    GUILayout.Label(GetNodeKindIcon(node.Kind), GUILayout.Width(18));
                    var label = node.Path == string.Empty ? "<Root>" : node.DisplayName;
                    var style = node.HasDirectChanges ? EditorStyles.boldLabel : EditorStyles.label;
                    EditorGUILayout.LabelField(label, style, GUILayout.Width(Mathf.Max(140f, position.width * 0.28f)));
                    GUILayout.Label(node.StatusLabel, CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(node.Kind), TextAnchor.MiddleCenter), GUILayout.Width(86));
                    EditorGUILayout.LabelField(node.DetailSummary, CreateTintedStyle(EditorStyles.miniLabel, node.HasDirectChanges ? GetNodeTextColor(node.Kind) : EditorStyles.miniLabel.normal.textColor), GUILayout.ExpandWidth(true));
                }

                if (node.Path != string.Empty)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(node.Depth * TreeIndentWidth + 38f);
                        EditorGUILayout.LabelField(node.Path, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
                }

                bool showDetails = GetFoldout(node.Path, node.Depth <= 1);
                if (!showDetails)
                    return;

                if (node.ObjectChanges.Count > 0)
                {
                    foreach (var change in node.ObjectChanges)
                        DrawDiffLine(node.Depth + 1, "Object", change, GetNodeTextColor(node.Kind));
                }

                foreach (var component in node.ComponentDiffs)
                {
                    DrawComponentLine(node, component);
                    foreach (var propertyChange in component.PropertyChanges.Take(15))
                        DrawPropertyChangeLine(node.Depth + 2, propertyChange);
                    if (component.PropertyChanges.Count > 15)
                        DrawDiffLine(node.Depth + 2, "…", $"其余 {component.PropertyChanges.Count - 15} 项属性变化已省略", EditorStyles.miniLabel.normal.textColor);
                }

                foreach (var child in node.Children)
                    DrawNode(child);

                GUILayout.Space(2f);
            }
        }

        private void DrawDiffLine(int depth, string prefix, string text, Color textColor)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * TreeIndentWidth + 36f);
                GUILayout.Label(prefix, CreateTintedStyle(EditorStyles.miniBoldLabel, textColor), GUILayout.Width(70));
                EditorGUILayout.LabelField(text, CreateTintedStyle(EditorStyles.miniLabel, textColor), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawPropertyChangeLine(int depth, PropertyChange propertyChange)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * TreeIndentWidth + 36f);
                GUILayout.Label("~", CreateTintedStyle(EditorStyles.miniBoldLabel, new Color(0.95f, 0.7f, 0.2f)), GUILayout.Width(18));
                GUILayout.Label(propertyChange.Path, EditorStyles.miniBoldLabel, GUILayout.Width(240));
                GUILayout.Label($"A: {propertyChange.Before}", CreateTintedStyle(EditorStyles.miniLabel, new Color(0.88f, 0.45f, 0.45f)), GUILayout.Width(Mathf.Max(180f, position.width * 0.22f)));
                GUILayout.Label("→", GUILayout.Width(16));
                GUILayout.Label($"B: {propertyChange.After}", CreateTintedStyle(EditorStyles.miniLabel, new Color(0.4f, 0.78f, 0.45f)), GUILayout.ExpandWidth(true));
            }
        }

        private void DrawComponentLine(DiffNode node, ComponentDiff component)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space((node.Depth + 1) * TreeIndentWidth + 18f);

                var selectionId = PrefabDiffUtility.MakeComponentSelectionId(node.Path, component.Key);
                bool canSelectIndividually = component.IsSelectable;
                using (new EditorGUI.DisabledScope(!canSelectIndividually))
                {
                    bool current = _componentSelectionById.TryGetValue(selectionId, out var selected) && selected;
                    bool next = EditorGUILayout.Toggle(current, GUILayout.Width(18));
                    if (next != current)
                    {
                        _componentSelectionById[selectionId] = next;
                        Repaint();
                    }
                }

                var detail = canSelectIndividually
                    ? component.Key
                    : component.Key + " (需要对象级选择)";
                GUILayout.Label(component.KindLabel, CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(component.Kind)), GUILayout.Width(18));
                GUILayout.Label(component.TypeName, CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(component.Kind)), GUILayout.Width(220));
                EditorGUILayout.LabelField(detail, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }
        }

        private void Analyze()
        {
            if (_sourceA == null || _sourceB == null)
            {
                EditorUtility.DisplayDialog("Missing Object", "请先同时指定 A 和 B。", "OK");
                return;
            }

            try
            {
                _diffRoot = PrefabDiffAnalyzer.Analyze(_sourceA, _sourceB);
                _selectionByPath.Clear();
                _componentSelectionById.Clear();
                _foldoutByPath.Clear();
                SelectAllChanges();
                EnsureTreeView();
                _treeView.SyncModel(resetExpansion: true);
                Debug.Log($"[PrefabDiffPatcher] Analyze complete. Changed nodes: {EnumerateChangedNodes(_diffRoot).Count()}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PrefabDiffPatcher] Analyze failed: {ex}");
                EditorUtility.DisplayDialog("Analyze Failed", ex.Message, "OK");
            }
        }

        private void ApplyPatch()
        {
            var selectedPaths = GetSelectedPaths().ToList();
            var selectedComponents = GetSelectedComponentSelections().ToList();
            if (selectedPaths.Count == 0 && selectedComponents.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing Selected", "请至少勾选一个对象路径或组件差异。", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(_outputPath) || !_outputPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Invalid Output", "输出路径必须位于 Assets/ 下，并以 .prefab 结尾。", "OK");
                return;
            }

            if (!_outputPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                _outputPath += ".prefab";

            try
            {
                var asset = PrefabDiffApplier.Apply(_sourceA, _sourceB, selectedPaths, selectedComponents, _outputPath);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                AssetDatabase.Refresh();
                Debug.Log($"[PrefabDiffPatcher] Generated prefab: {_outputPath}");
                EditorUtility.DisplayDialog("Done", $"已生成 Prefab:\n{_outputPath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PrefabDiffPatcher] Apply failed: {ex}");
                EditorUtility.DisplayDialog("Patch Failed", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void EnsureDefaultOutputPath()
        {
            if (_sourceA == null && _sourceB == null)
                return;

            var folder = GetExistingFolderOrFallback(_outputPath);
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
                folder = DefaultOutputFolder;
            _outputPath = folder.TrimEnd('/') + "/" + BuildDefaultPrefabName();
        }

        private string BuildDefaultPrefabName()
        {
            var aName = _sourceA != null ? _sourceA.name : "A";
            var bName = _sourceB != null ? _sourceB.name : "B";
            return $"{SanitizeFileName(aName)}__PatchedWith__{SanitizeFileName(bName)}.prefab";
        }

        private static string GetExistingFolderOrFallback(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return DefaultOutputFolder;
            var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(folder))
                return DefaultOutputFolder;
            return folder;
        }

        private bool GetFoldout(string path, bool defaultValue)
        {
            if (_foldoutByPath.TryGetValue(path, out var value))
                return value;
            return defaultValue;
        }

        private void ExpandChangedAncestors(DiffNode node)
        {
            if (node == null)
                return;
            if (node.HasAnyChanges)
                _foldoutByPath[node.Path] = node.Depth <= 1;
            foreach (var child in node.Children)
                ExpandChangedAncestors(child);
        }

        private TreeSelectionAggregate BuildSelectionStateCache(DiffNode node)
        {
            if (node == null)
                return default;

            int totalUnits = 0;
            int selectedUnits = 0;

            if (node.Path != string.Empty && node.HasAnyChanges)
            {
                totalUnits++;
                if (_selectionByPath.TryGetValue(node.Path, out var selected) && selected)
                    selectedUnits++;
            }

            if (node.HasAnyChanges)
            {
                foreach (var component in node.ComponentDiffs)
                {
                    if (!component.IsSelectable)
                        continue;
                    totalUnits++;
                    var selectionId = PrefabDiffUtility.MakeComponentSelectionId(node.Path, component.Key);
                    if (_componentSelectionById.TryGetValue(selectionId, out var selected) && selected)
                        selectedUnits++;
                }
            }

            foreach (var child in node.Children)
            {
                if (!child.HasAnyChanges)
                    continue;
                var childAggregate = BuildSelectionStateCache(child);
                totalUnits += childAggregate.TotalUnits;
                selectedUnits += childAggregate.SelectedUnits;
            }

            var state = totalUnits <= 0
                ? TreeSelectionState.None
                : selectedUnits == 0
                    ? TreeSelectionState.None
                    : selectedUnits == totalUnits
                        ? TreeSelectionState.All
                        : TreeSelectionState.Partial;

            var aggregate = new TreeSelectionAggregate(selectedUnits, totalUnits, state);
            _selectionAggregateByPath[node.Path] = aggregate;
            return aggregate;
        }

        private TreeSelectionAggregate GetSelectionAggregate(string path)
        {
            return _selectionAggregateByPath.TryGetValue(path ?? string.Empty, out var aggregate)
                ? aggregate
                : default;
        }

        private void SetFoldoutRecursive(DiffNode node, bool expanded)
        {
            if (node == null)
                return;
            _foldoutByPath[node.Path] = expanded;
            foreach (var child in node.Children)
                SetFoldoutRecursive(child, expanded);
        }

        private void SelectAllChanges()
        {
            _selectionByPath.Clear();
            _componentSelectionById.Clear();
            SetNodeSelectionRecursive(_diffRoot, true);
        }

        private void SelectAllComponents()
        {
            foreach (var node in EnumerateChangedNodes(_diffRoot))
            {
                foreach (var component in node.ComponentDiffs)
                {
                    if (!component.IsSelectable)
                        continue;
                    _componentSelectionById[PrefabDiffUtility.MakeComponentSelectionId(node.Path, component.Key)] = true;
                }
            }
        }

        private void SetObjectSelectionsRecursive(DiffNode node, bool selected)
        {
            if (node == null)
                return;

            if (!string.IsNullOrEmpty(node.Path) && node.HasAnyChanges)
                _selectionByPath[node.Path] = selected;

            foreach (var child in node.Children)
            {
                if (!child.HasAnyChanges)
                    continue;
                SetObjectSelectionsRecursive(child, selected);
            }
        }

        private void SetNodeSelectionRecursive(DiffNode node, bool selected)
        {
            if (node == null)
                return;

            if (!string.IsNullOrEmpty(node.Path))
            {
                _selectionByPath[node.Path] = selected;
                foreach (var component in node.ComponentDiffs)
                {
                    if (!component.IsSelectable)
                        continue;
                    _componentSelectionById[PrefabDiffUtility.MakeComponentSelectionId(node.Path, component.Key)] = selected;
                }
            }

            foreach (var child in node.Children)
            {
                if (!child.HasAnyChanges)
                    continue;
                SetNodeSelectionRecursive(child, selected);
            }
        }

        private IEnumerable<DiffNode> EnumerateChangedNodes(DiffNode node)
        {
            if (node == null)
                yield break;
            if (node.Path != string.Empty && node.HasAnyChanges)
                yield return node;
            foreach (var child in node.Children)
            {
                foreach (var nested in EnumerateChangedNodes(child))
                    yield return nested;
            }
        }

        private IEnumerable<string> GetSelectedPaths()
        {
            return _selectionByPath
                .Where(pair => pair.Value && !string.IsNullOrEmpty(pair.Key))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path.Count(ch => ch == '/'));
        }

        private IEnumerable<ComponentSelection> GetSelectedComponentSelections()
        {
            foreach (var pair in _componentSelectionById)
            {
                if (!pair.Value)
                    continue;

                if (!PrefabDiffUtility.TryParseComponentSelectionId(pair.Key, out var path, out var componentKey))
                    continue;

                yield return new ComponentSelection(path, componentKey);
            }
        }

        private static GUIStyle CreateTintedStyle(GUIStyle baseStyle, Color textColor, TextAnchor? alignment = null)
        {
            var style = new GUIStyle(baseStyle);
            style.normal.textColor = textColor;
            if (alignment.HasValue)
                style.alignment = alignment.Value;
            return style;
        }

        private static Color GetNodeTextColor(DiffKind kind)
        {
            return kind switch
            {
                DiffKind.Added => new Color(0.25f, 0.72f, 0.35f),
                DiffKind.Removed => new Color(0.88f, 0.38f, 0.38f),
                DiffKind.Modified => new Color(0.95f, 0.68f, 0.18f),
                _ => EditorStyles.label.normal.textColor
            };
        }

        private static string GetNodeKindIcon(DiffKind kind)
        {
            return kind switch
            {
                DiffKind.Added => "+",
                DiffKind.Removed => "-",
                DiffKind.Modified => "~",
                _ => "="
            };
        }

        private static Color GetNodeBackgroundColor(DiffKind kind, float alpha)
        {
            return kind switch
            {
                DiffKind.Added => new Color(0.14f, 0.35f, 0.18f, alpha),
                DiffKind.Removed => new Color(0.35f, 0.14f, 0.14f, alpha),
                DiffKind.Modified => new Color(0.35f, 0.27f, 0.08f, alpha),
                _ => new Color(0.18f, 0.18f, 0.18f, alpha)
            };
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Prefab";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }
    }

    internal sealed class PrefabDiffTreeView : TreeView
    {
        private const float ApplyColumnWidth = 52f;
        private const float NameColumnWidth = 230f;
        private const float StatusColumnWidth = 74f;
        private const float SummaryColumnWidth = 140f;

        private readonly Func<DiffNode> _rootProvider;
        private readonly Func<string, TreeSelectionAggregate> _aggregateProvider;
        private readonly Action<DiffNode, bool> _toggleNodeSelection;
        private readonly Action _requestRepaint;
        private string _selectedPath = string.Empty;

        public DiffNode SelectedNode { get; private set; }

        public PrefabDiffTreeView(
            TreeViewState state,
            Func<DiffNode> rootProvider,
            Func<string, TreeSelectionAggregate> aggregateProvider,
            Action<DiffNode, bool> toggleNodeSelection,
            Action requestRepaint) : base(state)
        {
            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _aggregateProvider = aggregateProvider ?? throw new ArgumentNullException(nameof(aggregateProvider));
            _toggleNodeSelection = toggleNodeSelection ?? throw new ArgumentNullException(nameof(toggleNodeSelection));
            _requestRepaint = requestRepaint ?? throw new ArgumentNullException(nameof(requestRepaint));

            rowHeight = 22f;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            Reload();
        }

        public void SyncModel(bool resetExpansion)
        {
            Reload();
            RestoreSelection();
            if (resetExpansion)
                ExpandFirstLevels();
        }

        protected override TreeViewItem BuildRoot()
        {
            var hiddenRoot = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var rootNode = _rootProvider();
            if (rootNode == null)
            {
                hiddenRoot.children = new List<TreeViewItem>();
                return hiddenRoot;
            }

            int nextId = 1;
            var rootItem = BuildItem(rootNode, ref nextId);
            hiddenRoot.AddChild(rootItem);
            SetupDepthsFromParentsAndChildren(hiddenRoot);
            SelectedNode = rootNode;
            return hiddenRoot;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item is not DiffTreeViewItem item)
            {
                base.RowGUI(args);
                return;
            }

            var node = item.Node;
            var rowRect = args.rowRect;
            if (!args.selected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, GetNodeBackgroundColor(node.Kind, 0.06f));

            var applyRect = new Rect(rowRect.x, rowRect.y + 1f, ApplyColumnWidth, rowRect.height - 2f);
            var nameRect = new Rect(applyRect.xMax, rowRect.y, NameColumnWidth, rowRect.height);
            var statusRect = new Rect(nameRect.xMax, rowRect.y, StatusColumnWidth, rowRect.height);
            var summaryRect = new Rect(statusRect.xMax, rowRect.y, SummaryColumnWidth, rowRect.height);
            var pathRect = new Rect(summaryRect.xMax, rowRect.y, rowRect.xMax - summaryRect.xMax, rowRect.height);

            DrawSelectionCell(applyRect, node);
            DrawNameCell(nameRect, item);
            EditorGUI.LabelField(statusRect, node.StatusLabel, CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(node.Kind), TextAnchor.MiddleCenter));
            EditorGUI.LabelField(summaryRect, node.DetailSummary, EditorStyles.miniLabel);
            EditorGUI.LabelField(pathRect, string.IsNullOrEmpty(node.Path) ? "<Root>" : node.Path, EditorStyles.miniLabel);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            if (selectedIds == null || selectedIds.Count == 0)
            {
                SelectedNode = _rootProvider();
                _selectedPath = SelectedNode?.Path ?? string.Empty;
                _requestRepaint();
                return;
            }

            var item = FindItem(selectedIds[0], rootItem) as DiffTreeViewItem;
            SelectedNode = item?.Node ?? _rootProvider();
            _selectedPath = SelectedNode?.Path ?? string.Empty;
            _requestRepaint();
        }

        private void RestoreSelection()
        {
            var rootNode = _rootProvider();
            if (rootNode == null)
                return;

            var target = FindItemByPath(rootItem, string.IsNullOrEmpty(_selectedPath) ? string.Empty : _selectedPath);
            if (target == null)
                target = rootItem?.children?.FirstOrDefault() as DiffTreeViewItem;

            if (target != null)
            {
                SetSelection(new List<int> { target.id }, TreeViewSelectionOptions.RevealAndFrame);
                if (target is DiffTreeViewItem targetItem)
                    SelectedNode = targetItem.Node;
            }
            else
            {
                SelectedNode = rootNode;
            }
        }

        private DiffTreeViewItem BuildItem(DiffNode node, ref int nextId)
        {
            var item = new DiffTreeViewItem(nextId++, node.Depth, node.DisplayName, node);
            var changedChildren = node.Children.Where(child => child.HasAnyChanges).ToList();
            if (changedChildren.Count > 0)
            {
                item.children = new List<TreeViewItem>(changedChildren.Count);
                foreach (var child in changedChildren)
                    item.AddChild(BuildItem(child, ref nextId));
            }
            return item;
        }

        private void ExpandFirstLevels()
        {
            CollapseAll();
            if (rootItem?.children == null)
                return;

            foreach (var child in rootItem.children)
            {
                SetExpanded(child.id, true);
                if (child.children == null)
                    continue;

                foreach (var grandChild in child.children)
                    SetExpanded(grandChild.id, true);
            }
        }

        private void DrawSelectionCell(Rect rect, DiffNode node)
        {
            var aggregate = _aggregateProvider(node.Path);
            bool displayValue = aggregate.State == TreeSelectionState.All;
            EditorGUI.showMixedValue = aggregate.State == TreeSelectionState.Partial;
            bool nextValue = EditorGUI.Toggle(new Rect(rect.x + 16f, rect.y + 2f, 18f, rect.height - 4f), displayValue);
            EditorGUI.showMixedValue = false;
            if (nextValue != displayValue || aggregate.State == TreeSelectionState.Partial)
            {
                _toggleNodeSelection(node, nextValue);
                _requestRepaint();
            }
        }

        private void DrawNameCell(Rect rect, DiffTreeViewItem item)
        {
            var contentRect = rect;
            contentRect.xMin += GetContentIndent(item);

            if (item.hasChildren)
            {
                var foldoutRect = new Rect(contentRect.x, contentRect.y + 2f, 14f, contentRect.height - 4f);
                bool expanded = IsExpanded(item.id);
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, false);
                if (nextExpanded != expanded)
                    SetExpanded(item.id, nextExpanded);
                contentRect.xMin = foldoutRect.xMax + 2f;
            }
            else
            {
                contentRect.xMin += 16f;
            }

            var iconRect = new Rect(contentRect.x, contentRect.y, 18f, contentRect.height);
            var labelRect = new Rect(iconRect.xMax + 2f, contentRect.y, Mathf.Max(0f, rect.xMax - (iconRect.xMax + 2f)), contentRect.height);
            EditorGUI.LabelField(iconRect, GetNodeKindIcon(item.Node.Kind), CreateTintedStyle(EditorStyles.miniBoldLabel, GetNodeTextColor(item.Node.Kind), TextAnchor.MiddleCenter));
            EditorGUI.LabelField(labelRect, item.Node.Path == string.Empty ? "<Root>" : item.Node.DisplayName, item.Node.HasDirectChanges ? EditorStyles.boldLabel : EditorStyles.label);
        }

        private DiffTreeViewItem FindItemByPath(TreeViewItem current, string path)
        {
            if (current is DiffTreeViewItem diffItem && string.Equals(diffItem.Node.Path, path ?? string.Empty, StringComparison.Ordinal))
                return diffItem;

            if (current?.children == null)
                return null;

            foreach (var child in current.children)
            {
                var found = FindItemByPath(child, path);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static GUIStyle CreateTintedStyle(GUIStyle baseStyle, Color textColor, TextAnchor? alignment = null)
        {
            var style = new GUIStyle(baseStyle);
            style.normal.textColor = textColor;
            if (alignment.HasValue)
                style.alignment = alignment.Value;
            return style;
        }

        private static Color GetNodeTextColor(DiffKind kind)
        {
            return kind switch
            {
                DiffKind.Added => new Color(0.25f, 0.72f, 0.35f),
                DiffKind.Removed => new Color(0.88f, 0.38f, 0.38f),
                DiffKind.Modified => new Color(0.95f, 0.68f, 0.18f),
                _ => EditorStyles.label.normal.textColor
            };
        }

        private static string GetNodeKindIcon(DiffKind kind)
        {
            return kind switch
            {
                DiffKind.Added => "+",
                DiffKind.Removed => "-",
                DiffKind.Modified => "~",
                _ => "="
            };
        }

        private static Color GetNodeBackgroundColor(DiffKind kind, float alpha)
        {
            return kind switch
            {
                DiffKind.Added => new Color(0.14f, 0.35f, 0.18f, alpha),
                DiffKind.Removed => new Color(0.35f, 0.14f, 0.14f, alpha),
                DiffKind.Modified => new Color(0.35f, 0.27f, 0.08f, alpha),
                _ => new Color(0.18f, 0.18f, 0.18f, alpha)
            };
        }

        private sealed class DiffTreeViewItem : TreeViewItem
        {
            public DiffNode Node { get; }

            public DiffTreeViewItem(int id, int depth, string displayName, DiffNode node) : base(id, depth, displayName)
            {
                Node = node;
            }
        }
    }

    internal static class PrefabDiffAnalyzer
    {
        public static DiffNode Analyze(GameObject rootA, GameObject rootB)
        {
            if (rootA == null) throw new ArgumentNullException(nameof(rootA));
            if (rootB == null) throw new ArgumentNullException(nameof(rootB));
            return BuildNode(rootA, rootB, string.Empty, 0);
        }

        private static DiffNode BuildNode(GameObject objectA, GameObject objectB, string path, int depth)
        {
            var node = new DiffNode
            {
                Path = path,
                Depth = depth,
                ExistsInA = objectA != null,
                ExistsInB = objectB != null,
                DisplayName = string.IsNullOrEmpty(path) ? "<Root>" : PrefabDiffUtility.GetLastSegment(path)
            };

            if (objectA == null && objectB == null)
                return node;

            if (objectA == null)
            {
                node.Kind = DiffKind.Added;
                node.ObjectChanges.Add("仅存在于 B（合并时会新增到结果中）");
                node.ComponentDiffs.AddRange(BuildAddedOrRemovedComponentDiffs(objectB, DiffKind.Added));
                PopulateChildrenFromExisting(node, null, objectB, path, depth);
                node.RefreshDerivedFlags();
                return node;
            }

            if (objectB == null)
            {
                node.Kind = DiffKind.Removed;
                node.ObjectChanges.Add("仅存在于 A（合并模式下会被保留）");
                node.ComponentDiffs.AddRange(BuildAddedOrRemovedComponentDiffs(objectA, DiffKind.Removed));
                PopulateChildrenFromExisting(node, objectA, null, path, depth);
                node.RefreshDerivedFlags();
                return node;
            }

            CompareGameObject(objectA, objectB, node);
            node.ComponentDiffs.AddRange(CompareComponents(objectA, objectB));
            CompareChildren(objectA, objectB, node, path, depth);
            node.Kind = node.HasDirectChanges || node.Children.Any(child => child.HasAnyChanges)
                ? DiffKind.Modified
                : DiffKind.Unchanged;
            node.RefreshDerivedFlags();
            return node;
        }

        private static void CompareGameObject(GameObject objectA, GameObject objectB, DiffNode node)
        {
            if (objectA.name != objectB.name)
                node.ObjectChanges.Add($"Name: {objectA.name} -> {objectB.name}");
            if (objectA.activeSelf != objectB.activeSelf)
                node.ObjectChanges.Add($"Active: {objectA.activeSelf} -> {objectB.activeSelf}");
            if (objectA.layer != objectB.layer)
                node.ObjectChanges.Add($"Layer: {objectA.layer} -> {objectB.layer}");
            if (!string.Equals(objectA.tag, objectB.tag, StringComparison.Ordinal))
                node.ObjectChanges.Add($"Tag: {objectA.tag} -> {objectB.tag}");
            if (GameObjectUtility.GetStaticEditorFlags(objectA) != GameObjectUtility.GetStaticEditorFlags(objectB))
            {
                node.ObjectChanges.Add($"Static flags changed");
            }
        }

        private static void CompareChildren(GameObject objectA, GameObject objectB, DiffNode node, string currentPath, int depth)
        {
            var childrenA = PrefabDiffUtility.GetChildMap(objectA.transform);
            var childrenB = PrefabDiffUtility.GetChildMap(objectB.transform);
            var keys = childrenA.Keys.Union(childrenB.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                childrenA.TryGetValue(key, out var childA);
                childrenB.TryGetValue(key, out var childB);
                var childPath = PrefabDiffUtility.CombinePath(currentPath, key);
                node.Children.Add(BuildNode(childA != null ? childA.gameObject : null, childB != null ? childB.gameObject : null, childPath, depth + 1));
            }
        }

        private static void PopulateChildrenFromExisting(DiffNode node, GameObject objectA, GameObject objectB, string currentPath, int depth)
        {
            var source = objectA != null ? objectA.transform : objectB.transform;
            var children = PrefabDiffUtility.GetChildMap(source);
            foreach (var child in children.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var childPath = PrefabDiffUtility.CombinePath(currentPath, child.Key);
                node.Children.Add(BuildNode(
                    objectA != null ? child.Value.gameObject : null,
                    objectB != null ? child.Value.gameObject : null,
                    childPath,
                    depth + 1));
            }
        }

        private static List<ComponentDiff> BuildAddedOrRemovedComponentDiffs(GameObject gameObject, DiffKind kind)
        {
            var result = new List<ComponentDiff>();
            if (gameObject == null)
                return result;
            foreach (var entry in PrefabDiffUtility.GetComponentMap(gameObject))
            {
                result.Add(new ComponentDiff
                {
                    Key = entry.Key,
                    TypeName = entry.Value.GetType().Name,
                    Kind = kind
                });
            }
            return result;
        }

        private static List<ComponentDiff> CompareComponents(GameObject objectA, GameObject objectB)
        {
            var mapA = PrefabDiffUtility.GetComponentMap(objectA);
            var mapB = PrefabDiffUtility.GetComponentMap(objectB);
            var allKeys = mapA.Keys.Union(mapB.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
            var result = new List<ComponentDiff>();
            foreach (var key in allKeys)
            {
                mapA.TryGetValue(key, out var componentA);
                mapB.TryGetValue(key, out var componentB);
                if (componentA == null && componentB == null)
                    continue;

                if (componentA == null)
                {
                    result.Add(new ComponentDiff
                    {
                        Key = key,
                        TypeName = componentB.GetType().Name,
                        Kind = DiffKind.Added
                    });
                    continue;
                }

                if (componentB == null)
                {
                    result.Add(new ComponentDiff
                    {
                        Key = key,
                        TypeName = componentA.GetType().Name,
                        Kind = DiffKind.Removed
                    });
                    continue;
                }

                var propertyChanges = CompareSerializedProperties(componentA, componentB);
                if (propertyChanges.Count == 0)
                    continue;

                result.Add(new ComponentDiff
                {
                    Key = key,
                    TypeName = componentA.GetType().Name,
                    Kind = DiffKind.Modified,
                    PropertyChanges = propertyChanges
                });
            }
            return result;
        }

        private static List<PropertyChange> CompareSerializedProperties(Component componentA, Component componentB)
        {
            var mapA = PrefabDiffUtility.ReadSerializedProperties(componentA);
            var mapB = PrefabDiffUtility.ReadSerializedProperties(componentB);
            var allPaths = mapA.Keys.Union(mapB.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
            var changes = new List<PropertyChange>();
            foreach (var propertyPath in allPaths)
            {
                mapA.TryGetValue(propertyPath, out var valueA);
                mapB.TryGetValue(propertyPath, out var valueB);
                if (valueA.Equals(valueB))
                    continue;
                changes.Add(new PropertyChange
                {
                    Path = propertyPath,
                    Before = valueA.ToDisplayString(),
                    After = valueB.ToDisplayString()
                });
            }
            return changes;
        }
    }

    internal static class PrefabDiffApplier
    {
        public static GameObject Apply(GameObject sourceA, GameObject sourceB, IReadOnlyList<string> selectedPaths, IReadOnlyList<ComponentSelection> selectedComponents, string outputPath)
        {
            if (sourceA == null) throw new ArgumentNullException(nameof(sourceA));
            if (sourceB == null) throw new ArgumentNullException(nameof(sourceB));
            if ((selectedPaths == null || selectedPaths.Count == 0) && (selectedComponents == null || selectedComponents.Count == 0))
                throw new ArgumentException("No selections.");

            EnsureFolderExists(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));

            var selectionContext = new SelectionContext(selectedPaths, selectedComponents);
            var workingRoot = ComposeNode(string.Empty, sourceA, sourceB, selectionContext, isRoot: true);
            try
            {
                if (workingRoot == null)
                    throw new InvalidOperationException("根据当前选择没有可生成的结果对象。");

                RemapReferencesFromSourceRoot(workingRoot, sourceA);
                RemapReferencesFromSourceRoot(workingRoot, sourceB);
                ClearHideFlagsRecursively(workingRoot.transform);

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                if (existing != null)
                    AssetDatabase.DeleteAsset(outputPath);

                var asset = PrefabUtility.SaveAsPrefabAsset(workingRoot, outputPath);
                AssetDatabase.SaveAssets();
                return asset;
            }
            finally
            {
                if (workingRoot != null)
                    Object.DestroyImmediate(workingRoot);
            }
        }

        private static GameObject ComposeNode(string path, GameObject sourceA, GameObject sourceB, SelectionContext selectionContext, bool isRoot = false)
        {
            bool objectSelected = selectionContext.IsObjectSelected(path);
            bool subtreeSelected = selectionContext.HasAnySelectionAtOrBelow(path);

            if (sourceA == null && sourceB == null)
                return null;

            if (sourceA == null && sourceB != null && !subtreeSelected && !isRoot)
                return null;

            var shellSource = ChooseShellSource(sourceA, sourceB, objectSelected);
            if (shellSource == null)
                return null;

            var result = CloneNodeShell(shellSource);
            result.name = shellSource.name;

            var baselineComponentSource = ChooseBaselineComponentSource(sourceA, sourceB);
            SyncNonTransformComponents(result, baselineComponentSource);

            if (sourceA != null && sourceB != null)
            {
                ApplySelectedComponentsAtNode(result, sourceA, sourceB, path, selectionContext);
            }

            foreach (var childEntry in BuildOrderedChildEntries(sourceA?.transform, sourceB?.transform))
            {
                string childPath = PrefabDiffUtility.CombinePath(path, childEntry.Key);
                bool childSubtreeSelected = selectionContext.HasAnySelectionAtOrBelow(childPath);
                GameObject childResult;

                if (childEntry.ChildA != null && !childSubtreeSelected)
                {
                    childResult = CloneWholeSubtree(childEntry.ChildA.gameObject);
                }
                else
                {
                    childResult = ComposeNode(childPath, childEntry.ChildA?.gameObject, childEntry.ChildB?.gameObject, selectionContext);
                }

                if (childResult == null)
                    continue;

                childResult.transform.SetParent(result.transform, false);
                childResult.transform.SetSiblingIndex(result.transform.childCount - 1);
            }

            return result;
        }

        private static GameObject ChooseShellSource(GameObject sourceA, GameObject sourceB, bool objectSelected)
        {
            if (sourceA != null && sourceB != null)
                return objectSelected ? sourceB : sourceA;
            return sourceA ?? sourceB;
        }

        private static GameObject ChooseBaselineComponentSource(GameObject sourceA, GameObject sourceB)
        {
            return sourceA ?? sourceB;
        }

        private static void ApplySelectedComponentsAtNode(GameObject resultNode, GameObject sourceNodeA, GameObject sourceNodeB, string path, SelectionContext selectionContext)
        {
            foreach (var componentKey in selectionContext.GetSelectedComponentKeys(path))
            {
                var componentsA = sourceNodeA != null ? PrefabDiffUtility.GetComponentMap(sourceNodeA) : new Dictionary<string, Component>(StringComparer.Ordinal);
                var componentsB = sourceNodeB != null ? PrefabDiffUtility.GetComponentMap(sourceNodeB) : new Dictionary<string, Component>(StringComparer.Ordinal);
                var resultComponents = PrefabDiffUtility.GetComponentMap(resultNode);

                componentsA.TryGetValue(componentKey, out var componentA);
                componentsB.TryGetValue(componentKey, out var componentB);
                resultComponents.TryGetValue(componentKey, out var resultComponent);

                if (componentA == null && componentB != null)
                {
                    if (resultComponent == null)
                        AddComponentFromSource(resultNode, componentB);
                    else
                        OverwriteComponentFromSource(resultComponent, componentB);
                    continue;
                }

                if (componentA != null && componentB == null)
                {
                    if (resultComponent is Transform)
                        throw new InvalidOperationException("不能移除 Transform 组件。");
                    if (resultComponent != null)
                        Object.DestroyImmediate(resultComponent);
                    continue;
                }

                if (componentA != null && componentB != null)
                {
                    if (resultComponent == null)
                        AddComponentFromSource(resultNode, componentB);
                    else
                        OverwriteComponentFromSource(resultComponent, componentB);
                }
            }
        }

        private static GameObject CloneNodeShell(GameObject source)
        {
            if (source == null)
                return null;

            var clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            for (int i = clone.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(clone.transform.GetChild(i).gameObject);
            }
            return clone;
        }

        private static GameObject CloneWholeSubtree(GameObject source)
        {
            if (source == null)
                return null;
            var clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }

        private static void SyncNonTransformComponents(GameObject resultNode, GameObject sourceNode)
        {
            foreach (var component in resultNode.GetComponents<Component>().Reverse())
            {
                if (component == null || component is Transform)
                    continue;
                Object.DestroyImmediate(component);
            }

            if (sourceNode == null)
                return;

            foreach (var component in sourceNode.GetComponents<Component>())
            {
                if (component == null || component is Transform)
                    continue;
                CopyComponentAsNew(component, resultNode);
            }
        }

        private static IEnumerable<OrderedChildEntry> BuildOrderedChildEntries(Transform transformA, Transform transformB)
        {
            var mapA = transformA != null
                ? PrefabDiffUtility.GetChildMap(transformA)
                : new Dictionary<string, Transform>(StringComparer.Ordinal);
            var mapB = transformB != null
                ? PrefabDiffUtility.GetChildMap(transformB)
                : new Dictionary<string, Transform>(StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in mapA)
            {
                emitted.Add(pair.Key);
                mapB.TryGetValue(pair.Key, out var childB);
                yield return new OrderedChildEntry(pair.Key, pair.Value, childB);
            }

            foreach (var pair in mapB)
            {
                if (emitted.Contains(pair.Key))
                    continue;
                mapA.TryGetValue(pair.Key, out var childA);
                yield return new OrderedChildEntry(pair.Key, childA, pair.Value);
            }
        }

        private static Component CopyComponentAsNew(Component sourceComponent, GameObject target)
        {
            if (sourceComponent == null || target == null)
                return null;

            var before = target.GetComponents<Component>().Where(component => component != null).ToList();
            if (!ComponentUtility.CopyComponent(sourceComponent))
                throw new InvalidOperationException($"无法复制组件: {sourceComponent.GetType().Name}");
            if (!ComponentUtility.PasteComponentAsNew(target))
                throw new InvalidOperationException($"无法把组件粘贴到对象上: {target.name} ({sourceComponent.GetType().Name})");

            var after = target.GetComponents<Component>().Where(component => component != null).ToList();
            return after.Except(before).FirstOrDefault();
        }

        private static Component AddComponentFromSource(GameObject target, Component sourceComponent)
        {
            return CopyComponentAsNew(sourceComponent, target);
        }

        private static void OverwriteComponentFromSource(Component targetComponent, Component sourceComponent)
        {
            if (targetComponent == null || sourceComponent == null)
                return;
            if (targetComponent is Transform)
                return;
            if (targetComponent.GetType() != sourceComponent.GetType())
                throw new InvalidOperationException($"组件类型不匹配，无法覆写: {targetComponent.GetType().Name} vs {sourceComponent.GetType().Name}");

            if (!ComponentUtility.CopyComponent(sourceComponent))
                throw new InvalidOperationException($"无法复制组件内容: {sourceComponent.GetType().Name}");
            if (!ComponentUtility.PasteComponentValues(targetComponent))
                throw new InvalidOperationException($"无法覆写组件内容: {targetComponent.GetType().Name}");
        }

        private static void AddSubtree(GameObject workingRoot, string path, GameObject sourceNodeB)
        {
            var parentPath = PrefabDiffUtility.GetParentPath(path);
            var parent = string.IsNullOrEmpty(parentPath)
                ? workingRoot.transform
                : PrefabDiffUtility.FindByPath(workingRoot.transform, parentPath);
            if (parent == null)
                throw new InvalidOperationException($"结果中找不到新增节点的父路径: {parentPath}");

            var added = Object.Instantiate(sourceNodeB);
            added.hideFlags = HideFlags.HideAndDontSave;
            added.transform.SetParent(parent, false);
            added.transform.SetSiblingIndex(Mathf.Min(sourceNodeB.transform.GetSiblingIndex(), parent.childCount - 1));
            added.name = sourceNodeB.name;
        }

        private static Dictionary<Object, Object> BuildOldToNewRemap(GameObject oldRoot, GameObject newRoot)
        {
            var map = new Dictionary<Object, Object>();
            var oldNodes = PrefabDiffUtility.GetRelativeNodeMap(oldRoot.transform);
            var newNodes = PrefabDiffUtility.GetRelativeNodeMap(newRoot.transform);

            foreach (var oldPair in oldNodes)
            {
                if (!newNodes.TryGetValue(oldPair.Key, out var newTransform))
                    continue;

                map[oldPair.Value.gameObject] = newTransform.gameObject;
                map[oldPair.Value] = newTransform;

                var oldComponents = PrefabDiffUtility.GetComponentMap(oldPair.Value.gameObject);
                var newComponents = PrefabDiffUtility.GetComponentMap(newTransform.gameObject);
                foreach (var componentPair in oldComponents)
                {
                    if (newComponents.TryGetValue(componentPair.Key, out var newComponent) && newComponent != null)
                        map[componentPair.Value] = newComponent;
                }
            }

            return map;
        }

        private static void RemapReferencesFromSourceRoot(GameObject workingRoot, GameObject sourceRoot)
        {
            foreach (var component in workingRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                var so = new SerializedObject(component);
                var iterator = so.GetIterator();
                bool enterChildren = true;
                bool changed = false;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = false;
                    if (!PrefabDiffUtility.IsReferenceProperty(iterator.propertyType))
                        continue;
                    if (PrefabDiffUtility.ShouldSkipProperty(iterator.propertyPath))
                        continue;

                    var current = PrefabDiffUtility.GetReferenceValue(iterator);
                    if (current == null)
                        continue;

                    var remapped = PrefabDiffUtility.TryMapObjectReference(current, sourceRoot.transform, workingRoot.transform);
                    if (remapped != null && remapped != current)
                    {
                        PrefabDiffUtility.SetReferenceValue(iterator, remapped);
                        changed = true;
                    }
                }

                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void RemapReferences(GameObject workingRoot, Dictionary<Object, Object> remap)
        {
            if (remap == null || remap.Count == 0)
                return;

            foreach (var component in workingRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                var so = new SerializedObject(component);
                var iterator = so.GetIterator();
                bool enterChildren = true;
                bool changed = false;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = false;
                    if (!PrefabDiffUtility.IsReferenceProperty(iterator.propertyType))
                        continue;
                    if (PrefabDiffUtility.ShouldSkipProperty(iterator.propertyPath))
                        continue;

                    var current = PrefabDiffUtility.GetReferenceValue(iterator);
                    if (current == null)
                        continue;

                    if (remap.TryGetValue(current, out var replacement) && replacement != current)
                    {
                        PrefabDiffUtility.SetReferenceValue(iterator, replacement);
                        changed = true;
                    }
                }

                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureFolderExists(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder == "Assets")
                return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private readonly struct OrderedChildEntry
        {
            public readonly string Key;
            public readonly Transform ChildA;
            public readonly Transform ChildB;

            public OrderedChildEntry(string key, Transform childA, Transform childB)
            {
                Key = key;
                ChildA = childA;
                ChildB = childB;
            }
        }

        private static void ClearHideFlagsRecursively(Transform root)
        {
            if (root == null)
                return;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.hideFlags = HideFlags.None;
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component != null)
                        component.hideFlags = HideFlags.None;
                }
                transform.gameObject.hideFlags = HideFlags.None;
            }
        }

        private sealed class SelectionContext
        {
            private readonly HashSet<string> _selectedObjectPaths;
            private readonly Dictionary<string, HashSet<string>> _componentKeysByPath;

            public SelectionContext(IReadOnlyList<string> selectedPaths, IReadOnlyList<ComponentSelection> selectedComponents)
            {
                _selectedObjectPaths = new HashSet<string>(selectedPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
                _componentKeysByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (var selection in selectedComponents ?? Array.Empty<ComponentSelection>())
                {
                    if (!_componentKeysByPath.TryGetValue(selection.Path, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        _componentKeysByPath[selection.Path] = set;
                    }
                    set.Add(selection.ComponentKey);
                }
            }

            public bool IsObjectSelected(string path)
            {
                return _selectedObjectPaths.Contains(path ?? string.Empty);
            }

            public IEnumerable<string> GetSelectedComponentKeys(string path)
            {
                return _componentKeysByPath.TryGetValue(path ?? string.Empty, out var set)
                    ? set
                    : Enumerable.Empty<string>();
            }

            public bool HasAnySelectionAtOrBelow(string path)
            {
                path ??= string.Empty;
                if (_selectedObjectPaths.Any(selected => PrefabDiffUtility.IsSameOrDescendant(selected, path)))
                    return true;
                if (_componentKeysByPath.Keys.Any(selected => PrefabDiffUtility.IsSameOrDescendant(selected, path)))
                    return true;
                return false;
            }
        }
    }

    internal static class PrefabDiffUtility
    {
        private const char ComponentSelectionSeparator = '|';
        private static readonly HashSet<string> SkippedPropertyPaths = new(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_Children",
            "m_Father",
            "m_RootOrder",
            "m_LocalEulerAnglesHint",
            "m_Script"
        };

        public static Dictionary<string, Transform> GetChildMap(Transform parent)
        {
            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var duplicateCounter = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!duplicateCounter.TryGetValue(child.name, out var count))
                    count = 0;
                duplicateCounter[child.name] = count + 1;
                var key = count == 0 ? child.name : $"{child.name}[{count}]";
                map[key] = child;
            }
            return map;
        }

        public static string CombinePath(string prefix, string child)
        {
            return string.IsNullOrEmpty(prefix) ? child : prefix + "/" + child;
        }

        public static string MakeComponentSelectionId(string path, string componentKey)
        {
            return path + ComponentSelectionSeparator + componentKey;
        }

        public static bool TryParseComponentSelectionId(string selectionId, out string path, out string componentKey)
        {
            path = null;
            componentKey = null;
            if (string.IsNullOrEmpty(selectionId))
                return false;

            var index = selectionId.IndexOf(ComponentSelectionSeparator);
            if (index < 0)
                return false;

            path = selectionId.Substring(0, index);
            componentKey = selectionId.Substring(index + 1);
            return true;
        }

        public static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            var index = path.LastIndexOf('/');
            return index < 0 ? string.Empty : path.Substring(0, index);
        }

        public static string GetLastSegment(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            var index = path.LastIndexOf('/');
            return index < 0 ? path : path.Substring(index + 1);
        }

        public static bool IsSameOrDescendant(string candidate, string ancestor)
        {
            if (string.Equals(candidate, ancestor, StringComparison.Ordinal))
                return true;
            if (string.IsNullOrEmpty(ancestor))
                return true;
            return candidate.StartsWith(ancestor + "/", StringComparison.Ordinal);
        }

        public static bool IsPrefabAssetRoot(GameObject gameObject)
        {
            return gameObject != null
                   && PrefabUtility.IsPartOfPrefabAsset(gameObject)
                   && gameObject.transform.parent == null;
        }

        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(path))
                return root;

            var current = root;
            foreach (var segment in path.Split('/'))
            {
                current = FindChildBySegment(current, segment);
                if (current == null)
                    return null;
            }
            return current;
        }

        public static Dictionary<string, Transform> GetRelativeNodeMap(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal)
            {
                [string.Empty] = root
            };
            FillNodeMap(root, string.Empty, result);
            return result;
        }

        public static Dictionary<string, Component> GetComponentMap(GameObject gameObject)
        {
            var map = new Dictionary<string, Component>(StringComparer.Ordinal);
            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                var typeKey = component.GetType().FullName ?? component.GetType().Name;
                if (!counters.TryGetValue(typeKey, out var index))
                    index = 0;
                counters[typeKey] = index + 1;
                map[typeKey + "#" + index] = component;
            }
            return map;
        }

        public static Dictionary<string, SerializedValue> ReadSerializedProperties(Component component)
        {
            var result = new Dictionary<string, SerializedValue>(StringComparer.Ordinal);
            if (component == null)
                return result;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = false;
                if (ShouldSkipProperty(iterator.propertyPath))
                    continue;
                result[iterator.propertyPath] = SerializedValue.FromProperty(iterator);
            }
            return result;
        }

        public static bool ShouldSkipProperty(string propertyPath)
        {
            return SkippedPropertyPaths.Contains(propertyPath);
        }

        public static bool IsReferenceProperty(SerializedPropertyType propertyType)
        {
            return propertyType == SerializedPropertyType.ObjectReference
                   || propertyType == SerializedPropertyType.ExposedReference;
        }

        public static Object GetReferenceValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                SerializedPropertyType.ExposedReference => property.exposedReferenceValue,
                _ => null
            };
        }

        public static void SetReferenceValue(SerializedProperty property, Object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value;
                    break;
                case SerializedPropertyType.ExposedReference:
                    property.exposedReferenceValue = value;
                    break;
            }
        }

        public static Object TryMapObjectReference(Object sourceReference, Transform sourceRoot, Transform resultRoot)
        {
            if (sourceReference == null || sourceRoot == null || resultRoot == null)
                return null;

            switch (sourceReference)
            {
                case GameObject gameObject:
                {
                    var path = GetRelativePath(sourceRoot, gameObject.transform);
                    if (path == null)
                        return null;
                    return FindByPath(resultRoot, path)?.gameObject;
                }
                case Component component:
                {
                    var path = GetRelativePath(sourceRoot, component.transform);
                    if (path == null)
                        return null;
                    var resultNode = FindByPath(resultRoot, path);
                    if (resultNode == null)
                        return null;
                    var sourceComponents = GetComponentMap(component.gameObject);
                    var resultComponents = GetComponentMap(resultNode.gameObject);
                    var sourceKey = sourceComponents.FirstOrDefault(pair => pair.Value == component).Key;
                    if (string.IsNullOrEmpty(sourceKey))
                        return null;
                    return resultComponents.TryGetValue(sourceKey, out var targetComponent) ? targetComponent : null;
                }
                default:
                    return null;
            }
        }

        private static void FillNodeMap(Transform parent, string parentPath, Dictionary<string, Transform> map)
        {
            foreach (var pair in GetChildMap(parent))
            {
                var childPath = CombinePath(parentPath, pair.Key);
                map[childPath] = pair.Value;
                FillNodeMap(pair.Value, childPath, map);
            }
        }

        private static Transform FindChildBySegment(Transform parent, string segment)
        {
            string expectedName = segment;
            int expectedDuplicateIndex = 0;
            var openBracket = segment.LastIndexOf('[');
            if (openBracket > 0 && segment.EndsWith("]", StringComparison.Ordinal))
            {
                var suffix = segment.Substring(openBracket + 1, segment.Length - openBracket - 2);
                if (int.TryParse(suffix, out var parsed))
                {
                    expectedName = segment.Substring(0, openBracket);
                    expectedDuplicateIndex = parsed;
                }
            }

            int seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!string.Equals(child.name, expectedName, StringComparison.Ordinal))
                    continue;
                if (seen == expectedDuplicateIndex)
                    return child;
                seen++;
            }
            return null;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return null;
            if (root == target)
                return string.Empty;

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                var parent = current.parent;
                if (parent == null)
                    return null;
                segments.Add(GetSegmentForChild(parent, current));
                current = parent;
            }

            if (current != root)
                return null;
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string GetSegmentForChild(Transform parent, Transform child)
        {
            int duplicateIndex = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (!string.Equals(sibling.name, child.name, StringComparison.Ordinal))
                    continue;
                if (sibling == child)
                    break;
                duplicateIndex++;
            }
            return duplicateIndex == 0 ? child.name : $"{child.name}[{duplicateIndex}]";
        }
    }

    internal enum DiffKind
    {
        Unchanged,
        Added,
        Removed,
        Modified
    }

    internal sealed class DiffNode
    {
        public string Path;
        public string DisplayName;
        public int Depth;
        public DiffKind Kind;
        public bool ExistsInA;
        public bool ExistsInB;
        public bool HasDirectChanges;
        public bool HasAnyChanges;
        public readonly List<string> ObjectChanges = new();
        public readonly List<ComponentDiff> ComponentDiffs = new();
        public readonly List<DiffNode> Children = new();

        public string StatusLabel => Kind switch
        {
            DiffKind.Added => "Added",
            DiffKind.Removed => "Removed",
            DiffKind.Modified => "Changed",
            _ => "Same"
        };

        public string DetailSummary
        {
            get
            {
                int objectCount = ObjectChanges.Count;
                int componentCount = ComponentDiffs.Count;
                int childCount = Children.Count(child => child.HasAnyChanges);
                return $"GO {objectCount} / Comp {componentCount} / Child {childCount}";
            }
        }

        public void RefreshDerivedFlags()
        {
            HasDirectChanges = ObjectChanges.Count > 0 || ComponentDiffs.Count > 0 || Kind == DiffKind.Added || Kind == DiffKind.Removed;
            HasAnyChanges = HasDirectChanges || Children.Any(child => child.HasAnyChanges);
        }
    }

    internal sealed class ComponentDiff
    {
        public string Key;
        public string TypeName;
        public DiffKind Kind;
        public List<PropertyChange> PropertyChanges = new();

        public bool IsSelectable => Kind != DiffKind.Unchanged;

        public string KindLabel => Kind switch
        {
            DiffKind.Added => "+",
            DiffKind.Removed => "-",
            DiffKind.Modified => "~",
            _ => "="
        };
    }

    internal sealed class PropertyChange
    {
        public string Path;
        public string Before;
        public string After;
    }

    internal readonly struct ComponentSelection
    {
        public readonly string Path;
        public readonly string ComponentKey;

        public ComponentSelection(string path, string componentKey)
        {
            Path = path ?? string.Empty;
            ComponentKey = componentKey ?? string.Empty;
        }
    }

    internal enum TreeSelectionState
    {
        None,
        Partial,
        All
    }

    internal readonly struct TreeSelectionAggregate
    {
        public readonly int SelectedUnits;
        public readonly int TotalUnits;
        public readonly TreeSelectionState State;

        public TreeSelectionAggregate(int selectedUnits, int totalUnits, TreeSelectionState state)
        {
            SelectedUnits = selectedUnits;
            TotalUnits = totalUnits;
            State = state;
        }
    }

    internal readonly struct SerializedValue : IEquatable<SerializedValue>
    {
        public readonly SerializedPropertyType PropertyType;
        public readonly string Value;

        public SerializedValue(SerializedPropertyType propertyType, string value)
        {
            PropertyType = propertyType;
            Value = value ?? string.Empty;
        }

        public static SerializedValue FromProperty(SerializedProperty property)
        {
            if (property == null)
                return default;

            string value = property.propertyType switch
            {
                SerializedPropertyType.Integer => property.longValue.ToString(),
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Float => property.doubleValue.ToString("R"),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.Color => property.colorValue.ToString(),
                SerializedPropertyType.ObjectReference => DescribeObjectReference(property.objectReferenceValue),
                SerializedPropertyType.LayerMask => property.intValue.ToString(),
                SerializedPropertyType.Enum => property.enumValueIndex + ":" + property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex),
                SerializedPropertyType.Vector2 => property.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => property.vector3Value.ToString(),
                SerializedPropertyType.Vector4 => property.vector4Value.ToString(),
                SerializedPropertyType.Rect => property.rectValue.ToString(),
                SerializedPropertyType.ArraySize => property.intValue.ToString(),
                SerializedPropertyType.Character => ((char)property.intValue).ToString(),
                SerializedPropertyType.AnimationCurve => property.animationCurveValue != null ? property.animationCurveValue.keys.Length + " keys" : "null",
                SerializedPropertyType.Bounds => property.boundsValue.ToString(),
                SerializedPropertyType.Gradient => property.gradientValue != null ? "Gradient" : "null",
                SerializedPropertyType.Quaternion => property.quaternionValue.eulerAngles.ToString(),
                SerializedPropertyType.ExposedReference => DescribeObjectReference(property.exposedReferenceValue),
                SerializedPropertyType.FixedBufferSize => property.fixedBufferSize.ToString(),
                SerializedPropertyType.Vector2Int => property.vector2IntValue.ToString(),
                SerializedPropertyType.Vector3Int => property.vector3IntValue.ToString(),
                SerializedPropertyType.RectInt => property.rectIntValue.ToString(),
                SerializedPropertyType.BoundsInt => property.boundsIntValue.ToString(),
                SerializedPropertyType.ManagedReference => property.managedReferenceFullTypename + ":" + (property.managedReferenceId.ToString()),
                SerializedPropertyType.Hash128 => property.hash128Value.ToString(),
                _ => property.propertyPath
            };
            return new SerializedValue(property.propertyType, value);
        }

        public bool Equals(SerializedValue other)
        {
            return PropertyType == other.PropertyType && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SerializedValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)PropertyType * 397) ^ (Value != null ? Value.GetHashCode() : 0);
            }
        }

        public string ToDisplayString()
        {
            return string.IsNullOrEmpty(Value) ? "<empty>" : Value;
        }

        private static string DescribeObjectReference(Object value)
        {
            if (value == null)
                return "null";
            return value switch
            {
                GameObject go => $"GameObject:{go.name}",
                Component component => $"{component.GetType().Name}:{component.gameObject.name}",
                _ => $"{value.GetType().Name}:{value.name}"
            };
        }
    }
}
