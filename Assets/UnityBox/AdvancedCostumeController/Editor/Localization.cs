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
    public static void DrawInspectorHeader(string title)
    {
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField(EditorGUIUtility.IconContent("console.infoicon"), GUILayout.Width(20));
      EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
      EditorGUILayout.EndHorizontal();
      EditorGUILayout.Space(3);
    }

    /// <summary>ACC 窗口设置语言时同步到此静态字段，供组件 Inspector 读取。</summary>
    public static ACCLanguage CurrentLanguage { get; set; } = ACCLanguage.Auto;

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
      return UseChinese(CurrentLanguage) ? chinese : english;
    }

    /// <summary>ACC 默认 Parts 子菜单的对象名。空 Label 时 MA 会直接显示该名称。</summary>
    public static string DefaultPartsMenuObjectName(ACCConfig config)
    {
      return Text(config, "部件", "Parts");
    }

    /// <summary>未填写 Custom Mixer Name 时默认 Mixer 节点的对象名。</summary>
    public static string DefaultMixerMenuObjectName(ACCConfig config)
    {
      return Text(config, "混搭", "Custom Mix");
    }

    /// <summary>默认 Mixer 启用控制的对象名。</summary>
    public static string DefaultMixerEnableObjectName(ACCConfig config)
    {
      return Text(config, "启用", "Enable");
    }

    public static string PartSourceLegend()
    {
      return Text(
        "[A] 自动 · [MG] 持久分组 · [SG] 临时分组 · [X] 已排除",
        "[A] Auto · [MG] Persistent Group · [SG] Session Group · [X] Excluded");
    }
  }
}