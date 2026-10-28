#if NDMF_AVAILABLE
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityBox.AnimatorOptimizer
{
    internal static partial class AnimatorOptimizerNdmfProcessor
    {
        private sealed class VirtualAapReplacement
        {
            public VirtualLayer Source;
            public VirtualLayer Update;
            public VirtualStateMachine Output;
            public HashSet<string> Inputs;
            public IReadOnlyList<AnimatorControllerLayer> Generated;
            public int Index;
            public int ScratchIndex;
        }

        // Use the core symbolic compiler for both backends. The scratch graph is built
        // from current virtual data, so MA/other plugins' edits are included. Only the
        // resulting motion DAG is copied back; retained virtual layer IDs never change.
        private static bool ConvertVirtualAapLayers(VirtualAnimatorController controller,
            CloneContext cloneContext, AnimatorOptimizerOptions options, ISet<VirtualLayer> requested)
        {
            var layers = controller.Layers.ToList();
            var synchronizedTargets = layers.Where(layer => layer.SyncedLayerIndex >= 0)
                .Select(layer => layer.SyncedLayerIndex).ToHashSet();
            var bindingScope = CollectAapBindingScope(layers);
            var staged = new List<VirtualAapReplacement>();
            var reserved = new List<AnimatorControllerParameter>();
            var initialParameters = controller.Parameters.Keys.ToHashSet(StringComparer.Ordinal);
            var scratches = new List<VirtualAapScratch>();
            try
            {
                for (var index = 0; index < layers.Count; index++)
                {
                    var layer = layers[index];
                    if (requested != null && !requested.Contains(layer) ||
                        options.PreserveMmdCompatibility && index == 0 ||
                        synchronizedTargets.Contains(layer.VirtualLayerIndex) || layer.SyncedLayerIndex >= 0 ||
                        layer.StateMachine == null || layer.StateMachine.States.Count < 2 ||
                        layer.StateMachine.StateMachines.Count != 0 ||
                        layer.StateMachine.States.Any(child => !CanMaterializeVirtualAapMotion(child.State.Motion)))
                        continue;
                    var scratch = new VirtualAapScratch();
                    scratches.Add(scratch);
                    if (!scratch.Build(layer, index, controller.Parameters.Values.Concat(reserved), bindingScope)) continue;
                    var coreOptions = options.Clone();
                    coreOptions.PreserveMmdCompatibility = false;
                    coreOptions.RequireAtLeastTwoLayers = false;
                    coreOptions.MergeSynchronizedStateMachines = false;
                    coreOptions.RemoveEffectivelyEmptyLayers = false;
                    coreOptions.NormalizeAlwaysOneParameters = false;
                    if (!bindingScope.Complete)
                        coreOptions.WriteDefaultsPolicy = AnimatorOptimizerComponent.WriteDefaultsPolicy.Strict;
                    var report = AnimatorOptimizerAnalyzer.Analyze(scratch.Controller, coreOptions);
                    var candidate = report.Layers[scratch.LayerIndex];
                    if (!candidate.CanConvert || candidate.ConversionKind != AnimatorOptimizerConversionKind.AapStateRegister)
                        continue;
                    var result = AnimatorOptimizerConverter.ConvertAapStateRegisterInPlace(scratch.Controller, scratch.LayerIndex, coreOptions);
                    var parameters = result.Controller.parameters.Where(parameter =>
                        !initialParameters.Contains(parameter.name) && reserved.All(other => other.name != parameter.name)).ToList();
                    reserved.AddRange(parameters);
                    staged.Add(new VirtualAapReplacement
                    {
                        Source = layer, Index = index, ScratchIndex = scratch.LayerIndex, Generated = result.GeneratedLayers,
                        Inputs = candidate.RequiredFloatParameters.ToHashSet(StringComparer.Ordinal),
                    });
                }
                if (staged.Count == 0) return false;
                var plans = staged.Select(item => new VirtualPlan { ReferencedParameters = item.Inputs }).ToList();
                var upgraded = UpgradeParameters(controller, plans, options, staged.Select(item => item.Source).ToHashSet());
                if (upgraded == null)
                {
                    Debug.LogWarning("[AnimatorOptimizer] AAP input parameters are written by another layer or Behaviour; source layers retained.");
                    return false;
                }
                foreach (var item in staged)
                {
                    var motions = new Dictionary<Motion, VirtualMotion>();
                    item.Update = VirtualLayer.Create(cloneContext,
                        item.Generated[0].name.Replace("AAPStateRegister_" + item.ScratchIndex, "AAPStateRegister_" + item.Index));
                    item.Update.StateMachine = CopyGeneratedAapMachine(item.Generated[0].stateMachine, cloneContext, motions);
                    item.Update.DefaultWeight = 1f;
                    item.Update.BlendingMode = AnimatorLayerBlendingMode.Override;
                    item.Output = CopyGeneratedAapMachine(item.Generated[1].stateMachine, cloneContext, motions);
                }
                foreach (var parameter in reserved)
                    controller.Parameters = controller.Parameters.SetItem(parameter.name, parameter);
                var replacements = staged.ToDictionary(item => item.Source);
                var outputLayers = new List<VirtualLayer>();
                foreach (var layer in layers)
                {
                    if (replacements.TryGetValue(layer, out var replacement))
                    {
                        outputLayers.Add(replacement.Update);
                        // Existing controls/manual selections keep addressing the original
                        // layer identity, now containing the observable Output graph.
                        layer.Name = replacement.Update.Name.Replace("/Update", "/Output");
                        layer.StateMachine = replacement.Output;
                        layer.DefaultWeight = replacement.Generated[1].defaultWeight;
                    }
                    outputLayers.Add(layer);
                }
                controller.Layers = outputLayers;
                if (options.VerboseLogging)
                    Debug.Log($"[AnimatorOptimizer] NDMF compiled {staged.Count} AAP StateRegister layers; upgraded parameters: {string.Join(", ", upgraded)}.");
                return true;
            }
            finally
            {
                foreach (var scratch in scratches) scratch.Dispose();
            }
        }

        private sealed class VirtualAapBindingScope
        {
            public bool Complete = true;
            public HashSet<EditorCurveBinding> Floats = new HashSet<EditorCurveBinding>();
            public HashSet<EditorCurveBinding> Objects = new HashSet<EditorCurveBinding>();
        }

        private static VirtualAapBindingScope CollectAapBindingScope(IEnumerable<VirtualLayer> layers)
        {
            var scope = new VirtualAapBindingScope();
            var visited = new HashSet<VirtualNode>();
            void Motion(VirtualMotion motion)
            {
                if (motion == null || !visited.Add(motion)) return;
                if (motion is VirtualClip clip)
                {
                    scope.Complete &= HasInspectableVirtualBindings(clip);
                    scope.Floats.UnionWith(clip.GetFloatCurveBindings());
                    scope.Objects.UnionWith(clip.GetObjectCurveBindings());
                }
                else if (motion is VirtualBlendTree tree)
                    foreach (var child in tree.Children) Motion(child.Motion);
                else scope.Complete = false;
            }
            void Machine(VirtualStateMachine machine)
            {
                if (machine == null || !visited.Add(machine)) return;
                foreach (var child in machine.States) Motion(child.State.Motion);
                foreach (var child in machine.StateMachines) Machine(child.StateMachine);
            }
            foreach (var layer in layers)
            {
                scope.Complete &= layer.SyncedLayerIndex < 0;
                Machine(layer.StateMachine);
                foreach (var motion in layer.SyncedLayerMotionOverrides.Values) Motion(motion);
            }
            return scope;
        }

        private static bool CanMaterializeVirtualAapMotion(VirtualMotion motion)
        {
            if (motion == null) return true;
            if (motion is VirtualClip clip)
                return IsOrdinaryVirtualClip(clip) && !HasVirtualAnimationEvents(clip) &&
                    !clip.Settings.hasAdditiveReferencePose && clip.AdditiveReferencePoseClip == null;
            return motion is VirtualBlendTree tree && tree.Children.All(child => CanMaterializeVirtualAapMotion(child.Motion));
        }

        private static VirtualStateMachine CopyGeneratedAapMachine(AnimatorStateMachine source,
            CloneContext context, Dictionary<Motion, VirtualMotion> motions)
        {
            var output = VirtualStateMachine.Create(context, source.name);
            foreach (var child in source.states)
            {
                var original = child.state;
                var state = output.AddState(original.name, CopyGeneratedAapMotion(original.motion, motions), child.position);
                state.WriteDefaultValues = original.writeDefaultValues;
                state.Speed = original.speed;
                state.CycleOffset = original.cycleOffset;
                state.Mirror = original.mirror;
                state.Tag = original.tag;
                state.IKOnFeet = original.iKOnFeet;
                if (original == source.defaultState) output.DefaultState = state;
            }
            output.Behaviours = source.behaviours.Select(behaviour => Object.Instantiate(behaviour)).ToImmutableList();
            return output;
        }

        private static VirtualMotion CopyGeneratedAapMotion(Motion source, Dictionary<Motion, VirtualMotion> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out var existing)) return existing;
            if (source is AnimationClip clip)
            {
                // Create owns its backing clip. No origin points at a scratch asset
                // deleted below, and actual final NDMF commit needs no temporary file.
                var output = VirtualClip.Create(clip.name);
                copies.Add(source, output);
                output.FrameRate = clip.frameRate;
                output.WrapMode = clip.wrapMode;
                output.Legacy = clip.legacy;
                output.LocalBounds = clip.localBounds;
                output.Settings = AnimationUtility.GetAnimationClipSettings(clip);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    output.SetFloatCurve(binding, AnimationUtility.GetEditorCurve(clip, binding));
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    output.SetObjectCurve(binding, AnimationUtility.GetObjectReferenceCurve(clip, binding));
                return output;
            }
            var tree = (BlendTree)source;
            var result = VirtualBlendTree.Create(tree.name);
            copies.Add(source, result);
            result.BlendType = tree.blendType;
            result.BlendParameter = tree.blendParameter;
            result.BlendParameterY = tree.blendParameterY;
            result.UseAutomaticThresholds = tree.useAutomaticThresholds;
            result.MinThreshold = tree.minThreshold;
            result.MaxThreshold = tree.maxThreshold;
            using (var serialized = new SerializedObject(tree))
                result.NormalizedBlendValues = serialized.FindProperty("m_NormalizedBlendValues").boolValue;
            result.Children = tree.children.Select(child => new VirtualBlendTree.VirtualChildMotion
            {
                Motion = CopyGeneratedAapMotion(child.motion, copies), Threshold = child.threshold,
                DirectBlendParameter = child.directBlendParameter, TimeScale = child.timeScale,
                CycleOffset = child.cycleOffset, Mirror = child.mirror, Position = child.position,
            }).ToImmutableList();
            return result;
        }

        private sealed class VirtualAapScratch : IDisposable
        {
            private readonly List<Object> objects = new List<Object>();
            private readonly Dictionary<VirtualMotion, Motion> motions = new Dictionary<VirtualMotion, Motion>();
            private string path;
            public AnimatorController Controller { get; private set; }
            public int LayerIndex { get; private set; }

            public bool Build(VirtualLayer layer, int originalIndex, IEnumerable<AnimatorControllerParameter> parameters,
                VirtualAapBindingScope scope)
            {
                var source = layer.StateMachine;
                var states = source.States.Select(child => child.State).ToHashSet();
                if (source.DefaultState == null || !states.Contains(source.DefaultState)) return false;
                var transitions = source.EntryTransitions.Cast<VirtualTransitionBase>()
                    .Concat(source.AnyStateTransitions).Concat(states.SelectMany(state => state.Transitions));
                if (transitions.Any(transition => transition.DestinationStateMachine != null ||
                    transition.DestinationState != null && !states.Contains(transition.DestinationState))) return false;
                const string folder = "Assets/UnityBox/Generated/AnimatorOptimizerNdmf";
                var parent = "Assets";
                foreach (var segment in folder.Split('/').Skip(1))
                {
                    if (!AssetDatabase.IsValidFolder(parent + "/" + segment)) AssetDatabase.CreateFolder(parent, segment);
                    parent += "/" + segment;
                }
                Controller = new AnimatorController { name = "AAP virtual materialization" };
                path = folder + "/AAP_" + Guid.NewGuid().ToString("N") + ".controller";
                AssetDatabase.CreateAsset(Controller, path);
                Controller.parameters = parameters.ToArray();
                var machine = Keep(new AnimatorStateMachine { name = source.Name });
                machine.behaviours = source.Behaviours.ToArray();
                var map = source.States.ToDictionary(child => child.State, child =>
                {
                    var state = Keep(new AnimatorState
                    {
                        name = child.State.Name, motion = CopyMotion(child.State.Motion),
                        writeDefaultValues = child.State.WriteDefaultValues, speed = child.State.Speed,
                        cycleOffset = child.State.CycleOffset, mirror = child.State.Mirror,
                        iKOnFeet = child.State.IKOnFeet, tag = child.State.Tag,
                        speedParameterActive = child.State.SpeedParameter != null, speedParameter = child.State.SpeedParameter ?? "",
                        timeParameterActive = child.State.TimeParameter != null, timeParameter = child.State.TimeParameter ?? "",
                        mirrorParameterActive = child.State.MirrorParameter != null, mirrorParameter = child.State.MirrorParameter ?? "",
                        cycleOffsetParameterActive = child.State.CycleOffsetParameter != null, cycleOffsetParameter = child.State.CycleOffsetParameter ?? "",
                        behaviours = child.State.Behaviours.ToArray(),
                    });
                    return state;
                });
                machine.states = source.States.Select(child => new ChildAnimatorState { state = map[child.State], position = child.Position }).ToArray();
                machine.defaultState = map[source.DefaultState];
                foreach (var state in states)
                    map[state].transitions = state.Transitions.Select(transition => CopyTransition(transition, map)).ToArray();
                machine.anyStateTransitions = source.AnyStateTransitions.Select(transition => CopyTransition(transition, map)).ToArray();
                machine.entryTransitions = source.EntryTransitions.Select(transition =>
                {
                    var copy = Keep(new AnimatorTransition());
                    SetTransition(copy, transition, map);
                    return copy;
                }).ToArray();
                var materialized = new AnimatorControllerLayer
                {
                    name = layer.Name, stateMachine = machine,
                    defaultWeight = layer.DefaultWeight,
                    blendingMode = layer.BlendingMode, avatarMask = layer.AvatarMask == null ? null :
                        (AvatarMask)VirtualMaskObjectField.GetValue(layer.AvatarMask),
                    iKPass = layer.IKPass, syncedLayerIndex = -1,
                };
                LayerIndex = originalIndex == 0 ? 0 : 1;
                if (LayerIndex == 0)
                    Controller.layers = new[] { materialized };
                else
                {
                    var baseMachine = Keep(new AnimatorStateMachine { name = "Base layer context" });
                    var baseState = Keep(baseMachine.AddState("Base"));
                    baseState.writeDefaultValues = false;
                    baseState.motion = Keep(new AnimationClip { name = "Base context empty" });
                    Controller.layers = new[]
                    {
                        new AnimatorControllerLayer { name = "Base context", stateMachine = baseMachine, defaultWeight = 1f, syncedLayerIndex = -1 },
                        materialized,
                    };
                }
                // Core WD Off eligibility uses every controller binding, including
                // retained layers. A zero-weight scope layer supplies those bindings
                // without copying or evaluating unrelated state-machine logic.
                var scopeClip = Keep(new AnimationClip { name = "Controller binding scope" });
                foreach (var binding in scope.Floats)
                    AnimationUtility.SetEditorCurve(scopeClip, binding, AnimationCurve.Constant(0f, 1f / 60f, 0f));
                foreach (var binding in scope.Objects)
                    AnimationUtility.SetObjectReferenceCurve(scopeClip, binding, new[] { new ObjectReferenceKeyframe { time = 0f } });
                var scopeMachine = Keep(new AnimatorStateMachine { name = "Controller binding scope" });
                var scopeState = Keep(scopeMachine.AddState("Scope"));
                scopeState.writeDefaultValues = false;
                scopeState.motion = scopeClip;
                Controller.layers = Controller.layers.Concat(new[] { new AnimatorControllerLayer
                {
                    name = "Binding scope", stateMachine = scopeMachine, defaultWeight = 0f, syncedLayerIndex = -1,
                } }).ToArray();
                return true;
            }

            private AnimatorStateTransition CopyTransition(VirtualStateTransition source, Dictionary<VirtualState, AnimatorState> states)
            {
                var result = Keep(new AnimatorStateTransition
                {
                    canTransitionToSelf = source.CanTransitionToSelf, duration = source.Duration,
                    hasExitTime = source.ExitTime.HasValue, exitTime = source.ExitTime ?? 0f,
                    hasFixedDuration = source.HasFixedDuration, offset = source.Offset,
                    interruptionSource = source.InterruptionSource, orderedInterruption = source.OrderedInterruption,
                });
                SetTransition(result, source, states);
                return result;
            }

            private static void SetTransition(AnimatorTransitionBase result, VirtualTransitionBase source,
                Dictionary<VirtualState, AnimatorState> states)
            {
                result.name = source.Name;
                result.mute = source.Mute;
                result.solo = source.Solo;
                result.conditions = source.Conditions.ToArray();
                result.isExit = source.IsExit;
                result.destinationState = source.DestinationState == null ? null : states[source.DestinationState];
            }

            private Motion CopyMotion(VirtualMotion source)
            {
                if (source == null) return null;
                if (motions.TryGetValue(source, out var existing)) return existing;
                if (source is VirtualClip clip)
                {
                    var result = Keep(new AnimationClip
                    {
                        name = clip.Name, frameRate = clip.FrameRate, wrapMode = clip.WrapMode,
                        legacy = clip.Legacy, localBounds = clip.LocalBounds,
                    });
                    motions.Add(source, result);
                    AnimationUtility.SetAnimationClipSettings(result, clip.Settings);
                    foreach (var binding in clip.GetFloatCurveBindings()) AnimationUtility.SetEditorCurve(result, binding, clip.GetFloatCurve(binding));
                    foreach (var binding in clip.GetObjectCurveBindings()) AnimationUtility.SetObjectReferenceCurve(result, binding, clip.GetObjectCurve(binding));
                    return result;
                }
                var tree = (VirtualBlendTree)source;
                var output = Keep(new BlendTree
                {
                    name = tree.Name, blendType = tree.BlendType, blendParameter = tree.BlendParameter,
                    blendParameterY = tree.BlendParameterY, useAutomaticThresholds = tree.UseAutomaticThresholds,
                    minThreshold = tree.MinThreshold, maxThreshold = tree.MaxThreshold,
                });
                motions.Add(source, output);
                using (var serialized = new SerializedObject(output))
                {
                    serialized.FindProperty("m_NormalizedBlendValues").boolValue = tree.NormalizedBlendValues;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                output.children = tree.Children.Select(child => new ChildMotion
                {
                    motion = CopyMotion(child.Motion), threshold = child.Threshold, position = child.Position,
                    directBlendParameter = child.DirectBlendParameter, timeScale = child.TimeScale,
                    cycleOffset = child.CycleOffset, mirror = child.Mirror,
                }).ToArray();
                return output;
            }

            private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
            public void Dispose()
            {
                if (path != null)
                {
                    foreach (var value in AssetDatabase.LoadAllAssetsAtPath(path)) Undo.ClearUndo(value);
                    AssetDatabase.DeleteAsset(path);
                }
                foreach (var value in objects.Where(value => value != null && !EditorUtility.IsPersistent(value)))
                    Object.DestroyImmediate(value);
            }
        }
    }
}
#endif
