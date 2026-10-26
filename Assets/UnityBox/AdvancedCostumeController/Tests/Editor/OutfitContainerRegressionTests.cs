using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class OutfitContainerRegressionTests
  {
    private Scene testScene;
    private Scene previousActiveScene;
    private GameObject root;
    private string temporaryAssetFolder;

    [SetUp]
    public void SetUp()
    {
      previousActiveScene = SceneManager.GetActiveScene();
      testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
      SceneManager.SetActiveScene(testScene);
      root = new GameObject("Costumes");
      temporaryAssetFolder = "Assets/__ACCRegression_" + Guid.NewGuid().ToString("N");
    }

    [TearDown]
    public void TearDown()
    {
      if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
        SceneManager.SetActiveScene(previousActiveScene);
      if (testScene.IsValid())
        EditorSceneManager.CloseScene(testScene, true);
      if (AssetDatabase.IsValidFolder(temporaryAssetFolder))
        AssetDatabase.DeleteAsset(temporaryAssetFolder);
    }

    [Test]
    public void ContainerWithAnotherIndependentOutfitIsNotAnActivationRoot()
    {
      var container = Child(root, "P");
      var outfitA = ExplicitOutfit(container, "A");
      var variantA = MaterialVariant(container, "VA", outfitA);
      var outfitB = ExplicitOutfit(container, "B");

      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(2));
      AssertOutfitRoot(outfits, outfitA, outfitA, "P/A");
      AssertOutfitRoot(outfits, outfitB, outfitB, "P/B");
      Assert.That(outfits.Single(o => o.BaseObject == outfitA).Variants,
        Is.EquivalentTo(new[] { variantA }));
    }

    [Test]
    public void TwoVariantGroupsSharingAContainerKeepSeparateRoots()
    {
      var container = Child(root, "P");
      var outfitA = ExplicitOutfit(container, "A");
      var variantA = MaterialVariant(container, "VA", outfitA);
      var outfitB = ExplicitOutfit(container, "B");
      var variantB = MaterialVariant(container, "VB", outfitB);

      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(2));
      AssertOutfitRoot(outfits, outfitA, outfitA, "P/A");
      AssertOutfitRoot(outfits, outfitB, outfitB, "P/B");
      Assert.That(outfits.Single(o => o.BaseObject == outfitA).Variants,
        Is.EquivalentTo(new[] { variantA }));
      Assert.That(outfits.Single(o => o.BaseObject == outfitB).Variants,
        Is.EquivalentTo(new[] { variantB }));
    }

    [Test]
    public void SingleBaseAndItsVariantsRetainTheirContainer()
    {
      var container = Child(root, "P");
      var outfitA = ExplicitOutfit(container, "A");
      var variantA = MaterialVariant(container, "VA", outfitA);

      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(1));
      AssertOutfitRoot(outfits, outfitA, container, "P");
      Assert.That(outfits[0].Variants, Is.EquivalentTo(new[] { variantA }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MainAndMixerMenusKeepSameNamedOutfitsInTheirRealFolders(
      bool independentMixerParameters)
    {
      var folderP = Child(root, "P");
      var outfitA = ExplicitOutfit(folderP, "A");
      MaterialVariant(folderP, "VA", outfitA);
      ExplicitOutfit(folderP, "B");
      var folderQ = Child(root, "Q");
      var outfitC = ExplicitOutfit(folderQ, "C");
      MaterialVariant(folderQ, "VC", outfitC);
      ExplicitOutfit(folderQ, "B");

      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(4));
      var config = Config();
      config.EnableParts = true;
      config.EnableCustomMixer = true;
      config.UseIndependentMixerPartParameters = independentMixerParameters;
      config.CustomMixerName = "Mix";
      var indices = ChoiceIndices(outfits);
      var menuRoot = Child(root, "ACC_Menu");
      var parameters = menuRoot.AddComponent<ModularAvatarParameters>();
      var generator = new Generator(config);

      Invoke(typeof(Generator), generator, "BuildMenus",
        menuRoot, outfits, indices, parameters, outfits[0], null);
      Assert.That(menuRoot.transform.Find("P/B"), Is.Not.Null);
      Assert.That(menuRoot.transform.Find("Q/B"), Is.Not.Null);
      Assert.That(menuRoot.transform.Find("P/A"), Is.Not.Null);
      Assert.That(menuRoot.transform.Find("Q/C"), Is.Not.Null);
      Assert.That(menuRoot.transform.Find("B"), Is.Null,
        "An organizational folder must not be dropped from the menu path.");

      // The final argument is an internal icon-request type. Icons are disabled,
      // so null allows exercising the actual Mixer without exposing internals.
      Invoke(typeof(Mixer), null, "BuildCustomMixerMenu",
        config, menuRoot, outfits, indices, Generator.GetCustomMixerValue(indices.Count),
        parameters, null, outfits[0], null);
      var mixerBInP = menuRoot.transform.Find("Mix/P/B");
      var mixerBInQ = menuRoot.transform.Find("Mix/Q/B");
      Assert.That(mixerBInP, Is.Not.Null);
      Assert.That(mixerBInQ, Is.Not.Null);
      Assert.That(mixerBInP, Is.Not.SameAs(mixerBInQ));
      var firstParameters = ControlParameters(mixerBInP);
      var secondParameters = ControlParameters(mixerBInQ);
      Assert.That(firstParameters, Has.Length.EqualTo(1));
      Assert.That(secondParameters, Has.Length.EqualTo(1));
      Assert.That(firstParameters.Intersect(secondParameters), Is.Empty,
        "Independent outfits must not share their part selection parameters.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectingSiblingRestoresTheContainerAndDisablesTheOtherOutfit(
      bool containerInitiallyActive)
    {
      var container = Child(root, "P");
      var outfitA = ExplicitOutfit(container, "A");
      MaterialVariant(container, "VA", outfitA);
      var outfitB = ExplicitOutfit(container, "B");
      container.SetActive(containerInitiallyActive);
      var outfits = Scanner.FindOutfits(root);
      var indices = ChoiceIndices(outfits);
      var clip = (AnimationClip)Invoke(typeof(AnimationBuilder),
        new AnimationBuilder(Config()), "CreateOutfitSwitchClip",
        outfits, indices.Keys.ToList(), outfitB, indices[outfitB]);

      AssertActiveCurve(clip, "P", 1f);
      AssertActiveCurve(clip, "P/B", 1f);
      AssertActiveCurve(clip, "P/A", 0f);
      clip.SampleAnimation(root, 0f);
      Assert.That(outfitB.activeInHierarchy, Is.True,
        "Enabling a child is insufficient when its shared parent is inactive.");
      Assert.That(outfitA.activeSelf, Is.False);
    }

    private ACCConfig Config()
    {
      return new ACCConfig
      {
        CostumesRoot = root,
        ParamPrefix = "ACCRegression",
        Language = ACCLanguage.English,
        GeneratedFolder = temporaryAssetFolder,
        AutoGenerateMenuIcons = false
      };
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var child = new GameObject(name);
      child.transform.SetParent(parent.transform, false);
      return child;
    }

    private static GameObject ExplicitOutfit(GameObject parent, string name)
    {
      var outfit = Child(parent, name);
      outfit.AddComponent<ACCOutfitMarker>();
      Child(outfit, "Part").AddComponent<ACCPartGroupMarker>();
      return outfit;
    }

    private static GameObject MaterialVariant(GameObject parent, string name, GameObject outfitBase)
    {
      var variant = Child(parent, name);
      variant.AddComponent<ACCVariantMaterialOverride>().OutfitBase = outfitBase;
      return variant;
    }

    private static void AssertOutfitRoot(
      IEnumerable<OutfitData> outfits, GameObject baseObject, GameObject expectedRoot,
      string expectedPath)
    {
      var outfit = outfits.Single(o => o.BaseObject == baseObject);
      Assert.That(outfit.OutfitObject, Is.SameAs(expectedRoot));
      Assert.That(outfit.Name, Is.EqualTo(expectedRoot.name));
      Assert.That(outfit.RelativePath, Is.EqualTo(expectedPath));
    }

    private static Dictionary<GameObject, int> ChoiceIndices(IEnumerable<OutfitData> outfits)
    {
      return outfits.SelectMany(o => o.GetAllObjects()).Distinct()
        .Select((obj, index) => new { obj, index })
        .ToDictionary(item => item.obj, item => item.index);
    }

    private static string[] ControlParameters(Transform menu)
    {
      return menu.GetComponentsInChildren<ModularAvatarMenuItem>(true)
        .Select(item => item.PortableControl.Parameter)
        .Where(parameter => !string.IsNullOrEmpty(parameter)).Distinct().ToArray();
    }

    private static void AssertActiveCurve(AnimationClip clip, string path, float expected)
    {
      var binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
      var curve = AnimationUtility.GetEditorCurve(clip, binding);
      Assert.That(curve, Is.Not.Null, "Missing activation curve for " + path);
      Assert.That(curve.Evaluate(0f), Is.EqualTo(expected));
    }

    private static object Invoke(Type type, object target, string methodName, params object[] arguments)
    {
      var method = type.GetMethod(methodName,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null, "Missing production method " + methodName);
      return method.Invoke(target, arguments);
    }
  }
}
