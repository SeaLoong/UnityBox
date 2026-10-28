using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Shared Unity hierarchy inspection used by both standalone and avatar generation.</summary>
  public static class CostumeHierarchy
  {
    /// <summary>
    /// 检查 Transform 上是否有网格组件
    /// </summary>
    public static bool HasMeshOn(Transform t)
    {
      if (t == null) return false;

      var smr = t.GetComponent<SkinnedMeshRenderer>();
      if (smr != null) return true;

      var mr = t.GetComponent<MeshRenderer>();
      var mf = t.GetComponent<MeshFilter>();
      return mr != null && mf != null;
    }

    /// <summary>检查节点或其后代是否包含可渲染网格。</summary>
    public static bool HasMeshInHierarchy(Transform root)
    {
      if (root == null) return false;
      return root.GetComponentsInChildren<Renderer>(true)
        .Any(renderer => renderer is SkinnedMeshRenderer ||
          (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null));
    }

    /// <summary>
    /// 检查节点是否拥有服装骨架：至少一个 SkinnedMeshRenderer 的 rootBone
    /// 与其 Renderer 的最低公共祖先必须正好是当前节点。这样既支持骨架分支
    /// 内包含 Mesh，也不会把包裹多个服装的上层容器误识别为服装。
    /// </summary>
    public static bool OwnsSkeleton(Transform root)
    {
      return TryGetOwnedArmature(root, out _);
    }

    /// <summary>
    /// 获取一个服装根所拥有的 Armature。已有 MA Merge Armature 时优先使用它；
    /// 否则兼容旧版通过 SkinnedMeshRenderer.rootBone 推导的骨架分支。
    /// </summary>
    public static bool TryGetOwnedArmature(Transform root, out Transform armature)
    {
      armature = null;
      if (root == null) return false;

      if (CostumeScanIntegration.Adapter != null &&
          CostumeScanIntegration.Adapter.TryGetArmature(root, out armature))
        return true;

      foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (renderer.rootBone == null) continue;

        var armatureCandidate = renderer.rootBone.parent ?? renderer.rootBone;
        bool candidateIsDirectArmature = armatureCandidate.parent == root;
        if (!candidateIsDirectArmature &&
            !IsLowestCommonAncestor(renderer.transform, armatureCandidate, root))
          continue;

        // Setup Outfit 采用 outfitHips.parent 作为 Armature；当 Hips 直接挂在
        // 服装根下时，退回使用 Hips 自身作为骨架分支，兼容旧版非标准层级。
        if (armatureCandidate == root)
        {
          armature = renderer.rootBone;
        }
        else
        {
          var branch = armatureCandidate;
          while (branch.parent != null && branch.parent != root)
            branch = branch.parent;
          armature = branch;
        }
        return true;
      }
      return false;
    }

    private static bool IsLowestCommonAncestor(
      Transform first, Transform second, Transform expected)
    {
      if (first == null || second == null || expected == null) return false;

      var ancestors = new HashSet<Transform>();
      for (var current = first; current != null; current = current.parent)
        ancestors.Add(current);

      for (var current = second; current != null; current = current.parent)
      {
        if (!ancestors.Contains(current)) continue;
        return current == expected;
      }
      return false;
    }

    /// <summary>检查节点是否位于任一 SkinnedMeshRenderer 的骨架分支中。</summary>
    public static bool IsSkeletonNode(Transform node, Transform outfitRoot)
    {
      if (node == null || outfitRoot == null) return false;

      foreach (var renderer in outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (renderer.rootBone != null && node.IsChildOf(renderer.rootBone))
          return true;
      }
      return false;
    }

    /// <summary>获取相对路径中的所有层级名称，每个名称保持原样。</summary>
    public static List<string> GetRelativePathSegments(GameObject root, GameObject node)
    {
      var parts = new List<string>();
      var t = node.transform;
      while (t != null && t != root.transform)
      {
        parts.Add(t.name);
        t = t.parent;
      }
      parts.Reverse();
      return parts;
    }

    /// <summary>
    /// 获取从 root 到 node 的相对路径
    /// </summary>
    public static string GetRelativePath(GameObject root, GameObject node)
    {
      return string.Join("/", GetRelativePathSegments(root, node));
    }

    /// <summary>
    /// 获取层级路径（用于排序）
    /// </summary>
    public static string GetHierarchyPath(GameObject root, GameObject node)
    {
      var indices = new List<int>();
      var t = node.transform;
      while (t != null && t != root.transform)
      {
        indices.Add(t.GetSiblingIndex());
        t = t.parent;
      }
      indices.Reverse();
      return string.Join("/", indices.Select(i => i.ToString("D4")));
    }

  }
}
