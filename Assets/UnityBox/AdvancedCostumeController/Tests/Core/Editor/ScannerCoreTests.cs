using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class ScannerCoreTests
  {
    private GameObject root;
    private Scene testScene;
    private Scene previousScene;
    private ICostumeScanIntegration previousIntegration;

    [SetUp]
    public void SetUp()
    {
      previousIntegration = CostumeScanIntegration.Adapter;
      CostumeScanIntegration.Adapter = null;
      previousScene = SceneManager.GetActiveScene();
      testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
      SceneManager.SetActiveScene(testScene);
      root = new GameObject("Costumes");
    }

    [TearDown]
    public void TearDown()
    {
      CostumeScanIntegration.Adapter = previousIntegration;
      if (previousScene.IsValid() && previousScene.isLoaded)
        SceneManager.SetActiveScene(previousScene);
      if (testScene.IsValid()) EditorSceneManager.CloseScene(testScene, true);
    }

    [Test]
    public void ExplicitMarkersDiscoverMeshlessOutfitsWithoutAvatarPackages()
    {
      var folder = Child(root, "Accessories");
      var hat = ExplicitOutfit(folder, "Hat");
      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(1));
      Assert.That(outfits[0].BaseObject, Is.EqualTo(hat));
      Assert.That(outfits[0].RelativePath, Is.EqualTo("Accessories/Hat"));
      Assert.That(outfits[0].Marker, Is.SameAs(hat.GetComponent<ACCOutfitMarker>()));
      Assert.That(outfits[0].ArmatureObject, Is.Null);
    }

    [Test]
    public void RootBoneOwnershipFindsActualOutfitRatherThanItsFolder()
    {
      var folder = Child(root, "Folder");
      var outfit = Child(folder, "Outfit");
      var armature = Child(outfit, "Rig");
      var hips = Child(armature, "Hips");
      MeshChild(outfit, "Body", hips.transform);
      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(1));
      Assert.That(outfits[0].BaseObject, Is.EqualTo(outfit));
      Assert.That(outfits[0].OutfitObject, Is.EqualTo(outfit));
      Assert.That(outfits[0].ArmatureObject, Is.EqualTo(armature));
      Assert.That(outfits[0].Parts.Select(part => part.name), Is.EqualTo(new[] { "Body" }));
      Assert.That(CostumeHierarchy.TryGetOwnedArmature(folder.transform, out _), Is.False);
    }

    [Test]
    public void ExplicitOutfitCanUseSiblingArmatureAndMeshVariant()
    {
      var container = Child(root, "SharedRig");
      var armature = Child(container, "Rig");
      var hips = Child(armature, "Hips");
      var outfit = ExplicitOutfit(container, "Base");
      MeshChild(outfit, "Body", hips.transform);
      var variant = Child(container, "Variant");
      MeshChild(variant, "Body", hips.transform);
      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(1));
      Assert.That(outfits[0].BaseObject, Is.EqualTo(outfit));
      Assert.That(outfits[0].OutfitObject, Is.EqualTo(container));
      Assert.That(outfits[0].ArmatureObject, Is.EqualTo(armature));
      Assert.That(outfits[0].Variants, Is.EqualTo(new[] { variant }));
    }

    [Test]
    public void SharedFolderKeepsSeparateOutfitsAndOwnedMaterialVariants()
    {
      var folder = Child(root, "Folder");
      var first = ExplicitOutfit(folder, "A");
      var second = ExplicitOutfit(folder, "B");
      var variant = Child(folder, "VA");
      variant.AddComponent<ACCVariantMaterialOverride>().OutfitBase = first;
      var outfits = Scanner.FindOutfits(root);
      Assert.That(outfits, Has.Count.EqualTo(2));
      var firstData = outfits.Single(outfit => outfit.BaseObject == first);
      Assert.That(firstData.OutfitObject, Is.EqualTo(first));
      Assert.That(firstData.Variants, Is.EqualTo(new[] { variant }));
      Assert.That(firstData.GetVariantPartData(variant).Parts, Is.Empty);
      Assert.That(outfits.Single(outfit => outfit.BaseObject == second).Variants, Is.Empty);
    }

    [Test]
    public void PersistentGroupMarkersCombineControlsAndExcludeMarkedParts()
    {
      var outfit = ExplicitOutfit(root, "Outfit");
      var first = MeshChild(outfit, "Left", null);
      var second = MeshChild(outfit, "Right", null);
      var hidden = MeshChild(outfit, "Hidden", null);
      first.AddComponent<ACCPartGroupMarker>().GroupName = "Sleeves";
      second.AddComponent<ACCPartGroupMarker>().GroupName = "Sleeves";
      hidden.AddComponent<ACCPartGroupMarker>().Mode = ACCPartControlMode.Exclude;
      Scanner.CollectParts(outfit.transform, out var parts, out var excluded, out var controls);
      Assert.That(parts, Is.EquivalentTo(new[] { first, second }));
      Assert.That(excluded, Is.EqualTo(new[] { hidden }));
      Assert.That(controls, Has.Count.EqualTo(1));
      Assert.That(controls[0].Name, Is.EqualTo("Sleeves"));
      Assert.That(controls[0].IsGroup, Is.True);
      Assert.That(controls[0].Parts, Is.EquivalentTo(parts));
    }

    [Test]
    public void ExplicitDefaultVariantIsPreservedWithoutIntegration()
    {
      var folder = Child(root, "Folder");
      var outfit = ExplicitOutfit(folder, "Base");
      var variant = Child(folder, "Variant");
      variant.AddComponent<ACCVariantMaterialOverride>().OutfitBase = outfit;
      var outfits = Scanner.FindOutfits(root);
      var selected = Scanner.FindDefaultOutfit(outfits, variant, root);
      Assert.That(selected, Is.SameAs(outfits.Single()));
      Assert.That(selected.DefaultChoiceObject, Is.EqualTo(variant));
    }

    [Test]
    public void ExternalRootBoneDoesNotTurnUnrelatedMeshIntoAnOutfit()
    {
      var outfit = Child(root, "Outfit");
      var externalRig = new GameObject("ExternalRig");
      var hips = Child(externalRig, "Hips");
      MeshChild(outfit, "Body", hips.transform);
      Assert.That(CostumeHierarchy.TryGetOwnedArmature(outfit.transform, out _), Is.False);
      Assert.That(Scanner.FindOutfits(root), Is.Empty);
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var child = new GameObject(name);
      child.transform.SetParent(parent.transform, false);
      return child;
    }

    private static GameObject ExplicitOutfit(GameObject parent, string name)
    {
      var child = Child(parent, name);
      child.AddComponent<ACCOutfitMarker>();
      return child;
    }

    private static GameObject MeshChild(GameObject parent, string name, Transform rootBone)
    {
      var child = Child(parent, name);
      child.AddComponent<SkinnedMeshRenderer>().rootBone = rootBone;
      return child;
    }
  }
}
