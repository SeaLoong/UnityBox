using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.AdvancedCostumeController
{
  [InitializeOnLoad]
  public sealed class VRChatCostumeIntegration : ICostumeAvatarIntegration
  {
    static VRChatCostumeIntegration()
    {
      CostumeAvatarIntegration.Adapter = new VRChatCostumeIntegration();
    }

    public GameObject FindAvatarRoot(GameObject costumesRoot)
    {
      var descriptor = costumesRoot != null ? costumesRoot.GetComponentInParent<VRCAvatarDescriptor>() : null;
      return descriptor != null ? descriptor.gameObject : null;
    }

    public void AddParameterSetDriver(AnimatorState state, bool localOnly, IReadOnlyDictionary<string, float> values)
    {
      var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
      driver.localOnly = localOnly;
      driver.parameters = values.Select(value => new VRC_AvatarParameterDriver.Parameter
      {
        name = value.Key,
        value = value.Value,
        type = VRC_AvatarParameterDriver.ChangeType.Set
      }).ToList();
    }
  }
}
