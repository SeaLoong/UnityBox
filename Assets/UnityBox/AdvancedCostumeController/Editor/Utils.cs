using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
  /// 选中并定位场景对象。用于 ACC 预览中的对象链接，不改变对象或生成配置。
  /// </summary>
  public static void SelectAndPingObject(GameObject target)
  {
    if (target == null) return;

    Selection.objects = new UnityEngine.Object[] { target };
    EditorGUIUtility.PingObject(target);
    var sceneView = SceneView.lastActiveSceneView ??
      SceneView.sceneViews.OfType<SceneView>().FirstOrDefault();
    if (sceneView == null) return;

    sceneView.Focus();
    if (TryGetObjectBounds(target, out var bounds))
      sceneView.Frame(bounds, false);
    else
      sceneView.FrameSelected();
  }

  /// <summary>获取对象及其子层级 Renderer 的世界空间包围盒。</summary>
  public static bool TryGetObjectBounds(GameObject target, out Bounds bounds)
  {
    bounds = default;
    if (target == null) return false;

    bool hasBounds = false;
    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
    {
      if (renderer == null) continue;
      if (!hasBounds)
      {
        bounds = renderer.bounds;
        hasBounds = true;
      }
      else
      {
        bounds.Encapsulate(renderer.bounds);
      }
    }
    if (hasBounds) return true;

    var transforms = target.GetComponentsInChildren<Transform>(true);
    if (transforms.Length == 0) return false;

    bounds = new Bounds(transforms[0].position, Vector3.zero);
    for (int i = 1; i < transforms.Length; i++)
      bounds.Encapsulate(transforms[i].position);
    bounds.Expand(Mathf.Max(bounds.size.magnitude * 0.05f, 0.1f));
    return true;
  }

  public static bool HasMeshOn(Transform target) => CostumeHierarchy.HasMeshOn(target);
  public static bool HasMeshInHierarchy(Transform root) => CostumeHierarchy.HasMeshInHierarchy(root);
  public static bool OwnsSkeleton(Transform root) => CostumeHierarchy.OwnsSkeleton(root);
  public static bool TryGetOwnedArmature(Transform root, out Transform armature)
    => CostumeHierarchy.TryGetOwnedArmature(root, out armature);
  public static bool TryGetOwnedMergeArmature(Transform root, out ModularAvatarMergeArmature armature)
    => ModularAvatarCostumeScanIntegration.TryGetOwnedMergeArmature(root, out armature);
  public static bool IsSkeletonNode(Transform node, Transform outfitRoot)
    => CostumeHierarchy.IsSkeletonNode(node, outfitRoot);

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

    return EnsureSubmenuPathSegments(parent,
      relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Trim()), presentation, semanticKeyPrefix);
  }

  /// <summary>按已经分解的层级名称创建菜单路径，不把对象名称中的 '/' 当作分隔符。</summary>
  public static GameObject EnsureSubmenuPathSegments(
    GameObject parent,
    IEnumerable<string> pathSegments,
    MenuPresentationSnapshot presentation = null,
    string semanticKeyPrefix = null)
  {
    if (pathSegments == null) return parent;

    var current = parent;
    var segments = new List<string>();
    foreach (var part in pathSegments)
    {
      if (string.IsNullOrEmpty(part)) continue;

      segments.Add(part);
      current = FindOrCreateSubmenuChild(current, part);
      string semanticKey = string.IsNullOrEmpty(semanticKeyPrefix)
        ? null
        : semanticKeyPrefix + GetStablePathFromSegments(segments);
      EnsureSubmenuOnNode(current, presentation: presentation, semanticKey: semanticKey);
    }
    return current;
  }

  /// <summary>
  /// 查找或创建路径中的菜单目录节点。
  ///
  /// 服装菜单可能先生成一个扁平 Toggle（例如没有部件且只有一个可选对象的
  /// 服装），而另一个服装的 OutfitObject 路径随后又把同名节点当作父目录。
  /// 不能把前者原地改成 SubMenu，否则后者会看起来被放进前一个服装菜单中。
  /// 遇到非目录菜单项时创建同级唯一目录，保留原菜单项的语义。
  /// </summary>
  private static GameObject FindOrCreateSubmenuChild(GameObject parent, string name)
  {
    var existing = FindDirectChild(parent.transform, name);
    if (existing == null) return FindOrCreateChild(parent, name);

    var menuItem = existing.GetComponent<ModularAvatarMenuItem>();
    if (menuItem == null || menuItem.PortableControl.Type == PortableControlType.SubMenu)
      return existing.gameObject;

    for (int suffix = 2; ; suffix++)
    {
      string candidateName = name + " (" + suffix + ")";
      var candidate = FindDirectChild(parent.transform, candidateName);
      if (candidate == null)
        return FindOrCreateChild(parent, candidateName);

      var candidateMenuItem = candidate.GetComponent<ModularAvatarMenuItem>();
      if (candidateMenuItem == null ||
          candidateMenuItem.PortableControl.Type == PortableControlType.SubMenu)
        return candidate.gameObject;
    }
  }

  /// <summary>用于跨语言恢复 Parts 子菜单展示属性的稳定语义键。</summary>
  public static string GetPartsMenuSemanticKey(GameObject menuRoot, GameObject outfitSubmenu)
  {
    return "acc:parts|" + GetStableMenuPath(menuRoot.transform, outfitSubmenu.transform);
  }

  public static List<string> GetRelativePathSegments(GameObject root, GameObject node)
    => CostumeHierarchy.GetRelativePathSegments(root, node);
  public static string GetRelativePath(GameObject root, GameObject node)
    => CostumeHierarchy.GetRelativePath(root, node);
  public static string GetHierarchyPath(GameObject root, GameObject node)
    => CostumeHierarchy.GetHierarchyPath(root, node);

  // Keep existing callers on the same rules as the SDK-independent core.
  public static string Sanitize(string value) => CostumeGenerationCore.Sanitize(value);
  public static string SanitizeForFileName(string value) => CostumeGenerationCore.SanitizeForFileName(value);
  public static string GetStableAssetName(string value) => CostumeGenerationCore.GetStableAssetName(value);
  public static bool HasUsableGenerationNamespace(string value) => CostumeGenerationCore.HasUsableGenerationNamespace(value);
  public static bool IsSafeAssetsFolder(string path) => CostumeGenerationCore.IsSafeAssetsFolder(path);
  public static string NormalizeAssetsFolder(string path) => CostumeGenerationCore.NormalizeAssetsFolder(path);
  public static string CombineAssetPath(params string[] parts) => CostumeGenerationCore.CombineAssetPath(parts);
  public static string BuildParamName(string prefix, string path) => CostumeGenerationCore.BuildParamName(prefix, path);
  public static string FormatPartDisplayName(string source, string prefix, string suffix, string pattern, string replacement)
    => CostumeGenerationCore.FormatPartDisplayName(source, prefix, suffix, pattern, replacement);
  public static bool IsValidRegex(string pattern) => CostumeGenerationCore.IsValidRegex(pattern);

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
  /// Assigns an ACC-provided menu icon only when the menu item has no icon yet.
  /// Once assigned, the reference is intentionally left untouched so a user can
  /// replace the preset without the next generation overwriting it.
  /// </summary>
  public static void EnsureDefaultMenuIcon(GameObject node, string resourceName)
  {
    if (node == null) return;
    var menuItem = node.GetComponent<ModularAvatarMenuItem>();
    if (menuItem == null || HasUsableMenuIcon(menuItem)) return;

    var icon = Resources.Load<Texture2D>(resourceName);
    if (icon == null)
    {
      Debug.LogWarning($"[ACC] Default menu icon resource not found: {resourceName}");
      return;
    }

    Undo.RecordObject(menuItem, "Assign ACC default menu icon");
    menuItem.PortableControl.Icon = icon;
    EditorUtility.SetDirty(menuItem);
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { menuItem });
  }

  /// <summary>
  /// Checks both the managed reference and its serialized object reference.
  /// Unity can retain a non-null-looking wrapper for a deleted asset, which is
  /// displayed as Missing in the Inspector and must be treated as absent.
  /// </summary>
  public static bool HasUsableMenuIcon(ModularAvatarMenuItem menuItem)
  {
    if (menuItem == null || menuItem.PortableControl.Icon == null) return false;

    try
    {
      var serializedObject = new SerializedObject(menuItem);
      var property = serializedObject.GetIterator();
      bool enterChildren = true;
      while (property.NextVisible(enterChildren))
      {
        enterChildren = false;
        if (property.propertyType != SerializedPropertyType.ObjectReference ||
            !string.Equals(property.name, "icon", StringComparison.OrdinalIgnoreCase))
          continue;
        return property.objectReferenceValue != null;
      }
    }
    catch (Exception)
    {
      // The managed Unity reference is still the best fallback if the object
      // cannot be inspected during an assembly or serialization transition.
    }
    return true;
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

    return ACCEditorUndo.AddComponent<ModularAvatarMenuItem>(node,
      "Create ACC menu item");
  }

  /// <summary>
  /// Resets submenu-only fields when an old generated folder node is reused as
  /// a Toggle item. Without this, a stale MenuSource/VRChatSubMenu can make a
  /// regenerated part control continue behaving like a folder.
  /// </summary>
  public static void ConfigureAsToggle(ModularAvatarMenuItem menuItem)
  {
    if (menuItem == null) return;
    if (menuItem.PortableControl.Type == PortableControlType.SubMenu ||
        menuItem.transform.childCount > 0)
      ClearMenuChildren(menuItem.gameObject);
    menuItem.PortableControl.Type = PortableControlType.Toggle;
    menuItem.PortableControl.SubParameters = ImmutableList<string>.Empty;
    menuItem.PortableControl.VRChatSubMenu = null;
    menuItem.MenuSource = default(SubmenuSource);
    menuItem.menuSource_otherObjectChildren = null;
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { menuItem });
  }

  /// <summary>
  /// 准备子根节点（复用或创建同名节点）
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
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { menuItem });
      return;
    }

    menuItem.PortableControl.Icon = saved.Icon;
    menuItem.label = IsCustomMenuLabel(saved.Label, saved.NodeName, generatedName)
      ? saved.Label
      : isAutomaticObjectName ? "" : generatedName;
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { menuItem });
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

        if (item.PortableControl.Type == PortableControlType.SubMenu)
        {
          bySemanticKey[OutfitPathSemanticKeyPrefix +
            GetStableMenuPath(menuRoot.transform, item.transform)] = presentation;
        }

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
  public const string OutfitPathSemanticKeyPrefix = "acc:outfit-path|";

  public static string GetMixerPathSemanticKey(params string[] pathsOrSegments)
  {
    var segments = pathsOrSegments
      .Where(path => !string.IsNullOrEmpty(path))
      .SelectMany(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries))
      .Select(segment => segment.Trim());
    return MixerPathSemanticKeyPrefix + GetStablePathFromSegments(segments);
  }

  /// <summary>为已分解的 Mixer 菜单层级生成稳定语义键。</summary>
  public static string GetMixerPathSemanticKeySegments(IEnumerable<string> segments)
  {
    return MixerPathSemanticKeyPrefix + GetStablePathFromSegments(
      (segments ?? Enumerable.Empty<string>()).Where(segment => !string.IsNullOrEmpty(segment)));
  }

  private static bool IsKnownPartsMenuName(string name)
  {
    return name == "Parts" || name == "部件" ||
      name == "Shared Parts" || name == "共享部件" ||
      name.StartsWith("Parts (", StringComparison.Ordinal) ||
      name.StartsWith("部件 (", StringComparison.Ordinal) ||
      name.StartsWith("Shared Parts (", StringComparison.Ordinal) ||
      name.StartsWith("共享部件 (", StringComparison.Ordinal);
  }

  private static bool IsKnownEnableMenuName(string name)
  {
    return name == "Enable" || name == "启用" ||
      name.StartsWith("Enable (", StringComparison.Ordinal) ||
      name.StartsWith("启用 (", StringComparison.Ordinal) ||
      name.StartsWith("Enable ", StringComparison.Ordinal) ||
      name.StartsWith("启用", StringComparison.Ordinal);
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
      comp = ACCEditorUndo.AddComponent<ModularAvatarParameters>(host,
        "Create ACC parameters");

    if (comp.parameters == null)
    {
      Undo.RecordObject(comp, "Init parameters list");
      comp.parameters = new List<ParameterConfig>();
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { comp });
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
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { destination });
    }

    Undo.RecordObject(source, "Remove migrated ACC parameter declarations");
    source.parameters.RemoveAll(parameter =>
      !parameter.isPrefix && nameSet.Contains(parameter.nameOrPrefix));
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { source });
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
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { parameters });
  }

  /// <summary>统计已识别但尚未配置 MA Merge Armature 的服装 Armature。</summary>
  public static int CountMissingMergeArmatures(IEnumerable<OutfitData> outfits)
  {
    return outfits
      .Where(outfit => outfit?.ArmatureObject != null)
      .Select(outfit => outfit.ArmatureObject)
      .Distinct()
      .Count(armature => !armature
        .GetComponentsInChildren<ModularAvatarMergeArmature>(true).Any());
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
      if (outfit.ArmatureObject
        .GetComponentsInChildren<ModularAvatarMergeArmature>(true).Any()) continue;

      Exception setupException = null;
      try
      {
        SetupOutfit.SetupOutfitUI(outfit.BaseObject);
      }
      catch (Exception exception)
      {
        setupException = exception;
      }

      if (!outfit.ArmatureObject
        .GetComponentsInChildren<ModularAvatarMergeArmature>(true).Any())
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
      mergeArmature = ACCEditorUndo.AddComponent<ModularAvatarMergeArmature>(
        outfit.ArmatureObject, "Create ACC merge armature");

      mergeArmature.mergeTarget = new AvatarObjectReference();
      mergeArmature.mergeTarget.Set(hips.parent.gameObject);
      mergeArmature.LockMode = ArmatureLockMode.BaseToMerge;
      mergeArmature.mangleNames = true;
      mergeArmature.InferPrefixSuffix();
      EditorUtility.SetDirty(mergeArmature);
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { mergeArmature });
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

    return ACCEditorUndo.AddComponent<ModularAvatarMenuInstaller>(host,
      "Create ACC menu installer");
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
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { maParams });
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
    ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { maParams });
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
