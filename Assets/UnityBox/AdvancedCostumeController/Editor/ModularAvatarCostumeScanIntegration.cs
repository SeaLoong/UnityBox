using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [InitializeOnLoad]
  public sealed class ModularAvatarCostumeScanIntegration : ICostumeScanIntegration
  {
    static ModularAvatarCostumeScanIntegration()
    {
      CostumeScanIntegration.Adapter = new ModularAvatarCostumeScanIntegration();
    }

    public bool HasOutfitRoot(Transform target)
      => target != null && target.GetComponent<ModularAvatarOutfitRoot>() != null;

    public bool HasMergeArmature(Transform target)
      => target != null && target.GetComponent<ModularAvatarMergeArmature>() != null;

    public bool TryGetArmature(Transform root, out Transform armature)
    {
      armature = null;
      if (root == null) return false;
      var outfitRoot = root.GetComponent<ModularAvatarOutfitRoot>();
      if (outfitRoot != null && outfitRoot.armatureRoot != null)
      {
        armature = outfitRoot.armatureRoot;
        return true;
      }
      if (!TryGetOwnedMergeArmature(root, out var mergeArmature)) return false;
      armature = mergeArmature.transform;
      return true;
    }

    /// <summary>
    /// 查找属于当前服装根的 MA Merge Armature。组件必须位于当前根的直接骨架分支中；
    /// MA Outfit Root/ACC Outfit Marker 下的嵌套服装会被隔离，避免外层组织节点吞掉子服装。
    /// </summary>
    public static bool TryGetOwnedMergeArmature(
      Transform root,
      out ModularAvatarMergeArmature mergeArmature)
    {
      mergeArmature = null;
      if (root == null) return false;

      foreach (var candidate in root.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
      {
        if (candidate == null) continue;
        if (candidate.transform == root)
        {
          mergeArmature = candidate;
          return true;
        }

        var branch = candidate.transform;
        while (branch.parent != null && branch.parent != root)
          branch = branch.parent;
        if (branch.parent != root) continue;

        var nestedOutfitRoot = candidate.GetComponentInParent<ModularAvatarOutfitRoot>();
        if (nestedOutfitRoot != null && nestedOutfitRoot.transform != root) continue;
        var nestedAccMarker = candidate.GetComponentInParent<ACCOutfitMarker>();
        if (nestedAccMarker != null && nestedAccMarker.transform != root) continue;

        mergeArmature = candidate;
        return true;
      }
      return false;
    }

  }
}
