using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

namespace SeaLoongUnityBox.ACC
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

    /// <summary>
    /// 执行完整的生成流程
    /// </summary>
    public void Execute(
      List<OutfitData> selectedOutfits,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData defaultOutfit)
    {
      string controllerPath = Path.Combine(config.GeneratedFolder, "CostumeController.controller").Replace("\\", "/");
      if (File.Exists(controllerPath))
      {
        if (!EditorUtility.DisplayDialog("覆盖确认",
              $"控制器文件已存在:\n{controllerPath}\n\n是否覆盖?", "覆盖", "取消"))
          return;
      }

      if (!ShowPreflightDialog(selectedOutfits, defaultOutfit)) return;

      try
      {
        EditorUtility.DisplayProgressBar("生成中", "初始化...", 0.1f);

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Advanced Costume Controller");

        // 创建菜单根节点
        var costumesRoot = config.CostumesRoot;
        var menuRoot = Utils.PrepareChildRoot(costumesRoot, costumesRoot.name + " Menu");
        Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot);
        var mergeAnimator = Undo.AddComponent<ModularAvatarMergeAnimator>(menuRoot);
        var rootParams = Utils.EnsureParametersComponent(menuRoot);
        Utils.EnsureSubmenuOnNode(menuRoot, costumesRoot.name);

        EditorUtility.DisplayProgressBar("生成中", "构建菜单...", 0.3f);
        BuildMenus(menuRoot, selectedOutfits, outfitIndexMap, rootParams, defaultOutfit);

        // CustomMixer 菜单
        if (config.EnableCustomMixer)
        {
          EditorUtility.DisplayProgressBar("生成中", "创建混搭菜单...", 0.5f);
          int customMixerIndex = outfitIndexMap.Count;
          Mixer.BuildCustomMixerMenu(
            config, menuRoot, selectedOutfits, outfitIndexMap,
            customMixerIndex, rootParams, defaultOutfit);
        }

        EditorUtility.DisplayProgressBar("生成中", "创建动画控制器...", 0.7f);
        if (!Directory.Exists(config.GeneratedFolder))
        {
          Directory.CreateDirectory(config.GeneratedFolder);
          AssetDatabase.Refresh();
        }

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

    #region 菜单构建

    private void BuildMenus(
      GameObject menuRoot,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      ModularAvatarParameters rootParams,
      OutfitData defaultOutfit)
    {
      // 添加服装参数
      int defaultIndex = defaultOutfit != null && outfitIndexMap.ContainsKey(defaultOutfit.BaseObject)
        ? outfitIndexMap[defaultOutfit.BaseObject] : 0;
      Utils.AddOrUpdateParameter(rootParams, config.CostumeParamName, ParameterSyncType.Int, defaultIndex, true);

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
            menuItem.PortableControl.Parameter = config.CostumeParamName;
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
          menuItem.PortableControl.Parameter = config.CostumeParamName;
          menuItem.PortableControl.Value = outfitIndexMap[outfit.BaseObject];
          menuItem.automaticValue = false;
          menuItem.isSaved = true;
          menuItem.isSynced = true;
        }
      }
    }

    private void BuildPartsMenu(
      GameObject outfitSubmenu,
      OutfitData outfit,
      ModularAvatarParameters rootParams)
    {
      var partsMenu = Utils.FindOrCreateChild(outfitSubmenu, "Parts");
      Utils.EnsureSubmenuOnNode(partsMenu);

      foreach (var part in outfit.Parts)
      {
        string partParamName = animBuilder.GetPartParamName(outfit, part);
        bool partDefaultActive = part.activeSelf;

        var partNode = Utils.FindOrCreateChild(partsMenu, part.name);
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

    private bool ShowPreflightDialog(List<OutfitData> outfits, OutfitData defaultOutfit)
    {
      var sb = new System.Text.StringBuilder();
      sb.AppendLine("即将生成，摘要：");
      sb.AppendLine($"- 服装数量：{outfits.Count}");
      sb.AppendLine($"- 部件数量：{outfits.Sum(o => o.Parts.Count)}");
      sb.AppendLine($"- 输出目录：{config.GeneratedFolder}");
      sb.AppendLine($"- 默认服装：{defaultOutfit?.Name ?? "未设置"}");
      if (config.EnableCustomMixer)
        sb.AppendLine($"- 混搭模式：已启用 ({config.CustomMixerName})");

      return EditorUtility.DisplayDialog("生成确认", sb.ToString(), "继续", "取消");
    }

    #endregion
  }
}
