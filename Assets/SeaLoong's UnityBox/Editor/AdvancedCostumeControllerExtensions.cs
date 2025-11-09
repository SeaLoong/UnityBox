using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

public static class AdvancedCostumeControllerExtensions
{
  public static Dictionary<GameObject, List<GameObject>> CreateCustomMixerMenu(
    GameObject costumesRoot,
    GameObject menuRoot,
    string customMixerName,
    string paramPrefix,
    string costumeParamName,
    Dictionary<GameObject, List<GameObject>> outfitMap,
    Dictionary<Transform, List<GameObject>> outfitGroups,
    Dictionary<GameObject, int> outfitToIndex,
    int customMixerIndex,
    ModularAvatarParameters rootParams,
    GameObject defaultOutfit)
  {
    var customMixerSubmenu = FindOrCreateChild(menuRoot, customMixerName);
    EnsureSubmenuOnNode(customMixerSubmenu, customMixerName);

    // 添加 CustomMixer 总开关
    var enableNode = FindOrCreateChild(customMixerSubmenu, "Enable");
    var enableMi = CreateMenuItem(enableNode);
    Undo.RecordObject(enableMi, "Configure CustomMixer enable toggle");
    enableMi.PortableControl.Type = PortableControlType.Toggle;
    enableMi.PortableControl.Parameter = costumeParamName;
    enableMi.automaticValue = false;
    enableMi.PortableControl.Value = customMixerIndex;
    enableMi.isSaved = true;
    enableMi.isSynced = true;

    var allParts = new Dictionary<GameObject, List<GameObject>>();
    var customMixerParts = new List<GameObject>();
    var processedOutfits = new HashSet<GameObject>();

    foreach (var kvp in outfitMap)
    {
      var outfit = kvp.Key;
      var parts = kvp.Value;
      if (parts.Count == 0) continue;
      if (processedOutfits.Contains(outfit)) continue;

      // 获取变体组
      var parent = outfit.transform.parent;
      var variantOutfits = (parent != null && outfitGroups.ContainsKey(parent))
        ? outfitGroups[parent].Where(go => outfitMap.ContainsKey(go)).ToList()
        : new List<GameObject> { outfit };
      bool hasVariants = variantOutfits.Count > 1 && parent != null && parent.gameObject != costumesRoot;

      // 确定菜单路径
      string outfitRelPath;
      if (hasVariants)
      {
        outfitRelPath = GetRelativePath(costumesRoot, parent.gameObject);
      }
      else
      {
        outfitRelPath = GetRelativePath(costumesRoot, outfit);
      }

      var pathParts = outfitRelPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
      var curMenu = customMixerSubmenu;

      for (int i = 0; i < pathParts.Length; i++)
      {
        curMenu = FindOrCreateChild(curMenu, pathParts[i]);
        EnsureSubmenuOnNode(curMenu, pathParts[i]);
      }

      // 如果有变体,创建 Parts 子菜单,然后在同级添加变体开关
      if (hasVariants)
      {
        // 创建 Parts 子菜单
        if (parts.Count > 0)
        {
          var partsSubmenu = FindOrCreateChild(curMenu, "Parts");
          EnsureSubmenuOnNode(partsSubmenu);

          foreach (var part in parts)
          {
            string partRelPath = GetRelativePath(outfit, part);

            var partNode = FindOrCreateChild(partsSubmenu, part.name);
            var partMi = CreateMenuItem(partNode);
            Undo.RecordObject(partMi, "Configure CustomMixer part toggle");
            partMi.PortableControl.Type = PortableControlType.Toggle;
            // CustomMixer 使用独立参数,通过层权重控制避免冲突
            string partParamName = string.IsNullOrEmpty(paramPrefix)
              ? Sanitize(customMixerName + "/" + outfitRelPath + "/" + partRelPath)
              : paramPrefix + "/" + Sanitize(customMixerName + "/" + outfitRelPath + "/" + partRelPath);
            partMi.PortableControl.Parameter = partParamName;
            partMi.automaticValue = true;
            partMi.isDefault = false;
            partMi.isSaved = true;
            partMi.isSynced = true;

            if (!rootParams.parameters.Any(p => p.nameOrPrefix == partParamName))
            {
              Undo.RecordObject(rootParams, "Add CustomMixer part parameter");
              var pc = new ParameterConfig
              {
                nameOrPrefix = partParamName,
                remapTo = "",
                internalParameter = false,
                isPrefix = false,
                syncType = ParameterSyncType.Bool,
                localOnly = false,
                defaultValue = 0f,
                saved = true,
                hasExplicitDefaultValue = true
              };
              rootParams.parameters.Add(pc);
            }

            customMixerParts.Add(part);
          }
        }

        // 在同级添加变体开关(使用Int参数控制变体组层)
        string groupRelPath = GetRelativePath(costumesRoot, parent.gameObject);
        string variantGroupParamName = string.IsNullOrEmpty(paramPrefix)
          ? Sanitize(customMixerName + "/" + groupRelPath)
          : paramPrefix + "/" + Sanitize(customMixerName + "/" + groupRelPath);

        // 确保变体组参数存在
        if (!rootParams.parameters.Any(p => p.nameOrPrefix == variantGroupParamName))
        {
          Undo.RecordObject(rootParams, "Add CustomMixer variant group parameter");
          var pc = new ParameterConfig
          {
            nameOrPrefix = variantGroupParamName,
            remapTo = "",
            internalParameter = false,
            isPrefix = false,
            syncType = ParameterSyncType.Int,
            localOnly = false,
            defaultValue = 0f,
            saved = true,
            hasExplicitDefaultValue = true
          };
          rootParams.parameters.Add(pc);
        }

        for (int i = 0; i < variantOutfits.Count; i++)
        {
          var variant = variantOutfits[i];
          processedOutfits.Add(variant);
          var variantNode = FindOrCreateChild(curMenu, variant.name);
          var variantMi = CreateMenuItem(variantNode);
          Undo.RecordObject(variantMi, "Configure CustomMixer variant toggle");
          variantMi.PortableControl.Type = PortableControlType.Toggle;
          variantMi.PortableControl.Parameter = variantGroupParamName;
          variantMi.automaticValue = false;
          variantMi.PortableControl.Value = i;
          variantMi.isSaved = true;
          variantMi.isSynced = true;
        }
      }
      // 如果没有变体,直接在当前菜单下创建部件开关(不需要 Parts 子菜单)
      else if (parts.Count > 0)
      {
        processedOutfits.Add(outfit);

        foreach (var part in parts)
        {
          string partRelPath = GetRelativePath(outfit, part);

          var partNode = FindOrCreateChild(curMenu, part.name);
          var partMi = CreateMenuItem(partNode);
          Undo.RecordObject(partMi, "Configure CustomMixer part toggle");
          partMi.PortableControl.Type = PortableControlType.Toggle;
          // CustomMixer 使用独立参数,通过层权重控制避免冲突
          string partParamName = string.IsNullOrEmpty(paramPrefix)
            ? Sanitize(customMixerName + "/" + outfitRelPath + "/" + partRelPath)
            : paramPrefix + "/" + Sanitize(customMixerName + "/" + outfitRelPath + "/" + partRelPath);
          partMi.PortableControl.Parameter = partParamName;
          partMi.automaticValue = true;
          partMi.isDefault = false;
          partMi.isSaved = true;
          partMi.isSynced = true;

          if (!rootParams.parameters.Any(p => p.nameOrPrefix == partParamName))
          {
            Undo.RecordObject(rootParams, "Add CustomMixer part parameter");
            var pc = new ParameterConfig
            {
              nameOrPrefix = partParamName,
              remapTo = "",
              internalParameter = false,
              isPrefix = false,
              syncType = ParameterSyncType.Bool,
              localOnly = false,
              defaultValue = 0f,
              saved = true,
              hasExplicitDefaultValue = true
            };
            rootParams.parameters.Add(pc);
          }

          customMixerParts.Add(part);
        }
      }
    }

    if (customMixerParts.Count > 0)
    {
      allParts[customMixerSubmenu] = customMixerParts;
    }

    return allParts;
  }

  private static GameObject FindOrCreateChild(GameObject parent, string name)
  {
    var child = parent.transform.Find(name);
    if (child != null) return child.gameObject;
    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Node");
    go.transform.SetParent(parent.transform, false);
    return go;
  }

  private static void EnsureSubmenuOnNode(GameObject node, string label = "")
  {
    var old = node.GetComponent<ModularAvatarMenuItem>();
    if (old != null) Undo.DestroyObjectImmediate(old);
    var mi = CreateMenuItem(node);
    mi.label = label;
    mi.PortableControl.Type = PortableControlType.SubMenu;
    mi.automaticValue = true;
    mi.MenuSource = SubmenuSource.Children;
  }

  private static ModularAvatarMenuItem CreateMenuItem(GameObject node)
  {
    var existing = node.GetComponent<ModularAvatarMenuItem>();
    if (existing != null) Undo.DestroyObjectImmediate(existing);
    try { return Undo.AddComponent<ModularAvatarMenuItem>(node); }
    catch { return node.AddComponent<ModularAvatarMenuItem>(); }
  }

  private static string GetRelativePath(GameObject root, GameObject node)
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

  private static string Sanitize(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) || c == '/' ? c : '_').ToArray();
    return new string(arr);
  }
}
