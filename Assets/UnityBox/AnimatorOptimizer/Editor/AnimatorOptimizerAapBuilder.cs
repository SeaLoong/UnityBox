using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    public readonly struct AnimatorOptimizerAapRange
    {
        public AnimatorOptimizerAapRange(float min, float max)
        {
            if (float.IsNaN(min) || float.IsInfinity(min) ||
                float.IsNaN(max) || float.IsInfinity(max) || min >= max)
                throw new ArgumentException("AAP 范围必须是有限且 min < max。", nameof(min));
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }
    }

    /// <summary>
    /// Creates BlendTree/AAP computation nodes. An AAP is represented by a FloatCurve
    /// whose binding is path="", type=Animator, propertyName=the controller parameter name.
    /// No StateMachineBehaviour is involved in this backend.
    /// </summary>
    public static class AnimatorOptimizerAapBuilder
    {
        private const string NormalizedBlendValuesProperty = "m_NormalizedBlendValues";
        private static int treeCounter;
        private static int clipCounter;

        public static AnimationClip CreateAapValueClip(
            AnimatorController controller,
            string parameterName,
            float value,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, parameterName);
            var clip = new AnimationClip
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_" + Sanitize(parameterName) + "_" + clipCounter++
                    : name,
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), parameterName);
            AnimationUtility.SetEditorCurve(
                clip,
                binding,
                AnimationCurve.Constant(0f, 1f / 60f, value));
            AddSubAsset(controller, clip);
            return clip;
        }

        public static BlendTree BuildBooleanTruthTable(
            AnimatorController controller,
            string outputParameter,
            IReadOnlyList<string> inputParameters,
            IReadOnlyDictionary<int, float> outputValues,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, outputParameter);
            var inputs = ValidateInputParameters(controller, inputParameters);
            var entryCount = 1 << inputs.Count;
            if (entryCount > 4096)
                throw new ArgumentException("AAP 真值表最多生成 4096 个组合。", nameof(inputParameters));
            if (outputValues == null || outputValues.Count != entryCount ||
                Enumerable.Range(0, entryCount).Any(index => !outputValues.ContainsKey(index)))
                throw new ArgumentException($"AAP 真值表必须完整覆盖 {entryCount} 个组合。", nameof(outputValues));

            var leafClips = new Dictionary<float, AnimationClip>();
            return (BlendTree)BuildBooleanNode(controller, outputParameter, inputs, outputValues, leafClips, 0, 0,
                string.IsNullOrWhiteSpace(name) ? "AnimatorOptimizer_AAP_Bool" : name);
        }

        /// <summary>
        /// Builds the feedback tree for a finite, zero-duration Boolean state machine.
        /// The outer 1D tree reads the current state register; each child is a Boolean
        /// decision tree that animates the register to the next state index. The caller
        /// should place this tree in a WD On state above/before the output tree that reads
        /// the same register.
        /// </summary>
        public static BlendTree BuildBooleanStateRegisterUpdate(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<string> inputParameters,
            int stateCount,
            int defaultState,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> nextStateByStateAndMask,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, stateRegisterParameter);
            if (stateCount < 1 || defaultState < 0 || defaultState >= stateCount)
                throw new ArgumentOutOfRangeException(nameof(stateCount), "State Register 的状态数量或默认状态无效。");
            if (stateCount > 4096)
                throw new ArgumentOutOfRangeException(nameof(stateCount), "AAP State Register 状态数量不能超过 4096。");
            var inputs = ValidateInputParameters(controller, inputParameters);
            var entryCount = 1 << inputs.Count;
            if (entryCount > 4096)
                throw new ArgumentException("AAP StateRegister 最多生成 4096 个 Bool 组合。", nameof(inputParameters));
            if (nextStateByStateAndMask == null || nextStateByStateAndMask.Count != stateCount)
                throw new ArgumentException("必须为每个 State 提供完整的下一状态表。", nameof(nextStateByStateAndMask));

            var stateTrees = new Motion[stateCount];
            for (var stateIndex = 0; stateIndex < stateCount; stateIndex++)
            {
                if (!nextStateByStateAndMask.TryGetValue(stateIndex, out var table) ||
                    table == null || table.Count != entryCount ||
                    Enumerable.Range(0, entryCount).Any(mask => !table.ContainsKey(mask)))
                    throw new ArgumentException($"State {stateIndex} 的下一状态表不完整。", nameof(nextStateByStateAndMask));
                foreach (var nextState in table.Values)
                {
                    if (nextState < 0 || nextState >= stateCount)
                        throw new ArgumentException("下一状态编号超出 stateCount。", nameof(nextStateByStateAndMask));
                }

                var outputValues = table.ToDictionary(pair => pair.Key, pair => (float)pair.Value);
                stateTrees[stateIndex] = BuildBooleanNode(
                    controller,
                    stateRegisterParameter,
                    inputs,
                    outputValues,
                    new Dictionary<float, AnimationClip>(),
                    0,
                    0,
                    (string.IsNullOrWhiteSpace(name) ? "AnimatorOptimizer_AAP_StateRegister" : name) +
                    "_State_" + stateIndex);
            }

            var root = new BlendTree
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_StateRegister_" + treeCounter++
                    : name,
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

        public static BlendTree BuildStateOutputTree(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<Motion> stateMotions,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, stateRegisterParameter);
            if (stateMotions == null || stateMotions.Count == 0 || stateMotions.Any(motion => motion == null))
                throw new ArgumentException("State Output Tree 必须包含至少一个非空 Motion。", nameof(stateMotions));

            var tree = new BlendTree
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_StateOutput_" + treeCounter++
                    : name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = stateRegisterParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = stateMotions.Select((motion, stateIndex) => new ChildMotion
            {
                motion = motion,
                threshold = stateIndex,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        public static BlendTree BuildRemap(
            AnimatorController controller,
            string outputParameter,
            string inputParameter,
            IReadOnlyList<float> inputThresholds,
            IReadOnlyList<float> outputValues,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, outputParameter);
            RequireFloatParameter(controller, inputParameter);
            ValidateThresholds(inputThresholds, outputValues);
            var clips = outputValues.Select((value, index) => CreateAapValueClip(
                controller,
                outputParameter,
                value,
                "AnimatorOptimizer_AAP_Remap_" + clipCounter++ + "_" + index)).ToArray();
            var tree = new BlendTree
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_Remap_" + treeCounter++
                    : name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = inputParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = inputThresholds.Select((threshold, index) => new ChildMotion
            {
                motion = clips[index],
                threshold = threshold,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        public static BlendTree BuildAddition(
            AnimatorController controller,
            string outputParameter,
            string leftParameter,
            string rightParameter,
            AnimatorOptimizerAapRange leftRange,
            AnimatorOptimizerAapRange rightRange,
            string weightParameter,
            string name = null)
        {
            RequireConstantOneWeight(controller, weightParameter);
            RequireDefaultZero(controller, outputParameter);
            var left = BuildRemap(controller, outputParameter, leftParameter,
                new[] { leftRange.Min, leftRange.Max },
                new[] { leftRange.Min, leftRange.Max },
                "AnimatorOptimizer_AAP_Add_Left_" + treeCounter++);
            var right = BuildRemap(controller, outputParameter, rightParameter,
                new[] { rightRange.Min, rightRange.Max },
                new[] { rightRange.Min, rightRange.Max },
                "AnimatorOptimizer_AAP_Add_Right_" + treeCounter++);
            return BuildDirectArithmeticTree(controller, weightParameter,
                new[] { left, right }, false,
                string.IsNullOrWhiteSpace(name) ? "AnimatorOptimizer_AAP_Add" : name);
        }

        public static BlendTree BuildSubtraction(
            AnimatorController controller,
            string outputParameter,
            string leftParameter,
            string rightParameter,
            AnimatorOptimizerAapRange leftRange,
            AnimatorOptimizerAapRange rightRange,
            string weightParameter,
            string name = null)
        {
            RequireConstantOneWeight(controller, weightParameter);
            RequireDefaultZero(controller, outputParameter);
            var left = BuildRemap(controller, outputParameter, leftParameter,
                new[] { leftRange.Min, leftRange.Max },
                new[] { leftRange.Min, leftRange.Max },
                "AnimatorOptimizer_AAP_Sub_Left_" + treeCounter++);
            var right = BuildRemap(controller, outputParameter, rightParameter,
                new[] { rightRange.Min, rightRange.Max },
                new[] { -rightRange.Min, -rightRange.Max },
                "AnimatorOptimizer_AAP_Sub_Right_" + treeCounter++);
            return BuildDirectArithmeticTree(controller, weightParameter,
                new[] { left, right }, false,
                string.IsNullOrWhiteSpace(name) ? "AnimatorOptimizer_AAP_Sub" : name);
        }

        public static BlendTree BuildMultiplication(
            AnimatorController controller,
            string outputParameter,
            string leftParameter,
            string rightParameter,
            AnimatorOptimizerAapRange leftRange,
            AnimatorOptimizerAapRange rightRange,
            string name = null)
        {
            RequireFloatParameter(controller, outputParameter);
            RequireFloatParameter(controller, leftParameter);
            RequireFloatParameter(controller, rightParameter);
            var leftThresholds = new[] { leftRange.Min, leftRange.Max };
            var children = leftThresholds.Select((leftValue, index) =>
                (Motion)BuildRemap(
                    controller,
                    outputParameter,
                    rightParameter,
                    new[] { rightRange.Min, rightRange.Max },
                    new[] { leftValue * rightRange.Min, leftValue * rightRange.Max },
                    "AnimatorOptimizer_AAP_Mul_Row_" + treeCounter++ + "_" + index)).ToArray();
            var tree = new BlendTree
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_Multiply_" + treeCounter++
                    : name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = leftParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.children = leftThresholds.Select((threshold, index) => new ChildMotion
            {
                motion = children[index],
                threshold = threshold,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        /// <summary>
        /// Builds A / (1 + B) using a normalized Direct tree. The denominator must stay
        /// non-negative; this is the exact normalization construction, not a reciprocal
        /// approximation. A / B can be represented by passing B-1 as the denominator input.
        /// </summary>
        public static BlendTree BuildOnePlusDivision(
            AnimatorController controller,
            string outputParameter,
            string numeratorParameter,
            string denominatorParameter,
            AnimatorOptimizerAapRange numeratorRange,
            string weightParameter,
            string name = null)
        {
            RequireConstantOneWeight(controller, weightParameter);
            RequireDefaultZero(controller, outputParameter);
            var numerator = BuildRemap(controller, outputParameter, numeratorParameter,
                new[] { numeratorRange.Min, numeratorRange.Max },
                new[] { numeratorRange.Min, numeratorRange.Max },
                "AnimatorOptimizer_AAP_Div_Numerator_" + treeCounter++);
            var dummy = new AnimationClip
            {
                name = "AnimatorOptimizer_AAP_Div_Dummy_" + clipCounter++,
                legacy = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AddSubAsset(controller, dummy);
            var tree = new BlendTree
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? "AnimatorOptimizer_AAP_Division_" + treeCounter++
                    : name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            SetNormalizedBlendValues(tree, true);
            tree.children = new[]
            {
                new ChildMotion
                {
                    motion = dummy,
                    directBlendParameter = denominatorParameter,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
                new ChildMotion
                {
                    motion = numerator,
                    directBlendParameter = weightParameter,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
            };
            AddSubAsset(controller, tree);
            return tree;
        }

        private static Motion BuildBooleanNode(
            AnimatorController controller,
            string outputParameter,
            IReadOnlyList<string> inputs,
            IReadOnlyDictionary<int, float> outputValues,
            IDictionary<float, AnimationClip> leafClips,
            int depth,
            int mask,
            string name)
        {
            if (depth >= inputs.Count)
            {
                var value = outputValues[mask];
                if (!leafClips.TryGetValue(value, out var clip))
                {
                    clip = CreateAapValueClip(controller, outputParameter, value,
                        name + "_Value_" + value.ToString("R", CultureInfo.InvariantCulture));
                    leafClips.Add(value, clip);
                }
                return clip;
            }

            var tree = new BlendTree
            {
                name = name + "_Node_" + depth + "_" + mask,
                blendType = BlendTreeType.Simple1D,
                blendParameter = inputs[depth],
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            var falseMotion = BuildBooleanNode(controller, outputParameter, inputs, outputValues,
                leafClips, depth + 1, mask, name);
            var trueMotion = BuildBooleanNode(controller, outputParameter, inputs, outputValues,
                leafClips, depth + 1, mask | (1 << depth), name);
            tree.children = new[]
            {
                new ChildMotion
                {
                    motion = falseMotion,
                    threshold = 0f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
                new ChildMotion
                {
                    motion = trueMotion,
                    threshold = 1f,
                    timeScale = 1f,
                    cycleOffset = 0f,
                    mirror = false,
                },
            };
            AddSubAsset(controller, tree);
            return tree;
        }

        private static BlendTree BuildDirectArithmeticTree(
            AnimatorController controller,
            string weightParameter,
            IReadOnlyList<Motion> motions,
            bool normalized,
            string name)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            SetNormalizedBlendValues(tree, normalized);
            tree.children = motions.Select(motion => new ChildMotion
            {
                motion = motion,
                directBlendParameter = weightParameter,
                timeScale = 1f,
                cycleOffset = 0f,
                mirror = false,
            }).ToArray();
            AddSubAsset(controller, tree);
            return tree;
        }

        private static List<string> ValidateInputParameters(
            AnimatorController controller,
            IReadOnlyList<string> inputParameters)
        {
            var inputs = (inputParameters ?? Array.Empty<string>()).ToList();
            if (inputs.Count == 0 || inputs.Count > 30)
                throw new ArgumentException("AAP 真值表需要 1 到 30 个输入参数。", nameof(inputParameters));
            if (inputs.Distinct(StringComparer.Ordinal).Count() != inputs.Count)
                throw new ArgumentException("AAP 输入参数不能重复。", nameof(inputParameters));
            foreach (var input in inputs)
                RequireFloatParameter(controller, input);
            return inputs;
        }

        private static void ValidateThresholds(
            IReadOnlyList<float> thresholds,
            IReadOnlyList<float> values)
        {
            if (thresholds == null || values == null || thresholds.Count != values.Count || thresholds.Count < 2)
                throw new ArgumentException("AAP 1D 重映射至少需要两个一一对应的阈值和值。", nameof(thresholds));
            for (var index = 0; index < thresholds.Count; index++)
            {
                if (float.IsNaN(thresholds[index]) || float.IsInfinity(thresholds[index]) ||
                    float.IsNaN(values[index]) || float.IsInfinity(values[index]) ||
                    (index > 0 && thresholds[index] <= thresholds[index - 1]))
                    throw new ArgumentException("AAP 1D 阈值必须是严格递增的有限数值。", nameof(thresholds));
            }
        }

        private static void RequireController(AnimatorController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(controller)))
                throw new InvalidOperationException("AAP 生成器需要已保存的 AnimatorController。");
        }

        private static void RequireFloatParameter(AnimatorController controller, string name)
        {
            var parameter = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (parameter == null || parameter.type != AnimatorControllerParameterType.Float)
                throw new InvalidOperationException($"AAP BlendTree 参数 '{name}' 必须是本地 Animator Float 参数。可先执行 Bool/Int → Float 映射。");
        }

        private static void RequireConstantOneWeight(AnimatorController controller, string name)
        {
            RequireFloatParameter(controller, name);
            var parameter = controller.parameters.First(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (!Mathf.Approximately(parameter.defaultFloat, 1f))
                throw new InvalidOperationException($"AAP Direct 权重参数 '{name}' 的默认值必须为 1。");
        }

        private static void RequireDefaultZero(AnimatorController controller, string name)
        {
            RequireFloatParameter(controller, name);
            var parameter = controller.parameters.First(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (!Mathf.Approximately(parameter.defaultFloat, 0f))
                throw new InvalidOperationException($"AAP 输出参数 '{name}' 的默认值必须为 0，才能进行未归一化 Direct 运算。");
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

        private static void AddSubAsset(AnimatorController controller, UnityEngine.Object asset)
        {
            AssetDatabase.AddObjectToAsset(asset, controller);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Parameter"
                : new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        }
    }
}
