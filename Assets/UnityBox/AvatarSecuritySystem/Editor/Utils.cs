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
  /// <summary>
  /// Avatar Security System 工具类
  /// </summary>
  public static class Utils
  {
    // ==================== Transform 工具方法 ====================

    /// <summary>
    /// 获取从 root 到 node 的相对路径
    /// </summary>
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

    // ==================== Animator 工具方法 ====================

    /// <summary>
    /// 创建一个新的 AnimatorControllerLayer
    /// </summary>
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

    /// <summary>
    /// 添加 Animator 参数（如果不存在）
    /// </summary>
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

    /// <summary>
    /// 获取 Animator 参数的当前类型，如果参数不存在则返回 null
    /// </summary>
    public static AnimatorControllerParameterType? GetParameterType(
      AnimatorController controller, string name)
    {
      var param = controller.parameters.FirstOrDefault(p => p.name == name);
      if (param == null) return null;
      return param.type;
    }

    /// <summary>
    /// 为 IsLocal 参数添加条件，自动适配参数类型。
    /// 
    /// VRCFury 会在 blend tree 中使用 IsLocal（作为 Float），导致 UpgradeWrongParamTypes
    /// 将 IsLocal 从 Bool 升级为 Float。如果此后使用 AnimatorConditionMode.If（仅适用于
    /// Bool），VRCFury 的 RemoveWrongParamTypes 会将该条件替换为 InvalidCondition（始终
    /// 为 false），从而导致 ASS 的安全系统完全失效。
    /// 
    /// 此方法根据 IsLocal 的实际类型选择合适的条件模式：
    /// - Bool:  If / IfNot（标准用法）
    /// - Int:   Greater 0 / Less 1（等效于 Bool 判断）
    /// - Float: Greater 0 / Less 1（等效于 Bool 判断）
    /// </summary>
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
          // VRCFury 的 blend tree 使用会将 IsLocal 升级为 Float
          // VRChat 在运行时设置 IsLocal = 1.0（本地）或 0.0（远端）
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
            isTrue ? 0.5f : 0.5f, Constants.PARAM_IS_LOCAL);
          break;

        default:
          // 参数不存在或类型未知，使用 Greater 0 作为最安全的通用条件
          // Greater 0 在所有类型下都能被正确处理：
          // - Bool: RemoveWrongParamTypes 会自动转换为 If
          // - Int/Float: Greater 0 直接有效
          Debug.LogWarning(
            $"[ASS] IsLocal parameter type is {paramType?.ToString() ?? "missing"}, " +
            $"using Greater>0 as fallback");
          transition.AddCondition(
            isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
            isTrue ? 0 : 1, Constants.PARAM_IS_LOCAL);
          break;
      }
    }

    /// <summary>
    /// 创建状态转换
    /// </summary>
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

    /// <summary>
    /// 创建 Any State 转换
    /// </summary>
    public static AnimatorStateTransition CreateAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState to, float duration = 0f)
    {
      var transition = stateMachine.AddAnyStateTransition(to);
      transition.hasExitTime = false;
      transition.duration = duration;
      transition.hasFixedDuration = true;
      return transition;
    }

    /// <summary>
    /// 复用空 Clip 优化状态（将 null motion 替换为指定的空 clip）
    /// </summary>
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

    /// <summary>
    /// 添加子资产到 Controller（自动检查有效性和重复）
    /// </summary>
    public static void AddSubAsset(AnimatorController controller, UnityEngine.Object asset)
    {
      if (asset == null || controller == null)
        return;

      string controllerPath = AssetDatabase.GetAssetPath(controller);
      string assetPath = AssetDatabase.GetAssetPath(asset);

      // 已有外部路径，跳过
      if (!string.IsNullOrEmpty(assetPath) && assetPath != controllerPath)
        return;

      // 已存在于 Controller 中，跳过
      if (AssetDatabase.LoadAllAssetsAtPath(controllerPath).Any(existing => existing == asset))
        return;

      AssetDatabase.AddObjectToAsset(asset, controllerPath);
    }

    /// <summary>
    /// 批量添加子资产
    /// </summary>
    public static void AddSubAssets(AnimatorController controller, params UnityEngine.Object[] assets)
    {
      foreach (var asset in assets)
        AddSubAsset(controller, asset);
    }

    /// <summary>
    /// 获取或创建共享的空 AnimationClip（自动缓存，按路径去重）
    /// </summary>
    private static readonly Dictionary<string, AnimationClip> _emptyClipCache = new Dictionary<string, AnimationClip>();

    public static AnimationClip GetOrCreateEmptyClip(string folder, string fileName)
    {
      string path = $"{folder}/{fileName}";

      if (_emptyClipCache.TryGetValue(path, out var cached) && cached != null)
        return cached;

      var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
      if (clip != null)
      {
        _emptyClipCache[path] = clip;
        return clip;
      }

      var newClip = new AnimationClip { legacy = false };
      var settings = AnimationUtility.GetAnimationClipSettings(newClip);
      settings.loopTime = false;
      AnimationUtility.SetAnimationClipSettings(newClip, settings);

      System.IO.Directory.CreateDirectory(folder);
      AssetDatabase.CreateAsset(newClip, path);

      _emptyClipCache[path] = newClip;
      return newClip;
    }

    /// <summary>
    /// 保存资产
    /// </summary>
    public static void SaveAndRefresh() => AssetDatabase.SaveAssets();

    /// <summary>
    /// 记录优化统计信息
    /// </summary>
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

    /// <summary>
    /// 在状态上添加 VRC Avatar Parameter Driver 行为（单个参数）
    /// </summary>
    public static void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false)
    {
      AddParameterDriverBehaviourInternal(state, new Dictionary<string, float> { { parameterName, value } }, localOnly);
    }

    /// <summary>
    /// 在状态上添加多个参数驱动
    /// </summary>
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

    /// <summary>
    /// 在状态上添加 VRC Animator Play Audio 行为
    /// </summary>
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
