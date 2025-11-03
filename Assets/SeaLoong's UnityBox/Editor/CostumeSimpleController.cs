using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

public class CostumeSimpleController : EditorWindow
{
  private static readonly string[] DefaultOutfitHints = { "origin", "original", "default", "base", "vanilla", "standard", "normal" };
  private GameObject costumesRoot;
  private string paramPrefix = "CST_";
  private string costumeParamName = "costume";
  private string generatedFolder = "Assets/SeaLoong's UnityBox/Costume Simple Controller/Generated";
  [SerializeField]
  private string ignoreNamesCsv = "Armature,Bones,Bone,Skeleton,Rig";
  private GameObject defaultOutfitOverride;

  private Dictionary<GameObject, List<GameObject>> currentOutfitMap = new Dictionary<GameObject, List<GameObject>>();
  private Dictionary<GameObject, bool> outfitSelections = new Dictionary<GameObject, bool>();
  private Dictionary<GameObject, Dictionary<GameObject, bool>> partSelections = new Dictionary<GameObject, Dictionary<GameObject, bool>>();
  private bool previewFoldout = true;
  private Vector2 scrollPosition = Vector2.zero;

  [MenuItem("Tools/SeaLoong's UnityBox/Costume Simple Controller")]
  public static void ShowWindow()
  {
    GetWindow<CostumeSimpleController>("Costume Simple Controller");
  }

  private void UpdatePreview()
  {
    currentOutfitMap.Clear();
    outfitSelections.Clear();
    partSelections.Clear();
    if (costumesRoot == null) return;
    var ignoreSet = BuildIgnoreSet();
    currentOutfitMap = FindOutfits(costumesRoot, ignoreSet);
    foreach (var outfit in currentOutfitMap.Keys)
    {
      outfitSelections[outfit] = true; // 默认选中
      partSelections[outfit] = new Dictionary<GameObject, bool>();
      foreach (var part in currentOutfitMap[outfit])
      {
        partSelections[outfit][part] = true; // 默认选中
      }
    }
  }

