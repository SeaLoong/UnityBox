using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 统一 ACC Inspector 的 Undo 边界。SerializedObject 默认会创建隐式记录，
  /// 但在刚添加组件、延迟初始化或多对象编辑时可能与组件创建合并；显式分组
  /// 后再使用 ApplyModifiedPropertiesWithoutUndo，确保一次用户操作对应一次 Undo。
  /// </summary>
  internal static class ACCEditorUndo
  {
    public static int Begin(string undoName)
    {
      Undo.IncrementCurrentGroup();
      int group = Undo.GetCurrentGroup();
      Undo.SetCurrentGroupName(undoName);
      return group;
    }

    public static void Complete(int group)
    {
      Undo.CollapseUndoOperations(group);
    }

    public static void RecordObjects(
      IEnumerable<Object> objects,
      string undoName)
    {
      var validObjects = objects?.Where(target => target != null).ToArray()
        ?? new Object[0];
      if (validObjects.Length > 0)
        Undo.RecordObjects(validObjects, undoName);
    }

    public static void RecordPrefabInstanceModifications(
      IEnumerable<Object> objects)
    {
      foreach (var target in objects?.Where(target => target != null) ??
        Enumerable.Empty<Object>())
      {
        if (PrefabUtility.IsPartOfPrefabInstance(target))
          PrefabUtility.RecordPrefabInstancePropertyModifications(target);
      }
    }

    public static T AddComponent<T>(GameObject target, string undoName)
      where T : Component
    {
      try
      {
        return Undo.AddComponent<T>(target);
      }
      catch
      {
        var created = target.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(created, undoName);
        Undo.RegisterCompleteObjectUndo(created, undoName);
        return created;
      }
    }

    public static bool ApplySerializedProperties(
      SerializedObject serializedObject,
      IEnumerable<Object> targets,
      string undoName)
    {
      if (serializedObject == null || !serializedObject.hasModifiedProperties)
        return false;

      int group = Begin(undoName);
      try
      {
        RecordObjects(targets, undoName);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        RecordPrefabInstanceModifications(targets);
      }
      finally
      {
        Complete(group);
      }
      return true;
    }
  }

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
        "将当前对象声明为服装根；名称格式化仅影响自动部件的菜单标签。",
        "Declares this object as an outfit root; name formatting only affects automatic part menu labels."),
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

      ACCEditorUndo.ApplySerializedProperties(serializedObject, targets,
        "Edit ACC outfit marker");

      if (!Utils.IsValidRegex(marker.PartNameRegexPattern))
        EditorGUILayout.HelpBox(Localization.Text("正则表达式无效，将忽略正则替换。",
          "The regex pattern is invalid and will be ignored."), MessageType.Warning);

      Scanner.CollectParts(marker.transform, out var parts, out var excludedParts, out var controls);
      EditorGUILayout.Space();
      DrawPartSummary(parts.Count, excludedParts.Count);

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

    private static void DrawPartSummary(int controlledCount, int excludedCount)
    {
      var countStyle = new GUIStyle(EditorStyles.boldLabel)
      {
        alignment = TextAnchor.MiddleRight
      };
      countStyle.normal.textColor = controlledCount > 0
        ? new Color(0.35f, 0.85f, 0.5f)
        : EditorStyles.label.normal.textColor;

      using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
      {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(Localization.Text(
          "部件扫描摘要", "Part Scan Summary"), EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(Localization.Text(
          $"{controlledCount} 可控制", $"{controlledCount} controllable"), countStyle,
          GUILayout.MinWidth(100));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(Localization.Text(
          $"已排除：{excludedCount}", $"Excluded: {excludedCount}"),
          EditorStyles.miniLabel);
      }
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
        "Group 合并为一个开关；Exclude 排除当前对象及其自动部件控制。",
        "Group combines objects into one toggle; Exclude removes this object and its automatic part from ACC control."),
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
      {
        EditorGUILayout.PropertyField(groupNameProp, new GUIContent(Localization.Text("分组名称", "Group Name")));
        string effectiveName = string.IsNullOrWhiteSpace(groupNameProp.stringValue)
          ? marker.gameObject.name
          : groupNameProp.stringValue.Trim();
        EditorGUILayout.LabelField(Localization.Text(
          "有效分组名称", "Effective Group Name"), effectiveName);
      }
      else
      {
        groupNameProp.stringValue = string.Empty;
        EditorGUILayout.HelpBox(Localization.Text(
          "当前对象及其所在自动部件不会生成 ACC 控制。",
          "This object and its containing automatic part will not receive ACC control."),
          MessageType.Warning);
      }

      ACCEditorUndo.ApplySerializedProperties(serializedObject, targets,
        "Edit ACC part group marker");
    }
  }
}