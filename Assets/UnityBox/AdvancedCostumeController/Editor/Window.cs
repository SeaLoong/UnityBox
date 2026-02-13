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
    private HashSet<string> ignoreSet = new();
    private List<OutfitData> currentOutfitDataList = new();
    private Dictionary<OutfitData, bool> outfitSelections = new();
    private Dictionary<OutfitData, Dictionary<GameObject, bool>> outfitObjectSelections = new();
    private Dictionary<OutfitData, Dictionary<GameObject, bool>> partSelections = new();
    private bool previewFoldout = true;
    private Vector2 scrollPosition = Vector2.zero;

[MenuItem("Tools/UnityBox/Advanced Costume Controller")]
    public static void ShowWindow() => GetWindow<Window>("Advanced Costume Controller");

    private void OnEnable()
    {
      if (config.CostumesRoot != null) RefreshPreview();
    }

    #region 预览刷新

    private void RefreshPreview()
    {
      currentOutfitDataList.Clear();
      outfitSelections.Clear();
      outfitObjectSelections.Clear();
      partSelections.Clear();

      if (config.CostumesRoot == null) return;

      ignoreSet = Utils.BuildIgnoreSet(config.IgnoreNamesCsv);
      currentOutfitDataList = Scanner.FindOutfits(config.CostumesRoot, ignoreSet);

      foreach (var outfit in currentOutfitDataList)
      {
        outfitSelections[outfit] = true;

        outfitObjectSelections[outfit] = new Dictionary<GameObject, bool>();
        foreach (var obj in outfit.GetAllObjects())
          outfitObjectSelections[outfit][obj] = true;

        partSelections[outfit] = new Dictionary<GameObject, bool>();
        foreach (var part in outfit.Parts)
          partSelections[outfit][part] = true;
      }
    }

    #endregion

    #region UI 绘制

    private void OnGUI()
    {
      EditorGUILayout.LabelField("Advanced Costume Controller", EditorStyles.boldLabel);

      DrawConfigSection();
      DrawHelpBox();
      DrawIgnoreNames();
      DrawPreviewSection();
      DrawGenerateButton();
    }

    private void DrawConfigSection()
    {
      var oldRoot = config.CostumesRoot;
      config.CostumesRoot = (GameObject)EditorGUILayout.ObjectField(
        "Costumes Root", config.CostumesRoot, typeof(GameObject), true);
      if (config.CostumesRoot != oldRoot) RefreshPreview();

      config.ParamPrefix = EditorGUILayout.TextField("Parameter Prefix", config.ParamPrefix);
      config.CostumeParamName = EditorGUILayout.TextField("Costume Parameter Name", config.CostumeParamName);
      config.DefaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField(
        "Default Outfit (optional)", config.DefaultOutfitOverride, typeof(GameObject), true);
      config.EnableParts = EditorGUILayout.Toggle("Enable Parts Control", config.EnableParts);
      config.EnableCustomMixer = EditorGUILayout.Toggle("Enable Custom Mixer", config.EnableCustomMixer);
      if (config.EnableCustomMixer)
      {
        EditorGUI.indentLevel++;
        config.CustomMixerName = EditorGUILayout.TextField("Custom Mixer Name", config.CustomMixerName);
        EditorGUILayout.HelpBox(
          "Custom Mixer 是一个特殊服装，可以自由组合所有服装的部件和变体。\n" +
          "启用后会生成一个额外的菜单入口和对应的独立动画层，不依赖 Parts Control。",
          MessageType.Info);
        EditorGUI.indentLevel--;
      }

      EditorGUILayout.Space(5);
      DrawFolderPicker();
    }

    private void DrawFolderPicker()
    {
      EditorGUILayout.BeginHorizontal();
      config.GeneratedFolder = EditorGUILayout.TextField("Output Folder", config.GeneratedFolder);

      if (GUILayout.Button("Browse…", GUILayout.MaxWidth(80)))
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

        var abs = EditorUtility.OpenFolderPanel("Select folder under Assets", defaultPath, "");
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
            EditorUtility.DisplayDialog("Invalid Folder", "请选择 Assets 目录内的文件夹。", "OK");
          }
        }
      }
      EditorGUILayout.EndHorizontal();
    }

    private void DrawHelpBox()
    {
      EditorGUILayout.HelpBox(
        "使用指南：\n" +
        "1) 选择 Costumes Root\n" +
        "2) 预览并选择要生成的服装和部件\n" +
        "3) 点击 Generate 生成控制菜单",
        MessageType.Info);
    }

    private void DrawIgnoreNames()
    {
      EditorGUILayout.LabelField("Ignore Names (逗号/分号/换行分隔)");
      config.IgnoreNamesCsv = EditorGUILayout.TextArea(config.IgnoreNamesCsv, GUILayout.MinHeight(40));
    }

    private void DrawGenerateButton()
    {
      using (new EditorGUI.DisabledScope(config.CostumesRoot == null))
      {
        if (GUILayout.Button("Generate"))
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
      if (config.CostumesRoot == null || currentOutfitDataList.Count == 0) return;

      previewFoldout = EditorGUILayout.Foldout(previewFoldout, "预览服装和部件", true);
      if (!previewFoldout) return;

      scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(400));

      // 计算当前选择下的实时索引映射
      var selectedOutfits = currentOutfitDataList.Where(o => outfitSelections[o]).ToList();
      var previewIndexMap = BuildIndexMap(selectedOutfits);

      EditorGUILayout.LabelField(
        $"总计: {currentOutfitDataList.Count} 个服装, 已选中: {selectedOutfits.Count} 个",
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
          objIndex >= 0 ? $"[{config.CostumeParamName} = {objIndex}]" : "(未选中)",
          GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
      }

      // 部件区域（启用 Parts Control 或 Custom Mixer 时都需显示）
      if ((config.EnableParts || config.EnableCustomMixer) && outfit.Parts.Count > 0)
      {
        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        EditorGUILayout.LabelField("部件:", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        foreach (var part in outfit.Parts)
        {
          var partPath = Utils.GetRelativePath(outfit.BaseObject, part);
          var partParam = Utils.BuildParamName(config.ParamPrefix, outfit.RelativePath + "/" + partPath);

          EditorGUILayout.BeginHorizontal();
          GUILayout.Space(20);
          bool curPartSel = partSelections[outfit][part];
          bool newPartSel = EditorGUILayout.Toggle(curPartSel, GUILayout.Width(20));
          if (newPartSel != curPartSel)
            partSelections[outfit][part] = newPartSel;
          EditorGUILayout.LabelField(part.name, GUILayout.Width(200));
          EditorGUILayout.LabelField($"[{partParam}]", GUILayout.Width(250));
          EditorGUILayout.EndHorizontal();
        }
      }

      EditorGUILayout.Space(8);
    }

    #endregion

    #region 生成逻辑

    private void DoGenerate()
    {
      if (config.CostumesRoot == null)
      {
        EditorUtility.DisplayDialog("错误", "请先指定 Costumes Root。", "确定");
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
          Name = o.Name,
          RelativePath = o.RelativePath,
          IsDefaultOutfit = o.IsDefaultOutfit,
          IsBaseSelected = baseSelected
        });
      }

      if (selectedOutfits.Count == 0)
      {
        EditorUtility.DisplayDialog("错误", "没有选中任何服装", "确定");
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
