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
                float.IsNaN(max) || float.IsInfinity(max) || min >= max || float.IsInfinity(max - min))
                throw new ArgumentException("AAP 范围及跨度必须是有限数，且 min < max。", nameof(min));
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
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "AAP 输出必须是有限数值。");
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
            if (outputValues.Values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
                throw new ArgumentException("AAP 真值表输出必须为有限数值。", nameof(outputValues));

            var leafClips = new Dictionary<int, AnimationClip>();
            var nodes = new Dictionary<(string Parameter, Motion False, Motion True), BlendTree>();
            return (BlendTree)BuildBooleanNode(controller, outputParameter, inputs, outputValues, leafClips, nodes, 0, 0,
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
            string name = null) => BuildBooleanStateRegisterUpdate(controller, stateRegisterParameter,
                inputParameters, stateCount, defaultState, nextStateByStateAndMask, false, name);

        public static BlendTree BuildBooleanStateRegisterUpdate(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<string> inputParameters,
            int stateCount,
            int defaultState,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> nextStateByStateAndMask,
            bool useVrChatParameterSemantics,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, stateRegisterParameter);
            if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(stateRegisterParameter, useVrChatParameterSemantics))
                throw new ArgumentException("AAP 状态寄存器不能使用 VRChat 内置参数名。", nameof(stateRegisterParameter));
            if (stateCount < 1 || defaultState < 0 || defaultState >= stateCount)
                throw new ArgumentOutOfRangeException(nameof(stateCount), "State Register 的状态数量或默认状态无效。");
            if (stateCount > 4096)
                throw new ArgumentOutOfRangeException(nameof(stateCount), "AAP State Register 状态数量不能超过 4096。");
            var register = controller.parameters.First(parameter => parameter.name == stateRegisterParameter);
            if (register.defaultFloat != defaultState)
                throw new ArgumentException("State Register 参数默认值必须与 defaultState 一致，不能仅校验编号后忽略初始状态。", nameof(defaultState));
            var inputs = ValidateInputParameters(controller, inputParameters);
            if (inputs.Contains(stateRegisterParameter))
                throw new ArgumentException("AAP 输入参数不能同时用作状态寄存器。", nameof(inputParameters));
            var entryCount = 1 << inputs.Count;
            if (entryCount > 4096)
                throw new ArgumentException("AAP StateRegister 最多生成 4096 个 Bool 组合。", nameof(inputParameters));
            if (nextStateByStateAndMask == null || nextStateByStateAndMask.Count != stateCount)
                throw new ArgumentException("必须为每个 State 提供完整的下一状态表。", nameof(nextStateByStateAndMask));
            if ((long)entryCount * stateCount > 65536)
                throw new ArgumentException("StateRegister 总状态/输入组合超过 65536。", nameof(nextStateByStateAndMask));
            for (var index = 0; index < stateCount; index++)
            {
                if (!nextStateByStateAndMask.TryGetValue(index, out var entries) || entries == null ||
                    entries.Count != entryCount || Enumerable.Range(0, entryCount).Any(mask => !entries.ContainsKey(mask)) ||
                    entries.Values.Any(next => next < 0 || next >= stateCount))
                    throw new ArgumentException("StateRegister 表不完整或包含无效目标。", nameof(nextStateByStateAndMask));
            }

            var stateTrees = new Motion[stateCount];
            var leafClips = new Dictionary<int, AnimationClip>();
            var nodes = new Dictionary<(string Parameter, Motion False, Motion True), BlendTree>();
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
                    leafClips,
                    nodes,
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

        /// <summary>
        /// Materializes a validated mixed Bool/Int/Float symbolic program. The caller
        /// owns parameter upgrades and the Update-before-Output layer boundary. Every
        /// leaf writes the initialization latch, including steady-state branches;
        /// leaving it absent there would allow WD On to restore its zero default.
        /// No intermediate condition parameters or ParameterDrivers are introduced.
        /// </summary>
        public static BlendTree BuildStateRegisterUpdate(
            AnimatorController controller,
            string stateRegisterParameter,
            AnimatorOptimizerAapProgram program,
            string initializedParameter = null,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, stateRegisterParameter);
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(stateRegisterParameter, program.UseVrChatParameterSemantics) ||
                AnimatorOptimizerAnalyzer.IsVrcGlobalParameter(initializedParameter, program.UseVrChatParameterSemantics))
                throw new ArgumentException("AAP 状态/初始化寄存器不能使用 VRChat 内置参数名。");
            if (controller.parameters.First(parameter => parameter.name == stateRegisterParameter).defaultFloat != program.DefaultState)
                throw new ArgumentException("AAP 寄存器默认值必须与符号计划一致。", nameof(stateRegisterParameter));
            if (program.RequiresInitialization)
            {
                RequireFloatParameter(controller, initializedParameter);
                if (initializedParameter == stateRegisterParameter ||
                    controller.parameters.First(parameter => parameter.name == initializedParameter).defaultFloat != 0f)
                    throw new ArgumentException("AAP 初始化标记必须是默认值为 0 的独立 Float 参数。", nameof(initializedParameter));
            }
            else if (!string.IsNullOrEmpty(initializedParameter))
                throw new ArgumentException("没有 Entry 初始化的计划不需要初始化参数。", nameof(initializedParameter));
            foreach (var axis in program.Axes.Concat(program.AssumptionAxes).GroupBy(axis => axis.Parameter, StringComparer.Ordinal)
                .Select(group => group.First()))
            {
                if (axis.Parameter == stateRegisterParameter || axis.Parameter == initializedParameter)
                    throw new ArgumentException("AAP 输入不能与新建的状态/初始化寄存器重名。");
                var declarations = controller.parameters.Where(parameter => parameter != null && parameter.name == axis.Parameter).ToArray();
                if (declarations.Length > 1)
                    throw new InvalidOperationException("AAP 输入参数重名: " + axis.Parameter);
                if (declarations.Any(parameter => parameter.type == AnimatorControllerParameterType.Float &&
                    !AnimatorOptimizerAnalyzer.IsFinite(parameter.defaultFloat)))
                    throw new InvalidOperationException("AAP 输入参数的默认值必须是有限数: " + axis.Parameter);
                if (AnimatorOptimizerAnalyzer.TryGetVrcGlobalParameterType(axis.Parameter, program.UseVrChatParameterSemantics, out var globalType) &&
                    globalType == axis.SourceType && (declarations.Length == 0 || declarations[0].type == globalType))
                    continue;
                RequireFloatParameter(controller, axis.Parameter);
                if (axis.AssumesBinaryFloat && declarations[0].defaultFloat != 0f && declarations[0].defaultFloat != 1f)
                    throw new InvalidOperationException("AAP 的 0/1 值域约定与当前参数默认值不符: " + axis.Parameter);
            }

            // All declarations, defaults and aliasing are checked before adding assets.
            name = string.IsNullOrWhiteSpace(name) ? "AnimatorOptimizer_AAP_Symbolic" : name;
            var motions = new Motion[program.Nodes.Count];
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                var node = program.Nodes[index];
                if (node.IsLeaf)
                {
                    var clip = CreateAapValueClip(controller, stateRegisterParameter, node.Value, name + "_Value_" + node.Value);
                    if (program.RequiresInitialization)
                    {
                        AnimationUtility.SetEditorCurve(clip,
                            EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), initializedParameter),
                            AnimationCurve.Constant(0f, 1f / 60f, 1f));
                        EditorUtility.SetDirty(clip);
                    }
                    motions[index] = clip;
                    continue;
                }
                var axis = program.Axes[node.AxisIndex];
                var retained = AnimatorOptimizerSemanticUtility.RetainRunEndpoints(node.Children
                    .Select(child => child.ToString(CultureInfo.InvariantCulture)).ToArray());
                var tree = new BlendTree
                {
                    name = name + "_Decision_" + index,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = axis.Parameter,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                    children = retained.Select(sample => new ChildMotion
                    {
                        motion = motions[node.Children[sample]], threshold = axis.Values[sample], timeScale = 1f,
                    }).ToArray(),
                };
                AddSubAsset(controller, tree);
                motions[index] = tree;
            }
            var update = new BlendTree
            {
                name = name + "_Update",
                blendType = BlendTreeType.Simple1D,
                blendParameter = stateRegisterParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
                children = program.StateRoots.Select((root, index) => new ChildMotion
                {
                    motion = motions[root], threshold = index, timeScale = 1f,
                }).ToArray(),
            };
            AddSubAsset(controller, update);
            if (!program.RequiresInitialization) return update;
            var initialized = new BlendTree
            {
                name = name + "_Initialize",
                blendType = BlendTreeType.Simple1D,
                blendParameter = initializedParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
                children = new[]
                {
                    new ChildMotion { motion = motions[program.EntryRoot], threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = update, threshold = 1f, timeScale = 1f },
                },
            };
            AddSubAsset(controller, initialized);
            return initialized;
        }

        public static BlendTree BuildStateOutputTree(
            AnimatorController controller,
            string stateRegisterParameter,
            IReadOnlyList<Motion> stateMotions,
            string name = null)
        {
            RequireController(controller);
            RequireFloatParameter(controller, stateRegisterParameter);
            if (stateMotions == null || stateMotions.Count == 0 || stateMotions.Count > 4096 ||
                stateMotions.Any(motion => motion == null || !AnimatorOptimizerMotionUtility.IsAcyclic(motion)))
                throw new ArgumentException("State Output Tree 需要 1 到 4096 个有效、无循环的 Motion。", nameof(stateMotions));
            var defaultState = controller.parameters.First(parameter => parameter.name == stateRegisterParameter).defaultFloat;
            if (defaultState < 0f || defaultState >= stateMotions.Count || defaultState != Mathf.Floor(defaultState))
                throw new ArgumentException("State Output 寄存器默认值必须是有效的整数状态编号。", nameof(stateRegisterParameter));

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
            var cachedValues = new Dictionary<int, AnimationClip>();
            var clips = outputValues.Select(value =>
            {
                // Preserve signed-zero identity for AAP arithmetic while sharing
                // truly identical setters within this single generated operation.
                var bits = BitConverter.SingleToInt32Bits(value);
                if (!cachedValues.TryGetValue(bits, out var clip))
                {
                    clip = CreateAapValueClip(controller, outputParameter, value,
                        "AnimatorOptimizer_AAP_Remap_" + clipCounter++);
                    cachedValues.Add(bits, clip);
                }
                return clip;
            }).ToArray();
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
            RequireFloatParameter(controller, leftParameter);
            RequireFloatParameter(controller, rightParameter);
            ValidateRange(leftRange);
            ValidateRange(rightRange);
            RequireFiniteValues(new[] { leftRange.Min + rightRange.Min, leftRange.Max + rightRange.Max });
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
            RequireFloatParameter(controller, leftParameter);
            RequireFloatParameter(controller, rightParameter);
            ValidateRange(leftRange);
            ValidateRange(rightRange);
            RequireFiniteValues(new[] { leftRange.Min - rightRange.Max, leftRange.Max - rightRange.Min });
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
            ValidateRange(leftRange);
            ValidateRange(rightRange);
            var products = new[] { leftRange.Min * rightRange.Min, leftRange.Min * rightRange.Max,
                leftRange.Max * rightRange.Min, leftRange.Max * rightRange.Max };
            RequireFiniteValues(products);
            if (float.IsInfinity(products.Max() - products.Min()))
                throw new ArgumentException("AAP 乘法的输出跨度溢出，不能安全插值。");
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
        /// approximation. A / B can be represented by passing B-1 when B >= 1.
        /// The caller must keep the weight parameter at one and the denominator finite
        /// and non-negative at runtime; parameter defaults alone cannot prove that.
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
            RequireFloatParameter(controller, denominatorParameter);
            ValidateRange(numeratorRange);
            if (controller.parameters.First(parameter => parameter.name == denominatorParameter).defaultFloat < 0f)
                throw new ArgumentException("AAP 归一化除法要求分母输入的默认值和运行时值均非负。", nameof(denominatorParameter));
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
            IDictionary<int, AnimationClip> leafClips,
            IDictionary<(string Parameter, Motion False, Motion True), BlendTree> nodes,
            int depth,
            int mask,
            string name)
        {
            if (depth >= inputs.Count)
            {
                var value = outputValues[mask];
                var bits = BitConverter.SingleToInt32Bits(value);
                if (!leafClips.TryGetValue(bits, out var clip))
                {
                    clip = CreateAapValueClip(controller, outputParameter, value,
                        name + "_Value_" + value.ToString("R", CultureInfo.InvariantCulture));
                    leafClips.Add(bits, clip);
                }
                return clip;
            }

            var falseMotion = BuildBooleanNode(controller, outputParameter, inputs, outputValues,
                leafClips, nodes, depth + 1, mask, name);
            var trueMotion = BuildBooleanNode(controller, outputParameter, inputs, outputValues,
                leafClips, nodes, depth + 1, mask | (1 << depth), name);
            var key = (inputs[depth], falseMotion, trueMotion);
            if (nodes.TryGetValue(key, out var cached)) return cached;
            // Share identical subtrees, but retain a BlendTree root for the public
            // API and never merge feedback layers or change their evaluation order.
            var tree = new BlendTree
            {
                name = name + "_Node_" + depth + "_" + mask,
                blendType = BlendTreeType.Simple1D,
                blendParameter = inputs[depth],
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
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
            nodes.Add(key, tree);
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
            if (inputs.Count == 0 || inputs.Count > 12)
                throw new ArgumentException("AAP 真值表需要 1 到 12 个输入参数（最多 4096 个组合）。", nameof(inputParameters));
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
            if (!AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(thresholds))
                throw new ArgumentException("AAP 1D 阈值必须严格递增、跨度有限，且最多 4096 个。", nameof(thresholds));
            RequireFiniteValues(values);
            if (float.IsInfinity(values.Max() - values.Min()))
                throw new ArgumentException("AAP 1D 输出跨度溢出，不能安全插值。", nameof(values));
        }

        private static void ValidateRange(AnimatorOptimizerAapRange range)
        {
            // default(AnimatorOptimizerAapRange) bypasses the struct constructor.
            if (!AnimatorOptimizerSemanticUtility.IsFiniteOrderedAxis(new[] { range.Min, range.Max }))
                throw new ArgumentException("AAP 输入范围无效或跨度溢出。", nameof(range));
        }

        private static void RequireFiniteValues(IEnumerable<float> values)
        {
            if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
                throw new ArgumentException("AAP 运算在声明范围内会产生非有限结果；生成资源前已拒绝。");
        }

        private static void RequireController(AnimatorController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            var path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AAP 生成器需要 Assets/ 下已保存的 AnimatorController；请先复制 Package 资源。");
        }

        private static void RequireFloatParameter(AnimatorController controller, string name)
        {
            RequireController(controller);
            var parameters = (controller.parameters ?? Array.Empty<AnimatorControllerParameter>())
                .Where(candidate => candidate != null && string.Equals(candidate.name, name, StringComparison.Ordinal)).Take(2).ToList();
            if (string.IsNullOrWhiteSpace(name) || parameters.Count != 1 || parameters[0].type != AnimatorControllerParameterType.Float)
                throw new InvalidOperationException($"AAP BlendTree 参数 '{name}' 必须是本地 Animator Float 参数。可先执行 Bool/Int → Float 映射。");
            var parameter = parameters[0];
            if (float.IsNaN(parameter.defaultFloat) || float.IsInfinity(parameter.defaultFloat))
                throw new InvalidOperationException($"AAP 参数 '{name}' 的默认值必须是有限数。");
        }

        private static void RequireConstantOneWeight(AnimatorController controller, string name)
        {
            RequireFloatParameter(controller, name);
            var parameter = controller.parameters.First(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            // This validates the initial value only. The caller must ensure the
            // weight remains one at runtime; static code cannot prove external writes.
            if (parameter.defaultFloat != 1f)
                throw new InvalidOperationException($"AAP Direct 权重参数 '{name}' 的默认值必须为 1。");
        }

        private static void RequireDefaultZero(AnimatorController controller, string name)
        {
            RequireFloatParameter(controller, name);
            var parameter = controller.parameters.First(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (parameter.defaultFloat != 0f)
                throw new InvalidOperationException($"AAP 输出参数 '{name}' 的默认值必须为 0，才能进行未归一化 Direct 运算。");
        }

        private static void SetNormalizedBlendValues(BlendTree tree, bool normalized)
        {
            using var serializedObject = new SerializedObject(tree);
            var property = serializedObject.FindProperty(NormalizedBlendValuesProperty);
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
