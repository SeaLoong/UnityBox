using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    // Shared by physical and NDMF adapters. This is graph coalescing, NOT a proof
    // that the graph is memoryless. In particular it preserves MA's equality-point
    // hysteresis, empty overlay states and the original Write Defaults setting.
    public static class AnimatorOptimizerSynchronizedGraph
    {
        public sealed class ClipData
        {
            public readonly Dictionary<EditorCurveBinding, AnimationCurve> Floats = new();
            public readonly Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]> Objects = new();
            public IEnumerable<string> BindingKeys => Floats.Keys.Concat(Objects.Keys).Select(PropertyKey);
        }

        public sealed class Description
        {
            public string GraphKey;
            public readonly List<ClipData> Clips = new();
            public readonly HashSet<string> Bindings = new(StringComparer.Ordinal);
        }

        public static string Atom(string value)
        {
            value ??= string.Empty;
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }

        // Conflict detection uses the target property; equivalence additionally
        // includes Unity's discrete/managed-reference evaluation flags.
        public static string PropertyKey(EditorCurveBinding binding) =>
            Atom(binding.path) + Atom(binding.type?.AssemblyQualifiedName) + Atom(binding.propertyName);

        public static string BindingKey(EditorCurveBinding binding) => PropertyKey(binding) +
            (binding.isPPtrCurve ? "O" : "F") + (binding.isDiscreteCurve ? "D" : "C") +
            (binding.isSerializeReferenceCurve ? "R" : "V");

        public static bool TryBuildMarkerKey(IEnumerable<StateMachineBehaviour> behaviours, out string key)
        {
            key = null;
            var builder = new StringBuilder("|markers[");
            try
            {
                foreach (var behaviour in behaviours)
                {
                    if (!AnimatorOptimizerAnalyzer.IsPreservableLayerBehaviour(behaviour)) return false;
                    builder.Append(Atom(EditorJsonUtility.ToJson(behaviour)));
                }
            }
            catch (Exception)
            {
                // An unreadable marker is not evidence of equal metadata.
                return false;
            }
            key = builder.Append(']').ToString();
            return true;
        }

        public static ClipData ReadClip(IEnumerable<EditorCurveBinding> floatBindings,
            Func<EditorCurveBinding, AnimationCurve> readFloat,
            IEnumerable<EditorCurveBinding> objectBindings,
            Func<EditorCurveBinding, ObjectReferenceKeyframe[]> readObject)
        {
            var data = new ClipData();
            foreach (var binding in floatBindings)
            {
                // Do not move AAP writes or NaNimated/bone outputs into a new
                // evaluation scope. RC v2's parameter/delay network stays intact.
                if (binding.type == null || binding.type == typeof(Animator) || binding.type == typeof(Transform))
                    return null;
                var curve = readFloat(binding);
                if (curve == null || curve.length == 0 || !AnimatorOptimizerSemanticUtility.IsConstantCurve(curve))
                    return null;
                data.Floats.Add(binding, new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode,
                });
            }
            foreach (var binding in objectBindings)
            {
                if (binding.type == null || binding.type == typeof(Animator) || binding.type == typeof(Transform))
                    return null;
                var curve = readObject(binding);
                if (curve == null || curve.Length == 0 || !AnimatorOptimizerSemanticUtility.IsConstantObjectCurve(curve))
                    return null;
                data.Objects.Add(binding, curve.ToArray());
            }
            return data;
        }

        public static string ConditionsKey(IEnumerable<AnimatorCondition> conditions,
            Func<string, AnimatorControllerParameterType?> resolveType)
        {
            var parts = new List<string>();
            foreach (var condition in conditions)
            {
                if (parts.Count >= 64 || string.IsNullOrWhiteSpace(condition.parameter)) return null;
                var type = resolveType(condition.parameter);
                if (type == AnimatorControllerParameterType.Bool)
                {
                    if (condition.mode != AnimatorConditionMode.If && condition.mode != AnimatorConditionMode.IfNot)
                        return null;
                }
                else if (type == AnimatorControllerParameterType.Float || type == AnimatorControllerParameterType.Int)
                {
                    if (float.IsNaN(condition.threshold) || float.IsInfinity(condition.threshold) ||
                        condition.mode != AnimatorConditionMode.Greater && condition.mode != AnimatorConditionMode.Less &&
                        condition.mode != AnimatorConditionMode.Equals && condition.mode != AnimatorConditionMode.NotEqual)
                        return null;
                }
                else return null; // Trigger consumption cannot be coalesced.
                parts.Add(Atom(condition.parameter) + ":" + (int)type.Value + ":" + (int)condition.mode + ":" +
                    condition.threshold.ToString("R", CultureInfo.InvariantCulture));
            }
            return string.Concat(parts.Select(Atom));
        }

        // Only adjacent eligible layers are combined. An intervening AAP writer,
        // callback, mask, weight control or different graph remains an order barrier.
        public static List<List<int>> FindGroups(IReadOnlyList<Description> descriptions)
        {
            var groups = new List<List<int>>();
            var current = new List<int>();
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            string key = null;
            for (var index = 0; index <= descriptions.Count; index++)
            {
                var description = index < descriptions.Count ? descriptions[index] : null;
                if (description == null || description.GraphKey != key || bindings.Overlaps(description.Bindings))
                {
                    if (current.Count > 1) groups.Add(current);
                    current = new List<int>();
                    bindings.Clear();
                }
                if (description == null) { key = null; continue; }
                key = description.GraphKey;
                current.Add(index);
                bindings.UnionWith(description.Bindings);
            }
            return groups;
        }
    }

    public static partial class AnimatorOptimizerConverter
    {
        public static IReadOnlyList<IReadOnlyList<int>> GetSynchronizedLayerGroups(AnimatorController controller,
            AnimatorOptimizerOptions options = null)
        {
            options = options?.Clone() ?? new AnimatorOptimizerOptions();
            if (controller == null || !options.MergeSynchronizedStateMachines ||
                !AnimatorOptimizerAnalyzer.IsValidControllerGraph(controller))
                return Array.Empty<IReadOnlyList<int>>();
            var layers = controller.layers;
            if (!options.HasKnownPlayableLayer && EnumerateLayerControlsFromLayers(layers).Any())
                return Array.Empty<IReadOnlyList<int>>();
            if (GetMalformedLayerReferenceIssues(layers, options).Count > 0)
                return Array.Empty<IReadOnlyList<int>>();
            var protectedIndices = GetProtectedLayerIndices(layers, options);
            var descriptions = DescribeSynchronizedLayers(controller, layers, protectedIndices);
            return AnimatorOptimizerSynchronizedGraph.FindGroups(descriptions)
                .Select(group => (IReadOnlyList<int>)group.AsReadOnly()).ToList().AsReadOnly();
        }

        private static List<AnimatorOptimizerSynchronizedGraph.Description> DescribeSynchronizedLayers(
            AnimatorController controller, IReadOnlyList<AnimatorControllerLayer> layers, ISet<int> protectedIndices)
        {
            var machines = layers.Where(layer => layer?.stateMachine != null)
                .GroupBy(layer => layer.stateMachine).ToDictionary(group => group.Key, group => group.Count());
            var stateOwners = layers.SelectMany(layer => EnumerateSynchronizedStates(layer?.stateMachine, new HashSet<AnimatorStateMachine>()))
                .Where(state => state != null).GroupBy(state => state)
                .ToDictionary(group => group.Key, group => group.Count());
            var result = new List<AnimatorOptimizerSynchronizedGraph.Description>();
            for (var index = 0; index < layers.Count; index++)
            {
                var layer = layers[index];
                result.Add(index == 0 || protectedIndices.Contains(index) || layer?.stateMachine == null ||
                    machines[layer.stateMachine] != 1 || layer.stateMachine.states.Any(child =>
                        child.state == null || stateOwners[child.state] != 1)
                    ? null : DescribeSynchronizedLayer(controller, layer));
            }
            return result;
        }

        private static IEnumerable<AnimatorState> EnumerateSynchronizedStates(AnimatorStateMachine machine,
            ISet<AnimatorStateMachine> visited)
        {
            if (machine == null || !visited.Add(machine)) yield break;
            foreach (var child in machine.states) yield return child.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in EnumerateSynchronizedStates(child.stateMachine, visited)) yield return state;
        }

        private static AnimatorOptimizerSynchronizedGraph.Description DescribeSynchronizedLayer(
            AnimatorController controller, AnimatorControllerLayer layer)
        {
            var machine = layer.stateMachine;
            if (layer.blendingMode != AnimatorLayerBlendingMode.Override || layer.avatarMask != null ||
                layer.defaultWeight != 1f || layer.iKPass || layer.syncedLayerIndex >= 0 ||
                machine.stateMachines.Length != 0 || machine.states.Length < 2 || machine.states.Length > 256 ||
                machine.behaviours.Any(behaviour => !IsSynchronizedLayerMarker(behaviour))) return null;
            var states = machine.states.Select(child => child.state).ToList();
            if (states.Any(state => state == null) || states.Distinct().Count() != states.Count ||
                !states.Contains(machine.defaultState) ||
                states.Any(state => state.writeDefaultValues != states[0].writeDefaultValues)) return null;

            if (!AnimatorOptimizerSynchronizedGraph.TryBuildMarkerKey(machine.behaviours, out var markerKey)) return null;
            var result = new AnimatorOptimizerSynchronizedGraph.Description();
            var key = new StringBuilder().Append(states.Count).Append('/').Append(states.IndexOf(machine.defaultState)).Append(markerKey);
            var edgeCount = 0;
            bool AddEdges(IEnumerable<AnimatorTransitionBase> edges)
            {
                key.Append('[');
                foreach (var edge in edges)
                {
                    if (edge == null || ++edgeCount > 8192 || edge.destinationStateMachine != null) return false;
                    var destination = edge.isExit ? -1 : states.IndexOf(edge.destinationState);
                    if (!edge.isExit && destination < 0 || edge.isExit && edge.destinationState != null) return false;
                    var conditions = AnimatorOptimizerSynchronizedGraph.ConditionsKey(edge.conditions, name =>
                    {
                        var parameter = controller.parameters.FirstOrDefault(p => p.name == name);
                        return parameter?.type;
                    });
                    if (conditions == null) return false;
                    key.Append(destination).Append(':').Append(edge.mute).Append(':').Append(edge.solo)
                        .Append(AnimatorOptimizerSynchronizedGraph.Atom(conditions));
                    if (edge is AnimatorStateTransition transition)
                    {
                        if (transition.hasExitTime || transition.duration != 0f || transition.offset != 0f) return false;
                        key.Append(':').Append(transition.hasFixedDuration).Append(':').Append(transition.canTransitionToSelf)
                            .Append(':').Append((int)transition.interruptionSource).Append(':').Append(transition.orderedInterruption);
                    }
                    key.Append(';');
                }
                key.Append(']');
                return true;
            }
            if (!AddEdges(machine.entryTransitions) || !AddEdges(machine.anyStateTransitions)) return null;
            foreach (var state in states)
            {
                // No transition observes time, and ReadClip excludes all bone/AAP
                // outputs. Playback parameters/mirroring cannot affect these properties.
                if (state.behaviours.Length > 0 || state.iKOnFeet ||
                    !AnimatorOptimizerAnalyzer.IsFinite(state.speed) || !AnimatorOptimizerAnalyzer.IsFinite(state.cycleOffset) ||
                    state.motion is not AnimationClip clip ||
                    !AnimatorOptimizerMotionUtility.IsStaticClip(clip)) return null;
                var data = AnimatorOptimizerSynchronizedGraph.ReadClip(AnimationUtility.GetCurveBindings(clip),
                    binding => AnimationUtility.GetEditorCurve(clip, binding),
                    AnimationUtility.GetObjectReferenceCurveBindings(clip),
                    binding => AnimationUtility.GetObjectReferenceCurve(clip, binding));
                if (data == null) return null;
                result.Clips.Add(data);
                result.Bindings.UnionWith(data.BindingKeys);
                key.Append(state.writeDefaultValues).Append(AnimatorOptimizerSynchronizedGraph.Atom(state.tag));
                // Preserve empty-vs-nonempty at matching states; null motions are
                // rejected above because Unity treats null and empty differently.
                key.Append(data.Floats.Count == 0 && data.Objects.Count == 0 ? 'E' : 'C');
                if (!AddEdges(state.transitions)) return null;
            }
            result.GraphKey = key.ToString();
            return result;
        }

        private static bool IsSynchronizedLayerMarker(StateMachineBehaviour behaviour) => behaviour != null &&
            behaviour.GetType().FullName == "nadena.dev.modular_avatar.core.ModularAvatarMMDLayerControl";

        private static void MergeSynchronizedLayersInPlace(AnimatorController controller, List<AnimatorControllerLayer> layers, ISet<int> protectedIndices,
            ICollection<string> messages, AnimatorOptimizerOptions options)
        {
            var descriptions = DescribeSynchronizedLayers(controller, layers, protectedIndices);
            var groups = AnimatorOptimizerSynchronizedGraph.FindGroups(descriptions);
            var removed = new HashSet<int>();
            // Build each group's replacement clips before touching its states. The original
            // machine is retained, so all destinations/order/WD/Entry semantics stay.
            foreach (var group in groups)
            {
                var machine = layers[group[0]].stateMachine;
                var replacements = new List<AnimationClip>();
                for (var stateIndex = 0; stateIndex < machine.states.Length; stateIndex++)
                {
                    var clip = UnityEngine.Object.Instantiate((AnimationClip)machine.states[stateIndex].state.motion);
                    clip.name = GeneratedAssetPrefix + "Synchronized_" + group[0] + "_" + stateIndex;
                    foreach (var index in group.Skip(1))
                    {
                        var data = descriptions[index].Clips[stateIndex];
                        foreach (var pair in data.Floats) AnimationUtility.SetEditorCurve(clip, pair.Key, pair.Value);
                        foreach (var pair in data.Objects) AnimationUtility.SetObjectReferenceCurve(clip, pair.Key, pair.Value);
                    }
                    AddSubAsset(controller, clip);
                    replacements.Add(clip);
                }
                for (var stateIndex = 0; stateIndex < machine.states.Length; stateIndex++)
                {
                    var state = machine.states[stateIndex].state;
                    Undo.RecordObject(state, "Animator Optimizer synchronized outputs");
                    state.motion = replacements[stateIndex];
                    EditorUtility.SetDirty(state);
                }
                removed.UnionWith(group.Skip(1));
                messages.Add($"同步状态机合并 [{string.Join(", ", group)}] → 1 层；保留原 Entry/Exit、等值边界历史及 WD，不引入 Float 插值或 AAP 延迟。");
            }
            if (removed.Count == 0) return;
            Undo.RecordObject(controller, "Animator Optimizer synchronized layers");
            foreach (var index in removed.OrderByDescending(index => index)) layers.RemoveAt(index);
            EditorUtility.SetDirty(controller);
        }
    }
}