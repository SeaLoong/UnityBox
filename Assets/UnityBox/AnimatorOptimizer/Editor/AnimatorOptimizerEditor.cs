using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AnimatorOptimizer
{
    [CustomEditor(typeof(AnimatorOptimizerComponent))]
    public sealed class AnimatorOptimizerEditor : UnityEditor.Editor
    {
        private readonly Dictionary<string, SerializedProperty> properties = new Dictionary<string, SerializedProperty>();
        private AnimatorOptimizerReport cachedReport;
        private AnimatorController cachedController;
        private string cachedOptionsKey;
        private string analysisError;
        private bool analysisDirty = true;
        private bool analysisQueued;
        private Vector2 layerScroll;

        private SerializedProperty Property(string name)
        {
            if (!properties.TryGetValue(name, out var property))
                properties[name] = property = serializedObject.FindProperty(name);
            return property;
        }

        private void OnEnable()
        {
            properties.Clear();
            Undo.undoRedoPerformed += Invalidate;
            Undo.postprocessModifications += OnModifications;
            EditorApplication.projectChanged += Invalidate;
            Invalidate();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Invalidate;
            Undo.postprocessModifications -= OnModifications;
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.delayCall -= RunQueuedAnalysis;
            analysisQueued = false;
        }

        private void Invalidate()
        {
            analysisDirty = true;
            Repaint();
        }

        private UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (modifications.Any(change => change.currentValue?.target is AnimatorController ||
                change.currentValue?.target is AnimatorState || change.currentValue?.target is AnimatorStateMachine ||
                change.currentValue?.target is AnimatorTransitionBase || change.currentValue?.target is Motion ||
                change.currentValue?.target is AvatarMask || change.currentValue?.target is StateMachineBehaviour))
                Invalidate();
            return modifications;
        }

        public override void OnInspectorGUI()
        {
            AnimatorOptimizerEditorStyles.EnsureSkin();
            serializedObject.Update();
            var component = (AnimatorOptimizerComponent)target;
            if (component == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                AnimatorOptimizerEditorStyles.ClippedText("Animator Optimizer", AnimatorOptimizerEditorStyles.ClippedTitle);
                AnimatorOptimizerEditorStyles.DrawBadge(component.enabled ? "BUILD TIME" : "DISABLED",
                    component.enabled ? AnimatorOptimizerEditorStyles.AccentColor : AnimatorOptimizerEditorStyles.UnsupportedColor);
            }
            EditorGUILayout.LabelField("构建时优化副本，不修改源 Controller。先选目标和策略，其他设置通常无需调整。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(5f);

            DrawTarget(component);
            DrawHeuristics();
            DrawConversion();
            DrawCompatibility();
            DrawAdvanced();
            DrawActiveRelaxations();
            if (serializedObject.ApplyModifiedProperties())
                analysisDirty = true;

            EnsureAnalysisQueued(component);
            DrawAnalysis(component);
        }

        private void Field(string name, string label, string tooltip = null)
        {
            var property = Property(name);
            var content = AnimatorOptimizerEditorStyles.FieldLabel(label, tooltip ?? property.tooltip,
                property.propertyType == SerializedPropertyType.Boolean ? 24f : 110f);
            EditorGUILayout.PropertyField(property, content);
        }

        private void DrawTarget(AnimatorOptimizerComponent component)
        {
            if (!AnimatorOptimizerEditorStyles.Section("Inspector.Target", "目标与范围"))
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Field(nameof(AnimatorOptimizerComponent.targetMode), "目标类型");
                var mode = (AnimatorOptimizerComponent.TargetMode)Property(nameof(AnimatorOptimizerComponent.targetMode)).enumValueIndex;
                if (mode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer)
                    Field(nameof(AnimatorOptimizerComponent.playableLayer), "Playable Layer");
                else
                    Field(nameof(AnimatorOptimizerComponent.animator), "Animator", "留空优先根 Animator，否则仅接受唯一子 Animator；多个候选时必须指定。");
                if (mode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
                    Field(nameof(AnimatorOptimizerComponent.controller), "Controller");

                Field(nameof(AnimatorOptimizerComponent.optimizeAllConvertibleLayers), "自动选择可转换层");
                if (!Property(nameof(AnimatorOptimizerComponent.optimizeAllConvertibleLayers)).boolValue)
                    DrawLayerSelection(component);
            }
        }

        private void DrawLayerSelection(AnimatorOptimizerComponent component)
        {
            var indices = Property(nameof(AnimatorOptimizerComponent.layerIndices));
            var controller = ResolveController(component);
            if (controller == null)
            {
                EditorGUILayout.PropertyField(indices, new GUIContent("Layer 索引"), true);
                return;
            }
            var layers = controller.layers;
            var selected = new HashSet<int>();
            for (var index = 0; index < indices.arraySize; index++)
                selected.Add(indices.GetArrayElementAtIndex(index).intValue);
            var original = selected.OrderBy(index => index).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                AnimatorOptimizerEditorStyles.ClippedText($"选择 Layer  ·  {selected.Count}/{layers.Length}", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("全选", EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                    selected = new HashSet<int>(Enumerable.Range(0, layers.Length));
                if (GUILayout.Button("清空", EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                    selected.Clear();
            }
            layerScroll = EditorGUILayout.BeginScrollView(layerScroll, GUILayout.Height(Mathf.Min(180f, layers.Length * 21f + 6f)));
            for (var index = 0; index < layers.Length; index++)
            {
                var title = $"[{index}]  {layers[index]?.name ?? "<空层>"}";
                var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.MinWidth(0f));
                var content = new GUIContent(AnimatorOptimizerEditorStyles.Ellipsize(title, EditorStyles.label, rect.width - 22f), title);
                var next = EditorGUI.ToggleLeft(rect, content, selected.Contains(index));
                if (next)
                    selected.Add(index);
                else
                    selected.Remove(index);
            }
            EditorGUILayout.EndScrollView();
            var ordered = selected.OrderBy(index => index).ToList();
            if (!original.SequenceEqual(ordered))
            {
                indices.arraySize = ordered.Count;
                for (var index = 0; index < ordered.Count; index++)
                    indices.GetArrayElementAtIndex(index).intValue = ordered[index];
            }
            if (ordered.Any(index => index >= layers.Length))
                EditorGUILayout.HelpBox("存在当前源 Controller 尚未包含的索引；已保留，可能由构建插件生成。", MessageType.Info);
            EditorGUILayout.LabelField("构建时其它插件可能改变 Layer 索引；自动选择通常更稳妥。", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawConversion()
        {
            if (!AnimatorOptimizerEditorStyles.Section("Inspector.ConversionDetails", "参数与合并（高级）", false))
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Field(nameof(AnimatorOptimizerComponent.upgradeBoolParametersToFloat), "Bool → Float");
                Field(nameof(AnimatorOptimizerComponent.upgradeIntParametersToFloat), "Int → Float");
                EditorGUILayout.LabelField("VRC Expression Bool/Int 可映射到 Float；普通 Unity 外部代码需同步调整 SetBool/SetInteger 等调用，静态分析无法证明这些调用不存在。", EditorStyles.wordWrappedMiniLabel);
                Field(nameof(AnimatorOptimizerComponent.writeDefaultsPolicy), "Write Defaults 策略");
                var policy = (AnimatorOptimizerComponent.WriteDefaultsPolicy)Property(nameof(AnimatorOptimizerComponent.writeDefaultsPolicy)).enumValueIndex;
                if (policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.AllowCoveredStates)
                    EditorGUILayout.LabelField("自动接受所有分支均保证覆盖 Controller 绑定的 WD Off State。", EditorStyles.wordWrappedMiniLabel);
                else if (policy == AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn)
                    EditorGUILayout.HelpBox("可能改变默认值回写和属性保持。仅在不依赖 WD Off 保持效果时使用。", MessageType.Warning);
                Field(nameof(AnimatorOptimizerComponent.mergeLayersWithEquivalentMasks), "合并等价 Mask");
                Field(nameof(AnimatorOptimizerComponent.resolveOverridePriorityWithStaticComposite), "合成静态曲线优先级");
                Field(nameof(AnimatorOptimizerComponent.strictCurveConflicts), "严格曲线冲突检查");
                if (!Property(nameof(AnimatorOptimizerComponent.strictCurveConflicts)).boolValue)
                    EditorGUILayout.HelpBox("关闭冲突检查可能改变 Layer 覆盖顺序；推荐保留开启。", MessageType.Warning);
            }
        }

        private void DrawHeuristics()
        {
            if (!AnimatorOptimizerEditorStyles.Section("Inspector.Strategy", "优化策略")) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var property = Property(nameof(AnimatorOptimizerComponent.heuristics));
                var flags = AnimatorOptimizerEditorStyles.DrawHeuristics((AnimatorOptimizerComponent.HeuristicFlags)property.intValue,
                    "Inspector", out var details);
                property.intValue = (int)flags;
                if (details && (flags & AnimatorOptimizerComponent.HeuristicFlags.ShortAnimationEndpoints) != 0)
                {
                    Field(nameof(AnimatorOptimizerComponent.shortAnimationMaxFrames), "短动画最大帧数");
                    Field(nameof(AnimatorOptimizerComponent.shortAnimationMaxSeconds), "短动画最大播放秒数");
                }
                if (details && (flags & AnimatorOptimizerComponent.HeuristicFlags.SampleMotionTime) != 0)
                    Field(nameof(AnimatorOptimizerComponent.motionTimeSampleCount), "Motion Time 基础采样点数");
                EditorGUILayout.Space(4f);
                Field(nameof(AnimatorOptimizerComponent.mergeSynchronizedStateMachines), "合并同图状态机");
                Field(nameof(AnimatorOptimizerComponent.removeEffectivelyEmptyLayers), "清理无效果空层");
                Field(nameof(AnimatorOptimizerComponent.enableAapStateRegisterOptimization), "AAP 状态运算（实验性）");
                if (Property(nameof(AnimatorOptimizerComponent.enableAapStateRegisterOptimization)).boolValue)
                    AnimatorOptimizerEditorStyles.DrawAapSummary();
            }
        }

        private void DrawCompatibility()
        {
            if (!AnimatorOptimizerEditorStyles.Section("Inspector.Compatibility", "近似与兼容", false))
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Field(nameof(AnimatorOptimizerComponent.normalizeAlwaysOneParameters), "假定 AlwaysOne 恒为 1");
                if (Property(nameof(AnimatorOptimizerComponent.normalizeAlwaysOneParameters)).boolValue)
                    EditorGUILayout.HelpBox("名称和默认值不能证明参数恒为 1。统一这些权重参数依赖显式约定；如存在外部写入，请关闭。", MessageType.Warning);
                Field(nameof(AnimatorOptimizerComponent.allowFloatConditionApproximation), "Float 近似回退");
                EditorGUILayout.LabelField("Float 默认优先采用相邻可表示值精确阶跃，并检查等值边界的状态历史，不假定参数只有 0/1。", EditorStyles.wordWrappedMiniLabel);
                if (Property(nameof(AnimatorOptimizerComponent.allowFloatConditionApproximation)).boolValue)
                    EditorGUILayout.HelpBox("不支持的零/极端阈值允许回退到线性插值；回退时不等价于 Animator 阶跃切换。", MessageType.Warning);
                Field(nameof(AnimatorOptimizerComponent.ignoreTransitionTimingForStaticMotions), "忽略静态 Motion 的过渡时间");
                if (Property(nameof(AnimatorOptimizerComponent.ignoreTransitionTimingForStaticMotions)).boolValue)
                    EditorGUILayout.HelpBox("静态 Motion 之间的 Exit Time / Duration / Offset 会被忽略，切换变为瞬时。动态 Motion 不受此选项影响。", MessageType.Warning);
                Field(nameof(AnimatorOptimizerComponent.preserveMmdCompatibility), "保留 MMD 兼容槽位");
                if (Property(nameof(AnimatorOptimizerComponent.preserveMmdCompatibility)).boolValue)
                    EditorGUILayout.LabelField("保留首层，并在 FX 索引 1、2 插入零权重 Dummy Layer。", EditorStyles.wordWrappedMiniLabel);
                Field(nameof(AnimatorOptimizerComponent.executeLateOptimization), "尽量晚执行优化");
            }
        }

        private void DrawAdvanced()
        {
            if (!AnimatorOptimizerEditorStyles.Section("Inspector.Advanced", "高级", false))
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Field(nameof(AnimatorOptimizerComponent.maxPriorityCombinations), "最大有限输入组合数");
                if (Property(nameof(AnimatorOptimizerComponent.enableAapStateRegisterOptimization)).boolValue)
                    Field(nameof(AnimatorOptimizerComponent.maxAapDecisionNodes), "AAP 共享条件节点预算");
                Field(nameof(AnimatorOptimizerComponent.generatedLayerName), "生成层名称");
                Field(nameof(AnimatorOptimizerComponent.weightParameterPrefix), "权重参数前缀");
                Field(nameof(AnimatorOptimizerComponent.verboseLogging), "详细构建日志");
            }
        }

        private void DrawActiveRelaxations()
        {
            var active = new List<string>();
            if (!Property(nameof(AnimatorOptimizerComponent.strictCurveConflicts)).boolValue) active.Add("关闭冲突检查");
            if (Property(nameof(AnimatorOptimizerComponent.writeDefaultsPolicy)).enumValueIndex ==
                (int)AnimatorOptimizerComponent.WriteDefaultsPolicy.TreatAsWriteDefaultsOn) active.Add("强制 WD On");
            if (Property(nameof(AnimatorOptimizerComponent.normalizeAlwaysOneParameters)).boolValue) active.Add("AlwaysOne 恒为 1");
            if (Property(nameof(AnimatorOptimizerComponent.allowFloatConditionApproximation)).boolValue) active.Add("Float 近似");
            if (Property(nameof(AnimatorOptimizerComponent.ignoreTransitionTimingForStaticMotions)).boolValue) active.Add("忽略过渡时间");
            if (active.Count > 0)
                EditorGUILayout.HelpBox("额外放宽：" + string.Join("、", active) +
                    "。这些设置不受启发式预设控制，折叠高级选项也不会将其关闭。", MessageType.Warning);
        }

        private void EnsureAnalysisQueued(AnimatorOptimizerComponent component)
        {
            var controller = ResolveController(component);
            var key = AnimatorOptimizerEditorStyles.BuildOptionsKey(AnimatorOptimizerConfigData.CreateOptions(component));
            if (controller != cachedController || key != cachedOptionsKey)
                analysisDirty = true;
            if (!analysisDirty || analysisQueued)
                return;
            analysisQueued = true;
            EditorApplication.delayCall += RunQueuedAnalysis;
        }

        private void RunQueuedAnalysis()
        {
            analysisQueued = false;
            if (this == null || target == null)
                return;
            var component = (AnimatorOptimizerComponent)target;
            cachedController = ResolveController(component);
            var options = AnimatorOptimizerConfigData.CreateOptions(component);
            cachedOptionsKey = AnimatorOptimizerEditorStyles.BuildOptionsKey(options);
            analysisError = null;
            try
            {
                cachedReport = cachedController == null ? null : AnimatorOptimizerAnalyzer.Analyze(cachedController, options);
            }
            catch (Exception exception)
            {
                cachedReport = null;
                analysisError = exception.Message;
            }
            analysisDirty = false;
            Repaint();
        }

        private void DrawAnalysis(AnimatorOptimizerComponent component)
        {
            EditorGUILayout.Space(5f);
            AnimatorOptimizerEditorStyles.Separator();
            EditorGUILayout.Space(5f);
            using (new EditorGUILayout.HorizontalScope())
            {
                AnimatorOptimizerEditorStyles.ClippedText("分析预览", AnimatorOptimizerEditorStyles.ClippedTitle);
                if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(48f)))
                    Invalidate();
            }
            if (!string.IsNullOrEmpty(analysisError))
                EditorGUILayout.HelpBox(analysisError, MessageType.Error);
            else if (analysisQueued || analysisDirty)
                EditorGUILayout.LabelField("等待分析…", EditorStyles.centeredGreyMiniLabel);
            else if (cachedController == null)
                EditorGUILayout.HelpBox("没有解析到 AnimatorController。请检查目标配置；AnimatorOverrideController 暂不支持。", MessageType.Warning);
            else if (cachedReport != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    AnimatorOptimizerEditorStyles.ClippedText(cachedController.name, AnimatorOptimizerEditorStyles.ClippedTitle,
                        AssetDatabase.GetAssetPath(cachedController));
                    AnimatorOptimizerEditorStyles.DrawBadge($"{cachedReport.ConvertibleLayerCount} / {cachedReport.Layers.Count} 可转换",
                        cachedReport.ConvertibleLayerCount > 0 ? AnimatorOptimizerEditorStyles.ConvertibleColor : AnimatorOptimizerEditorStyles.UnsupportedColor);
                }
                EditorGUILayout.LabelField("这是源 Controller 预览；构建时会重新分析其它插件生成后的最终 Controller。", EditorStyles.wordWrappedMiniLabel);
                var heuristicCount = cachedReport.ConvertibleLayers.Count(layer => layer.UsesHeuristics);
                if (heuristicCount > 0)
                    EditorGUILayout.LabelField($"其中 {heuristicCount} 层依赖已启用的启发式；详细面板可查看每层假设。", EditorStyles.wordWrappedMiniLabel);
                var optimizations = cachedReport.ConvertibleLayers.Sum(layer => layer.Optimizations.Count);
                var actionable = cachedReport.Layers.Count(layer => layer.Suggestions.Count > 0);
                EditorGUILayout.LabelField($"{optimizations} 项简化 / 证明记录  ·  {actionable} 层有处理建议", EditorStyles.wordWrappedMiniLabel);
                foreach (var layer in cachedReport.Layers.Where(layer => !layer.CanConvert && layer.Suggestions.Count > 0).Take(2))
                    EditorGUILayout.LabelField($"[{layer.Index}] {layer.Name}：{layer.Suggestions[0]}", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUI.DisabledScope(ResolveController(component) == null))
            {
                if (GUILayout.Button("打开详细分析与优化面板", GUILayout.Height(28f)))
                {
                    var window = AnimatorOptimizerWindow.ShowWindow();
                    window.SetTarget(ResolveController(component), AnimatorOptimizerConfigData.CreateOptions(component));
                }
            }
        }

        internal static AnimatorController ResolveController(AnimatorOptimizerComponent component)
        {
            if (component == null)
                return null;
            if (component.targetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
                return component.controller as AnimatorController;
            if (component.targetMode == AnimatorOptimizerComponent.TargetMode.AnimatorComponent)
            {
                var descriptorRoot = component.GetComponentInParent<VRCAvatarDescriptor>();
                var avatarRoot = descriptorRoot != null ? descriptorRoot.gameObject : component.gameObject;
                var config = AnimatorOptimizerConfigData.FromComponent(component, avatarRoot, requireEnabled: false);
                var animator = config.ResolveAnimator(avatarRoot);
                return animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            }
            var descriptor = component.GetComponent<VRCAvatarDescriptor>() ?? component.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null || !descriptor.customizeAnimationLayers)
                return null;
            var layerType = ToVrcLayerType(component.playableLayer);
            var layers = component.playableLayer <= AnimatorOptimizerComponent.PlayableLayer.FX
                ? descriptor.baseAnimationLayers : descriptor.specialAnimationLayers;
            var layer = layers?.FirstOrDefault(candidate => candidate.type == layerType);
            return layer == null || layer.Value.isDefault ? null : layer.Value.animatorController as AnimatorController;
        }

        private static VRCAvatarDescriptor.AnimLayerType ToVrcLayerType(AnimatorOptimizerComponent.PlayableLayer layer)
        {
            switch (layer)
            {
                case AnimatorOptimizerComponent.PlayableLayer.Base: return VRCAvatarDescriptor.AnimLayerType.Base;
                case AnimatorOptimizerComponent.PlayableLayer.Additive: return VRCAvatarDescriptor.AnimLayerType.Additive;
                case AnimatorOptimizerComponent.PlayableLayer.Gesture: return VRCAvatarDescriptor.AnimLayerType.Gesture;
                case AnimatorOptimizerComponent.PlayableLayer.Action: return VRCAvatarDescriptor.AnimLayerType.Action;
                case AnimatorOptimizerComponent.PlayableLayer.FX: return VRCAvatarDescriptor.AnimLayerType.FX;
                case AnimatorOptimizerComponent.PlayableLayer.Sitting: return VRCAvatarDescriptor.AnimLayerType.Sitting;
                case AnimatorOptimizerComponent.PlayableLayer.TPose: return VRCAvatarDescriptor.AnimLayerType.TPose;
                default: return VRCAvatarDescriptor.AnimLayerType.IKPose;
            }
        }
    }
}
