using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

public class CostumeCustomMixer : EditorWindow
{
  private static readonly string[] DefaultOutfitHints = { "origin", "original", "default", "base", "vanilla", "standard", "normal" };
  private GameObject costumesRoot;
  private string customMixName = "CustomMix";
  private string paramPrefix = "CSTM_";
  private string costumeParamName = "costume";
  private string generatedFolder = "Assets/SeaLoong's UnityBox/Costume Custom Mixer/Generated";
  [SerializeField]
  private string ignoreNamesCsv = "Armature,Bones,Bone,Skeleton,Rig";
  private GameObject defaultOutfitOverride;
  [MenuItem("Tools/SeaLoong's UnityBox/Costume Custom Mixer")]
  public static void ShowWindow()
  {
    GetWindow<CostumeCustomMixer>("Costume Custom Mixer");
  }
  private void OnGUI()
  {
    EditorGUILayout.LabelField("Costume Custom Mixer", EditorStyles.boldLabel);
    costumesRoot = (GameObject)EditorGUILayout.ObjectField("Costumes Root", costumesRoot, typeof(GameObject), true);
    customMixName = EditorGUILayout.TextField("CustomMix Node Name", customMixName);
    paramPrefix = EditorGUILayout.TextField("Parameter Prefix", paramPrefix);

    EditorGUILayout.BeginHorizontal();
    generatedFolder = EditorGUILayout.TextField("Output Folder", generatedFolder);
    if (GUILayout.Button("Browse…", GUILayout.MaxWidth(80)))
    {
      var abs = EditorUtility.OpenFolderPanel("Select folder under Assets", Application.dataPath, "");
      if (!string.IsNullOrEmpty(abs))
      {
        var assetsAbs = Application.dataPath.Replace('\\', '/');
        abs = abs.Replace('\\', '/');
        if (abs.StartsWith(assetsAbs))
        {
          var rel = abs.Length == assetsAbs.Length ? "Assets" : ("Assets/" + abs.Substring(assetsAbs.Length + 1));
          generatedFolder = rel;
        }
        else
        {
          EditorUtility.DisplayDialog("Invalid Folder", "请选择 Assets 目录内的文件夹。", "OK");
        }
      }
    }
    EditorGUILayout.EndHorizontal();

    defaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField("Default Outfit (optional)", defaultOutfitOverride, typeof(GameObject), true);
    EditorGUILayout.HelpBox(
      "使用指南：\n" +
      "1) 选择 Costumes Root\n" +
      "2) 点击 Generate：重建 Menu、'" + customMixName + "' 子菜单与参数；生成 Reset 控制器并合并到 FX\n" +
      "3) Output Folder 可自定义生成文件输出目录\n" +
      "4) Default Outfit 可拖入默认服装节点\n" +
      "5) Ignore Names：结构匹配时忽略的直接子对象名称",
      MessageType.Info);

    EditorGUILayout.LabelField("Ignore Names (逗号/分号/换行分隔)");
    ignoreNamesCsv = EditorGUILayout.TextArea(ignoreNamesCsv, GUILayout.MinHeight(40));

    using (new EditorGUI.DisabledScope(costumesRoot == null))
    {
      if (GUILayout.Button("Generate"))
      {
        try
        {
          Generate();
        }
        catch (Exception ex)
        {
          Debug.LogError("CostumeCustomMixer generation failed: " + ex);
        }
      }
    }
  }
  private void Generate()
  {
    if (costumesRoot == null)
    {
      EditorUtility.DisplayDialog("未选择 Costumes Root", "请先指定 Costumes Root。", "确定");
      return;
    }

    if (!PreflightCheck(out var outfitMap)) return;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Generate Costume Custom Mix");
    void Progress(string info, float p) { EditorUtility.DisplayProgressBar("Costume Custom Mixer", info, Mathf.Clamp01(p)); }
    try
    {
      Progress("Initializing...", 0.05f);
      var menuRoot = PrepareChildRoot(costumesRoot, "Menu");
      if (menuRoot.GetComponent<ModularAvatarMenuInstaller>() == null)
      {
        try { Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot); }
        catch { menuRoot.AddComponent<ModularAvatarMenuInstaller>(); }
      }
      var rootParams = EnsureParametersComponent(menuRoot);
      var rootParamNames = new HashSet<string>(rootParams.parameters.Select(p => p.nameOrPrefix ?? ""));

      GameObject defaultOutfit = null;
      {
        var overrideOutfit = FindNearestOutfitParent(defaultOutfitOverride, outfitMap);
        if (overrideOutfit != null) defaultOutfit = overrideOutfit;

        if (defaultOutfit == null)
        {
          foreach (var outfitGo in outfitMap.Keys)
          {
            var rel = GetRelativePath(costumesRoot, outfitGo);
            string lower = (outfitGo.name + "/" + rel).ToLowerInvariant();
            if (DefaultOutfitHints.Any(h => lower.Contains(h))) { defaultOutfit = outfitGo; break; }
          }
        }
      }
      Debug.Log(defaultOutfit != null
        ? $"[CostumeCustomMixer] 默认服装：{defaultOutfit.name}"
        : "[CostumeCustomMixer] 默认服装未解析");

      if (!rootParamNames.Contains(costumeParamName))
      {
        Undo.RecordObject(rootParams, "Add costume parameter");
        var pcCostume = new ParameterConfig
        {
          nameOrPrefix = costumeParamName,
          remapTo = "",
          internalParameter = false,
          isPrefix = false,
          syncType = ParameterSyncType.Int,
          localOnly = false,
          defaultValue = 0f,
          saved = true,
          hasExplicitDefaultValue = false
        };
        rootParams.parameters.Add(pcCostume);
        rootParamNames.Add(costumeParamName);
      }

      var resetParam = BuildParamName("ResetCustomMix");
      {
        var resetNode = FindOrCreateChild(menuRoot, "Reset CustomMix");
        var resetItem = CreateMenuItem(resetNode);
        Undo.RecordObject(resetItem, "Configure Reset CustomMix button");
        resetItem.PortableControl.Type = PortableControlType.Button;
        resetItem.PortableControl.Parameter = resetParam;
        resetItem.automaticValue = true;
        resetItem.isSaved = false;
        resetItem.isSynced = false;
        if (!rootParamNames.Contains(resetParam))
        {
          Undo.RecordObject(rootParams, "Add reset parameter");
          var rp = new ParameterConfig
          {
            nameOrPrefix = resetParam,
            remapTo = "",
            internalParameter = false,
            isPrefix = false,
            syncType = ParameterSyncType.Bool,
            localOnly = false,
            defaultValue = 0f,
            saved = false,
            hasExplicitDefaultValue = false
          };
          rootParams.parameters.Add(rp);
          rootParamNames.Add(resetParam);
        }
      }

      {
        Progress("Building Enable toggle...", 0.15f);
        var enableNode = FindOrCreateChild(menuRoot, "Enable CustomMix");
        var enableItem = CreateMenuItem(enableNode);
        Undo.RecordObject(enableItem, "Configure Enable CustomMix toggle");
        enableItem.PortableControl.Type = PortableControlType.Toggle;
        enableItem.PortableControl.Parameter = costumeParamName;
        enableItem.automaticValue = true;
        enableItem.isSaved = true;
        enableItem.isSynced = true;
        var enableToggle = RecreateComponent<ModularAvatarObjectToggle>(enableNode);
        foreach (var pair in outfitMap)
        {
          var outfitGO = pair.Key;
          var meshes = pair.Value;
          AddObjectToToggle(enableToggle, outfitGO, true);
          foreach (var mesh in meshes) AddObjectToToggle(enableToggle, mesh, false);
        }
      }

      var customMixParamNames = new List<string>();
      {
        Progress("Building CustomMix tree and parameters...", 0.35f);
        var customMixMenu = FindOrCreateChild(menuRoot, customMixName);
        EnsureSubmenuOnNode(customMixMenu);
        var paramsComp = EnsureParametersComponent(menuRoot);
        var existingParams = new HashSet<string>(paramsComp.parameters.Select(p => p.nameOrPrefix ?? ""));
        foreach (var pair in outfitMap)
        {
          var outfitNode = pair.Key;
          string outfitRelPath2 = GetRelativePath(costumesRoot, outfitNode);
          var parts2 = outfitRelPath2.Split('/', StringSplitOptions.RemoveEmptyEntries);
          var cur = customMixMenu;
          foreach (var seg in parts2)
          {
            cur = EnsurePath(cur, seg);
            EnsureSubmenuOnNode(cur);
          }
          var meshNodes = pair.Value;
          foreach (var meshNode in meshNodes)
          {
            string partRelPath = GetRelativePath(outfitNode, meshNode);
            var controlNode = FindOrCreateChild(cur, meshNode.name);
            Debug.Log($"[CostumeCustomMixer] Mesh Path: {partRelPath}, Control Node: {controlNode.name}");
            string paramName = BuildParamName(customMixName + "/" + outfitRelPath2 + "/" + partRelPath);
            var menuItem = CreateMenuItem(controlNode);
            Undo.RecordObject(menuItem, "Configure CustomMix part toggle");
            menuItem.PortableControl.Type = PortableControlType.Toggle;
            menuItem.PortableControl.Parameter = paramName;
            menuItem.automaticValue = true;
            menuItem.isSaved = true;
            menuItem.isSynced = true;
            var toggle = RecreateComponent<ModularAvatarObjectToggle>(controlNode);
            AddObjectToToggle(toggle, meshNode);
            if (!existingParams.Contains(paramName))
            {
              Undo.RecordObject(paramsComp, "Add CustomMix parameter");
              var pc = new ParameterConfig
              {
                nameOrPrefix = paramName,
                remapTo = "",
                internalParameter = false,
                isPrefix = false,
                syncType = ParameterSyncType.Bool,
                localOnly = false,
                defaultValue = 0f,
                saved = true,
                hasExplicitDefaultValue = false
              };
              paramsComp.parameters.Add(pc);
              existingParams.Add(paramName);
            }
            customMixParamNames.Add(paramName);
          }
        }
      }

      Progress("Building outfit toggles...", 0.70f);
      foreach (var outfitGO in outfitMap.Keys)
      {
        string outfitRelPath = GetRelativePath(costumesRoot, outfitGO);
        var partsOutfit = outfitRelPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var curMenu = menuRoot;
        for (int i = 0; i < Mathf.Max(0, partsOutfit.Length - 1); i++)
        {
          curMenu = EnsurePath(curMenu, partsOutfit[i]);
          EnsureSubmenuOnNode(curMenu);
        }
        var outfitLeafName = partsOutfit.Length > 0 ? partsOutfit[partsOutfit.Length - 1] : outfitGO.name;
        var outfitLeaf = EnsurePath(curMenu, outfitLeafName);
        var mi = CreateMenuItem(outfitLeaf);
        Undo.RecordObject(mi, "Configure outfit toggle");
        mi.PortableControl.Type = PortableControlType.Toggle;
        mi.PortableControl.Parameter = costumeParamName;
        mi.automaticValue = true;
        mi.isDefault = (defaultOutfit != null && outfitGO == defaultOutfit);
        mi.isSaved = true;
        mi.isSynced = true;
        var outfitToggle = RecreateComponent<ModularAvatarObjectToggle>(outfitLeaf);
        AddObjectToToggle(outfitToggle, outfitGO);
      }

      Progress("Deactivating outfits by default...", 0.85f);
      foreach (var outfitGO in outfitMap.Keys)
      {
        Undo.RegisterCompleteObjectUndo(outfitGO, "Deactivate outfit by default");
        outfitGO.SetActive(false);
        EditorUtility.SetDirty(outfitGO);
        ActivateAncestorsUntilRoot(outfitGO, costumesRoot);
      }

      Progress("Merging Reset controller to FX...", 0.95f);
      TrySetupResetControllerAndMerge(menuRoot, resetParam, customMixParamNames);

      EditorUtility.SetDirty(menuRoot);
      Undo.CollapseUndoOperations(undoGroup);
      Progress("Done", 1f);
      Debug.Log($"[CostumeCustomMixer] Generated: outfits={outfitMap.Count}, parts={customMixParamNames.Count}; outputDir='{generatedFolder}'.");
      EditorUtility.DisplayDialog("生成完成", "已生成/重建 Menu、参数与 Reset 控制器（已合并至 FX）。", "确定");
    }
    finally
    {
      EditorUtility.ClearProgressBar();
    }
  }
  private bool PreflightCheck(out Dictionary<GameObject, List<GameObject>> outfitMap)
  {
    var ignoreSet = BuildIgnoreSet();
    outfitMap = FindOutfits(costumesRoot, excludeRootNamed: customMixName, ignoreSet: ignoreSet);

    bool hasMenuChild = costumesRoot.transform.Find("Menu") != null;
    int outfitCount = outfitMap.Count;
    int partCount = 0;
    foreach (var kv in outfitMap) partCount += kv.Value?.Count ?? 0;
    bool rootHasMesh = HasMeshOn(costumesRoot.transform);
    int excludedSubtrees = 0;
    foreach (Transform t in costumesRoot.GetComponentsInChildren<Transform>(true))
    {
      if (t != costumesRoot.transform && t.name == customMixName) excludedSubtrees++;
    }

    GameObject previewDefault = null;
    string defaultSource = "";
    if (outfitMap.Count > 0)
    {
      var overrideParent = FindNearestOutfitParent(defaultOutfitOverride, outfitMap);
      if (overrideParent != null)
      {
        previewDefault = overrideParent;
        defaultSource = "手动选择";
      }
      if (previewDefault == null)
      {
        var outfitsList = outfitMap.Keys.ToList();
        for (int i = 0; i < outfitsList.Count; i++)
        {
          var outfitGo = outfitsList[i];
          var rel = GetRelativePath(costumesRoot, outfitGo);
          string lower = (outfitGo.name + "/" + rel).ToLowerInvariant();
          if (DefaultOutfitHints.Any(h => lower.Contains(h))) { previewDefault = outfitGo; defaultSource = "名称匹配"; break; }
        }
      }
    }

    var sb = new StringBuilder();
    sb.AppendLine("即将生成/重建，摘要：");
    sb.AppendLine($"- 服装(outfit)：{outfitCount}");
    sb.AppendLine($"- 部件(mesh)：{partCount}");
    var folderPreview = string.IsNullOrWhiteSpace(generatedFolder) ? "Assets" : generatedFolder.Trim();
    if (!folderPreview.StartsWith("Assets")) folderPreview = "Assets";
    sb.AppendLine($"- 生成文件目录：{folderPreview}");
    var controllerPreviewPath = $"{folderPreview}/CostumeCustomMixer.controller";
    bool controllerExists = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPreviewPath) != null;
    sb.AppendLine($"- 控制器：{controllerPreviewPath} {(controllerExists ? "(已存在)" : "(将创建)")}");
    if (previewDefault != null)
    {
      sb.AppendLine($"- 默认服装：{previewDefault.name}（{defaultSource}）");
    }
    else
    {
      sb.AppendLine("- 默认服装：未设置");
    }
    if (hasMenuChild) sb.AppendLine("- 注意：存在 'Menu' 子物体，将被重建");
    if (rootHasMesh) sb.AppendLine("- 提示：根节点含网格，根上的网格不会被处理");
    if (excludedSubtrees > 0) sb.AppendLine($"- 提示：检测到 {excludedSubtrees} 个名为 '{customMixName}' 的子树，将被跳过");
    if (string.IsNullOrWhiteSpace(costumeParamName)) sb.AppendLine("- 警告：Costume 参数名为空，建议填写有效名称");
    if (string.IsNullOrWhiteSpace(customMixName)) sb.AppendLine("- 警告：CustomMix 节点名为空，建议填写有效名称");
    if (ignoreSet != null && ignoreSet.Count > 0)
    {
      sb.AppendLine("- 忽略名称（包含匹配，不区分大小写）：" + string.Join(", ", ignoreSet));
    }
    else
    {
      sb.AppendLine("- 忽略名称：无");
    }

    if (outfitMap.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("- 识别到的服装清单：");
      var list = outfitMap.Keys
        .Select(go => new { go, path = GetRelativePath(costumesRoot, go) })
        .OrderBy(x => string.IsNullOrEmpty(x.path) ? x.go.name : x.path, StringComparer.Ordinal)
        .ToList();
      foreach (var x in list)
      {
        var displayPath = string.IsNullOrEmpty(x.path) ? x.go.name : x.path;
        int parts = outfitMap.TryGetValue(x.go, out var ps) && ps != null ? ps.Count : 0;
        sb.AppendLine($"  - {x.go.name}    路径：{displayPath}    部件：{parts}");
      }
      sb.AppendLine();
      sb.AppendLine("- 服装分组（同父）：");
      var groups = outfitMap.Keys
        .Where(go => go != null && go.transform != null && go.transform.parent != null)
        .GroupBy(go => go.transform.parent)
        .OrderBy(g => GetRelativePath(costumesRoot, g.Key.gameObject), StringComparer.Ordinal);
      foreach (var g in groups)
      {
        var parentPath = g.Key.gameObject == costumesRoot ? "(root)" : GetRelativePath(costumesRoot, g.Key.gameObject);
        sb.AppendLine($"  - 父节点：{parentPath}    服装数：{g.Count()}");
      }
    }

    if (outfitCount == 0)
    {
      sb.AppendLine();
      sb.AppendLine("未检测到任何 outfit（要求：网格的父物体）。请确认：\n- 网格是否挂在其 outfit 父物体下\n- CustomMix 名称是否与待扫描子树冲突");
    }

    var message = sb.ToString();
    Debug.Log("[CostumeCustomMixer] 预检\n" + message);
    return EditorUtility.DisplayDialog("生成前预检", message, "继续", "取消");
  }

  private static Dictionary<GameObject, List<GameObject>> FindOutfits(GameObject root, string excludeRootNamed, HashSet<string> ignoreSet)
  {
    var outfitMap = new Dictionary<GameObject, List<GameObject>>();
    var stack = new Stack<Transform>();
    stack.Push(root.transform);
    while (stack.Count > 0)
    {
      var t = stack.Pop();
      if (!string.IsNullOrEmpty(excludeRootNamed) && t != root.transform && t.name == excludeRootNamed) continue;

      if (HasMeshOn(t))
      {
        var parent = t.parent;
        if (parent != null)
        {
          var outfitNode = parent.gameObject;
          if (!outfitMap.TryGetValue(outfitNode, out var list))
          {
            list = new List<GameObject>();
            outfitMap[outfitNode] = list;
            AppendStructureBasedOutfits(outfitNode, excludeRootNamed, outfitMap, ignoreSet);
          }
          list.Add(t.gameObject);
        }
      }
      for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
    }
    return outfitMap;
  }

  /// <summary>
  /// 在“同级（同父）”范围内为换色套装做结构/名称匹配；调用时机：每次新发现一个服装根节点后。
  /// 仅在该新服装根节点所在的同父分组内进行部分匹配（直接子对象名称交集≥1）。
  /// </summary>
  private static void AppendStructureBasedOutfits(GameObject newOutfitRoot, string excludeRootNamed, Dictionary<GameObject, List<GameObject>> outfitMap, HashSet<string> ignoreSet)
  {
    if (newOutfitRoot == null || outfitMap == null) return;
    var parent = newOutfitRoot.transform?.parent;
    if (parent == null) return;
    if (!string.IsNullOrEmpty(excludeRootNamed) && (parent.name == excludeRootNamed || newOutfitRoot.name == excludeRootNamed)) return;

    // 模板：同父下所有已识别服装
    var templateNameSets = new List<HashSet<string>>();
    var templateRoots = new HashSet<GameObject>(outfitMap.Keys.Where(k => k != null && k.transform.parent == parent));
    foreach (var tmpl in templateRoots)
    {
      var set = new HashSet<string>(StringComparer.Ordinal);
      var t = tmpl.transform;
      for (int i = 0; i < t.childCount; i++)
      {
        var c = t.GetChild(i);
        if (c == null) continue;
        if (NameMatchesIgnore(c.name, ignoreSet)) continue;
        set.Add(c.name);
      }
      if (set.Count > 0) templateNameSets.Add(set);
    }
    if (templateNameSets.Count == 0) return;

    // 遍历同父兄弟作为候选：未在 outfitMap 中的节点
    int childCount = parent.childCount;
    for (int i = 0; i < childCount; i++)
    {
      var cand = parent.GetChild(i);
      if (cand == null) continue;
      if (!string.IsNullOrEmpty(excludeRootNamed) && cand.name == excludeRootNamed) continue;
      var go = cand.gameObject;
      if (outfitMap.ContainsKey(go)) continue; // 已是服装

      // 候选的直接子对象名集合（过滤忽略名）
      var candNames = new HashSet<string>(StringComparer.Ordinal);
      for (int j = 0; j < cand.childCount; j++)
      {
        var c = cand.GetChild(j);
        if (c == null) continue;
        if (NameMatchesIgnore(c.name, ignoreSet)) continue;
        candNames.Add(c.name);
      }
      if (candNames.Count == 0) continue;

      for (int tIdx = 0; tIdx < templateNameSets.Count; tIdx++)
      {
        var inter = candNames.Intersect(templateNameSets[tIdx], StringComparer.Ordinal).ToList();
        if (inter.Count <= 0) continue;

        var matchedParts = new List<GameObject>();
        foreach (var name in inter)
        {
          for (int k = 0; k < cand.childCount; k++)
          {
            var ch = cand.GetChild(k);
            if (ch != null && ch.name == name)
            {
              matchedParts.Add(ch.gameObject);
              break;
            }
          }
        }
        if (matchedParts.Count > 0)
        {
          outfitMap[go] = matchedParts.Distinct().ToList();
          templateNameSets.Add(new HashSet<string>(candNames, StringComparer.Ordinal));
        }
        break;
      }
    }
  }

  private HashSet<string> BuildIgnoreSet()
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

  private static bool NameMatchesIgnore(string name, HashSet<string> ignoreSet)
  {
    if (string.IsNullOrEmpty(name) || ignoreSet == null || ignoreSet.Count == 0) return false;
    foreach (var ig in ignoreSet)
    {
      if (string.IsNullOrEmpty(ig)) continue;
      if (name.IndexOf(ig, StringComparison.OrdinalIgnoreCase) >= 0) return true;
    }
    return false;
  }

  private static bool HasMeshOn(Transform t)
  {
    if (t == null) return false;
    var smr = t.GetComponent<SkinnedMeshRenderer>();
    if (smr != null && smr.sharedMesh != null) return true;
    var mr = t.GetComponent<MeshRenderer>();
    var mf = t.GetComponent<MeshFilter>();
    return mr != null && mf != null && mf.sharedMesh != null;
  }

  private static GameObject FindOrCreateChild(GameObject parent, string name)
  {
    var child = parent.transform.Find(name);
    if (child != null) return child.gameObject;
    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Mirror Node");
    go.transform.SetParent(parent.transform, false);
    return go;
  }

  private static GameObject EnsurePath(GameObject parent, string relativePath)
  {
    var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var cur = parent;
    foreach (var raw in parts)
    {
      var part = raw.Trim();
      if (string.IsNullOrEmpty(part)) continue;
      cur = FindOrCreateChild(cur, part);
    }
    return cur;
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

  private static GameObject PrepareChildRoot(GameObject parent, string name)
  {
    var existing = parent.transform.Find(name);
    if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
    var go = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(go, "Create Root Child");
    go.transform.SetParent(parent.transform, false);
    go.transform.SetAsFirstSibling(); // 设置为第一个子节点
    EnsureSubmenuOnNode(go, "Costume Custom Mixer");
    return go;
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

  private string BuildParamName(string relPath)
  {
    string raw = relPath.Replace('/', '_');
    return paramPrefix + Sanitize(raw);
  }

  private static string Sanitize(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
    return new string(arr);
  }

  private static GameObject FindNearestOutfitParent(GameObject node, Dictionary<GameObject, List<GameObject>> outfitMap)
  {
    if (node == null || outfitMap == null || outfitMap.Count == 0) return null;
    GameObject cand = node;
    while (cand != null && !outfitMap.ContainsKey(cand))
    {
      var t = cand.transform;
      cand = t != null && t.parent != null ? t.parent.gameObject : null;
    }
    return cand != null && outfitMap.ContainsKey(cand) ? cand : null;
  }
  private static ModularAvatarMenuItem CreateMenuItem(GameObject node)
  {
    var existing = node.GetComponent<ModularAvatarMenuItem>();
    if (existing != null) Undo.DestroyObjectImmediate(existing);
    try { return Undo.AddComponent<ModularAvatarMenuItem>(node); }
    catch { return node.AddComponent<ModularAvatarMenuItem>(); }
  }

  private static ModularAvatarParameters EnsureParametersComponent(GameObject host)
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

  private static T RecreateComponent<T>(GameObject node) where T : Component
  {
    var existing = node.GetComponent<T>();
    if (existing != null) Undo.DestroyObjectImmediate((UnityEngine.Object)existing);
    try { return Undo.AddComponent<T>(node); }
    catch { return node.AddComponent<T>(); }
  }
  private static void AddObjectToToggle(ModularAvatarObjectToggle toggle, GameObject target, bool active = true)
  {
    if (toggle == null || target == null) return;
    Undo.RecordObject(toggle, "Add Object To MA Object Toggle");
    // Build AvatarObjectReference for target
    var aor = new AvatarObjectReference();
    aor.Set(target);
    // Avoid duplicates by comparing referencePath or resolved target
    var list = toggle.Objects ?? new List<ToggledObject>();
    bool exists = false;
    foreach (var item in list)
    {
      if (item.Object != null)
      {
        if (!string.IsNullOrEmpty(item.Object.referencePath) && item.Object.referencePath == aor.referencePath)
        {
          exists = true;
          break;
        }
        var resolved = item.Object.Get(toggle);
        if (resolved == target)
        {
          exists = true;
          break;
        }
      }
    }
    if (!exists)
    {
      list.Add(new ToggledObject { Object = aor, Active = active });
      toggle.Objects = list;
      EditorUtility.SetDirty(toggle);
    }
  }
  // 创建/更新带有“Reset CustomMix”层的 AnimatorController，并使用 MergeAnimator 合并到 FX。
  private bool TrySetupResetControllerAndMerge(GameObject menuRoot, string resetParam, List<string> customMixParamNames)
  {
    if (menuRoot == null || string.IsNullOrEmpty(resetParam) || customMixParamNames == null || customMixParamNames.Count == 0)
      return false;
    try
    {
      var folder = string.IsNullOrWhiteSpace(generatedFolder) ? "Assets" : generatedFolder.Trim();
      if (!folder.StartsWith("Assets")) folder = "Assets";
      EnsureAssetFolder(folder);
      var controllerPath = $"{folder}/CostumeCustomMixer.controller";
      var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
      if (controller == null)
      {
        controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
      }

      void EnsureBoolParam(string name)
      {
        if (controller.parameters.All(p => p.name != name))
        {
          controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        }
      }
      EnsureBoolParam(resetParam);
      foreach (var p in customMixParamNames.Distinct()) EnsureBoolParam(p);

      const string LayerName = "Reset CustomMix";
      var layers = controller.layers.ToList();
      int existingIdx = layers.FindIndex(l => l.name == LayerName);
      if (existingIdx >= 0)
      {
        layers.RemoveAt(existingIdx);
        controller.layers = layers.ToArray();
      }
      controller.AddLayer(LayerName);
      var layersAfterAdd = controller.layers;
      int newLayerIdx = Array.FindIndex(layersAfterAdd, l => l.name == LayerName);
      if (newLayerIdx >= 0)
      {
        var newLayer = layersAfterAdd[newLayerIdx];
        newLayer.defaultWeight = 1f;
        layersAfterAdd[newLayerIdx] = newLayer;
        controller.layers = layersAfterAdd;
      }
      var layer = controller.layers.First(l => l.name == LayerName);
      var sm = layer.stateMachine;
      foreach (var st in sm.states.Select(s => s.state).ToArray())
      {
        sm.RemoveState(st);
      }
      var idle = sm.AddState("Idle");
      sm.defaultState = idle;
      var resetState = sm.AddState("Reset");
      resetState.writeDefaultValues = false;
      var toReset = sm.AddAnyStateTransition(resetState);
      toReset.hasExitTime = false;
      toReset.hasFixedDuration = true;
      toReset.duration = 0f;
      toReset.offset = 0f;
      toReset.AddCondition(AnimatorConditionMode.If, 0, resetParam);
      var back = resetState.AddTransition(idle);
      back.hasExitTime = false;
      back.hasFixedDuration = true;
      back.duration = 0f;
      back.offset = 0f;
      var behaviour = resetState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
      if (behaviour == null) return false;
      var newList = new List<VRCAvatarParameterDriver.Parameter>();
      foreach (var p in customMixParamNames.Distinct())
      {
        newList.Add(new VRCAvatarParameterDriver.Parameter
        {
          name = p,
          value = 0f,
          type = VRCAvatarParameterDriver.ChangeType.Set
        });
      }
      newList.Add(new VRCAvatarParameterDriver.Parameter
      {
        name = resetParam,
        value = 0f,
        type = VRCAvatarParameterDriver.ChangeType.Set
      });
      behaviour.parameters = newList;
      Debug.Log($"[CostumeCustomMixer] Controller updated: {controllerPath}");
      EditorUtility.SetDirty(controller);
      AssetDatabase.SaveAssets();

      var merge = RecreateComponent<ModularAvatarMergeAnimator>(menuRoot);
      merge.animator = controller;
      merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
      merge.deleteAttachedAnimator = false;
      merge.mergeAnimatorMode = MergeAnimatorMode.Append;
      EditorUtility.SetDirty(menuRoot);
      return true;
    }
    catch (Exception e)
    {
      Debug.LogWarning($"Failed to set up Reset controller/merge: {e.Message}");
      return false;
    }
  }

  private static void EnsureAssetFolder(string assetsRelativePath)
  {
    if (string.IsNullOrEmpty(assetsRelativePath)) return;
    assetsRelativePath = assetsRelativePath.Replace('\\', '/');
    if (!assetsRelativePath.StartsWith("Assets")) return;
    var parts = assetsRelativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) return;
    var current = parts[0];
    for (int i = 1; i < parts.Length; i++)
    {
      var next = parts[i];
      var combined = current + "/" + next;
      if (!AssetDatabase.IsValidFolder(combined))
      {
        AssetDatabase.CreateFolder(current, next);
      }
      current = combined;
    }
  }

  private static void ActivateAncestorsUntilRoot(GameObject node, GameObject root)
  {
    if (node == null || root == null) return;
    var t = node.transform.parent;
    while (t != null && t.gameObject != root)
    {
      if (!t.gameObject.activeSelf)
      {
        Undo.RegisterCompleteObjectUndo(t.gameObject, "Activate ancestor for menu visibility");
        t.gameObject.SetActive(true);
        EditorUtility.SetDirty(t.gameObject);
      }
      t = t.parent;
    }
  }
}
