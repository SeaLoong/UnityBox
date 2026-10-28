using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Shared default-choice and mixer parameter rules for Unity and avatar generation.</summary>
  public static class CostumeChoiceResolver
  {
    public static ChoiceParameterLayout GetMainChoiceLayout(
      Dictionary<GameObject, int> outfitIndexMap, bool includeCustomMixer,
      bool usesCompression)
    {
      return new ChoiceParameterLayout(outfitIndexMap.Count + (includeCustomMixer ? 1 : 0),
        usesCompression);
    }

    public static int GetCustomMixerValue(int outfitCount)
    {
      return outfitCount;
    }

    public static GameObject ResolveDefaultChoiceObject(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      if (defaultOutfit == null) return null;
      if (defaultOutfit.DefaultChoiceObject != null &&
          outfitIndexMap.ContainsKey(defaultOutfit.DefaultChoiceObject))
        return defaultOutfit.DefaultChoiceObject;
      if (outfitIndexMap.ContainsKey(defaultOutfit.BaseObject))
        return defaultOutfit.BaseObject;

      return defaultOutfit.GetAllObjects().FirstOrDefault(outfitIndexMap.ContainsKey);
    }

    public static int ResolveDefaultChoiceIndex(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      var defaultChoice = ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      return defaultChoice != null && outfitIndexMap.TryGetValue(defaultChoice, out var index)
        ? index
        : 0;
    }

    public static int GetMixerSlotDefaultValue(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData outfit,
      MixerPartSlot slot)
    {
      if (defaultOutfit == null || outfit == null || slot == null ||
          outfit.BaseObject != defaultOutfit.BaseObject)
        return 0;

      var defaultChoice = ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      int candidateIndex = slot.Candidates.FindIndex(candidate =>
        candidate.VariantObject == defaultChoice);
      if (candidateIndex < 0) return 0;

      var normalControl = outfit.GetPartControls().FirstOrDefault(control =>
        control.Parts != null && control.Parts.Count > 0 &&
        OutfitData.GetMixerSlotKey(outfit.BaseObject, control) == slot.Key);
      bool defaultActive = normalControl != null
        ? normalControl.Parts.All(part => part != null && part.activeSelf)
        : slot.Candidates[candidateIndex].Control.Parts.All(part =>
          part != null && part.activeSelf);
      return defaultActive ? candidateIndex + 1 : 0;
    }

    public static int GetMixerVariantDefaultValue(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData outfit)
    {
      if (outfit == null || defaultOutfit == null ||
          outfit.BaseObject != defaultOutfit.BaseObject)
        return 0;

      var choices = outfit.GetAllObjects();
      var defaultChoice = ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      int index = choices.IndexOf(defaultChoice);
      return index >= 0 ? index : 0;
    }

    public static string BuildMixerSlotParamName(
      ACCConfig config, OutfitData outfit, MixerPartSlot slot)
    {
      string groupPath = CostumeHierarchy.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      string raw = ACCConfig.MixerParamPrefix + "/" + groupPath + "/" + slot.Key;
      return CostumeGenerationCore.BuildParamName(config.MainParameterName, raw);
    }

    public static string BuildMixerVariantParamName(
      ACCConfig config, OutfitData outfit)
    {
      string groupPath = CostumeHierarchy.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      string raw = ACCConfig.MixerParamPrefix + "/" + groupPath + "/Variant";
      return CostumeGenerationCore.BuildParamName(config.MainParameterName, raw);
    }
  }
}
