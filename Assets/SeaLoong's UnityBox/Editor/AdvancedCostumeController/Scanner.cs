using System.Collections.Generic;
using UnityEngine;

namespace SeaLoongUnityBox.ACC
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
    /// <param name="ignoreSet">忽略名称集合</param>
    /// <returns>发现的服装列表</returns>
    public static List<OutfitData> FindOutfits(GameObject costumesRoot, HashSet<string> ignoreSet)
    {
      var outfitDataList = new List<OutfitData>();
      var processedBases = new HashSet<GameObject>();
      var stack = new Stack<Transform>();
      stack.Push(costumesRoot.transform);

      while (stack.Count > 0)
      {
        var t = stack.Pop();

        // 不是 mesh 节点则继续往下遍历
        if (!Utils.HasMeshOn(t))
        {
          for (int i = t.childCount - 1; i >= 0; i--)
          {
            var child = t.GetChild(i);
            if (!Utils.IsNameIgnored(child.name, ignoreSet))
              stack.Push(child);
          }
          continue;
        }

        // 找到 mesh 节点，其父节点视为 outfit base
        var outfitBase = t.parent;
        if (outfitBase == null) continue;
        if (processedBases.Contains(outfitBase.gameObject)) continue;
        processedBases.Add(outfitBase.gameObject);

        // 查找变体（同级的其他节点，必须也包含 Mesh 子节点才视为变体）
        var variants = new List<GameObject>();
        var outfitParent = outfitBase.parent;
        if (outfitParent != null && outfitParent.gameObject != costumesRoot)
        {
          for (int i = 0; i < outfitParent.childCount; i++)
          {
            var sibling = outfitParent.GetChild(i);
            if (sibling != outfitBase && !Utils.IsNameIgnored(sibling.name, ignoreSet) && HasMeshChild(sibling))
              variants.Add(sibling.gameObject);
          }
        }

        var outfitObject = variants.Count > 0 ? outfitParent.gameObject : outfitBase.gameObject;

        // 收集部件（BaseObject 下的子节点）
        var parts = new List<GameObject>();
        for (int i = 0; i < outfitBase.childCount; i++)
        {
          var child = outfitBase.GetChild(i);
          if (!Utils.IsNameIgnored(child.name, ignoreSet))
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
    /// 检查 Transform 下是否有任何子节点包含 Mesh 组件
    /// </summary>
    private static bool HasMeshChild(Transform t)
    {
      for (int i = 0; i < t.childCount; i++)
      {
        if (Utils.HasMeshOn(t.GetChild(i))) return true;
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
