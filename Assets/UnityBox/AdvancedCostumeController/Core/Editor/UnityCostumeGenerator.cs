using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Creates real Unity animation assets using the same scanner and builder as the avatar workflow.</summary>
  public static class UnityCostumeGenerator
  {
    public const string DefaultGeneratedFolder = "Assets/UnityBox/Generated/AdvancedCostumeControllerUnity";

    public static AnimatorController Generate(ACCConfig config, List<OutfitData> selectedOutfits = null)
    {
      if (config?.CostumesRoot == null || EditorUtility.IsPersistent(config.CostumesRoot))
        throw new ArgumentException("Choose a Costumes Root instance in a scene.");
      if (!CostumeGenerationCore.HasUsableGenerationNamespace(config.MainParameterName))
        throw new ArgumentException("Parameter Prefix must contain at least one letter or digit.");
      if (!CostumeGenerationCore.IsSafeAssetsFolder(config.GeneratedFolder))
        throw new ArgumentException("Output Folder must be a safe Assets-relative path.");
      if (config.EnableCustomMixer && !config.EnableParts)
        throw new ArgumentException("Custom Mixer requires Parts Control.");
      if (config.EnableParameterCompression && CostumeAvatarIntegration.Adapter == null)
        throw new InvalidOperationException("VRChat parameter compression is unavailable. Disable compression to generate an ordinary Unity controller.");
      var outfits = selectedOutfits ?? Scanner.FindOutfits(config.CostumesRoot);
      if (outfits.Count == 0 || outfits.Any(outfit => outfit == null))
        throw new ArgumentException("No outfits selected. Add ACC Outfit Marker to a costume root, or scan a hierarchy containing skinned meshes.");
      var choices = outfits.SelectMany(outfit => outfit.GetAllObjects()).Distinct().ToList();
      if (choices.Count == 0 || choices.Any(choice => choice == null))
        throw new ArgumentException("At least one valid costume choice is required. Refresh the scan if objects were removed.");
      choices = choices.OrderBy(choice => CostumeHierarchy.GetHierarchyPath(config.CostumesRoot, choice), StringComparer.Ordinal).ToList();
      // Unity paths cannot distinguish same-named siblings or names containing '/'.
      // Validate all scanned descendants, including inactive parts and renderers.
      var targetNames = new HashSet<(Transform Parent, string Name)>();
      foreach (var node in config.CostumesRoot.GetComponentsInChildren<Transform>(true))
      {
        if (node == config.CostumesRoot.transform) continue;
        if (node.name.Contains("/") || node.name.Length == 0)
          throw new ArgumentException("Animation target names cannot be empty or contain '/': " + node.name);
        if (!targetNames.Add((node.parent, node.name)))
          throw new ArgumentException("Same-named siblings make animation bindings ambiguous: " + node.name);
      }
      if (choices.Any(choice => !choice.transform.IsChildOf(config.CostumesRoot.transform)))
        throw new ArgumentException("Every selected outfit must be inside Costumes Root.");
      var indexMap = choices.Select((choice, index) => new { choice, index })
        .ToDictionary(item => item.choice, item => item.index);
      var defaultOutfit = Scanner.FindDefaultOutfit(outfits, config.DefaultOutfitOverride, config.CostumesRoot);
      var folder = config.GetResolvedGeneratedFolder();
      EnsureFolder(folder);
      var buildFolder = AssetDatabase.GenerateUniqueAssetPath(folder + "/Build");
      EnsureFolder(buildFolder);
      try
      {
        var controller = new AnimationBuilder(config, buildFolder).CreateController(outfits, indexMap,
          defaultOutfit, buildFolder + "/" + config.GetControllerFileName());
        AssetDatabase.SaveAssets();
        return controller;
      }
      catch
      {
        AssetDatabase.DeleteAsset(buildFolder);
        throw;
      }
    }

    private static void EnsureFolder(string path)
    {
      var parts = path.Split('/');
      var current = "Assets";
      for (int index = 1; index < parts.Length; index++)
      {
        var next = current + "/" + parts[index];
        if (!AssetDatabase.IsValidFolder(next))
        {
          if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[index])))
            throw new InvalidOperationException("Could not create output folder: " + next);
        }
        current = next;
      }
    }
  }
}
