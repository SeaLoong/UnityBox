#if NDMF_AVAILABLE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    internal static partial class AnimatorOptimizerNdmfProcessor
    {
        private static void MergeSynchronizedVirtualLayers(VirtualAnimatorController controller,
            List<VirtualLayer> layers, ISet<int> protectedIndices, AnimatorOptimizerOptions options)
        {
            var machineOwners = layers.Where(layer => layer?.StateMachine != null)
                .GroupBy(layer => layer.StateMachine).ToDictionary(group => group.Key, group => group.Count());
            var stateOwners = layers.Where(layer => layer?.StateMachine != null)
                .SelectMany(layer => layer.StateMachine.AllStates()).Where(state => state != null)
                .GroupBy(state => state).ToDictionary(group => group.Key, group => group.Count());
            var descriptions = layers.Select((layer, index) => index == 0 || layer?.StateMachine == null ||
                protectedIndices.Contains(layer.VirtualLayerIndex) || machineOwners[layer.StateMachine] != 1 ||
                layer.StateMachine.States.Any(child => child.State == null || stateOwners[child.State] != 1)
                ? null : DescribeSynchronizedVirtualLayer(controller, layer)).ToList();
            var groups = AnimatorOptimizerSynchronizedGraph.FindGroups(descriptions);
            var removed = new HashSet<VirtualLayer>();
            foreach (var group in groups)
            {
                var machine = layers[group[0]].StateMachine;
                var replacements = new List<VirtualClip>();
                for (var stateIndex = 0; stateIndex < machine.States.Count; stateIndex++)
                {
                    var source = (VirtualClip)machine.States[stateIndex].State.Motion;
                    var clip = VirtualClip.Create(GeneratedAssetPrefix + "Synchronized_" + group[0] + "_" + stateIndex);
                    clip.FrameRate = source.FrameRate;
                    clip.WrapMode = source.WrapMode;
                    clip.UseHighQualityCurves = source.UseHighQualityCurves;
                    clip.LocalBounds = source.LocalBounds;
                    clip.Settings = source.Settings;
                    foreach (var index in group)
                    {
                        var data = descriptions[index].Clips[stateIndex];
                        foreach (var pair in data.Floats) clip.SetFloatCurve(pair.Key, pair.Value);
                        foreach (var pair in data.Objects) clip.SetObjectCurve(pair.Key, pair.Value);
                    }
                    replacements.Add(clip);
                }
                for (var stateIndex = 0; stateIndex < machine.States.Count; stateIndex++)
                    machine.States[stateIndex].State.Motion = replacements[stateIndex];
                foreach (var index in group.Skip(1)) removed.Add(layers[index]);
                if (options.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] Coalesced synchronized layers [{string.Join(", ", group)}]; retained original Entry/Exit, equality-point history and WD. No AAP writes were moved.");
            }
            layers.RemoveAll(removed.Contains);
            // Keep VirtualLayerIndex identities; NDMF resolves physical indices on commit.
        }

        private static AnimatorOptimizerSynchronizedGraph.Description DescribeSynchronizedVirtualLayer(
            VirtualAnimatorController controller, VirtualLayer layer)
        {
            var machine = layer.StateMachine;
            if (layer.BlendingMode != AnimatorLayerBlendingMode.Override || layer.AvatarMask != null ||
                layer.OriginalPhysicalLayerIndex != 0 && layer.DefaultWeight != 1f || layer.IKPass ||
                layer.SyncedLayerIndex >= 0 || machine.StateMachines.Count != 0 ||
                machine.States.Count < 2 || machine.States.Count > 256 ||
                machine.Behaviours.Any(behaviour => behaviour == null || behaviour.GetType().FullName !=
                    "nadena.dev.modular_avatar.core.ModularAvatarMMDLayerControl")) return null;
            var states = machine.States.Select(child => child.State).ToList();
            if (states.Any(state => state == null) || states.Distinct().Count() != states.Count ||
                !states.Contains(machine.DefaultState) ||
                states.Any(state => state.WriteDefaultValues != states[0].WriteDefaultValues)) return null;
            if (!AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(machine.Behaviours, out var markerKey)) return null;
            var result = new AnimatorOptimizerSynchronizedGraph.Description();
            var key = new StringBuilder().Append(states.Count).Append('/').Append(states.IndexOf(machine.DefaultState)).Append(markerKey);
            var edgeCount = 0;
            bool AddEdges(IEnumerable<VirtualTransitionBase> edges)
            {
                key.Append('[');
                foreach (var edge in edges)
                {
                    if (edge == null || ++edgeCount > 8192 || edge.DestinationStateMachine != null) return false;
                    var destination = edge.IsExit ? -1 : states.IndexOf(edge.DestinationState);
                    if (!edge.IsExit && destination < 0 || edge.IsExit && edge.DestinationState != null) return false;
                    var conditions = AnimatorOptimizerSynchronizedGraph.ConditionsKey(edge.Conditions, name =>
                        controller.Parameters.TryGetValue(name, out var parameter) ? parameter.type : (AnimatorControllerParameterType?)null);
                    if (conditions == null) return false;
                    key.Append(destination).Append(':').Append(edge.Mute).Append(':').Append(edge.Solo)
                        .Append(AnimatorOptimizerSynchronizedGraph.Atom(conditions));
                    if (edge is VirtualStateTransition transition)
                    {
                        if (transition.ExitTime.HasValue || transition.Duration != 0f || transition.Offset != 0f) return false;
                        key.Append(':').Append(transition.HasFixedDuration).Append(':').Append(transition.CanTransitionToSelf)
                            .Append(':').Append((int)transition.InterruptionSource).Append(':').Append(transition.OrderedInterruption);
                    }
                    key.Append(';');
                }
                key.Append(']');
                return true;
            }
            if (!AddEdges(machine.EntryTransitions) || !AddEdges(machine.AnyStateTransitions)) return null;
            foreach (var state in states)
            {
                if (state.Behaviours.Count > 0 || state.IKOnFeet || !IsFinite(state.Speed) || !IsFinite(state.CycleOffset) ||
                    state.Motion is not VirtualClip clip ||
                    !IsStaticVirtualClip(clip)) return null;
                var data = AnimatorOptimizerSynchronizedGraph.ReadClip(clip.GetFloatCurveBindings(), clip.GetFloatCurve,
                    clip.GetObjectCurveBindings(), clip.GetObjectCurve);
                if (data == null) return null;
                result.Clips.Add(data);
                result.Bindings.UnionWith(data.BindingKeys);
                key.Append(state.WriteDefaultValues).Append(AnimatorOptimizerSynchronizedGraph.Atom(state.Tag));
                key.Append(data.Floats.Count == 0 && data.Objects.Count == 0 ? 'E' : 'C');
                if (!AddEdges(state.Transitions)) return null;
            }
            result.GraphKey = key.ToString();
            return result;
        }

        private static IEnumerable<VirtualCandidateGroup> SplitVirtualCandidateGroup(VirtualCandidateGroup source,
            IReadOnlyList<VirtualLayer> layers, IReadOnlyDictionary<VirtualLayer, HashSet<BindingKey>> bindingsByLayer,
            ISet<VirtualLayer> completeBindings, AnimatorOptimizerOptions options)
        {
            VirtualCandidateGroup Create(List<VirtualCandidate> candidates) => new VirtualCandidateGroup
            {
                BlendingMode = source.BlendingMode, MaskKey = source.MaskKey, Mask = source.Mask,
                StateTag = source.StateTag, Candidates = candidates, OriginalCandidates = candidates.ToList(),
            };
            bool Safe(VirtualCandidateGroup trial)
            {
                if (HasVirtualParameterOrderConflict(trial, layers) ||
                    !CanPreserveVirtualLayerBehaviours(trial.Candidates) || !CanMergeVirtualMotionSet(trial.Candidates)) return false;
                if (!options.StrictCurveConflicts) return true;
                if (HasVirtualBindingOverlap(trial.Candidates) && !CanResolveVirtualPriority(trial.Candidates, options)) return false;
                var members = trial.Candidates.Select(candidate => candidate.Layer).ToHashSet();
                var bindings = trial.Candidates.SelectMany(candidate => candidate.Bindings).ToHashSet();
                for (var index = trial.FirstIndex + 1; index < trial.Candidates.Last().Index; index++)
                    if (!members.Contains(layers[index]) && (!completeBindings.Contains(layers[index]) ||
                        bindings.Overlaps(bindingsByLayer[layers[index]]))) return false;
                return true;
            }
            var current = new List<VirtualCandidate>();
            foreach (var candidate in source.OriginalCandidates)
            {
                var trial = Create(current.Concat(new[] { candidate }).ToList());
                if (current.Count > 0 && !Safe(trial))
                {
                    if (current.Count > 1) yield return Create(current);
                    current = new List<VirtualCandidate>();
                }
                current.Add(candidate);
            }
            if (current.Count > 1) yield return Create(current);
        }

        private static bool HasVirtualParameterOrderConflict(VirtualCandidateGroup group, IReadOnlyList<VirtualLayer> layers)
        {
            foreach (var candidate in group.OriginalCandidates)
            {
                var plan = candidate.Plan;
                var reads = (plan.BooleanParameters ?? Enumerable.Empty<string>())
                    .Concat(plan.DecisionDimensions?.Select(dimension => dimension.Parameter) ?? Enumerable.Empty<string>())
                    .Concat(plan.ReferencedParameters).Append(plan.SelectorParameter).Where(name => name != null).ToHashSet();
                for (var index = group.FirstIndex + 1; index < candidate.Index; index++)
                {
                    var writes = new HashSet<string>(StringComparer.Ordinal);
                    CollectVirtualAnimatedAnimatorParameterNames(layers, index, writes, new HashSet<VirtualMotion>());
                    if (reads.Overlaps(writes)) return true;
                }
            }
            return false;
        }
    }
}
#endif