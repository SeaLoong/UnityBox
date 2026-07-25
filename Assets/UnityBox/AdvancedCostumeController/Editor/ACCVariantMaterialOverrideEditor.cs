using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [CustomEditor(typeof(ACCVariantMaterialOverride))]
  public class ACCVariantMaterialOverrideEditor : Editor
  {
    public override void OnInspectorGUI()
    {
      var marker = (ACCVariantMaterialOverride)target;
      EditorGUI.BeginChangeCheck();
      var outfitBase = (GameObject)EditorGUILayout.ObjectField("Outfit Base", marker.OutfitBase,
        typeof(GameObject), true);
      if (EditorGUI.EndChangeCheck())
      {
        Undo.RecordObject(marker, "Set ACC variant outfit base");
        marker.OutfitBase = outfitBase;
        PopulateMaterialEntries(marker);
        EditorUtility.SetDirty(marker);
      }

      using (new EditorGUI.DisabledScope(marker.OutfitBase == null))
      {
        if (GUILayout.Button("Refresh Materials"))
        {
          Undo.RecordObject(marker, "Refresh ACC variant materials");
          PopulateMaterialEntries(marker);
          EditorUtility.SetDirty(marker);
        }
      }

      if (marker.OutfitBase == null)
      {
        EditorGUILayout.HelpBox("选择服装本体后可配置其中所有 Renderer 的材质替换。", MessageType.Info);
        return;
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField("Material Replacements", EditorStyles.boldLabel);
      EditorGUILayout.HelpBox("Replacement 留空时保留原材质。", MessageType.None);
      foreach (var entry in marker.Replacements)
      {
        using (new EditorGUI.DisabledScope(true))
          EditorGUILayout.ObjectField(entry.Source, typeof(Material), false);
          var replacement = (Material)EditorGUILayout.ObjectField("Replace With", entry.Replacement,
            typeof(Material), false);
          if (replacement != entry.Replacement)
          {
            Undo.RecordObject(marker, "Change ACC material replacement");
            entry.Replacement = replacement;
            EditorUtility.SetDirty(marker);
          }
      }
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