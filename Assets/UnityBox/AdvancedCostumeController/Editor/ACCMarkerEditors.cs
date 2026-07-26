using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [CustomEditor(typeof(ACCOutfitMarker))]
  public class ACCOutfitMarkerEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      var marker = (ACCOutfitMarker)target;
      ACCInspectorUI.DrawHeader(Localization.Text("ACC 服装标记", "ACC Outfit Marker"),
        "acc-outfit-marker");
      EditorGUILayout.HelpBox(Localization.Text(
        "将当前对象明确作为服装根。部件名称格式化会持久保存在此组件，并仅影响自动部件的菜单显示名称。",
        "Explicitly declares this object as an outfit root. Part name formatting is stored here and changes automatic part menu labels only."),
        MessageType.Info);

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(Localization.Text("部件名称格式化", "Part Name Formatting"), EditorStyles.boldLabel);
      DrawTextField(marker, Localization.Text("移除前缀", "Remove Prefix"),
        () => marker.PartNamePrefixToRemove, value => marker.PartNamePrefixToRemove = value);
      DrawTextField(marker, Localization.Text("移除后缀", "Remove Suffix"),
        () => marker.PartNameSuffixToRemove, value => marker.PartNameSuffixToRemove = value);
      DrawTextField(marker, Localization.Text("正则表达式", "Regex Pattern"),
        () => marker.PartNameRegexPattern, value => marker.PartNameRegexPattern = value);
      DrawTextField(marker, Localization.Text("正则替换为", "Regex Replacement"),
        () => marker.PartNameRegexReplacement, value => marker.PartNameRegexReplacement = value);

      if (!Utils.IsValidRegex(marker.PartNameRegexPattern))
        EditorGUILayout.HelpBox(Localization.Text("正则表达式无效，将忽略正则替换。",
          "The regex pattern is invalid and will be ignored."), MessageType.Warning);

      Scanner.CollectParts(marker.transform, out var parts, out var excludedParts, out var controls);
      EditorGUILayout.Space();
      EditorGUILayout.LabelField(Localization.Text(
        $"部件预览（可控制：{parts.Count}，已排除：{excludedParts.Count}）",
        $"Part Preview (controlled: {parts.Count}, excluded: {excludedParts.Count})"),
        EditorStyles.boldLabel);

      if (parts.Count == 0 && excludedParts.Count == 0)
      {
        EditorGUILayout.HelpBox(Localization.Text(
          "未找到可控制或已排除的部件。添加网格、部件标记，或检查嵌套 Outfit Marker。",
          "No controlled or excluded parts found. Add meshes or part markers, or check nested Outfit Markers."),
          MessageType.Info);
      }
      else
      {
        foreach (var part in parts)
          DrawPartPreviewRow(marker, part, controls, false);
        foreach (var part in excludedParts)
          DrawPartPreviewRow(marker, part, controls, true);
      }

      EditorGUILayout.LabelField(Localization.Text(
        "[A] 自动 · [MG] 持久分组 · [X] 已排除",
        "[A] Auto · [MG] Persistent · [X] Excluded"),
        EditorStyles.miniLabel);
    }

    private static void DrawTextField(
      ACCOutfitMarker marker,
      string label,
      System.Func<string> getter,
      System.Action<string> setter)
    {
      var value = EditorGUILayout.TextField(label, getter());
      if (value == getter()) return;
      Undo.RecordObject(marker, "Configure ACC outfit marker");
      setter(value);
      EditorUtility.SetDirty(marker);
    }

    private static void DrawPartPreviewRow(
      ACCOutfitMarker outfitMarker,
      GameObject part,
      System.Collections.Generic.List<PartControlData> controls,
      bool excluded)
    {
      var control = controls.FirstOrDefault(item => item.Parts.Contains(part));
      string label = Utils.FormatPartDisplayName(part.name,
        outfitMarker.PartNamePrefixToRemove, outfitMarker.PartNameSuffixToRemove,
        outfitMarker.PartNameRegexPattern, outfitMarker.PartNameRegexReplacement);
      string source = excluded
        ? Localization.Text("[X] 已排除", "[X] Excluded")
        : control != null && control.IsGroup
          ? $"[MG: {control.Name}]"
          : Localization.Text("[A] 自动", "[A] Auto");

      using (new EditorGUI.DisabledScope(excluded))
      {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, EditorStyles.label, GUILayout.Width(180));
        EditorGUILayout.LabelField(source, EditorStyles.label, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
      }
    }
  }

  [CustomEditor(typeof(ACCPartGroupMarker))]
  public class ACCPartGroupMarkerEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      var marker = (ACCPartGroupMarker)target;
      ACCInspectorUI.DrawHeader(Localization.Text("ACC 部件控制标记", "ACC Part Control Marker"),
        "acc-part-group-marker");
      EditorGUILayout.HelpBox(Localization.Text(
        "Group 会将同名对象合并为一个开关；Exclude 不会生成当前对象或所在自动部件的 ACC 控制。",
        "Group combines same-name objects into one toggle. Exclude prevents ACC controls for this object and its containing automatic part."),
        MessageType.Info);

      EditorGUI.BeginChangeCheck();
      int modeIndex = EditorGUILayout.Popup(Localization.Text("模式", "Mode"),
        (int)marker.Mode, new[]
        {
          Localization.Text("分组", "Group"),
          Localization.Text("不控制", "Exclude")
        });
      var mode = (ACCPartControlMode)modeIndex;
      var groupName = marker.GroupName;
      if (mode == ACCPartControlMode.Group)
        groupName = EditorGUILayout.TextField(Localization.Text("分组名称", "Group Name"), groupName);

      if (!EditorGUI.EndChangeCheck()) return;
      Undo.RecordObject(marker, "Configure ACC part control marker");
      marker.Mode = mode;
      marker.GroupName = groupName;
      EditorUtility.SetDirty(marker);
    }
  }

  internal static class ACCInspectorUI
  {
    public static void DrawHeader(string title, string documentationAnchor)
    {
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
      GUILayout.FlexibleSpace();
      Localization.DrawDocumentationButton(documentationAnchor);
      EditorGUILayout.EndHorizontal();
    }
  }
}