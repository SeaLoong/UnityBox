using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [CustomEditor(typeof(ACCVariantMaterialOverride))]
  [CanEditMultipleObjects]
  public class ACCVariantMaterialOverrideEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      serializedObject.Update();
      var marker = (ACCVariantMaterialOverride)target;
      ACCInspectorUI.DrawHeader(Localization.Text("ACC 材质变体替换", "ACC Material Variant Override"));
      EditorGUILayout.HelpBox(Localization.Text(
        "为当前变体指定服装本体的材质替换。替换材质留空时保持原材质。",
        "Assign material replacements for this variant's outfit base. Leave a replacement empty to keep the original material."),
        MessageType.Info);

      if (targets.Length > 1)
      {
        EditorGUILayout.HelpBox(Localization.Text(
          "当前正在同时编辑多个 ACC 材质变体，修改会应用到所有选中的对象。",
          "Multiple ACC material variants are selected. Changes will be applied to all selected objects."),
          MessageType.Info);
      }

      var outfitBaseProp = serializedObject.FindProperty("OutfitBase");
      EditorGUI.BeginChangeCheck();
      EditorGUILayout.PropertyField(outfitBaseProp, new GUIContent(Localization.Text("服装本体", "Outfit Base")));
      if (EditorGUI.EndChangeCheck())
      {
        serializedObject.ApplyModifiedProperties();
        foreach (var selectedMarker in targets.OfType<ACCVariantMaterialOverride>())
        {
          Undo.RecordObject(selectedMarker, "Set ACC variant outfit base");
          PopulateMaterialEntries(selectedMarker);
          EditorUtility.SetDirty(selectedMarker);
        }
      }
      else
      {
        serializedObject.ApplyModifiedProperties();
      }

      using (new EditorGUI.DisabledScope(marker.OutfitBase == null))
      {
        if (GUILayout.Button(Localization.Text("刷新材质列表", "Refresh Materials")))
        {
          foreach (var selectedMarker in targets.OfType<ACCVariantMaterialOverride>())
          {
            Undo.RecordObject(selectedMarker, "Refresh ACC variant materials");
            PopulateMaterialEntries(selectedMarker);
            EditorUtility.SetDirty(selectedMarker);
          }
        }
      }

      if (marker.OutfitBase == null)
      {
        EditorGUILayout.HelpBox(Localization.Text("选择服装本体后可配置其中所有 Renderer 的材质替换。",
          "Select an Outfit Base to configure replacements for all of its Renderers."), MessageType.Info);
        return;
      }

      EditorGUILayout.Space();
      EditorGUILayout.PropertyField(serializedObject.FindProperty("Replacements"), new GUIContent(Localization.Text("材质替换", "Material Replacements")), true);
      serializedObject.ApplyModifiedProperties();
    }

    private static void PopulateMaterialEntries(ACCVariantMaterialOverride marker)
    {
      if (marker.OutfitBase == null) return;
      var previous = marker.Replacements
        .Where(entry => entry != null && entry.Source != null)
        .GroupBy(entry => entry.Source)
        .ToDictionary(group => group.Key, group => group.First().Replacement);
      var materials = marker.OutfitBase.GetComponentsInChildren<Renderer>(true)
        .SelectMany(renderer => renderer.sharedMaterials)
        .Where(material => material != null)
        .Distinct()
        .ToList();

      marker.Replacements = materials.Select(material => new ACCVariantMaterialOverride.MaterialReplacement
      {
        Source = material,
        Replacement = previous.TryGetValue(material, out var replacement) ? replacement : null
      }).ToList();
    }
  }
}