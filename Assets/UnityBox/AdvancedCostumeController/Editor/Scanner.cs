using System.Collections.Generic;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 服装扫描器 — 从 CostumesRoot 层级结构中自动发现服装、变体和部件
  /// </summary>
  public static class Scanner
  {
    /// <summary>用于自动识别默认服装的关键词</summary>
    public static readonly string[] DefaultOutfitHints =
      { "origin", "original", "default", "base", "vanilla", "standard", "normal" };

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

        // 默认由骨架和网格识别服装；ACCOutfitMarker 是完全显式的服装声明，
        // 不要求骨架或网格。命中后不进入其后代，从而避免嵌套对象被重复识别。
        bool isExplicitOutfit = t.GetComponent<ACCOutfitMarker>() != null;
        bool isAutoDetectedOutfit = Utils.OwnsSkeleton(t) && Utils.HasMeshInHierarchy(t);
        if (!isExplicitOutfit && !isAutoDetectedOutfit)
        {
          for (int i = t.childCount - 1; i >= 0; i--)
            stack.Push(t.GetChild(i));
          continue;
        }

        var outfitBase = t;

        // 查找变体（同级的其他含网格节点）。允许变体复用本体骨架，
        // 因而不强制每个变体都各自拥有骨架。
        var variants = new List<GameObject>();
        var outfitParent = outfitBase.parent;
        // 只有当父节点存在且不是根节点时才查找变体
        if (outfitParent != null && outfitParent.gameObject != costumesRoot)
        {
          for (int i = 0; i < outfitParent.childCount; i++)
          {
            var sibling = outfitParent.GetChild(i);
            var materialVariant = sibling.GetComponent<ACCVariantMaterialOverride>();
            bool isExplicitVariant = sibling.GetComponent<ACCOutfitMarker>() != null;
            bool isMaterialVariant = materialVariant != null &&
              materialVariant.OutfitBase == outfitBase.gameObject;
            if (sibling != outfitBase &&
                (Utils.HasMeshInHierarchy(sibling) || isMaterialVariant || isExplicitVariant))
              variants.Add(sibling.gameObject);
          }
        }

        var outfitObject = variants.Count > 0 ? outfitParent.gameObject : outfitBase.gameObject;
        processedOutfitObjects.Add(outfitBase.gameObject);
        foreach (var variant in variants)
          processedOutfitObjects.Add(variant);

        // 收集服装根下的顶层网格部件。功能节点（菜单、动骨等）没有网格，
        // 骨架分支也不会成为部件。
        var parts = new List<GameObject>();
        for (int i = 0; i < outfitBase.childCount; i++)
        {
          var child = outfitBase.GetChild(i);
            if (!Utils.IsSkeletonNode(child, outfitBase) &&
              Utils.HasMeshInHierarchy(child))
            parts.Add(child.gameObject);
        }

        outfitDataList.Add(new OutfitData
        {
          BaseObject = outfitBase.gameObject,
          OutfitObject = outfitObject,
          Name = outfitObject.name,
          RelativePath = Utils.GetRelativePath(costumesRoot, outfitObject),
          Parts = parts,
          Variants = variants
        });
      }

      return outfitDataList;
    }

    /// <summary>
    /// 自动选择默认服装
    /// </summary>
    public static OutfitData FindDefaultOutfit(List<OutfitData> outfits, GameObject overrideObject)
    {
      OutfitData result = null;

      // 优先使用用户指定的默认服装
      if (overrideObject != null)
      {
        result = outfits.Find(o =>
          o.BaseObject == overrideObject ||
          o.BaseObject.transform.IsChildOf(overrideObject.transform) ||
          overrideObject.transform.IsChildOf(o.BaseObject.transform));
      }

      // 按名称关键词匹配
      if (result == null)
      {
        foreach (var hint in DefaultOutfitHints)
        {
          result = outfits.Find(o =>
            (o.Name + "/" + o.RelativePath).ToLowerInvariant().Contains(hint));
          if (result != null) break;
        }
      }

      // 兜底：使用第一个
      return result ?? (outfits.Count > 0 ? outfits[0] : null);
    }
  }
}
