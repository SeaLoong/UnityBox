using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCAnimatorPlayAudio = VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio;

/// <summary>
/// Advanced Costume Controller 工具类
/// </summary>
public static class Utils
{
  /// <summary>
  /// 检查 Transform 上是否有网格组件
  /// </summary>
  public static bool HasMeshOn(Transform t)
  {
    if (t == null) return false;

    var smr = t.GetComponent<SkinnedMeshRenderer>();
    if (smr != null) return true;

    var mr = t.GetComponent<MeshRenderer>();
    var mf = t.GetComponent<MeshFilter>();
    return mr != null && mf != null;
  }

  /// <summary>
  /// 检查名称是否在忽略列表中
  /// </summary>
  public static bool IsNameIgnored(string name, HashSet<string> ignoreSet)
  {
    if (string.IsNullOrEmpty(name)) return true;
    if (ignoreSet == null || ignoreSet.Count == 0) return false;

    foreach (var ig in ignoreSet)
    {
      if (string.IsNullOrEmpty(ig)) continue;
      if (name.IndexOf(ig, StringComparison.OrdinalIgnoreCase) >= 0) return true;
    }
    return false;
  }

  /// <summary>
  /// 从 CSV 字符串构建忽略集合
  /// </summary>
  public static HashSet<string> BuildIgnoreSet(string ignoreNamesCsv)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(ignoreNamesCsv)) return set;

    var parts = ignoreNamesCsv
      .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
      .Select(s => s.Trim())
      .Where(s => !string.IsNullOrEmpty(s));

    foreach (var p in parts) set.Add(p);
    return set;
  }

  /// <summary>
  /// 查找或创建子对象
  /// </summary>
  public static GameObject FindOrCreateChild(GameObject parent, string name)
  {
    var child = parent.transform.Find(name);
    if (child != null) return child.gameObject;

    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Node");
    go.transform.SetParent(parent.transform, false);
    return go;
  }

  /// <summary>
  /// 确保菜单路径上的所有节点存在
  /// </summary>
  public static GameObject EnsureMenuPath(GameObject parent, string relativePath)
  {
    if (string.IsNullOrEmpty(relativePath)) return parent;

    var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var current = parent;

    for (int i = 0; i < parts.Length - 1; i++)
    {
      current = FindOrCreateChild(current, parts[i].Trim());
      EnsureSubmenuOnNode(current);
    }

    if (parts.Length > 0)
      current = FindOrCreateChild(current, parts[parts.Length - 1].Trim());

    return current;
  }

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

  /// <summary>
  /// 获取层级路径（用于排序）
  /// </summary>
  public static string GetHierarchyPath(GameObject root, GameObject node)
  {
    var indices = new List<int>();
    var t = node.transform;
    while (t != null && t != root.transform)
    {
      indices.Add(t.GetSiblingIndex());
      t = t.parent;
    }
    indices.Reverse();
    return string.Join("/", indices.Select(i => i.ToString("D4")));
  }

  /// <summary>
  /// 清理字符串，只保留字母数字和斜杠
  /// </summary>
  public static string Sanitize(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) || c == '/' ? c : '_').ToArray();
    return new string(arr);
  }

  /// <summary>
  /// 清理字符串用于文件名，将所有非法字符（包括斜杠）替换为下划线
  /// </summary>
  public static string SanitizeForFileName(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
    return new string(arr);
  }

  /// <summary>
  /// 构建参数名称
  /// </summary>
  public static string BuildParamName(string paramPrefix, string relPath)
  {
    if (string.IsNullOrEmpty(paramPrefix))
      return Sanitize(relPath);
    return paramPrefix + "/" + Sanitize(relPath);
  }

  /// <summary>
  /// 在节点上确保存在子菜单组件
  /// </summary>
  public static void EnsureSubmenuOnNode(GameObject node, string label = "")
  {
    var old = node.GetComponent<ModularAvatarMenuItem>();
    if (old != null) Undo.DestroyObjectImmediate(old);

    var mi = CreateMenuItem(node);
    if (!string.IsNullOrEmpty(label)) mi.label = label;
    mi.PortableControl.Type = PortableControlType.SubMenu;
    mi.automaticValue = true;
    mi.MenuSource = SubmenuSource.Children;
  }

  /// <summary>
  /// 创建菜单项组件
  /// </summary>
  public static ModularAvatarMenuItem CreateMenuItem(GameObject node)
  {
    var existing = node.GetComponent<ModularAvatarMenuItem>();
    if (existing != null) Undo.DestroyObjectImmediate(existing);

    try { return Undo.AddComponent<ModularAvatarMenuItem>(node); }
    catch { return node.AddComponent<ModularAvatarMenuItem>(); }
  }

  /// <summary>
  /// 准备子根节点（会删除已存在的同名节点）
  /// </summary>
  public static GameObject PrepareChildRoot(GameObject parent, string name)
  {
    var existing = parent.transform.Find(name);
    if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Root Child");
    go.transform.SetParent(parent.transform, false);
    go.transform.SetAsFirstSibling();
    return go;
  }

  /// <summary>
  /// 确保 Parameters 组件存在
  /// </summary>
  public static ModularAvatarParameters EnsureParametersComponent(GameObject host)
  {
    var comp = host.GetComponent<ModularAvatarParameters>();
    if (comp == null)
    {
      try { comp = Undo.AddComponent<ModularAvatarParameters>(host); }
      catch { comp = host.AddComponent<ModularAvatarParameters>(); }
    }

    if (comp.parameters == null)
    {
      Undo.RecordObject(comp, "Init parameters list");
      comp.parameters = new List<ParameterConfig>();
    }
    return comp;
  }

  /// <summary>
  /// 添加或更新参数
  /// </summary>
  public static void AddOrUpdateParameter(ModularAvatarParameters maParams, string paramName,
    ParameterSyncType syncType, float defaultValue, bool saved)
  {
    var existingIndex = maParams.parameters.FindIndex(p => p.nameOrPrefix == paramName);
    if (existingIndex >= 0)
    {
      Undo.RecordObject(maParams, "Update parameter");
      var existing = maParams.parameters[existingIndex];
      existing.defaultValue = defaultValue;
      existing.hasExplicitDefaultValue = true;
      existing.saved = saved;
      maParams.parameters[existingIndex] = existing;
      return;
    }

    Undo.RecordObject(maParams, "Add parameter");
    maParams.parameters.Add(new ParameterConfig
    {
      nameOrPrefix = paramName,
      remapTo = "",
      internalParameter = false,
      isPrefix = false,
      syncType = syncType,
      localOnly = false,
      defaultValue = defaultValue,
      saved = saved,
      hasExplicitDefaultValue = true
    });
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
  public static void AddSubAsset(AnimatorController controller, Object asset)
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
  public static void AddSubAssets(AnimatorController controller, params Object[] assets)
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
  /// 在状态上添加 VRC Animator Layer Control 行为（用于控制单个层的权重）
  /// </summary>
  public static void AddLayerControlBehaviour(AnimatorState state, int layerIndex, float goalWeight, float blendDuration = 0f)
  {
    var behaviour = state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>();
    behaviour.layer = layerIndex;
    behaviour.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX;
    behaviour.goalWeight = goalWeight;
    behaviour.blendDuration = blendDuration;
  }

  /// <summary>
  /// 在状态上添加多个层权重控制行为
  /// </summary>
  public static void AddMultiLayerControlBehaviour(AnimatorState state, int[] layerIndices, float goalWeight, float blendDuration = 0f)
  {
    foreach (int layerIndex in layerIndices)
    {
      var behaviour = state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>();
      behaviour.layer = layerIndex;
      behaviour.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX;
      behaviour.goalWeight = goalWeight;
      behaviour.blendDuration = blendDuration;
    }
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
