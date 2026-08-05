using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 混搭器 — 负责混搭菜单构建和参数注册。
  /// 混搭是主服装参数中的一个特殊服装值；可复用普通部件参数，
  /// 也可按配置使用独立槽位选择本体或变体候选。
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
      if (config.UseIndependentMixerPartParameters)
      {
        // 已处理的服装组
        var processedOutfitObjects = new HashSet<GameObject>();

        foreach (var outfit in outfits)
        {
          if (!processedOutfitObjects.Add(outfit.OutfitObject)) continue;

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
            var variantMenu = Utils.FindOrCreateUniqueChild(curMenu, variant.name);
            Utils.EnsureSubmenuOnNode(variantMenu, presentation: menuPresentation,
              semanticKey: Utils.GetMixerPathSemanticKeySegments(
                outfitPathSegments.Concat(new[] { variantMenu.name })));
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
                variantMenu.name + "_" + slot.Key;
              MenuIconGenerator.AddRequest(config, menuIconRequests, candidateNode, iconTargets,
                partStableKey, variantMarker);
            }
          }
        }
      }
      else
      {
        BuildSharedPartParameterMenu(config, menuRoot, mixerSubmenu, outfits,
          outfitIndexMap, rootParams, defaultOutfit, menuPresentation,
          menuIconRequests);
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

    private static void BuildSharedPartParameterMenu(
      ACCConfig config,
      GameObject menuRoot,
      GameObject mixerSubmenu,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      ModularAvatarParameters rootParams,
      OutfitData defaultOutfit,
      Utils.MenuPresentationSnapshot menuPresentation,
      List<MenuIconRequest> menuIconRequests)
    {
      var costumesRoot = config.CostumesRoot;
      var processedOutfitObjects = new HashSet<GameObject>();
      foreach (var outfit in outfits)
      {
        if (!processedOutfitObjects.Add(outfit.OutfitObject)) continue;

        var controls = outfit.GetPartControls();
        var choices = outfit.GetAllObjects();
        if (controls.Count == 0 && choices.Count <= 1) continue;

        var outfitPathSegments = Utils.GetRelativePathSegments(costumesRoot,
          outfit.OutfitObject);
        string outfitRelPath = string.Join("/", outfitPathSegments);
        var curMenu = Utils.EnsureSubmenuPathSegments(mixerSubmenu, outfitPathSegments,
          menuPresentation, Utils.MixerPathSemanticKeyPrefix);
        MenuIconGenerator.AddRequest(config, menuIconRequests, curMenu,
          new[] { outfit.BaseObject },
          "Mixer_Group_" + outfitRelPath, useSharedOutfitFraming: true);

        if (choices.Count > 1)
        {
          string variantParameter = BuildMixerVariantParamName(config, outfit);
          int defaultVariant = Generator.GetMixerVariantDefaultValue(defaultOutfit,
            outfitIndexMap, outfit);
          var variantLayout = new ChoiceParameterLayout(choices.Count,
            config.EnableParameterCompression);
          Generator.AddChoiceParameters(rootParams, variantParameter,
            variantLayout, defaultVariant);

          for (int index = 0; index < choices.Count; index++)
          {
            var choice = choices[index];
            // 变体是服装组级别的选择，与 Shared Parts 菜单同级；不要把它
            // 放进 Shared Parts，否则会把“选择服装版本”和“开关部件”混成一层。
            var variantNode = Utils.FindOrCreateUniqueChild(curMenu, choice.name);
            var variantItem = Utils.CreateMenuItem(variantNode);
            Undo.RecordObject(variantItem, "Configure shared mixer variant");
            Generator.ConfigurePersistentChoiceMenuItem(variantItem,
              variantParameter, variantLayout, index);
            variantItem.isDefault = index == defaultVariant;
            Utils.ApplyMenuPresentation(menuPresentation, variantNode, variantItem, "",
              semanticKey: Utils.GetMixerPathSemanticKeySegments(
                outfitPathSegments.Concat(new[] { variantNode.name })));
            ACCEditorUndo.RecordPrefabInstanceModifications(
              new UnityEngine.Object[] { variantItem });

            var marker = choice.GetComponent<ACCVariantMaterialOverride>();
            var iconTargets = marker != null && marker.OutfitBase != null
              ? new[] { marker.OutfitBase }
              : new[] { choice };
            MenuIconGenerator.AddRequest(config, menuIconRequests, variantNode,
              iconTargets, "Mixer_Shared_Variant_" + outfitRelPath + "_" + variantNode.name,
              marker, useSharedOutfitFraming: true);
          }
        }

        if (controls.Count == 0) continue;

        var sharedPartsMenu = Utils.FindOrCreateUniqueChild(curMenu,
          Localization.DefaultMixerSharedPartsMenuObjectName(config));
        Utils.EnsureSubmenuOnNode(sharedPartsMenu, presentation: menuPresentation,
          semanticKey: Utils.GetPartsMenuSemanticKey(menuRoot, curMenu));
        Utils.EnsureDefaultMenuIcon(sharedPartsMenu, "OutlineBlank2");

        foreach (var control in controls)
        {
          if (control?.Parts == null || control.Parts.Count == 0) continue;

          // 名称冲突时追加序号，避免同名部件覆盖同一服装组的其他部件控制。
          var partNode = Utils.FindOrCreateUniqueChild(sharedPartsMenu, control.Name);
          var partItem = Utils.CreateMenuItem(partNode);
          Undo.RecordObject(partItem, "Configure shared mixer part");
          Utils.ConfigureAsToggle(partItem);
          partItem.PortableControl.Parameter = AnimationBuilder.BuildPartParamName(
            config, outfit, control);
          partItem.automaticValue = true;
          partItem.isDefault = control.Parts.All(part => part != null && part.activeSelf);
          partItem.isSaved = true;
          partItem.isSynced = true;
          Utils.ApplyMenuPresentation(menuPresentation, partNode, partItem, "");
          ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { partItem });

          var iconTargets = control.Parts
            .Where(part => part != null)
            .Distinct()
            .ToList();
          string partStableKey = "Mixer_Shared_Part_" + outfitRelPath + "_" +
            control.Name;
          MenuIconGenerator.AddRequest(config, menuIconRequests, partNode,
            iconTargets, partStableKey);
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

    /// <summary>共享普通部件参数模式下一个服装组的整体变体 Int 参数名。</summary>
    public static string BuildMixerVariantParamName(
      ACCConfig config, OutfitData outfit)
    {
      string groupPath = Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      string raw = ACCConfig.MixerParamPrefix + "/" + groupPath + "/Variant";
      return Utils.BuildParamName(config.MainParameterName, raw);
    }

  }
}
