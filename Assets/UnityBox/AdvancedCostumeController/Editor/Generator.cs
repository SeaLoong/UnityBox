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

      if (config.EnableCustomMixer && outfitIndexMap.Count > ACCConfig.CustomMixerIndex)
      {
        EditorUtility.DisplayDialog(T("生成失败", "Generation Failed"),
          T("选中的服装对象数量超过 255，无法为混搭保留固定参数值 255。",
            "More than 255 outfit objects are selected; mixer value 255 cannot remain unique."), "OK");
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
      string controllerPath = Path.Combine(resolvedFolder, config.GetControllerFileName()).Replace("\\", "/");
      if (!ShowPreflightDialog(selectedOutfits, defaultOutfit,
        File.Exists(controllerPath), controllerPath)) return;

      try
      {
        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("初始化…", "Initializing…"), 0.1f);

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Advanced Costume Controller");

        // 创建菜单根节点
        var costumesRoot = config.CostumesRoot;
        var menuRoot = Utils.PrepareChildRoot(costumesRoot, ACCConfig.MenuObjectName);
        Utils.EnsureMenuInstaller(menuRoot);
        var mergeAnimator = menuRoot.GetComponent<ModularAvatarMergeAnimator>();
        if (mergeAnimator == null)
        {
          try { mergeAnimator = Undo.AddComponent<ModularAvatarMergeAnimator>(menuRoot); }
          catch { mergeAnimator = menuRoot.AddComponent<ModularAvatarMergeAnimator>(); }
        }
        var rootParams = Utils.EnsureParametersComponent(menuRoot);
        Utils.EnsureSubmenuOnNode(menuRoot, config.EffectiveRootMenuName);

        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("构建菜单…", "Building menus…"), 0.3f);
        BuildMenus(menuRoot, selectedOutfits, outfitIndexMap, rootParams, defaultOutfit);

        // CustomMixer 菜单
        if (config.EnableCustomMixer)
        {
          EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("创建混搭菜单…", "Creating mixer menu…"), 0.5f);
          int customMixerIndex = ACCConfig.CustomMixerIndex;
          Mixer.BuildCustomMixerMenu(
            config, menuRoot, selectedOutfits, outfitIndexMap,
            customMixerIndex, rootParams, defaultOutfit);
        }

        EditorUtility.DisplayProgressBar(T("生成中", "Generating"), T("创建动画控制器…", "Creating animator controller…"), 0.7f);
        PrepareGeneratedFolder(resolvedFolder);

        var controller = animBuilder.CreateController(selectedOutfits, outfitIndexMap, defaultOutfit, controllerPath);

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
                  (config.EnableCustomMixer ? ", 已启用混搭模式" : ""));
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    private static void PrepareGeneratedFolder(string resolvedFolder)
    {
      // 输出目录按 ParamPrefix 隔离，因而可以在重新生成时安全清理旧 Controller 与 Clip，
      // 避免 AssetDatabase.CreateAsset 因同名旧动画文件而失败。
      if (AssetDatabase.IsValidFolder(resolvedFolder))
        AssetDatabase.DeleteAsset(resolvedFolder);

      Directory.CreateDirectory(resolvedFolder);
      AssetDatabase.Refresh();
    }

    #region 菜单构建

    private void BuildMenus(
      GameObject menuRoot,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      ModularAvatarParameters rootParams,
      OutfitData defaultOutfit)
    {
      // 添加服装参数
      int defaultIndex = ResolveDefaultIndex(defaultOutfit, outfitIndexMap);
        Utils.AddOrUpdateParameter(rootParams, config.MainParameterName, ParameterSyncType.Int, defaultIndex, true);

      var costumesRoot = config.CostumesRoot;

      foreach (var outfit in outfits)
      {
        string outfitPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        var pathParts = outfitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 创建父级菜单路径
        GameObject parentMenu = menuRoot;
        for (int i = 0; i < pathParts.Length - 1; i++)
        {
          parentMenu = Utils.FindOrCreateChild(parentMenu, pathParts[i]);
          Utils.EnsureSubmenuOnNode(parentMenu);
        }

        string outfitName = pathParts.Length > 0 ? pathParts[pathParts.Length - 1] : outfit.Name;
        bool needSubmenu = config.EnableParts || outfit.HasVariants();

        if (needSubmenu)
        {
          var outfitSubmenu = Utils.FindOrCreateChild(parentMenu, outfitName);
          Utils.EnsureSubmenuOnNode(outfitSubmenu);

          // 部件菜单
          if (config.EnableParts && outfit.Parts.Count > 0)
            BuildPartsMenu(outfitSubmenu, outfit, rootParams);

          // 本体和变体开关
          foreach (var obj in outfit.GetAllObjects())
          {
            if (!outfitIndexMap.ContainsKey(obj)) continue;

            var itemNode = Utils.FindOrCreateChild(outfitSubmenu, obj.name);
            var menuItem = Utils.CreateMenuItem(itemNode);
            menuItem.PortableControl.Type = PortableControlType.Toggle;
              menuItem.PortableControl.Parameter = config.MainParameterName;
            menuItem.PortableControl.Value = outfitIndexMap[obj];
            menuItem.automaticValue = false;
            menuItem.isSaved = true;
            menuItem.isSynced = true;
          }
        }
        else
        {
          if (!outfitIndexMap.ContainsKey(outfit.BaseObject)) continue;

          var itemNode = Utils.FindOrCreateChild(parentMenu, outfitName);
          var menuItem = Utils.CreateMenuItem(itemNode);
          menuItem.PortableControl.Type = PortableControlType.Toggle;
            menuItem.PortableControl.Parameter = config.MainParameterName;
          menuItem.PortableControl.Value = outfitIndexMap[outfit.BaseObject];
          menuItem.automaticValue = false;
          menuItem.isSaved = true;
          menuItem.isSynced = true;
        }
      }
    }

    private static int ResolveDefaultIndex(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      if (defaultOutfit != null)
      {
        if (outfitIndexMap.TryGetValue(defaultOutfit.BaseObject, out var baseIndex))
          return baseIndex;

        foreach (var obj in defaultOutfit.GetAllObjects())
        {
          if (outfitIndexMap.TryGetValue(obj, out var variantIndex))
            return variantIndex;
        }
      }
      return 0;
    }

    private void BuildPartsMenu(
      GameObject outfitSubmenu,
      OutfitData outfit,
      ModularAvatarParameters rootParams)
    {
      var partsMenu = Utils.FindOrCreateChild(outfitSubmenu, "Parts");
      Utils.EnsureSubmenuOnNode(partsMenu, T("部件", "Parts"));

      foreach (var control in outfit.GetPartControls())
      {
        string partParamName = animBuilder.GetPartParamName(outfit, control);
        bool partDefaultActive = control.Parts.All(part => part.activeSelf);

        var partNode = Utils.FindOrCreateChild(partsMenu, control.Name);
        var partItem = Utils.CreateMenuItem(partNode);

        partItem.PortableControl.Type = PortableControlType.Toggle;
        partItem.PortableControl.Parameter = partParamName;
        partItem.automaticValue = true;
        partItem.isDefault = partDefaultActive;
        partItem.isSaved = true;
        partItem.isSynced = true;

        Utils.AddOrUpdateParameter(rootParams, partParamName, ParameterSyncType.Bool,
          partDefaultActive ? 1 : 0, true);
      }
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
        sb.AppendLine(T("- 菜单对象：复用现有 ACC_Menu，将保留已有菜单图标等非 ACC 属性。",
          "- Menu object: existing ACC_Menu will be reused; non-ACC properties such as icons will be preserved."));
      }
      var paramCounts = CountParameterBits(config, outfits, T);
      sb.AppendLine(T($"- 预计占用参数：{paramCounts}", $"- Estimated parameters: {paramCounts}"));
      if (config.EnableCustomMixer)
      {
        string mixerLabel = string.IsNullOrWhiteSpace(config.CustomMixerName)
          ? T("混搭", "Custom Mix")
          : config.CustomMixerName;
        sb.AppendLine(T($"- 混搭模式：已启用（{mixerLabel}）",
          $"- Custom Mixer: enabled ({mixerLabel})"));
      }

      return EditorUtility.DisplayDialog(T("生成确认", "Confirm Generation"), sb.ToString(),
        T("继续", "Continue"), T("取消", "Cancel"));
    }

    /// <summary>
    /// 估算 VRChat 参数位占用。
    /// 主 Int 1 个 + 普通部件 Bool 每控制项 1 个 + 混搭部件槽位 Int 每槽位 1 个。
    /// </summary>
    public static string CountParameterBits(ACCConfig config, List<OutfitData> outfits,
      System.Func<string, string, string> loc)
    {
      const int boolBits = 1;
      const int intBits = 8;

      int mainInt = 1;
      int partBools = 0;
      int mixerSlotInts = 0;

      foreach (var outfit in outfits)
      {
        if (config.EnableParts)
          partBools += outfit.GetPartControls().Count;

        if (config.EnableCustomMixer)
          mixerSlotInts += outfit.GetMixerPartSlots().Count;
      }

      int totalBits = mainInt * intBits + partBools * boolBits +
                      mixerSlotInts * intBits;
      var parts = new System.Collections.Generic.List<string>();
      parts.Add($"{loc("主 Int", "main Int")} 1 × {intBits} = {mainInt * intBits}bit");
      if (partBools > 0)
        parts.Add($"{loc("部件 Bool", "part Bool")} {partBools} × {boolBits} = {partBools * boolBits}bit");
      if (mixerSlotInts > 0)
        parts.Add($"{loc("混搭槽位 Int", "mixer slot Int")} {mixerSlotInts} × {intBits} = {mixerSlotInts * intBits}bit");
      return $"{totalBits}bit ({string.Join(", ", parts)})";
    }

    #endregion
  }
}
