using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class PartSelectionRegressionTests
  {
    private GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("ACC Part Selection Tests");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    [Test]
    public void MixerUsesPreviewGroupInsteadOfOriginalScanGroups()
    {
      var first = Child(root, "First");
      var second = Child(root, "Second");
      var preview = Control("Combined", true, first, second);
      var outfit = Outfit(preview);
      outfit.VariantPartData.Add(new VariantPartData
      {
        VariantObject = root,
        PartControls = new List<PartControlData>
        {
          Control("First", false, first), Control("Second", false, second)
        }
      });

      var slot = outfit.GetMixerPartSlots().Single();
      Assert.That(slot.Name, Is.EqualTo("Combined"));
      Assert.That(slot.Candidates.Single().Control.Parts,
        Is.EquivalentTo(new[] { first, second }));
    }

    [Test]
    public void MixerDoesNotReintroduceDeselectedMembersOfAnExistingGroup()
    {
      var kept = Child(root, "Kept");
      var removed = Child(root, "Removed");
      var outfit = Outfit(Control("Group", true, kept));
      outfit.VariantPartData.Add(new VariantPartData
      {
        VariantObject = root,
        PartControls = new List<PartControlData> { Control("Group", true, kept, removed) }
      });

      Assert.That(outfit.GetMixerPartSlots().Single().Candidates.Single().Control.Parts,
        Is.EquivalentTo(new[] { kept }));
    }

    [Test]
    public void MixerDoesNotOfferAnUnselectedBaseEvenWhenItsScanDataRemains()
    {
      var part = Child(root, "Part");
      var outfit = Outfit(Control("Part", false, part));
      outfit.IsBaseSelected = false;
      outfit.VariantPartData.Add(new VariantPartData
      {
        VariantObject = root,
        PartControls = outfit.PartControls
      });
      var variant = Child(root, "Material Variant");
      variant.AddComponent<ACCVariantMaterialOverride>().OutfitBase = root;
      outfit.Variants.Add(variant);
      outfit.VariantPartData.Add(new VariantPartData { VariantObject = variant });

      Assert.That(outfit.GetMixerPartSlots().Single().Candidates.Select(c => c.VariantObject),
        Is.EquivalentTo(new[] { variant }));
    }

    [Test]
    public void EqualFormattedPartLabelsKeepBothControlsAndParameters()
    {
      var first = Child(Child(root, "Left"), "Part");
      var second = Child(Child(root, "Right"), "Part");
      var outfit = Outfit(Control("Same label", false, first),
        Control("Same label", false, second));
      var config = new ACCConfig { CostumesRoot = root, ParamPrefix = "ACC Parts" };
      var menu = Child(root, "Menu");
      var outfitMenu = Child(menu, "Outfit");
      var parameters = menu.AddComponent<ModularAvatarParameters>();
      var method = typeof(Generator).GetMethod("BuildPartsMenu",
        BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null);
      method.Invoke(new Generator(config), new object[] { menu, outfitMenu, outfit, parameters, null });

      var controls = outfitMenu.GetComponentsInChildren<ModularAvatarMenuItem>()
        .Where(item => !string.IsNullOrEmpty(item.PortableControl.Parameter)).ToArray();
      Assert.That(controls, Has.Length.EqualTo(2));
      Assert.That(controls.Select(item => item.PortableControl.Parameter).Distinct().Count(), Is.EqualTo(2));
    }

    [TestCase("Outfit/A-B", "Outfit/A B")]
    [TestCase("Outfit/A-B", "Outfit/A_B")]
    public void DistinctPartPathsDoNotShareAParameter(string first, string second)
    {
      Assert.That(Utils.BuildParamName("ACC", first),
        Is.Not.EqualTo(Utils.BuildParamName("ACC", second)));
    }

    [TestCase("Outfit/A", "Outfit_A")]
    [TestCase("A-B", "A B")]
    [TestCase("A-B", "A_B")]
    public void DistinctAssetIdentitiesDoNotCollapseToTheSameFile(string first, string second)
    {
      Assert.That(Utils.GetStableAssetName(first), Is.Not.EqualTo(Utils.GetStableAssetName(second)));
    }

    [Test]
    public void ExistingSafeNamesRemainCompatible()
    {
      Assert.That(Utils.BuildParamName("ACC", "Outfit/Parts/Hat_01"),
        Is.EqualTo("ACC/Outfit/Parts/Hat_01"));
      Assert.That(Utils.GetStableAssetName("Hat_01"), Is.EqualTo("Hat_01"));
      Assert.That(Utils.GetStableAssetName("A-B"), Is.EqualTo("A_B_77101aaa54e63e73"));
    }

    [Test]
    public void DifferentParameterNamespacesCannotDeleteTheSameOutputDirectory()
    {
      var config = new ACCConfig { CostumesRoot = root, ParamPrefix = "A-B" };
      string first = config.GetResolvedGeneratedFolder();
      config.ParamPrefix = "A B";
      Assert.That(config.GetResolvedGeneratedFolder(), Is.Not.EqualTo(first));
    }

    [Test]
    public void ConvertingWithoutMatchingMaterialsPreservesTheSource()
    {
      var outfitBase = Child(root, "Base");
      var source = Child(root, "Source");
      var sourceChild = Child(source, "Unmatched renderer");
      var renderer = sourceChild.AddComponent<MeshRenderer>();
      var method = typeof(ACCVariantMaterialOverrideEditor).GetMethod("ConvertObjectToVariant",
        BindingFlags.Static | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null);

      Assert.That(method.Invoke(null, new object[] { source, outfitBase }), Is.False);
      Assert.That(sourceChild != null && renderer != null, Is.True);
      Assert.That(source.transform.childCount, Is.EqualTo(1));
      Assert.That(source.GetComponent<ACCVariantMaterialOverride>(), Is.Null);
    }

    private OutfitData Outfit(params PartControlData[] controls) => new OutfitData
    {
      BaseObject = root,
      OutfitObject = root,
      RelativePath = "Outfit",
      PartControls = controls.ToList(),
      Parts = controls.SelectMany(control => control.Parts).ToList()
    };

    private static PartControlData Control(string name, bool group, params GameObject[] parts) =>
      new PartControlData { Name = name, IsGroup = group, Parts = parts.ToList() };

    private static GameObject Child(GameObject parent, string name)
    {
      var obj = new GameObject(name);
      obj.transform.SetParent(parent.transform, false);
      return obj;
    }
  }
}
