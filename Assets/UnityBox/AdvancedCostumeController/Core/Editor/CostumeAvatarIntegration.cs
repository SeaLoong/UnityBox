using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Only avatar identification and network parameter drivers require an SDK.</summary>
  public interface ICostumeAvatarIntegration
  {
    GameObject FindAvatarRoot(GameObject costumesRoot);
    void AddParameterSetDriver(AnimatorState state, bool localOnly, IReadOnlyDictionary<string, float> values);
  }

  public static class CostumeAvatarIntegration
  {
    public static ICostumeAvatarIntegration Adapter { get; set; }
  }
}
