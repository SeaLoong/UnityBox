using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 生成器 — 负责菜单构建和整体生成流程的协调
  /// </summary>
  public class Generator
  {
    private readonly ACCConfig config;
    private readonly AnimationBuilder animBuilder;
    private readonly List<MenuIconRequest> menuIconRequests = new List<MenuIconRequest>();

    public Generator(ACCConfig config)
    {
      this.config = config;
      this.animBuilder = new AnimationBuilder(config);
    }

    private string T(string chinese, string english) => Localization.Text(config, chinese, english);

    /// <summary>
    /// 执行完整的生成流程
    /// </summary>
    public void Execute(
      List<OutfitData> selectedOutfits,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData defaultOutfit)
    {
      if (config.EnableCustomMixer && !config.EnableParts)
      {
        EditorUtility.DisplayDialog(T("生成失败", "Generation Failed"),
          T("Custom Mixer 需要启用部件控制。", "Custom Mixer requires Parts Control."), "OK");
        return;
      }

      int maximumChoiceValue = config.EnableCustomMixer
        ? outfitIndexMap.Count
        : outfitIndexMap.Count - 1;
      if (maximumChoiceValue > byte.MaxValue)
      {
        EditorUtility.DisplayDialog(T("生成失败", "Generation Failed"),
          T("服装选择值超过 255，超出 VRChat Int 参数可表达的范围。",
            "A costume choice value exceeds 255, which is outside the VRChat Int parameter range."), "OK");
        return;
      }

      if (!Utils.HasUsableGenerationNamespace(config.MainParameterName))
      {
        EditorUtility.DisplayDialog(T("生成失败", "Generation Failed"),
          T("参数前缀必须至少包含一个字母或数字。", "Parameter Prefix must contain at least one letter or digit."), "OK");
        return;
      }

      if (!Utils.IsSafeAssetsFolder(config.GeneratedFolder))
      {
        EditorUtility.DisplayDialog(T("生成失败", "Generation Failed"),
          T("参数前缀必须可用于生成文件，且输出目录必须是 Assets 下不含 . 或 .. 的相对路径。",
            "Parameter Prefix must be usable in generated filenames, and Output Folder must be an Assets-relative path without . or .. segments."),
          "OK");
        return;
      }

      var resolvedFolder = config.GetResolvedGeneratedFolder();
      string controllerPath = Utils.CombineAssetPath(resolvedFolder, config.GetControllerFileName());
      if (!ShowPreflightDialog(selectedOutfits, defaultOutfit,
        File.Exists(controllerPath), controllerPath)) return;

      try
      {
        menuIconRequests.Clear();
        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("初始化…", "Initializing…"), 0.1f);

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Advanced Costume Controller");

        var costumesRoot = config.CostumesRoot;
        Utils.MigrateLegacyGeneratedNodes(costumesRoot);
        int generatedMergeArmatures = Utils.CountMissingMergeArmatures(selectedOutfits) > 0
          ? Utils.EnsureMergeArmaturesForOutfits(selectedOutfits)
          : 0;

        // ACC_Menu 是唯一的 ACC 生成节点。复用其图标等展示属性，但重建控制字段与子项。
        var menuRoot = Utils.PrepareChildRoot(costumesRoot, ACCConfig.MenuObjectName);
        var previousControllerParameters = Utils.GetAnimatorParameterNames(
          menuRoot.GetComponent<ModularAvatarMergeAnimator>());
        var menuPresentation = Utils.CaptureMenuPresentation(menuRoot, config,
          config.EnableCustomMixer ? GetCustomMixerValue(outfitIndexMap.Count) : (int?)null);
        Utils.ClearMenuChildren(menuRoot);
        Utils.EnsureMenuInstaller(menuRoot);
        var mergeAnimator = menuRoot.GetComponent<ModularAvatarMergeAnimator>();
        if (mergeAnimator == null)
        {
          try { mergeAnimator = Undo.AddComponent<ModularAvatarMergeAnimator>(menuRoot); }
          catch { mergeAnimator = menuRoot.AddComponent<ModularAvatarMergeAnimator>(); }
        }
        var rootParams = Utils.EnsureParametersComponent(menuRoot);
        var generatedParameterNames = GetGeneratedParameterNames(selectedOutfits, outfitIndexMap);
        Utils.RemoveParameterDeclarations(rootParams, previousControllerParameters);
        Utils.RemoveParameterDeclarations(rootParams, generatedParameterNames);
        Utils.EnsureSubmenuOnNode(menuRoot, config.EffectiveRootMenuName);
        Utils.EnsureDefaultMenuIcon(menuRoot, "OutlineClothing2");

        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("构建菜单…", "Building menus…"), 0.3f);
        BuildMenus(menuRoot, selectedOutfits, outfitIndexMap, rootParams, defaultOutfit,
          menuPresentation);

        // CustomMixer 菜单
        if (config.EnableCustomMixer)
        {
          EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("创建混搭菜单…", "Creating mixer menu…"), 0.5f);
          int customMixerValue = GetCustomMixerValue(outfitIndexMap.Count);
          Mixer.BuildCustomMixerMenu(
            config, menuRoot, selectedOutfits, outfitIndexMap,
            customMixerValue, rootParams, menuPresentation, defaultOutfit, menuIconRequests);
        }

        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("创建动画控制器…", "Creating animator controller…"), 0.7f);
        // When icon generation is disabled, keep the existing MenuIcons assets.
        // The menu presentation snapshot restores their references after the
        // generated menu tree is rebuilt; deleting the folder here would turn
        // those restored references into Missing assets.
        PrepareGeneratedFolder(resolvedFolder, preserveMenuIcons: !config.AutoGenerateMenuIcons);

        var controller = animBuilder.CreateController(selectedOutfits, outfitIndexMap, defaultOutfit, controllerPath);

        if (config.AutoGenerateMenuIcons)
        {
          EditorUtility.DisplayProgressBar(T("生成中", "Generating"),
            T("生成菜单图标…", "Generating menu icons…"), 0.9f);
          int iconCount = MenuIconGenerator.Generate(costumesRoot, menuRoot,
            resolvedFolder, menuIconRequests);
          Debug.Log($"[ACC] Generated {iconCount} menu icons.");
        }

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

        Debug.Log($"[ACC] 生成完成: {selectedOutfits.Count} 个服装, " +
                  $"{selectedOutfits.Sum(o => o.Parts.Count)} 个部件" +
                  (config.EnableCustomMixer ? ", 已启用混搭模式" : "") +
                  (generatedMergeArmatures > 0
                    ? $", 已配置 {generatedMergeArmatures} 个 MA Merge Armature"
                    : ""));
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    private static void PrepareGeneratedFolder(string resolvedFolder, bool preserveMenuIcons)
    {
      // 输出目录按 ParamPrefix 隔离，因而可以在重新生成时安全清理旧 Controller 与 Clip，
      // 避免 AssetDatabase.CreateAsset 因同名旧动画文件而失败。自动图标关闭时保留
      // MenuIcons，否则菜单展示快照恢复的 Texture2D 引用会因资产被删除而变成 Missing。
      if (AssetDatabase.IsValidFolder(resolvedFolder))
      {
        if (!preserveMenuIcons)
        {
          AssetDatabase.DeleteAsset(resolvedFolder);
        }
        else
        {
          string menuIconFolder = Utils.CombineAssetPath(resolvedFolder, "MenuIcons");
          foreach (var subfolder in AssetDatabase.GetSubFolders(resolvedFolder))
          {
            if (string.Equals(subfolder, menuIconFolder, StringComparison.OrdinalIgnoreCase))
              continue;
            AssetDatabase.DeleteAsset(subfolder);
          }

          string projectRoot = Directory.GetParent(Application.dataPath).FullName;
          string fullFolder = Path.Combine(projectRoot,
            resolvedFolder.Replace('/', Path.DirectorySeparatorChar));
          if (Directory.Exists(fullFolder))
          {
            foreach (var file in Directory.GetFiles(fullFolder, "*",
              SearchOption.TopDirectoryOnly))
            {
              if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
              AssetDatabase.DeleteAsset(ToAssetPath(file));
            }
          }
        }
      }

      Directory.CreateDirectory(resolvedFolder);
      AssetDatabase.Refresh();
    }

    private static string ToAssetPath(string fullPath)
    {
      string normalizedPath = Path.GetFullPath(fullPath).Replace('\\', '/');
      string projectRoot = Directory.GetParent(Application.dataPath).FullName
        .Replace('\\', '/');
      if (normalizedPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
        return normalizedPath.Substring(projectRoot.Length + 1);
      return normalizedPath;
    }

    #region 菜单构建

    private void BuildMenus(
      GameObject menuContentRoot,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      ModularAvatarParameters rootParams,
      OutfitData defaultOutfit,
      Utils.MenuPresentationSnapshot menuPresentation)
    {
      int defaultIndex = ResolveDefaultChoiceIndex(defaultOutfit, outfitIndexMap);
      var mainLayout = GetMainChoiceLayout(outfitIndexMap, config.EnableCustomMixer,
        config.EnableParameterCompression);
      AddChoiceParameters(rootParams, config.MainParameterName, mainLayout, defaultIndex);

      var costumesRoot = config.CostumesRoot;

      foreach (var outfit in outfits)
      {
        string outfitPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        // 创建父级菜单路径
        var pathParts = outfitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string parentPath = string.Join("/", pathParts.Take(pathParts.Length - 1));
        GameObject parentMenu = Utils.EnsureSubmenuPath(menuContentRoot, parentPath,
          menuPresentation);

        string outfitName = pathParts.Length > 0 ? pathParts[pathParts.Length - 1] : outfit.Name;
        bool needSubmenu = config.EnableParts || outfit.HasVariants();

        if (needSubmenu)
        {
          var outfitSubmenu = Utils.FindOrCreateChild(parentMenu, outfitName);
          Utils.EnsureSubmenuOnNode(outfitSubmenu, presentation: menuPresentation);

          // 本体和变体开关
          foreach (var obj in outfit.GetAllObjects())
          {
            if (!outfitIndexMap.ContainsKey(obj)) continue;

            var itemNode = Utils.FindOrCreateChild(outfitSubmenu,
              obj.name);
            var menuItem = Utils.CreateMenuItem(itemNode);
            // Button controls are momentary in VRChat; outfit choices must persist after release.
            Utils.ConfigureAsToggle(menuItem);
            ConfigureChoiceMenuItem(menuItem, config.MainParameterName, mainLayout, outfitIndexMap[obj]);
            menuItem.isSaved = true;
            menuItem.isSynced = mainLayout.RequiresSynchronization &&
              !mainLayout.UsesCompression;
            Utils.ApplyMenuPresentation(menuPresentation, itemNode, menuItem, "");
            AddMenuIconRequest(itemNode,
              GetMenuIconTargets(obj), "Choice_" + Utils.GetHierarchyPath(costumesRoot, obj),
              obj.GetComponent<ACCVariantMaterialOverride>(), true);
          }

          // 先写入本体和变体；若它们恰好也叫“Parts/部件”，部件菜单会安全追加后缀。
          if (config.EnableParts && outfit.Parts.Count > 0)
            BuildPartsMenu(menuContentRoot, outfitSubmenu, outfit, rootParams, menuPresentation);
        }
        else
        {
          if (!outfitIndexMap.ContainsKey(outfit.BaseObject)) continue;

          var itemNode = Utils.FindOrCreateChild(parentMenu, outfitName);
          var menuItem = Utils.CreateMenuItem(itemNode);
          // Button controls are momentary in VRChat; outfit choices must persist after release.
          Utils.ConfigureAsToggle(menuItem);
          ConfigureChoiceMenuItem(menuItem, config.MainParameterName, mainLayout,
            outfitIndexMap[outfit.BaseObject]);
          menuItem.isSaved = true;
          menuItem.isSynced = mainLayout.RequiresSynchronization &&
            !mainLayout.UsesCompression;
          Utils.ApplyMenuPresentation(menuPresentation, itemNode, menuItem, "");
          AddMenuIconRequest(itemNode, new[] { outfit.BaseObject },
            "Choice_" + Utils.GetHierarchyPath(costumesRoot, outfit.BaseObject),
            useSharedOutfitFraming: true);
        }
      }
    }

    public static ChoiceParameterLayout GetMainChoiceLayout(
      Dictionary<GameObject, int> outfitIndexMap, bool includeCustomMixer,
      bool usesCompression)
    {
      return new ChoiceParameterLayout(outfitIndexMap.Count + (includeCustomMixer ? 1 : 0),
        usesCompression);
    }

    /// <summary>混搭紧随最后一个服装对象，使用连续的选择值。</summary>
    public static int GetCustomMixerValue(int outfitCount)
    {
      return outfitCount;
    }

    public static void AddChoiceParameters(
      ModularAvatarParameters rootParams,
      string baseParameterName,
      ChoiceParameterLayout layout,
      int defaultChoiceIndex)
    {
      if (!layout.RequiresSynchronization)
      {
        // 单一固定选择仍保留本地参数供菜单/Animator 使用，但不占表达式同步预算。
        Utils.AddOrUpdateParameter(rootParams, baseParameterName,
          ParameterSyncType.Int, defaultChoiceIndex, true, true);
        return;
      }
      if (layout.UsesBoolean)
      {
        Utils.AddOrUpdateParameter(rootParams, baseParameterName,
          ParameterSyncType.Bool, defaultChoiceIndex == 1 ? 1 : 0, true);
        return;
      }
      if (!layout.UsesCompression)
      {
        Utils.AddOrUpdateParameter(rootParams, baseParameterName,
          ParameterSyncType.Int, defaultChoiceIndex, true);
        return;
      }

      Utils.AddOrUpdateParameter(rootParams, baseParameterName,
        ParameterSyncType.Int, defaultChoiceIndex, true, layout.UsesCompression);
      for (int i = 0; i < layout.BitCount; i++)
      {
        bool defaultBit = (defaultChoiceIndex & (1 << i)) != 0;
        Utils.AddOrUpdateParameter(rootParams, layout.GetBitParameterName(baseParameterName, i),
          ParameterSyncType.Bool, defaultBit ? 1 : 0, true);
      }
    }

    public static void ConfigureChoiceMenuItem(
      ModularAvatarMenuItem menuItem,
      string baseParameterName,
      ChoiceParameterLayout layout,
      int choiceIndex)
    {
      menuItem.PortableControl.Parameter = baseParameterName;
      menuItem.PortableControl.Value = choiceIndex;
      menuItem.automaticValue = false;
    }

    private HashSet<string> GetGeneratedParameterNames(
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      var names = new HashSet<string>();
      var mainLayout = GetMainChoiceLayout(outfitIndexMap, config.EnableCustomMixer,
        config.EnableParameterCompression);
      AddChoiceParameterNames(names, config.MainParameterName, mainLayout);

      foreach (var outfit in outfits)
      {
        if (config.EnableParts)
        {
          foreach (var control in outfit.GetPartControls())
            names.Add(animBuilder.GetPartParamName(outfit, control));
        }

        if (!config.EnableCustomMixer ||
            outfits.FirstOrDefault(candidate => candidate.OutfitObject == outfit.OutfitObject) != outfit)
          continue;

        foreach (var slot in outfit.GetMixerPartSlots())
        {
          string slotParameterName = Mixer.BuildMixerSlotParamName(config, outfit, slot);
          AddChoiceParameterNames(names, slotParameterName,
            new ChoiceParameterLayout(slot.Candidates.Count + 1,
              config.EnableParameterCompression));
        }
      }
      return names;
    }

    private static void AddChoiceParameterNames(
      ISet<string> names,
      string baseParameterName,
      ChoiceParameterLayout layout)
    {
      names.Add(baseParameterName);
      if (!layout.UsesCompression) return;
      for (int bit = 0; bit < layout.BitCount; bit++)
        names.Add(layout.GetBitParameterName(baseParameterName, bit));
    }

    public static GameObject ResolveDefaultChoiceObject(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      if (defaultOutfit == null) return null;
      if (outfitIndexMap.ContainsKey(defaultOutfit.BaseObject))
        return defaultOutfit.BaseObject;

      return defaultOutfit.GetAllObjects().FirstOrDefault(outfitIndexMap.ContainsKey);
    }

    public static int ResolveDefaultChoiceIndex(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      var defaultChoice = ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      return defaultChoice != null && outfitIndexMap.TryGetValue(defaultChoice, out var index)
        ? index
        : 0;
    }

    /// <summary>
    /// Mixer 进入时预选默认服装组中与默认版本对应的部件候选，并沿用普通 Parts
    /// 菜单的初始 activeSelf 状态；其它服装组及默认关闭部件均为 0（Off）。
    /// </summary>
    public static int GetMixerSlotDefaultValue(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData outfit,
      MixerPartSlot slot)
    {
      if (defaultOutfit == null || outfit == null || slot == null ||
          outfit.OutfitObject != defaultOutfit.OutfitObject)
        return 0;

      var defaultChoice = ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      int candidateIndex = slot.Candidates.FindIndex(candidate =>
        candidate.VariantObject == defaultChoice);
      if (candidateIndex < 0) return 0;

      var normalControl = outfit.GetPartControls().FirstOrDefault(control =>
        control.Parts != null && control.Parts.Count > 0 &&
        OutfitData.GetMixerSlotKey(outfit.BaseObject, control) == slot.Key);
      bool defaultActive = normalControl != null
        ? normalControl.Parts.All(part => part != null && part.activeSelf)
        : slot.Candidates[candidateIndex].Control.Parts.All(part =>
          part != null && part.activeSelf);
      return defaultActive ? candidateIndex + 1 : 0;
    }

    private void BuildPartsMenu(
      GameObject menuRoot,
      GameObject outfitSubmenu,
      OutfitData outfit,
      ModularAvatarParameters rootParams,
      Utils.MenuPresentationSnapshot menuPresentation)
    {
      var partsMenu = Utils.FindOrCreateUniqueChild(outfitSubmenu,
        Localization.DefaultPartsMenuObjectName(config));
      Utils.EnsureSubmenuOnNode(partsMenu, presentation: menuPresentation,
        semanticKey: Utils.GetPartsMenuSemanticKey(menuRoot, outfitSubmenu));
      Utils.EnsureDefaultMenuIcon(partsMenu, "OutlineBlank2");

      foreach (var control in outfit.GetPartControls())
      {
        string partParamName = animBuilder.GetPartParamName(outfit, control);
        bool partDefaultActive = control.Parts.All(part => part.activeSelf);

        var partNode = Utils.FindOrCreateChild(partsMenu, control.Name);
        var partItem = Utils.CreateMenuItem(partNode);

        Utils.ConfigureAsToggle(partItem);
        partItem.PortableControl.Parameter = partParamName;
        partItem.automaticValue = true;
        partItem.isDefault = partDefaultActive;
        partItem.isSaved = true;
        partItem.isSynced = true;
        Utils.ApplyMenuPresentation(menuPresentation, partNode, partItem, "");
        var iconTargets = (control.Parts ?? new List<GameObject>())
          .Where(part => part != null)
          .Distinct()
          .ToList();
        string partStableKey = "Part_" + string.Join("_",
          iconTargets.Select(part => Utils.GetHierarchyPath(config.CostumesRoot, part)));
        AddMenuIconRequest(partNode, iconTargets, partStableKey);

        Utils.AddOrUpdateParameter(rootParams, partParamName, ParameterSyncType.Bool,
          partDefaultActive ? 1 : 0, true);
      }
    }

    private IEnumerable<GameObject> GetMenuIconTargets(GameObject choiceObject)
    {
      var materialVariant = choiceObject != null
        ? choiceObject.GetComponent<ACCVariantMaterialOverride>()
        : null;
      if (materialVariant != null && materialVariant.OutfitBase != null)
        return new[] { materialVariant.OutfitBase };
      return choiceObject != null ? new[] { choiceObject } : Array.Empty<GameObject>();
    }

    private void AddMenuIconRequest(
      GameObject menuNode,
      IEnumerable<GameObject> targets,
      string stableKey,
      ACCVariantMaterialOverride materialVariant = null,
      bool useSharedOutfitFraming = false)
    {
      if (!config.AutoGenerateMenuIcons || menuNode == null) return;
      menuIconRequests.Add(new MenuIconRequest
      {
        MenuNode = menuNode,
        Targets = targets?.Where(target => target != null).Distinct().ToList()
          ?? new List<GameObject>(),
        StableKey = stableKey,
        MaterialVariant = materialVariant,
        UseSharedOutfitFraming = useSharedOutfitFraming
      });
    }

    #endregion

    #region 预览对话框

    private bool ShowPreflightDialog(
      List<OutfitData> outfits,
      OutfitData defaultOutfit,
      bool controllerExists,
      string controllerPath)
    {
      var sb = new System.Text.StringBuilder();
      sb.AppendLine(T("即将生成，摘要：", "Generation summary:"));
      sb.AppendLine(T($"- 根菜单名称：{config.EffectiveRootMenuName}", $"- Root Menu: {config.EffectiveRootMenuName}"));
      sb.AppendLine(T($"- 参数前缀：{config.EffectiveParamPrefix}", $"- Prefix: {config.EffectiveParamPrefix}"));
      sb.AppendLine(T($"- 服装数量：{outfits.Count}", $"- Outfits: {outfits.Count}"));
      sb.AppendLine(T($"- 部件数量：{outfits.Sum(o => o.Parts.Count)}", $"- Parts: {outfits.Sum(o => o.Parts.Count)}"));
      sb.AppendLine(T($"- 输出目录：{config.GetResolvedGeneratedFolder()}", $"- Output: {config.GetResolvedGeneratedFolder()}"));
      sb.AppendLine(controllerExists
        ? T($"- Controller：已存在，继续生成将覆盖 {controllerPath}",
          $"- Controller: existing file will be overwritten {controllerPath}")
        : T($"- Controller：将新建 {controllerPath}",
          $"- Controller: new file will be created {controllerPath}"));
      sb.AppendLine(T($"- 默认服装：{defaultOutfit?.Name ?? "未设置"}",
        $"- Default outfit: {defaultOutfit?.Name ?? "Not set"}"));
      if (Utils.FindDirectChild(config.CostumesRoot.transform, ACCConfig.MenuObjectName) != null)
      {
        sb.AppendLine(T("- 菜单对象：复用现有 ACC_Menu 的展示属性（如图标）；控制字段和子菜单会重建，不会复用旧参数。",
          "- Menu object: the existing ACC_Menu presentation (such as its icon) will be reused; controls and child menus will be rebuilt without reusing old parameters."));
      }
      int missingMergeArmatures = Utils.CountMissingMergeArmatures(outfits);
      if (missingMergeArmatures > 0)
        sb.AppendLine(T($"- 骨架合并：将对 {missingMergeArmatures} 个已识别服装调用 Modular Avatar 的 Setup Outfit。",
          $"- Armature merge: Modular Avatar Setup Outfit will run for {missingMergeArmatures} detected outfits."));
      var paramCounts = CountParameterBits(config, outfits, T);
      sb.AppendLine(T($"- 预计占用参数：{paramCounts}", $"- Estimated parameters: {paramCounts}"));
      sb.AppendLine("- " + GetAnimatorLayerSummary(config, outfits, T));
      if (config.EnableParameterCompression)
        sb.AppendLine("- " + GetCompressionSummary(config, outfits, T));
      if (config.EnableCustomMixer)
      {
        string mixerObjectName = string.IsNullOrWhiteSpace(config.CustomMixerName)
          ? Localization.DefaultMixerMenuObjectName(config)
          : config.CustomMixerName.Trim();
        sb.AppendLine(T($"- 混搭模式：已启用（节点名称：{mixerObjectName}）",
          $"- Custom Mixer: enabled (node name: {mixerObjectName})"));
      }
      if (config.AutoGenerateMenuIcons)
        sb.AppendLine(T("- 菜单图标：将仅拍摄各服装/部件并替换 ACC 菜单树现有图标。",
          "- Menu icons: each outfit/part will be captured in isolation and existing ACC menu-tree icons will be replaced."));

      return EditorUtility.DisplayDialog(T("生成确认", "Confirm Generation"), sb.ToString(),
        T("继续", "Continue"), T("取消", "Cancel"));
    }

    /// <summary>
    /// 估算 VRChat 参数位占用。压缩模式仅同步二进制 Bool，菜单 Int 保持本地。
    /// </summary>
    public static string CountParameterBits(ACCConfig config, List<OutfitData> outfits,
      System.Func<string, string, string> loc)
    {
      const int intBits = 8;

      int outfitChoices = outfits.SelectMany(outfit => outfit.GetAllObjects()).Distinct().Count();
      int mainChoices = outfitChoices + (config.EnableCustomMixer ? 1 : 0);
      var mainLayout = new ChoiceParameterLayout(mainChoices, config.EnableParameterCompression);
      int partBits = 0;
      int mixerSlotBits = 0;
      int GetSynchronizedBits(ChoiceParameterLayout layout)
      {
        if (!layout.RequiresSynchronization) return 0;
        if (layout.UsesBoolean) return 1;
        return layout.UsesCompression ? layout.BitCount : intBits;
      }

      foreach (var outfit in outfits)
      {
        if (config.EnableParts)
          partBits += outfit.GetPartControls().Count;

        if (config.EnableCustomMixer && IsFirstMixerOutfit(outfit, outfits))
        {
          foreach (var slot in outfit.GetMixerPartSlots())
            mixerSlotBits += GetSynchronizedBits(new ChoiceParameterLayout(
              slot.Candidates.Count + 1, config.EnableParameterCompression));
        }
      }

      int mainBits = GetSynchronizedBits(mainLayout);
      int totalBits = mainBits + partBits + mixerSlotBits;
      var parts = new System.Collections.Generic.List<string>();
      parts.Add(!mainLayout.RequiresSynchronization
        ? loc("主选择固定值 0bit", "fixed main choice 0bit")
        : mainLayout.UsesBoolean
          ? loc("主选择 Bool", "main Bool")
        : mainLayout.UsesCompression
          ? $"{loc("主选择压缩 Bool", "compressed main Bool")} {mainBits}bit"
          : $"{loc("主 Int", "main Int")} {mainBits}bit");
      if (partBits > 0)
        parts.Add($"{loc("部件 Bool", "part Bool")} {partBits}bit");
      if (mixerSlotBits > 0)
        parts.Add($"{loc("混搭槽位", "mixer slots")} {mixerSlotBits}bit");
      return $"{totalBits}bit ({string.Join(", ", parts)})";
    }

    /// <summary>返回压缩模式实际新增的共享编码/解码 Animator Layer 数。</summary>
    public static int CountCompressionLayers(ACCConfig config, List<OutfitData> outfits)
    {
      return CountCompressedChoiceDomains(config, outfits) > 0 ? 1 : 0;
    }

    /// <summary>统计实际需要编码/解码的选择域数量。</summary>
    public static int CountCompressedChoiceDomains(ACCConfig config, List<OutfitData> outfits)
    {
      if (!config.EnableParameterCompression) return 0;

      int outfitChoices = outfits.SelectMany(outfit => outfit.GetAllObjects()).Distinct().Count();
      int mainChoices = outfitChoices + (config.EnableCustomMixer ? 1 : 0);
      var mainLayout = new ChoiceParameterLayout(mainChoices, true);
      int domains = mainLayout.UsesCompression && mainLayout.BitCount > 0 ? 1 : 0;

      if (!config.EnableCustomMixer) return domains;
      foreach (var outfit in outfits)
      {
        if (!IsFirstMixerOutfit(outfit, outfits)) continue;
        foreach (var slot in outfit.GetMixerPartSlots())
        {
          var slotLayout = new ChoiceParameterLayout(slot.Candidates.Count + 1, true);
          if (slotLayout.UsesCompression && slotLayout.BitCount > 0)
            domains++;
        }
      }
      return domains;
    }

    /// <summary>估算生成 Controller 的实际 Layer 数。</summary>
    public static int CountGeneratedAnimatorLayers(ACCConfig config, List<OutfitData> outfits)
    {
      // 保留 Unity 默认 Base Layer；ACC 的选择树始终以普通追加层合入，避免 MA MMD
      // Relay 与首层特殊语义干扰服装参数。
      int layers = 2; // Unity 默认 Base Layer + Outfit Switching。
      bool hasNormalParts = config.EnableParts && outfits.Any(o => o.GetPartControls().Count > 0);
      bool hasMixerParts = config.EnableCustomMixer && outfits
        .GroupBy(outfit => outfit.OutfitObject)
        .Any(group => group.First().GetMixerPartSlots().Count > 0);
      if (hasNormalParts || hasMixerParts) layers++;
      layers += CountCompressionLayers(config, outfits);
      return layers;
    }

    /// <summary>压缩生成的实际 Animator 开销与远端传播路径说明。</summary>
    public static string GetCompressionSummary(ACCConfig config, List<OutfitData> outfits,
      System.Func<string, string, string> loc)
    {
      int layerCount = CountCompressionLayers(config, outfits);
      int domainCount = CountCompressedChoiceDomains(config, outfits);
      int totalLayerCount = CountGeneratedAnimatorLayers(config, outfits);
      return loc(
        $"参数压缩：{domainCount} 个有效选择域共用 {layerCount} 个编码/解码 Animator Layer；Controller 总计 {totalLayerCount} 个 Layer，并新增对应的本地 Int 与同步 Bool 位参数。",
        $"Parameter compression: {domainCount} active choice domains share {layerCount} encode/decode Animator Layer; the Controller has {totalLayerCount} Layers total, plus the corresponding local Int and synced Bool-bit parameters.");
    }

    public static string GetAnimatorLayerSummary(ACCConfig config, List<OutfitData> outfits,
      System.Func<string, string, string> loc)
    {
      int layerCount = CountGeneratedAnimatorLayers(config, outfits);
      return loc($"Animator Layer：{layerCount} 个",
        $"Animator Layers: {layerCount}");
    }

    private static bool IsFirstMixerOutfit(OutfitData outfit, IEnumerable<OutfitData> outfits)
    {
      return outfits.FirstOrDefault(candidate => candidate.OutfitObject == outfit.OutfitObject) == outfit;
    }

    #endregion
  }
}
