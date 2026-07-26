using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// Advanced Costume Controller 编辑器窗口
  /// 负责 UI 绘制和用户交互，不包含核心生成逻辑
  /// </summary>
  public class Window : EditorWindow
  {
    // ── 配置 ──
    private ACCConfig config = new ACCConfig();

    // ── 运行时状态 ──
    private List<OutfitData> currentOutfitDataList = new();
    private Dictionary<OutfitData, bool> outfitSelections = new();
    private Dictionary<OutfitData, Dictionary<GameObject, bool>> outfitObjectSelections = new();
    private Dictionary<OutfitData, Dictionary<GameObject, bool>> partSelections = new();
    private Dictionary<OutfitData, Dictionary<GameObject, string>> partGroupNames = new();
    private bool previewFoldout = true;
    private Vector2 scrollPosition = Vector2.zero;

[MenuItem("Tools/UnityBox/Advanced Costume Controller")]
    public static void ShowWindow() => GetWindow<Window>("Advanced Costume Controller");

    private string T(string chinese, string english) => Localization.Text(config, chinese, english);

    private void OnEnable()
    {
      config.ApplyAutoDefaultsFromRoot();
      if (config.CostumesRoot != null) RefreshPreview();
    }

    #region 预览刷新

    private void RefreshPreview()
    {
      currentOutfitDataList.Clear();
      outfitSelections.Clear();
      outfitObjectSelections.Clear();
      partSelections.Clear();
      partGroupNames.Clear();

      if (config.CostumesRoot == null) return;

      currentOutfitDataList = Scanner.FindOutfits(config.CostumesRoot);

      foreach (var outfit in currentOutfitDataList)
      {
        outfitSelections[outfit] = true;

        outfitObjectSelections[outfit] = new Dictionary<GameObject, bool>();
        foreach (var obj in outfit.GetAllObjects())
          outfitObjectSelections[outfit][obj] = true;

        partSelections[outfit] = new Dictionary<GameObject, bool>();
        partGroupNames[outfit] = new Dictionary<GameObject, string>();
        foreach (var part in outfit.Parts)
        {
          partSelections[outfit][part] = true;
          partGroupNames[outfit][part] = "";
        }

        foreach (var control in outfit.GetPartControls())
        {
          if (!control.IsGroup) continue;
          foreach (var part in control.Parts)
          {
            if (partGroupNames[outfit].ContainsKey(part))
              partGroupNames[outfit][part] = control.Name;
          }
        }
      }
    }

    #endregion

    #region UI 绘制

    private void OnGUI()
    {
      EditorGUILayout.LabelField("Advanced Costume Controller", EditorStyles.boldLabel);

      var languageOptions = new[]
      {
        "Auto / 自动",
        "English",
        "中文"
      };
      config.Language = (ACCLanguage)EditorGUILayout.Popup(
        T("语言", "Language"), (int)config.Language, languageOptions);

      DrawConfigSection();
      DrawHelpBox();
      DrawPreviewSection();
      DrawGenerateButton();
    }

    private void DrawConfigSection()
    {
      var oldRoot = config.CostumesRoot;
      config.CostumesRoot = (GameObject)EditorGUILayout.ObjectField(
        T("服装根节点", "Costumes Root"), config.CostumesRoot, typeof(GameObject), true);
      if (config.CostumesRoot != oldRoot)
      {
        config.ApplyAutoDefaultsFromRoot();
        RefreshPreview();
      }

      var paramPrefix = EditorGUILayout.TextField(T("参数前缀", "Parameter Prefix"), config.ParamPrefix);
      if (paramPrefix != config.ParamPrefix)
      {
        config.ParamPrefix = paramPrefix;
        config.AutoParamPrefix = false;
      }

      EditorGUILayout.HelpBox(
        T("参数前缀同时作为主服装 Int 参数、Animator Layer 和生成文件的命名空间；同一 Avatar 上必须唯一。",
          "Parameter Prefix is also the main costume Int parameter and the namespace for Animator Layers and generated files; it must be unique per Avatar."),
        MessageType.Info);

      config.DefaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField(
        T("默认服装（可选）", "Default Outfit (optional)"), config.DefaultOutfitOverride, typeof(GameObject), true);
      config.EnableParts = EditorGUILayout.Toggle(T("启用部件控制", "Enable Parts Control"), config.EnableParts);
      if (!config.EnableParts)
        config.EnableCustomMixer = false;

      using (new EditorGUI.DisabledScope(!config.EnableParts))
        config.EnableCustomMixer = EditorGUILayout.Toggle(T("启用混搭模式", "Enable Custom Mixer"), config.EnableCustomMixer);
      if (config.EnableCustomMixer)
      {
        EditorGUI.indentLevel++;
        config.CustomMixerName = EditorGUILayout.TextField(T("混搭菜单名称", "Custom Mixer Name"), config.CustomMixerName);
        EditorGUILayout.HelpBox(
          T("混搭模式会激活各服装本体，再用独立参数控制部件与变体；必须启用部件控制。",
            "Custom Mixer activates outfit bases, then controls parts and variants with independent parameters; Parts Control is required."),
          MessageType.Info);
        EditorGUI.indentLevel--;
      }

      EditorGUILayout.Space(5);
      DrawFolderPicker();
    }

    private void DrawFolderPicker()
    {
      EditorGUILayout.BeginHorizontal();
      config.GeneratedFolder = EditorGUILayout.TextField(T("输出目录", "Output Folder"), config.GeneratedFolder);

      if (GUILayout.Button(T("浏览…", "Browse…"), GUILayout.MaxWidth(80)))
      {
        string defaultPath = Application.dataPath;
        if (!string.IsNullOrEmpty(config.GeneratedFolder) && config.GeneratedFolder.StartsWith("Assets"))
        {
          var assetsAbs = Application.dataPath.Replace('\\', '/');
          if (config.GeneratedFolder == "Assets")
          {
            defaultPath = assetsAbs;
          }
          else
          {
            var relativePart = config.GeneratedFolder.Substring("Assets/".Length);
            defaultPath = Path.Combine(assetsAbs, relativePart).Replace('\\', '/');
            while (!Directory.Exists(defaultPath) && defaultPath.Length > assetsAbs.Length)
            {
              defaultPath = Path.GetDirectoryName(defaultPath)?.Replace('\\', '/');
              if (string.IsNullOrEmpty(defaultPath))
              {
                defaultPath = assetsAbs;
                break;
              }
            }
            if (!Directory.Exists(defaultPath))
              defaultPath = assetsAbs;
          }
        }

        var abs = EditorUtility.OpenFolderPanel(T("选择 Assets 下的目录", "Select folder under Assets"), defaultPath, "");
        if (!string.IsNullOrEmpty(abs))
        {
          var assetsAbs = Application.dataPath.Replace('\\', '/');
          abs = abs.Replace('\\', '/');
          if (abs.StartsWith(assetsAbs))
          {
            config.GeneratedFolder = abs.Length == assetsAbs.Length
              ? "Assets"
              : ("Assets/" + abs.Substring(assetsAbs.Length + 1));
          }
          else
          {
            EditorUtility.DisplayDialog(T("无效目录", "Invalid Folder"),
              T("请选择 Assets 目录内的文件夹。", "Please select a folder under Assets."), "OK");
          }
        }
      }
      EditorGUILayout.EndHorizontal();
    }

    private void DrawHelpBox()
    {
      EditorGUILayout.HelpBox(
        T("使用指南：\n1) 选择服装根节点\n2) 刷新预览并选择服装和部件\n3) 点击生成",
          "Quick start:\n1) Select Costumes Root\n2) Refresh and select outfits and parts\n3) Click Generate"),
        MessageType.Info);
    }

    private void DrawGenerateButton()
    {
      using (new EditorGUI.DisabledScope(config.CostumesRoot == null))
      {
        if (GUILayout.Button(T("生成", "Generate")))
        {
          try { DoGenerate(); }
          catch (Exception ex) { Debug.LogError($"[ACC] Generation failed: {ex}"); }
        }
      }
    }

    #endregion

    #region 预览区域

    private void DrawPreviewSection()
    {
      if (config.CostumesRoot == null) return;

      if (GUILayout.Button(T("刷新预览", "Refresh Preview")))
        RefreshPreview();

      if (currentOutfitDataList.Count == 0)
      {
        EditorGUILayout.HelpBox(T("未找到拥有骨架和网格的服装。", "No outfit with both a skeleton and mesh was found."),
          MessageType.Warning);
        return;
      }

      previewFoldout = EditorGUILayout.Foldout(previewFoldout, T("预览服装和部件", "Outfit and Parts Preview"), true);
      if (!previewFoldout) return;

      scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(400));

      // 计算当前选择下的实时索引映射
      var selectedOutfits = currentOutfitDataList.Where(o => outfitSelections[o]).ToList();
      var previewIndexMap = BuildIndexMap(selectedOutfits);

      EditorGUILayout.LabelField(
        T($"总计：{currentOutfitDataList.Count} 个服装，已选中：{selectedOutfits.Count} 个",
          $"Total: {currentOutfitDataList.Count} outfits, selected: {selectedOutfits.Count}"),
        EditorStyles.boldLabel);
      EditorGUILayout.Space(5);

      foreach (var outfit in currentOutfitDataList)
      {
        DrawOutfitPreview(outfit, previewIndexMap);
      }

      EditorGUILayout.EndScrollView();
    }

    private void DrawOutfitPreview(OutfitData outfit, Dictionary<GameObject, int> previewIndexMap)
    {
      string displayName = outfit.HasVariants()
        ? Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject)
        : outfit.RelativePath;
      if (string.IsNullOrEmpty(displayName)) displayName = outfit.Name;

      // 标题行
      EditorGUILayout.BeginHorizontal();
      bool newSel = EditorGUILayout.ToggleLeft(displayName, outfitSelections[outfit],
        EditorStyles.boldLabel, GUILayout.Width(300));
      if (newSel != outfitSelections[outfit])
      {
        outfitSelections[outfit] = newSel;
        Repaint();
      }
      EditorGUILayout.LabelField(
        outfit.HasVariants() ? $"({outfit.GetAllObjects().Count} 个变体)" : "",
        GUILayout.Width(100));
      EditorGUILayout.EndHorizontal();

      if (!outfitSelections[outfit]) return;

      // 服装对象（本体 + 变体）
      foreach (var obj in outfit.GetAllObjects())
      {
        bool objSel = outfitObjectSelections[outfit][obj];
        int objIndex = objSel && previewIndexMap.ContainsKey(obj) ? previewIndexMap[obj] : -1;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        bool newObjSel = EditorGUILayout.Toggle(objSel, GUILayout.Width(20));
        if (newObjSel != objSel)
        {
          outfitObjectSelections[outfit][obj] = newObjSel;
          Repaint();
        }
        EditorGUILayout.LabelField(obj.name, GUILayout.Width(200));
        EditorGUILayout.LabelField(
          objIndex >= 0 ? $"[{config.MainParameterName} = {objIndex}]" : T("（未选中）", "(not selected)"),
          GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
      }

      // 部件区域（启用 Parts Control 或 Custom Mixer 时都需显示）
      if ((config.EnableParts || config.EnableCustomMixer) && outfit.Parts.Count > 0)
      {
        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        EditorGUILayout.LabelField(T("部件：", "Parts:"), EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        foreach (var part in outfit.Parts)
        {
          var partParam = GetPreviewPartParamName(outfit, part);

          EditorGUILayout.BeginHorizontal();
          GUILayout.Space(20);
          bool curPartSel = partSelections[outfit][part];
          bool newPartSel = EditorGUILayout.Toggle(curPartSel, GUILayout.Width(20));
          if (newPartSel != curPartSel)
            partSelections[outfit][part] = newPartSel;
          EditorGUILayout.LabelField(part.name, GUILayout.Width(200));
          EditorGUILayout.LabelField($"[{partParam}]", GUILayout.Width(250));
          EditorGUILayout.LabelField(T("分组", "Group"), GUILayout.Width(45));
          partGroupNames[outfit][part] = EditorGUILayout.TextField(
            partGroupNames[outfit][part], GUILayout.Width(120));
          EditorGUILayout.EndHorizontal();
        }
      }

      EditorGUILayout.Space(8);
    }

    private string GetPreviewPartParamName(OutfitData outfit, GameObject part)
    {
      string groupName = partGroupNames[outfit].TryGetValue(part, out var value)
        ? value.Trim() : "";
      string controlPath = string.IsNullOrEmpty(groupName)
        ? Utils.GetRelativePath(outfit.BaseObject, part)
        : "Groups/" + groupName;
      return Utils.BuildParamName(config.ParamPrefix, outfit.RelativePath + "/" + controlPath);
    }

    #endregion

    #region 生成逻辑

    private void DoGenerate()
    {
      if (config.CostumesRoot == null)
      {
        EditorUtility.DisplayDialog(T("错误", "Error"), T("请先指定服装根节点。", "Please select Costumes Root."), "OK");
        return;
      }

      if (config.EnableCustomMixer && !config.EnableParts)
      {
        EditorUtility.DisplayDialog(T("错误", "Error"),
          T("Custom Mixer 需要启用部件控制。", "Custom Mixer requires Parts Control."), "OK");
        return;
      }

      if (!Utils.HasUsableGenerationNamespace(config.MainParameterName))
      {
        EditorUtility.DisplayDialog(T("错误", "Error"),
          T("参数前缀必须至少包含一个字母或数字。", "Parameter Prefix must contain at least one letter or digit."), "OK");
        return;
      }

      if (!Utils.IsSafeAssetsFolder(config.GeneratedFolder))
      {
        EditorUtility.DisplayDialog(T("错误", "Error"),
          T("参数前缀必须可用于生成文件，且输出目录必须是 Assets 下不含 . 或 .. 的相对路径。",
            "Parameter Prefix must be usable in generated filenames, and Output Folder must be an Assets-relative path without . or .. segments."),
          "OK");
        return;
      }

      if (config.EnableCustomMixer && string.IsNullOrWhiteSpace(config.CustomMixerName))
      {
        EditorUtility.DisplayDialog(T("错误", "Error"),
          T("混搭菜单名称不能为空。", "Custom Mixer Name cannot be empty."), "OK");
        return;
      }

      // 构建过滤后的服装列表
      var selectedOutfits = new List<OutfitData>();
      foreach (var o in currentOutfitDataList)
      {
        if (!outfitSelections[o]) continue;

        bool baseSelected = outfitObjectSelections[o].ContainsKey(o.BaseObject) && outfitObjectSelections[o][o.BaseObject];
        var selectedVariants = o.Variants
          .Where(v => outfitObjectSelections[o].ContainsKey(v) && outfitObjectSelections[o][v]).ToList();

        // 本体和所有变体都未选中时，跳过此服装
        if (!baseSelected && selectedVariants.Count == 0) continue;

        selectedOutfits.Add(new OutfitData
        {
          BaseObject = o.BaseObject,
          OutfitObject = o.OutfitObject,
          Variants = selectedVariants,
          Parts = o.Parts.Where(p =>
            partSelections[o].ContainsKey(p) && partSelections[o][p]).ToList(),
          PartControls = BuildPartControls(o),
          Name = o.Name,
          RelativePath = o.RelativePath,
          IsDefaultOutfit = o.IsDefaultOutfit,
          IsBaseSelected = baseSelected
        });
      }

      if (selectedOutfits.Count == 0)
      {
        EditorUtility.DisplayDialog(T("错误", "Error"), T("没有选中任何服装。", "No outfit is selected."), "OK");
        return;
      }

      // 生成索引映射（仅包含用户勾选的对象）
      var outfitIndexMap = BuildIndexMapFromSelected(selectedOutfits);

      // 查找默认服装
      var defaultOutfit = Scanner.FindDefaultOutfit(selectedOutfits, config.DefaultOutfitOverride);

      // 执行生成
      var generator = new Generator(config);
      generator.Execute(selectedOutfits, outfitIndexMap, defaultOutfit);
    }

    private List<PartControlData> BuildPartControls(OutfitData outfit)
    {
      var result = new List<PartControlData>();
      var groups = new Dictionary<string, PartControlData>();

      foreach (var part in outfit.Parts)
      {
        if (!partSelections[outfit].TryGetValue(part, out var selected) || !selected)
          continue;

        string groupName = partGroupNames[outfit].TryGetValue(part, out var value)
          ? value.Trim() : "";
        string key = string.IsNullOrEmpty(groupName)
          ? "@" + part.GetInstanceID() : groupName;
        if (!groups.TryGetValue(key, out var control))
        {
          control = new PartControlData
          {
            Name = string.IsNullOrEmpty(groupName) ? part.name : groupName,
            IsGroup = !string.IsNullOrEmpty(groupName)
          };
          groups[key] = control;
          result.Add(control);
        }
        control.Parts.Add(part);
      }
      return result;
    }

    /// <summary>构建索引映射（用于预览）</summary>
    private Dictionary<GameObject, int> BuildIndexMap(List<OutfitData> selectedOutfits)
    {
      return selectedOutfits
        .SelectMany(o => o.GetAllObjects()
          .Where(obj => outfitObjectSelections[o].ContainsKey(obj) && outfitObjectSelections[o][obj]))
        .Distinct()
        .OrderBy(go => Utils.GetHierarchyPath(config.CostumesRoot, go))
        .Select((go, index) => new { go, index })
        .ToDictionary(x => x.go, x => x.index);
    }

    /// <summary>构建索引映射（用于生成，从过滤后的列表）</summary>
    private Dictionary<GameObject, int> BuildIndexMapFromSelected(List<OutfitData> selectedOutfits)
    {
      // 过滤后的 OutfitData 中 GetAllObjects() 已只包含用户选中的对象
      return selectedOutfits
        .SelectMany(o => o.GetAllObjects())
        .Distinct()
        .OrderBy(go => Utils.GetHierarchyPath(config.CostumesRoot, go))
        .Select((go, index) => new { go, index })
        .ToDictionary(x => x.go, x => x.index);
    }

    #endregion
  }
}
