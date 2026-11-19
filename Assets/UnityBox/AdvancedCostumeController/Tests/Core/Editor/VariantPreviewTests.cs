using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityBox.AdvancedCostumeController.Tests
{
  // AnimationMode, Selection and the save/Undo callbacks are editor-wide state.
  public sealed class VariantPreviewTests
  {
    private string folder;
    private Scene scene;
    private SceneSetup[] previousScenes;
    private bool ownsSceneContext;
    private Object[] previousSelection;
    private GameObject root;
    private GameObject outfit;
    private MeshRenderer renderer;
    private ACCVariantMaterialOverride marker;
    private Material original;
    private Material red;
    private Material blue;
    private Material untouched;
    private bool ownsExternalAnimationMode;
    private PrefabStage stage;
    private bool previousAutoSave;
    private PropertyInfo autoSaveProperty;

    [SetUp]
    public void SetUp()
    {
      Assert.That(AnimationMode.InAnimationMode(), Is.False,
        "Run in an isolated project with no existing animation preview.");
      previousSelection = Selection.objects;
      previousScenes = EditorSceneManager.GetSceneManagerSetup();
      folder = "Assets/__ACCVariantPreview_" + Guid.NewGuid().ToString("N");
      AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
      original = MaterialAsset("Original");
      red = MaterialAsset("Red");
      blue = MaterialAsset("Blue");
      untouched = MaterialAsset("Untouched");
      // Unity refuses an additive editor scene beside an untitled scene. Use
      // an owned saved empty anchor; never save an unrelated editing context.
      ownsSceneContext = true;
      var anchor = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
      Assert.That(EditorSceneManager.SaveScene(anchor, folder + "/Anchor.unity"), Is.True);
      scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
      root = new GameObject("ACC Preview Fixture");
      SceneManager.MoveGameObjectToScene(root, scene);
      outfit = Child(root, "Outfit");
      renderer = Child(outfit, "Body").AddComponent<MeshRenderer>();
      renderer.sharedMaterials = new[] { original, original };
      marker = Variant(root, outfit, "Variant", red);
      // Legacy invalid authoring data must be ignored, never normalized by preview/save.
      marker.RendererOverrides.Add(Precise(renderer, 99, blue));
    }

    [TearDown]
    public void TearDown()
    {
      ACCVariantPreview.Stop();
      if (ownsExternalAnimationMode && AnimationMode.InAnimationMode())
        AnimationMode.StopAnimationMode();
      ownsExternalAnimationMode = false;
      if (stage != null)
      {
        // Never save failed-test preview data as a side effect of leaving the stage.
        var clearDirtiness = typeof(PrefabStage).GetMethod("ClearDirtiness",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        clearDirtiness?.Invoke(stage, null);
        autoSaveProperty?.SetValue(stage, previousAutoSave);
        StageUtility.GoToMainStage();
        stage = null;
      }
      if (root != null) Undo.ClearUndo(root.transform);
      if (marker != null) Undo.ClearUndo(marker);
      if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
      if (ownsSceneContext)
      {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (previousScenes != null && previousScenes.Length > 0 &&
            previousScenes.All(item => !string.IsNullOrEmpty(item.path)))
          EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
        ownsSceneContext = false;
      }
      Selection.objects = previousSelection == null
        ? Array.Empty<Object>() : previousSelection.Where(value => value != null).ToArray();
      if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
        AssetDatabase.DeleteAsset(folder);
    }

    [Test]
    public void StartSwitchAndIdempotentStopUseRealRendererAndRestoreBeforeResolvingNextVariant()
    {
      var next = Variant(root, outfit, "Next", blue);
      next.Replacements.Add(Global(red, untouched));
      var originals = renderer.sharedMaterials;
      string configuration = EditorJsonUtility.ToJson(marker);

      Assert.That(ACCVariantPreview.CanPreview(marker), Is.True);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(AnimationMode.InAnimationMode(), Is.True);
      Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { red, red }));
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.SameAs(marker));
      Assert.That(ACCVariantPreview.CanPreview(next), Is.True);

      Assert.That(ACCVariantPreview.Start(next), Is.True);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { blue, blue }),
        "Switch must resolve against original, not the previous preview's red materials.");
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.SameAs(next));
      ACCVariantPreview.Stop();
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, originals);
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
    }

    [Test]
    public void RefreshReplacesAndClearsGlobalRulesAgainstOriginalMaterialsWithoutEndingSession()
    {
      Assert.That(ACCVariantPreview.Refresh(), Is.False);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      AssertActive(new[] { red, red });

      marker.Replacements[0].Replacement = blue;
      marker.Replacements.Add(Global(red, untouched));
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { blue, blue });

      marker.Replacements[0].Replacement = null;
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { original, original });

      marker.Replacements[0].Replacement = red;
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { red, red });

      marker.Replacements.Clear();
      marker.RendererOverrides.Clear();
      string configuration = EditorJsonUtility.ToJson(marker);
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { original, original });
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));

      // An empty rule set keeps the session available for the next edit.
      marker.Replacements.Add(Global(original, blue));
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { blue, blue });
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { blue, blue });
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, original });
      Assert.That(ACCVariantPreview.Refresh(), Is.False);
    }

    [Test]
    public void RefreshAddsMovesEditsAndRemovesPreciseRulesWithoutLeavingStaleSlots()
    {
      renderer.sharedMaterials = new[] { original, null, untouched };
      var sibling = Child(outfit, "Sibling").AddComponent<SkinnedMeshRenderer>();
      sibling.sharedMaterials = new[] { original, untouched };
      marker.RendererOverrides.Clear();
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      AssertActive(new[] { red, null, untouched });

      var precise = Precise(renderer, 0, blue);
      marker.RendererOverrides.Add(precise);
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { blue, null, untouched });

      precise.MaterialSlot = 1;
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { red, blue, untouched });

      precise.Replacement = red;
      marker.RendererOverrides.Add(Precise(renderer, 2, blue));
      marker.RendererOverrides.Add(Precise(sibling, 1, blue));
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { red, red, blue });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { red, blue }));

      marker.RendererOverrides.Remove(precise);
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { red, null, blue });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { red, blue }));

      marker.RendererOverrides.Clear();
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { red, null, untouched });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { red, untouched }));
      marker.Replacements.Clear();
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { original, null, untouched });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { original, untouched }));
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, null, untouched });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { original, untouched }));
    }

    [Test]
    public void GetOriginalMaterialsReturnsIndependentSnapshotsBeforeDuringAndAfterRefresh()
    {
      renderer.sharedMaterials = new[] { original, null, untouched };
      var before = ACCVariantPreview.GetOriginalMaterials(renderer);
      Assert.That(before, Is.EqualTo(new[] { original, null, untouched }));
      before[0] = blue;
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original, null, untouched }));
      marker.RendererOverrides.Add(Precise(renderer, 1, blue));
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      AssertActive(new[] { red, blue, untouched });

      var snapshot = ACCVariantPreview.GetOriginalMaterials(renderer);
      var another = ACCVariantPreview.GetOriginalMaterials(renderer);
      Assert.That(snapshot, Is.EqualTo(new[] { original, null, untouched }));
      Assert.That(another, Is.EqualTo(snapshot));
      Assert.That(another, Is.Not.SameAs(snapshot));
      snapshot[0] = blue;
      snapshot[1] = red;
      snapshot[2] = null;
      Assert.That(another, Is.EqualTo(new[] { original, null, untouched }));
      AssertActive(new[] { red, blue, untouched });

      marker.Replacements[0].Replacement = blue;
      marker.RendererOverrides.Clear();
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { blue, null, untouched });
      Assert.That(ACCVariantPreview.GetOriginalMaterials(renderer),
        Is.EqualTo(new[] { original, null, untouched }));
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, null, untouched });
      Assert.That(ACCVariantPreview.GetOriginalMaterials(renderer),
        Is.EqualTo(new[] { original, null, untouched }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RefreshStopsSafelyWhenOutfitBaseChanges(bool clearBase)
    {
      var otherOutfit = Child(root, "Other outfit");
      var otherRenderer = Child(otherOutfit, "Body").AddComponent<MeshRenderer>();
      otherRenderer.sharedMaterials = new[] { original, untouched };
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      AssertActive(new[] { red, red });

      marker.OutfitBase = clearBase ? null : otherOutfit;

      Assert.That(ACCVariantPreview.Refresh(), Is.False);
      AssertStopped(outfit, renderer, new[] { original, original });
      Assert.That(ACCVariantPreview.GetActiveVariant(otherOutfit), Is.Null);
      Assert.That(otherRenderer.sharedMaterials, Is.EqualTo(new[] { original, untouched }));
      Assert.That(marker.OutfitBase, Is.EqualTo(clearBase ? null : otherOutfit));
    }

    [Test]
    public void SameNamedGroupsAndVariantsUseExactOutfitIdentityAndOneEditorWideSession()
    {
      var otherParent = Child(root, "Other group");
      var otherOutfit = Child(otherParent, outfit.name);
      var otherRenderer = Child(otherOutfit, renderer.name).AddComponent<MeshRenderer>();
      otherRenderer.sharedMaterials = new[] { original, original };
      var other = Variant(otherParent, otherOutfit, marker.name, blue);

      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(ACCVariantPreview.GetActiveVariant(otherOutfit), Is.Null);
      Assert.That(ACCVariantPreview.GetActiveVariant(null), Is.Null);
      Assert.That(otherRenderer.sharedMaterials, Is.EqualTo(new[] { original, original }));
      Assert.That(ACCVariantPreview.Start(other), Is.True);
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.Null);
      Assert.That(ACCVariantPreview.GetActiveVariant(otherOutfit), Is.SameAs(other));
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original, original }));
      Assert.That(otherRenderer.sharedMaterials, Is.EqualTo(new[] { blue, blue }));
      ACCVariantPreview.Stop();
      AssertStopped(otherOutfit, otherRenderer, new[] { original, original });
    }

    [Test]
    public void GlobalRulesUseFirstMatchingSourceEvenWhenItsReplacementIsNullAndDoNotChain()
    {
      renderer.sharedMaterials = new[] { original, red, untouched, null };
      marker.Replacements.Clear();
      marker.Replacements.Add(null);
      marker.Replacements.Add(Global(original, null));
      marker.Replacements.Add(Global(original, blue));
      marker.Replacements.Add(Global(red, blue));
      marker.Replacements.Add(Global(blue, untouched));
      var originals = renderer.sharedMaterials;

      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original, blue, untouched, null }));
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, originals);
    }

    [Test]
    public void PreciseRulesOverrideNullSlotsAndGlobalsWhileInvalidAndOutsideRenderersAreIgnored()
    {
      renderer.sharedMaterials = new[] { original, null, original };
      renderer.gameObject.SetActive(false);
      var sibling = Child(outfit, "Sibling").AddComponent<SkinnedMeshRenderer>();
      sibling.sharedMaterials = new[] { original, null };
      var outside = Child(root, "Outside outfit").AddComponent<MeshRenderer>();
      outside.sharedMaterials = new[] { original, null };
      marker.RendererOverrides.Add(null);
      marker.RendererOverrides.Add(Precise(null, 0, blue));
      marker.RendererOverrides.Add(Precise(outside, 0, blue));
      marker.RendererOverrides.Add(Precise(renderer, -1, blue));
      marker.RendererOverrides.Add(Precise(renderer, 99, blue));
      marker.RendererOverrides.Add(Precise(renderer, 0, null));
      marker.RendererOverrides.Add(Precise(renderer, 0, blue));
      marker.RendererOverrides.Add(Precise(renderer, 1, blue));
      marker.RendererOverrides.Add(Precise(renderer, 1, untouched));
      marker.RendererOverrides.Add(Precise(renderer, 2, null));
      string configuration = EditorJsonUtility.ToJson(marker);

      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { blue, blue, red }),
        "Precise source is diagnostic only; first non-null replacement wins, null falls back to global.");
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { red, null }));
      Assert.That(outside.sharedMaterials, Is.EqualTo(new[] { original, null }));
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, null, original });
      Assert.That(sibling.sharedMaterials, Is.EqualTo(new[] { original, null }));
      Assert.That(outside.sharedMaterials, Is.EqualTo(new[] { original, null }));
      Assert.That(renderer.gameObject.activeSelf, Is.False);
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
    }

    [Test]
    public void InvalidMarkersAndPersistentAssetsCannotInterruptAValidPreview()
    {
      string prefabPath = folder + "/Configuration.prefab";
      var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
      var persistent = prefab.GetComponentInChildren<ACCVariantMaterialOverride>();
      var invalid = Child(root, "Invalid").AddComponent<ACCVariantMaterialOverride>();
      invalid.OutfitBase = null;
      Assert.That(ACCVariantPreview.Start(marker), Is.True);

      AssertRejected(null);
      AssertRejected(persistent);
      AssertRejected(invalid);
      invalid.OutfitBase = invalid.gameObject;
      AssertRejected(invalid);
      // The base and marker must belong to the same loaded scene.
      var otherScene = EditorSceneManager.NewPreviewScene();
      try
      {
        var foreignBase = new GameObject("Foreign base");
        SceneManager.MoveGameObjectToScene(foreignBase, otherScene);
        invalid.OutfitBase = foreignBase;
        AssertRejected(invalid);
      }
      finally { EditorSceneManager.ClosePreviewScene(otherScene); }
      invalid.OutfitBase = prefab;
      AssertRejected(invalid);
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.SameAs(marker));
      Assert.That(AnimationMode.InAnimationMode(), Is.True);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { red, red }));
    }

    [Test]
    public void ExistingForeignAnimationModeIsNeitherTakenOverNorStopped()
    {
      AnimationMode.StartAnimationMode();
      ownsExternalAnimationMode = true;
      AssertRejected(marker);
      ACCVariantPreview.Stop();
      Assert.That(AnimationMode.InAnimationMode(), Is.True);
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.Null);
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original, original }));
    }

    [UnityTest]
    public IEnumerator SelectionChangesDoNotOwnOrStopThePreview()
    {
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      foreach (var selected in new Object[] { renderer, outfit, root, null })
      {
        Selection.activeObject = selected;
        // Let actual selectionChanged and EditorApplication.update subscribers run.
        yield return null;
        yield return null;
        Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.SameAs(marker));
        Assert.That(AnimationMode.InAnimationMode(), Is.True);
        Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { red, red }));
      }
    }

    [Test]
    public void UndoAndRedoPreservePreviewWhileApplyingTheActualUserTransformEdit()
    {
      Undo.IncrementCurrentGroup();
      Undo.RecordObject(root.transform, "Preview fixture move");
      root.transform.localPosition = new Vector3(1, 2, 3);
      Undo.FlushUndoRecordObjects();
      Undo.IncrementCurrentGroup();
      string configuration = EditorJsonUtility.ToJson(marker);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);

      Undo.PerformUndo();

      Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
      AssertActive(new[] { red, red });
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
      Undo.PerformRedo();
      Assert.That(root.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
      AssertActive(new[] { red, red });
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, original });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UndoAndRedoRuleEditsAndClearsRefreshRealRendererWhileKeepingSession(bool preciseRule)
    {
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      string initialConfiguration = EditorJsonUtility.ToJson(marker);
      Undo.IncrementCurrentGroup();
      Undo.RecordObject(marker, "Edit preview material rule");
      if (preciseRule) marker.RendererOverrides.Add(Precise(renderer, 1, blue));
      else marker.Replacements[0].Replacement = blue;
      Undo.FlushUndoRecordObjects();
      Undo.IncrementCurrentGroup();
      string editedConfiguration = EditorJsonUtility.ToJson(marker);
      var editedMaterials = preciseRule ? new[] { red, blue } : new[] { blue, blue };
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(editedMaterials);

      Undo.RecordObject(marker, "Clear preview material rules");
      marker.Replacements.Clear();
      marker.RendererOverrides.Clear();
      Undo.FlushUndoRecordObjects();
      Undo.IncrementCurrentGroup();
      string emptyConfiguration = EditorJsonUtility.ToJson(marker);
      Assert.That(ACCVariantPreview.Refresh(), Is.True);
      AssertActive(new[] { original, original });

      // No explicit Refresh here: exercise Unity's real undoRedoPerformed event.
      Undo.PerformUndo();
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(editedConfiguration));
      AssertActive(editedMaterials);
      Undo.PerformUndo();
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(initialConfiguration));
      AssertActive(new[] { red, red });
      Undo.PerformRedo();
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(editedConfiguration));
      AssertActive(editedMaterials);
      Undo.PerformRedo();
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(emptyConfiguration));
      AssertActive(new[] { original, original });
      ACCVariantPreview.Stop();
      AssertStopped(outfit, renderer, new[] { original, original });
    }

    [Test]
    public void PreviewRetainsPreexistingAndNewUnrelatedDirtyEdits()
    {
      SaveCleanScene();
      EditorUtility.SetDirty(renderer);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      ACCVariantPreview.Stop();
      Assert.That(EditorUtility.IsDirty(renderer), Is.True);

      EditorUtility.ClearDirty(renderer);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      renderer.enabled = false;
      EditorUtility.SetDirty(renderer);
      root.transform.localPosition = Vector3.one;
      EditorSceneManager.MarkSceneDirty(scene);
      ACCVariantPreview.Stop();
      Assert.That(renderer.enabled, Is.False);
      Assert.That(EditorUtility.IsDirty(renderer), Is.True);
      Assert.That(scene.isDirty, Is.True);
      Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.one));
      Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original, original }));
    }

    [Test]
    public void SavingSceneStopsBeforeSerializationAndPreservesBytesMetadataConfigurationAndCleanState()
    {
      string path = SaveCleanScene();
      var source = new AssetBytes(path);
      string configuration = EditorJsonUtility.ToJson(marker);
      string rendererConfiguration = EditorJsonUtility.ToJson(renderer);
      bool markerDirty = EditorUtility.IsDirty(marker);
      bool rendererDirty = EditorUtility.IsDirty(renderer);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(scene.isDirty, Is.False);
      Assert.That(EditorUtility.IsDirty(marker), Is.EqualTo(markerDirty));
      Assert.That(EditorUtility.IsDirty(renderer), Is.EqualTo(rendererDirty));
      bool savingObserved = false;
      void OnSaving(Scene saving, string savingPath)
      {
        if (saving != scene) return;
        savingObserved = true;
        AssertStopped(outfit, renderer, new[] { original, original });
      }
      EditorSceneManager.sceneSaving += OnSaving;
      try { Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True); }
      finally { EditorSceneManager.sceneSaving -= OnSaving; }

      Assert.That(savingObserved, Is.True, "Exercise the real sceneSaving event, not a reflected callback.");
      AssertStopped(outfit, renderer, new[] { original, original });
      Assert.That(scene.isDirty, Is.False);
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
      Assert.That(EditorJsonUtility.ToJson(renderer), Is.EqualTo(rendererConfiguration));
      Assert.That(EditorUtility.IsDirty(marker), Is.EqualTo(markerDirty));
      Assert.That(EditorUtility.IsDirty(renderer), Is.EqualTo(rendererDirty));
      source.AssertUnchanged();

      Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);
      scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
      root = scene.GetRootGameObjects().Single();
      var reloaded = root.GetComponentInChildren<ACCVariantMaterialOverride>();
      AssertSavedConfiguration(root, reloaded);
      Assert.That(scene.isDirty, Is.False);
      source.AssertUnchanged();
    }

    [Test]
    public void SaveAssetsStopsPreviewWithoutSavingSceneOrChangingSerializedConfiguration()
    {
      string path = SaveCleanScene();
      var source = new AssetBytes(path);
      var materialSource = new AssetBytes(AssetDatabase.GetAssetPath(red));
      string configuration = EditorJsonUtility.ToJson(marker);
      bool markerDirty = EditorUtility.IsDirty(marker);
      bool rendererDirty = EditorUtility.IsDirty(renderer);
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(scene.isDirty, Is.False);
      // Ensure SaveAssets has real pending asset work, with no actual material-value change.
      EditorUtility.SetDirty(red);
      Assert.That(EditorUtility.IsDirty(red), Is.True);

      AssetDatabase.SaveAssets();

      AssertStopped(outfit, renderer, new[] { original, original });
      Assert.That(scene.isDirty, Is.False);
      Assert.That(EditorUtility.IsDirty(marker), Is.EqualTo(markerDirty));
      Assert.That(EditorUtility.IsDirty(renderer), Is.EqualTo(rendererDirty));
      Assert.That(EditorUtility.IsDirty(red), Is.False);
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
      source.AssertUnchanged();
      materialSource.AssertUnchanged();
    }

    [Test]
    public void SavingPrefabOutsideStageStopsPreviewAndPreservesSourceBytesAndStoredReferences()
    {
      string path = folder + "/Configuration.prefab";
      Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
      var source = new AssetBytes(path);
      string configuration = EditorJsonUtility.ToJson(marker);
      bool dirty = scene.isDirty;
      Assert.That(ACCVariantPreview.Start(marker), Is.True);
      Assert.That(scene.isDirty, Is.EqualTo(dirty));

      Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);

      AssertStopped(outfit, renderer, new[] { original, original });
      Assert.That(scene.isDirty, Is.EqualTo(dirty));
      Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(configuration));
      source.AssertUnchanged();
      var contents = PrefabUtility.LoadPrefabContents(path);
      try { AssertSavedConfiguration(contents, contents.GetComponentInChildren<ACCVariantMaterialOverride>()); }
      finally { PrefabUtility.UnloadPrefabContents(contents); }
      source.AssertUnchanged();
    }

    [Test, Category("UnityBoxGraphics")]
    public void SavingRealPrefabStageRestoresBeforePrefabSavingAndKeepsBytesAndConfiguration()
    {
      Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null,
        "Run in an isolated project, not in an existing user Prefab Stage.");
      string path = folder + "/Configuration.prefab";
      Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
      var source = new AssetBytes(path);
      autoSaveProperty = typeof(PrefabStage).GetProperty("autoSave",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      Assert.That(autoSaveProperty, Is.Not.Null);
      stage = PrefabStageUtility.OpenPrefab(path);
      Assert.That(stage, Is.Not.Null);
      previousAutoSave = (bool)autoSaveProperty.GetValue(stage);
      autoSaveProperty.SetValue(stage, false);
      var stageRoot = stage.prefabContentsRoot;
      var stageMarker = stageRoot.GetComponentInChildren<ACCVariantMaterialOverride>();
      var stageRenderer = stageMarker.OutfitBase.GetComponentInChildren<MeshRenderer>();
      string configuration = EditorJsonUtility.ToJson(stageMarker);
      bool markerDirty = EditorUtility.IsDirty(stageMarker);
      bool rendererDirty = EditorUtility.IsDirty(stageRenderer);
      Assert.That(stage.scene.isDirty, Is.False);
      Assert.That(ACCVariantPreview.Start(stageMarker), Is.True);
      Assert.That(stageRenderer.sharedMaterials, Is.EqualTo(new[] { red, red }));
      Assert.That(stage.scene.isDirty, Is.False);
      Assert.That(EditorUtility.IsDirty(stageMarker), Is.EqualTo(markerDirty));
      Assert.That(EditorUtility.IsDirty(stageRenderer), Is.EqualTo(rendererDirty));
      bool savingObserved = false;
      void OnSaving(GameObject savingRoot)
      {
        if (savingRoot != stageRoot) return;
        savingObserved = true;
        AssertStopped(stageMarker.OutfitBase, stageRenderer, new[] { original, original });
      }
      PrefabStage.prefabSaving += OnSaving;
      try
      {
        // Request an explicit real save even if native SavePrefab skips a clean stage.
        EditorSceneManager.MarkSceneDirty(stage.scene);
        var save = typeof(PrefabStage).GetMethod("SavePrefab",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          null, Type.EmptyTypes, null);
        Assert.That(save, Is.Not.Null);
        save.Invoke(stage, null);
      }
      finally { PrefabStage.prefabSaving -= OnSaving; }
      Assert.That(savingObserved, Is.True, "The real PrefabStage save pipeline must execute.");
      AssertStopped(stageMarker.OutfitBase, stageRenderer, new[] { original, original });
      Assert.That(stage.scene.isDirty, Is.False);
      Assert.That(EditorJsonUtility.ToJson(stageMarker), Is.EqualTo(configuration));
      Assert.That(EditorUtility.IsDirty(stageMarker), Is.EqualTo(markerDirty));
      Assert.That(EditorUtility.IsDirty(stageRenderer), Is.EqualTo(rendererDirty));
      AssertSavedConfiguration(stageRoot, stageMarker);
      source.AssertUnchanged();
    }

    private string SaveCleanScene()
    {
      string path = folder + "/Configuration.unity";
      Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
      Assert.That(scene.isDirty, Is.False);
      return path;
    }

    private void AssertSavedConfiguration(GameObject savedRoot, ACCVariantMaterialOverride savedMarker)
    {
      var savedBase = savedRoot.transform.Find("Outfit").gameObject;
      var savedRenderer = savedBase.GetComponentInChildren<MeshRenderer>();
      Assert.That(savedRenderer.sharedMaterials, Is.EqualTo(new[] { original, original }));
      // Inspect actual serialized authoring data, not a second implementation of rule resolution.
      var serialized = new SerializedObject(savedMarker);
      serialized.Update();
      Assert.That(serialized.FindProperty("OutfitBase").objectReferenceValue, Is.EqualTo(savedBase));
      var replacements = serialized.FindProperty("Replacements");
      Assert.That(replacements.arraySize, Is.EqualTo(1));
      var replacement = replacements.GetArrayElementAtIndex(0);
      Assert.That(replacement.FindPropertyRelative("Source").objectReferenceValue, Is.EqualTo(original));
      Assert.That(replacement.FindPropertyRelative("Replacement").objectReferenceValue, Is.EqualTo(red));
      var precise = serialized.FindProperty("RendererOverrides");
      Assert.That(precise.arraySize, Is.EqualTo(1));
      var entry = precise.GetArrayElementAtIndex(0);
      Assert.That(entry.FindPropertyRelative("TargetRenderer").objectReferenceValue, Is.EqualTo(savedRenderer));
      Assert.That(entry.FindPropertyRelative("MaterialSlot").intValue, Is.EqualTo(99));
      Assert.That(entry.FindPropertyRelative("Source").objectReferenceValue, Is.EqualTo(untouched));
      Assert.That(entry.FindPropertyRelative("Replacement").objectReferenceValue, Is.EqualTo(blue));
      Assert.That(serialized.FindProperty("editorPreviewInitialized").boolValue, Is.False);
    }

    private static void AssertRejected(ACCVariantMaterialOverride candidate)
    {
      Assert.That(ACCVariantPreview.CanPreview(candidate), Is.False);
      Assert.That(ACCVariantPreview.Start(candidate), Is.False);
    }

    private void AssertActive(Material[] expected)
    {
      Assert.That(AnimationMode.InAnimationMode(), Is.True);
      Assert.That(ACCVariantPreview.GetActiveVariant(outfit), Is.SameAs(marker));
      Assert.That(renderer.sharedMaterials, Is.EqualTo(expected));
    }

    private static void AssertStopped(GameObject targetBase, Renderer target, Material[] expected)
    {
      Assert.That(AnimationMode.InAnimationMode(), Is.False);
      Assert.That(ACCVariantPreview.GetActiveVariant(targetBase), Is.Null);
      Assert.That(target.sharedMaterials, Is.EqualTo(expected));
    }

    private Material MaterialAsset(string name)
    {
      var shader = Shader.Find("Hidden/InternalErrorShader");
      Assert.That(shader, Is.Not.Null);
      var material = new Material(shader) { name = name };
      AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
      return material;
    }

    private ACCVariantMaterialOverride Variant(GameObject parent, GameObject targetBase,
      string name, Material replacement)
    {
      var result = Child(parent, name).AddComponent<ACCVariantMaterialOverride>();
      result.OutfitBase = targetBase;
      result.Replacements.Add(Global(original, replacement));
      return result;
    }

    private static ACCVariantMaterialOverride.MaterialReplacement Global(Material source, Material replacement)
      => new ACCVariantMaterialOverride.MaterialReplacement { Source = source, Replacement = replacement };

    private ACCVariantMaterialOverride.RendererMaterialReplacement Precise(Renderer target, int slot,
      Material replacement) => new ACCVariantMaterialOverride.RendererMaterialReplacement
      {
        TargetRenderer = target, MaterialSlot = slot, Source = untouched, Replacement = replacement
      };

    private static GameObject Child(GameObject parent, string name)
    {
      var result = new GameObject(name);
      SceneManager.MoveGameObjectToScene(result, parent.scene);
      result.transform.SetParent(parent.transform, false);
      return result;
    }

    private sealed class AssetBytes
    {
      private readonly string path;
      private readonly byte[] source;
      private readonly byte[] metadata;

      internal AssetBytes(string path)
      {
        this.path = path;
        source = File.ReadAllBytes(path);
        metadata = File.ReadAllBytes(path + ".meta");
      }

      internal void AssertUnchanged()
      {
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(source), path);
        Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata), path + ".meta");
      }
    }
  }
}
