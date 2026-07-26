using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>ACC 编辑器界面的显示语言。</summary>
  public enum ACCLanguage
  {
    Auto,
    English,
    Chinese
  }

  /// <summary>轻量的中英文编辑器本地化工具。</summary>
  public static class Localization
  {
    public const string DocumentationBaseUrl =
      "https://github.com/SeaLoong/UnityBox/blob/master/Docs/AdvancedCostumeController.md";

    public static bool UseChinese(ACCLanguage language)
    {
      if (language == ACCLanguage.Chinese) return true;
      if (language == ACCLanguage.English) return false;

      return Application.systemLanguage == SystemLanguage.ChineseSimplified ||
        Application.systemLanguage == SystemLanguage.ChineseTraditional;
    }

    public static string Text(ACCConfig config, string chinese, string english)
    {
      return UseChinese(config.Language) ? chinese : english;
    }

    public static string Text(string chinese, string english)
    {
      return UseChinese(ACCLanguage.Auto) ? chinese : english;
    }

    public static void DrawDocumentationButton(string anchor)
    {
      if (GUILayout.Button(new GUIContent("?", Text("打开使用手册", "Open documentation")),
          EditorStyles.miniButton, GUILayout.Width(24)))
        Application.OpenURL(DocumentationBaseUrl + "#" + anchor);
    }
  }
}