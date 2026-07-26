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
      ACCInspectorUI.DrawHeader(Localization.Text("ACC 材质变体", "ACC Material Variant"),
        "acc-variant-material-override");
      EditorGUILayout.HelpBox(Localization.Text(
        "为当前变体指定服装本体的材质替换。替换材质留空时保持原材质。",
        "Assign material replacements for this variant's outfit base. Leave a replacement empty to keep the original material."),
        MessageType.Info);

      EditorGUI.BeginChangeCheck();
      var outfitBase = (GameObject)EditorGUILayout.ObjectField(Localization.Text("服装本体", "Outfit Base"), marker.OutfitBase,
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
        if (GUILayout.Button(Localization.Text("刷新材质列表", "Refresh Materials")))
        {
          Undo.RecordObject(marker, "Refresh ACC variant materials");
          PopulateMaterialEntries(marker);
          EditorUtility.SetDirty(marker);
        }
      }

      if (marker.OutfitBase == null)
      {
        EditorGUILayout.HelpBox(Localization.Text("选择服装本体后可配置其中所有 Renderer 的材质替换。",
          "Select an Outfit Base to configure replacements for all of its Renderers."), MessageType.Info);
        return;
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(Localization.Text("材质替换", "Material Replacements"), EditorStyles.boldLabel);
      EditorGUILayout.HelpBox(Localization.Text("替换材质留空时保留原材质。",
        "Leave a replacement empty to keep the original material."), MessageType.None);
      foreach (var entry in marker.Replacements)
      {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.ObjectField(Localization.Text("原材质", "Source Material"), entry.Source,
          typeof(Material), false);
        var replacement = (Material)EditorGUILayout.ObjectField(
          Localization.Text("替换为", "Replace With"), entry.Replacement, typeof(Material), false);
        if (replacement != entry.Replacement)
        {
          Undo.RecordObject(marker, "Change ACC material replacement");
          entry.Replacement = replacement;
          EditorUtility.SetDirty(marker);
        }
        EditorGUILayout.EndVertical();
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