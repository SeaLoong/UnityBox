using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Optional package-specific outfit declarations and armature ownership.</summary>
  public interface ICostumeScanIntegration
  {
    bool HasOutfitRoot(Transform target);
    bool HasMergeArmature(Transform target);
    bool TryGetArmature(Transform target, out Transform armature);
  }

  public static class CostumeScanIntegration
  {
    public static ICostumeScanIntegration Adapter { get; set; }

    public static bool HasOutfitRoot(Transform target)
      => Adapter != null && Adapter.HasOutfitRoot(target);

    public static bool HasMergeArmature(Transform target)
      => Adapter != null && Adapter.HasMergeArmature(target);
  }
}
