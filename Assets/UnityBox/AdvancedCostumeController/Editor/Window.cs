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
    private Dictionary<string, Color> previewGroupColors = new();
    private int nextPreviewGroupColor;
    private bool previewFoldout = true;
    private Vector2 scrollPosition = Vector2.zero;

    // Keep a broad, high-contrast base palette for the common case. When a
    // preview contains more groups than this list, GetPreviewGroupColor uses
    // golden-ratio HSV hues instead of imposing a hard color-count limit.
    private static readonly Color[] PreviewGroupPalette =
    {
      new Color32(77, 121, 191, 255),
      new Color32(190, 106, 54, 255),
      new Color32(78, 145, 98, 255),
      new Color32(137, 93, 169, 255),
      new Color32(177, 75, 103, 255),
      new Color32(53, 137, 145, 255),
      new Color32(158, 132, 48, 255),
      new Color32(104, 111, 171, 255),
      new Color32(211, 126, 62, 255),
      new Color32(62, 153, 203, 255),
      new Color32(122, 156, 67, 255),
      new Color32(199, 92, 142, 255),
      new Color32(82, 111, 70, 255),
      new Color32(142, 107, 61, 255),
      new Color32(76, 159, 146, 255),
      new Color32(166, 92, 180, 255)
    };

[MenuItem("Tools/UnityBox/Advanced Costume Controller")]
    public static void ShowWindow()
    {
      var window = GetWindow<Window>();
      window.UpdateWindowTitle();
      window.Show();
    }

    private string T(string chinese, string english) => Localization.Text(config, chinese, english);

    private void UpdateWindowTitle()
    {
      titleContent = new GUIContent(T("高级服装控制器", "Advanced Costume Controller"));
    }

    private void OnEnable()
    {
      UpdateWindowTitle();
      config.ApplyAutoDefaultsFromRoot();
      if (config.CostumesRoot != null) RefreshPreview(confirmDiscard: true);
    }

    #region 预览刷新

    private bool RefreshPreview(bool confirmDiscard)
    {
      if (confirmDiscard && !ConfirmDiscardPreviewChanges()) return false;

      currentOutfitDataList.Clear();
      outfitSelections.Clear();
      outfitObjectSelections.Clear();
      partSelections.Clear();
      partGroupNames.Clear();
      previewGroupColors.Clear();
      nextPreviewGroupColor = 0;

      if (config.CostumesRoot == null) return true;

      currentOutfitDataList = Scanner.FindOutfits(config.CostumesRoot);

      foreach (var outfit in currentOutfitDataList)
      {
        outfitSelections[outfit] = true;

        outfitObjectSelections[outfit] = new Dictionary<GameObject, bool>();
        foreach (var obj in outfit.GetAllObjects())
          outfitObjectSelections[outfit][obj] = true;

        partSelections[outfit] = new Dictionary<GameObject, bool>();
        partGroupNames[outfit] = new Dictionary<GameObject, string>();
        foreach (var part in GetOrderedPreviewParts(outfit))
        {
          partSelections[outfit][part] = outfit.Parts.Contains(part);
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
      return true;
    }

    private bool ConfirmDiscardPreviewChanges()
    {
      if (!HasUnsavedPreviewChanges()) return true;

      return EditorUtility.DisplayDialog(
        T("存在未保存的预览修改", "Unsaved Preview Changes"),
        T("当前预览包含未保存的服装、变体或部件勾选，以及临时分组编辑。继续刷新预览或切换服装根节点会丢失这些修改。是否丢弃并继续？",
          "The preview contains unsaved outfit, variant, or part selections and temporary group edits. Refreshing the preview or changing Costumes Root will discard them. Discard and continue?"),
        T("丢弃并继续", "Discard and Continue"),
        T("取消", "Cancel"));
    }

    private bool HasUnsavedPreviewChanges(OutfitData ignoredOutfit = null)
    {
      foreach (var outfit in currentOutfitDataList)
      {
        if (outfit == ignoredOutfit) continue;

        if (!outfitSelections.TryGetValue(outfit, out var outfitSelected) || !outfitSelected)
          return true;

        if (!outfitObjectSelections.TryGetValue(outfit, out var objectSelections))
          return true;
        foreach (var obj in outfit.GetAllObjects())
        {
          if (!objectSelections.TryGetValue(obj, out var selected) || !selected)
            return true;
        }

        if (!partSelections.TryGetValue(outfit, out var selections) ||
            !partGroupNames.TryGetValue(outfit, out var groupNames))
          return true;
        foreach (var part in GetOrderedPreviewParts(outfit))
        {
          string currentGroupName = groupNames.TryGetValue(part, out var groupName)
            ? groupName.Trim()
            : "";
          bool selected = selections.TryGetValue(part, out var partSelected) && partSelected;
          var marker = part.GetComponent<ACCPartGroupMarker>();
          if (!selected)
          {
            if (!string.IsNullOrEmpty(currentGroupName))
              return true;
            if (marker == null || marker.Mode != ACCPartControlMode.Exclude)
              return true;
            continue;
          }

          if (marker != null && marker.Mode == ACCPartControlMode.Exclude)
            return true;

          string persistentGroupName = marker != null && marker.Mode == ACCPartControlMode.Group
            ? GetPersistentGroupName(part, marker)
            : "";
          if (!string.Equals(currentGroupName, persistentGroupName,
            StringComparison.Ordinal))
            return true;
        }
      }
      return false;
    }

    private bool HasUnsavedSelectionChanges(OutfitData outfit)
    {
      if (outfit == null) return false;
      if (!outfitSelections.TryGetValue(outfit, out var outfitSelected) || !outfitSelected)
        return true;

      if (!outfitObjectSelections.TryGetValue(outfit, out var objectSelections))
        return true;
      return outfit.GetAllObjects().Any(obj =>
        !objectSelections.TryGetValue(obj, out var selected) || !selected);
    }

    private bool ConfirmDiscardOtherPreviewChanges(OutfitData savedOutfit)
    {
      if (!HasUnsavedPreviewChanges(savedOutfit) &&
          !HasUnsavedSelectionChanges(savedOutfit))
        return true;

      return EditorUtility.DisplayDialog(
        T("存在其他未保存的预览修改", "Other Unsaved Preview Changes"),
        T("保存当前服装的分组后会刷新整个预览，其他服装或当前服装中未纳入本次保存的临时选择和分组编辑将会丢失。是否继续并丢弃这些修改？",
          "Saving this outfit's groups will refresh the entire preview. Temporary selections or group edits on other outfits or outside this save will be discarded. Continue and discard them?"),
        T("继续并丢弃", "Continue and Discard"),
        T("取消", "Cancel"));
    }

    private static string GetPersistentGroupName(
      GameObject part,
      ACCPartGroupMarker marker)
    {
      if (marker == null || marker.Mode != ACCPartControlMode.Group) return "";
      return string.IsNullOrWhiteSpace(marker.GroupName)
        ? part.name
        : marker.GroupName.Trim();
    }

    #endregion

    #region UI 绘制

    private void OnGUI()
    {
      UpdateWindowTitle();
      EditorGUILayout.LabelField(T("高级服装控制器", "Advanced Costume Controller"), EditorStyles.boldLabel);

      var languageOptions = new[]
      {
        T("自动（跟随系统）", "Auto (System)"),
        T("英文", "English"),
        T("中文", "Chinese")
      };
      config.Language = (ACCLanguage)EditorGUILayout.Popup(
        new GUIContent(T("语言", "Language"), T(
          "切换 ACC 编辑器界面语言。",
          "Change the ACC editor language.")),
        (int)config.Language, languageOptions);
      Localization.CurrentLanguage = config.Language;
      UpdateWindowTitle();

      DrawConfigSection();
      DrawHelpBox();
      DrawPreviewSection();
      DrawGenerateButton();
    }

    private void DrawConfigSection()
    {
      EditorGUILayout.Space(3);
      EditorGUILayout.LabelField(T("配置", "Configuration"), EditorStyles.boldLabel);

      var oldRoot = config.CostumesRoot;
      config.CostumesRoot = (GameObject)EditorGUILayout.ObjectField(
        new GUIContent(T("服装根节点", "Costumes Root"), T(
          "扫描服装、变体和部件的根节点。",
          "Root used to scan outfits, variants, and parts.")),
        config.CostumesRoot, typeof(GameObject), true);
      if (config.CostumesRoot != oldRoot)
      {
        if (!ConfirmDiscardPreviewChanges())
        {
          config.CostumesRoot = oldRoot;
          GUIUtility.ExitGUI();
        }
        config.ApplyAutoDefaultsFromRoot();
        RefreshPreview(confirmDiscard: false);
      }

      config.RootMenuName = EditorGUILayout.TextField(
        new GUIContent(T("根菜单名称", "Root Menu Name"), T(
          "VRChat 菜单中的根节点名称。",
          "Name shown for the root node in the VRChat menu.")), config.RootMenuName);

      var paramPrefix = EditorGUILayout.TextField(
        new GUIContent(T("参数前缀", "Parameter Prefix"), T(
          "用于主参数、Animator 和生成资产命名；同一 Avatar 上请保持唯一。",
          "Used for the main parameter, Animator layers, and generated assets; keep it unique per Avatar.")),
        config.ParamPrefix);
      if (paramPrefix != config.ParamPrefix)
      {
        config.ParamPrefix = paramPrefix;
      }
      if (string.IsNullOrWhiteSpace(config.ParamPrefix))
      {
        EditorGUILayout.HelpBox(
          T("参数前缀为空，将使用服装根节点名称作为默认值。",
            "Parameter Prefix is empty; the costumes root name will be used as fallback."),
          MessageType.Info);
      }

      config.DefaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField(
        new GUIContent(T("默认服装或变体（可选）", "Default Outfit or Variant (optional)"), T(
          "可指定服装本体、对象变体或材质变体作为初始选择。",
          "Assign an outfit base, object variant, or material variant as the initial choice.")),
        config.DefaultOutfitOverride, typeof(GameObject), true);
      config.EnableParts = EditorGUILayout.Toggle(new GUIContent(
        T("启用部件控制", "Enable Parts Control"), T(
          "为部件或分组生成独立开关。",
          "Generate independent toggles for parts and groups.")), config.EnableParts);
      if (!config.EnableParts)
        config.EnableCustomMixer = false;

      using (new EditorGUI.DisabledScope(!config.EnableParts))
        config.EnableCustomMixer = EditorGUILayout.Toggle(new GUIContent(
          T("启用混搭", "Enable Custom Mixer"), T(
            "按部件选择不同服装组的本体或变体；需要先启用部件控制。",
            "Choose base or variant parts across outfit groups; requires Parts Control.")),
          config.EnableCustomMixer);
      if (config.EnableCustomMixer)
      {
        EditorGUI.indentLevel++;
        config.UseIndependentMixerPartParameters = EditorGUILayout.Toggle(
          new GUIContent(T("使用独立部件参数", "Use Independent Part Parameters"), T(
            "启用后为每个混搭槽位生成独立的 0..N 候选参数，可选择本体或变体；关闭时复用普通部件参数，不新增混搭槽位参数。",
            "Generate independent 0..N candidate parameters for Mixer slots so base/variants can be selected; when disabled, reuse normal part parameters without adding Mixer slot parameters.")),
          config.UseIndependentMixerPartParameters);
        string effectiveMixerObjectName = string.IsNullOrWhiteSpace(config.CustomMixerName)
          ? Localization.DefaultMixerMenuObjectName(config)
          : config.CustomMixerName.Trim();
        config.CustomMixerName = EditorGUILayout.TextField(new GUIContent(
          T("混搭菜单名称", "Custom Mixer Name"), T(
            $"当前节点：{effectiveMixerObjectName}；参数前缀固定为“{ACCConfig.MixerParamPrefix}”。",
            $"Current node: {effectiveMixerObjectName}; parameter prefix is \"{ACCConfig.MixerParamPrefix}\".")),
          config.CustomMixerName);
        EditorGUI.indentLevel--;
      }

      config.EnableParameterCompression = EditorGUILayout.Toggle(
        new GUIContent(T("启用参数压缩", "Enable Parameter Compression"), T(
          "多值选择使用本地 Int，并压缩为同步 Bool 位。",
          "Compress multi-value choices from local Ints into synced Bool bits.")),
        config.EnableParameterCompression);

      config.AutoGenerateMenuIcons = EditorGUILayout.Toggle(
        new GUIContent(T("自动生成菜单图标", "Auto Generate Menu Icons"), T(
          "生成服装、变体、部件和 Mixer 项的 256×256 透明 PNG。",
          "Generate 256×256 transparent PNGs for outfits, variants, parts, and Mixer items.")),
        config.AutoGenerateMenuIcons);

      EditorGUILayout.Space(5);
      DrawFolderPicker();
    }

    private void DrawFolderPicker()
    {
      EditorGUILayout.BeginHorizontal();
      config.GeneratedFolder = EditorGUILayout.TextField(new GUIContent(
        T("输出目录", "Output Folder"), T(
          "生成 Controller、动画和图标的 Assets 目录。",
          "Assets folder for generated controllers, animations, and icons.")), config.GeneratedFolder);

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
      EditorGUILayout.LabelField(T("使用方式", "How to use"), EditorStyles.boldLabel);
      EditorGUILayout.HelpBox(
        T("1. 选择服装根节点，确认扫描到目标服装。\n" +
          "2. 设置参数前缀；需要时指定默认服装或变体。\n" +
          "3. 点击“刷新预览”，勾选要生成的服装、本体/变体和部件。\n" +
          "4. 按需启用部件控制、混搭（可选择独立部件参数）、参数压缩或自动图标。\n" +
          "5. 查看预览下方的参数占用和动画层信息。\n" +
          "6. 点击“生成”，在确认窗口核对输出路径和默认选择。",
          "1. Select Costumes Root and confirm the outfits were scanned.\n" +
          "2. Set the parameter prefix; optionally assign a default outfit or variant.\n" +
          "3. Click Refresh Preview, then select outfits, base/variants, and parts.\n" +
          "4. Enable Parts, Custom Mixer (optionally independent Mixer part parameters), parameter compression, or menu icons as needed.\n" +
          "5. Review parameter usage and Animator layer information below the preview.\n" +
          "6. Click Generate and verify the output path and default choice."),
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

      EditorGUILayout.Space(3);
      EditorGUILayout.LabelField(T("预览", "Preview"), EditorStyles.boldLabel);

      if (GUILayout.Button(T("刷新预览", "Refresh Preview")))
        RefreshPreview(confirmDiscard: true);

      if (currentOutfitDataList.Count == 0)
      {
        EditorGUILayout.HelpBox(T(
          "未找到可识别的服装。请检查骨架/网格结构，或添加 ACC Outfit Marker。",
          "No recognizable outfits were found. Check the skeleton/mesh structure or add an ACC Outfit Marker."),
          MessageType.Warning);
        return;
      }

      previewFoldout = EditorGUILayout.Foldout(previewFoldout, T("预览服装和部件", "Outfit and Parts Preview"), true);
      if (!previewFoldout) return;

      scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(400));

      // 计算当前选择下的实时索引映射
      var selectedOutfits = currentOutfitDataList.Where(o => outfitSelections[o]).ToList();
      var previewIndexMap = BuildIndexMap(selectedOutfits.SelectMany(outfit =>
        outfit.GetAllObjects().Where(obj => outfitObjectSelections[outfit]
          .TryGetValue(obj, out var selected) && selected)));

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

      EditorGUILayout.Space(3);
      DrawParameterEstimate();
    }

    private void DrawParameterEstimate()
    {
      var filteredOutfits = BuildSelectedOutfits();
      var usage = Generator.CalculateParameterUsage(config, filteredOutfits);
      DrawParameterUsageSummary(usage, filteredOutfits);
    }

    private void DrawParameterUsageSummary(
      Generator.ParameterUsageSummary usage, List<OutfitData> outfits)
    {
      const string highlightHex = "F5A623";
      var summaryStyle = new GUIStyle(EditorStyles.label)
      {
        richText = true,
        wordWrap = true
      };
      string highlighted(string text) => $"<color=#{highlightHex}>{text}</color>";
      string FormatSyncTypes(int intCount, int boolCount)
      {
        var types = new List<string>();
        if (intCount > 0) types.Add(highlighted($"{intCount} Int"));
        if (boolCount > 0) types.Add(highlighted($"{boolCount} Bool"));
        return string.Join(" + ", types);
      }

      var chineseSections = new List<string>();
      var englishSections = new List<string>();
      AddParameterSection(chineseSections, englishSections, usage.Main,
        "主选择", "Main choice", FormatSyncTypes);
      AddParameterSection(chineseSections, englishSections, usage.Parts,
        "部件", "Parts", FormatSyncTypes);
      AddParameterSection(chineseSections, englishSections, usage.Mixer,
        "混搭槽位", "Mixer slots", FormatSyncTypes);
      AddParameterSection(chineseSections, englishSections, usage.MixerVariants,
        "混搭变体", "Mixer variants", FormatSyncTypes);
      string sectionLine = T(
        chineseSections.Count > 0
          ? string.Join(" + ", chineseSections) + " = " + highlighted($"{usage.TotalBits} Bit")
          : "无同步参数占用",
        englishSections.Count > 0
          ? string.Join(" + ", englishSections) + " = " + highlighted($"{usage.TotalBits} bits")
          : "No synced parameter usage");
      string calculation = T(
        $"计算：{usage.IntCount} Int × {Generator.IntParameterBits} + {usage.BoolCount} Bool × {Generator.BoolParameterBits} = {usage.TotalBits}bit",
        $"Calculation: {usage.IntCount} Int × {Generator.IntParameterBits} + {usage.BoolCount} Bool × {Generator.BoolParameterBits} = {usage.TotalBits} bits");
      int layerCount = Generator.CountGeneratedAnimatorLayers(config, outfits);
      string layerLine = T(
        $"动画层({highlighted(layerCount.ToString())})。",
        $"Animator layers({highlighted(layerCount.ToString())}). ");

      EditorGUILayout.LabelField(new GUIContent(layerLine + sectionLine, calculation), summaryStyle);
      if (config.EnableParameterCompression || usage.Local.HasAny)
      {
        string compressionLine = BuildCompressionSummaryLine(usage, highlighted);
        string localLine = BuildLocalParameterLine(usage, highlighted);
        var secondLine = new List<string>();
        if (!string.IsNullOrEmpty(compressionLine)) secondLine.Add(compressionLine);
        if (!string.IsNullOrEmpty(localLine)) secondLine.Add(localLine);
        if (secondLine.Count > 0)
          EditorGUILayout.LabelField(string.Join(T("。", ". "), secondLine), summaryStyle);
      }
    }

    private string BuildCompressionSummaryLine(
      Generator.ParameterUsageSummary usage, System.Func<string, string> highlighted)
    {
      if (!config.EnableParameterCompression) return "";

      var chineseSections = new List<string>();
      var englishSections = new List<string>();
      AddCompressionSection(chineseSections, englishSections, usage.Main,
        "主选择", "Main choice", highlighted);
      AddCompressionSection(chineseSections, englishSections, usage.Parts,
        "部件", "Parts", highlighted);
      AddCompressionSection(chineseSections, englishSections, usage.Mixer,
        "混搭槽位", "Mixer slots", highlighted);
      AddCompressionSection(chineseSections, englishSections, usage.MixerVariants,
        "混搭变体", "Mixer variants", highlighted);
      if (chineseSections.Count == 0)
        return T("参数压缩：无可压缩参数", "Compression: no compressible parameters");

      return T(
        "参数压缩：" + string.Join(" + ", chineseSections),
        "Compression: " + string.Join(" + ", englishSections));
    }

    private static void AddCompressionSection(
      List<string> chineseSections,
      List<string> englishSections,
      Generator.ParameterUsageSection section,
      string chineseLabel,
      string englishLabel,
      System.Func<string, string> highlighted)
    {
      if (!section.HasAny) return;

      var chineseTypes = new List<string>();
      var englishTypes = new List<string>();
      if (section.HasCompression)
      {
        chineseTypes.Add(highlighted($"{section.CompressedIntCount} Int → {section.CompressedBoolCount} Bool"));
        englishTypes.Add(highlighted($"{section.CompressedIntCount} Int → {section.CompressedBoolCount} Bool"));
      }

      int ordinaryBoolCount = section.BoolCount - section.CompressedBoolCount;
      if (ordinaryBoolCount > 0)
      {
        chineseTypes.Add(highlighted($"{ordinaryBoolCount} Bool"));
        englishTypes.Add(highlighted($"{ordinaryBoolCount} Bool"));
      }

      if (section.IntCount > 0)
      {
        chineseTypes.Add(highlighted($"{section.IntCount} Int"));
        englishTypes.Add(highlighted($"{section.IntCount} Int"));
      }

      if (chineseTypes.Count == 0) return;
      chineseSections.Add($"{chineseLabel}({string.Join(" + ", chineseTypes)})");
      englishSections.Add($"{englishLabel}({string.Join(" + ", englishTypes)})");
    }

    private string BuildLocalParameterLine(
      Generator.ParameterUsageSummary usage, System.Func<string, string> highlighted)
    {
      if (!usage.Local.HasAny) return "";
      var types = new List<string>();
      if (usage.Local.IntCount > 0)
        types.Add(highlighted($"{usage.Local.IntCount} Int"));
      if (usage.Local.BoolCount > 0)
        types.Add(highlighted($"{usage.Local.BoolCount} Bool"));
      if (usage.Local.FloatCount > 0)
        types.Add(highlighted($"{usage.Local.FloatCount} Float"));
      string typeText = string.Join(" + ", types);
      return T(
        $"本地参数({typeText})",
        $"Local parameters({typeText})");
    }

    private static void AddParameterSection(
      List<string> chineseSections,
      List<string> englishSections,
      Generator.ParameterUsageSection section,
      string chineseLabel,
      string englishLabel,
      System.Func<int, int, string> formatTypes)
    {
      if (!section.HasAny) return;
      string types = formatTypes(section.IntCount, section.BoolCount);
      chineseSections.Add($"{chineseLabel}({types})");
      englishSections.Add($"{englishLabel}({types})");
    }

    private void DrawOutfitPreview(OutfitData outfit, Dictionary<GameObject, int> previewIndexMap)
    {
      string displayName = outfit.HasVariants()
        ? Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject)
        : outfit.RelativePath;
      if (string.IsNullOrEmpty(displayName)) displayName = outfit.Name;

      // 标题行
      EditorGUILayout.BeginHorizontal();
      bool newSel = EditorGUILayout.Toggle(outfitSelections[outfit], GUILayout.Width(20));
      if (newSel != outfitSelections[outfit])
      {
        outfitSelections[outfit] = newSel;
        Repaint();
      }
      DrawObjectLink(outfit.OutfitObject ?? outfit.BaseObject, displayName, 280);
      EditorGUILayout.LabelField(
        outfit.HasVariants()
          ? T($"（{outfit.GetAllObjects().Count} 个变体）", $"({outfit.GetAllObjects().Count} variants)")
          : "",
        GUILayout.Width(100));
      EditorGUILayout.EndHorizontal();

      if (!outfitSelections[outfit]) return;

      // 服装对象（本体 + 变体）
      foreach (var obj in GetOrderedPreviewObjects(outfit.GetAllObjects()))
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
        DrawObjectLink(obj, obj.name, 200);
        EditorGUILayout.LabelField(
          objIndex >= 0 ? $"[{config.MainParameterName} = {objIndex}]" : T("（未选中）", "(not selected)"),
          GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
      }

      // 始终展示扫描到的部件决策，方便确认自动、Marker 分组和排除结果。
      if (outfit.Parts.Count > 0 || outfit.ExcludedParts.Count > 0)
      {
        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        EditorGUILayout.LabelField(T(
          $"部件预览（可控制：{outfit.Parts.Count}，已排除：{outfit.ExcludedParts.Count}）",
          $"Part Preview (controlled: {outfit.Parts.Count}, excluded: {outfit.ExcludedParts.Count})"),
          EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        var excludedParts = new HashSet<GameObject>(outfit.ExcludedParts ??
          new List<GameObject>());
        foreach (var part in GetOrderedPreviewParts(outfit))
        {
          if (excludedParts.Contains(part))
            DrawExcludedPartPreviewRow(outfit, part);
          else if (config.EnableParts)
            DrawControlledPartPreviewRow(outfit, part);
          else
            DrawReadOnlyPartPreviewRow(outfit, part);
        }

        EditorGUILayout.LabelField(Localization.PartSourceLegend(), EditorStyles.miniLabel);
        if (config.EnableParts)
          DrawPersistPartGroupsButton(outfit);
      }

      EditorGUILayout.Space(8);
    }

    private void DrawPersistPartGroupsButton(OutfitData outfit)
    {
      var changes = new List<(GameObject Part, string GroupName, string Action,
        bool Apply, bool Remove, bool Exclude)>();
      foreach (var part in GetOrderedPreviewParts(outfit))
      {
        bool selected = partSelections[outfit].TryGetValue(part, out var partSelected) &&
          partSelected;
        string groupName = partGroupNames[outfit].TryGetValue(part, out var value)
          ? value.Trim()
          : "";
        var marker = part.GetComponent<ACCPartGroupMarker>();

        if (!selected)
        {
          if (marker == null || marker.Mode != ACCPartControlMode.Exclude)
            changes.Add((part, "-", T("设置为不控制", "Set as Exclude"), true, false, true));
          continue;
        }

        if (marker != null && marker.Mode == ACCPartControlMode.Exclude)
        {
          string restoredGroupName = string.IsNullOrEmpty(groupName)
            ? part.name
            : groupName;
          changes.Add((part, restoredGroupName,
            T("恢复为分组", "Restore as Group"), true, false, false));
          continue;
        }

        if (string.IsNullOrEmpty(groupName))
        {
          if (marker != null && marker.Mode == ACCPartControlMode.Group)
            changes.Add((part, "-", T("移除标记", "Remove marker"), true, true, false));
          continue;
        }
        if (marker != null && marker.Mode == ACCPartControlMode.Group &&
            string.Equals(GetPersistentGroupName(part, marker), groupName,
              StringComparison.Ordinal))
          continue;
        changes.Add((part, groupName,
          marker == null ? T("新增标记", "Add marker") : T("更新标记", "Update marker"),
          true, false, false));
      }

      using (new EditorGUI.DisabledScope(!changes.Any(change => change.Apply)))
      {
        if (!GUILayout.Button(T("保存预览分组到服装", "Save Preview Groups to Outfit")))
          return;
      }

      var preview = new System.Text.StringBuilder();
      preview.AppendLine(T(
        $"将持久化“{outfit.Name}”中的预览分组：",
        $"The following preview groups in “{outfit.Name}” will be persisted:"));
      foreach (var change in changes)
        preview.AppendLine(T(
          $"- {change.Part.name} → {change.GroupName}（{change.Action}）",
          $"- {change.Part.name} → {change.GroupName} ({change.Action})"));
      preview.AppendLine();
      preview.AppendLine(T(
        "将添加、更新、移除或设置为 Exclude 的 ACC Part Group Marker。保存后预览会刷新，未列出的临时勾选和分组编辑会丢失。",
        "ACC Part Group Marker components will be added, updated, removed, or set to Exclude. The preview will refresh after saving; unlisted temporary selections and group edits will be discarded."));

      if (!EditorUtility.DisplayDialog(T("保存持久分组", "Save Persistent Groups"),
        preview.ToString(), T("保存", "Save"), T("取消", "Cancel")))
        return;

      if (!ConfirmDiscardOtherPreviewChanges(outfit))
        return;

      int undoGroup = ACCEditorUndo.Begin("Persist ACC part groups");
      try
      {
        foreach (var change in changes)
        {
          if (!change.Apply) continue;
          var marker = change.Part.GetComponent<ACCPartGroupMarker>();
          if (change.Remove)
          {
            if (marker != null)
              Undo.DestroyObjectImmediate(marker);
            continue;
          }
          if (marker == null)
            marker = ACCEditorUndo.AddComponent<ACCPartGroupMarker>(change.Part,
              "Create ACC part group marker");
          Undo.RecordObject(marker, "Configure ACC part group marker");
          marker.Mode = change.Exclude
            ? ACCPartControlMode.Exclude
            : ACCPartControlMode.Group;
          marker.GroupName = change.Exclude ? string.Empty : change.GroupName;
          EditorUtility.SetDirty(marker);
          ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { marker });
        }
      }
      finally
      {
        ACCEditorUndo.Complete(undoGroup);
      }
      RefreshPreview(confirmDiscard: false);
      Repaint();
      GUIUtility.ExitGUI();
    }

    private void DrawControlledPartPreviewRow(OutfitData outfit, GameObject part)
    {
      var partParam = GetPreviewPartParamName(outfit, part);
      string groupName = partGroupNames[outfit].TryGetValue(part, out var value) ? value.Trim() : "";
      var control = FindPreviewPartControl(outfit, part);
      bool hasPersistentGroup = control != null && control.IsGroup;
      bool isUnchangedPersistentGroup = hasPersistentGroup && control.Name == groupName;
      string source = !string.IsNullOrEmpty(groupName)
        ? (isUnchangedPersistentGroup
          ? $"[MG: {groupName}]"
          : $"[SG: {groupName}]")
        : hasPersistentGroup
          ? T("[SG] 待移除", "[SG] Pending removal")
          : T("[A] 自动", "[A] Auto");
      var groupColor = GetPreviewGroupColor(part, groupName,
        control, hasPersistentGroup);

      EditorGUILayout.BeginHorizontal();
      GUILayout.Space(20);
      bool curPartSel = partSelections[outfit][part];
      bool newPartSel = EditorGUILayout.Toggle(curPartSel, GUILayout.Width(20));
      if (newPartSel != curPartSel)
      {
        partSelections[outfit][part] = newPartSel;
        Repaint();
      }
      DrawObjectLink(part, FormatPartDisplayName(outfit, part.name), 150);
      EditorGUILayout.LabelField(ColorizeGroupText(source, groupColor),
        GetRichLabelStyle(), GUILayout.Width(100));
      EditorGUILayout.LabelField(T("分组", "Group"), GUILayout.Width(32));
      partGroupNames[outfit][part] = EditorGUILayout.TextField(groupName, GUILayout.Width(120));
      GUILayout.Space(20);
      EditorGUILayout.LabelField(partParam, EditorStyles.label, GUILayout.ExpandWidth(true));
      EditorGUILayout.EndHorizontal();
    }

    private void DrawReadOnlyPartPreviewRow(OutfitData outfit, GameObject part)
    {
      var control = FindPreviewPartControl(outfit, part);
      bool isPersistentGroup = control != null && control.IsGroup;
      string source = isPersistentGroup
        ? $"[MG: {control.Name}]"
        : T("[A] 自动", "[A] Auto");
      var groupColor = GetPreviewGroupColor(part,
        isPersistentGroup ? control.Name : "", control, isPersistentGroup);

      EditorGUILayout.BeginHorizontal();
      GUILayout.Space(20);
      DrawDisabledPartToggle();
      DrawObjectLink(part, FormatPartDisplayName(outfit, part.name), 150);
      EditorGUILayout.LabelField(ColorizeGroupText(source, groupColor),
        GetRichLabelStyle(), GUILayout.Width(100));
      EditorGUILayout.LabelField("", GUILayout.ExpandWidth(true));
      EditorGUILayout.EndHorizontal();
    }

    private static void DrawDisabledPartToggle()
    {
      using (new EditorGUI.DisabledScope(true))
        EditorGUILayout.Toggle(false, GUILayout.Width(20));
    }

    private static void DrawObjectLink(GameObject target, string label, float width)
    {
      var content = new GUIContent(label,
        "点击选择并定位对象 / Click to select and locate this object");
      if (GUILayout.Button(content, EditorStyles.linkLabel, GUILayout.Width(width)))
        Utils.SelectAndPingObject(target);
    }

    private IEnumerable<GameObject> GetOrderedPreviewObjects(
      IEnumerable<GameObject> objects)
    {
      return (objects ?? Enumerable.Empty<GameObject>())
        .Where(obj => obj != null)
        .Distinct()
        .OrderBy(obj => Utils.GetHierarchyPath(config.CostumesRoot, obj),
          StringComparer.Ordinal);
    }

    private IEnumerable<GameObject> GetOrderedPreviewParts(OutfitData outfit)
    {
      return GetOrderedPreviewObjects((outfit.Parts ?? new List<GameObject>())
        .Concat(outfit.ExcludedParts ?? new List<GameObject>()));
    }

    private Color GetPreviewGroupColor(
      GameObject part,
      string groupName,
      PartControlData control,
      bool hasPersistentGroup)
    {
      string key;
      if (!string.IsNullOrEmpty(groupName))
        key = "Group|" + groupName;
      else if (hasPersistentGroup && control != null)
        key = "Group|" + control.Name;
      else
        key = "Auto|" + Utils.GetHierarchyPath(config.CostumesRoot, part);

      if (previewGroupColors.TryGetValue(key, out var color)) return color;

      int colorIndex = nextPreviewGroupColor++;
      color = colorIndex < PreviewGroupPalette.Length
        ? PreviewGroupPalette[colorIndex]
        : Color.HSVToRGB((colorIndex * 0.61803398875f) % 1f, 0.55f, 0.85f);
      previewGroupColors[key] = color;
      return color;
    }

    private static GUIStyle GetRichLabelStyle()
    {
      var style = new GUIStyle(EditorStyles.label) { richText = true };
      return style;
    }

    private static string ColorizeGroupText(string text, Color color)
    {
      return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }

    private static PartControlData FindPreviewPartControl(OutfitData outfit, GameObject part)
    {
      return outfit.GetPartControls().FirstOrDefault(control => control.Parts.Contains(part));
    }

    private void DrawExcludedPartPreviewRow(OutfitData outfit, GameObject part)
    {
      bool selected = partSelections[outfit].TryGetValue(part, out var partSelected) &&
        partSelected;
      string groupName = partGroupNames[outfit].TryGetValue(part, out var value)
        ? value.Trim()
        : "";

      EditorGUILayout.BeginHorizontal();
      GUILayout.Space(20);
      if (config.EnableParts)
      {
        bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(20));
        if (newSelected != selected)
        {
          selected = newSelected;
          partSelections[outfit][part] = newSelected;
          if (newSelected && string.IsNullOrEmpty(groupName))
          {
            groupName = part.name;
            partGroupNames[outfit][part] = groupName;
          }
          else if (!newSelected)
          {
            partGroupNames[outfit][part] = "";
          }
          Repaint();
        }
      }
      else
      {
        DrawDisabledPartToggle();
      }
      DrawObjectLink(part, FormatPartDisplayName(outfit, part.name), 150);
      if (config.EnableParts && selected)
      {
        var groupColor = GetPreviewGroupColor(part, groupName, null, false);
        EditorGUILayout.LabelField(ColorizeGroupText(
          $"[SG: {groupName}]", groupColor), GetRichLabelStyle(),
          GUILayout.Width(100));
        EditorGUILayout.LabelField(T("分组", "Group"), GUILayout.Width(32));
        partGroupNames[outfit][part] = EditorGUILayout.TextField(
          groupName, GUILayout.Width(120));
      }
      else
      {
        EditorGUILayout.LabelField(T("[X] 已排除", "[X] Excluded"), EditorStyles.label,
          GUILayout.Width(100));
        EditorGUILayout.LabelField(T("不会生成控制", "No control"), EditorStyles.label,
          GUILayout.ExpandWidth(true));
      }
      EditorGUILayout.EndHorizontal();
    }

    private string GetPreviewPartParamName(OutfitData outfit, GameObject part)
    {
      string groupName = partGroupNames[outfit].TryGetValue(part, out var value)
        ? value.Trim() : "";
      string controlPath = string.IsNullOrEmpty(groupName)
        ? "Parts/" + Utils.GetRelativePath(outfit.BaseObject, part)
        : "Groups/" + groupName;
      return Utils.BuildParamName(config.MainParameterName, outfit.RelativePath + "/" + controlPath);
    }

    private string FormatPartDisplayName(OutfitData outfit, string partName)
    {
      var marker = outfit.Marker;
      if (marker == null) return partName;

      return Utils.FormatPartDisplayName(partName,
        marker.PartNamePrefixToRemove,
        marker.PartNameSuffixToRemove,
        marker.PartNameRegexPattern,
        marker.PartNameRegexReplacement);
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
          T("输出目录必须是 Assets 下不含 . 或 .. 的安全相对路径。",
            "Output Folder must be a safe Assets-relative path without . or .. segments."),
          "OK");
        return;
      }

      var selectedOutfits = BuildSelectedOutfits();

      // 本体和所有变体都未选中时，跳过此服装。
      if (selectedOutfits.Count == 0)
      {
        EditorUtility.DisplayDialog(T("错误", "Error"), T("没有选中任何服装。", "No outfit is selected."), "OK");
        return;
      }

      // 生成索引映射（仅包含用户勾选的对象）
      var outfitIndexMap = BuildIndexMap(selectedOutfits.SelectMany(outfit =>
        outfit.GetAllObjects()));

      // 查找默认服装
      var defaultOutfit = Scanner.FindDefaultOutfit(selectedOutfits, config.DefaultOutfitOverride);

      // 执行生成
      var generator = new Generator(config);
      generator.Execute(selectedOutfits, outfitIndexMap, defaultOutfit);
    }

    private List<OutfitData> BuildSelectedOutfits()
    {
      var selectedOutfits = new List<OutfitData>();
      foreach (var o in currentOutfitDataList)
      {
        if (!outfitSelections.TryGetValue(o, out var outfitSelected) || !outfitSelected)
          continue;

        var objectSelections = outfitObjectSelections[o];
        bool baseSelected = objectSelections.TryGetValue(o.BaseObject, out var baseValue) && baseValue;
        var selectedVariants = o.Variants.Where(variant =>
          objectSelections.TryGetValue(variant, out var selected) && selected).ToList();

        // 本体和所有变体都未选中时，跳过此服装
        if (!baseSelected && selectedVariants.Count == 0) continue;

        selectedOutfits.Add(new OutfitData
        {
          BaseObject = o.BaseObject,
          ArmatureObject = o.ArmatureObject,
          OutfitObject = o.OutfitObject,
          Variants = selectedVariants,
          Parts = o.Parts.Where(p =>
            partSelections[o].TryGetValue(p, out var sel) && sel).ToList(),
          ExcludedParts = o.ExcludedParts,
          PartControls = BuildPartControls(o),
          VariantPartData = o.VariantPartData.Where(item =>
            (item.VariantObject == o.BaseObject && baseSelected) ||
            (item.VariantObject != o.BaseObject && objectSelections.TryGetValue(item.VariantObject, out var selected) && selected)).ToList(),
          Marker = o.Marker,
          Name = o.Name,
          RelativePath = o.RelativePath,
          IsDefaultOutfit = o.IsDefaultOutfit,
          IsBaseSelected = baseSelected
        });
      }
      return selectedOutfits;
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
            Name = string.IsNullOrEmpty(groupName) ? FormatPartDisplayName(outfit, part.name) : groupName,
            IsGroup = !string.IsNullOrEmpty(groupName)
          };
          groups[key] = control;
          result.Add(control);
        }
        control.Parts.Add(part);
      }
      return result;
    }

    /// <summary>按层级顺序构建生成菜单与动画使用的对象索引映射。</summary>
    private Dictionary<GameObject, int> BuildIndexMap(IEnumerable<GameObject> objects)
    {
      return objects
        .Distinct()
        .OrderBy(go => Utils.GetHierarchyPath(config.CostumesRoot, go))
        .Select((go, index) => new { go, index })
        .ToDictionary(x => x.go, x => x.index);
    }

    #endregion
  }
}
