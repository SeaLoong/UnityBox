using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 混搭器 — 负责混搭模式的菜单构建和参数注册。
  /// 混搭是一个特殊"虚拟服装"，选中后激活所有服装本体，
  /// 允许用户跨服装自由搭配变体和部件。
  /// </summary>
  public static class Mixer
  {
    private static string T(ACCConfig config, string chinese, string english) =>
      Localization.Text(config, chinese, english);

    /// <summary>
    /// 构建混搭模式的完整菜单结构
    /// </summary>
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
      string mixerObjectName = string.IsNullOrWhiteSpace(config.CustomMixerName)
        ? "CustomMixer"
        : config.CustomMixerName;
      string mixerLabel = string.IsNullOrWhiteSpace(config.CustomMixerName)
        ? T(config, "混搭", "Custom Mix")
        : config.CustomMixerName;
      string mainParameterName = config.MainParameterName;

      // 创建混搭子菜单
      var mixerSubmenu = Utils.FindOrCreateChild(menuRoot, mixerObjectName);
      Utils.EnsureSubmenuOnNode(mixerSubmenu, mixerLabel);

      // 混搭特殊服装入口：设置主参数到混搭索引；变体本身不再有额外开关
      var enableNode = Utils.FindOrCreateChild(mixerSubmenu, "Enable");
      var enableMi = Utils.CreateMenuItem(enableNode);
      Undo.RecordObject(enableMi, "Configure mixer toggle");
      enableMi.label = T(config, "启用", "Enable");
      enableMi.PortableControl.Type = PortableControlType.Toggle;
      enableMi.PortableControl.Parameter = mainParameterName;
      enableMi.automaticValue = false;
      enableMi.PortableControl.Value = customMixerIndex;
      enableMi.isSaved = true;
      enableMi.isSynced = true;

      // 已处理的服装组
      var processedOutfitObjects = new HashSet<GameObject>();

      foreach (var outfit in outfits)
      {
        if (processedOutfitObjects.Contains(outfit.OutfitObject)) continue;
        processedOutfitObjects.Add(outfit.OutfitObject);

        var slots = outfit.GetMixerPartSlots();
        if (slots.Count == 0) continue;

        string outfitRelPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        var pathParts = outfitRelPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 创建服装组菜单层级
        var curMenu = mixerSubmenu;
        for (int i = 0; i < pathParts.Length; i++)
        {
          curMenu = Utils.FindOrCreateChild(curMenu, pathParts[i]);
          Utils.EnsureSubmenuOnNode(curMenu, pathParts[i]);
        }

        // 菜单按“服装组 → 变体 → 部件”组织。
        // 不在混搭菜单中显示普通模式的 Parts/Groups 分类层，参数仍复用普通分组槽位。
        foreach (var variant in outfit.GetAllObjects())
        {
          var variantMenu = Utils.FindOrCreateChild(curMenu, variant.name);
          Utils.EnsureSubmenuOnNode(variantMenu, variant.name);

          foreach (var slot in slots)
          {
            int candidateIndex = slot.Candidates.FindIndex(item => item.VariantObject == variant);
            if (candidateIndex < 0) continue;

            string slotParamName = BuildMixerSlotParamName(config, outfit, slot);
            Utils.AddOrUpdateParameter(rootParams, slotParamName,
              ParameterSyncType.Int, 0, true);

            // 使用槽位键作为对象名，避免同名普通部件与分组相互覆盖；显示标签仍使用分组名称。
            string slotObjectName = "Slot_" + Utils.SanitizeForFileName(slot.Key);
            var candidateNode = Utils.FindOrCreateChild(variantMenu, slotObjectName);
            var candidateMi = Utils.CreateMenuItem(candidateNode);
            Undo.RecordObject(candidateMi, "Configure mixer part candidate");
            candidateMi.label = slot.Name;
            candidateMi.PortableControl.Type = PortableControlType.Toggle;
            candidateMi.PortableControl.Parameter = slotParamName;
            candidateMi.PortableControl.Value = candidateIndex + 1;
            candidateMi.automaticValue = false;
            candidateMi.isSaved = true;
            candidateMi.isSynced = true;
          }
        }
      }
    }

    /// <summary>混搭模式下一个部件槽位的 Int 参数名。</summary>
    public static string BuildMixerSlotParamName(
      ACCConfig config, OutfitData outfit, MixerPartSlot slot)
    {
      string groupPath = Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      string raw = ACCConfig.MixerParamPrefix + "/" + groupPath + "/" + slot.Key;
      return Utils.BuildParamName(config.MainParameterName, raw);
    }

  }
}
