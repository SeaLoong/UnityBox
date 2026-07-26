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

        CollectParts(outfitBase, out var parts, out var partControls);

        outfitDataList.Add(new OutfitData
        {
          BaseObject = outfitBase.gameObject,
          OutfitObject = outfitObject,
          Name = outfitObject.name,
          RelativePath = Utils.GetRelativePath(costumesRoot, outfitObject),
          Parts = parts,
          PartControls = partControls,
          Variants = variants
        });
      }

      return outfitDataList;
    }

    /// <summary>
    /// 收集自动部件与显式分组标记。标记对象可以位于服装层级的任意位置；
    /// 同名标记会组合为一个控制项，并覆盖包含它们的自动顶层部件，避免重叠动画。
    /// </summary>
    private static void CollectParts(
      Transform outfitBase,
      out List<GameObject> parts,
      out List<PartControlData> partControls)
    {
      parts = new List<GameObject>();
      partControls = new List<PartControlData>();
      var markedParts = new List<ACCPartGroupMarker>();
      var controlsByName = new Dictionary<string, PartControlData>();

      foreach (var marker in outfitBase.GetComponentsInChildren<ACCPartGroupMarker>(true))
      {
        if (marker.transform == outfitBase || IsUnderNestedOutfit(marker.transform, outfitBase))
          continue;

        markedParts.Add(marker);
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
