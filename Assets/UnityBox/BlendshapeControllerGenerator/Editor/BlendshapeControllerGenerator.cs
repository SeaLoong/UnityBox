using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class BlendshapeControllerGenerator : EditorWindow
{
  private enum BlendParamType { Float, Bool }

  [Serializable]
  private struct CurveData { public Keyframe[] keys; }

  private GameObject avatarRoot;
  private Vector2 meshListScroll;
  private string blendshapePropertyPrefix = "blendShape.";
  private string outputFolder = "Assets/UnityBox/Generated/BlendshapeControllerGenerator";
  private string parameterPrefix = "BS_";
  private const string EditorPrefKey_ParameterPrefix = "BlendshapeControllerGenerator.ParameterPrefix";

  [SerializeField] private AnimationCurve floatCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
  [SerializeField] private float floatCurveDuration = 1f;
  [SerializeField] private bool floatCurveLoop = false;
  [SerializeField] private float floatValueScale = 100f;
  private const string EditorPrefKey_FloatCurve = "BlendshapeControllerGenerator.FloatCurve";
  private const string EditorPrefKey_FloatCurveDuration = "BlendshapeControllerGenerator.FloatCurveDuration";
  private const string EditorPrefKey_FloatCurveLoop = "BlendshapeControllerGenerator.FloatCurveLoop";
  private const string EditorPrefKey_FloatValueScale = "BlendshapeControllerGenerator.FloatValueScale";

  private BlendParamType batchParamType = BlendParamType.Float;
  private bool batchBoolInvert = false;
  private float batchBoolOff = 0f;
  private float batchBoolOn = 100f;
  private string controllerName = "";
  private string pendingGenerationFolder;
  private bool installAvatarControls = true;
  private AnimatorController lastMeshController;
  public AnimatorController GeneratedController { get; private set; }

  private class MeshEntry
  {
    public SkinnedMeshRenderer renderer;
    public List<string> blendshapeNames = new();
    public List<bool> selected = new();
    public List<BlendParamType> paramTypes = new();
    public List<bool> boolInverts = new();
    public List<float> boolOffValues = new();
    public List<float> boolOnValues = new();
    public bool enabled = true;
    public bool foldout = false;
    public string prefix = "";
    public Vector2 scroll = Vector2.zero;
  }

  private List<MeshEntry> meshEntries = new List<MeshEntry>();
  private string scanFilter = "";
  private bool scanIncludeInactive = true;

  [MenuItem("Tools/UnityBox/Blendshape Controller Generator")]
  public static void ShowWindow()
  {
    GetWindow<BlendshapeControllerGenerator>("Blendshape Controller Generator");
  }

  private void OnGUI()
  {
    EditorGUILayout.LabelField("Blendshape Controller Generator", EditorStyles.boldLabel);

    avatarRoot = (GameObject)EditorGUILayout.ObjectField("Animation Root", avatarRoot, typeof(GameObject), true);

    EditorGUILayout.HelpBox("Bindings are relative to the Animation Root. Targets must be under this root.", MessageType.Info);
    if (BlendshapeAvatarIntegration.Installer != null)
      installAvatarControls = EditorGUILayout.Toggle("Install MA Controls", installAvatarControls);
    EditorGUILayout.HelpBox(!installAvatarControls || BlendshapeAvatarIntegration.Installer == null
      ? "Unity asset generation: assign the generated controller to an Animator on the Animation Root. Install VRChat Avatars SDK 3.7.3+ and Modular Avatar 1.13+ to also create avatar menus and FX installation."
      : "Modular Avatar integration active: generation also installs expression menus, parameters and the FX controller on the Animation Root.", MessageType.Info);

    EditorGUILayout.BeginHorizontal();
    EditorGUILayout.LabelField("Output Folder", GUILayout.MaxWidth(80));
    outputFolder = EditorGUILayout.TextField(outputFolder);
    if (GUILayout.Button("Browse", GUILayout.MaxWidth(80)))
    {
      var folder = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
      if (!string.IsNullOrEmpty(folder))
      {
        if (folder == Application.dataPath || folder.StartsWith(Application.dataPath + "/", StringComparison.Ordinal))
          outputFolder = "Assets" + folder.Substring(Application.dataPath.Length);
        else
          EditorUtility.DisplayDialog("Invalid folder", "Please select a folder inside this Unity project's Assets folder.", "OK");
      }
    }
    EditorGUILayout.EndHorizontal();

    controllerName = EditorGUILayout.TextField("Controller FileName", controllerName);
    blendshapePropertyPrefix = EditorGUILayout.TextField("Blendshape Property Prefix", blendshapePropertyPrefix);
    string newParamPrefix = EditorGUILayout.TextField("Parameter Prefix", parameterPrefix);
    if (newParamPrefix != parameterPrefix)
    {
      parameterPrefix = newParamPrefix ?? string.Empty;
      EditorPrefs.SetString(EditorPrefKey_ParameterPrefix, parameterPrefix);
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Float Curve Settings", EditorStyles.boldLabel);
    var newCurve = EditorGUILayout.CurveField("Curve (0..1 -> 0..1)", floatCurve);
    if (newCurve != floatCurve && newCurve != null)
    {
      floatCurve = newCurve;
      try
      {
        var data = new CurveData { keys = floatCurve.keys };
        EditorPrefs.SetString(EditorPrefKey_FloatCurve, JsonUtility.ToJson(data));
      }
      catch { }
    }
    var newDuration = EditorGUILayout.FloatField("Duration (s)", Mathf.Max(0.0001f, floatCurveDuration));
    if (!Mathf.Approximately(newDuration, floatCurveDuration))
    {
      floatCurveDuration = Mathf.Max(0.0001f, newDuration);
      EditorPrefs.SetFloat(EditorPrefKey_FloatCurveDuration, floatCurveDuration);
    }
    var newLoop = EditorGUILayout.Toggle("Loop Time", floatCurveLoop);
    if (newLoop != floatCurveLoop)
    {
      floatCurveLoop = newLoop;
      EditorPrefs.SetInt(EditorPrefKey_FloatCurveLoop, floatCurveLoop ? 1 : 0);
    }
    var newScale = EditorGUILayout.FloatField("Value Scale", floatValueScale);
    if (!Mathf.Approximately(newScale, floatValueScale))
    {
      floatValueScale = newScale;
      EditorPrefs.SetFloat(EditorPrefKey_FloatValueScale, floatValueScale);
    }

    EditorGUILayout.Space();

    if (avatarRoot != null)
    {
      EditorGUILayout.BeginHorizontal();
      scanFilter = EditorGUILayout.TextField(scanFilter, GUILayout.MinWidth(120));
      scanIncludeInactive = EditorGUILayout.ToggleLeft("Include Inactive", scanIncludeInactive, GUILayout.Width(120));

      if (GUILayout.Button("Scan", GUILayout.Width(70)))
      {
        RefreshMeshes(scanIncludeInactive, string.IsNullOrWhiteSpace(scanFilter) ? null : scanFilter);
        for (int i = 0; i < meshEntries.Count; i++)
        {
          var e = meshEntries[i];
          e.prefix = e.renderer != null ? e.renderer.gameObject.name + "_" : "mesh_" + i + "_";
        }
      }
      EditorGUILayout.EndHorizontal();

      EditorGUILayout.HelpBox("Scan Filter: the filter matches GameObject names under the Animation Root. Leave empty for no filtering.", MessageType.Info);

      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Meshes to generate (choose from the Animation Root or add manually)", EditorStyles.label);

      if (GUILayout.Button("Add", GUILayout.Width(60)))
      {
        var e = new MeshEntry();
        e.renderer = null;
        e.blendshapeNames = new List<string>();
        e.selected = new List<bool>();
        e.paramTypes = new List<BlendParamType>();
        e.boolInverts = new List<bool>();
        e.boolOffValues = new List<float>();
        e.boolOnValues = new List<float>();
        e.enabled = true;
        e.foldout = false;
        e.prefix = "mesh_" + meshEntries.Count + "_";
        e.scroll = Vector2.zero;
        meshEntries.Add(e);
      }

      if (GUILayout.Button("Clear", GUILayout.Width(60)))
      {
        meshEntries.Clear();
      }

      EditorGUILayout.EndHorizontal();
    }

    if (avatarRoot != null)
    {
      meshListScroll = EditorGUILayout.BeginScrollView(meshListScroll, GUILayout.MaxHeight(800), GUILayout.ExpandHeight(false));
      for (int mi = 0; mi < meshEntries.Count; mi++)
      {
        var entry = meshEntries[mi];
        var mr = entry.renderer;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.MaxWidth(18));
        var newMr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(mr, typeof(SkinnedMeshRenderer), true);
        string relPath = null;
        if (newMr != null && avatarRoot != null)
          relPath = GetRelativePath(avatarRoot.transform, newMr.transform);
        if (newMr != null && relPath == null)
          relPath = GetTransformPath(newMr.transform);
        if (newMr != mr)
        {
          if (newMr != null && meshEntries.Where((existing, idx) => idx != mi && existing.renderer == newMr).Any())
          {
            EditorUtility.DisplayDialog("Duplicate Mesh", "This SkinnedMeshRenderer is already added in the list. Please select a different mesh.", "OK");
          }
          else
          {
            entry.renderer = newMr;
            var names = new List<string>();
            var sels = new List<bool>();
            if (newMr != null && newMr.sharedMesh != null)
            {
              var types = new List<BlendParamType>();
              var inverts = new List<bool>();
              var offs = new List<float>();
              var ons = new List<float>();
              for (int bi = 0; bi < newMr.sharedMesh.blendShapeCount; bi++)
              {
                names.Add(newMr.sharedMesh.GetBlendShapeName(bi));
                sels.Add(true);
                types.Add(BlendParamType.Float);
                inverts.Add(false);
                offs.Add(0f);
                ons.Add(100f);
              }
              entry.paramTypes = types;
              entry.boolInverts = inverts;
              entry.boolOffValues = offs;
              entry.boolOnValues = ons;
            }
            entry.blendshapeNames = names;
            entry.selected = sels;
            entry.foldout = names.Count > 0;
            entry.prefix = newMr != null ? newMr.gameObject.name + "_" : (string.IsNullOrWhiteSpace(entry.prefix) ? "mesh_" + mi + "_" : entry.prefix);
          }
        }

        if (GUILayout.Button("Remove", GUILayout.Width(64)))
        {
          meshEntries.RemoveAt(mi);
          EditorGUILayout.EndHorizontal();
          EditorGUILayout.EndVertical();
          break;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Param Prefix", GUILayout.MaxWidth(80));
        if (string.IsNullOrWhiteSpace(entry.prefix)) entry.prefix = mr != null ? (mr.gameObject.name + "_") : ("mesh_" + mi + "_");
        entry.prefix = EditorGUILayout.TextField(entry.prefix);
        EditorGUILayout.SelectableLabel(parameterPrefix + entry.prefix + "<blendshape>", GUILayout.MaxWidth(300), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();

        string header;
        if (mr == null)
          header = "(unset)";
        else if (!string.IsNullOrEmpty(relPath))
          header = relPath;
        else
          header = GetTransformPath(mr.transform);
        entry.foldout = EditorGUILayout.Foldout(entry.foldout, header);

        if (entry.foldout)
        {
          var names = entry.blendshapeNames;
          var safeNames = BuildParameterSuffixes(names ?? new List<string>());
          var sels = entry.selected;
          var types = entry.paramTypes;
          var inverts = entry.boolInverts;
          var offs = entry.boolOffValues;
          var ons = entry.boolOnValues;
          if (names != null && names.Count > 0)
          {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Batch:", GUILayout.Width(44));
            batchParamType = (BlendParamType)EditorGUILayout.EnumPopup(batchParamType, GUILayout.Width(60));
            if (GUILayout.Button("Set Type", GUILayout.Width(64)))
            {
              if (types == null || types.Count != names.Count)
                entry.paramTypes = types = Enumerable.Repeat(batchParamType, names.Count).ToList();
              else
                for (int k = 0; k < types.Count; k++) types[k] = batchParamType;
            }
            batchBoolInvert = EditorGUILayout.ToggleLeft("Inv", batchBoolInvert, GUILayout.Width(42));
            EditorGUILayout.LabelField("Off", GUILayout.Width(28));
            batchBoolOff = EditorGUILayout.FloatField(batchBoolOff, GUILayout.Width(60));
            EditorGUILayout.LabelField("On", GUILayout.Width(24));
            batchBoolOn = EditorGUILayout.FloatField(batchBoolOn, GUILayout.Width(60));
            if (GUILayout.Button("Apply Bool", GUILayout.Width(88)))
            {
              if (inverts == null || inverts.Count != names.Count) inverts = entry.boolInverts = Enumerable.Repeat(false, names.Count).ToList();
              if (offs == null || offs.Count != names.Count) offs = entry.boolOffValues = Enumerable.Repeat(0f, names.Count).ToList();
              if (ons == null || ons.Count != names.Count) ons = entry.boolOnValues = Enumerable.Repeat(100f, names.Count).ToList();
              for (int k = 0; k < names.Count; k++)
              {
                if (types[k] != BlendParamType.Bool) continue;
                inverts[k] = batchBoolInvert;
                offs[k] = batchBoolOff;
                ons[k] = batchBoolOn;
              }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", GUILayout.Width(44)))
            {
              for (int k = 0; k < sels.Count; k++) sels[k] = true;
            }
            if (GUILayout.Button("None", GUILayout.Width(44)))
            {
              for (int k = 0; k < sels.Count; k++) sels[k] = false;
            }
            EditorGUILayout.EndHorizontal();

            float lineH = EditorGUIUtility.singleLineHeight + 4f;
            float desiredH = Mathf.Min(names.Count * lineH + 8f, 300f);
            entry.scroll = EditorGUILayout.BeginScrollView(entry.scroll, GUILayout.Height(desiredH));
            for (int bi = 0; bi < names.Count; bi++)
            {
              EditorGUILayout.BeginHorizontal();

              sels[bi] = EditorGUILayout.ToggleLeft(names[bi], sels[bi]);

              var previewPrefix = !string.IsNullOrWhiteSpace(entry.prefix) ? entry.prefix : (mr != null ? mr.gameObject.name + "_" : "");
              if (types == null || types.Count != names.Count)
                types = entry.paramTypes = Enumerable.Repeat(BlendParamType.Float, names.Count).ToList();
              types[bi] = (BlendParamType)EditorGUILayout.EnumPopup(types[bi], GUILayout.Width(60));
              if (inverts == null || inverts.Count != names.Count)
                inverts = entry.boolInverts = Enumerable.Repeat(false, names.Count).ToList();
              if (types[bi] == BlendParamType.Bool)
              {
                inverts[bi] = EditorGUILayout.ToggleLeft("Inv", inverts[bi], GUILayout.Width(42));
                if (offs == null || offs.Count != names.Count) offs = entry.boolOffValues = Enumerable.Repeat(0f, names.Count).ToList();
                if (ons == null || ons.Count != names.Count) ons = entry.boolOnValues = Enumerable.Repeat(100f, names.Count).ToList();
                EditorGUILayout.LabelField("Off", GUILayout.Width(28));
                float newOff = EditorGUILayout.FloatField(offs[bi], GUILayout.Width(60));
                EditorGUILayout.LabelField("On", GUILayout.Width(24));
                float newOn = EditorGUILayout.FloatField(ons[bi], GUILayout.Width(60));
                if (!Mathf.Approximately(newOff, offs[bi])) offs[bi] = newOff;
                if (!Mathf.Approximately(newOn, ons[bi])) ons[bi] = newOn;
              }
              EditorGUILayout.SelectableLabel(parameterPrefix + previewPrefix + safeNames[bi], GUILayout.MaxWidth(260), GUILayout.Height(EditorGUIUtility.singleLineHeight));

              EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
          }
          else
          {
            EditorGUILayout.LabelField("(no blendshapes)");
          }
        }
        EditorGUILayout.EndVertical();
      }
      EditorGUILayout.EndScrollView();
      EditorGUILayout.Space();
    }

    if (avatarRoot == null)
    {
      EditorGUILayout.HelpBox("Please specify an Animation Root.", MessageType.Error);
    }

    EditorGUILayout.Space();
    EditorGUILayout.HelpBox("The generator will create an AnimationClip (0->100) for each selected blendshape and generate a corresponding Animator Controller.", MessageType.Info);

    bool avatarOk = avatarRoot != null;
    EditorGUI.BeginDisabledGroup(!avatarOk);
    if (GUILayout.Button("Generate"))
    {
      if (!meshEntries.Any(e => e.enabled))
      {
        EditorUtility.DisplayDialog("No meshes selected", "Please enable at least one mesh for export.", "OK");
      }
      else
      {
        foreach (var entry in meshEntries)
        {
          if (!entry.enabled) continue;
          var mr = entry.renderer;
          if (mr == null) continue;
          var rel = GetRelativePath(avatarRoot.transform, mr.transform);
          if (rel == null)
          {
            EditorUtility.DisplayDialog("Invalid Animation Root", "One or more selected meshes are outside the Animation Root. Choose their common ancestor as the root.", "OK");
            return;
          }
        }
        Generate();
      }
    }
    if (BlendshapeAvatarIntegration.Installer is IBlendshapeAvatarUninstaller uninstaller &&
        GUILayout.Button("Remove Selected MA Controls"))
      uninstaller.Uninstall(avatarRoot, meshEntries.Where(entry => entry.enabled && entry.renderer != null)
        .SelectMany(entry => new[] { GetMeshIdentity(entry.renderer), entry.renderer.name }).Distinct().ToArray());
    if (GeneratedController != null)
    {
      EditorGUILayout.ObjectField("Generated Controller", GeneratedController, typeof(AnimatorController), false);
      if (GUILayout.Button("Assign to Animation Root Animator")) AssignGeneratedController();
    }
    EditorGUI.EndDisabledGroup();
  }

  public void AssignGeneratedController()
  {
    var animator = avatarRoot.GetComponent<Animator>();
    if (animator == null) animator = Undo.AddComponent<Animator>(avatarRoot);
    Undo.RecordObject(animator, "Assign generated blendshape controller");
    animator.runtimeAnimatorController = GeneratedController;
    EditorUtility.SetDirty(animator);
    PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
  }

  private bool ValidateGeneration(out string error)
  {
    error = null;
    if (avatarRoot == null || EditorUtility.IsPersistent(avatarRoot))
      error = "Select an Animation Root instance in a scene.";
    else if (!IsSafeOutputFolder(outputFolder))
      error = "Output Folder must be a project Assets path without empty, '.' or '..' segments.";
    else if (string.IsNullOrEmpty(blendshapePropertyPrefix))
      error = "Blendshape Property Prefix cannot be empty.";
    else if (float.IsNaN(floatCurveDuration) || float.IsInfinity(floatCurveDuration) || floatCurveDuration <= 0 ||
      float.IsNaN(floatValueScale) || float.IsInfinity(floatValueScale))
      error = "Float duration must be positive and all curve settings must be finite.";
    if (error != null) return false;

    var selectedBindings = new HashSet<(SkinnedMeshRenderer Renderer, string Shape)>();
    foreach (var entry in meshEntries.Where(entry => entry.enabled))
    {
      if (entry.renderer == null || entry.renderer.sharedMesh == null)
      {
        error = "Every enabled entry needs a SkinnedMeshRenderer with a mesh.";
        return false;
      }
      if (GetRelativePath(avatarRoot.transform, entry.renderer.transform) == null)
      {
        error = "Every selected renderer must be inside the selected Animation Root.";
        return false;
      }
      for (var current = entry.renderer.transform; current != avatarRoot.transform; current = current.parent)
      {
        if (current.name.Contains("/") || current.parent.Cast<Transform>().Count(child => child.name == current.name) > 1)
        {
          error = "Animation paths require unique sibling names without '/': " + current.name;
          return false;
        }
      }
      if (entry.blendshapeNames == null || entry.selected == null ||
          entry.blendshapeNames.Count != entry.selected.Count ||
          entry.blendshapeNames.Where((name, i) => entry.selected[i])
            .Any(name => entry.renderer.sharedMesh.GetBlendShapeIndex(name) < 0))
      {
        error = "The mesh has changed since scanning. Scan it again before generating.";
        return false;
      }
      foreach (var shape in entry.blendshapeNames.Where((name, index) => entry.selected[index]))
        if (!selectedBindings.Add((entry.renderer, shape)))
        {
          error = "Blendshape selected more than once: " + entry.renderer.name + "/" + shape;
          return false;
        }
      if ((entry.boolOffValues ?? new List<float>()).Concat(entry.boolOnValues ?? new List<float>())
          .Any(value => float.IsNaN(value) || float.IsInfinity(value)))
      {
        error = "Bool Off and On values must be finite.";
        return false;
      }
    }
    return true;
  }

  private static bool IsSafeOutputFolder(string folder)
  {
    if (string.IsNullOrWhiteSpace(folder) || folder.Contains("\\") || folder.Contains(":")) return false;
    var parts = folder.Split('/');
    return parts[0] == "Assets" && parts.All(part => !string.IsNullOrWhiteSpace(part) && part != "." && part != "..");
  }

  private static void EnsureAssetFolder(string path)
  {
    var parts = path.Split('/');
    string current = parts[0];
    for (int i = 1; i < parts.Length; i++)
    {
      string next = current + "/" + parts[i];
      if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i])))
        throw new IOException("Could not create asset folder: " + next);
      current = next;
    }
  }

  private static string StableHash(string value)
  {
    using (var sha = System.Security.Cryptography.SHA256.Create())
      return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)))
        .Replace("-", "").Substring(0, 12).ToLowerInvariant();
  }

  private static string GetHierarchyIdentity(Transform target)
  {
    var indices = new List<int>();
    for (var current = target; current != null; current = current.parent)
      indices.Add(current.GetSiblingIndex());
    indices.Reverse();
    return string.Join("/", indices);
  }

  private static string[] BuildParameterSuffixes(IReadOnlyList<string> names)
  {
    // Reserve every original sanitized name before allocating collision suffixes.
    var reserved = new HashSet<string>(names.Select(SanitizeName), StringComparer.Ordinal);
    var assigned = new HashSet<string>(StringComparer.Ordinal);
    var result = new string[names.Count];
    for (int i = 0; i < names.Count; i++)
    {
      string basis = SanitizeName(names[i]);
      string candidate = basis;
      if (!assigned.Add(candidate))
      {
        int suffix = 2;
        do { candidate = basis + "_" + suffix++; }
        while (reserved.Contains(candidate) || !assigned.Add(candidate));
      }
      result[i] = candidate;
    }
    return result;
  }

  private void Generate()
  {
    if (!ValidateGeneration(out var error))
    {
      EditorUtility.DisplayDialog("Cannot generate", error, "OK");
      return;
    }
    Undo.IncrementCurrentGroup();
    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Generate blendshape controls");
    try
    {
      GenerateInternal();
    }
    catch (Exception exception)
    {
      Debug.LogException(exception);
      EditorUtility.DisplayDialog("Generation failed", exception.Message, "OK");
    }
    finally
    {
      if (!string.IsNullOrEmpty(pendingGenerationFolder))
        AssetDatabase.DeleteAsset(pendingGenerationFolder);
      pendingGenerationFolder = null;
      EditorUtility.ClearProgressBar();
      Undo.CollapseUndoOperations(undoGroup);
    }
  }

  private void GenerateInternal()
  {
    var usedParams = new HashSet<string>(StringComparer.Ordinal);
    var proposedPrefixes = new List<string>();
    for (int i = 0; i < meshEntries.Count; i++) proposedPrefixes.Add(!string.IsNullOrWhiteSpace(meshEntries[i].prefix) ? meshEntries[i].prefix : null);

    var proposals = new List<string>();
    for (int mi = 0; mi < meshEntries.Count; mi++)
    {
      var entry = meshEntries[mi];
      if (!entry.enabled) continue;
      var names = entry.blendshapeNames;
      var sels = entry.selected;
      var types = entry.paramTypes;
      if (names == null || sels == null) continue;
      var safeNames = BuildParameterSuffixes(names);
      if (string.IsNullOrWhiteSpace(proposedPrefixes[mi]))
        proposedPrefixes[mi] = (entry.renderer != null ? entry.renderer.gameObject.name : "mesh") + "_";

      string basePrefix = proposedPrefixes[mi];
      string attemptPrefix = basePrefix;
      int suffix = 1;
      bool collides = false;
      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var pname = parameterPrefix + attemptPrefix + safeNames[j];
        if (usedParams.Contains(pname)) { collides = true; break; }
      }
      if (collides)
      {
        do
        {
          attemptPrefix = basePrefix + suffix + "_";
          suffix++;
          collides = false;
          for (int j = 0; j < names.Count; j++)
          {
            if (!sels[j]) continue;
            var pname = parameterPrefix + attemptPrefix + safeNames[j];
            if (usedParams.Contains(pname)) { collides = true; break; }
          }
        } while (collides);
        proposals.Add($"Entry #{mi}: '{basePrefix}' -> '{attemptPrefix}'");
        proposedPrefixes[mi] = attemptPrefix;
      }

      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var pname = parameterPrefix + proposedPrefixes[mi] + safeNames[j];
        usedParams.Add(pname);
      }
    }

    if (proposals.Count > 0)
    {
      var msg = "The following prefix proposals were generated to avoid parameter name collisions:\n\n" + string.Join("\n", proposals) + "\n\nChoose an action:";
      bool autoApply = EditorUtility.DisplayDialog("Parameter Prefix Conflicts", msg, "Auto-apply", "Edit Prefixes");
      if (autoApply)
      {
        for (int i = 0; i < proposedPrefixes.Count; i++)
        {
          if (i < meshEntries.Count) meshEntries[i].prefix = proposedPrefixes[i];
        }
        foreach (var s in proposals) Debug.Log(s + " (auto-applied)");
      }
      else
      {
        EditorUtility.DisplayDialog("Edit Prefixes", "Please edit the prefixes in the list and press Generate again.", "OK");
        return;
      }
    }

    int totalClips = 0;
    foreach (var entry in meshEntries)
    {
      if (!entry.enabled) continue;
      var names = entry.blendshapeNames;
      var sels = entry.selected;
      var types = entry.paramTypes;
      if (names == null || sels == null) continue;
      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var t = (types != null && types.Count == names.Count) ? types[j] : BlendParamType.Float;
        totalClips += (t == BlendParamType.Bool) ? 2 : 1;
      }
    }

    if (totalClips == 0)
    {
      EditorUtility.DisplayDialog("Nothing to generate", "No blendshape clips selected for export.", "OK");
      return;
    }

    var generated = new List<AnimatorController>();
    int clipIndex = 0;
    for (int mi = 0; mi < meshEntries.Count; mi++)
    {
      var entry = meshEntries[mi];
      if (!entry.enabled) continue;
      var mr = entry.renderer;
      var names = entry.blendshapeNames;
      var sels = entry.selected;
      var types = entry.paramTypes;
      var inverts = entry.boolInverts;
      var offs = entry.boolOffValues;
      var ons = entry.boolOnValues;
      if (names == null || sels == null) continue;

      var usePrefix = (mi < proposedPrefixes.Count) ? proposedPrefixes[mi] : entry.prefix;
      bool cancelled = GenerateForMesh(mr, names, sels, types, inverts, offs, ons, clipIndex, totalClips, usePrefix);
      if (cancelled)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogWarning("Generation cancelled by user.");
        return;
      }

      generated.Add(lastMeshController);
      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var t = (types != null && types.Count == names.Count) ? types[j] : BlendParamType.Float;
        clipIndex += (t == BlendParamType.Bool) ? 2 : 1;
      }
    }
    if (!installAvatarControls || BlendshapeAvatarIntegration.Installer == null)
    {
      string avatarIdentity = SanitizeName(avatarRoot.name) + "_" +
        StableHash(avatarRoot.scene.path + "/" + GetHierarchyIdentity(avatarRoot.transform));
      string combinedRoot = outputFolder.TrimEnd('/') + "/" + avatarIdentity + "/Combined";
      EnsureAssetFolder(combinedRoot);
      string combinedFolder = AssetDatabase.GenerateUniqueAssetPath(combinedRoot + "/Build");
      EnsureAssetFolder(combinedFolder);
      GeneratedController = BlendshapeControllerComposer.Combine(generated, combinedFolder + "/Blendshapes.controller");
      EditorGUIUtility.PingObject(GeneratedController);
    }
    EditorUtility.ClearProgressBar();
  }

  private bool GenerateForMesh(SkinnedMeshRenderer mr, List<string> names, List<bool> sels, List<BlendParamType> types, List<bool> inverts, List<float> offs, List<float> ons, int clipIndexOffset, int totalClips, string usePrefix)
  {
    var safeNames = BuildParameterSuffixes(names);
    string meshName = mr.gameObject.name;
    string computedRelative = GetRelativePath(avatarRoot.transform, mr.transform);
    string meshIdentity = GetMeshIdentity(mr);
    string avatarIdentity = SanitizeName(avatarRoot.name) + "_" +
      StableHash(avatarRoot.scene.path + "/" + GetHierarchyIdentity(avatarRoot.transform));
    string meshOutputFolder = outputFolder.TrimEnd('/') + "/" + avatarIdentity + "/" + meshIdentity;
    EnsureAssetFolder(meshOutputFolder);
    // Build new assets before replacing the scene references. Old assets remain valid
    // for Undo and a cancelled/failed build cannot corrupt an existing controller.
    string meshSubFolder = AssetDatabase.GenerateUniqueAssetPath(meshOutputFolder + "/Build");
    EnsureAssetFolder(meshSubFolder);
    pendingGenerationFolder = meshSubFolder;
    string fileName = string.IsNullOrWhiteSpace(controllerName)
      ? SanitizeName(meshName) : SanitizeName(Path.GetFileNameWithoutExtension(controllerName));
    string controllerPath = meshSubFolder + "/" + fileName + ".controller";
    string baseControllerNameForMenu = meshIdentity;
    var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
    if (controller.layers.Length > 0)
    {
      controller.RemoveLayer(0);
    }

    string meshPrefix = !string.IsNullOrEmpty(usePrefix) ? usePrefix : (mr != null ? mr.gameObject.name + "_" : "");

    string finalBindingPath = computedRelative;

    int localCount = 0;
    for (int i = 0; i < names.Count; i++)
    {
      if (!sels[i]) continue;
      var name = names[i];
      var pType = (types != null && types.Count == names.Count) ? types[i] : BlendParamType.Float;
      bool invert = (inverts != null && inverts.Count == names.Count) ? inverts[i] : false;
      float offVal = (offs != null && offs.Count == names.Count) ? offs[i] : 0f;
      float onVal = (ons != null && ons.Count == names.Count) ? ons[i] : 100f;
      string safeName = safeNames[i];

      if (pType == BlendParamType.Float)
      {
        float globalProgress = (float)(clipIndexOffset + localCount) / Math.Max(1, totalClips);
        if (EditorUtility.DisplayCancelableProgressBar("Generating Blendshape Clips", $"{meshName}: {localCount + 1}/{names.Count} - {name}", globalProgress))
        {
          EditorUtility.ClearProgressBar();
          Debug.LogWarning("Generation cancelled by user during clip creation.");
          return true;
        }
        localCount++;

        string clipPath = Path.Combine(meshSubFolder, i.ToString("D4") + "_Float.anim").Replace("\\", "/");

        var clip = new AnimationClip();
        clip.name = safeName;
        clip.wrapMode = floatCurveLoop ? WrapMode.Loop : WrapMode.Default;

        var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = floatCurveLoop;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

        var binding = new EditorCurveBinding();
        binding.path = finalBindingPath;
        binding.type = typeof(SkinnedMeshRenderer);
        binding.propertyName = blendshapePropertyPrefix + name;

        var curve = BuildScaledCurve(floatCurve, floatCurveDuration, floatValueScale);
        AnimationUtility.SetEditorCurve(clip, binding, curve);
        AssetDatabase.CreateAsset(clip, clipPath);

        string paramName = parameterPrefix + meshPrefix + safeName;
        if (!controller.parameters.Any(p => p.name == paramName))
        {
          controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
        }

        var stateMachine = new AnimatorStateMachine();
        stateMachine.name = safeName;
        AssetDatabase.AddObjectToAsset(stateMachine, controllerPath);

        var state = stateMachine.AddState(safeName);
        state.motion = clip;
        state.writeDefaultValues = false;
        stateMachine.defaultState = state;
        state.timeParameterActive = true;
        state.timeParameter = paramName;

        var layer = new AnimatorControllerLayer();
        layer.name = safeName;
        layer.defaultWeight = 1f;
        layer.stateMachine = stateMachine;
        layer.syncedLayerIndex = -1;

        controller.AddLayer(layer);
      }
      else
      {
        float globalProgress = (float)(clipIndexOffset + localCount) / Math.Max(1, totalClips);
        if (EditorUtility.DisplayCancelableProgressBar("Generating Blendshape Clips", $"{meshName}: {localCount + 1}/{names.Count} - {name} (" + (invert ? "On" : "Off") + ")", globalProgress))
        {
          EditorUtility.ClearProgressBar();
          Debug.LogWarning("Generation cancelled by user during clip creation.");
          return true;
        }
        localCount++;
        string aClipName = invert ? "_On" : "_Off";
        float aValue = invert ? onVal : offVal;
        string aClipPath = Path.Combine(meshSubFolder, i.ToString("D4") + "_False.anim").Replace("\\", "/");
        var aClip = new AnimationClip();
        aClip.name = safeName + aClipName;
        var aBinding = new EditorCurveBinding { path = finalBindingPath, type = typeof(SkinnedMeshRenderer), propertyName = blendshapePropertyPrefix + name };
        var aCurve = AnimationCurve.Constant(0f, 1f, aValue);
        AnimationUtility.SetEditorCurve(aClip, aBinding, aCurve);
        AssetDatabase.CreateAsset(aClip, aClipPath);

        globalProgress = (float)(clipIndexOffset + localCount) / Math.Max(1, totalClips);
        if (EditorUtility.DisplayCancelableProgressBar("Generating Blendshape Clips", $"{meshName}: {localCount + 1}/{names.Count} - {name} (" + (invert ? "Off" : "On") + ")", globalProgress))
        {
          EditorUtility.ClearProgressBar();
          Debug.LogWarning("Generation cancelled by user during clip creation.");
          return true;
        }
        localCount++;
        string bClipName = invert ? "_Off" : "_On";
        float bValue = invert ? offVal : onVal;
        string bClipPath = Path.Combine(meshSubFolder, i.ToString("D4") + "_True.anim").Replace("\\", "/");
        var bClip = new AnimationClip();
        bClip.name = safeName + bClipName;
        var bBinding = new EditorCurveBinding { path = finalBindingPath, type = typeof(SkinnedMeshRenderer), propertyName = blendshapePropertyPrefix + name };
        var bCurve = AnimationCurve.Constant(0f, 1f, bValue);
        AnimationUtility.SetEditorCurve(bClip, bBinding, bCurve);
        AssetDatabase.CreateAsset(bClip, bClipPath);

        string paramName = parameterPrefix + meshPrefix + safeName;
        if (!controller.parameters.Any(p => p.name == paramName))
        {
          controller.AddParameter(paramName, AnimatorControllerParameterType.Bool);
        }

        var stateMachine = new AnimatorStateMachine { name = safeName };
        AssetDatabase.AddObjectToAsset(stateMachine, controllerPath);

        var stateA = stateMachine.AddState(safeName + aClipName);
        stateA.motion = aClip;
        stateA.writeDefaultValues = false;
        var stateB = stateMachine.AddState(safeName + bClipName);
        stateB.motion = bClip;
        stateB.writeDefaultValues = false;

        stateMachine.defaultState = stateA;

        var toOn = stateMachine.AddAnyStateTransition(stateB);
        toOn.hasExitTime = false;
        toOn.duration = 0f;
        toOn.canTransitionToSelf = false;
        toOn.AddCondition(AnimatorConditionMode.If, 0f, paramName);

        var toOff = stateMachine.AddAnyStateTransition(stateA);
        toOff.hasExitTime = false;
        toOff.duration = 0f;
        toOff.canTransitionToSelf = false;
        toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, paramName);

        var layer = new AnimatorControllerLayer { name = safeName, defaultWeight = 1f, stateMachine = stateMachine, syncedLayerIndex = -1 };
        controller.AddLayer(layer);
      }
    }

    var controls = new List<BlendshapeGeneratedControl>();
    for (int i = 0; i < names.Count; i++)
    {
      if (!sels[i]) continue;
      controls.Add(new BlendshapeGeneratedControl(names[i], parameterPrefix + meshPrefix + safeNames[i],
        types != null && types.Count == names.Count && types[i] == BlendParamType.Bool));
    }
    if (installAvatarControls)
      BlendshapeAvatarIntegration.Installer?.Install(new BlendshapeGenerationResult(
        avatarRoot, controller, baseControllerNameForMenu, meshName, controls));
    pendingGenerationFolder = null;
    lastMeshController = controller;

    EditorUtility.SetDirty(controller);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    string shownPath = computedRelative != null ? (computedRelative + " (relative to Animation Root)") : "(bound to target object)";
    Debug.Log("Generated Animator Controller and clips in: " + meshSubFolder + " Binding path used: '" + shownPath + "'");
    return false;
  }

  private string GetMeshIdentity(SkinnedMeshRenderer renderer) =>
    SanitizeName(renderer.name) + "_" + StableHash(GetRelativePath(avatarRoot.transform, renderer.transform));

  private static string GetRelativePath(Transform root, Transform target)
  {
    if (root == null || target == null) return null;
    if (root == target) return "";
    var parts = new List<string>();
    var t = target;
    while (t != null && t != root)
    {
      parts.Add(t.name);
      t = t.parent;
    }
    if (t != root) return null;
    parts.Reverse();
    return string.Join("/", parts.ToArray());
  }

  private static string SanitizeName(string s)
  {
    var arr = (s ?? "").Select(c => (char.IsLetterOrDigit(c) ? c : '_')).ToArray();
    return arr.Length == 0 ? "Item" : new string(arr);
  }

  private static string GetTransformPath(Transform t)
  {
    var path = t.name;
    while (t.parent != null && t.parent.parent != null)
    {
      t = t.parent;
      path = t.name + "/" + path;
    }
    return path;
  }

  private void RefreshMeshes()
  {
    RefreshMeshes(true, null);
  }

  private void RefreshMeshes(bool includeInactive, string nameFilter)
  {
    meshEntries.Clear();

    if (avatarRoot == null) return;

    var srs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
    foreach (var sr in srs)
    {
      if (!string.IsNullOrEmpty(nameFilter) && !sr.gameObject.name.Contains(nameFilter)) continue;
      var entry = new MeshEntry();
      entry.renderer = sr;

      var names = new List<string>();
      var sels = new List<bool>();
      var types = new List<BlendParamType>();
      var mesh = sr.sharedMesh;
      if (mesh != null)
      {
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
          names.Add(mesh.GetBlendShapeName(i));
          sels.Add(true);
          types.Add(BlendParamType.Float);
        }
      }
      entry.blendshapeNames = names;
      entry.selected = sels;
      entry.paramTypes = types;
      entry.scroll = Vector2.zero;

      entry.enabled = true;
      entry.foldout = false;

      entry.prefix = sr != null ? sr.gameObject.name + "_" : "mesh_" + meshEntries.Count + "_";

      meshEntries.Add(entry);
    }
  }

  private void OnEnable()
  {
    if (EditorPrefs.HasKey(EditorPrefKey_ParameterPrefix))
    {
      parameterPrefix = EditorPrefs.GetString(EditorPrefKey_ParameterPrefix, parameterPrefix);
    }
    try
    {
      if (EditorPrefs.HasKey(EditorPrefKey_FloatCurve))
      {
        var json = EditorPrefs.GetString(EditorPrefKey_FloatCurve, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
          var data = JsonUtility.FromJson<CurveData>(json);
          if (data.keys != null && data.keys.Length > 0)
          {
            floatCurve = new AnimationCurve(data.keys);
          }
        }
      }
      if (EditorPrefs.HasKey(EditorPrefKey_FloatCurveDuration))
      {
        floatCurveDuration = Mathf.Max(0.0001f, EditorPrefs.GetFloat(EditorPrefKey_FloatCurveDuration, floatCurveDuration));
      }
      if (EditorPrefs.HasKey(EditorPrefKey_FloatCurveLoop))
      {
        floatCurveLoop = EditorPrefs.GetInt(EditorPrefKey_FloatCurveLoop, floatCurveLoop ? 1 : 0) != 0;
      }
      if (EditorPrefs.HasKey(EditorPrefKey_FloatValueScale))
      {
        floatValueScale = EditorPrefs.GetFloat(EditorPrefKey_FloatValueScale, floatValueScale);
      }
    }
    catch { }
    if (avatarRoot != null)
      RefreshMeshes();
  }

  private static float ScaleTangent(float tangent, float scale)
  {
    return float.IsInfinity(tangent) ? tangent : tangent * scale;
  }

  private static AnimationCurve BuildScaledCurve(AnimationCurve source, float duration, float valueScale)
  {
    if (source == null || source.keys == null || source.keys.Length == 0)
    {
      return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(duration, valueScale));
    }
    var keys = source.keys;
    var newKeys = new Keyframe[keys.Length];
    float slopeScale = (duration == 0f) ? 0f : (valueScale / duration);
    for (int i = 0; i < keys.Length; i++)
    {
      var k = keys[i];
      var nk = new Keyframe(k.time * duration, k.value * valueScale, ScaleTangent(k.inTangent, slopeScale), ScaleTangent(k.outTangent, slopeScale))
      {
        weightedMode = k.weightedMode,
        inWeight = k.inWeight,
        outWeight = k.outWeight
      };
      newKeys[i] = nk;
    }
    var curve = new AnimationCurve(newKeys);
    return curve;
  }
}
