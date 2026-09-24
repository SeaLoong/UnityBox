#if NDMF_AVAILABLE
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    internal static partial class AnimatorOptimizerNdmfProcessor
    {
        private static bool TryBuildFiniteVirtualGraphPlan(VirtualAnimatorController controller, VirtualLayer layer,
            AnimatorOptimizerOptions options, bool allowHeuristics, out VirtualPlan plan)
        {
            plan = null;
            if (layer?.StateMachine == null || IsGeneratedLayerName(layer.Name, options.GeneratedLayerName) ||
                (layer.Name ?? string.Empty).StartsWith(GeneratedAssetPrefix, StringComparison.Ordinal) ||
                layer.StateMachine.StateMachines.Count != 0 || layer.IKPass || layer.SyncedLayerIndex >= 0 ||
                layer.BlendingMode != AnimatorLayerBlendingMode.Override && layer.BlendingMode != AnimatorLayerBlendingMode.Additive ||
                controller.Layers.FirstOrDefault() != layer && layer.OriginalPhysicalLayerIndex != 0 && layer.DefaultWeight != 1f ||
                !AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(layer.StateMachine.Behaviours, out _)) return false;
            var machine = layer.StateMachine;
            if (machine.States.Any(child => child.State == null) ||
                machine.States.GroupBy(child => child.State).Any(group => group.Count() > 1)) return false;
            var states = GetPotentiallyReachableVirtualStates(machine);
            if (states.Count == 0 || states.Count > 256 || machine.DefaultState == null) return false;
            if (states.Distinct().Count() != states.Count) return false;
            var indices = states.Select((state, index) => (state, index)).ToDictionary(item => item.state, item => item.index);
            if (!indices.TryGetValue(machine.DefaultState, out var defaultIndex)) return false;
            var effective = options.Clone();
            if (!allowHeuristics) effective.Heuristics = AnimatorOptimizerComponent.HeuristicFlags.None;
            var outputs = new List<VirtualMotionRef>();
            var assumptions = new List<string>();
            var sampledFrames = 0;
            long sampledValues = 0;
            foreach (var state in states)
            {
                if (state.Behaviours.Count != 0 || state.IKOnFeet || !IsFinite(state.Speed) || !IsFinite(state.CycleOffset) ||
                    !IsWriteDefaultsAcceptable(state)) return false;
                var output = ToMotionRef(state);
                if (!IsTimeIndependentMotion(state.Motion))
                {
                    if (output.Motion is not VirtualClip clip || !IsOrdinaryVirtualClip(clip) ||
                        clip.WrapMode == WrapMode.Loop || clip.WrapMode == WrapMode.PingPong ||
                        clip.AdditiveReferencePoseClip != null) return false;
                    if (state.TimeParameter != null &&
                        (!controller.Parameters.TryGetValue(state.TimeParameter, out var parameter) || parameter == null ||
                         parameter.type != AnimatorControllerParameterType.Float || !IsFinite(parameter.defaultFloat))) return false;
                    if (!AnimatorOptimizerClipBake.TryCreate(clip.Name,
                        GetVirtualObservedLength(clip) ?? 0f, clip.FrameRate, clip.LocalBounds, clip.UseHighQualityCurves, clip.Settings,
                        clip.GetFloatCurveBindings().Select(binding => (binding, clip.GetFloatCurve(binding))).ToArray(),
                        clip.GetObjectCurveBindings().Select(binding => (binding, clip.GetObjectCurve(binding))).ToArray(),
                        state.Speed, state.CycleOffset, state.SpeedParameter != null, state.CycleOffsetParameter != null,
                        state.TimeParameter, effective, out var bake, out var assumption)) return false;
                    sampledFrames += bake.Frames.Length;
                    sampledValues += (long)bake.Frames.Length * (bake.FloatBindings.Length + bake.ObjectBindings.Length);
                    if (sampledFrames > 1024 || sampledValues > 262144) return false;
                    output = new VirtualMotionRef { Motion = clip, Bake = bake };
                    assumptions.Add(assumption);
                }
                else if (!ValidateState(state, allowTransitions: true) || output.Mirror) return false;
                outputs.Add(output);
            }

            List<AnimatorOptimizerFiniteGraph.Edge> ConvertEdges(IEnumerable<VirtualTransitionBase> source, bool entry)
            {
                var result = new List<AnimatorOptimizerFiniteGraph.Edge>();
                foreach (var transition in source)
                {
                    if (transition.DestinationStateMachine != null || entry && transition.IsExit ||
                        transition.IsExit && transition.DestinationState != null ||
                        !transition.IsExit && (transition.DestinationState == null || !indices.ContainsKey(transition.DestinationState))) return null;
                    if (transition is VirtualStateTransition timed)
                    {
                        var irrelevantOffset = currentLayerScope != null && !currentLayerScope.HasExitTime &&
                            timed.DestinationState != null && IsTimeIndependentMotion(timed.DestinationState.Motion);
                        var needsRelaxation = timed.ExitTime.HasValue || timed.Duration != 0f || timed.Offset != 0f && !irrelevantOffset;
                        if (!IsFinite(timed.Duration) || !IsFinite(timed.Offset) || timed.ExitTime.HasValue && !IsFinite(timed.ExitTime.Value) ||
                            needsRelaxation && !effective.IgnoreTransitionTimingForStaticMotions) return null;
                        if (needsRelaxation)
                            assumptions.Add("按已启用的选项忽略过渡时间；采样/静态输出间切换改为瞬时。");
                    }
                    result.Add(new AnimatorOptimizerFiniteGraph.Edge
                    {
                        Destination = transition.IsExit ? -1 : indices[transition.DestinationState],
                        CanTransitionToSelf = transition is not VirtualStateTransition stateTransition || stateTransition.CanTransitionToSelf,
                        Conditions = transition.Conditions.ToArray(),
                    });
                }
                return result;
            }
            var graph = new AnimatorOptimizerFiniteGraph
            {
                DefaultState = defaultIndex,
                Entries = ConvertEdges(GetEffectiveVirtualTransitions(machine.EntryTransitions), true),
                AnyState = ConvertEdges(GetEffectiveVirtualTransitions(machine.AnyStateTransitions), false),
                States = states.Select(state => ConvertEdges(GetEffectiveVirtualTransitions(state.Transitions), false)).ToList(),
            };
            if (graph.Entries == null || graph.AnyState == null || graph.States.Any(edges => edges == null)) return false;
            var keys = outputs.Select(output => output.Bake?.Key ??
                (!output.Mirror && TryBuildVirtualStaticObservableMotionSignature(output.Motion, out var signature)
                    ? signature : BuildVirtualMotionRefEquivalenceKey(output))).ToArray();
            if (!graph.TryCompile(keys, controller.Parameters, effective, out var compiled)) return false;
            var dimensions = compiled.Axes.Select(axis => new VirtualDecisionDimension
            {
                Parameter = axis.Parameter, Type = axis.Type, Values = axis.Values, IsApproximate = axis.IsApproximate,
            }).ToList();
            if (dimensions.Any(axis => controller.Parameters.TryGetValue(axis.Parameter, out var parameter) &&
                !IsVrcGlobalParameter(axis.Parameter) &&
                (parameter.type == AnimatorControllerParameterType.Bool && !options.UpgradeBoolParametersToFloat ||
                 parameter.type == AnimatorControllerParameterType.Int && !options.UpgradeIntParametersToFloat))) return false;
            plan = BuildVirtualDecisionPlanFromTable(dimensions,
                compiled.Outputs.ToDictionary(pair => pair.Key, pair => outputs[pair.Value]));
            plan.Assumptions = assumptions.Concat(compiled.Assumptions).Distinct(StringComparer.Ordinal).ToList();
            plan.AssumedBinaryParameters = compiled.BinaryParameters;
            return true;
        }

        private static bool IsTimeIndependentVirtualRef(VirtualMotionRef motion) =>
            motion?.Bake != null || IsTimeIndependentMotion(motion?.Motion);

        private static int VirtualPlanEntryCount(VirtualPlan plan) =>
            plan?.TruthTable?.Count ?? plan?.DecisionTable?.Count ?? plan?.SelectorSamples?.Count ?? 1;

        private static bool AreSampledVirtualMotionsCompatible(IEnumerable<VirtualMotionRef> motions)
        {
            bool? precision = null;
            foreach (var motion in motions)
            {
                if (motion == null || motion.Mirror) return false;
                bool current;
                if (motion.Bake != null)
                {
                    if (motion.Bake.Parameter != null || motion.Bake.Frames.Length != 1) return false;
                    current = motion.Bake.HighQuality;
                }
                else
                {
                    if (motion.Motion == null) continue;
                    if (motion.Motion is not VirtualClip clip || !IsStaticVirtualClip(clip) ||
                        !IsPropertyOnlyVirtualMotion(clip)) return false;
                    current = clip.UseHighQualityCurves;
                }
                if (precision.HasValue && precision.Value != current) return false;
                precision = current;
            }
            return true;
        }

        private static void MaterializeVirtualBakedMotions(IEnumerable<VirtualCandidate> candidates)
        {
            var cache = new Dictionary<string, VirtualMotion>(StringComparer.Ordinal);
            foreach (var reference in candidates.SelectMany(candidate => EnumerateVirtualPlanMotions(candidate.Plan)).Distinct())
            {
                var bake = reference?.Bake;
                if (bake == null) continue;
                if (!cache.TryGetValue(bake.Key, out var motion))
                {
                    var frames = new List<VirtualMotion>();
                    foreach (var frame in bake.Frames)
                    {
                        var clip = VirtualClip.Create(GeneratedAssetPrefix + "Sample_" + bake.Name + "_" + frames.Count);
                        clip.FrameRate = bake.FrameRate;
                        clip.LocalBounds = bake.Bounds;
                        clip.UseHighQualityCurves = bake.HighQuality;
                        clip.WrapMode = WrapMode.ClampForever;
                        for (var index = 0; index < bake.FloatBindings.Length; index++)
                            clip.SetFloatCurve(bake.FloatBindings[index], AnimationCurve.Constant(0f, 1f / 60f, frame.Floats[index]));
                        for (var index = 0; index < bake.ObjectBindings.Length; index++)
                            clip.SetObjectCurve(bake.ObjectBindings[index],
                                new[] { new ObjectReferenceKeyframe { time = 0f, value = frame.Objects[index] } });
                        frames.Add(clip);
                    }
                    motion = frames[0];
                    if (bake.Parameter != null)
                    {
                        var tree = VirtualBlendTree.Create(GeneratedAssetPrefix + "MotionTime_" + bake.Name);
                        tree.BlendType = BlendTreeType.Simple1D;
                        tree.BlendParameter = bake.Parameter;
                        tree.UseAutomaticThresholds = false;
                        tree.Children = frames.Select((frame, index) => new VirtualBlendTree.VirtualChildMotion
                        {
                            Motion = frame, Threshold = bake.Frames[index].Threshold, TimeScale = 1f,
                        }).ToImmutableList();
                        motion = tree;
                    }
                    cache.Add(bake.Key, motion);
                }
                reference.Motion = motion;
                // Retain sampling provenance so post-generation checks use the same
                // property-only contract and precision as the analysis stage.
                reference.Speed = 1f;
                reference.CycleOffset = 0f;
                reference.Mirror = false;
            }
        }
    }
}
#endif