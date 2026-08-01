using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  [CustomEditor(typeof(ACCVariantMaterialOverride))]
  [CanEditMultipleObjects]
  public class ACCVariantMaterialOverrideEditor : Editor
  {
      private const string ConvertMenuPath =
        "GameObject/ACC/转换成服装变体 (Convert to Outfit Variant)";

      public override void OnInspectorGUI()
      {
        serializedObject.Update();
        var marker = (ACCVariantMaterialOverride)target;
        Localization.DrawInspectorHeader(Localization.Text(
          "ACC 材质变体替换", "ACC Material Variant Override"));
        EditorGUILayout.HelpBox(Localization.Text(
          "全局规则适合一次替换所有相同材质；精准覆盖只记录特定 Renderer 的差异槽位，并优先于全局规则。",
          "Global rules replace every matching material. Precise overrides store only differing renderer slots and take priority."),
          MessageType.Info);

        if (targets.Length > 1)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "当前正在同时编辑多个材质变体；刷新和自动对照会应用到所有选中对象。",
            "Multiple material variants are selected; refresh and compare actions apply to all selected objects."),
            MessageType.Info);
        }

        var outfitBaseProp = serializedObject.FindProperty("OutfitBase");
        bool sceneLocal = IsSceneLocal(marker.gameObject);
        if (!sceneLocal)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "自动分析和转换仅允许在普通场景中执行，Prefab Mode 中不会修改 Prefab 资产。",
            "Automatic analysis and conversion are available only in regular scenes; Prefab assets are not modified in Prefab Mode."),
            MessageType.Warning);
        }
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(outfitBaseProp,
          new GUIContent(Localization.Text("服装本体", "Outfit Base")));
        if (EditorGUI.EndChangeCheck())
        {
          serializedObject.ApplyModifiedProperties();
          if (sceneLocal)
            ForEachMarker("Set ACC variant outfit base", selectedMarker =>
              AnalyzeOrPopulate(selectedMarker));
          serializedObject.Update();
        }

        using (new EditorGUI.DisabledScope(marker.OutfitBase == null || !sceneLocal))
        {
          EditorGUILayout.BeginHorizontal();
          if (GUILayout.Button(Localization.Text("刷新全局材质", "Refresh Global Materials")))
          {
            ForEachMarker("Refresh ACC global materials", PopulateGlobalMaterialEntries);
            serializedObject.Update();
          }
          if (GUILayout.Button(Localization.Text("自动分析最优替换", "Analyze Optimal Replacements")))
          {
            ForEachMarker("Analyze ACC material replacements", selectedMarker =>
              AnalyzeOrPopulate(selectedMarker));
            serializedObject.Update();
          }
          EditorGUILayout.EndHorizontal();
        }

        if (marker.OutfitBase == null)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "请选择服装本体；也可同时选中本体与变体对象后使用 GameObject 右键转换菜单。",
            "Select an Outfit Base, or select a base and variant together and use the GameObject conversion menu."),
            MessageType.Info);
          return;
        }

        EditorGUILayout.Space();
        DrawGlobalReplacementList(
          Localization.Text("全局材质替换", "Global Material Replacements"),
          Localization.Text("同一 Source 在所有 Renderer 中统一替换；Replacement 留空表示不替换。",
            "Replaces the same Source across all Renderers; empty Replacement means no override."));

        EditorGUILayout.Space();
        DrawRendererOverrideList(
          Localization.Text("精准 Renderer 槽位覆盖", "Precise Renderer Slot Overrides"),
          Localization.Text("仅保存自动对照发现的差异槽位，无需配置每个 Mesh 的所有材质。",
            "Stores only differing slots found by comparison; no need to configure every material on every mesh."));

        serializedObject.ApplyModifiedProperties();
      }

      private void DrawGlobalReplacementList(string label, string help)
      {
        var property = serializedObject.FindProperty("Replacements");
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(help, MessageType.None);
        for (int i = 0; i < property.arraySize; i++)
        {
          var entry = property.GetArrayElementAtIndex(i);
          entry.isExpanded = true;
          using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
          {
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("Source"),
              new GUIContent(Localization.Text("原材质", "Source")));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("Replacement"),
              new GUIContent(Localization.Text("替换为", "Replacement")));
            if (GUILayout.Button(Localization.Text("移除此规则", "Remove Rule")))
            {
              property.DeleteArrayElementAtIndex(i);
              break;
            }
          }
        }
        if (GUILayout.Button(Localization.Text("添加全局规则", "Add Global Rule")))
        {
          property.InsertArrayElementAtIndex(property.arraySize);
          var entry = property.GetArrayElementAtIndex(property.arraySize - 1);
          entry.FindPropertyRelative("Source").objectReferenceValue = null;
          entry.FindPropertyRelative("Replacement").objectReferenceValue = null;
        }
      }

      private void DrawRendererOverrideList(string label, string help)
      {
        var property = serializedObject.FindProperty("RendererOverrides");
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(help, MessageType.None);
        for (int i = 0; i < property.arraySize; i++)
        {
          var entry = property.GetArrayElementAtIndex(i);
          entry.isExpanded = true;
          using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
          {
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("TargetRenderer"),
              new GUIContent(Localization.Text("目标 Renderer", "Target Renderer")));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("MaterialSlot"),
              new GUIContent(Localization.Text("材质槽位", "Material Slot")));
            using (new EditorGUI.DisabledScope(true))
              EditorGUILayout.PropertyField(entry.FindPropertyRelative("Source"),
                new GUIContent(Localization.Text("原材质", "Source")));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("Replacement"),
              new GUIContent(Localization.Text("替换为", "Replacement")));
            if (GUILayout.Button(Localization.Text("移除此覆盖", "Remove Override")))
            {
              property.DeleteArrayElementAtIndex(i);
              break;
            }
          }
        }
        if (GUILayout.Button(Localization.Text("添加精准覆盖", "Add Precise Override")))
        {
          property.InsertArrayElementAtIndex(property.arraySize);
          var entry = property.GetArrayElementAtIndex(property.arraySize - 1);
          entry.FindPropertyRelative("TargetRenderer").objectReferenceValue = null;
          entry.FindPropertyRelative("MaterialSlot").intValue = 0;
          entry.FindPropertyRelative("Source").objectReferenceValue = null;
          entry.FindPropertyRelative("Replacement").objectReferenceValue = null;
        }
      }

      private void ForEachMarker(string undoName,
        System.Action<ACCVariantMaterialOverride> action)
      {
        foreach (var selectedMarker in targets.OfType<ACCVariantMaterialOverride>())
        {
          if (!IsSceneLocal(selectedMarker.gameObject)) continue;
          Undo.RecordObject(selectedMarker, undoName);
          action(selectedMarker);
          EditorUtility.SetDirty(selectedMarker);
        }
      }

      internal static void PopulateGlobalMaterialEntries(ACCVariantMaterialOverride marker)
      {
        if (marker.OutfitBase == null) return;
        var previous = (marker.Replacements ??
          new List<ACCVariantMaterialOverride.MaterialReplacement>())
          .Where(entry => entry != null && entry.Source != null)
          .GroupBy(entry => entry.Source)
          .ToDictionary(group => group.Key, group => group.First().Replacement);
        var materials = marker.OutfitBase.GetComponentsInChildren<Renderer>(true)
          .SelectMany(renderer => renderer.sharedMaterials)
          .Where(material => material != null)
          .Distinct()
          .ToList();

        marker.Replacements = materials.Select(material =>
          new ACCVariantMaterialOverride.MaterialReplacement
          {
            Source = material,
            Replacement = previous.TryGetValue(material, out var replacement)
              ? replacement
              : null
          }).ToList();
      }

      internal static int AnalyzeOptimalReplacements(ACCVariantMaterialOverride marker)
      {
        if (marker.OutfitBase == null) return 0;
        var baseRenderers = BuildRendererMap(marker.OutfitBase.transform);
        var variantRenderers = BuildRendererMap(marker.transform);
        var observations = new List<MaterialSlotObservation>();

        foreach (var pair in baseRenderers)
        {
          if (!variantRenderers.TryGetValue(pair.Key, out var variantRenderer)) continue;
          var baseMaterials = pair.Value.sharedMaterials;
          var variantMaterials = variantRenderer.sharedMaterials;
          int slots = Mathf.Min(baseMaterials.Length, variantMaterials.Length);
          for (int slot = 0; slot < slots; slot++)
          {
            // 空目标材质表示清空槽位；当前组件以空 Replacement 表示“不覆盖”，
            // 因此不自动生成这种不可表达的规则。
            if (variantMaterials[slot] == null) continue;
            observations.Add(new MaterialSlotObservation
            {
              Renderer = pair.Value,
              Slot = slot,
              Source = baseMaterials[slot],
              Target = variantMaterials[slot]
            });
          }
        }

        if (observations.Count == 0) return -1;

        var globalRules = new List<ACCVariantMaterialOverride.MaterialReplacement>();
        var overrides = new List<ACCVariantMaterialOverride.RendererMaterialReplacement>();
        foreach (var sourceGroup in observations.GroupBy(item => item.Source))
        {
          // 最常见映射作为全局规则；票数相同时优先保持原材质，避免过度替换。
          var majorityTarget = sourceGroup
            .GroupBy(item => item.Target)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key == sourceGroup.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

          if (majorityTarget != sourceGroup.Key)
          {
            globalRules.Add(new ACCVariantMaterialOverride.MaterialReplacement
            {
              Source = sourceGroup.Key,
              Replacement = majorityTarget
            });
          }

          foreach (var observation in sourceGroup)
          {
            if (observation.Target == majorityTarget) continue;
            overrides.Add(new ACCVariantMaterialOverride.RendererMaterialReplacement
            {
              TargetRenderer = observation.Renderer,
              MaterialSlot = observation.Slot,
              Source = observation.Source,
              Replacement = observation.Target
            });
          }
        }

        marker.Replacements = globalRules;
        marker.RendererOverrides = overrides;
        return globalRules.Count + overrides.Count;
      }

      private static int AnalyzeOrPopulate(ACCVariantMaterialOverride marker)
      {
        int result = AnalyzeOptimalReplacements(marker);
        if (result >= 0) return result;
        PopulateGlobalMaterialEntries(marker);
        marker.RendererOverrides = new List<ACCVariantMaterialOverride.RendererMaterialReplacement>();
        return 0;
      }

      private sealed class MaterialSlotObservation
      {
        public Renderer Renderer;
        public int Slot;
        public Material Source;
        public Material Target;
      }

      private static Dictionary<string, Renderer> BuildRendererMap(Transform root)
      {
        var result = new Dictionary<string, Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
          string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
          var sameObjectRenderers = renderer.GetComponents<Renderer>();
          int componentIndex = System.Array.IndexOf(sameObjectRenderers, renderer);
          string key = path + "|" + renderer.GetType().FullName + "|" + componentIndex;
          result[key] = renderer;
        }
        return result;
      }

      [MenuItem(ConvertMenuPath, false, 20)]
      private static void ConvertSelectedObjectsToVariant()
      {
        var variant = Selection.activeGameObject;
        if (variant == null || !IsSceneLocal(variant))
        {
          EditorUtility.DisplayDialog(Localization.Text("无法转换", "Cannot Convert"),
            Localization.Text("请选择场景中的服装变体对象；ACC 不会修改 Project 中的 Prefab 资产。",
              "Select an outfit variant object in the scene; ACC will not modify Prefab assets in the Project."), "OK");
          return;
        }

        var outfitBase = FindLikelyOutfitBase(variant);
        if (outfitBase == null)
        {
          EditorUtility.DisplayDialog(Localization.Text("无法转换", "Cannot Convert"),
            Localization.Text("未能在同级对象中自动识别唯一的服装本体。请先确保本体具有 ACC Outfit Marker、MA Merge Armature 或可识别的独立骨架。",
              "Could not identify a unique sibling Outfit Base. Ensure the base has an ACC Outfit Marker, MA Merge Armature, or a recognizable owned armature."), "OK");
          return;
        }
        if (!EditorUtility.DisplayDialog(Localization.Text("转换成服装变体", "Convert to Outfit Variant"),
          Localization.Text(
            $"自动识别本体：{outfitBase.name}\n变体来源：{variant.name}\n\n将添加或更新材质变体组件，并生成多数全局规则与少数精准例外。",
            $"Detected Outfit Base: {outfitBase.name}\nVariant Source: {variant.name}\n\nThe material variant component will be added or updated using majority global rules plus precise exceptions."),
          Localization.Text("转换", "Convert"), Localization.Text("取消", "Cancel")))
          return;

        var marker = variant.GetComponent<ACCVariantMaterialOverride>();
        if (marker == null) marker = Undo.AddComponent<ACCVariantMaterialOverride>(variant);
        Undo.RecordObject(marker, "Convert to ACC outfit variant");
        marker.OutfitBase = outfitBase;
        int ruleCount = AnalyzeOrPopulate(marker);
        EditorUtility.SetDirty(marker);
        Debug.Log(Localization.Text(
          $"[ACC] 已将 {variant.name} 转换为 {outfitBase.name} 的材质变体，生成 {ruleCount} 条最优替换规则。",
          $"[ACC] Converted {variant.name} to a material variant of {outfitBase.name}; generated {ruleCount} optimized replacement rules."), marker);
      }

      private static GameObject FindLikelyOutfitBase(GameObject variant)
      {
        var existing = variant.GetComponent<ACCVariantMaterialOverride>();
        if (existing != null && existing.OutfitBase != null &&
            existing.OutfitBase != variant &&
            existing.OutfitBase.transform.parent == variant.transform.parent)
          return existing.OutfitBase;
        if (variant.transform.parent == null) return null;

        var candidates = new List<GameObject>();
        for (int i = 0; i < variant.transform.parent.childCount; i++)
        {
          var sibling = variant.transform.parent.GetChild(i).gameObject;
          if (sibling == variant) continue;
          var siblingVariant = sibling.GetComponent<ACCVariantMaterialOverride>();
          if (siblingVariant != null && siblingVariant.OutfitBase != null) continue;
          bool explicitOutfit = sibling.GetComponent<ACCOutfitMarker>() != null;
          bool detectedOutfit = Utils.TryGetOwnedArmature(sibling.transform, out _) &&
            Utils.HasMeshInHierarchy(sibling.transform);
          if (explicitOutfit || detectedOutfit) candidates.Add(sibling);
        }

        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) return null;
        var ranked = candidates
          .Select(candidate => new
          {
            Candidate = candidate,
            Score = CountMatchingRendererSlots(candidate.transform, variant.transform)
          })
          .OrderByDescending(item => item.Score)
          .ThenBy(item => item.Candidate.transform.GetSiblingIndex())
          .ToList();
        if (ranked.Count > 1 && ranked[0].Score == ranked[1].Score)
          return null;
        return ranked[0].Candidate;
      }

      private static int CountMatchingRendererSlots(Transform outfitBase, Transform variant)
      {
        var baseRenderers = BuildRendererMap(outfitBase);
        var variantRenderers = BuildRendererMap(variant);
        int score = 0;
        foreach (var pair in baseRenderers)
        {
          if (!variantRenderers.TryGetValue(pair.Key, out var variantRenderer)) continue;
          score += Mathf.Min(pair.Value.sharedMaterials.Length,
            variantRenderer.sharedMaterials.Length);
        }
        return score;
      }

      [MenuItem(ConvertMenuPath, true)]
      private static bool ValidateConvertSelectedObjectsToVariant()
      {
        var selected = Selection.activeGameObject;
        return selected != null && IsSceneLocal(selected);
      }

      private static bool IsSceneLocal(GameObject gameObject)
      {
        if (gameObject == null || EditorUtility.IsPersistent(gameObject) ||
            !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
          return false;
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        return prefabStage == null || prefabStage.scene != gameObject.scene;
      }
  }
}