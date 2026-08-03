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
      private const string ConvertMenuPath =
        "GameObject/AdvancedCostumeController/转换成服装变体 (Convert to Outfit Variant)";
      private bool showGlobalReplacements = true;
      private bool showRendererOverrides = true;
      private GameObject analysisSource;
      private string analysisStatus;
      private MessageType analysisStatusType = MessageType.Info;

      private void OnEnable()
      {
        // Unity calls Reset when the component is added, but Reset cannot safely
        // compare imported Renderer data. Defer one frame so serialized OutfitBase
        // and the sibling hierarchy are ready, then initialize the preview once.
        EditorApplication.delayCall += AutoInitializePreview;
      }

      private void OnDisable()
      {
        EditorApplication.delayCall -= AutoInitializePreview;
      }

      private void AutoInitializePreview()
      {
        if (this == null || targets == null) return;

        var pendingMarkers = targets.OfType<ACCVariantMaterialOverride>()
          .Where(selectedMarker => selectedMarker != null &&
            !selectedMarker.EditorPreviewInitialized &&
            selectedMarker.OutfitBase != null &&
            IsEditableSceneObject(selectedMarker.gameObject))
          .ToList();
        if (pendingMarkers.Count == 0) return;

        const string undoName = "Initialize ACC variant material preview";
        int undoGroup = ACCEditorUndo.Begin(undoName);
        try
        {
          ACCEditorUndo.RecordObjects(pendingMarkers.Cast<Object>(), undoName);
          foreach (var selectedMarker in pendingMarkers)
          {
            // Components created before the one-time initialization flag was
            // introduced may already contain user-authored rules. Preserve them
            // rather than treating an old serialized false flag as a new marker.
            if ((selectedMarker.Replacements?.Count ?? 0) > 0 ||
                (selectedMarker.RendererOverrides?.Count ?? 0) > 0)
            {
              selectedMarker.MarkEditorPreviewInitialized();
              EditorUtility.SetDirty(selectedMarker);
              continue;
            }

            AnalyzeOrPopulate(selectedMarker);
            selectedMarker.MarkEditorPreviewInitialized();
            EditorUtility.SetDirty(selectedMarker);
          }
          ACCEditorUndo.RecordPrefabInstanceModifications(pendingMarkers.Cast<Object>());
        }
        finally
        {
          ACCEditorUndo.Complete(undoGroup);
        }

        serializedObject.Update();
        Repaint();
      }

      public override void OnInspectorGUI()
      {
        serializedObject.Update();
        var marker = (ACCVariantMaterialOverride)target;
        Localization.DrawInspectorHeader(Localization.Text(
          "ACC 材质变体替换", "ACC Material Variant Override"));
        EditorGUILayout.HelpBox(Localization.Text(
          "全局规则按材质匹配，精准覆盖按 Renderer 槽位匹配且优先。",
          "Global rules match materials; precise overrides match Renderer slots and take priority."),
          MessageType.Info);

        if (targets.Length > 1)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "多选编辑：操作将应用到所有选中对象。",
            "Multi-edit: actions apply to all selected objects."),
            MessageType.Info);
        }

        var outfitBaseProp = serializedObject.FindProperty("OutfitBase");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(outfitBaseProp,
          new GUIContent(Localization.Text("服装本体", "Outfit Base")));
        if (EditorGUI.EndChangeCheck())
        {
          ForEachMarker("Set ACC variant outfit base", selectedMarker =>
          {
            if (CanAnalyzeMarker(selectedMarker))
              AnalyzeOrPopulate(selectedMarker);
            else if (CanRefreshMarker(selectedMarker))
              PopulateGlobalMaterialEntries(selectedMarker);
            selectedMarker.MarkEditorPreviewInitialized();
          });
          serializedObject.Update();
        }

        if (marker.OutfitBase == null)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "请先设置 Outfit Base。",
            "Set an Outfit Base first."),
            MessageType.Info);
          return;
        }

        if (marker.OutfitBase == marker.gameObject)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "Outfit Base 不能指向自身。",
            "Outfit Base cannot reference itself."),
            MessageType.Warning);
        }

        bool canRefresh = targets.OfType<ACCVariantMaterialOverride>()
          .Any(CanRefreshMarker);
        bool canAnalyze = targets.OfType<ACCVariantMaterialOverride>()
          .Any(CanAnalyzeMarker);
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!canRefresh))
        {
          if (GUILayout.Button(Localization.Text(
            "刷新本体材质", "Refresh Outfit Materials")))
          {
            ForEachMarker("Refresh ACC outfit materials", selectedMarker =>
            {
              PopulateGlobalMaterialEntries(selectedMarker);
              selectedMarker.MarkEditorPreviewInitialized();
            }, CanRefreshMarker);
            serializedObject.Update();
          }
        }
        using (new EditorGUI.DisabledScope(!canAnalyze))
        {
          if (GUILayout.Button(Localization.Text(
            "分析当前对象", "Analyze Current Object")))
          {
            ForEachMarker("Analyze ACC material replacements", selectedMarker =>
            {
              AnalyzeOrPopulate(selectedMarker);
              selectedMarker.MarkEditorPreviewInitialized();
            }, CanAnalyzeMarker);
            serializedObject.Update();
          }
        }
        EditorGUILayout.EndHorizontal();

        DrawAnalysisTool();

        if (IsAlreadyConvertedEmptyVariant(marker) && analysisSource == null)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "当前对象已转换且为空，请使用外部来源分析。",
            "This object is converted and empty; use an external source to analyze it."),
            MessageType.Info);
        }

        EditorGUILayout.Space();
        DrawGlobalReplacementList(
          Localization.Text("全局替换", "Global Replacements"),
          Localization.Text("在所有 Renderer 中替换同一 Source；留空表示不替换。",
            "Replaces a Source on all Renderers; empty means no override."));

        EditorGUILayout.Space();
        DrawRendererOverrideList(
          Localization.Text("精准槽位覆盖", "Precise Slot Overrides"),
          Localization.Text("仅记录差异槽位，且优先于全局规则。",
            "Stores differing slots only and takes priority over global rules."));

        ACCEditorUndo.ApplySerializedProperties(serializedObject, targets,
          "Edit ACC material variant");
      }

      private void DrawAnalysisTool()
      {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(Localization.Text(
          "外部材质来源", "External Material Source"),
          EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        analysisSource = (GameObject)EditorGUILayout.ObjectField(
          new GUIContent(Localization.Text("来源", "Source")),
          analysisSource, typeof(GameObject), true);
        bool sourceChanged = EditorGUI.EndChangeCheck();
        if (sourceChanged)
          analysisStatus = null;

        bool validSource = IsValidAnalysisSource(analysisSource);
        bool canApply = validSource && targets.OfType<ACCVariantMaterialOverride>()
          .Any(CanApplyAnalysisMarker);
        using (new EditorGUI.DisabledScope(!canApply))
        {
          if (GUILayout.Button(Localization.Text("应用", "Apply"),
            GUILayout.Width(58)))
          {
            ApplyAnalysisSource(analysisSource);
            serializedObject.Update();
            Repaint();
          }
        }
        using (new EditorGUI.DisabledScope(analysisSource == null))
        {
          if (GUILayout.Button(Localization.Text("清除", "Clear"),
            GUILayout.Width(58)))
          {
            analysisSource = null;
            analysisStatus = null;
          }
        }
        EditorGUILayout.EndHorizontal();

        if (sourceChanged && analysisSource != null && canApply &&
            ApplyAnalysisSource(analysisSource))
        {
          serializedObject.Update();
          Repaint();
          GUIUtility.ExitGUI();
        }

        if (analysisSource != null && !validSource)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "请拖入场景 GameObject 或 Project Prefab。",
            "Drop a scene GameObject or Project Prefab."),
            MessageType.Warning);
        }
        else if (analysisSource != null && !canApply)
        {
          EditorGUILayout.HelpBox(Localization.Text(
            "请先设置 Outfit Base。",
            "Set an Outfit Base first."), MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(analysisStatus))
          EditorGUILayout.HelpBox(analysisStatus, analysisStatusType);
      }

      private bool ApplyAnalysisSource(GameObject source)
      {
        analysisStatus = null;
        analysisStatusType = MessageType.Info;

        if (!IsValidAnalysisSource(source))
        {
          analysisStatus = Localization.Text(
            "来源无效，未修改。",
            "Invalid source; no changes made.");
          analysisStatusType = MessageType.Warning;
          return false;
        }

        var selectedMarkers = targets.OfType<ACCVariantMaterialOverride>()
          .Where(CanApplyAnalysisMarker)
          .ToList();
        if (selectedMarkers.Count == 0)
        {
          analysisStatus = Localization.Text(
            "没有可用的 ACC 组件，请先设置 Outfit Base。",
            "No ACC component is ready. Set an Outfit Base first.");
          analysisStatusType = MessageType.Warning;
          return false;
        }

        // Do this read-only preflight before opening an Undo group. A source
        // with no matching renderer slots must not clear existing rules or
        // create an empty Undo entry.
        var analyzableMarkers = selectedMarkers
          .Where(marker => CollectMaterialSlotObservations(
            marker.OutfitBase.transform, source.transform).Count > 0)
          .ToList();
        if (analyzableMarkers.Count == 0)
        {
          analysisStatus = Localization.Text(
            $"未找到匹配槽位，规则未修改：{source.name}",
            $"No matching slots; rules unchanged: {source.name}");
          analysisStatusType = MessageType.Warning;
          return false;
        }

        const string undoName = "Apply ACC material analysis";
        int undoGroup = ACCEditorUndo.Begin(undoName);
        int appliedCount = 0;
        int totalRuleCount = 0;
        try
        {
          ACCEditorUndo.RecordObjects(analyzableMarkers.Cast<Object>(), undoName);
          if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

          foreach (var selectedMarker in analyzableMarkers)
          {
            int ruleCount = AnalyzeOrPopulate(selectedMarker, source);
            if (ruleCount < 0) continue;

            selectedMarker.MarkEditorPreviewInitialized();
            EditorUtility.SetDirty(selectedMarker);
            appliedCount++;
            totalRuleCount += ruleCount;
          }
          ACCEditorUndo.RecordPrefabInstanceModifications(analyzableMarkers.Cast<Object>());
        }
        finally
        {
          ACCEditorUndo.Complete(undoGroup);
        }

        if (appliedCount == 0)
        {
          analysisStatus = Localization.Text(
            "未生成规则，当前规则未修改。",
            "No rules generated; existing rules unchanged.");
          analysisStatusType = MessageType.Warning;
          return false;
        }

        int skippedCount = selectedMarkers.Count - appliedCount;
        string skippedText = skippedCount > 0
          ? Localization.Text($"；跳过 {skippedCount} 个无匹配对象",
            $"; skipped {skippedCount} without matching slots")
          : string.Empty;
        analysisStatus = Localization.Text(
          $"已应用 {source.name}：{totalRuleCount} 条规则{skippedText}。",
          $"Applied {source.name}: {totalRuleCount} rule(s){skippedText}.");
        analysisStatusType = MessageType.Info;
        return true;
      }

      private void DrawGlobalReplacementList(string label, string help)
      {
        var property = serializedObject.FindProperty("Replacements");
        showGlobalReplacements = EditorGUILayout.Foldout(showGlobalReplacements,
          $"{label} ({property.arraySize})", true);
        if (!showGlobalReplacements) return;
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
            if (GUILayout.Button(Localization.Text("删除规则", "Delete Rule")))
            {
              property.DeleteArrayElementAtIndex(i);
              break;
            }
          }
        }
        if (GUILayout.Button(Localization.Text("添加规则", "Add Rule")))
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
        showRendererOverrides = EditorGUILayout.Foldout(showRendererOverrides,
          $"{label} ({property.arraySize})", true);
        if (!showRendererOverrides) return;
        EditorGUILayout.HelpBox(help, MessageType.None);
        for (int i = 0; i < property.arraySize; i++)
        {
          var entry = property.GetArrayElementAtIndex(i);
          entry.isExpanded = true;
          using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
          {
            var targetRendererProperty = entry.FindPropertyRelative("TargetRenderer");
            var slotProperty = entry.FindPropertyRelative("MaterialSlot");
            var sourceProperty = entry.FindPropertyRelative("Source");
            var previousRenderer = targetRendererProperty.objectReferenceValue as Renderer;
            int previousSlot = slotProperty.intValue;
            EditorGUILayout.PropertyField(targetRendererProperty,
              new GUIContent(Localization.Text("目标 Renderer", "Target Renderer")));
            DrawMaterialSlotPopup(
              slotProperty,
              targetRendererProperty.objectReferenceValue as Renderer);
            if (previousRenderer != targetRendererProperty.objectReferenceValue as Renderer ||
                previousSlot != slotProperty.intValue)
            {
              sourceProperty.objectReferenceValue = GetMaterialAtSlot(
                targetRendererProperty.objectReferenceValue as Renderer,
                slotProperty.intValue);
            }
            using (new EditorGUI.DisabledScope(true))
              EditorGUILayout.PropertyField(sourceProperty,
                new GUIContent(Localization.Text("原材质", "Source")));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("Replacement"),
              new GUIContent(Localization.Text("替换为", "Replacement")));
              if (GUILayout.Button(Localization.Text("删除覆盖", "Delete Override")))
            {
              property.DeleteArrayElementAtIndex(i);
              break;
            }
          }
        }
        if (GUILayout.Button(Localization.Text("添加覆盖", "Add Override")))
        {
          property.InsertArrayElementAtIndex(property.arraySize);
          var entry = property.GetArrayElementAtIndex(property.arraySize - 1);
          entry.FindPropertyRelative("TargetRenderer").objectReferenceValue = null;
          entry.FindPropertyRelative("MaterialSlot").intValue = 0;
          entry.FindPropertyRelative("Source").objectReferenceValue = null;
          entry.FindPropertyRelative("Replacement").objectReferenceValue = null;
        }
      }

      private static void DrawMaterialSlotPopup(
        SerializedProperty slotProperty,
        Renderer targetRenderer)
      {
        Material[] materials = targetRenderer != null
          ? targetRenderer.sharedMaterials
          : null;
        bool hasSlots = materials != null && materials.Length > 0;
        string[] options = hasSlots
          ? materials.Select((material, index) =>
            string.Format("{0}: {1}", index, GetMaterialSlotName(material)))
            .ToArray()
          : new[]
          {
            targetRenderer == null
              ? Localization.Text("请先选择目标 Renderer", "Select a Target Renderer first")
              : Localization.Text("目标 Renderer 没有材质槽", "Target Renderer has no material slots")
          };

        using (new EditorGUI.DisabledScope(!hasSlots))
        {
          int selectedSlot = hasSlots
            ? Mathf.Clamp(slotProperty.intValue, 0, options.Length - 1)
            : 0;
          int newSlot = EditorGUILayout.Popup(
            Localization.Text("材质槽位", "Material Slot"), selectedSlot, options);
          if (hasSlots)
            slotProperty.intValue = newSlot;
        }
      }

      private static string GetMaterialSlotName(Material material)
      {
        if (material == null)
          return Localization.Text("<空槽位>", "<Empty Slot>");
        return string.IsNullOrEmpty(material.name)
          ? Localization.Text("<未命名材质>", "<Unnamed Material>")
          : material.name;
      }

      private static Material GetMaterialAtSlot(Renderer renderer, int slot)
      {
        if (renderer == null) return null;
        var materials = renderer.sharedMaterials;
        return materials != null && slot >= 0 && slot < materials.Length
          ? materials[slot]
          : null;
      }

      private void ForEachMarker(string undoName,
        System.Action<ACCVariantMaterialOverride> action,
        System.Func<ACCVariantMaterialOverride, bool> predicate = null)
      {
        var selectedMarkers = targets.OfType<ACCVariantMaterialOverride>()
          .Where(selectedMarker => selectedMarker != null &&
            IsEditableSceneObject(selectedMarker.gameObject) &&
            (predicate == null || predicate(selectedMarker)))
          .ToList();
        if (selectedMarkers.Count == 0) return;

        int undoGroup = ACCEditorUndo.Begin(undoName);
        try
        {
          ACCEditorUndo.RecordObjects(selectedMarkers.Cast<Object>(), undoName);
          if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

          foreach (var selectedMarker in selectedMarkers)
          {
            action(selectedMarker);
            EditorUtility.SetDirty(selectedMarker);
          }
          ACCEditorUndo.RecordPrefabInstanceModifications(selectedMarkers.Cast<Object>());
        }
        finally
        {
          ACCEditorUndo.Complete(undoGroup);
        }
      }

      internal static void PopulateGlobalMaterialEntries(ACCVariantMaterialOverride marker)
      {
        if (marker == null || marker.OutfitBase == null) return;
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
        return AnalyzeOptimalReplacements(marker,
          marker != null ? marker.gameObject : null);
      }

      internal static int AnalyzeOptimalReplacements(
        ACCVariantMaterialOverride marker,
        GameObject variantSource)
      {
        if (marker == null || marker.OutfitBase == null || variantSource == null)
          return 0;
        if (variantSource == marker.gameObject && IsAlreadyConvertedEmptyVariant(marker))
        {
          return (marker.Replacements?.Count ?? 0) +
            (marker.RendererOverrides?.Count ?? 0);
        }

        // Start with the same complete Source list used by "Refresh Outfit
        // Materials". Automatic analysis only fills in the replacements; it
        // must not produce a shorter, structurally different list.
        PopulateGlobalMaterialEntries(marker);
        var observations = CollectMaterialSlotObservations(
          marker.OutfitBase.transform, variantSource.transform);

        if (observations.Count == 0) return -1;

        var majorityTargets = new Dictionary<Material, Material>();
        var overrides = new List<ACCVariantMaterialOverride.RendererMaterialReplacement>();
        foreach (var sourceGroup in observations
          .Where(item => item.Source != null)
          .GroupBy(item => item.Source))
        {
          // 最常见映射作为全局规则；票数相同时优先保持原材质，避免过度替换。
          var majorityTarget = sourceGroup
            .GroupBy(item => item.Target)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key == sourceGroup.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

          if (majorityTarget != sourceGroup.Key)
            majorityTargets[sourceGroup.Key] = majorityTarget;

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

        foreach (var entry in marker.Replacements ??
          new List<ACCVariantMaterialOverride.MaterialReplacement>())
        {
          if (entry == null || entry.Source == null) continue;
          entry.Replacement = majorityTargets.TryGetValue(entry.Source,
            out var replacement) ? replacement : null;
        }

        marker.RendererOverrides = overrides;
        return majorityTargets.Count + overrides.Count;
      }

      private static List<MaterialSlotObservation> CollectMaterialSlotObservations(
        Transform outfitBase,
        Transform variantSource)
      {
        var observations = new List<MaterialSlotObservation>();
        if (outfitBase == null || variantSource == null) return observations;

        var baseRenderers = BuildRendererMap(outfitBase);
        var variantRenderers = BuildRendererMap(variantSource);
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
        return observations;
      }

      private static int AnalyzeOrPopulate(ACCVariantMaterialOverride marker)
      {
        return AnalyzeOrPopulate(marker,
          marker != null ? marker.gameObject : null);
      }

      private static int AnalyzeOrPopulate(
        ACCVariantMaterialOverride marker,
        GameObject variantSource)
      {
        int result = AnalyzeOptimalReplacements(marker, variantSource);
        if (result >= 0) return result;
        PopulateGlobalMaterialEntries(marker);
        marker.RendererOverrides = new List<ACCVariantMaterialOverride.RendererMaterialReplacement>();
        return 0;
      }

      private static bool CanAnalyzeMarker(ACCVariantMaterialOverride marker)
      {
        return CanRefreshMarker(marker) &&
          !IsAlreadyConvertedEmptyVariant(marker);
      }

      private static bool CanRefreshMarker(ACCVariantMaterialOverride marker)
      {
        return marker != null && IsEditableSceneObject(marker.gameObject) &&
          marker.OutfitBase != null && marker.OutfitBase != marker.gameObject;
      }

      private static bool CanApplyAnalysisMarker(
        ACCVariantMaterialOverride marker)
      {
        return CanRefreshMarker(marker);
      }

      private static bool IsValidAnalysisSource(GameObject source)
      {
        if (source == null) return false;
        // Persistent GameObjects are read-only analysis sources here, which
        // allows a Prefab from the Project window without editing its asset.
        return EditorUtility.IsPersistent(source) ||
          IsEditableSceneObject(source);
      }

      private static bool IsAlreadyConvertedEmptyVariant(
        ACCVariantMaterialOverride marker)
      {
        if (marker == null || !marker.EditorPreviewInitialized ||
            marker.transform.childCount != 0)
          return false;

        // A converted variant deliberately keeps only Transform and the ACC
        // marker. Do not classify a normal object with a root Renderer or an
        // unrelated component as an already-converted empty variant.
        return marker.GetComponents<Component>()
          .All(component => component == null || component == marker ||
            component is Transform);
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
        ConvertObjectToVariantWithConfirmation(Selection.activeGameObject);
      }

      private static bool ConvertObjectToVariantWithConfirmation(GameObject variant)
      {
        if (!IsEditableSceneObject(variant))
        {
          EditorUtility.DisplayDialog(Localization.Text("无法转换", "Cannot Convert"),
            Localization.Text("请选择可编辑的服装变体对象。",
              "Select an editable outfit variant object."), "OK");
          return false;
        }

        var outfitBase = FindLikelyOutfitBase(variant);
        if (outfitBase == null)
        {
          EditorUtility.DisplayDialog(Localization.Text("无法转换", "Cannot Convert"),
            Localization.Text("未找到同级 Outfit Base。",
              "No sibling Outfit Base found."), "OK");
          return false;
        }
        if (!EditorUtility.DisplayDialog(Localization.Text("转换成服装变体", "Convert to Outfit Variant"),
          Localization.Text(
            $"本体：{outfitBase.name}\n来源：{variant.name}\n\n将解压来源、生成规则并清理其组件和子对象。",
            $"Base: {outfitBase.name}\nSource: {variant.name}\n\nThe source will be unpacked, analyzed, and stripped of its components and children."),
          Localization.Text("转换", "Convert"), Localization.Text("取消", "Cancel")))
          return false;

        return ConvertObjectToVariant(variant, outfitBase);
      }

      private static bool ConvertObjectToVariant(
        GameObject variant,
        GameObject outfitBase)
      {
        const string undoName = "Convert to ACC outfit variant";
        int undoGroup = ACCEditorUndo.Begin(undoName);
        try
        {
          // Unpacking is intentionally best-effort. Unity does not expose a
          // reliable scene Undo for every prefab unpack edge case. The rest
          // of this conversion is kept in the same ACC Undo group.
          UnpackPrefabCompletely(variant);

          var marker = variant.GetComponent<ACCVariantMaterialOverride>();
          if (marker == null)
          {
            marker = ACCEditorUndo.AddComponent<ACCVariantMaterialOverride>(variant,
              "Create ACC variant material override");
            ACCEditorUndo.RecordObjects(new Object[] { marker }, undoName);
          }
          else
            ACCEditorUndo.RecordObjects(new Object[] { marker }, undoName);

          marker.OutfitBase = outfitBase;
          int ruleCount = AnalyzeOrPopulate(marker);
          marker.MarkEditorPreviewInitialized();
          EditorUtility.SetDirty(marker);

          RemoveOtherComponentsAndChildren(variant, marker, undoName);
          ACCEditorUndo.RecordPrefabInstanceModifications(new Object[] { marker });
          Debug.Log(Localization.Text(
            $"[ACC] 已将 {variant.name} 转换为 {outfitBase.name} 的材质变体，生成 {ruleCount} 条最优替换规则。",
            $"[ACC] Converted {variant.name} to a material variant of {outfitBase.name}; generated {ruleCount} optimized replacement rules."), marker);
          return true;
        }
        finally
        {
          ACCEditorUndo.Complete(undoGroup);
        }
      }

      private static void UnpackPrefabCompletely(GameObject target)
      {
        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target);
        if (prefabRoot == null || !PrefabUtility.IsPartOfPrefabInstance(prefabRoot))
          return;

        PrefabUtility.UnpackPrefabInstance(prefabRoot,
          PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
      }

      private static void RemoveOtherComponentsAndChildren(
        GameObject target,
        ACCVariantMaterialOverride marker,
        string undoName)
      {
        var removableComponents = target.GetComponents<Component>()
          .Where(component => component != null && component != marker &&
            !(component is Transform))
          .ToList();
        ACCEditorUndo.RecordObjects(removableComponents.Cast<Object>(), undoName);
        foreach (var component in removableComponents)
          Undo.DestroyObjectImmediate(component);

        for (int i = target.transform.childCount - 1; i >= 0; i--)
          Undo.DestroyObjectImmediate(target.transform.GetChild(i).gameObject);
      }

      private static GameObject FindLikelyOutfitBase(GameObject variant)
      {
        var existing = variant.GetComponent<ACCVariantMaterialOverride>();
        if (existing != null && existing.OutfitBase != null &&
            existing.OutfitBase != variant &&
            existing.OutfitBase.transform.parent == variant.transform.parent)
          return existing.OutfitBase;
        if (variant.transform.parent == null) return null;

        for (int i = 0; i < variant.transform.parent.childCount; i++)
        {
          var sibling = variant.transform.parent.GetChild(i).gameObject;
          if (sibling == variant) continue;
          var siblingVariant = sibling.GetComponent<ACCVariantMaterialOverride>();
          if (siblingVariant != null && siblingVariant.OutfitBase != null) continue;
          bool explicitOutfit = sibling.GetComponent<ACCOutfitMarker>() != null;
          bool detectedOutfit = Utils.TryGetOwnedArmature(sibling.transform, out _) &&
            Utils.HasMeshInHierarchy(sibling.transform);
          if (explicitOutfit || detectedOutfit) return sibling;
        }
        return null;
      }

      [MenuItem(ConvertMenuPath, true)]
      private static bool ValidateConvertSelectedObjectsToVariant()
      {
        var selected = Selection.activeGameObject;
        return IsEditableSceneObject(selected);
      }

      private static bool IsEditableSceneObject(GameObject gameObject)
      {
        if (gameObject == null || EditorUtility.IsPersistent(gameObject) ||
            !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
          return false;
        return true;
      }
  }
}