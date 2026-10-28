using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Combines generated renderer controllers into one Animator-ready asset.</summary>
public static class BlendshapeControllerComposer
{
  public static AnimatorController Combine(IReadOnlyList<AnimatorController> sources, string outputPath)
  {
    if (sources == null || sources.Count == 0)
      throw new ArgumentException("Select at least one generated controller.", nameof(sources));

    if (sources.Any(source => string.Equals(AssetDatabase.GetAssetPath(source), outputPath, StringComparison.OrdinalIgnoreCase)))
      throw new ArgumentException("Choose a combined output path different from the input controller assets.", nameof(outputPath));

    var parameters = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
    var bindings = new Dictionary<(string Path, Type Type, string Property), int>();
    var layers = new List<AnimatorControllerLayer>();
    var layerNames = new HashSet<string>(StringComparer.Ordinal);
    for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
    {
      var source = sources[sourceIndex];
      foreach (var parameter in source.parameters)
      {
        if (parameters.TryGetValue(parameter.name, out var existing))
        {
          if (existing.type != parameter.type || existing.defaultBool != parameter.defaultBool ||
              existing.defaultInt != parameter.defaultInt || existing.defaultFloat != parameter.defaultFloat)
            throw new ArgumentException("Controllers declare different types or defaults for parameter: " + parameter.name);
        }
        else parameters.Add(parameter.name, parameter);
      }
      foreach (var binding in source.animationClips.Distinct().SelectMany(AnimationUtility.GetCurveBindings))
      {
        var key = (binding.path, binding.type, binding.propertyName);
        if (bindings.TryGetValue(key, out var owner) && owner != sourceIndex)
          throw new ArgumentException("Two controllers animate the same binding: " + binding.path + "/" + binding.propertyName);
        bindings[key] = sourceIndex;
      }

      var sourceLayers = source.layers;
      int offset = layers.Count;
      foreach (var layer in sourceLayers)
      {
        string name = source.name + "/" + layer.name;
        string candidate = name;
        for (int suffix = 2; !layerNames.Add(candidate); suffix++) candidate = name + " (" + suffix + ")";
        layers.Add(new AnimatorControllerLayer
        {
          name = candidate,
          avatarMask = layer.avatarMask,
          blendingMode = layer.blendingMode,
          defaultWeight = layer.defaultWeight,
          iKPass = layer.iKPass,
          stateMachine = layer.stateMachine,
          syncedLayerIndex = layer.syncedLayerIndex < 0 ? -1 : offset + layer.syncedLayerIndex,
          syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming
        });
      }
    }

    // States, clips and BlendTrees remain persistent assets in their renderer Build folders.
    // Regeneration creates a new Build, so an existing combined controller keeps its references.
    var combined = AnimatorController.CreateAnimatorControllerAtPath(outputPath);
    combined.parameters = parameters.Values.ToArray();
    combined.layers = layers.ToArray();
    EditorUtility.SetDirty(combined);
    AssetDatabase.SaveAssets();
    return combined;
  }
}
