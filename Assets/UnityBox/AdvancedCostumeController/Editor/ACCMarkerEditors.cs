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
      ACCVariantPreview.Stop();
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
      return Undo.AddComponent<T>(target);
    }

    public static bool ApplySerializedProperties(
      SerializedObject serializedObject,
      IEnumerable<Object> targets,
      string undoName,
      bool keepVariantPreview = false)
    {
      if (serializedObject == null || !serializedObject.hasModifiedProperties)
        return false;

      var preview = keepVariantPreview ? ACCVariantPreview.ActiveVariant : null;
      var previewBase = preview != null ? preview.OutfitBase : null;
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
      // Only rule editing opts into live preview. Restore before recording Undo
      // and writing Prefab overrides, then resample the same active variant.
      if (preview != null && preview.OutfitBase == previewBase)
        ACCVariantPreview.Start(preview);
      return true;
    }
  }

  internal static class ACCVariantPreviewGUI
  {
    public static void Draw(GameObject outfitBase, ACCVariantMaterialOverride variant = null)
    {
      var active = ACCVariantPreview.GetActiveVariant(outfitBase);
      if (variant == null && active == null) return;
      using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
      {
        if (active != null)
          EditorGUILayout.HelpBox(Localization.Text(
            $"正在预览 {active.name} 变体", $"Previewing variant: {active.name}"), MessageType.Info);
        else
          EditorGUILayout.LabelField(Localization.Text("材质变体预览", "Material Variant Preview"), EditorStyles.boldLabel);

        if (active != null)
          EditorGUILayout.LabelField(Localization.Text(
            "实时预览中：修改替换规则会立即更新场景。",
            "Live preview: editing replacement rules updates the scene immediately."),
            EditorStyles.wordWrappedMiniLabel);

        bool stop = active != null && (variant == null || variant == active);
        using (new EditorGUI.DisabledScope(!stop && !ACCVariantPreview.CanPreview(variant)))
        {
          string label = stop ? Localization.Text("停止预览", "Stop Preview") :
            active != null ? Localization.Text($"切换预览到 {variant.name}", $"Preview {variant.name}") :
            Localization.Text("预览变体", "Preview Variant");
          if (GUILayout.Button(label))
          {
            if (stop) ACCVariantPreview.Stop();
            else ACCVariantPreview.Start(variant);
          }
        }
        if (variant != null && active == null && !ACCVariantPreview.CanPreview(variant))
          EditorGUILayout.HelpBox(Localization.Text(
            "请在编辑模式选择同一场景中的有效本体；先停止其它动画预览。",
            "Use a valid base in the same scene in Edit Mode; stop other animation previews first."), MessageType.None);
      }
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

      foreach (var selected in targets.OfType<ACCOutfitMarker>())
        ACCVariantPreviewGUI.Draw(selected.gameObject);

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
        var excludedSet = new System.Collections.Generic.HashSet<GameObject>(excludedParts);
        foreach (var part in parts.Concat(excludedParts)
          .Where(part => part != null)
          .Distinct()
          .OrderBy(part => Utils.GetHierarchyPath(marker.gameObject, part),
            System.StringComparer.Ordinal))
        {
          DrawPartPreviewRow(marker, part, controls, excludedSet.Contains(part));
        }
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

      EditorGUILayout.BeginHorizontal();
      var content = new GUIContent(label,
        "点击选择并定位对象 / Click to select and locate this object");
      if (GUILayout.Button(content, EditorStyles.linkLabel, GUILayout.Width(180)))
      {
        Utils.SelectAndPingObject(part);
      }
      EditorGUILayout.LabelField(source, EditorStyles.label, GUILayout.ExpandWidth(true));
      EditorGUILayout.EndHorizontal();
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