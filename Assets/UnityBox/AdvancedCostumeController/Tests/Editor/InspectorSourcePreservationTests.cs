using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public sealed class InspectorSourcePreservationTests
  {
    private string folder;
    private string prefabPath;
    private GameObject contents;
    private GameObject sourcePrefab;
    private Material original;
    private Material replacement;
    private Material stored;
    private ACCVariantMaterialOverride emptyMarker;
    private ACCVariantMaterialOverride configuredMarker;
    private ACCPartGroupMarker excludedMarker;
    private readonly List<UnityEditor.Editor> inspectors = new List<UnityEditor.Editor>();
    private ACCInspectorTestHost host;

    [SetUp]
    public void SetUp()
    {
      folder = "Assets/__ACCInspectorPreservation_" + Guid.NewGuid().ToString("N");
      AssetDatabase.CreateFolder("Assets", folder.Substring(7));
      original = CreateMaterial("Original", Color.red);
      replacement = CreateMaterial("Replacement", Color.green);
      stored = CreateMaterial("Stored user replacement", Color.blue);
      var scene = EditorSceneManager.NewPreviewScene();
      try
      {
        var root = new GameObject("ACC Inspector Source");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var outfitBase = Child(root, "Base");
        var renderer = Child(outfitBase, "Body").AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { original, original };
        outfitBase.AddComponent<ACCOutfitMarker>().PartNameRegexPattern = "^(.*)$";
        var empty = Child(root, "Uninitialized variant");
        Child(empty, "Body").AddComponent<MeshRenderer>().sharedMaterials = new[] { replacement, replacement };
        empty.AddComponent<ACCVariantMaterialOverride>().OutfitBase = outfitBase;
        var configured = Child(root, "Configured variant");
        var marker = configured.AddComponent<ACCVariantMaterialOverride>();
        marker.OutfitBase = outfitBase;
        marker.Replacements.Add(new ACCVariantMaterialOverride.MaterialReplacement
        {
          Source = original,
          Replacement = stored
        });
        marker.RendererOverrides.Add(new ACCVariantMaterialOverride.RendererMaterialReplacement
        {
          TargetRenderer = renderer,
          MaterialSlot = 9,
          Source = original,
          Replacement = stored
        });
        var excluded = Child(outfitBase, "Excluded part").AddComponent<ACCPartGroupMarker>();
        excluded.Mode = ACCPartControlMode.Exclude;
        excluded.GroupName = "Preserve this group name";
        prefabPath = folder + "/Configuration.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

        var external = new GameObject("External analysis source");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(external, scene);
        Child(external, "Body").AddComponent<MeshRenderer>().sharedMaterials = new[] { replacement, replacement };
        string sourcePath = folder + "/External.prefab";
        PrefabUtility.SaveAsPrefabAsset(external, sourcePath);
        sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
      }
      finally { EditorSceneManager.ClosePreviewScene(scene); }
      contents = PrefabUtility.LoadPrefabContents(prefabPath);
      emptyMarker = contents.transform.Find("Uninitialized variant").GetComponent<ACCVariantMaterialOverride>();
      configuredMarker = contents.transform.Find("Configured variant").GetComponent<ACCVariantMaterialOverride>();
      excludedMarker = contents.transform.Find("Base/Excluded part").GetComponent<ACCPartGroupMarker>();
    }

    [TearDown]
    public void TearDown()
    {
      if (host != null) host.Close();
      foreach (var inspector in inspectors)
        if (inspector != null) Object.DestroyImmediate(inspector);
      inspectors.Clear();
      if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
      AssetDatabase.DeleteAsset(folder);
    }

    private Material CreateMaterial(string name, Color color)
    {
      var shader = Shader.Find("Standard");
      Assert.That(shader, Is.Not.Null);
      var material = new Material(shader) { name = name, color = color };
      AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
      return material;
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var result = new GameObject(name);
      result.transform.SetParent(parent.transform, false);
      return result;
    }

    private UnityEditor.Editor OpenInspector(Object target)
    {
      var inspector = UnityEditor.Editor.CreateEditor(target);
      inspectors.Add(inspector);
      return inspector;
    }

    private static object Invoke(object instance, string method, params object[] arguments)
    {
      var member = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(member, Is.Not.Null, method);
      return member.Invoke(instance, arguments);
    }

    private string Snapshot() => string.Join("\n", new Object[] { emptyMarker, configuredMarker, excludedMarker }
      .Select(item => EditorJsonUtility.ToJson(item)));

    [UnityTest]
    public IEnumerator OpeningSavedPrefabInspectorsDoesNotInitializeOrRewriteConfigurations()
    {
      string before = Snapshot();
      byte[] bytes = File.ReadAllBytes(prefabPath);
      byte[] metadata = File.ReadAllBytes(prefabPath + ".meta");
      bool dirty = contents.scene.isDirty;
      bool emptyDirty = EditorUtility.IsDirty(emptyMarker);
      bool configuredDirty = EditorUtility.IsDirty(configuredMarker);
      OpenInspector(emptyMarker);
      OpenInspector(configuredMarker);
      OpenInspector(excludedMarker);
      for (int frame = 0; frame < 4; frame++) yield return null;
      Assert.That(Snapshot(), Is.EqualTo(before));
      Assert.That(emptyMarker.EditorPreviewInitialized, Is.False);
      Assert.That(emptyMarker.Replacements, Is.Empty);
      Assert.That(contents.scene.isDirty, Is.EqualTo(dirty));
      Assert.That(EditorUtility.IsDirty(emptyMarker), Is.EqualTo(emptyDirty));
      Assert.That(EditorUtility.IsDirty(configuredMarker), Is.EqualTo(configuredDirty));
      AssetDatabase.SaveAssets();
      Assert.That(File.ReadAllBytes(prefabPath), Is.EqualTo(bytes));
      Assert.That(File.ReadAllBytes(prefabPath + ".meta"), Is.EqualTo(metadata));
    }

    [Test]
    public void SelectingAnalysisSourceIsReadOnlyAndExplicitApplySupportsUndoRedo()
    {
      string before = Snapshot();
      byte[] bytes = File.ReadAllBytes(prefabPath);
      bool dirty = contents.scene.isDirty;
      var inspector = OpenInspector(configuredMarker);
      Invoke(inspector, "SelectAnalysisSource", sourcePrefab);
      Assert.That(Snapshot(), Is.EqualTo(before));
      Assert.That(contents.scene.isDirty, Is.EqualTo(dirty));
      Assert.That(configuredMarker.EditorPreviewInitialized, Is.False);

      Assert.That(Invoke(inspector, "ApplyAnalysisSource", sourcePrefab), Is.True);
      Assert.That(configuredMarker.Replacements, Has.Count.EqualTo(1));
      Assert.That(configuredMarker.Replacements[0].Source, Is.EqualTo(original));
      Assert.That(configuredMarker.Replacements[0].Replacement, Is.EqualTo(replacement));
      Assert.That(configuredMarker.RendererOverrides, Is.Empty);
      Assert.That(configuredMarker.EditorPreviewInitialized, Is.True);
      string applied = Snapshot();
      Undo.FlushUndoRecordObjects();
      Undo.PerformUndo();
      Assert.That(Snapshot(), Is.EqualTo(before));
      Undo.PerformRedo();
      Assert.That(Snapshot(), Is.EqualTo(applied));
      Assert.That(File.ReadAllBytes(prefabPath), Is.EqualTo(bytes));
    }

    [UnityTest, Category("UnityBoxGraphics")]
    public IEnumerator RepaintingSavedPrefabInspectorsKeepsInvalidSlotAndExcludedGroupName()
    {
      string before = Snapshot();
      byte[] bytes = File.ReadAllBytes(prefabPath);
      byte[] metadata = File.ReadAllBytes(prefabPath + ".meta");
      bool dirty = contents.scene.isDirty;
      host = ScriptableObject.CreateInstance<ACCInspectorTestHost>();
      host.Inspectors = new[] { OpenInspector(emptyMarker), OpenInspector(configuredMarker), OpenInspector(excludedMarker) };
      host.position = new Rect(40, 40, 640, 900);
      host.ShowUtility();
      host.Repaint();
      for (int frame = 0; frame < 4; frame++) yield return null;
      host.SendEvent(new Event { type = EventType.Layout });
      host.SendEvent(new Event { type = EventType.Repaint });
      Assert.That(host.DrawCount, Is.GreaterThan(0), "The native EditorWindow must actually invoke the custom inspectors.");
      Assert.That(configuredMarker.RendererOverrides.Single().MaterialSlot, Is.EqualTo(9));
      Assert.That(configuredMarker.RendererOverrides.Single().Source, Is.EqualTo(original));
      Assert.That(excludedMarker.GroupName, Is.EqualTo("Preserve this group name"));
      Assert.That(Snapshot(), Is.EqualTo(before));
      Assert.That(contents.scene.isDirty, Is.EqualTo(dirty));
      AssetDatabase.SaveAssets();
      Assert.That(File.ReadAllBytes(prefabPath), Is.EqualTo(bytes));
      Assert.That(File.ReadAllBytes(prefabPath + ".meta"), Is.EqualTo(metadata));
    }
  }
}
