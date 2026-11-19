using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// One editor-wide, transient material preview. Group identity is the exact
  /// OutfitBase instance, not its name. Selection and Inspector lifetime do not
  /// own the session. Unity AnimationMode restores values without scene edits.
  /// </summary>
  [InitializeOnLoad]
  public static class ACCVariantPreview
  {
    private static ACCVariantMaterialOverride activeVariant;
    private static GameObject activeBase;
    private static bool ownsAnimationMode;
    private static AnimationModeDriver driver;
    private static readonly List<Renderer> previewRenderers = new List<Renderer>();
    private static readonly Dictionary<Renderer, Material[]> originalMaterials =
      new Dictionary<Renderer, Material[]>();

    public static ACCVariantMaterialOverride ActiveVariant
    {
      get { ValidateSession(); return activeVariant; }
    }

    /// <summary>Re-evaluate changed rules against restored originals, never the previous result.</summary>
    public static bool Refresh()
    {
      var variant = ActiveVariant;
      return variant != null && Start(variant);
    }

    /// <summary>Read-only authoring view for slot pickers while the Renderer is previewing.</summary>
    public static Material[] GetOriginalMaterials(Renderer renderer)
    {
      ValidateSession();
      if (renderer == null) return new Material[0];
      return originalMaterials.TryGetValue(renderer, out var materials)
        ? (Material[])materials.Clone() : renderer.sharedMaterials;
    }

    static ACCVariantPreview()
    {
      AssemblyReloadEvents.beforeAssemblyReload += Stop;
      EditorApplication.quitting += Stop;
      EditorApplication.playModeStateChanged += _ => Stop();
      Undo.undoRedoPerformed += OnUndoRedo;
      EditorSceneManager.sceneSaving += (_, __) => Stop();
      PrefabStage.prefabSaving += _ => Stop();
      EditorApplication.update += ValidateSession;
    }

    public static ACCVariantMaterialOverride GetActiveVariant(GameObject outfitBase)
    {
      ValidateSession();
      return outfitBase != null && outfitBase == activeBase ? activeVariant : null;
    }

    public static bool CanPreview(ACCVariantMaterialOverride variant)
    {
      ValidateSession();
      return !EditorApplication.isPlayingOrWillChangePlaymode &&
        variant != null && IsSceneObject(variant.gameObject) &&
        variant.OutfitBase != variant.gameObject && IsSceneObject(variant.OutfitBase) &&
        variant.gameObject.scene == variant.OutfitBase.scene &&
        (!AnimationMode.InAnimationMode() || ownsAnimationMode);
    }

    private static bool IsSceneObject(GameObject value)
    {
      return value != null && !EditorUtility.IsPersistent(value) &&
        value.scene.IsValid() && value.scene.isLoaded;
    }

    public static bool Start(ACCVariantMaterialOverride variant)
    {
      if (!CanPreview(variant)) return false;
      // Always restore the previous session first, even when switching within
      // the same group. Rules must be evaluated against original materials.
      Stop();
      activeVariant = variant;
      activeBase = variant.OutfitBase;
      driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
      driver.hideFlags = HideFlags.HideAndDontSave;
      driver.name = "ACC Material Variant Preview";
      AnimationMode.StartAnimationMode(driver);
      ownsAnimationMode = true;
      try
      {
        foreach (var renderer in activeBase.GetComponentsInChildren<Renderer>(true))
        {
          var materials = renderer.sharedMaterials;
          bool wasDirty = EditorUtility.IsDirty(renderer);
          var originals = (Material[])materials.Clone();
          bool changed = false;
          for (int slot = 0; slot < materials.Length; slot++)
          {
            var original = materials[slot];
            var replacement = MaterialVariantUtility.Resolve(variant, renderer, slot, original);
            if (replacement == original) continue;
            string property = $"m_Materials.Array.data[{slot}]";
            AnimationMode.AddPropertyModification(
              EditorCurveBinding.PPtrCurve("", renderer.GetType(), property),
              new PropertyModification
              {
                target = renderer,
                propertyPath = property,
                objectReference = original
              }, true);
            materials[slot] = replacement;
            changed = true;
          }
          if (changed)
          {
            previewRenderers.Add(renderer);
            originalMaterials.Add(renderer, originals);
            renderer.sharedMaterials = materials;
            // Native Renderer assignment dirties the object even in animation
            // mode (but not its Scene). Clear only this operation's dirty bit.
            if (!wasDirty) EditorUtility.ClearDirty(renderer);
          }
        }
      }
      catch
      {
        Stop();
        throw;
      }
      Repaint();
      return true;
    }

    public static void Stop()
    {
      bool wasActive = ownsAnimationMode;
      activeVariant = null;
      activeBase = null;
      ownsAnimationMode = false;
      if (!wasActive) return;
      // Preserve edits made during preview too: a Renderer already dirty now
      // must stay dirty. Only remove dirty bits produced by our own restoration.
      var cleanRenderers = previewRenderers.Where(renderer => renderer != null &&
        !EditorUtility.IsDirty(renderer)).ToArray();
      previewRenderers.Clear();
      originalMaterials.Clear();
      if (driver != null)
      {
        if (AnimationMode.InAnimationMode(driver)) AnimationMode.StopAnimationMode(driver);
        Object.DestroyImmediate(driver);
        driver = null;
      }
      foreach (var renderer in cleanRenderers)
        if (renderer != null) EditorUtility.ClearDirty(renderer);
      Repaint();
    }

    private static void OnUndoRedo()
    {
      // The Undo operation has already restored authoring fields. Keep a valid
      // session live, but a deleted marker or changed base ends it safely.
      Refresh();
    }

    private static void ValidateSession()
    {
      if (ownsAnimationMode && (activeVariant == null || activeBase == null ||
          activeVariant.OutfitBase != activeBase || driver == null || !AnimationMode.InAnimationMode(driver)))
        Stop();
    }

    private static void Repaint()
    {
      SceneView.RepaintAll();
      InternalEditorUtility.RepaintAllViews();
    }
  }

  internal static class MaterialVariantUtility
  {
    internal static Material Resolve(ACCVariantMaterialOverride marker,
      Renderer renderer, int slot, Material original)
    {
      if (marker == null) return original;
      var precise = marker.RendererOverrides?.FirstOrDefault(entry =>
        entry != null && entry.TargetRenderer == renderer &&
        entry.MaterialSlot == slot && entry.Replacement != null);
      if (precise != null) return precise.Replacement;
      var global = marker.Replacements?.FirstOrDefault(entry =>
        entry != null && entry.Source == original);
      return global != null && global.Replacement != null ? global.Replacement : original;
    }
  }

  // Also covers SaveAssets and Prefab writes outside the current Scene context.
  internal sealed class ACCVariantPreviewSaveGuard : AssetModificationProcessor
  {
    private static string[] OnWillSaveAssets(string[] paths)
    {
      ACCVariantPreview.Stop();
      return paths;
    }
  }
}
