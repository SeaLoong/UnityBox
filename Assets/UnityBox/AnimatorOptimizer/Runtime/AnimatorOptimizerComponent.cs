using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace UnityBox.AnimatorOptimizer
{
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Animator Optimizer")]
#endif
    [DisallowMultipleComponent]
    public sealed class AnimatorOptimizerComponent : MonoBehaviour, IEditorOnly
    {
        public enum TargetMode
        {
            AvatarPlayableLayer,
            AnimatorComponent,
            ExplicitController,
        }

        public enum PlayableLayer
        {
            Base,
            Additive,
            Gesture,
            Action,
            FX,
            Sitting,
            TPose,
            IKPose,
        }

        [Tooltip("要优化 Avatar 的哪个 Animator Controller。默认处理 Avatar Descriptor 的 FX 层。")]
        public TargetMode targetMode = TargetMode.AvatarPlayableLayer;

        [Tooltip("Target Mode 为 Avatar Playable Layer 时使用的层。")]
        public PlayableLayer playableLayer = PlayableLayer.FX;

        [Tooltip("Target Mode 为 Animator Component 时指定 Animator；留空会在 Avatar 根及子对象中自动寻找。")]
        public Animator animator;

        [Tooltip("Target Mode 为 Explicit Controller 时指定 Controller；若同时指定 Animator，构建结果会挂回该 Animator。")]
        public RuntimeAnimatorController controller;

        [Tooltip("关闭后只处理 layerIndices 中的 Layer；开启后自动处理所有通过安全分析的 Layer。")]
        public bool optimizeAllConvertibleLayers = true;

        [Tooltip("当 optimizeAllConvertibleLayers 关闭时要处理的 Layer 索引。")]
        public List<int> layerIndices = new List<int>();

        [Tooltip("把本地 Animator Bool 参数改为 Float。VRChat Bool Expression 参数会按 0/1 映射到本地 Float。")]
        public bool upgradeBoolParametersToFloat = true;

        [Tooltip("把本地 Animator Int 参数改为 Float。VRChat Int Expression 参数会以整数值映射到本地 Float。")]
        public bool upgradeIntParametersToFloat = true;

        [Tooltip("允许用 BlendTree 近似 Float Transition 的阶跃条件。关闭时，Float 条件会保留原 Layer。")]
        public bool allowFloatConditionApproximation = false;

        [Tooltip("严格阻止源 Layer 之间或与未合并 Layer 之间存在重复动画绑定。关闭可能改变原 Layer 覆盖优先级。")]
        public bool strictCurveConflicts = true;

        [Tooltip("允许使用同一内容的 AvatarMask 的 Layer 合并；生成层会继承该 Mask。")]
        public bool mergeLayersWithEquivalentMasks = true;

        [Tooltip("当 Layer 有重复曲线时，若输出是静态 Clip 且参数域有限，枚举输入并按原 Layer 顺序生成复合 Clip 来模拟 Override 优先级。")]
        public bool resolveOverridePriorityWithStaticComposite = true;

        [Tooltip("实验性：用 AAP StateRegister 保留无时间 Bool 状态机的 State 历史，再用 1D BlendTree 输出 Motion。会为一个源 Layer 生成 AAP 更新层和输出层。")]
        public bool enableAapStateRegisterOptimization = false;

        [Tooltip("删除能证明没有可见动画输出的空 Layer；默认只删除非首层，并保留含 AAP/Behaviour 的 Layer。")]
        public bool removeEffectivelyEmptyLayers = true;

        [Tooltip("把不同插件生成的、默认值为 1 且名称含 AlwaysOne 的安全常量参数统一用于 Direct BlendTree 权重。")]
        public bool normalizeAlwaysOneParameters = true;

        [Tooltip("尽量晚执行优化：NDMF 使用 PlatformFinish；无 NDMF 时在 VRCSDK/ASS 等高优先级 preprocess 之后执行。")]
        public bool executeLateOptimization = false;

        [Tooltip("保留 MMD 兼容层：保留首层，并在 FX Controller 索引 1、2 插入权重为 0 的 MMD Dummy Layer。")]
        public bool preserveMmdCompatibility = false;

        [Range(16, 4096)]
        [Tooltip("优先级复合树允许枚举的最大参数组合数。")]
        public int maxPriorityCombinations = 4096;

        [Tooltip("构建时输出详细的跳过和转换日志。")]
        public bool verboseLogging = false;

        [Tooltip("生成层名称。生成层必须保持 Write Defaults On。")]
        public string generatedLayerName = "(WD On) AnimatorOptimizer";

        [Tooltip("内部 Direct BlendTree 权重参数名前缀。")]
        public string weightParameterPrefix = "AO/LayerWeight";

#if UNITY_EDITOR
        private void Reset()
        {
            targetMode = TargetMode.AvatarPlayableLayer;
            playableLayer = PlayableLayer.FX;
            optimizeAllConvertibleLayers = true;
            upgradeBoolParametersToFloat = true;
            upgradeIntParametersToFloat = true;
            allowFloatConditionApproximation = false;
            strictCurveConflicts = true;
            mergeLayersWithEquivalentMasks = true;
            resolveOverridePriorityWithStaticComposite = true;
            enableAapStateRegisterOptimization = false;
            removeEffectivelyEmptyLayers = true;
            normalizeAlwaysOneParameters = true;
            executeLateOptimization = false;
            preserveMmdCompatibility = false;
            maxPriorityCombinations = 4096;
            verboseLogging = false;
            generatedLayerName = "(WD On) AnimatorOptimizer";
            weightParameterPrefix = "AO/LayerWeight";
            layerIndices = new List<int>();
        }

        private void OnValidate()
        {
            generatedLayerName = string.IsNullOrWhiteSpace(generatedLayerName)
                ? "(WD On) AnimatorOptimizer"
                : generatedLayerName.Trim();
            weightParameterPrefix = string.IsNullOrWhiteSpace(weightParameterPrefix)
                ? "AO/LayerWeight"
                : weightParameterPrefix.Trim();

            if (layerIndices == null)
                layerIndices = new List<int>();

            maxPriorityCombinations = Mathf.Clamp(maxPriorityCombinations, 16, 4096);

            for (var index = layerIndices.Count - 1; index >= 0; index--)
            {
                if (layerIndices[index] < 0)
                    layerIndices.RemoveAt(index);
            }
        }
#endif
    }
}
