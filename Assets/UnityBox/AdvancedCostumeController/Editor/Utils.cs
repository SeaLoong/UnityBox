using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.core.editor;
using UnityBox.AdvancedCostumeController;
using VRC.SDK3.Avatars.Components;

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

  /// <summary>检查节点或其后代是否包含可渲染网格。</summary>
  public static bool HasMeshInHierarchy(Transform root)
  {
    if (root == null) return false;
    return root.GetComponentsInChildren<Renderer>(true)
      .Any(renderer => renderer is SkinnedMeshRenderer ||
        (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null));
  }

  /// <summary>
  /// 检查节点是否拥有服装骨架：节点必须直接拥有一个不含网格的骨架分支，
  /// 并且至少一个 SkinnedMeshRenderer 的 rootBone 位于该分支中。
  /// 这避免把仅用于组织多个变体的上层容器误识别为一套服装。
  /// </summary>
  public static bool OwnsSkeleton(Transform root)
  {
    return TryGetOwnedArmature(root, out _);
  }

  /// <summary>
  /// 获取一个服装根所拥有的 Armature。已有 MA Merge Armature 时优先使用它；
  /// 否则兼容旧版通过 SkinnedMeshRenderer.rootBone 推导的骨架分支。
  /// </summary>
  public static bool TryGetOwnedArmature(Transform root, out Transform armature)
  {
    armature = null;
    if (root == null) return false;

    if (TryGetOwnedMergeArmature(root, out var mergeArmature))
    {
      armature = mergeArmature.transform;
      return true;
    }

    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
      if (renderer.rootBone == null || !renderer.rootBone.IsChildOf(root)) continue;

      var branch = renderer.rootBone;
      while (branch.parent != null && branch.parent != root)
        branch = branch.parent;

      if (branch.parent == root && !HasMeshInHierarchy(branch))
      {
        armature = branch;
        return true;
      }
    }
    return false;
  }

  /// <summary>
  /// 查找属于当前服装根的 MA Merge Armature。只接受位于其直接无网格骨架分支中的
  /// 组件，避免把嵌套服装或仅用于组织变体的容器误判为一套服装。
  /// </summary>
  public static bool TryGetOwnedMergeArmature(
    Transform root,
    out ModularAvatarMergeArmature mergeArmature)
  {
    mergeArmature = null;
    if (root == null) return false;

    foreach (var candidate in root.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
    {
      if (candidate == null) continue;
      if (candidate.transform == root)
      {
        mergeArmature = candidate;
        return true;
      }

      var branch = candidate.transform;
      while (branch.parent != null && branch.parent != root)
        branch = branch.parent;
      if (branch.parent != root || HasMeshInHierarchy(branch)) continue;

      mergeArmature = candidate;
      return true;
    }
    return false;
  }

  /// <summary>检查节点是否位于任一 SkinnedMeshRenderer 的骨架分支中。</summary>
  public static bool IsSkeletonNode(Transform node, Transform outfitRoot)
  {
    if (node == null || outfitRoot == null) return false;

    foreach (var renderer in outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
      if (renderer.rootBone != null && node.IsChildOf(renderer.rootBone))
        return true;
    }
    return false;
  }

  /// <summary>
  /// 查找或创建子对象
  /// </summary>
  public static GameObject FindOrCreateChild(GameObject parent, string name)
  {
    var child = FindDirectChild(parent.transform, name);
    if (child != null) return child.gameObject;

    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Node");
    go.transform.SetParent(parent.transform, false);
    return go;
  }

  /// <summary>
  /// 创建默认展示名为 preferredName 的菜单子项。仅当同级已存在同名节点时才追加序号，
  /// 让正常情况下的菜单显示名直接跟随对象名，同时避免同名部件覆盖彼此。
  /// </summary>
  public static GameObject FindOrCreateUniqueChild(GameObject parent, string preferredName)
  {
    preferredName = string.IsNullOrWhiteSpace(preferredName) ? "Item" : preferredName.Trim();
    if (FindDirectChild(parent.transform, preferredName) == null)
      return FindOrCreateChild(parent, preferredName);

    for (int suffix = 2; ; suffix++)
    {
      string candidate = preferredName + " (" + suffix + ")";
      if (FindDirectChild(parent.transform, candidate) == null)
        return FindOrCreateChild(parent, candidate);
    }
  }

  /// <summary>
  /// 按精确名称查找直接子对象。不能使用 Transform.Find，
  /// 因为它会将名称中的 '/' 解释成层级路径。
  /// </summary>
  public static Transform FindDirectChild(Transform parent, string name)
  {
    for (int i = 0; i < parent.childCount; i++)
    {
      var child = parent.GetChild(i);
      if (child.name == name) return child;
    }
    return null;
  }

  /// <summary>
  /// 确保菜单路径上的所有节点存在
  /// </summary>
  public static GameObject EnsureMenuPath(
    GameObject parent,
    string relativePath,
    MenuPresentationSnapshot presentation = null)
  {
    if (string.IsNullOrEmpty(relativePath)) return parent;

    var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var current = parent;

    for (int i = 0; i < parts.Length - 1; i++)
    {
      current = FindOrCreateChild(current, parts[i].Trim());
      EnsureSubmenuOnNode(current, presentation: presentation);
    }

    if (parts.Length > 0)
      current = FindOrCreateChild(current, parts[parts.Length - 1].Trim());

    return current;
  }

  /// <summary>确保相对路径中的每一级菜单节点都具有子菜单配置。</summary>
  public static GameObject EnsureSubmenuPath(
    GameObject parent,
    string relativePath,
    MenuPresentationSnapshot presentation = null,
    string semanticKeyPrefix = null)
  {
    if (string.IsNullOrEmpty(relativePath)) return parent;

    var current = parent;
    var segments = new List<string>();
    foreach (var part in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
      string trimmedPart = part.Trim();
      segments.Add(trimmedPart);
      current = FindOrCreateChild(current, trimmedPart);
      string semanticKey = string.IsNullOrEmpty(semanticKeyPrefix)
        ? null
        : semanticKeyPrefix + GetStablePathFromSegments(segments);
      EnsureSubmenuOnNode(current, presentation: presentation, semanticKey: semanticKey);
    }
    return current;
  }

  /// <summary>用于跨语言恢复 Parts 子菜单展示属性的稳定语义键。</summary>
  public static string GetPartsMenuSemanticKey(GameObject menuRoot, GameObject outfitSubmenu)
  {
    return "acc:parts|" + GetStableMenuPath(menuRoot.transform, outfitSubmenu.transform);
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

  /// <summary>检查命名空间能否产生至少包含一个字母或数字的稳定资产名。</summary>
  public static bool HasUsableGenerationNamespace(string value)
  {
    return !string.IsNullOrWhiteSpace(value) &&
      SanitizeForFileName(value).Any(char.IsLetterOrDigit);
  }

  /// <summary>
  /// 验证用户输入的输出目录是 Assets 内的安全相对路径。
  /// 生成时会删除该目录下当前 ACC 的专属子目录，不能接受绝对路径或父级跳转。
  /// </summary>
  public static bool IsSafeAssetsFolder(string path)
  {
    if (string.IsNullOrWhiteSpace(path)) return false;

    string normalized = path.Replace('\\', '/').TrimEnd('/');
    if (normalized != "Assets" && !normalized.StartsWith("Assets/")) return false;

    return normalized.Split('/').All(segment =>
      !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
  }

  /// <summary>将合法的 Assets 路径规范化为 Unity AssetDatabase 使用的斜杠形式。</summary>
  public static string NormalizeAssetsFolder(string path)
  {
    return path.Replace('\\', '/').TrimEnd('/');
  }

  /// <summary>合并并规范化 Unity AssetDatabase 使用的路径。</summary>
  public static string CombineAssetPath(params string[] parts)
  {
    return Path.Combine(parts).Replace('\\', '/');
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
  /// 格式化菜单显示名称。格式化仅用于标签，不会改变动画绑定或参数路径。
  /// 依次执行精确前缀/后缀移除和可选正则替换；无效正则会保留前两步结果。
  /// </summary>
  public static string FormatPartDisplayName(
    string source,
    string prefixToRemove,
    string suffixToRemove,
    string regexPattern,
    string regexReplacement)
  {
    string result = source ?? "";
    if (!string.IsNullOrEmpty(prefixToRemove) &&
        result.StartsWith(prefixToRemove, StringComparison.Ordinal))
      result = result.Substring(prefixToRemove.Length);
    if (!string.IsNullOrEmpty(suffixToRemove) &&
        result.EndsWith(suffixToRemove, StringComparison.Ordinal))
      result = result.Substring(0, result.Length - suffixToRemove.Length);

    if (!string.IsNullOrWhiteSpace(regexPattern))
    {
      try { result = Regex.Replace(result, regexPattern, regexReplacement ?? ""); }
      catch (ArgumentException) { }
    }

    return string.IsNullOrWhiteSpace(result) ? source : result;
  }

  public static bool IsValidRegex(string pattern)
  {
    if (string.IsNullOrWhiteSpace(pattern)) return true;
    try
    {
      _ = new Regex(pattern);
      return true;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  /// <summary>
  /// 在节点上确保存在子菜单组件
  /// </summary>
  public static void EnsureSubmenuOnNode(
    GameObject node,
    string label = "",
    MenuPresentationSnapshot presentation = null,
    string semanticKey = null)
  {
    var mi = CreateMenuItem(node);
    if (!string.IsNullOrEmpty(label)) mi.label = label;
    mi.PortableControl.Type = PortableControlType.SubMenu;
    mi.PortableControl.Parameter = "";
    mi.PortableControl.Value = 1;
    mi.PortableControl.SubParameters = ImmutableList<string>.Empty;
    mi.PortableControl.VRChatSubMenu = null;
    mi.automaticValue = true;
    mi.isDefault = false;
    mi.isSaved = false;
    mi.isSynced = false;
    mi.MenuSource = SubmenuSource.Children;
    mi.menuSource_otherObjectChildren = null;
    ApplyMenuPresentation(presentation, node, mi, label, semanticKey);
  }

  /// <summary>
  /// 创建菜单项组件
  /// </summary>
  public static ModularAvatarMenuItem CreateMenuItem(GameObject node)
  {
    var existing = node.GetComponent<ModularAvatarMenuItem>();
    if (existing != null)
    {
      // 调用方会覆盖 ACC 控制字段；图标等展示属性不在这里改动。
      Undo.RecordObject(existing, "Update ACC menu item");
      return existing;
    }

    try { return Undo.AddComponent<ModularAvatarMenuItem>(node); }
    catch { return node.AddComponent<ModularAvatarMenuItem>(); }
  }

  /// <summary>
  /// 准备子根节点（会删除已存在的同名节点）
  /// </summary>
  public static GameObject PrepareChildRoot(GameObject parent, string name)
  {
    var existing = FindDirectChild(parent.transform, name);
    if (existing != null)
    {
      // ACC_Menu 是稳定根节点；重新生成时复用它，保留菜单图标等展示属性。
      Undo.RecordObject(existing.gameObject, "Reuse ACC menu root");
      return existing.gameObject;
    }

    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Root Child");
    go.transform.SetParent(parent.transform, false);
    go.transform.SetAsFirstSibling();
    return go;
  }

  /// <summary>清除 ACC_Menu 的旧生成子项，随后由生成器直接在 ACC_Menu 下重建菜单树。</summary>
  public static void ClearMenuChildren(GameObject menuRoot)
  {
    for (int i = menuRoot.transform.childCount - 1; i >= 0; i--)
      Undo.DestroyObjectImmediate(menuRoot.transform.GetChild(i).gameObject);
  }

  /// <summary>
  /// 重建控制逻辑前保存 ACC 子菜单的展示数据。快照不包含 Parameter、Value、子参数、
  /// 菜单来源或同步策略，因此不会把旧控制逻辑带入下一次生成。
  /// </summary>
  public static MenuPresentationSnapshot CaptureMenuPresentation(
    GameObject menuRoot,
    ACCConfig config = null,
    int? customMixerValue = null)
  {
    var snapshot = new MenuPresentationSnapshot(menuRoot);
    if (menuRoot == null) return snapshot;

    foreach (var item in menuRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
    {
      if (item.gameObject == menuRoot) continue;
      snapshot.Add(item);
    }
    snapshot.AddGeneratedSemanticKeys(config, customMixerValue);
    return snapshot;
  }

  /// <summary>
  /// 恢复用户设置的图标与自定义标签。自动模式保持空 Label，由 MA 直接显示节点对象名；
  /// 空标签或与默认名相同的标签视为自动模式，会跟随当前对象/部件名称更新。
  /// </summary>
  public static void ApplyMenuPresentation(
    MenuPresentationSnapshot snapshot,
    GameObject node,
    ModularAvatarMenuItem menuItem,
    string defaultLabel,
    string semanticKey = null)
  {
    if (menuItem == null) return;
    bool isAutomaticObjectName = string.IsNullOrEmpty(defaultLabel);
    string generatedName = isAutomaticObjectName ? node.name : defaultLabel;
    var saved = snapshot?.Find(node, menuItem, semanticKey);
    if (saved == null)
    {
      menuItem.label = isAutomaticObjectName ? "" : generatedName;
      return;
    }

    menuItem.PortableControl.Icon = saved.Icon;
    menuItem.label = IsCustomMenuLabel(saved.Label, saved.NodeName, generatedName)
      ? saved.Label
      : isAutomaticObjectName ? "" : generatedName;
  }

  private static bool IsCustomMenuLabel(string label, string nodeName, string defaultLabel)
  {
    return !string.IsNullOrEmpty(label) &&
      !IsLegacyGeneratedAutomaticLabel(label, nodeName) &&
      !string.Equals(label, nodeName, StringComparison.Ordinal) &&
      !string.Equals(label, defaultLabel, StringComparison.Ordinal);
  }

  private static bool IsLegacyGeneratedAutomaticLabel(string label, string nodeName)
  {
    if (IsKnownPartsMenuName(nodeName))
      return label == "Parts" || label == "部件";
    if (IsKnownEnableMenuName(nodeName))
      return label == "Enable" || label == "启用";
    if (IsKnownDefaultMixerName(nodeName))
      return label == "CustomMixer" || label == "Custom Mix" || label == "混搭";
    return false;
  }

  /// <summary>
  /// ACC 菜单展示属性快照。生成语义键优先处理本地化节点，Parameter + Value 用于
  /// Toggle，路径只作为结构未变化时的最后兜底。
  /// </summary>
  public sealed class MenuPresentationSnapshot
  {
    private readonly GameObject menuRoot;
    private readonly Dictionary<string, MenuPresentation> bySemanticKey = new();
    private readonly Dictionary<string, MenuPresentation> byPath = new();
    private readonly Dictionary<string, MenuPresentation> byControl = new();

    internal MenuPresentationSnapshot(GameObject menuRoot)
    {
      this.menuRoot = menuRoot;
    }

    internal void Add(ModularAvatarMenuItem item)
    {
      var presentation = new MenuPresentation
      {
        NodeName = item.gameObject.name,
        Label = item.label,
        Icon = item.PortableControl.Icon,
        ControlKey = GetControlKey(item)
      };
      byPath[GetStableMenuPath(menuRoot.transform, item.transform)] = presentation;
      if (!string.IsNullOrEmpty(presentation.ControlKey))
        byControl[presentation.ControlKey] = presentation;
    }

    internal void AddGeneratedSemanticKeys(ACCConfig config, int? customMixerValue)
    {
      if (menuRoot == null || config == null) return;

      foreach (var item in menuRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
      {
        if (item.gameObject == menuRoot) continue;
        var presentation = FindByPath(item.transform);
        if (presentation == null) continue;

        if (item.PortableControl.Type == PortableControlType.SubMenu &&
            IsKnownPartsMenuName(item.gameObject.name) && item.transform.parent != menuRoot.transform)
        {
          bySemanticKey[GetPartsMenuSemanticKey(menuRoot, item.transform.parent.gameObject)] =
            presentation;
        }
      }

      var mixerRoot = FindMixerRoot(config, customMixerValue);
      if (mixerRoot == null) return;

      var rootPresentation = FindByPath(mixerRoot.transform);
      if (rootPresentation != null)
        bySemanticKey[MixerRootSemanticKey] = rootPresentation;

      foreach (var item in mixerRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
      {
        if (item.gameObject == mixerRoot) continue;
        var presentation = FindByPath(item.transform);
        if (presentation == null) continue;
        bySemanticKey[MixerPathSemanticKeyPrefix +
          GetStableMenuPath(mixerRoot.transform, item.transform)] = presentation;
      }
    }

    internal MenuPresentation Find(
      GameObject node,
      ModularAvatarMenuItem item,
      string semanticKey = null)
    {
      if (node == null || menuRoot == null) return null;
      if (!string.IsNullOrEmpty(semanticKey) &&
          bySemanticKey.TryGetValue(semanticKey, out var bySemanticMatch))
        return bySemanticMatch;

      string controlKey = GetControlKey(item);
      if (!string.IsNullOrEmpty(controlKey) && byControl.TryGetValue(controlKey, out var byControlMatch))
        return byControlMatch;

      return byPath.TryGetValue(GetStableMenuPath(menuRoot.transform, node.transform), out var byPathMatch)
        ? byPathMatch
        : null;
    }

    private MenuPresentation FindByPath(Transform node)
    {
      return byPath.TryGetValue(GetStableMenuPath(menuRoot.transform, node), out var presentation)
        ? presentation
        : null;
    }

    private GameObject FindMixerRoot(ACCConfig config, int? customMixerValue)
    {
      foreach (Transform child in menuRoot.transform)
      {
        var item = child.GetComponent<ModularAvatarMenuItem>();
        if (item == null || item.PortableControl.Type != PortableControlType.SubMenu) continue;

        if (!string.IsNullOrWhiteSpace(config.CustomMixerName) &&
            child.name == config.CustomMixerName.Trim()) return child.gameObject;
        if (IsKnownDefaultMixerName(child.name)) return child.gameObject;

        if (customMixerValue.HasValue && child.GetComponentsInChildren<ModularAvatarMenuItem>(true)
          .Any(candidate => candidate != item &&
            candidate.PortableControl.Parameter == config.MainParameterName &&
            Mathf.Approximately(candidate.PortableControl.Value, customMixerValue.Value)))
          return child.gameObject;
      }
      return null;
    }
  }

  internal sealed class MenuPresentation
  {
    public string NodeName;
    public string Label;
    public Texture2D Icon;
    public string ControlKey;
  }

  private static string GetStableMenuPath(Transform root, Transform node)
  {
    var segments = new List<string>();
    for (var current = node; current != null && current != root; current = current.parent)
      segments.Add(current.name.Length + ":" + current.name);
    segments.Reverse();
    return string.Join("/", segments);
  }

  private static string GetStablePathFromSegments(IEnumerable<string> segments)
  {
    return string.Join("/", segments.Select(segment => segment.Length + ":" + segment));
  }

  public const string MixerRootSemanticKey = "acc:mixer-root";
  public const string MixerPathSemanticKeyPrefix = "acc:mixer-path|";

  public static string GetMixerPathSemanticKey(params string[] pathsOrSegments)
  {
    var segments = pathsOrSegments
      .Where(path => !string.IsNullOrEmpty(path))
      .SelectMany(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries))
      .Select(segment => segment.Trim());
    return MixerPathSemanticKeyPrefix + GetStablePathFromSegments(segments);
  }

  private static bool IsKnownPartsMenuName(string name)
  {
    return name == "Parts" || name == "部件" ||
      name.StartsWith("Parts (", StringComparison.Ordinal) ||
      name.StartsWith("部件 (", StringComparison.Ordinal);
  }

  private static bool IsKnownEnableMenuName(string name)
  {
    return name == "Enable" || name == "启用" ||
      name.StartsWith("Enable (", StringComparison.Ordinal) ||
      name.StartsWith("启用 (", StringComparison.Ordinal);
  }

  private static bool IsKnownDefaultMixerName(string name)
  {
    return name == "CustomMixer" || name == "Custom Mix" || name == "混搭" ||
      name.StartsWith("Custom Mix (", StringComparison.Ordinal) ||
      name.StartsWith("混搭 (", StringComparison.Ordinal);
  }

  private static string GetControlKey(ModularAvatarMenuItem item)
  {
    if (item == null || string.IsNullOrWhiteSpace(item.PortableControl.Parameter)) return "";
    return item.PortableControl.Type + "|" + item.PortableControl.Parameter + "|" +
      item.PortableControl.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
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
  /// 迁移 0.3.16 错误创建的内部节点。新结构直接使用 ACC_Menu，
  /// 此方法只在检测到旧节点时执行，迁移后会删除它们。
  /// </summary>
  public static void MigrateLegacyGeneratedNodes(GameObject costumesRoot)
  {
    const string legacyMenuContentName = "__ACC_Generated_Menu";
    const string legacyParametersName = "__ACC_Generated_Parameters";
    if (costumesRoot == null) return;

    var menuRoot = FindDirectChild(costumesRoot.transform, ACCConfig.MenuObjectName);
    if (menuRoot == null) return;

    var destination = EnsureParametersComponent(menuRoot.gameObject);
    var controllerParameterNames = GetAnimatorParameterNames(
      menuRoot.GetComponent<ModularAvatarMergeAnimator>());
    var legacyRootParameters = costumesRoot.GetComponent<ModularAvatarParameters>();
    if (legacyRootParameters != null && controllerParameterNames.Count > 0)
    {
      MoveParameterDeclarations(legacyRootParameters, destination, controllerParameterNames);
      if (legacyRootParameters.parameters.Count == 0)
        Undo.DestroyObjectImmediate(legacyRootParameters);
    }

    var legacyMenuContent = FindDirectChild(menuRoot, legacyMenuContentName);
    if (legacyMenuContent != null)
    {
      while (legacyMenuContent.childCount > 0)
        Undo.SetTransformParent(legacyMenuContent.GetChild(0), menuRoot,
          "Migrate ACC menu content");
      Undo.DestroyObjectImmediate(legacyMenuContent.gameObject);
      EnsureSubmenuOnNode(menuRoot.gameObject);
    }

    var legacyParametersRoot = FindDirectChild(costumesRoot.transform, legacyParametersName);
    if (legacyParametersRoot == null) return;

    var legacyParameters = legacyParametersRoot.GetComponent<ModularAvatarParameters>();
    if (legacyParameters != null && legacyParameters.parameters != null)
      MoveParameterDeclarations(legacyParameters, destination,
        legacyParameters.parameters.Select(parameter => parameter.nameOrPrefix));
    Undo.DestroyObjectImmediate(legacyParametersRoot.gameObject);
  }

  private static void MoveParameterDeclarations(
    ModularAvatarParameters source,
    ModularAvatarParameters destination,
    IEnumerable<string> names)
  {
    if (source == null || destination == null) return;
    var nameSet = new HashSet<string>(names ?? Enumerable.Empty<string>());
    if (nameSet.Count == 0) return;

    var knownDeclarations = new HashSet<string>(destination.parameters.Select(parameter =>
      parameter.nameOrPrefix + (parameter.isPrefix ? "*" : "")));
    var additions = source.parameters.Where(parameter =>
      !parameter.isPrefix && nameSet.Contains(parameter.nameOrPrefix) &&
      knownDeclarations.Add(parameter.nameOrPrefix)).ToList();
    if (additions.Count > 0)
    {
      Undo.RecordObject(destination, "Migrate ACC parameter declarations");
      destination.parameters.AddRange(additions);
    }

    Undo.RecordObject(source, "Remove migrated ACC parameter declarations");
    source.parameters.RemoveAll(parameter =>
      !parameter.isPrefix && nameSet.Contains(parameter.nameOrPrefix));
  }

  /// <summary>提取旧 ACC Controller 声明过的参数，以便精确清理旧生成声明。</summary>
  public static HashSet<string> GetAnimatorParameterNames(ModularAvatarMergeAnimator mergeAnimator)
  {
    var names = new HashSet<string>();
    if (mergeAnimator?.animator is not AnimatorController controller) return names;
    foreach (var parameter in controller.parameters)
    {
      // IsLocal 是 VRChat 内建参数，绝不应删除用户可能显式配置的声明。
      if (!string.IsNullOrWhiteSpace(parameter.name) && parameter.name != "IsLocal")
        names.Add(parameter.name);
    }
    return names;
  }

  /// <summary>删除已确定属于 ACC 的精确参数声明，保留前缀和无关用户声明。</summary>
  public static void RemoveParameterDeclarations(
    ModularAvatarParameters parameters,
    IEnumerable<string> names)
  {
    if (parameters == null) return;
    var nameSet = new HashSet<string>(names ?? Enumerable.Empty<string>());
    if (nameSet.Count == 0) return;

    Undo.RecordObject(parameters, "Remove ACC parameter declarations");
    parameters.parameters.RemoveAll(parameter =>
      !parameter.isPrefix && nameSet.Contains(parameter.nameOrPrefix));
  }

  /// <summary>统计已识别但尚未配置 MA Merge Armature 的服装 Armature。</summary>
  public static int CountMissingMergeArmatures(IEnumerable<OutfitData> outfits)
  {
    return outfits
      .Where(outfit => outfit?.ArmatureObject != null)
      .Select(outfit => outfit.ArmatureObject)
      .Distinct()
      .Count(armature => armature.GetComponent<ModularAvatarMergeArmature>() == null);
  }

  /// <summary>
  /// 对旧骨架识别结果优先调用 MA Setup Outfit。若 MA 无法处理当前 Outfit 或未实际
  /// 生成 Merge Armature，才使用与当前 MA 默认配置等价的最小兜底。
  /// </summary>
  public static int EnsureMergeArmaturesForOutfits(IEnumerable<OutfitData> outfits)
  {
    int configured = 0;
    foreach (var outfit in outfits
      .Where(outfit => outfit?.ArmatureObject != null)
      .GroupBy(outfit => outfit.ArmatureObject)
      .Select(group => group.First()))
    {
      if (outfit.ArmatureObject.GetComponent<ModularAvatarMergeArmature>() != null) continue;

      Exception setupException = null;
      try
      {
        SetupOutfit.SetupOutfitUI(outfit.BaseObject);
      }
      catch (Exception exception)
      {
        setupException = exception;
      }

      if (outfit.ArmatureObject.GetComponent<ModularAvatarMergeArmature>() == null)
      {
        if (!TryConfigureMergeArmatureFallback(outfit, out var fallbackError))
        {
          string setupDetail = setupException != null
            ? $" MA Setup Outfit exception: {setupException.Message}"
            : " MA Setup Outfit did not add MA Merge Armature.";
          throw new InvalidOperationException(
            $"Unable to configure MA Merge Armature for '{outfit.BaseObject.name}'." +
            setupDetail + " Fallback error: " + fallbackError);
        }

        Debug.LogWarning($"[ACC] MA Setup Outfit could not configure '{outfit.BaseObject.name}'; " +
                         "used the ACC Merge Armature fallback instead." +
                         (setupException != null ? " " + setupException.Message : ""));
      }
      configured++;
    }
    return configured;
  }

  private static bool TryConfigureMergeArmatureFallback(
    OutfitData outfit,
    out string error)
  {
    error = "";
    if (outfit?.BaseObject == null || outfit.ArmatureObject == null)
    {
      error = "Outfit Base or Armature is missing.";
      return false;
    }

    var descriptor = outfit.BaseObject.GetComponentInParent<VRCAvatarDescriptor>();
    var animator = descriptor != null ? descriptor.GetComponent<Animator>() : null;
    var hips = animator != null && animator.isHuman
      ? animator.GetBoneTransform(HumanBodyBones.Hips)
      : null;
    if (hips?.parent == null)
    {
      error = "Avatar Humanoid Hips parent Armature was not found.";
      return false;
    }

    try
    {
      ModularAvatarMergeArmature mergeArmature;
      try { mergeArmature = Undo.AddComponent<ModularAvatarMergeArmature>(outfit.ArmatureObject); }
      catch { mergeArmature = outfit.ArmatureObject.AddComponent<ModularAvatarMergeArmature>(); }

      mergeArmature.mergeTarget = new AvatarObjectReference();
      mergeArmature.mergeTarget.Set(hips.parent.gameObject);
      mergeArmature.LockMode = ArmatureLockMode.BaseToMerge;
      mergeArmature.mangleNames = true;
      mergeArmature.InferPrefixSuffix();
      EditorUtility.SetDirty(mergeArmature);
      PrefabUtility.RecordPrefabInstancePropertyModifications(mergeArmature);
      return true;
    }
    catch (Exception exception)
    {
      error = exception.Message;
      return false;
    }
  }

  /// <summary>确保菜单根节点可由 Modular Avatar 安装到 Avatar Expressions Menu。
  /// 若自身或任一父节点已有 Installer，则跳过创建。</summary>
  public static ModularAvatarMenuInstaller EnsureMenuInstaller(GameObject host)
  {
    // 已有自身 Installer，复用
    var installer = host.GetComponent<ModularAvatarMenuInstaller>();
    if (installer != null) return installer;

    // 父路径上已有 Installer，无需再挂载
    if (host.GetComponentInParent<ModularAvatarMenuInstaller>() != null) return null;

    try
    {
      installer = Undo.AddComponent<ModularAvatarMenuInstaller>(host);
      return installer;
    }
    catch
    {
      return host.AddComponent<ModularAvatarMenuInstaller>();
    }
  }

  /// <summary>
  /// 添加或更新参数
  /// </summary>
  public static void AddOrUpdateParameter(ModularAvatarParameters maParams, string paramName,
    ParameterSyncType syncType, float defaultValue, bool saved, bool localOnly = false)
  {
    var existingIndex = maParams.parameters.FindIndex(p => p.nameOrPrefix == paramName);
    if (existingIndex >= 0)
    {
      Undo.RecordObject(maParams, "Update parameter");
      var existing = maParams.parameters[existingIndex];
      existing.syncType = syncType;
      existing.defaultValue = defaultValue;
      existing.hasExplicitDefaultValue = true;
      existing.saved = saved;
      existing.localOnly = localOnly;
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
      localOnly = localOnly,
      defaultValue = defaultValue,
      saved = saved,
      hasExplicitDefaultValue = true
    });
  }

  /// <summary>移除由指定选择参数布局留下的旧参数，避免重新生成后残留占用。</summary>
  public static void RemoveChoiceParameters(ModularAvatarParameters maParams, string baseParameterName)
  {
    string bitsPrefix = baseParameterName + "/Bits/";
    string selectionPrefix = baseParameterName + "/Selection/";
    Undo.RecordObject(maParams, "Remove obsolete choice parameters");
    maParams.parameters.RemoveAll(parameter =>
      parameter.nameOrPrefix == baseParameterName ||
      parameter.nameOrPrefix.StartsWith(bitsPrefix, StringComparison.Ordinal) ||
      parameter.nameOrPrefix.StartsWith(selectionPrefix, StringComparison.Ordinal));
  }
}
