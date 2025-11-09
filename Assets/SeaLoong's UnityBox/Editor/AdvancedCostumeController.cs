using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using Algolia.Search.Models.Common;

/// <summary>
/// 服装数据结构
/// </summary>
public class OutfitData
{
  public GameObject BaseObject { get; set; }
  public GameObject OutfitObject { get; set; }
  public List<GameObject> Variants { get; set; } = new List<GameObject>();
  public List<GameObject> Parts { get; set; } = new List<GameObject>();
  public string Name { get; set; }
  public string RelativePath { get; set; }
  public bool IsDefaultOutfit { get; set; }

  public bool HasVariants() => Variants.Count > 0;
  public List<GameObject> GetAllObjects()
  {
    var result = new List<GameObject> { BaseObject };
    result.AddRange(Variants);
    return result;
  }
}

/// <summary>
/// 高级服装控制器 - 精简重构版
/// </summary>
public class AdvancedCostumeController : EditorWindow
{
  private static readonly string[] DefaultOutfitHints = { "origin", "original", "default", "base", "vanilla", "standard", "normal" };

  private GameObject costumesRoot;
  private string paramPrefix = "CST";
  private string costumeParamName = "costume";
  private string generatedFolder = "Assets/SeaLoong's UnityBox/Advanced Costume Controller/Generated";
  private string ignoreNamesCsv = "Armature,Bone,Skeleton";
  private GameObject defaultOutfitOverride;
  private bool enableParts = false;
  private bool enableCustomMixer = false;
  private string customMixerName = "CustomMix";

  private HashSet<string> ignoreSet = new();
  private List<OutfitData> currentOutfitDataList = new List<OutfitData>();
  private Dictionary<OutfitData, bool> outfitSelections = new Dictionary<OutfitData, bool>();
  private Dictionary<OutfitData, Dictionary<GameObject, bool>> outfitObjectSelections = new Dictionary<OutfitData, Dictionary<GameObject, bool>>();
  private Dictionary<OutfitData, Dictionary<GameObject, bool>> partSelections = new Dictionary<OutfitData, Dictionary<GameObject, bool>>();
  private bool previewFoldout = true;
  private Vector2 scrollPosition = Vector2.zero;

  [MenuItem("Tools/SeaLoong's UnityBox/Advanced Costume Controller")]
  public static void ShowWindow() => GetWindow<AdvancedCostumeController>("Advanced Costume Controller");

  private void OnEnable()
  {
    if (costumesRoot != null) UpdatePreview();
  }

  private void UpdatePreview()
  {
    currentOutfitDataList.Clear();
    outfitSelections.Clear();
    outfitObjectSelections.Clear();
    partSelections.Clear();
    if (costumesRoot == null) return;

    ignoreSet = Utils.BuildIgnoreSet(ignoreNamesCsv);
    currentOutfitDataList = FindOutfits();

    foreach (var outfitData in currentOutfitDataList)
    {
      outfitSelections[outfitData] = true;

      // 初始化服装对象选择状态（本体+变体）
      outfitObjectSelections[outfitData] = new Dictionary<GameObject, bool>();
      foreach (var obj in outfitData.GetAllObjects())
        outfitObjectSelections[outfitData][obj] = true;

      // 初始化部件选择状态
      partSelections[outfitData] = new Dictionary<GameObject, bool>();
      foreach (var part in outfitData.Parts)
        partSelections[outfitData][part] = true;
    }
  }

