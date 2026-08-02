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
  /// 混搭是一个特殊“虚拟服装”，选中后按部件/分组槽位选择本体或变体候选，
  /// 允许用户跨服装组自由搭配部件。
  /// </summary>
  public static class Mixer
  {
    private static string T(ACCConfig config, string chinese, string english) =>
      Localization.Text(config, chinese, english);

    /// <summary>
    /// 构建混搭模式的完整菜单结构
    /// </summary>
    internal static void BuildCustomMixerMenu(
      ACCConfig config,
      GameObject menuRoot,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      int customMixerValue,
      ModularAvatarParameters rootParams,
      Utils.MenuPresentationSnapshot menuPresentation,
      OutfitData defaultOutfit,
      List<MenuIconRequest> menuIconRequests)
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
      var allOutfitTargets = outfits
        .Select(outfit => outfit.GetAllObjects().FirstOrDefault() ?? outfit.BaseObject)
        .Select(target =>
        {
          var marker = target != null
            ? target.GetComponent<ACCVariantMaterialOverride>()
            : null;
          return marker != null && marker.OutfitBase != null
            ? marker.OutfitBase
            : target;
        })
        .Where(target => target != null)
        .Distinct()
        .ToList();
      MenuIconGenerator.AddRequest(config, menuIconRequests, mixerSubmenu, allOutfitTargets,
        "Mixer_AllOutfits", useSharedOutfitFraming: true);
      // 已处理的服装组
      var processedOutfitObjects = new HashSet<GameObject>();

      foreach (var outfit in outfits)
      {
        if (processedOutfitObjects.Contains(outfit.OutfitObject)) continue;
        processedOutfitObjects.Add(outfit.OutfitObject);

        var slots = outfit.GetMixerPartSlots();
        if (slots.Count == 0) continue;

        // 创建服装组菜单层级
        var outfitPathSegments = Utils.GetRelativePathSegments(costumesRoot, outfit.OutfitObject);
        string outfitRelPath = string.Join("/", outfitPathSegments);
        var curMenu = Utils.EnsureSubmenuPathSegments(mixerSubmenu, outfitPathSegments,
          menuPresentation, Utils.MixerPathSemanticKeyPrefix);
        MenuIconGenerator.AddRequest(config, menuIconRequests, curMenu,
          new[] { outfit.BaseObject },
          "Mixer_Group_" + outfitRelPath, useSharedOutfitFraming: true);

        // 菜单按“服装组 → 版本 → 部件/分组候选”组织。每个部件/分组
        // 自己拥有一个参数，0 表示关闭，1..N 表示对应版本的候选。
        foreach (var variant in outfit.GetAllObjects())
        {
          var variantMenu = Utils.FindOrCreateChild(curMenu, variant.name);
          Utils.EnsureSubmenuOnNode(variantMenu, presentation: menuPresentation,
            semanticKey: Utils.GetMixerPathSemanticKeySegments(
              outfitPathSegments.Concat(new[] { variant.name })));
          var variantMarker = variant.GetComponent<ACCVariantMaterialOverride>();
          MenuIconGenerator.AddRequest(config, menuIconRequests, variantMenu,
            variantMarker != null && variantMarker.OutfitBase != null
              ? new[] { variantMarker.OutfitBase }
              : new[] { variant },
            "Mixer_Variant_" + outfitRelPath + "_" + variant.name,
            variantMarker, useSharedOutfitFraming: true);

          foreach (var slot in slots)
          {
            int candidateIndex = slot.Candidates.FindIndex(item =>
              item.VariantObject == variant);
            if (candidateIndex < 0) continue;

            string slotParamName = BuildMixerSlotParamName(config, outfit, slot);
            var slotLayout = new ChoiceParameterLayout(slot.Candidates.Count + 1,
              config.EnableParameterCompression);
            int slotDefaultValue = Generator.GetMixerSlotDefaultValue(defaultOutfit,
              outfitIndexMap, outfit, slot);
            Generator.AddChoiceParameters(rootParams, slotParamName, slotLayout,
              slotDefaultValue);

            var candidateNode = Utils.FindOrCreateUniqueChild(variantMenu, slot.Name);
            var candidateMi = Utils.CreateMenuItem(candidateNode);
            Undo.RecordObject(candidateMi, "Configure mixer part candidate");
            Generator.ConfigurePersistentChoiceMenuItem(candidateMi, slotParamName,
              slotLayout, candidateIndex + 1);
            candidateMi.isDefault = candidateIndex + 1 == slotDefaultValue;
            Utils.ApplyMenuPresentation(menuPresentation, candidateNode, candidateMi, "");
            ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { candidateMi });

            var iconTargets = slot.Candidates[candidateIndex].Control?.Parts
              ?.Where(part => part != null)
              .Distinct()
              .ToList()
              ?? new List<GameObject>();
            string partStableKey = "Mixer_Part_" + outfitRelPath + "_" +
              variant.name + "_" + slot.Key;
            MenuIconGenerator.AddRequest(config, menuIconRequests, candidateNode, iconTargets,
              partStableKey, variantMarker);
          }
        }
      }

      // 最后创建“启用混搭”，避免服装组恰好同名时覆盖其菜单；再将它置顶。
      var enableNode = Utils.FindOrCreateUniqueChild(mixerSubmenu,
        Localization.DefaultMixerEnableObjectName(config, mixerObjectName));
      enableNode.transform.SetAsFirstSibling();
      var enableMi = Utils.CreateMenuItem(enableNode);
      Undo.RecordObject(enableMi, "Configure mixer toggle");
      Generator.ConfigurePersistentChoiceMenuItem(enableMi, mainParameterName,
        mainLayout, customMixerValue);
      Utils.ApplyMenuPresentation(menuPresentation, enableNode, enableMi, "");
      ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { enableMi });
      MenuIconGenerator.AddRequest(config, menuIconRequests, enableNode, allOutfitTargets,
        "Mixer_AllOutfits", useSharedOutfitFraming: true);
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
