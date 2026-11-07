#if !UNITYBOX_VRC_AVATARS || !MODULAR_AVATAR_AVAILABLE
using UnityEditor;

namespace UnityBox.AdvancedCostumeController
{
  internal static class IntegrationAvailability
  {
    [MenuItem("Tools/UnityBox/Advanced Costume Controller/Open")]
    private static void ShowRequiredIntegrations()
    {
      var config = new ACCConfig { Language = Localization.CurrentLanguage };
      EditorUtility.DisplayDialog("Advanced Costume Controller", Localization.Text(config,
        "ACC 需要 VRChat SDK Base/Avatars 3.7.3 或更高版本，以及 Modular Avatar 1.18.0 或更高版本。请通过 VCC 安装后打开换装面板。",
        "ACC requires VRChat SDK Base/Avatars 3.7.3 or later and Modular Avatar 1.18.0 or later. Install them through VCC to open the costume panel."),
        Localization.Text(config, "确定", "OK"));
    }
  }
}
#endif
