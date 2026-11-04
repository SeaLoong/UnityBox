#if !UNITYBOX_VRC_AVATARS || !MODULAR_AVATAR_AVAILABLE
using UnityEditor;

namespace UnityBox.AdvancedCostumeController
{
  internal static class IntegrationAvailability
  {
    [MenuItem("Tools/UnityBox/Advanced Costume Controller")]
    private static void OpenUnityAssetWorkflow()
    {
      UnityCostumeWindow.ShowWindow();
    }
  }
}
#endif