  private void OnGUI()
  {
    EditorGUILayout.LabelField("Advanced Costume Controller", EditorStyles.boldLabel);

    var oldRoot = costumesRoot;
    costumesRoot = (GameObject)EditorGUILayout.ObjectField("Costumes Root", costumesRoot, typeof(GameObject), true);
    if (costumesRoot != oldRoot) UpdatePreview();

    paramPrefix = EditorGUILayout.TextField("Parameter Prefix", paramPrefix);
    costumeParamName = EditorGUILayout.TextField("Costume Parameter Name", costumeParamName);
    defaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField("Default Outfit (optional)", defaultOutfitOverride, typeof(GameObject), true);
    enableParts = EditorGUILayout.Toggle("Enable Parts Control", enableParts);
    EditorGUI.BeginDisabledGroup(!enableParts);
    enableCustomMixer = EditorGUILayout.Toggle("Enable Custom Mixer", enableCustomMixer);
    if (enableCustomMixer)
    {
      EditorGUI.indentLevel++;
      customMixerName = EditorGUILayout.TextField("Custom Mixer Name", customMixerName);
      EditorGUILayout.HelpBox("Custom Mixer 是一个特殊服装，可以自由组合所有服装的部件。", MessageType.Info);
      EditorGUI.indentLevel--;
    }
    EditorGUI.EndDisabledGroup();
    if (!enableParts && enableCustomMixer) enableCustomMixer = false;

    EditorGUILayout.Space(5);
    EditorGUILayout.BeginHorizontal();
    generatedFolder = EditorGUILayout.TextField("Output Folder", generatedFolder);
    if (GUILayout.Button("Browse…", GUILayout.MaxWidth(80)))
    {
      // 计算当前选择路径的绝对路径作为默认路径
      string defaultPath = Application.dataPath;
      if (!string.IsNullOrEmpty(generatedFolder) && generatedFolder.StartsWith("Assets"))
      {
        var assetsAbs = Application.dataPath.Replace('\\', '/');
        if (generatedFolder == "Assets")
        {
          defaultPath = assetsAbs;
        }
        else
        {
          var relativePart = generatedFolder.Substring("Assets/".Length);
          defaultPath = Path.Combine(assetsAbs, relativePart).Replace('\\', '/');
          // 如果路径不存在，逐级向上查找存在的父目录
          while (!Directory.Exists(defaultPath) && defaultPath.Length > assetsAbs.Length)
          {
            defaultPath = Path.GetDirectoryName(defaultPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(defaultPath))
            {
              defaultPath = assetsAbs;
              break;
            }
          }
          // 最终回退到 Assets
          if (!Directory.Exists(defaultPath))
            defaultPath = assetsAbs;
        }
      }

      var abs = EditorUtility.OpenFolderPanel("Select folder under Assets", defaultPath, "");
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
      "使用指南：\n1) 选择 Costumes Root\n2) 预览并选择要生成的服装和部件\n3) 点击 Generate 生成控制菜单",
      MessageType.Info);

    EditorGUILayout.LabelField("Ignore Names (逗号/分号/换行分隔)");
    ignoreNamesCsv = EditorGUILayout.TextArea(ignoreNamesCsv, GUILayout.MinHeight(40));

    DrawPreviewSection();

    using (new EditorGUI.DisabledScope(costumesRoot == null))
    {
      if (GUILayout.Button("Generate"))
      {
        try { Generate(); }
        catch (Exception ex) { Debug.LogError($"Generation failed: {ex}"); }
      }
    }
  }

  private void DrawPreviewSection()
  {
    if (costumesRoot == null || currentOutfitDataList.Count == 0) return;

    previewFoldout = EditorGUILayout.Foldout(previewFoldout, "预览服装和部件", true);
    if (!previewFoldout) return;

    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(400));

    // 计算当前选择状态下的索引映射（实时预览）
    var selectedOutfits = currentOutfitDataList.Where(o => outfitSelections[o]).ToList();
    var previewIndexMap = selectedOutfits
      .SelectMany(o => o.GetAllObjects().Where(obj => outfitObjectSelections[o].ContainsKey(obj) && outfitObjectSelections[o][obj]))
      .Distinct()
      .OrderBy(go => Utils.GetHierarchyPath(costumesRoot, go))
      .Select((go, index) => new { go, index })
      .ToDictionary(x => x.go, x => x.index);

    EditorGUILayout.LabelField($"总计: {currentOutfitDataList.Count} 个服装, 已选中: {selectedOutfits.Count} 个", EditorStyles.boldLabel);
    EditorGUILayout.Space(5);

