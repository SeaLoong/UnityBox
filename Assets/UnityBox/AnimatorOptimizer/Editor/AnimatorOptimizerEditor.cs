using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    [CustomEditor(typeof(AnimatorOptimizerComponent))]
    public sealed class AnimatorOptimizerEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetMode;
        private SerializedProperty _playableLayer;
        private SerializedProperty _animator;
        private SerializedProperty _controller;
        private SerializedProperty _optimizeAll;
        private SerializedProperty _layerIndices;
        private SerializedProperty _upgradeBool;
        private SerializedProperty _upgradeInt;
        private SerializedProperty _allowFloatApproximation;
        private SerializedProperty _strictConflicts;
        private SerializedProperty _mergeEquivalentMasks;
        private SerializedProperty _resolveOverridePriority;
        private SerializedProperty _enableAapStateRegister;
        private SerializedProperty _removeEmptyLayers;
        private SerializedProperty _normalizeAlwaysOne;
        private SerializedProperty _executeLate;
        private SerializedProperty _preserveMmd;
        private SerializedProperty _maxPriorityCombinations;
        private SerializedProperty _verbose;
        private SerializedProperty _generatedLayerName;
        private SerializedProperty _weightPrefix;

        private void OnEnable()
        {
            _targetMode = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.targetMode));
            _playableLayer = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.playableLayer));
            _animator = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.animator));
            _controller = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.controller));
            _optimizeAll = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.optimizeAllConvertibleLayers));
            _layerIndices = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.layerIndices));
            _upgradeBool = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.upgradeBoolParametersToFloat));
            _upgradeInt = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.upgradeIntParametersToFloat));
            _allowFloatApproximation = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.allowFloatConditionApproximation));
            _strictConflicts = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.strictCurveConflicts));
            _mergeEquivalentMasks = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.mergeLayersWithEquivalentMasks));
            _resolveOverridePriority = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.resolveOverridePriorityWithStaticComposite));
            _enableAapStateRegister = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.enableAapStateRegisterOptimization));
            _removeEmptyLayers = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.removeEffectivelyEmptyLayers));
            _normalizeAlwaysOne = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.normalizeAlwaysOneParameters));
            _executeLate = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.executeLateOptimization));
            _preserveMmd = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.preserveMmdCompatibility));
            _maxPriorityCombinations = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.maxPriorityCombinations));
            _verbose = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.verboseLogging));
            _generatedLayerName = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.generatedLayerName));
            _weightPrefix = serializedObject.FindProperty(nameof(AnimatorOptimizerComponent.weightParameterPrefix));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var component = (AnimatorOptimizerComponent)target;

            EditorGUILayout.LabelField("Animator Optimizer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "构建时把可证明为参数函数的 Animator Layer/状态机转换为 BlendTree，并合并安全的静态 Clip/BlendTree Motion。" +
                "含时间、历史或 Behaviour 的图会保留；AAP 运算图可通过 AnimatorOptimizerAapBuilder 显式生成。\n" +
                "推荐先使用下方 Analyze 查看哪些 Layer 会被转换。",
                MessageType.Info);

            EditorGUILayout.PropertyField(_targetMode, new GUIContent("Target"));
            var targetMode = (AnimatorOptimizerComponent.TargetMode)_targetMode.enumValueIndex;
            if (targetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer)
                EditorGUILayout.PropertyField(_playableLayer, new GUIContent("Playable Layer"));
            else
                EditorGUILayout.PropertyField(_animator, new GUIContent("Animator"));

            if (targetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
                EditorGUILayout.PropertyField(_controller, new GUIContent("Controller"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(_optimizeAll, new GUIContent("Optimize All Convertible Layers"));
            if (!_optimizeAll.boolValue)
            {
                EditorGUILayout.PropertyField(_layerIndices, new GUIContent("Layer Indices"));
                EditorGUILayout.HelpBox("只会处理列出的 Layer 索引；索引从 0 开始。", MessageType.None);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Parameter Compatibility", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_upgradeBool, new GUIContent("Upgrade Bool → Float"));
            EditorGUILayout.PropertyField(_upgradeInt, new GUIContent("Upgrade Int → Float"));
            EditorGUILayout.PropertyField(_allowFloatApproximation, new GUIContent("Approximate Float Conditions"));
            EditorGUILayout.HelpBox(
                "VRChat Bool/Int 表达式参数可以分别映射到本地 Animator Float 的 0/1 或整数值。Float Transition 本身是阶跃判断，只有勾选近似后才会生成阈值附近的 BlendTree 插值。",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Safety", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_strictConflicts, new GUIContent("Strict Curve Conflicts"));
            EditorGUILayout.PropertyField(_mergeEquivalentMasks, new GUIContent("Merge Equivalent Masks"));
            EditorGUILayout.PropertyField(_resolveOverridePriority, new GUIContent("Resolve Override Priority"));
            if (_resolveOverridePriority.boolValue)
                EditorGUILayout.PropertyField(_maxPriorityCombinations, new GUIContent("Max Priority Combinations"));
            EditorGUILayout.PropertyField(_enableAapStateRegister,
                new GUIContent("AAP StateRegister Optimization"));
            if (_enableAapStateRegister.boolValue)
                EditorGUILayout.HelpBox(
                    "仅处理无 Behaviour、无 ExitTime/Duration/Offset、Bool 条件和时间无关 Motion 的状态机；一个源 Layer 会生成 AAP 更新层和输出 BlendTree 层。",
                    MessageType.Warning);
            EditorGUILayout.PropertyField(_removeEmptyLayers, new GUIContent("Remove Effectively Empty Layers"));
            EditorGUILayout.PropertyField(_normalizeAlwaysOne, new GUIContent("Normalize AlwaysOne Parameters"));
            EditorGUILayout.PropertyField(_executeLate, new GUIContent("Execute Optimization As Late As Possible"));
            EditorGUILayout.PropertyField(_preserveMmd, new GUIContent("Preserve MMD Compatibility Layers"));
            if (_preserveMmd.boolValue)
                EditorGUILayout.HelpBox("保留首层，并在 FX Controller 的索引 1、2 插入权重为 0 的 MMD Dummy Layer。", MessageType.Info);
            EditorGUILayout.PropertyField(_verbose, new GUIContent("Verbose Build Log"));
            EditorGUILayout.PropertyField(_generatedLayerName, new GUIContent("Generated Layer Name"));
            EditorGUILayout.PropertyField(_weightPrefix, new GUIContent("Weight Parameter Prefix"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze Controller"))
                    Analyze(component);
                if (GUILayout.Button("Open Optimizer Window"))
                    OpenWindow(component);
            }

            DrawCurrentTarget(component);
        }

        private static void Analyze(AnimatorOptimizerComponent component)
        {
            var controller = ResolveController(component);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Animator Optimizer", "没有解析到 AnimatorController。", "OK");
                return;
            }

            var options = CreateOptions(component);
            var report = AnimatorOptimizerAnalyzer.Analyze(controller, options);
            var convertible = report.ConvertibleLayers.Count(layer => layer.CanConvert);
            var details = string.Join("\n", report.Layers.Select(layer =>
                $"[{layer.Index}] {layer.Name}: {(layer.CanConvert ? layer.ConversionKind.ToString() : "Skip")}"));
            Debug.Log($"[AnimatorOptimizer] '{controller.name}': {convertible}/{report.Layers.Count} convertible Layers.\n{details}");
            EditorUtility.DisplayDialog(
                "Animator Optimizer",
                $"Controller: {controller.name}\nConvertible Layers: {convertible}/{report.Layers.Count}\n\n详细原因请打开 Optimizer Window 查看。",
                "OK");
        }

        private static void OpenWindow(AnimatorOptimizerComponent component)
        {
            var controller = ResolveController(component);
            var window = AnimatorOptimizerWindow.ShowWindow();
            window.SetTarget(controller, component != null ? CreateOptions(component) : null);
        }

        private static void DrawCurrentTarget(AnimatorOptimizerComponent component)
        {
            var controller = ResolveController(component);
            if (controller == null)
            {
                EditorGUILayout.HelpBox("当前配置没有解析到 AnimatorController。", MessageType.Warning);
                return;
            }

            var report = AnimatorOptimizerAnalyzer.Analyze(controller, CreateOptions(component));
            EditorGUILayout.HelpBox(
                $"当前 Controller: {controller.name}\n可转换 Layer: {report.ConvertibleLayerCount}/{report.Layers.Count}",
                report.ConvertibleLayerCount > 1 ? MessageType.Info : MessageType.Warning);
        }

        private static AnimatorController ResolveController(AnimatorOptimizerComponent component)
        {
            if (component == null)
                return null;
            if (component.targetMode == AnimatorOptimizerComponent.TargetMode.ExplicitController)
                return component.controller as AnimatorController;
            if (component.targetMode == AnimatorOptimizerComponent.TargetMode.AnimatorComponent)
            {
                var animator = component.animator != null
                    ? component.animator
                    : component.GetComponentInChildren<Animator>(true);
                return animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            }

            var descriptor = component.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (descriptor == null)
                return null;
            var layerType = ToVrcLayerType(component.playableLayer);
            var layers = component.playableLayer <= AnimatorOptimizerComponent.PlayableLayer.FX
                ? descriptor.baseAnimationLayers
                : descriptor.specialAnimationLayers;
            var layer = layers?.FirstOrDefault(candidate => candidate.type == layerType);
            return layer?.animatorController as AnimatorController;
        }

        private static AnimatorOptimizerOptions CreateOptions(AnimatorOptimizerComponent component)
        {
            return new AnimatorOptimizerOptions
            {
                GeneratedLayerName = component.generatedLayerName,
                WeightParameterPrefix = component.weightParameterPrefix,
                StrictCurveConflicts = component.strictCurveConflicts,
                UpgradeBoolParametersToFloat = component.upgradeBoolParametersToFloat,
                UpgradeIntParametersToFloat = component.upgradeIntParametersToFloat,
                AllowFloatConditionApproximation = component.allowFloatConditionApproximation,
                MergeLayersWithEquivalentMasks = component.mergeLayersWithEquivalentMasks,
                ResolveOverridePriorityWithStaticComposite = component.resolveOverridePriorityWithStaticComposite,
                EnableAapStateRegisterOptimization = component.enableAapStateRegisterOptimization,
                RemoveEffectivelyEmptyLayers = component.removeEffectivelyEmptyLayers,
                NormalizeAlwaysOneParameters = component.normalizeAlwaysOneParameters,
                ExecuteLateOptimization = component.executeLateOptimization,
                PreserveMmdCompatibility = component.preserveMmdCompatibility,
                HasKnownPlayableLayer = component.targetMode == AnimatorOptimizerComponent.TargetMode.AvatarPlayableLayer,
                TargetPlayableLayer = component.playableLayer,
                MaxPriorityCombinations = component.maxPriorityCombinations,
                VerboseLogging = component.verboseLogging,
            };
        }

        private static VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType ToVrcLayerType(
            AnimatorOptimizerComponent.PlayableLayer playableLayer)
        {
            switch (playableLayer)
            {
                case AnimatorOptimizerComponent.PlayableLayer.Base:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Base;
                case AnimatorOptimizerComponent.PlayableLayer.Additive:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Additive;
                case AnimatorOptimizerComponent.PlayableLayer.Gesture:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Gesture;
                case AnimatorOptimizerComponent.PlayableLayer.Action:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Action;
                case AnimatorOptimizerComponent.PlayableLayer.FX:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX;
                case AnimatorOptimizerComponent.PlayableLayer.Sitting:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Sitting;
                case AnimatorOptimizerComponent.PlayableLayer.TPose:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.TPose;
                default:
                    return VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.IKPose;
            }
        }
    }
}
