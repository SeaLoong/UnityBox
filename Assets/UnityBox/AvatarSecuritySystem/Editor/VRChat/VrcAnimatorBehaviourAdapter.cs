using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCAnimatorPlayAudio = VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio;

namespace UnityBox.AvatarSecuritySystem.Editor
{
  public sealed class VrcAnimatorBehaviourAdapter : IAnimatorBehaviourAdapter
  {
    public static readonly VrcAnimatorBehaviourAdapter Instance = new VrcAnimatorBehaviourAdapter();
    /// <summary>
    /// 在终态把当前 ASS FX 子层权重设为 0。
    /// </summary>
    public void AddDisableSelfLayerControl(AnimatorState finalState,
      int fxLayerIndex, string debugString)
    {
      if (finalState == null || fxLayerIndex < 0)
        return;

      var control = finalState.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
      control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
      control.layer = fxLayerIndex;
      control.goalWeight = 0f;
      control.blendDuration = 0f;
      control.debugString = debugString;
      Debug.Log($"[ASS] Final state '{finalState.name}' disables FX sub-layer index {fxLayerIndex}");
    }
    public void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false)
    {
      AddParameterDriverBehaviourInternal(state, new Dictionary<string, float> { { parameterName, value } }, localOnly);
    }
    public void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
    {
      AddParameterDriverBehaviourInternal(state, parameters, localOnly);
    }
    private void AddParameterDriverBehaviourInternal(AnimatorState state, Dictionary<string, float> parameters, bool localOnly)
    {
      var behaviour = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
      behaviour.localOnly = localOnly;
      behaviour.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();
      foreach (var kvp in parameters)
        behaviour.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter
        {
          name = kvp.Key,
          value = kvp.Value,
          type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set
        });
    }
    private const float PLAY_AUDIO_VOLUME = 0.5f;
    public void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip)
    {
      if (clip == null) return;
      var behaviour = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
      behaviour.SourcePath = audioSourcePath;
      behaviour.Clips = new AudioClip[] { clip };
      behaviour.PlayOnEnter = true;
      behaviour.StopOnEnter = false;
      behaviour.StopOnExit = false;
      behaviour.PlayOnExit = false;
      behaviour.Loop = false;
      behaviour.Volume = new Vector2(PLAY_AUDIO_VOLUME, PLAY_AUDIO_VOLUME);
      behaviour.VolumeApplySettings = VRCAnimatorPlayAudio.ApplySettings.ApplyIfStopped;
    }
  }
}
