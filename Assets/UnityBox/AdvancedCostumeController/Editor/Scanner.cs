using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 服装扫描器 — 从 CostumesRoot 层级结构中自动发现服装、变体和部件
  /// </summary>
  public static class Scanner
  {
    /// <summary>
    /// 扫描 CostumesRoot 下的所有服装
    /// </summary>
    /// <param name="costumesRoot">服装根节点</param>
    /// <returns>发现的服装列表</returns>
    public static List<OutfitData> FindOutfits(GameObject costumesRoot)
    {
      var outfitDataList = new List<OutfitData>();
      var processedOutfitObjects = new HashSet<GameObject>();
      var stack = new Stack<Transform>();
      for (int i = costumesRoot.transform.childCount - 1; i >= 0; i--)
        stack.Push(costumesRoot.transform.GetChild(i));

      while (stack.Count > 0)
      {
        var t = stack.Pop();

        if (processedOutfitObjects.Contains(t.gameObject)) continue;

        // MA Merge Armature 挂在骨架对象上，而不是服装对象上。没有 ACC/MA
        // Outfit Root 时，如果当前遍历节点本身就是 Merge Armature 所在的骨架，
        // 应先提升到其父节点（服装对象），再判断网格、部件和同级变体。
        if (t.GetComponent<ModularAvatarMergeArmature>() != null &&
            t.parent != null && t.parent != costumesRoot.transform &&
            t.GetComponent<ACCOutfitMarker>() == null &&
            t.GetComponent<ModularAvatarOutfitRoot>() == null)
        {
          t = t.parent;
          if (processedOutfitObjects.Contains(t.gameObject)) continue;
        }

        var outfitMarker = t.GetComponent<ACCOutfitMarker>();
        var modularAvatarOutfitRoot = t.GetComponent<ModularAvatarOutfitRoot>();
        var explicitMaterialVariant = t.GetComponent<ACCVariantMaterialOverride>();
        // ACC 自己的显式服装标记优先级最高；其次是 MA Outfit Root，最后才把
        // 材质变体标记视为“不要独立识别”的信号。
        if (outfitMarker == null && modularAvatarOutfitRoot == null &&
            explicitMaterialVariant != null && explicitMaterialVariant.OutfitBase != null &&
            explicitMaterialVariant.OutfitBase != t.gameObject)
          continue;

        // ACCOutfitMarker 是 ACC 自己的最高优先级显式服装声明；MA Outfit Root
        // 是其次的显式声明。两者都不要求骨架或网格，命中后不进入其后代，
        // 从而避免嵌套对象被重复识别。其余对象再按 MA Merge Armature 和旧版
        // 骨架/网格规则自动识别。
        bool isExplicitOutfit = outfitMarker != null || modularAvatarOutfitRoot != null;
        bool hasOwnedArmature = Utils.TryGetOwnedArmature(t, out var armatureRoot);
        bool isAutoDetectedOutfit = hasOwnedArmature &&
          armatureRoot.parent == t && Utils.HasMeshInHierarchy(t);
        if (!isExplicitOutfit && !isAutoDetectedOutfit)
        {
          for (int i = t.childCount - 1; i >= 0; i--)
            stack.Push(t.GetChild(i));
          continue;
        }

        var outfitBase = t;

        // 查找变体（同级的其他含网格节点）。允许变体复用本体骨架，
        // 因而不强制每个变体都各自拥有骨架。即使本体直接位于 CostumesRoot
        // 下也要扫描同级变体；但这种情况下不能把 CostumesRoot 作为 OutfitObject，
        // 最终使用 Outfit Base 自身作为该变体组的菜单根。
        var variants = new List<GameObject>();
        var outfitParent = outfitBase.parent;
        if (outfitParent != null)
        {
          for (int i = 0; i < outfitParent.childCount; i++)
          {
            var sibling = outfitParent.GetChild(i);
            if (sibling == outfitBase) continue;

            var materialVariant = sibling.GetComponent<ACCVariantMaterialOverride>();
            bool isMaterialVariant = materialVariant != null &&
              materialVariant.OutfitBase == outfitBase.gameObject;

            // 已由其他服装的 ACCVariantMaterialOverride 指向 → 不属于当前服装
            if (materialVariant != null && materialVariant.OutfitBase != null &&
                materialVariant.OutfitBase != outfitBase.gameObject)
              continue;

            // 显式材质变体优先于“完整服装兄弟”排除规则，支持直接转换完整预制件。
            if (!isMaterialVariant &&
                Utils.TryGetOwnedArmature(sibling, out _) && Utils.HasMeshInHierarchy(sibling))
              continue;
            // 显式服装根优先级高于材质变体标记，不能把另一个 ACC/MA 服装吞成变体。
            if (sibling.GetComponent<ACCOutfitMarker>() != null)
              continue;
            if (sibling.GetComponent<ModularAvatarOutfitRoot>() != null)
              continue;
            // 有网格且未被其他服装认领 → 加入当前变体；如果已由其他 Outfit 处理过变体加入逻辑，
            // 则 processedOutfitObjects 会跳过此对象，此处仅添加仍未处理的网格兄弟。
            if (!processedOutfitObjects.Contains(sibling.gameObject) &&
                (Utils.HasMeshInHierarchy(sibling) || isMaterialVariant))
              variants.Add(sibling.gameObject);
          }
        }

        var outfitObject = variants.Count > 0 && outfitParent != costumesRoot.transform
          ? outfitParent.gameObject
          : outfitBase.gameObject;
        processedOutfitObjects.Add(outfitBase.gameObject);
        foreach (var variant in variants)
          processedOutfitObjects.Add(variant);

        CollectParts(outfitBase, out var parts, out var excludedParts, out var partControls);
        var variantPartData = new List<VariantPartData>();
        AddVariantPartData(variantPartData, outfitBase.gameObject, parts, excludedParts, partControls);
        foreach (var variant in variants)
        {
          var materialVariant = variant.GetComponent<ACCVariantMaterialOverride>();
          if (materialVariant != null)
          {
            // 材质变体只提供材质曲线，不作为 Mixer 的网格部件来源。
            AddVariantPartData(variantPartData, variant,
              new List<GameObject>(), new List<GameObject>(), new List<PartControlData>());
            continue;
          }
          CollectParts(variant.transform, out var variantParts,
            out var variantExcludedParts, out var variantPartControls);
          AddVariantPartData(variantPartData, variant.gameObject, variantParts,
            variantExcludedParts, variantPartControls);
        }

        outfitDataList.Add(new OutfitData
        {
          BaseObject = outfitBase.gameObject,
          ArmatureObject = armatureRoot != null ? armatureRoot.gameObject : null,
          OutfitObject = outfitObject,
          Name = outfitObject.name,
          RelativePath = Utils.GetRelativePath(costumesRoot, outfitObject),
          Parts = parts,
          ExcludedParts = excludedParts,
          PartControls = partControls,
          VariantPartData = variantPartData,
          Marker = outfitMarker,
          Variants = variants
        });
      }

      return outfitDataList;
    }

    private static void AddVariantPartData(
      List<VariantPartData> result,
      GameObject variantObject,
      List<GameObject> parts,
      List<GameObject> excludedParts,
      List<PartControlData> partControls)
    {
      result.Add(new VariantPartData
      {
        VariantObject = variantObject,
        Parts = parts,
        ExcludedParts = excludedParts,
        PartControls = partControls
      });
    }

    /// <summary>
    /// 收集自动部件与显式分组标记。标记对象可以位于服装层级的任意位置；
    /// 同名标记会组合为一个控制项，并覆盖包含它们的自动顶层部件，避免重叠动画。
    /// </summary>
    public static void CollectParts(
      Transform outfitBase,
      out List<GameObject> parts,
      out List<GameObject> excludedParts,
      out List<PartControlData> partControls)
    {
      parts = new List<GameObject>();
      excludedParts = new List<GameObject>();
      partControls = new List<PartControlData>();
      var markedParts = new List<ACCPartGroupMarker>();
      var controlsByName = new Dictionary<string, PartControlData>();

      foreach (var marker in outfitBase.GetComponentsInChildren<ACCPartGroupMarker>(true))
      {
        if (marker.transform == outfitBase || IsUnderNestedOutfit(marker.transform, outfitBase))
          continue;

        markedParts.Add(marker);
        if (marker.Mode == ACCPartControlMode.Exclude)
        {
          excludedParts.Add(marker.gameObject);
          continue;
        }

        if (!parts.Contains(marker.gameObject))
          parts.Add(marker.gameObject);

        string groupName = string.IsNullOrWhiteSpace(marker.GroupName)
          ? marker.gameObject.name
          : marker.GroupName.Trim();
        if (!controlsByName.TryGetValue(groupName, out var control))
        {
          control = new PartControlData { Name = groupName, IsGroup = true };
          controlsByName.Add(groupName, control);
          partControls.Add(control);
        }
        control.Parts.Add(marker.gameObject);
      }

      // 未被标记覆盖的顶层 Mesh 容器仍按原逻辑作为独立部件。
      for (int i = 0; i < outfitBase.childCount; i++)
      {
        var child = outfitBase.GetChild(i);
        if (Utils.IsSkeletonNode(child, outfitBase) ||
            !Utils.HasMeshInHierarchy(child) ||
            ContainsMarkedPart(child, markedParts))
          continue;

        parts.Add(child.gameObject);
        partControls.Add(new PartControlData
        {
          Name = child.name,
          Parts = new List<GameObject> { child.gameObject },
          IsGroup = false
        });
      }
    }

    private static bool ContainsMarkedPart(Transform root, List<ACCPartGroupMarker> markers)
    {
      foreach (var marker in markers)
      {
        if (marker.transform.IsChildOf(root)) return true;
      }
      return false;
    }

    private static bool IsUnderNestedOutfit(Transform node, Transform outfitBase)
    {
      for (var current = node.parent; current != null && current != outfitBase; current = current.parent)
      {
        if (current.GetComponent<ACCOutfitMarker>() != null)
          return true;
      }
      return false;
    }

    /// <summary>
    /// 选择默认服装组，并保留用户显式指定的本体/变体对象。
    /// 未指定或未命中时按 Costumes Root 的层级顺序选择第一个启用对象。
    /// </summary>
    public static OutfitData FindDefaultOutfit(
      List<OutfitData> outfits,
      GameObject overrideObject,
      GameObject costumesRoot = null)
    {
      if (outfits == null || outfits.Count == 0) return null;

      foreach (var outfit in outfits)
      {
        if (outfit != null) outfit.DefaultChoiceObject = null;
      }

      OutfitData result = null;

      // 优先使用用户指定的默认服装或变体。精确命中变体后，后续主选择和
      // Mixer 默认槽位都会使用该变体，而不是再次强制回退到 Outfit Base。
      if (overrideObject != null)
      {
        foreach (var outfit in outfits)
        {
          var choiceObject = FindOverrideChoiceObject(outfit, overrideObject);
          if (choiceObject == null) continue;

          outfit.DefaultChoiceObject = choiceObject;
          result = outfit;
          break;
        }
      }

      if (result != null) return result;

      // 未指定时严格使用预览中第一个启用的 GameObject 所属服装，而不是按名称关键词猜测。
      // 用层级路径排序，确保 Base/Variant 的实际显示顺序与默认值一致。
      var enabledChoices = outfits
        .Where(outfit => outfit != null)
        .SelectMany(outfit => outfit.GetAllObjects()
          .Select(choice => new { Outfit = outfit, Choice = choice }))
        .Where(item => item.Choice != null);
      if (costumesRoot != null)
      {
        enabledChoices = enabledChoices.OrderBy(item =>
          Utils.GetHierarchyPath(costumesRoot, item.Choice), StringComparer.Ordinal);
      }

      foreach (var item in enabledChoices)
      {
        item.Outfit.DefaultChoiceObject = item.Choice;
        return item.Outfit;
      }

      return null;
    }

    public static GameObject FindOverrideChoiceObject(
      OutfitData outfit, GameObject overrideObject)
    {
      if (outfit == null || overrideObject == null) return null;

      var choices = new List<GameObject>();
      choices.AddRange(outfit.GetAllObjects().Where(choice => choice != null));

      // 精确命中优先，允许直接把某个变体拖入 Default Outfit 字段。
      var exact = choices.FirstOrDefault(choice => choice == overrideObject);
      if (exact != null) return exact;

      // 兼容拖入共同父节点、服装容器或某个本体/变体子节点的用法。
      return choices.FirstOrDefault(choice =>
        choice.transform.IsChildOf(overrideObject.transform) ||
        overrideObject.transform.IsChildOf(choice.transform));
    }
  }
}
