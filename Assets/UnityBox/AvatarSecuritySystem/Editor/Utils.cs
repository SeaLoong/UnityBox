using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCAnimatorPlayAudio = VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio;
namespace UnityBox.AvatarSecuritySystem.Editor
{
  public static class Utils
  {
    public static string GetRelativePath(GameObject root, GameObject node)
    {
      var parts = new List<string>();
      var t = node.transform;
      while (t != null && t != root.transform)
      {
        parts.Add(t.name);
        t = t.parent;
      }
      parts.Reverse();
      return string.Join("/", parts);
    }
    public static AnimatorControllerLayer CreateLayer(string name, float defaultWeight = 1f)
    {
      var stateMachine = new AnimatorStateMachine
      {
        name = name,
        hideFlags = HideFlags.HideInHierarchy
      };
      return new AnimatorControllerLayer
      {
        name = name,
        defaultWeight = defaultWeight,
        stateMachine = stateMachine
      };
    }
    public static void AddParameterIfNotExists(AnimatorController controller, string name,
      AnimatorControllerParameterType type, bool defaultBool = false, int defaultInt = 0,
      float defaultFloat = 0f)
    {
      if (controller.parameters.Any(p => p.name == name))
        return;
      controller.AddParameter(new AnimatorControllerParameter
      {
        name = name,
        type = type,
        defaultBool = defaultBool,
        defaultInt = defaultInt,
        defaultFloat = defaultFloat
      });
    }
    public static void EnsureBuiltInVRCParameters(
      AnimatorController controller,
      bool ensureIsLocal = true,
      bool ensureGestureParameters = true)
    {
      if (controller == null)
        return;
      if (ensureIsLocal)
      {
        AddParameterIfNotExists(controller, Constants.PARAM_IS_LOCAL,
          AnimatorControllerParameterType.Bool, defaultBool: false);
        WarnIfParameterTypeMismatch(controller, Constants.PARAM_IS_LOCAL,
          AnimatorControllerParameterType.Bool);
      }
      if (ensureGestureParameters)
      {
        AddParameterIfNotExists(controller, Constants.PARAM_GESTURE_LEFT,
          AnimatorControllerParameterType.Int, defaultInt: 0);
        AddParameterIfNotExists(controller, Constants.PARAM_GESTURE_RIGHT,
          AnimatorControllerParameterType.Int, defaultInt: 0);
        WarnIfParameterTypeMismatch(controller, Constants.PARAM_GESTURE_LEFT,
          AnimatorControllerParameterType.Int);
        WarnIfParameterTypeMismatch(controller, Constants.PARAM_GESTURE_RIGHT,
          AnimatorControllerParameterType.Int);
      }
    }
    private static void WarnIfParameterTypeMismatch(
      AnimatorController controller,
      string parameterName,
      AnimatorControllerParameterType expectedType)
    {
      var actualType = GetParameterType(controller, parameterName);
      if (!actualType.HasValue || actualType.Value == expectedType)
        return;
      Debug.LogWarning(
        $"[ASS] Parameter '{parameterName}' type is {actualType.Value} (expected {expectedType}). " +
        "This may cause transition conditions to behave unexpectedly.");
    }
    public static AnimatorControllerParameterType? GetParameterType(
      AnimatorController controller, string name)
    {
      var param = controller.parameters.FirstOrDefault(p => p.name == name);
      if (param == null) return null;
      return param.type;
    }
    public static void AddIsLocalCondition(
      AnimatorStateTransition transition,
      AnimatorController controller,
      bool isTrue = true)
    {
      var paramType = GetParameterType(controller, Constants.PARAM_IS_LOCAL);
      switch (paramType)
      {
        case AnimatorControllerParameterType.Bool:
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            0, Constants.PARAM_IS_LOCAL);
          break;
        case AnimatorControllerParameterType.Int:
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
            isTrue ? 0 : 1, Constants.PARAM_IS_LOCAL);
          break;
        case AnimatorControllerParameterType.Float:
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
            isTrue ? 0.5f : 0.5f, Constants.PARAM_IS_LOCAL);
          break;
        default:
          Debug.LogWarning(
            $"[ASS] IsLocal parameter type is {paramType?.ToString() ?? "missing"}, " +
            $"using Greater>0 as fallback");
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
            isTrue ? 0 : 1, Constants.PARAM_IS_LOCAL);
          break;
      }
    }
    public static AnimatorStateTransition CreateTransition(AnimatorState from, AnimatorState to,
      bool hasExitTime = false, float exitTime = 0f, float duration = 0f)
    {
      var transition = from.AddTransition(to);
      transition.hasExitTime = hasExitTime;
      transition.exitTime = exitTime;
      transition.duration = duration;
      transition.hasFixedDuration = true;
      return transition;
    }
    public static AnimatorStateTransition CreateAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState to, float duration = 0f)
    {
      var transition = stateMachine.AddAnyStateTransition(to);
      transition.hasExitTime = false;
      transition.duration = duration;
      transition.hasFixedDuration = true;
      transition.canTransitionToSelf = false; // 避免 AnyState 自循环，减少性能开销
      return transition;
    }
    public static void OptimizeStates(AnimatorStateMachine stateMachine, AnimationClip emptyClip)
    {
      foreach (var childState in stateMachine.states)
      {
        if (childState.state.motion == null)
          childState.state.motion = emptyClip;
      }
      foreach (var childMachine in stateMachine.stateMachines)
        OptimizeStates(childMachine.stateMachine, emptyClip);
    }
    public static void AddSubAsset(AnimatorController controller, UnityEngine.Object asset)
    {
      if (asset == null || controller == null)
        return;
      string controllerPath = AssetDatabase.GetAssetPath(controller);
      string assetPath = AssetDatabase.GetAssetPath(asset);
      if (!string.IsNullOrEmpty(assetPath) && assetPath != controllerPath)
        return;
      if (AssetDatabase.LoadAllAssetsAtPath(controllerPath).Any(existing => existing == asset))
        return;
      AssetDatabase.AddObjectToAsset(asset, controllerPath);
    }
    public static void AddSubAssets(AnimatorController controller, params UnityEngine.Object[] assets)
    {
      foreach (var asset in assets)
        AddSubAsset(controller, asset);
    }
    private static readonly Dictionary<string, AnimationClip> _emptyClipCache = new Dictionary<string, AnimationClip>();
    public static AnimationClip GetOrCreateEmptyClip(string folder, string fileName)
    {
      string path = $"{folder}/{fileName}";
      if (_emptyClipCache.TryGetValue(path, out var cached) && cached != null)
        return cached;
      var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
      if (clip != null)
      {
        clip.name = Constants.SHARED_EMPTY_CLIP_DISPLAY_NAME;
        _emptyClipCache[path] = clip;
        return clip;
      }
      var newClip = new AnimationClip
      {
        name = Constants.SHARED_EMPTY_CLIP_DISPLAY_NAME,
        legacy = false
      };
      var settings = AnimationUtility.GetAnimationClipSettings(newClip);
      settings.loopTime = false;
      AnimationUtility.SetAnimationClipSettings(newClip, settings);
      System.IO.Directory.CreateDirectory(folder);
      AssetDatabase.CreateAsset(newClip, path);
      _emptyClipCache[path] = newClip;
      return newClip;
    }
    public static void SaveAndRefresh() => AssetDatabase.SaveAssets();
    public static void LogOptimizationStats(AnimatorController controller, string systemName = "ASS")
    {
      int stateCount = 0, transitionCount = 0, blendTreeCount = 0;
      foreach (var layer in controller.layers)
        CountStatesRecursive(layer.stateMachine, ref stateCount, ref transitionCount, ref blendTreeCount);
      string controllerPath = AssetDatabase.GetAssetPath(controller);
      long fileSize = 0;
      if (!string.IsNullOrEmpty(controllerPath))
      {
        var fileInfo = new System.IO.FileInfo(controllerPath);
        if (fileInfo.Exists) fileSize = fileInfo.Length;
      }
      float fileSizeKB = fileSize / 1024f;
      float avgSizePerState = stateCount > 0 ? fileSize / stateCount / 1024f : 0;
      Debug.Log($"[{systemName}] 优化统计:\n" +
                $"  状态数: {stateCount}\n" +
                $"  转换数: {transitionCount}\n" +
                $"  BlendTree数: {blendTreeCount}\n" +
                $"  文件大小: {fileSizeKB:F2} KB\n" +
                $"  平均每状态: {avgSizePerState:F2} KB");
    }
    private static void CountStatesRecursive(AnimatorStateMachine sm, ref int states, ref int transitions, ref int blendTrees)
    {
      states += sm.states.Length;
      transitions += sm.anyStateTransitions.Length;
      foreach (var state in sm.states)
      {
        transitions += state.state.transitions.Length;
        if (state.state.motion is BlendTree)
          blendTrees++;
      }
      foreach (var childMachine in sm.stateMachines)
        CountStatesRecursive(childMachine.stateMachine, ref states, ref transitions, ref blendTrees);
    }
    public static void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false)
    {
      AddParameterDriverBehaviourInternal(state, new Dictionary<string, float> { { parameterName, value } }, localOnly);
    }
    public static void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
    {
      AddParameterDriverBehaviourInternal(state, parameters, localOnly);
    }
    private static void AddParameterDriverBehaviourInternal(AnimatorState state, Dictionary<string, float> parameters, bool localOnly)
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
    public static void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip)
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
