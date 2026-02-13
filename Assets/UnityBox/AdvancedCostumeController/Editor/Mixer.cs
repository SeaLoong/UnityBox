using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 混搭器 — 负责 CustomMixer 的菜单构建和参数注册
  /// CustomMixer 是一个特殊"虚拟服装"，允许用户在 VRC 表情菜单中
  /// 自由组合不同服装的部件和变体。
  /// </summary>
  public static class Mixer
  {
    /// <summary>
    /// 构建 CustomMixer 的完整菜单结构
    /// </summary>
    /// <param name="config">ACC 配置</param>
    /// <param name="menuRoot">菜单根节点</param>
    /// <param name="outfits">所有服装数据</param>
    /// <param name="outfitIndexMap">服装→索引映射</param>
    /// <param name="customMixerIndex">CustomMixer 使用的索引值</param>
    /// <param name="rootParams">MA 参数组件</param>
    /// <param name="defaultOutfit">默认服装</param>
    public static void BuildCustomMixerMenu(
      ACCConfig config,
      GameObject menuRoot,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      int customMixerIndex,
      ModularAvatarParameters rootParams,
      OutfitData defaultOutfit)
    {
      var costumesRoot = config.CostumesRoot;
      string mixerName = config.CustomMixerName;
      string paramPrefix = config.ParamPrefix;
      string costumeParamName = config.CostumeParamName;

      // 创建 CustomMixer 子菜单
      var mixerSubmenu = Utils.FindOrCreateChild(menuRoot, mixerName);
      Utils.EnsureSubmenuOnNode(mixerSubmenu, mixerName);

      // 添加 CustomMixer 总开关（设置 costume = customMixerIndex）
      var enableNode = Utils.FindOrCreateChild(mixerSubmenu, "Enable");
      var enableMi = Utils.CreateMenuItem(enableNode);
      Undo.RecordObject(enableMi, "Configure CustomMixer enable toggle");
      enableMi.PortableControl.Type = PortableControlType.Toggle;
      enableMi.PortableControl.Parameter = costumeParamName;
      enableMi.automaticValue = false;
      enableMi.PortableControl.Value = customMixerIndex;
      enableMi.isSaved = true;
      enableMi.isSynced = true;

      // 已处理的服装组（防止变体组重复处理）
      var processedOutfitObjects = new HashSet<GameObject>();

      foreach (var outfit in outfits)
      {
        // 避免同一个 OutfitObject 被重复处理（多个变体共享同一个 OutfitObject）
        if (processedOutfitObjects.Contains(outfit.OutfitObject)) continue;
        processedOutfitObjects.Add(outfit.OutfitObject);

        // 无部件也无变体则跳过
        if (outfit.Parts.Count == 0 && !outfit.HasVariants()) continue;

        // 计算菜单路径
        string outfitRelPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        var pathParts = outfitRelPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 创建菜单层级
        var curMenu = mixerSubmenu;
        for (int i = 0; i < pathParts.Length; i++)
        {
          curMenu = Utils.FindOrCreateChild(curMenu, pathParts[i]);
          Utils.EnsureSubmenuOnNode(curMenu, pathParts[i]);
        }

        // 有变体的情况
        if (outfit.HasVariants())
        {
          BuildMixerVariantGroup(config, curMenu, outfit, rootParams);
        }

        // 有部件的情况
        if (outfit.Parts.Count > 0)
        {
          bool useParts = outfit.HasVariants(); // 有变体时放到 Parts 子菜单
          var partsParent = useParts
            ? CreatePartsSubmenu(curMenu)
            : curMenu;

          BuildMixerPartsMenu(config, partsParent, outfit, rootParams);
        }
      }
    }

    #region 变体组

    /// <summary>
    /// 为有变体的服装创建变体切换菜单
    /// 使用 Int 参数控制变体选择
    /// </summary>
    private static void BuildMixerVariantGroup(
      ACCConfig config,
      GameObject parentMenu,
      OutfitData outfit,
      ModularAvatarParameters rootParams)
    {
      string variantParamName = BuildMixerVariantGroupParamName(config, outfit);

      // 注册变体组参数
      Utils.AddOrUpdateParameter(rootParams, variantParamName, ParameterSyncType.Int, 0, true);

      // 为每个变体创建 Toggle
      var allVariants = outfit.GetAllObjects();
      for (int i = 0; i < allVariants.Count; i++)
      {
        var variant = allVariants[i];
        var variantNode = Utils.FindOrCreateChild(parentMenu, variant.name);
        var variantMi = Utils.CreateMenuItem(variantNode);
        Undo.RecordObject(variantMi, "Configure CustomMixer variant toggle");
        variantMi.PortableControl.Type = PortableControlType.Toggle;
        variantMi.PortableControl.Parameter = variantParamName;
        variantMi.automaticValue = false;
        variantMi.PortableControl.Value = i;
        variantMi.isSaved = true;
        variantMi.isSynced = true;
      }
    }

    #endregion

    #region 部件菜单

    /// <summary>
    /// 创建 Parts 子菜单节点
    /// </summary>
    private static GameObject CreatePartsSubmenu(GameObject parent)
    {
      var partsMenu = Utils.FindOrCreateChild(parent, "Parts");
      Utils.EnsureSubmenuOnNode(partsMenu);
      return partsMenu;
    }

    /// <summary>
    /// 为指定服装创建混搭模式下的部件开关菜单
    /// 使用独立于普通模式的参数，避免参数冲突
    /// </summary>
    private static void BuildMixerPartsMenu(
      ACCConfig config,
      GameObject partsParent,
      OutfitData outfit,
      ModularAvatarParameters rootParams)
    {
      string outfitRelPath = outfit.RelativePath;

      foreach (var part in outfit.Parts)
      {
        string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
        string partParamName = BuildMixerPartParamName(config, outfitRelPath, partRelPath);

        var partNode = Utils.FindOrCreateChild(partsParent, part.name);
        var partMi = Utils.CreateMenuItem(partNode);
        Undo.RecordObject(partMi, "Configure CustomMixer part toggle");
        partMi.PortableControl.Type = PortableControlType.Toggle;
        partMi.PortableControl.Parameter = partParamName;
        partMi.automaticValue = true;
        partMi.isDefault = false; // 混搭模式下默认全部关闭
        partMi.isSaved = true;
        partMi.isSynced = true;

        // 注册参数
        Utils.AddOrUpdateParameter(rootParams, partParamName, ParameterSyncType.Bool, 0f, true);
      }
    }

    #endregion

    #region 参数名构建

    /// <summary>
    /// 构建混搭模式下的部件参数名
    /// 格式: {prefix}/{mixerName}/{outfitRelPath}/{partRelPath}
    /// </summary>
    public static string BuildMixerPartParamName(ACCConfig config, string outfitRelPath, string partRelPath)
    {
      string raw = config.CustomMixerName + "/" + outfitRelPath + "/" + partRelPath;
      return Utils.BuildParamName(config.ParamPrefix, raw);
    }

    /// <summary>
    /// 构建混搭模式下的变体组参数名
    /// 格式: {prefix}/{mixerName}/{outfitGroupRelPath}
    /// </summary>
    public static string BuildMixerVariantGroupParamName(ACCConfig config, OutfitData outfit)
    {
      string groupRelPath = Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      string raw = config.CustomMixerName + "/" + groupRelPath;
      return Utils.BuildParamName(config.ParamPrefix, raw);
    }

    #endregion
  }
}
