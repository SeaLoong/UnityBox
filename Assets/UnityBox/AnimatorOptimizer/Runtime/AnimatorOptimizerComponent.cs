using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Animator Optimizer")]
#endif
    [DisallowMultipleComponent]
    public sealed class AnimatorOptimizerComponent : MonoBehaviour
#if UNITYBOX_VRC_BASE
        , VRC.SDKBase.IEditorOnly
#endif
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

        public enum WriteDefaultsPolicy
        {
            [InspectorName("严格：仅 WD On")]
            Strict,
            [InspectorName("自动：保证覆盖的 WD Off")]
            AllowCoveredStates,
            [InspectorName("放宽：视为 WD On（可能改变效果）")]
            TreatAsWriteDefaultsOn,
        }

        [Flags]
        public enum HeuristicFlags
        {
            None = 0,
            [InspectorName("Float 开关假定为 0/1")]
            BinaryFloatToggles = 1,
            [InspectorName("短动画取播放端点")]
            ShortAnimationEndpoints = 2,
            [InspectorName("Motion Time 采样树")]
            SampleMotionTime = 4,
            [InspectorName("双态返回条件按补集补齐")]
            ComplementaryToggle = 8,
            [InspectorName("稳定输入下消除中间状态")]
            StableInputConvergence = 16,
        }

        [Tooltip("要优化 Avatar 的哪个 Animator Controller。默认处理 Avatar Descriptor 的 FX 层。")]
        public TargetMode targetMode = TargetMode.AvatarPlayableLayer;

        [Tooltip("Target Mode 为 Avatar Playable Layer 时使用的层。")]
        public PlayableLayer playableLayer = PlayableLayer.FX;

        [Tooltip("指定 Avatar 内的 Animator；留空优先根 Animator，否则仅接受唯一子 Animator。多个候选时必须指定。")]
        public Animator animator;

        [Tooltip("Target Mode 为 Explicit Controller 时指定 Controller；构建必须有可唯一定位的 Avatar 内 Animator 接收结果。离线优化可用工具窗口。")]
        public RuntimeAnimatorController controller;

        [Tooltip("关闭后只处理 layerIndices 中的 Layer；开启后自动处理所有通过安全分析的 Layer。")]
        public bool optimizeAllConvertibleLayers = true;

        [Tooltip("当 optimizeAllConvertibleLayers 关闭时要处理的 Layer 索引。")]
        public List<int> layerIndices = new List<int>();

        [Tooltip("仅对 Animator / Explicit Controller 目标显式启用 VRChat 内建参数语义；子 Animator 默认不会收到 Avatar Playable 参数。仅在调用方确实提供这些参数时开启。Avatar Playable Layer 目标始终使用 VRChat 语义。")]
        public bool useVrChatParameterSemantics = false;

        [Tooltip("把本地 Animator Bool 参数改为 Float。VRChat Expression 自动映射 0/1；普通 Unity 脚本须改用 SetFloat，否则关闭升级。")]
        public bool upgradeBoolParametersToFloat = true;

        [Tooltip("把本地 Animator Int 参数改为 Float。VRChat Expression 自动映射整数值；普通 Unity 脚本须改用 SetFloat，否则关闭升级。")]
        public bool upgradeIntParametersToFloat = true;

        [Tooltip("Float 条件默认优先用相邻可表示值构造精确阶跃；此选项仅允许不支持的零/极端阈值回退到近似。存在状态历史的图仍需单独证明。")]
        public bool allowFloatConditionApproximation = false;

        [Tooltip("默认只在每个分支保证覆盖 Controller 动画绑定时接受 WD Off。显式选择“视为 WD On”允许默认值回写变化，但不会绕过 Behaviour/AAP 等检查。")]
        public WriteDefaultsPolicy writeDefaultsPolicy = WriteDefaultsPolicy.AllowCoveredStates;

        [Tooltip("忽略静态 Motion 之间 Transition 的 Exit Time / Duration / Offset。开启后这些切换会变为瞬时，不再有过渡淡入淡出。")]
        public bool ignoreTransitionTimingForStaticMotions = false;

        [Tooltip("可选启发式，不是完整等价证明。实用预设适合常见 0/1 开关和短动画；激进预设还允许 Motion Time 采样、补齐双态返回条件及提前收敛。实际采用的假设会逐层报告。")]
        public HeuristicFlags heuristics = HeuristicFlags.None;

        [Range(1, 10)]
        [Tooltip("短动画端点化同时要求帧数和实际播放秒数不超过限制；循环、骨骼、AAP、事件动画不参与。")]
        public int shortAnimationMaxFrames = 5;

        [Range(0.01f, 0.25f)]
        public float shortAnimationMaxSeconds = 0.1f;

        [Range(2, 33)]
        [Tooltip("Motion Time 使用这些均匀采样点，并补入原曲线关键时间（总计最多 65 点）。采样点之间线性插值；增加采样数可更细致地保留缓动形状。")]
        public int motionTimeSampleCount = 9;

        [Tooltip("严格阻止源 Layer 之间或与未合并 Layer 之间存在重复动画绑定。关闭可能改变原 Layer 覆盖优先级。")]
        public bool strictCurveConflicts = true;

        [Tooltip("允许使用同一内容的 AvatarMask 的 Layer 合并；生成层会继承该 Mask。")]
        public bool mergeLayersWithEquivalentMasks = true;

        [Tooltip("当 Layer 有重复曲线时，若输出是静态 Clip 且参数域有限，枚举输入并按原 Layer 顺序生成复合 Clip 来模拟 Override 优先级。")]
        public bool resolveOverridePriorityWithStaticComposite = true;

        [Tooltip("合并相同 Entry/Exit/Transition 结构、互不重叠的静态属性层，保留原状态历史和 WD，不强制变成无状态树。可用于旧版 MA 的重复 Shape/Material 响应式层；AAP、NaN、骨骼和回调层保留。")]
        public bool mergeSynchronizedStateMachines = true;

        [Tooltip("用共享 AAP 决策图计算混合 Bool/Int/Float 条件、有序 Entry/AnyState 和状态保持。Update 与 Output 同帧计算下一状态，支持首帧 Entry 初始化；每个源层生成更新/输出两层。")]
        public bool enableAapStateRegisterOptimization = false;

        [Range(64, 16384)]
        [Tooltip("AAP 共享条件节点数量上限；输入组合由共享决策图压缩，生成预算包含更新与输出两套分支。")]
        public int maxAapDecisionNodes = 4096;

        [Tooltip("删除能证明没有可见动画输出的空 Layer；默认只删除非首层，并保留含 AAP/Behaviour 的 Layer。")]
        public bool removeEffectivelyEmptyLayers = true;

        [Tooltip("显式假定名称含 AlwaysOne 且默认值为 1 的参数运行时恒为 1。名称和默认值无法排除外部写入；仅在确认此约定后开启，否则可能改变权重。")]
        public bool normalizeAlwaysOneParameters = false;

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
            useVrChatParameterSemantics = false;
            upgradeBoolParametersToFloat = true;
            upgradeIntParametersToFloat = true;
            allowFloatConditionApproximation = false;
            writeDefaultsPolicy = WriteDefaultsPolicy.AllowCoveredStates;
            ignoreTransitionTimingForStaticMotions = false;
            heuristics = HeuristicFlags.None;
            shortAnimationMaxFrames = 5;
            shortAnimationMaxSeconds = 0.1f;
            motionTimeSampleCount = 9;
            strictCurveConflicts = true;
            mergeLayersWithEquivalentMasks = true;
            resolveOverridePriorityWithStaticComposite = true;
            mergeSynchronizedStateMachines = true;
            enableAapStateRegisterOptimization = false;
            maxAapDecisionNodes = 4096;
            removeEffectivelyEmptyLayers = true;
            normalizeAlwaysOneParameters = false;
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
            maxAapDecisionNodes = Mathf.Clamp(maxAapDecisionNodes, 64, 16384);
            heuristics &= (HeuristicFlags)31;
            shortAnimationMaxFrames = Mathf.Clamp(shortAnimationMaxFrames, 1, 10);
            shortAnimationMaxSeconds = float.IsNaN(shortAnimationMaxSeconds) || float.IsInfinity(shortAnimationMaxSeconds)
                ? 0.1f : Mathf.Clamp(shortAnimationMaxSeconds, 0.01f, 0.25f);
            motionTimeSampleCount = Mathf.Clamp(motionTimeSampleCount, 2, 33);
            if (!Enum.IsDefined(typeof(WriteDefaultsPolicy), writeDefaultsPolicy))
                writeDefaultsPolicy = WriteDefaultsPolicy.Strict;

            var seen = new HashSet<int>();
            for (var index = layerIndices.Count - 1; index >= 0; index--)
            {
                if (layerIndices[index] < 0 || !seen.Add(layerIndices[index]))
                    layerIndices.RemoveAt(index);
            }
        }
#endif
    }
}
