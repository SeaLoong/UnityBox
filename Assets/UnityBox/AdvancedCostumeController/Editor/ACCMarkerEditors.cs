using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [CustomEditor(typeof(ACCOutfitMarker))]
  [CanEditMultipleObjects]
  public class ACCOutfitMarkerEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      serializedObject.Update();
      var marker = (ACCOutfitMarker)target;
      Localization.DrawInspectorHeader(Localization.Text("ACC 服装标记", "ACC Outfit Marker"));
      EditorGUILayout.HelpBox(Localization.Text(
        "将当前对象明确作为服装根。部件名称格式化会持久保存在此组件，并仅影响自动部件的菜单显示名称。",
        "Explicitly declares this object as an outfit root. Part name formatting is stored here and changes automatic part menu labels only."),
        MessageType.Info);

      if (targets.Length > 1)
      {
        EditorGUILayout.HelpBox(Localization.Text(
          "当前正在同时编辑多个 ACC 服装标记，修改会应用到所有选中的对象。",
          "Multiple ACC outfit markers are selected. Changes will be applied to all selected objects."),
          MessageType.Info);
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(Localization.Text("部件名称格式化", "Part Name Formatting"), EditorStyles.boldLabel);
      EditorGUILayout.PropertyField(serializedObject.FindProperty("PartNamePrefixToRemove"),
        new GUIContent(Localization.Text("移除前缀", "Remove Prefix")));
      EditorGUILayout.PropertyField(serializedObject.FindProperty("PartNameSuffixToRemove"),
        new GUIContent(Localization.Text("移除后缀", "Remove Suffix")));
      EditorGUILayout.PropertyField(serializedObject.FindProperty("PartNameRegexPattern"),
        new GUIContent(Localization.Text("正则表达式", "Regex Pattern")));
      EditorGUILayout.PropertyField(serializedObject.FindProperty("PartNameRegexReplacement"),
        new GUIContent(Localization.Text("正则替换为", "Regex Replacement")));

      serializedObject.ApplyModifiedProperties();

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

        EditorGUILayout.LabelField(Localization.PartSourceLegend(),
          EditorStyles.miniLabel);
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
  [CanEditMultipleObjects]
  public class ACCPartGroupMarkerEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      serializedObject.Update();
      var marker = (ACCPartGroupMarker)target;
      Localization.DrawInspectorHeader(Localization.Text("ACC 部件控制标记", "ACC Part Control Marker"));
      EditorGUILayout.HelpBox(Localization.Text(
        "Group 会将同名对象合并为一个开关；Exclude 不会生成当前对象或所在自动部件的 ACC 控制。",
        "Group combines same-name objects into one toggle. Exclude prevents ACC controls for this object and its containing automatic part."),
        MessageType.Info);

      if (targets.Length > 1)
      {
        EditorGUILayout.HelpBox(Localization.Text(
          "当前正在同时编辑多个 ACC 部件控制标记，修改会应用到所有选中的对象。",
          "Multiple ACC part control markers are selected. Changes will be applied to all selected objects."),
          MessageType.Info);
      }

      var modeProp = serializedObject.FindProperty("Mode");
      var modeIndex = EditorGUILayout.Popup(Localization.Text("模式", "Mode"),
        modeProp.enumValueIndex, new[]
        {
          Localization.Text("分组", "Group"),
          Localization.Text("不控制", "Exclude")
        });
      modeProp.enumValueIndex = modeIndex;

      var groupNameProp = serializedObject.FindProperty("GroupName");
      if ((ACCPartControlMode)modeIndex == ACCPartControlMode.Group)
        EditorGUILayout.PropertyField(groupNameProp, new GUIContent(Localization.Text("分组名称", "Group Name")));
      else
        groupNameProp.stringValue = string.Empty;

      serializedObject.ApplyModifiedProperties();
    }
  }
}