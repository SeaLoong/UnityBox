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
      int customMixerValue,
      ModularAvatarParameters rootParams,
      Utils.MenuPresentationSnapshot menuPresentation,
      OutfitData defaultOutfit)
    {
      var costumesRoot = config.CostumesRoot;
      string mixerObjectName = string.IsNullOrWhiteSpace(config.CustomMixerName)
        ? Localization.DefaultMixerMenuObjectName(config)
        : config.CustomMixerName.Trim();
      string mainParameterName = config.MainParameterName;

      // 创建混搭子菜单
      var mixerSubmenu = Utils.FindOrCreateUniqueChild(menuRoot, mixerObjectName);
      Utils.EnsureSubmenuOnNode(mixerSubmenu, presentation: menuPresentation,
        semanticKey: Utils.MixerRootSemanticKey);

      var mainLayout = Generator.GetMainChoiceLayout(outfitIndexMap, true,
        config.EnableParameterCompression);
      // 已处理的服装组
      var processedOutfitObjects = new HashSet<GameObject>();

      foreach (var outfit in outfits)
      {
        if (processedOutfitObjects.Contains(outfit.OutfitObject)) continue;
        processedOutfitObjects.Add(outfit.OutfitObject);

        var slots = outfit.GetMixerPartSlots();
        if (slots.Count == 0) continue;

        // 创建服装组菜单层级
        string outfitRelPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        var curMenu = Utils.EnsureSubmenuPath(mixerSubmenu, outfitRelPath, menuPresentation,
          Utils.MixerPathSemanticKeyPrefix);

        // 菜单按“服装组 → 变体 → 部件”组织。
        // 不在混搭菜单中显示普通模式的 Parts/Groups 分类层，参数仍复用普通分组槽位。
        foreach (var variant in outfit.GetAllObjects())
        {
          var variantMenu = Utils.FindOrCreateChild(curMenu, variant.name);
          Utils.EnsureSubmenuOnNode(variantMenu, presentation: menuPresentation,
            semanticKey: Utils.GetMixerPathSemanticKey(outfitRelPath, variant.name));

          foreach (var slot in slots)
          {
            int candidateIndex = slot.Candidates.FindIndex(item => item.VariantObject == variant);
            if (candidateIndex < 0) continue;

            string slotParamName = BuildMixerSlotParamName(config, outfit, slot);
            var slotLayout = new ChoiceParameterLayout(slot.Candidates.Count + 1,
              config.EnableParameterCompression);
            int slotDefaultValue = Generator.GetMixerSlotDefaultValue(defaultOutfit,
              outfitIndexMap, outfit, slot);
            Generator.AddChoiceParameters(rootParams, slotParamName, slotLayout, slotDefaultValue);

            // 直接以显示名作为节点名，使默认菜单名称跟随对象名。少见的同名槽位
            // 才追加稳定序号，避免同一个变体菜单下两个控制项相互覆盖。
            var candidateNode = Utils.FindOrCreateUniqueChild(variantMenu, slot.Name);
            var candidateMi = Utils.CreateMenuItem(candidateNode);
            Undo.RecordObject(candidateMi, "Configure mixer part candidate");
            // 槽位 0 是合法的 Off 状态；Toggle 允许用户再次点击当前候选将其写回 0。
            // 这与“必须始终有一个主服装/必须始终处于某个主选择值”的 Button 语义不同。
            candidateMi.PortableControl.Type = PortableControlType.Toggle;
            Generator.ConfigureChoiceMenuItem(candidateMi, slotParamName, slotLayout, candidateIndex + 1);
            candidateMi.isDefault = candidateIndex + 1 == slotDefaultValue;
            candidateMi.isSaved = true;
            candidateMi.isSynced = !slotLayout.UsesCompression;
            Utils.ApplyMenuPresentation(menuPresentation, candidateNode, candidateMi, "");
          }
        }
      }

      // 最后创建 Enable，避免服装组恰好也叫“Enable/启用”时覆盖其菜单；再将它置顶。
      var enableNode = Utils.FindOrCreateUniqueChild(mixerSubmenu,
        Localization.DefaultMixerEnableObjectName(config));
      enableNode.transform.SetAsFirstSibling();
      var enableMi = Utils.CreateMenuItem(enableNode);
      Undo.RecordObject(enableMi, "Configure mixer toggle");
      // The mixer entry is a persistent state; Button would only apply while held.
      enableMi.PortableControl.Type = PortableControlType.Toggle;
      Generator.ConfigureChoiceMenuItem(enableMi, mainParameterName, mainLayout, customMixerValue);
      enableMi.isSaved = true;
      enableMi.isSynced = !mainLayout.UsesCompression;
      Utils.ApplyMenuPresentation(menuPresentation, enableNode, enableMi, "");
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
