using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public interface IAnimatorLayerWeightAdapter
    {
        void AddLayerWeightControl(AnimatorState state, int layerIndex, float weight, string debugString);
    }

    /// <summary>Produces real StateMachineBehaviours for standalone Unity Animators.</summary>
    public sealed class UnityAnimatorBehaviourAdapter : IAnimatorBehaviourAdapter, IAnimatorLayerWeightAdapter
    {
        public static readonly UnityAnimatorBehaviourAdapter Instance = new UnityAnimatorBehaviourAdapter();

        public void AddDisableSelfLayerControl(AnimatorState state, int layerIndex, string debugString) =>
            AddLayerWeightControl(state, layerIndex, 0f, debugString);

        public void AddLayerWeightControl(AnimatorState state, int layerIndex, float weight, string debugString)
        {
            var actions = state.AddStateMachineBehaviour<AnimatorStateEntryActions>();
            actions.controlledLayer = layerIndex;
            actions.layerWeight = weight;
        }

        public void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false) =>
            AddMultiParameterDriverBehaviour(state, new Dictionary<string, float> { [parameterName] = value }, localOnly);

        public void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
        {
            var actions = state.AddStateMachineBehaviour<AnimatorStateEntryActions>();
            actions.localOnly = localOnly;
            actions.assignments = parameters.Select(item => new AnimatorStateEntryActions.Assignment
            {
                parameter = item.Key, value = item.Value,
            }).ToArray();
        }

        public void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip)
        {
            var actions = state.AddStateMachineBehaviour<AnimatorStateEntryActions>();
            actions.audioSourcePath = audioSourcePath;
            actions.audioClip = clip;
        }
    }
}