  private void OnGUI()
  {
    EditorGUILayout.LabelField("Costume Simple Controller", EditorStyles.boldLabel);
    var oldRoot = costumesRoot;
    costumesRoot = (GameObject)EditorGUILayout.ObjectField("Costumes Root", costumesRoot, typeof(GameObject), true);
    if (costumesRoot != oldRoot)
    {
      UpdatePreview();
    }
    paramPrefix = EditorGUILayout.TextField("Parameter Prefix", paramPrefix);
    costumeParamName = EditorGUILayout.TextField("Costume Parameter Name", costumeParamName);
    defaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField("Default Outfit (optional)", defaultOutfitOverride, typeof(GameObject), true);

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

    EditorGUILayout.HelpBox(
      "使用指南：\n" +
      "1) 选择 Costumes Root\n" +
      "2) 预览并选择要生成的服装和部件\n" +
      "3) 点击 Generate：生成选中的控制菜单\n" +
      "4) Output Folder 可自定义生成文件输出目录\n" +
      "5) Ignore Names：结构匹配时忽略的直接子对象名称",
      MessageType.Info);

    EditorGUILayout.LabelField("Ignore Names (逗号/分号/换行分隔)");
    ignoreNamesCsv = EditorGUILayout.TextArea(ignoreNamesCsv, GUILayout.MinHeight(40));

    // Preview section
    if (costumesRoot != null && currentOutfitMap.Count > 0)
    {
      previewFoldout = EditorGUILayout.Foldout(previewFoldout, "预览服装和部件", true);
      if (previewFoldout)
      {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(200));
        EditorGUI.indentLevel++;

        var variantGroups = new Dictionary<Transform, List<GameObject>>();
        foreach (var outfit in currentOutfitMap.Keys)
        {
          var parent = outfit.transform.parent;
          if (parent != null && parent.gameObject != costumesRoot)
          {
            var siblings = currentOutfitMap.Keys.Where(o => o.transform.parent == parent).ToList();
            if (siblings.Count > 1)
            {
              bool hasTemplate = siblings.Any(s => currentOutfitMap[s].Count > 0);
              bool hasVariant = siblings.Any(s => currentOutfitMap[s].Count == 0);

              if (hasTemplate && hasVariant)
              {
                if (!variantGroups.ContainsKey(parent))
                {
                  variantGroups[parent] = new List<GameObject>();
                }
                if (!variantGroups[parent].Contains(outfit))
                {
                  variantGroups[parent] = siblings.OrderBy(o => o.name).ToList();
                }
              }
            }
          }
        }

        var processedInPreview = new HashSet<GameObject>();

        foreach (var outfit in currentOutfitMap.Keys.OrderBy(go => GetRelativePath(costumesRoot, go)))
        {
          if (processedInPreview.Contains(outfit)) continue;

          var outfitPath = GetRelativePath(costumesRoot, outfit);
          var parent = outfit.transform.parent;
          bool hasVariants = parent != null && variantGroups.ContainsKey(parent);
          List<GameObject> variantOutfits = hasVariants ? variantGroups[parent] : new List<GameObject> { outfit };

          if (hasVariants)
          {
            var template = variantOutfits.FirstOrDefault(o => currentOutfitMap[o].Count > 0);
            if (template == null || template != outfit)
            {
              continue;
            }
          }

          string displayName;
          if (hasVariants)
          {
            var parentPath = GetRelativePath(costumesRoot, parent.gameObject);
            displayName = string.IsNullOrEmpty(parentPath) ? parent.name : parentPath;
          }
          else
          {
            displayName = string.IsNullOrEmpty(outfitPath) ? outfit.name : outfitPath;
          }

          // 显示服装复选框
          EditorGUILayout.BeginHorizontal();
          outfitSelections[outfit] = EditorGUILayout.ToggleLeft(displayName, outfitSelections[outfit]);
          if (hasVariants)
          {
            EditorGUILayout.LabelField($"(变体组: {variantOutfits.Count})", EditorStyles.miniLabel, GUILayout.MinWidth(200));
          }
          else
          {
            EditorGUILayout.LabelField($"({costumeParamName})", EditorStyles.miniLabel, GUILayout.MinWidth(200));
          }
          EditorGUILayout.EndHorizontal();

          if (outfitSelections[outfit])
          {
            EditorGUI.indentLevel++;

            // 显示变体（所有变体都可以勾选）
            if (hasVariants)
            {
              EditorGUILayout.LabelField("变体:", EditorStyles.boldLabel);
              foreach (var variant in variantOutfits)
              {
                processedInPreview.Add(variant);

                // 初始化变体的选择状态
                if (!outfitSelections.ContainsKey(variant))
                {
                  outfitSelections[variant] = true;
                }
                // 初始化变体的部件字典（即使是空的也要初始化）
                if (!partSelections.ContainsKey(variant))
                {
                  partSelections[variant] = new Dictionary<GameObject, bool>();
                }

                EditorGUILayout.BeginHorizontal();
                outfitSelections[variant] = EditorGUILayout.ToggleLeft($"  {variant.name}", outfitSelections[variant]);
                EditorGUILayout.LabelField($"({costumeParamName})", EditorStyles.miniLabel, GUILayout.MinWidth(200));
                EditorGUILayout.EndHorizontal();
              }
              EditorGUILayout.Space(5);
            }

            // 显示部件（只有模板outfit才有部件）
            var parts = currentOutfitMap[outfit];
            if (parts.Count > 0)
            {
              EditorGUILayout.LabelField("部件:", EditorStyles.boldLabel);
              foreach (var part in parts)
              {
                var partPath = GetRelativePath(outfit, part);
                var partParam = BuildParamName(outfitPath + "/" + partPath);
                EditorGUILayout.BeginHorizontal();
                partSelections[outfit][part] = EditorGUILayout.ToggleLeft($"  {part.name}", partSelections[outfit][part]);
                EditorGUILayout.LabelField($"({partParam})", EditorStyles.miniLabel, GUILayout.MinWidth(200));
                EditorGUILayout.EndHorizontal();
              }
            }

            EditorGUI.indentLevel--;
          }
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.EndScrollView();
      }
    }

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
          Debug.LogError("CostumeSimpleController generation failed: " + ex);
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

    // Build filtered outfit map based on selections
    var filteredOutfitMap = new Dictionary<GameObject, List<GameObject>>();
    foreach (var outfit in currentOutfitMap.Keys)
    {
      if (outfitSelections[outfit])
      {
        var selectedParts = currentOutfitMap[outfit].Where(part => partSelections[outfit][part]).ToList();
        // 允许空部件列表（用于变体）
        filteredOutfitMap[outfit] = selectedParts;
      }
    }

    if (!PreflightCheck(filteredOutfitMap)) return;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Generate Costume Simple Controller");
    void Progress(string info, float p) { EditorUtility.DisplayProgressBar("Costume Simple Controller", info, Mathf.Clamp01(p)); }
    try
    {
      Progress("Initializing...", 0.1f);
      var menuRoot = PrepareChildRoot(costumesRoot, "Costume_Menu");
      if (menuRoot.GetComponent<ModularAvatarMenuInstaller>() == null)
      {
        try { Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot); }
        catch { menuRoot.AddComponent<ModularAvatarMenuInstaller>(); }
      }
      var rootParams = EnsureParametersComponent(menuRoot);

      // Add unified costume parameter
      if (!rootParams.parameters.Any(p => p.nameOrPrefix == costumeParamName))
      {
        Undo.RecordObject(rootParams, "Add costume parameter");
        var pc = new ParameterConfig
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
        rootParams.parameters.Add(pc);
      }

      GameObject defaultOutfit = null;
      {
        var overrideOutfit = FindNearestOutfitParent(defaultOutfitOverride, filteredOutfitMap);
        if (overrideOutfit != null) defaultOutfit = overrideOutfit;

        if (defaultOutfit == null)
        {
          foreach (var outfitGo in filteredOutfitMap.Keys)
          {
            var rel = GetRelativePath(costumesRoot, outfitGo);
            string lower = (outfitGo.name + "/" + rel).ToLowerInvariant();
            if (DefaultOutfitHints.Any(h => lower.Contains(h))) { defaultOutfit = outfitGo; break; }
          }
        }
        Debug.Log(defaultOutfit != null
          ? $"[CostumeSimpleController] 默认服装：{defaultOutfit.name}"
          : "[CostumeSimpleController] 默认服装未解析");
      }

      Progress("Building toggles...", 0.5f);

      // 对服装进行分组：同父节点下的服装为一组
      var outfitGroups = new Dictionary<Transform, List<GameObject>>();
      foreach (var outfitGO in filteredOutfitMap.Keys)
      {
        var parent = outfitGO.transform.parent;
        if (parent != null)
        {
          if (!outfitGroups.ContainsKey(parent))
          {
            outfitGroups[parent] = new List<GameObject>();
          }
          outfitGroups[parent].Add(outfitGO);
          Debug.Log($"[Group] Adding {outfitGO.name} (parts: {filteredOutfitMap[outfitGO].Count}) to parent {parent.name}");
        }
      }

      // 已处理的服装集合，避免重复处理变体
      var processedOutfits = new HashSet<GameObject>();

      foreach (var outfitGO in filteredOutfitMap.Keys)
      {
        if (processedOutfits.Contains(outfitGO)) continue;

        // 获取同组的所有服装（包括变体）
        var parent = outfitGO.transform.parent;
        var variantOutfits = (parent != null && outfitGroups.ContainsKey(parent))
          ? outfitGroups[parent].Where(go => filteredOutfitMap.ContainsKey(go)).ToList()
          : new List<GameObject> { outfitGO };

        // 只有多个变体时才认为是变体组
        bool hasVariants = variantOutfits.Count > 1 && parent != null && parent.gameObject != costumesRoot;

        Debug.Log($"[Generate] Processing {outfitGO.name}: hasVariants={hasVariants}, variantCount={variantOutfits.Count}, parent={parent?.name ?? "null"}");
        if (hasVariants)
        {
          Debug.Log($"[Generate] Variants in group: {string.Join(", ", variantOutfits.Select(v => $"{v.name}(parts:{filteredOutfitMap[v].Count})"))}");
        }

        // 确定菜单路径：如果有变体，使用父节点路径；否则使用outfit路径
        string menuBasePath;
        GameObject menuTargetObject;
        if (hasVariants)
        {
          // 变体组：使用父节点路径
          menuBasePath = GetRelativePath(costumesRoot, parent.gameObject);
          menuTargetObject = parent.gameObject;
          Debug.Log($"[Generate] Using parent path: {menuBasePath}");
        }
        else
        {
          // 单个outfit：使用outfit路径
          menuBasePath = GetRelativePath(costumesRoot, outfitGO);
          menuTargetObject = outfitGO;
          Debug.Log($"[Generate] Using outfit path: {menuBasePath}");
        }

        var partsMenu = menuBasePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var curMenu = menuRoot;

        // 创建层级菜单
        for (int i = 0; i < Mathf.Max(0, partsMenu.Length - 1); i++)
        {
          curMenu = EnsurePath(curMenu, partsMenu[i]);
          EnsureSubmenuOnNode(curMenu);
        }

        // 创建最终的outfit子菜单
        var outfitLeafName = partsMenu.Length > 0 ? partsMenu[partsMenu.Length - 1] : menuTargetObject.name;
        var outfitSubmenu = EnsurePath(curMenu, outfitLeafName);
        EnsureSubmenuOnNode(outfitSubmenu, outfitLeafName);

        // 添加部件开关（仅第一个服装，即模板服装）
        var meshes = filteredOutfitMap[outfitGO];
        if (meshes.Count > 0)
        {
          // 创建部件子菜单
          var partsSubmenu = FindOrCreateChild(outfitSubmenu, "Parts");
          EnsureSubmenuOnNode(partsSubmenu);

          foreach (var meshNode in meshes)
          {
            string partRelPath = GetRelativePath(outfitGO, meshNode);
            bool partDefaultActive = meshNode.activeSelf;

            var partNode = FindOrCreateChild(partsSubmenu, meshNode.name);
            var partMi = CreateMenuItem(partNode);
            Undo.RecordObject(partMi, "Configure part toggle");
            partMi.PortableControl.Type = PortableControlType.Toggle;
            string outfitRelPath = GetRelativePath(costumesRoot, outfitGO);
            string partParamName = BuildParamName(outfitRelPath + "/" + partRelPath);
            partMi.PortableControl.Parameter = partParamName;
            partMi.automaticValue = true;
            partMi.isDefault = partDefaultActive;
            partMi.isSaved = true;
            partMi.isSynced = true;
            var partToggle = RecreateComponent<ModularAvatarObjectToggle>(partNode);
            AddObjectToToggle(partToggle, meshNode, true);

            if (!rootParams.parameters.Any(p => p.nameOrPrefix == partParamName))
            {
              Undo.RecordObject(rootParams, "Add part parameter");
              var pc = new ParameterConfig
              {
                nameOrPrefix = partParamName,
                remapTo = "",
                internalParameter = false,
                isPrefix = false,
                syncType = ParameterSyncType.Bool,
                localOnly = false,
                defaultValue = partDefaultActive ? 1f : 0f,
                saved = true,
                hasExplicitDefaultValue = true
              };
              rootParams.parameters.Add(pc);
            }
            meshNode.SetActive(false);
          }
        }

        if (hasVariants)
        {
          foreach (var variant in variantOutfits)
          {
            processedOutfits.Add(variant);
            var variantNode = FindOrCreateChild(outfitSubmenu, variant.name);
            var variantMi = CreateMenuItem(variantNode);
            Undo.RecordObject(variantMi, "Configure variant toggle");
            variantMi.PortableControl.Type = PortableControlType.Toggle;
            variantMi.PortableControl.Parameter = costumeParamName;
            variantMi.automaticValue = true;
            variantMi.isDefault = (defaultOutfit != null && variant == defaultOutfit);
            variantMi.isSaved = true;
            variantMi.isSynced = true;
            var variantToggle = RecreateComponent<ModularAvatarObjectToggle>(variantNode);
            AddObjectToToggle(variantToggle, variant);
          }
        }
        else
        {
          processedOutfits.Add(outfitGO);
          var outfitNode = FindOrCreateChild(outfitSubmenu, outfitGO.name);
          var outfitMi = CreateMenuItem(outfitNode);
          Undo.RecordObject(outfitMi, "Configure outfit toggle");
          outfitMi.PortableControl.Type = PortableControlType.Toggle;
          outfitMi.PortableControl.Parameter = costumeParamName;
          outfitMi.automaticValue = true;
          outfitMi.isDefault = (defaultOutfit != null && outfitGO == defaultOutfit);
          outfitMi.isSaved = true;
          outfitMi.isSynced = true;
          var outfitToggle = RecreateComponent<ModularAvatarObjectToggle>(outfitNode);
          AddObjectToToggle(outfitToggle, outfitGO);
        }
      }

      if (!rootParams.parameters.Any(p => p.nameOrPrefix == costumeParamName))
      {
        Undo.RecordObject(rootParams, "Add costume parameter");
        var pc = new ParameterConfig
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
        rootParams.parameters.Add(pc);
      }

      Progress("Deactivating outfits by default...", 0.9f);
      foreach (var outfitGO in filteredOutfitMap.Keys)
      {
        Undo.RegisterCompleteObjectUndo(outfitGO, "Deactivate outfit by default");
        outfitGO.SetActive(false);
        EditorUtility.SetDirty(outfitGO);
        ActivateAncestorsUntilRoot(outfitGO, costumesRoot);
      }

      EditorUtility.SetDirty(menuRoot);
      Undo.CollapseUndoOperations(undoGroup);
      Progress("Done", 1f);
      Debug.Log($"[CostumeSimpleController] Generated: outfits={filteredOutfitMap.Count}, parts={filteredOutfitMap.Values.Sum(l => l.Count)}; outputDir='{generatedFolder}'.");
      EditorUtility.DisplayDialog("生成完成", "已生成选中的服装和部件的控制菜单。", "确定");
    }
    finally
    {
      EditorUtility.ClearProgressBar();
    }
  }

  private bool PreflightCheck(Dictionary<GameObject, List<GameObject>> outfitMap)
  {
    bool hasMenuChild = costumesRoot.transform.Find("Costume_Menu") != null;
    int outfitCount = outfitMap.Count;
    int partCount = 0;
    foreach (var kv in outfitMap) partCount += kv.Value?.Count ?? 0;
    bool rootHasMesh = HasMeshOn(costumesRoot.transform);
    int excludedSubtrees = 0;
    foreach (Transform t in costumesRoot.GetComponentsInChildren<Transform>(true))
    {
      if (t != costumesRoot.transform && t.name == "Costume_Menu") excludedSubtrees++;
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
    sb.AppendLine("即将生成，摘要：");
    sb.AppendLine($"- 服装(outfit)：{outfitCount}");
    sb.AppendLine($"- 部件(mesh)：{partCount}");
    var folderPreview = string.IsNullOrWhiteSpace(generatedFolder) ? "Assets" : generatedFolder.Trim();
    if (!folderPreview.StartsWith("Assets")) folderPreview = "Assets";
    sb.AppendLine($"- 生成文件目录：{folderPreview}");
    if (previewDefault != null)
    {
      sb.AppendLine($"- 默认服装：{previewDefault.name}（{defaultSource}）");
    }
    else
    {
      sb.AppendLine("- 默认服装：未设置");
    }
    if (hasMenuChild) sb.AppendLine("- 注意：存在 'Costume_Menu' 子物体，将被重建");
    if (rootHasMesh) sb.AppendLine("- 提示：根节点含网格，根上的网格不会被处理");
    if (excludedSubtrees > 0) sb.AppendLine($"- 提示：检测到 {excludedSubtrees} 个名为 'Costume_Menu' 的子树，将被跳过");

    if (outfitMap.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("- 选中的服装清单：");
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
    }

    if (outfitCount == 0)
    {
      sb.AppendLine();
      sb.AppendLine("未选中任何服装。请在预览中选择要生成的项。");
    }

    var message = sb.ToString();
    Debug.Log("[CostumeSimpleController] 预检\n" + message);
    return EditorUtility.DisplayDialog("生成前预检", message, "继续", "取消");
  }

  private static Dictionary<GameObject, List<GameObject>> FindOutfits(GameObject root, HashSet<string> ignoreSet)
  {
    var outfitMap = new Dictionary<GameObject, List<GameObject>>();
    var stack = new Stack<Transform>();
    stack.Push(root.transform);

    Debug.Log($"[FindOutfits] Starting search from root: {root.name}");

    while (stack.Count > 0)
    {
      var t = stack.Pop();
      if (HasMeshOn(t))
      {
        var parent = t.parent;
        if (parent != null)
        {
          var outfitNode = parent.gameObject;
          if (!outfitMap.ContainsKey(outfitNode))
          {
            var parts = new List<GameObject>();
            for (int i = 0; i < outfitNode.transform.childCount; i++)
            {
              var child = outfitNode.transform.GetChild(i);
              if (!NameMatchesIgnore(child.name, ignoreSet))
              {
                parts.Add(child.gameObject);
              }
            }
            if (parts.Count > 0)
            {
              outfitMap[outfitNode] = parts;
              Debug.Log($"[FindOutfits] Found outfit: {outfitNode.name} with {parts.Count} parts, parent: {parent.parent?.name ?? "null"}");
            }
            AppendStructureBasedOutfits(outfitNode, outfitMap, ignoreSet, root);
          }
        }
      }
      for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
    }

    Debug.Log($"[FindOutfits] Total outfits found: {outfitMap.Count}");
    foreach (var kv in outfitMap)
    {
      Debug.Log($"[FindOutfits] - {kv.Key.name}: {kv.Value.Count} parts");
    }

    return outfitMap;
  }

  /// <summary>
  /// 在"同级（同父）"范围内识别换色套装变体；调用时机：每次新发现一个服装根节点后。
  /// 将所有同父兄弟节点识别为变体（不需要子对象名称匹配）。
  /// </summary>
  private static void AppendStructureBasedOutfits(GameObject newOutfitRoot, Dictionary<GameObject, List<GameObject>> outfitMap, HashSet<string> ignoreSet, GameObject costumeRoot)
  {
    if (newOutfitRoot == null || outfitMap == null) return;
    var parent = newOutfitRoot.transform?.parent;
    if (parent == null) return;

    Debug.Log($"[AppendStructureBasedOutfits] Checking outfit: {newOutfitRoot.name}, parent: {parent.name}");

    // 检查父节点是否为costume根节点（即是否在根节点下）
    if (costumeRoot != null && parent.gameObject == costumeRoot)
    {
      Debug.Log($"[AppendStructureBasedOutfits] Parent is costume root, skipping");
      return;
    }

    // 遍历同父兄弟作为变体：所有未在 outfitMap 中的节点
    int childCount = parent.childCount;
    int addedCount = 0;
    for (int i = 0; i < childCount; i++)
    {
      var candidate = parent.GetChild(i);
      if (candidate == null) continue;
      var candidateGo = candidate.gameObject;
      if (outfitMap.ContainsKey(candidateGo)) continue; // 已是服装

      // 直接将同父兄弟识别为变体，无需子对象匹配
      Debug.Log($"[AppendStructureBasedOutfits] Found variant: {candidateGo.name} (sibling of {newOutfitRoot.name})");
      outfitMap[candidateGo] = new List<GameObject>(); // 变体的部件列表为空
      addedCount++;
    }

    Debug.Log($"[AppendStructureBasedOutfits] Added {addedCount} variants for template '{newOutfitRoot.name}'");
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
    Undo.RegisterCreatedObjectUndo(go, "Create Node");
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
    go.transform.SetAsFirstSibling();
    EnsureSubmenuOnNode(go, "Costume");
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
    var aor = new AvatarObjectReference();
    aor.Set(target);
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