using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

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
}
