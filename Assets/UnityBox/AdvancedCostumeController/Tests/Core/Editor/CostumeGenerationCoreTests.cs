using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class CostumeGenerationCoreTests
  {
    [TestCase(1, 0, false, false)]
    [TestCase(2, 1, true, true)]
    [TestCase(3, 2, false, true)]
    [TestCase(4, 2, false, true)]
    [TestCase(5, 3, false, true)]
    [TestCase(256, 8, false, true)]
    public void ChoiceLayoutKeepsBooleanAndCompressionBoundaries(int choices, int bits, bool boolean, bool sync)
    {
      var layout = new ChoiceParameterLayout(choices, true);
      Assert.That(layout.BitCount, Is.EqualTo(bits));
      Assert.That(layout.UsesBoolean, Is.EqualTo(boolean));
      Assert.That(layout.RequiresSynchronization, Is.EqualTo(sync));
      Assert.That(layout.UsesCompression, Is.EqualTo(!boolean));
      Assert.That(layout.GetBitParameterName("ACC/Outfit", 3), Is.EqualTo("ACC/Outfit/Bits/03"));
      Assert.That(new ChoiceParameterLayout(choices, false).UsesCompression, Is.False);
    }

    [Test]
    public void StableNamesDistinguishSanitizationCollisionsAndStayBounded()
    {
      var names = new[] { "A/B", "A_B", "A B", "A-B", new string('A', 90), new string('A', 91) };
      var actual = names.Select(CostumeGenerationCore.GetStableAssetName).ToArray();
      Assert.That(actual.Distinct().Count(), Is.EqualTo(names.Length));
      Assert.That(actual.All(name => name.Length <= 97), Is.True);
      Assert.That(actual, Is.EqualTo(names.Select(CostumeGenerationCore.GetStableAssetName)));
      Assert.That(CostumeGenerationCore.GetStableAssetName("Simple123"), Is.EqualTo("Simple123"));
      Assert.That(CostumeGenerationCore.BuildParamName("ACC", "A B"),
        Is.Not.EqualTo(CostumeGenerationCore.BuildParamName("ACC", "A_B")));
    }

    [TestCase("Assets", true)]
    [TestCase("Assets/Generated", true)]
    [TestCase("Assets\\Generated/", true)]
    [TestCase("Assets/../Outside", false)]
    [TestCase("/tmp/Assets", false)]
    [TestCase("Assets//Generated", false)]
    [TestCase("Assets/./Generated", false)]
    public void GeneratedFoldersCannotEscapeAssets(string path, bool allowed)
    {
      Assert.That(CostumeGenerationCore.IsSafeAssetsFolder(path), Is.EqualTo(allowed));
      if (allowed)
        Assert.That(CostumeGenerationCore.NormalizeAssetsFolder(path), Does.Not.Contain("\\"));
    }

    [Test]
    public void InvalidDisplayRegexKeepsPrefixAndSuffixFormatting()
    {
      Assert.That(CostumeGenerationCore.FormatPartDisplayName("Prefix_Hat_End", "Prefix_", "_End", "[", ""),
        Is.EqualTo("Hat"));
      Assert.That(CostumeGenerationCore.IsValidRegex("["), Is.False);
    }

    [TestCase(2, 1, 0)]
    [TestCase(5, 4, 3)]
    public void AnimatorChoiceDefaultsAndCompressionBitsSurviveAssetReload(int choices, int selected, int bits)
    {
      string path = "Assets/__ACCChoiceTest_" + Guid.NewGuid().ToString("N") + ".controller";
      try
      {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var layout = new ChoiceParameterLayout(choices, true);
        AnimatorChoiceBuilder.AddChoiceParameters(controller, "Outfit", layout, selected);
        AnimatorChoiceBuilder.AddChoiceParameters(controller, "Outfit", layout, selected);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        Assert.That(controller.parameters, Has.Length.EqualTo(1 + bits));
        var main = controller.parameters.Single(parameter => parameter.name == "Outfit");
        Assert.That(main.type, Is.EqualTo(AnimatorControllerParameterType.Float));
        Assert.That(main.defaultFloat, Is.EqualTo(selected));
        for (int index = 0; index < bits; index++)
        {
          var parameter = controller.parameters.Single(p => p.name == layout.GetBitParameterName("Outfit", index));
          Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Bool));
          Assert.That(parameter.defaultBool, Is.EqualTo((selected & (1 << index)) != 0));
        }
      }
      finally { AssetDatabase.DeleteAsset(path); }
    }

    [Test]
    public void DiscreteFloatConditionsAndTransitionsAvoidRepeatedSelfTransitions()
    {
      var machine = new AnimatorStateMachine();
      try
      {
        var first = machine.AddState("First");
        var target = machine.AddState("Selected");
        var any = AnimatorChoiceBuilder.CreateAnyStateTransition(machine, target);
        var direct = AnimatorChoiceBuilder.CreateStateTransition(first, target);
        AnimatorChoiceBuilder.AddChoiceValueConditions(any, "Outfit", 3);
        Assert.That(any.conditions.Select(condition => condition.mode),
          Is.EqualTo(new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less }));
        Assert.That(any.conditions.Select(condition => condition.threshold), Is.EqualTo(new[] { 2.75f, 3.25f }));
        Assert.That(any.conditions.All(condition => condition.parameter == "Outfit"), Is.True);
        foreach (var transition in new[] { any, direct })
        {
          Assert.That(transition.hasExitTime, Is.False);
          Assert.That(transition.canTransitionToSelf, Is.False);
          Assert.That(transition.duration, Is.Zero);
        }
        UnityEngine.Object.DestroyImmediate(any);
        UnityEngine.Object.DestroyImmediate(direct);
        UnityEngine.Object.DestroyImmediate(first);
        UnityEngine.Object.DestroyImmediate(target);
      }
      finally { UnityEngine.Object.DestroyImmediate(machine); }
    }
  }
}
