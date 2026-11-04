using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  public sealed class UnityCostumeWindow : EditorWindow
  {
    private readonly ACCConfig config = new ACCConfig { GeneratedFolder = UnityCostumeGenerator.DefaultGeneratedFolder };
    private List<OutfitData> outfits = new List<OutfitData>();
    private readonly HashSet<GameObject> selected = new HashSet<GameObject>();
    private Vector2 scroll;
    private AnimatorController generated;

    [MenuItem("Tools/UnityBox/Advanced Costume Controller (Unity Assets)")]
    public static void ShowWindow()
    {
      GetWindow<UnityCostumeWindow>("ACC Unity Assets");
    }

    private void OnGUI()
    {
      EditorGUILayout.LabelField("Advanced Costume Controller — Unity Assets", EditorStyles.boldLabel);
      EditorGUILayout.HelpBox("Scan costumes and generate the same switching, part, mixer and material animations used by ACC. Assign the generated Controller to an Animator on Costumes Root. This entry generates assets; avatar menus and armature setup use the avatar workflow with VRChat Avatars SDK 3.7.3+ and Modular Avatar 1.18+.", MessageType.Info);
      var root = (GameObject)EditorGUILayout.ObjectField("Costumes Root", config.CostumesRoot, typeof(GameObject), true);
      if (root != config.CostumesRoot)
      {
        config.CostumesRoot = root;
        config.ApplyAutoDefaultsFromRoot();
        outfits.Clear();
        selected.Clear();
      }
      config.ParamPrefix = EditorGUILayout.TextField("Parameter Prefix", config.ParamPrefix);
      config.GeneratedFolder = EditorGUILayout.TextField("Output Folder", config.GeneratedFolder);
      config.DefaultOutfitOverride = (GameObject)EditorGUILayout.ObjectField("Default Outfit / Variant", config.DefaultOutfitOverride, typeof(GameObject), true);
      config.EnableParts = EditorGUILayout.Toggle("Parts Control", config.EnableParts);
      using (new EditorGUI.DisabledScope(!config.EnableParts))
      {
        config.EnableCustomMixer = EditorGUILayout.Toggle("Custom Mixer", config.EnableCustomMixer && config.EnableParts);
        config.UseIndependentMixerPartParameters = EditorGUILayout.Toggle("Independent Mixer Parts", config.UseIndependentMixerPartParameters);
      }
      if (CostumeAvatarIntegration.Adapter == null)
      {
        config.EnableParameterCompression = false;
        EditorGUILayout.HelpBox("Ordinary Unity controllers use local Animator parameters. VRChat network compression requires the real SDK integration and is unavailable in this project.", MessageType.Info);
      }
      else
      {
        config.EnableParameterCompression = EditorGUILayout.Toggle("VRChat Parameter Compression", config.EnableParameterCompression);
        if (config.EnableParameterCompression)
          EditorGUILayout.HelpBox("Compression adds VRChat Parameter Drivers; this output requires VRChat at runtime. Use the avatar workflow to install and synchronize its expression parameters.", MessageType.Warning);
      }
      using (new EditorGUI.DisabledScope(config.CostumesRoot == null))
      {
        if (GUILayout.Button("Scan / Refresh"))
        {
          outfits = Scanner.FindOutfits(config.CostumesRoot);
          selected.Clear();
          foreach (var outfit in outfits) selected.Add(outfit.BaseObject);
        }
      }
      scroll = EditorGUILayout.BeginScrollView(scroll);
      foreach (var outfit in outfits)
      {
        bool enabled = EditorGUILayout.ToggleLeft(outfit.Name + " (" + outfit.GetAllObjects().Count + " choices)", selected.Contains(outfit.BaseObject));
        if (enabled) selected.Add(outfit.BaseObject); else selected.Remove(outfit.BaseObject);
        EditorGUILayout.LabelField(outfit.RelativePath ?? "", EditorStyles.miniLabel);
      }
      EditorGUILayout.EndScrollView();
      using (new EditorGUI.DisabledScope(selected.Count == 0))
      {
        if (GUILayout.Button("Generate Unity Assets"))
        {
          try
          {
            generated = UnityCostumeGenerator.Generate(config, outfits.Where(outfit => selected.Contains(outfit.BaseObject)).ToList());
            Selection.activeObject = generated;
            EditorGUIUtility.PingObject(generated);
          }
          catch (Exception exception)
          {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Cannot generate", exception.Message, "OK");
          }
        }
      }
      if (generated != null)
      {
        EditorGUILayout.ObjectField("Generated Controller", generated, typeof(AnimatorController), false);
        EditorGUILayout.HelpBox("Drive the listed Animator parameters from your game's UI or scripts. Choice/part parameters are Float values used by blend trees; switching choices uses consecutive integer values starting at 0. Each generation creates a new Build folder and preserves previous assets.", MessageType.Info);
      }
    }
  }
}