    for (int outfitIdx = 0; outfitIdx < currentOutfitDataList.Count; outfitIdx++)
    {
      var outfit = currentOutfitDataList[outfitIdx];

      string displayName = outfit.HasVariants()
        ? Utils.GetRelativePath(costumesRoot, outfit.OutfitObject)
        : outfit.RelativePath;
      if (string.IsNullOrEmpty(displayName)) displayName = outfit.Name;

      // Outfit 标题行
      EditorGUILayout.BeginHorizontal();
      bool newOutfitSelection = EditorGUILayout.ToggleLeft($"{displayName}", outfitSelections[outfit], EditorStyles.boldLabel, GUILayout.Width(300));
      if (newOutfitSelection != outfitSelections[outfit])
      {
        outfitSelections[outfit] = newOutfitSelection;
        Repaint(); // 刷新界面以更新索引
      }
      EditorGUILayout.LabelField(outfit.HasVariants() ? $"({outfit.GetAllObjects().Count} 个变体)" : "", GUILayout.Width(100));
      EditorGUILayout.EndHorizontal();

      if (outfitSelections[outfit])
      {
        // 显示所有服装对象（本体+变体）
        foreach (var obj in outfit.GetAllObjects())
        {
          bool objSelected = outfitObjectSelections[outfit][obj];
          int objIndex = objSelected && previewIndexMap.ContainsKey(obj) ? previewIndexMap[obj] : -1;

          EditorGUILayout.BeginHorizontal();
          GUILayout.Space(20); // 使用空格缩进而不是 indentLevel
          bool newObjSelection = EditorGUILayout.Toggle(objSelected, GUILayout.Width(20));
          if (newObjSelection != objSelected)
          {
            outfitObjectSelections[outfit][obj] = newObjSelection;
            Repaint(); // 刷新界面以更新索引
          }
          EditorGUILayout.LabelField(obj.name, GUILayout.Width(200));
          if (objIndex >= 0)
            EditorGUILayout.LabelField($"[{costumeParamName} = {objIndex}]", EditorStyles.label, GUILayout.Width(150));
          else
            EditorGUILayout.LabelField("(未选中)", GUILayout.Width(150));
          EditorGUILayout.EndHorizontal();
        }

        // 显示部件区域
        if (enableParts && outfit.Parts.Count > 0)
        {
          EditorGUILayout.Space(3);
          EditorGUILayout.BeginHorizontal();
          GUILayout.Space(20);
          EditorGUILayout.LabelField("部件:", EditorStyles.boldLabel);
          EditorGUILayout.EndHorizontal();

          foreach (var part in outfit.Parts)
          {
            var partPath = Utils.GetRelativePath(outfit.BaseObject, part);
            var partParam = Utils.BuildParamName(paramPrefix, outfit.RelativePath + "/" + partPath);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);
            bool currentPartSelection = partSelections[outfit][part];
            bool newPartSelection = EditorGUILayout.Toggle(currentPartSelection, GUILayout.Width(20));
            if (newPartSelection != currentPartSelection)
            {
              partSelections[outfit][part] = newPartSelection;
            }
            EditorGUILayout.LabelField(part.name, GUILayout.Width(200));
            EditorGUILayout.LabelField($"[{partParam}]", GUILayout.Width(250));
            EditorGUILayout.EndHorizontal();
          }
        }

        EditorGUILayout.Space(8);
      }
    }

    EditorGUILayout.EndScrollView();
  }

  private void Generate()
  {
    if (costumesRoot == null)
    {
      EditorUtility.DisplayDialog("错误", "请先指定 Costumes Root。", "确定");
      return;
    }

    // 构建过滤后的服装列表（与预览逻辑完全一致）
    var selectedOutfits = currentOutfitDataList
      .Where(o => outfitSelections[o])
      .Select(o => new OutfitData
      {
        BaseObject = o.BaseObject,
        OutfitObject = o.OutfitObject,
        // 只保留被勾选的变体
        Variants = o.Variants.Where(v => outfitObjectSelections[o].ContainsKey(v) && outfitObjectSelections[o][v]).ToList(),
        // 只保留被勾选的部件
        Parts = o.Parts.Where(p => partSelections[o].ContainsKey(p) && partSelections[o][p]).ToList(),
        Name = o.Name,
        RelativePath = o.RelativePath,
        IsDefaultOutfit = o.IsDefaultOutfit
      })
      .ToList();

    if (selectedOutfits.Count == 0)
    {
      EditorUtility.DisplayDialog("错误", "没有选中任何服装", "确定");
      return;
    }

    // 生成索引映射（与预览完全一致的逻辑）
    var outfitIndexMap = selectedOutfits
      .SelectMany(o => o.GetAllObjects().Where(obj =>
      {
        var origOutfit = currentOutfitDataList.First(orig => orig.BaseObject == o.BaseObject);
        return outfitObjectSelections[origOutfit].ContainsKey(obj) && outfitObjectSelections[origOutfit][obj];
      }))
      .Distinct()
      .OrderBy(go => Utils.GetHierarchyPath(costumesRoot, go))
      .Select((go, index) => new { go, index })
      .ToDictionary(x => x.go, x => x.index);

    // 查找默认服装
    OutfitData defaultOutfit = null;
    if (defaultOutfitOverride != null)
    {
      defaultOutfit = selectedOutfits.FirstOrDefault(o =>
        o.BaseObject == defaultOutfitOverride ||
        o.BaseObject.transform.IsChildOf(defaultOutfitOverride.transform) ||
        defaultOutfitOverride.transform.IsChildOf(o.BaseObject.transform));
    }
    if (defaultOutfit == null)
    {
      defaultOutfit = selectedOutfits.FirstOrDefault(o =>
        DefaultOutfitHints.Any(h => (o.Name + "/" + o.RelativePath).ToLowerInvariant().Contains(h)));
    }
    if (defaultOutfit == null) defaultOutfit = selectedOutfits[0];

    // 确认生成
    if (!ShowPreflightDialog(selectedOutfits, defaultOutfit)) return;

    string controllerPath = Path.Combine(generatedFolder, "CostumeController.controller").Replace("\\", "/");
    if (File.Exists(controllerPath))
    {
      if (!EditorUtility.DisplayDialog("覆盖确认", $"控制器文件已存在:\n{controllerPath}\n\n是否覆盖?", "覆盖", "取消"))
        return;
    }

    try
    {
      EditorUtility.DisplayProgressBar("生成中", "初始化...", 0.1f);

      int undoGroup = Undo.GetCurrentGroup();
      Undo.SetCurrentGroupName("Generate Advanced Costume Controller");

      // 创建菜单根节点
      var menuRoot = Utils.PrepareChildRoot(costumesRoot, costumesRoot.name + " Menu");
      Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot);
      var mergeAnimator = Undo.AddComponent<ModularAvatarMergeAnimator>(menuRoot);
      var rootParams = Utils.EnsureParametersComponent(menuRoot);
      Utils.EnsureSubmenuOnNode(menuRoot, costumesRoot.name);

      EditorUtility.DisplayProgressBar("生成中", "构建菜单...", 0.3f);
      BuildMenus(menuRoot, selectedOutfits, outfitIndexMap, rootParams, defaultOutfit);

      EditorUtility.DisplayProgressBar("生成中", "创建动画控制器...", 0.7f);
      if (!Directory.Exists(generatedFolder))
      {
        Directory.CreateDirectory(generatedFolder);
        AssetDatabase.Refresh();
      }

      var controller = CreateAnimatorController(selectedOutfits, outfitIndexMap, defaultOutfit, controllerPath);

      // 配置 MergeAnimator
      mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
      mergeAnimator.pathMode = MergeAnimatorPathMode.Relative;
      mergeAnimator.relativePathRoot = new AvatarObjectReference();
      mergeAnimator.relativePathRoot.Set(costumesRoot);
      mergeAnimator.matchAvatarWriteDefaults = true;
      mergeAnimator.animator = controller;

      EditorUtility.SetDirty(menuRoot);
      EditorUtility.SetDirty(mergeAnimator);
      Undo.CollapseUndoOperations(undoGroup);

      Debug.Log($"[AdvancedCostumeController] 生成完成: {selectedOutfits.Count} 个服装, {selectedOutfits.Sum(o => o.Parts.Count)} 个部件");
    }
    finally
    {
      EditorUtility.ClearProgressBar();
    }
  }

  private void BuildMenus(GameObject menuRoot, List<OutfitData> outfits, Dictionary<GameObject, int> outfitIndexMap, ModularAvatarParameters rootParams, OutfitData defaultOutfit)
  {
    // 添加服装参数
    int defaultIndex = defaultOutfit != null && outfitIndexMap.ContainsKey(defaultOutfit.BaseObject)
      ? outfitIndexMap[defaultOutfit.BaseObject] : 0;
    Utils.AddOrUpdateParameter(rootParams, costumeParamName, ParameterSyncType.Int, defaultIndex, true);

    // 处理每个服装
    foreach (var outfit in outfits)
    {
      // 获取菜单父路径（如果有的话）
      string outfitPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
      var pathParts = outfitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

      // 创建父级菜单路径
      GameObject parentMenu = menuRoot;
      for (int i = 0; i < pathParts.Length - 1; i++)
      {
        parentMenu = Utils.FindOrCreateChild(parentMenu, pathParts[i]);
        Utils.EnsureSubmenuOnNode(parentMenu);
      }

      // 获取outfit的名称（最后一级）
      string outfitName = pathParts.Length > 0 ? pathParts[pathParts.Length - 1] : outfit.Name;

      // 判断是否需要创建子菜单
      bool needSubmenu = enableParts || outfit.HasVariants();

      if (needSubmenu)
      {
        // 创建outfit的子菜单
        var outfitSubmenu = Utils.FindOrCreateChild(parentMenu, outfitName);
        Utils.EnsureSubmenuOnNode(outfitSubmenu);

        // 如果启用部件控制，Parts 菜单放在第一位
        if (enableParts && outfit.Parts.Count > 0)
        {
          var partsMenu = Utils.FindOrCreateChild(outfitSubmenu, "Parts");
          Utils.EnsureSubmenuOnNode(partsMenu);

          foreach (var part in outfit.Parts)
          {
            string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
            string partParamName = Utils.BuildParamName(paramPrefix, outfit.RelativePath + "/" + partRelPath);
            bool partDefaultActive = part.activeSelf;

            var partNode = Utils.FindOrCreateChild(partsMenu, part.name);
            var partItem = Utils.CreateMenuItem(partNode);

            partItem.PortableControl.Type = PortableControlType.Toggle;
            partItem.PortableControl.Parameter = partParamName;
            partItem.automaticValue = true;
            partItem.isDefault = partDefaultActive;
            partItem.isSaved = true;
            partItem.isSynced = true;

            Utils.AddOrUpdateParameter(rootParams, partParamName, ParameterSyncType.Bool, partDefaultActive ? 1 : 0, true);
          }
        }

        // 添加本体和变体的开关
        var allOutfitObjects = outfit.GetAllObjects();
        foreach (var obj in allOutfitObjects)
        {
          if (!outfitIndexMap.ContainsKey(obj)) continue;

          var itemNode = Utils.FindOrCreateChild(outfitSubmenu, obj.name);
          var menuItem = Utils.CreateMenuItem(itemNode);

          menuItem.PortableControl.Type = PortableControlType.Toggle;
          menuItem.PortableControl.Parameter = costumeParamName;
          menuItem.PortableControl.Value = outfitIndexMap[obj];
          menuItem.automaticValue = false;
          menuItem.isSaved = true;
          menuItem.isSynced = true;
        }
      }
      else
      {
        // 没有变体且不启用部件控制：直接创建开关
        if (!outfitIndexMap.ContainsKey(outfit.BaseObject)) continue;

        var itemNode = Utils.FindOrCreateChild(parentMenu, outfitName);
        var menuItem = Utils.CreateMenuItem(itemNode);

        menuItem.PortableControl.Type = PortableControlType.Toggle;
        menuItem.PortableControl.Parameter = costumeParamName;
        menuItem.PortableControl.Value = outfitIndexMap[outfit.BaseObject];
        menuItem.automaticValue = false;
        menuItem.isSaved = true;
        menuItem.isSynced = true;
      }
    }

    // TODO: CustomMixer 功能（如果需要）
  }

  private AnimatorController CreateAnimatorController(List<OutfitData> outfits, Dictionary<GameObject, int> outfitIndexMap, OutfitData defaultOutfit, string path)
  {
    var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

    // 添加服装参数
    controller.AddParameter(costumeParamName, AnimatorControllerParameterType.Int);

    // 添加部件参数
    if (enableParts)
    {
      foreach (var outfit in outfits)
      {
        foreach (var part in outfit.Parts)
        {
          string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
          string partParamName = Utils.BuildParamName(paramPrefix, outfit.RelativePath + "/" + partRelPath);
          // Animator 中使用 Float 类型
          controller.AddParameter(partParamName, AnimatorControllerParameterType.Float);
        }
      }
    }

    // 创建服装切换层
    CreateOutfitSwitchingLayer(controller, outfits, outfitIndexMap, defaultOutfit);

    // 创建部件初始化层和控制层
    if (enableParts)
    {
      // 先创建初始化层,确保所有部件初始为 OFF
      CreatePartsInitLayer(controller, outfits);

      // 创建一个统一的部件控制层,包含所有 outfit 的所有部件
      CreatePartsControlLayer(controller, outfits);
    }

    AssetDatabase.SaveAssets();
    return controller;
  }

  private void CreateOutfitSwitchingLayer(AnimatorController controller, List<OutfitData> outfits, Dictionary<GameObject, int> outfitIndexMap, OutfitData defaultOutfit)
  {
    var layer = new AnimatorControllerLayer
    {
      name = "Outfit Switching",
      defaultWeight = 1f,
      stateMachine = new AnimatorStateMachine()
    };
    layer.stateMachine.name = layer.name;
    layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
    AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);

    // 使用 outfitIndexMap 获取所有对象及其索引
    var allObjects = outfitIndexMap.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

    AnimatorState defaultState = null;
    foreach (var obj in allObjects)
    {
      int index = outfitIndexMap[obj];
      var state = layer.stateMachine.AddState(obj.name, new Vector3(300, 50 + index * 60, 0));

      // 创建动画
      var clip = CreateOutfitSwitchAnimation(outfits, allObjects, obj, index);
      state.motion = clip;
      state.writeDefaultValues = true;

      if (defaultOutfit != null && obj == defaultOutfit.BaseObject)
        defaultState = state;

      var transition = layer.stateMachine.AddAnyStateTransition(state);
      transition.AddCondition(AnimatorConditionMode.Equals, index, costumeParamName);
      transition.duration = 0;
      transition.hasExitTime = false;
    }

    if (defaultState != null)
      layer.stateMachine.defaultState = defaultState;

    controller.AddLayer(layer);
  }

  private AnimationClip CreateOutfitSwitchAnimation(List<OutfitData> outfits, List<GameObject> allObjects, GameObject activeObject, int index)
  {
    string animFolder = Path.Combine(generatedFolder, "Animations");
    if (!Directory.Exists(animFolder))
    {
      Directory.CreateDirectory(animFolder);
      AssetDatabase.Refresh();
    }

    string sanitizedName = Utils.SanitizeForFileName(activeObject.name);
    string animPath = Path.Combine(animFolder, $"Outfit_{index:D3}_{sanitizedName}.anim").Replace("\\", "/");
    var clip = new AnimationClip { legacy = false, wrapMode = WrapMode.Once };

    var settings = AnimationUtility.GetAnimationClipSettings(clip);
    settings.loopTime = false;
    AnimationUtility.SetAnimationClipSettings(clip, settings);

    // 找出 activeObject 所属的 outfit
    var activeOutfit = outfits.FirstOrDefault(o => o.GetAllObjects().Contains(activeObject));

    foreach (var obj in allObjects)
    {
      bool active = false;

      if (obj == activeObject)
      {
        // 激活的对象本身
        active = true;
      }
      else if (activeOutfit != null && obj == activeOutfit.BaseObject && activeOutfit.Variants.Contains(activeObject))
      {
        // 如果 activeObject 是变体，也要启用其本体
        active = true;
      }

      var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
      string path = Utils.GetRelativePath(costumesRoot, obj);
      clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
    }

    AssetDatabase.CreateAsset(clip, animPath);
    return clip;
  }

  private void CreatePartsInitLayer(AnimatorController controller, List<OutfitData> outfits)
  {
    // 创建初始化层,确保所有部件初始为 OFF
    var layer = new AnimatorControllerLayer
    {
      name = "Parts Init",
      defaultWeight = 1f,
      stateMachine = new AnimatorStateMachine()
    };
    layer.stateMachine.name = layer.name;
    layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
    AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);

    // 创建一个空动画,设置所有部件为 OFF
    string animFolder = Path.Combine(generatedFolder, "Animations");
    if (!Directory.Exists(animFolder))
    {
      Directory.CreateDirectory(animFolder);
      AssetDatabase.Refresh();
    }

    string animPath = Path.Combine(animFolder, "PartsInit_OFF.anim").Replace("\\", "/");
    var clip = new AnimationClip { legacy = false, wrapMode = WrapMode.Once };
    var settings = AnimationUtility.GetAnimationClipSettings(clip);
    settings.loopTime = false;
    AnimationUtility.SetAnimationClipSettings(clip, settings);

    // 为所有部件添加 OFF 曲线
    foreach (var outfit in outfits)
    {
      foreach (var part in outfit.Parts)
      {
        var curve = AnimationCurve.Constant(0, 1f / 60f, 0f);
        string path = Utils.GetRelativePath(costumesRoot, part);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }
    }

    AssetDatabase.CreateAsset(clip, animPath);

    // 创建状态
    var state = layer.stateMachine.AddState("Init", new Vector3(300, 50, 0));
    state.motion = clip;
    state.writeDefaultValues = true;  // WD=true: 写入所有部件为 OFF 的默认值
    layer.stateMachine.defaultState = state;

    controller.AddLayer(layer);
  }

  private void CreatePartsControlLayer(AnimatorController controller, List<OutfitData> outfits)
  {
    // 检查是否有任何部件
    if (!outfits.Any(o => o.Parts.Count > 0)) return;

    // 为所有部件创建一个统一的层,使用 Direct BlendTree
    var layer = new AnimatorControllerLayer
    {
      name = "Parts Control",
      defaultWeight = 1f,
      stateMachine = new AnimatorStateMachine()
    };
    layer.stateMachine.name = layer.name;
    layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
    AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);

    // 创建 Direct BlendTree
    var blendTree = new BlendTree
    {
      name = "Parts",
      blendType = BlendTreeType.Direct,
      hideFlags = HideFlags.HideInHierarchy
    };
    AssetDatabase.AddObjectToAsset(blendTree, controller);

    // 遍历所有 outfit 的所有部件
    foreach (var outfit in outfits)
    {
      foreach (var part in outfit.Parts)
      {
        string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
        string partParamName = Utils.BuildParamName(paramPrefix, outfit.RelativePath + "/" + partRelPath);

        // 只创建 ON 动画
        var onClip = CreatePartToggleAnimation(part, true, partParamName);

        // 添加到 Direct BlendTree
        blendTree.AddChild(onClip, 0.5f);

        // 使用反射调用内部方法 SetDirectBlendTreeParameter
        int childIndex = blendTree.children.Length - 1;
        var setDirectBlendMethod = typeof(BlendTree).GetMethod("SetDirectBlendTreeParameter",
          System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (setDirectBlendMethod != null)
        {
          setDirectBlendMethod.Invoke(blendTree, new object[] { childIndex, partParamName });
        }
      }
    }

    // 创建状态
    var state = layer.stateMachine.AddState("Parts", new Vector3(300, 50, 0));
    state.motion = blendTree;
    state.writeDefaultValues = true;  // WD=true: ON 动画会覆盖初始化层的 OFF 状态
    layer.stateMachine.defaultState = state;

    controller.AddLayer(layer);
  }

  private AnimationClip CreatePartToggleAnimation(GameObject part, bool active, string paramName)
  {
    string animFolder = Path.Combine(generatedFolder, "Animations", "Parts");
    if (!Directory.Exists(animFolder))
    {
      Directory.CreateDirectory(animFolder);
      AssetDatabase.Refresh();
    }

    string sanitized = Utils.SanitizeForFileName(paramName);
    string animPath = Path.Combine(animFolder, $"{sanitized}_{(active ? "ON" : "OFF")}.anim").Replace("\\", "/");

    var clip = new AnimationClip { legacy = false, wrapMode = WrapMode.Once };
    var settings = AnimationUtility.GetAnimationClipSettings(clip);
    settings.loopTime = false;
    AnimationUtility.SetAnimationClipSettings(clip, settings);

    var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
    string path = Utils.GetRelativePath(costumesRoot, part);
    clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);

    AssetDatabase.CreateAsset(clip, animPath);
    return clip;
  }

  private bool ShowPreflightDialog(List<OutfitData> outfits, OutfitData defaultOutfit)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("即将生成，摘要：");
    sb.AppendLine($"- 服装数量：{outfits.Count}");
    sb.AppendLine($"- 部件数量：{outfits.Sum(o => o.Parts.Count)}");
    sb.AppendLine($"- 输出目录：{generatedFolder}");
    sb.AppendLine($"- 默认服装：{defaultOutfit?.Name ?? "未设置"}");

    return EditorUtility.DisplayDialog("生成确认", sb.ToString(), "继续", "取消");
  }

  private List<OutfitData> FindOutfits()
  {
    var outfitDataList = new List<OutfitData>();
    var processedBases = new HashSet<GameObject>(); // 防止重复处理同一个 base
    var stack = new Stack<Transform>();
    stack.Push(costumesRoot.transform);

    while (stack.Count > 0)
    {
      var t = stack.Pop();

      // 如果不是mesh节点，继续遍历子节点
      if (!Utils.HasMeshOn(t))
      {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
          var child = t.GetChild(i);
          bool ignored = Utils.IsNameIgnored(child.name, ignoreSet);
          if (!ignored)
            stack.Push(child);
        }
        continue;
      }

      // 找到mesh节点，其父节点是outfit base
      var outfitBase = t.parent;

      // 如果父节点为空，跳过
      if (outfitBase == null) continue;

      // 如果这个 base 已经处理过，跳过
      if (processedBases.Contains(outfitBase.gameObject)) continue;
      processedBases.Add(outfitBase.gameObject);

      // 查找变体（同级的其他节点）
      var variants = new List<GameObject>();
      var outfitParent = outfitBase.parent;

      // 只有当父节点存在且不是根节点时才查找变体
      if (outfitParent != null && outfitParent.gameObject != costumesRoot)
      {
        for (int i = 0; i < outfitParent.childCount; i++)
        {
          var sibling = outfitParent.GetChild(i);
          if (sibling != outfitBase && !Utils.IsNameIgnored(sibling.name, ignoreSet))
            variants.Add(sibling.gameObject);
        }
      }

      var outfitObject = variants.Count > 0 ? outfitParent.gameObject : outfitBase.gameObject;

      // 收集部件
      var parts = new List<GameObject>();
      for (int i = 0; i < outfitBase.childCount; i++)
      {
        var child = outfitBase.GetChild(i);
        if (!Utils.IsNameIgnored(child.name, ignoreSet))
          parts.Add(child.gameObject);
      }

      outfitDataList.Add(new OutfitData
      {
        BaseObject = outfitBase.gameObject,
        OutfitObject = outfitObject,
        Name = outfitObject.name,
        RelativePath = Utils.GetRelativePath(costumesRoot, outfitObject),
        Parts = parts,
        Variants = variants
      });
    }

    return outfitDataList;
  }
}
