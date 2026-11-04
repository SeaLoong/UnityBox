using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class UnityCostumeGenerationTests
  {
    private GameObject root;
    private ACCConfig config;
    private string folder;
    private Scene testScene;
    private ICostumeAvatarIntegration previousAvatarIntegration;
    private ICostumeScanIntegration previousScanIntegration;
    private readonly List<Material> materials = new List<Material>();

    [SetUp]
    public void SetUp()
    {
      previousAvatarIntegration = CostumeAvatarIntegration.Adapter;
      previousScanIntegration = CostumeScanIntegration.Adapter;
      CostumeAvatarIntegration.Adapter = null;
      CostumeScanIntegration.Adapter = null;
      testScene = EditorSceneManager.NewPreviewScene();
      root = new GameObject("Unity Costumes");
      SceneManager.MoveGameObjectToScene(root, testScene);
      folder = "Assets/__ACCGeneration_" + Guid.NewGuid().ToString("N");
      config = new ACCConfig { CostumesRoot = root, ParamPrefix = "Costumes", GeneratedFolder = folder };
    }

    [TearDown]
    public void TearDown()
    {
      CostumeAvatarIntegration.Adapter = previousAvatarIntegration;
      CostumeScanIntegration.Adapter = previousScanIntegration;
      if (root != null) UnityEngine.Object.DestroyImmediate(root);
      if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
      foreach (var material in materials)
        if (material != null && !EditorUtility.IsPersistent(material)) UnityEngine.Object.DestroyImmediate(material);
      materials.Clear();
      if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
    }

    [Test]
    public void PlainUnityScanGeneratesClipsThatActuallySwitchCostumes()
    {
      var first = Outfit("First");
      var second = Outfit("Second");
      var controller = UnityCostumeGenerator.Generate(config);
      var parameter = controller.parameters.Single(p => p.name == "Costumes");
      Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
      var firstClip = controller.animationClips.Single(clip => ActiveWeight(clip, "First") == 1f && ActiveWeight(clip, "Second") == 0f);
      first.SetActive(false);
      second.SetActive(true);
      firstClip.SampleAnimation(root, 0f);
      Assert.That(first.activeSelf, Is.True);
      Assert.That(second.activeSelf, Is.False);
      Assert.That(controller.layers.Any(layer => layer.stateMachine.defaultState != null && layer.stateMachine.defaultState.motion is BlendTree), Is.True);
    }

    [Test]
    public void RegenerationKeepsPriorAssetsAndDoesNotInstallSceneControllers()
    {
      Outfit("First");
      Outfit("Second");
      var original = UnityCostumeGenerator.Generate(config);
      var originalPath = AssetDatabase.GetAssetPath(original);
      var replacement = UnityCostumeGenerator.Generate(config);
      Assert.That(AssetDatabase.GetAssetPath(replacement), Is.Not.EqualTo(originalPath));
      Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(originalPath), Is.Not.Null);
      Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
      Assert.That(root.transform.childCount, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PartsAndBothMixerModesGenerateWithoutAvatarPackages(bool independent)
    {
      Outfit("First");
      Outfit("Second");
      config.EnableParts = true;
      config.EnableCustomMixer = true;
      config.UseIndependentMixerPartParameters = independent;
      var controller = UnityCostumeGenerator.Generate(config);
      Assert.That(controller.parameters.Any(parameter => parameter.name.Contains("/Parts/")), Is.True);
      Assert.That(controller.animationClips.Any(clip => AssetDatabase.GetAssetPath(clip).Contains("_CustomMixer.anim")), Is.True);
      Assert.That(controller.layers.Length, Is.GreaterThanOrEqualTo(3));
      Assert.That(controller.layers.SelectMany(layer => layer.stateMachine.states)
        .SelectMany(state => state.state.behaviours), Is.Empty, "Ordinary Unity generation must not create SDK drivers.");
      if (independent)
        Assert.That(controller.parameters.Any(parameter => parameter.name.Contains("/Mixer/")), Is.True);
    }

    [Test]
    public void MaterialVariantWritesOriginalAndReplacementObjectCurves()
    {
      var outfit = Outfit("First");
      var variant = Child(root, "Red Variant");
      var shader = Shader.Find("Hidden/InternalErrorShader");
      Assert.That(shader, Is.Not.Null);
      var original = new Material(shader); materials.Add(original);
      var replacement = new Material(shader); materials.Add(replacement);
      AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
      AssetDatabase.CreateAsset(original, folder + "/Original.mat");
      AssetDatabase.CreateAsset(replacement, folder + "/Replacement.mat");
      outfit.GetComponentInChildren<MeshRenderer>().sharedMaterials = new[] { original };
      var marker = variant.AddComponent<ACCVariantMaterialOverride>();
      marker.OutfitBase = outfit;
      marker.Replacements.Add(new ACCVariantMaterialOverride.MaterialReplacement { Source = original, Replacement = replacement });
      var data = new OutfitData { BaseObject = outfit, OutfitObject = outfit, Name = "First", RelativePath = "First", Variants = new List<GameObject> { variant } };
      var controller = UnityCostumeGenerator.Generate(config, new List<OutfitData> { data });
      var curves = controller.animationClips.SelectMany(clip => AnimationUtility.GetObjectReferenceCurveBindings(clip)
        .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding))).ToArray();
      Assert.That(curves.Any(key => key.value == original), Is.True);
      Assert.That(curves.Any(key => key.value == replacement), Is.True);
    }

    [Test]
    public void CompressionWithoutRealIntegrationFailsBeforeCreatingAssets()
    {
      Outfit("First");
      config.EnableParameterCompression = true;
      Assert.Throws<InvalidOperationException>(() => UnityCostumeGenerator.Generate(config));
      Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
    }

    [Test]
    public void AmbiguousSiblingBindingsFailBeforeCreatingAssets()
    {
      Outfit("Same");
      Outfit("Same");
      Assert.Throws<ArgumentException>(() => UnityCostumeGenerator.Generate(config));
      Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
    }

    private GameObject Outfit(string name)
    {
      var outfit = Child(root, name);
      outfit.AddComponent<ACCOutfitMarker>();
      var part = Child(outfit, "Part");
      part.AddComponent<MeshFilter>();
      part.AddComponent<MeshRenderer>();
      return outfit;
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var child = new GameObject(name);
      SceneManager.MoveGameObjectToScene(child, parent.scene);
      child.transform.SetParent(parent.transform, false);
      return child;
    }

    private static float? ActiveWeight(AnimationClip clip, string path)
    {
      foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        if (binding.path == path && binding.propertyName == "m_IsActive")
          return AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
      return null;
    }
  }
}
