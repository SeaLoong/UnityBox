using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Explicit platform operations used by the otherwise Unity-only graph generators.
    /// Implementations must provide the requested behaviour; a graph without these
    /// state-entry effects is not a functioning password/countdown system.
    /// </summary>
    public interface IAnimatorBehaviourAdapter
    {
        void AddDisableSelfLayerControl(AnimatorState state, int layerIndex, string debugString);
        void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false);
        void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false);
        void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip);
    }
}
