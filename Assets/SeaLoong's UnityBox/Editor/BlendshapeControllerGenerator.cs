using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using System.Collections.Immutable;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;

// Generator: create AnimationClips and an AnimatorController for selected Blendshapes on a
// SkinnedMeshRenderer — one Layer per blendshape, controlled by a float parameter.
public class BlendshapeControllerGenerator : EditorWindow
{
  private GameObject avatarRoot;
  private Vector2 meshListScroll;
  private string blendshapePropertyPrefix = "blendShape.";
  private string outputFolder = "Assets/SeaLoong's UnityBox/GeneratedBlendshapes";

  private string controllerName = "";
  // Multi-mesh support: represent each entry as a struct-like class to avoid parallel-list desync
  private class MeshEntry
  {
    public SkinnedMeshRenderer renderer;
    public List<string> blendshapeNames = new();
    public List<bool> selected = new();
    public bool enabled = true;
    public bool foldout = false;
    public string prefix = "";
    public Vector2 scroll = Vector2.zero;
  }

  private List<MeshEntry> meshEntries = new List<MeshEntry>();
  private string scanFilter = "";
  private bool scanIncludeInactive = true;
  private List<string> lastGeneratedAssets = new List<string>();


  [MenuItem("Tools/SeaLoong's UnityBox/Blendshape Controller Generator")]
  public static void ShowWindow()
  {
    GetWindow<BlendshapeControllerGenerator>("Blendshape Controller Generator");
  }

  private void OnGUI()
  {
    EditorGUILayout.LabelField("Blendshape Controller Generator", EditorStyles.boldLabel);

    avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar", avatarRoot, typeof(GameObject), true);

    EditorGUILayout.HelpBox("Binding path uses the Avatar-to-Target relative path (Target must be a descendant of the Avatar).", MessageType.Info);

    EditorGUILayout.BeginHorizontal();
    EditorGUILayout.LabelField("Output Folder", GUILayout.MaxWidth(80));
    outputFolder = EditorGUILayout.TextField(outputFolder);
    if (GUILayout.Button("Browse", GUILayout.MaxWidth(80)))
    {
      var folder = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
      if (!string.IsNullOrEmpty(folder))
      {
        if (folder.StartsWith(Application.dataPath))
          outputFolder = "Assets" + folder.Substring(Application.dataPath.Length);
        else
          EditorUtility.DisplayDialog("Invalid folder", "Please select a folder inside this Unity project's Assets folder.", "OK");
      }
    }
    EditorGUILayout.EndHorizontal();

    controllerName = EditorGUILayout.TextField("Controller FileName", controllerName);
    blendshapePropertyPrefix = EditorGUILayout.TextField("Blendshape Property Prefix", blendshapePropertyPrefix);

    EditorGUILayout.Space();

    if (avatarRoot != null)
    {
      // --- Scan toolbar (filter + scan) ---
      EditorGUILayout.BeginHorizontal();
      scanFilter = EditorGUILayout.TextField(scanFilter, GUILayout.MinWidth(120));
      scanIncludeInactive = EditorGUILayout.ToggleLeft("Include Inactive", scanIncludeInactive, GUILayout.Width(120));

      if (GUILayout.Button("Scan", GUILayout.Width(70)))
      {
        RefreshMeshes(scanIncludeInactive, string.IsNullOrWhiteSpace(scanFilter) ? null : scanFilter);
        // align prefixes on the new meshEntries
        for (int i = 0; i < meshEntries.Count; i++)
        {
          var e = meshEntries[i];
          e.prefix = e.renderer != null ? e.renderer.gameObject.name + "_" : "mesh_" + i + "_";
        }
      }
      EditorGUILayout.EndHorizontal();

      EditorGUILayout.HelpBox("Scan Filter: the filter matches GameObject names under the Avatar. Leave empty for no filtering.", MessageType.Info);

      // --- Selection toolbar (Add/Clear) placed under Scan ---
      EditorGUILayout.BeginHorizontal();

      EditorGUILayout.LabelField("Meshes to generate (choose from Avatar or add manually)", EditorStyles.label);

      if (GUILayout.Button("Add", GUILayout.Width(60)))
      {
        var e = new MeshEntry();
        e.renderer = null;
        e.blendshapeNames = new List<string>();
        e.selected = new List<bool>();
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

      // Suggest button removed
      EditorGUILayout.EndHorizontal();
    }

    if (avatarRoot != null)
    {
      // Render mesh entries (user-managed) inside a dedicated scroll area
      meshListScroll = EditorGUILayout.BeginScrollView(meshListScroll, GUILayout.MaxHeight(800), GUILayout.ExpandHeight(false));
      for (int mi = 0; mi < meshEntries.Count; mi++)
      {
        var entry = meshEntries[mi];
        var mr = entry.renderer;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.MaxWidth(18));
        var newMr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(mr, typeof(SkinnedMeshRenderer), true);
        // display relative path for the mesh to make entries distinguishable
        string relPath = null;
        if (newMr != null && avatarRoot != null)
          relPath = GetRelativePath(avatarRoot.transform, newMr.transform);
        if (newMr != null && relPath == null)
          relPath = GetTransformPath(newMr.transform);
        if (newMr != mr)
        {
          // Prevent duplicate selection of same SkinnedMeshRenderer instance
          if (newMr != null && meshEntries.Where((existing, idx) => idx != mi && existing.renderer == newMr).Any())
          {
            EditorUtility.DisplayDialog("Duplicate Mesh", "This SkinnedMeshRenderer is already added in the list. Please select a different mesh.", "OK");
          }
          else
          {
            entry.renderer = newMr;
            // refresh this entry's blendshape lists
            var names = new List<string>();
            var sels = new List<bool>();
            if (newMr != null && newMr.sharedMesh != null)
            {
              for (int bi = 0; bi < newMr.sharedMesh.blendShapeCount; bi++)
              {
                names.Add(newMr.sharedMesh.GetBlendShapeName(bi));
                sels.Add(true);
              }
            }
            entry.blendshapeNames = names;
            entry.selected = sels;
            entry.foldout = names.Count > 0;
            // Reset prefix to the mesh's name when user assigns a mesh (drag-in)
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

        // Parameter prefix editor and preview
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Param Prefix", GUILayout.MaxWidth(80));
        if (string.IsNullOrWhiteSpace(entry.prefix)) entry.prefix = mr != null ? (mr.gameObject.name + "_") : ("mesh_" + mi + "_");
        entry.prefix = EditorGUILayout.TextField(entry.prefix);
        // read-only preview (SelectableLabel allows copy on newer Unity versions; fallback to LabelField)
        EditorGUILayout.SelectableLabel("BS_" + entry.prefix + "<blendshape>", GUILayout.MaxWidth(300), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();

        // foldout header: show Avatar-relative path when available, otherwise show full transform path
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
          var sels = entry.selected;
          if (names != null && names.Count > 0)
          {
            // Compact select/deselect toolbar
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

            // calculate desired height based on number of names (line height + padding), cap at 300
            float lineH = EditorGUIUtility.singleLineHeight + 4f;
            float desiredH = Mathf.Min(names.Count * lineH + 8f, 300f);
            entry.scroll = EditorGUILayout.BeginScrollView(entry.scroll, GUILayout.Height(desiredH));
            for (int bi = 0; bi < names.Count; bi++)
            {
              EditorGUILayout.BeginHorizontal();

              sels[bi] = EditorGUILayout.ToggleLeft(names[bi], sels[bi]);

              var previewPrefix = !string.IsNullOrWhiteSpace(entry.prefix) ? entry.prefix : (mr != null ? mr.gameObject.name + "_" : "");
              EditorGUILayout.SelectableLabel("BS_" + previewPrefix + SanitizeName(names[bi]), GUILayout.MaxWidth(300), GUILayout.Height(EditorGUIUtility.singleLineHeight));

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

    // Validate Avatar presence
    if (avatarRoot == null)
    {
      EditorGUILayout.HelpBox("Please specify an Avatar (used as the binding root).", MessageType.Error);
    }

    EditorGUILayout.Space();
    EditorGUILayout.HelpBox("The generator will create an AnimationClip (0->100) for each selected blendshape and generate a corresponding Animator Controller.", MessageType.Info);

    bool avatarOk = avatarRoot != null;
    EditorGUI.BeginDisabledGroup(!avatarOk);
    if (GUILayout.Button("Generate"))
    {
      // ensure at least one enabled mesh and validate ancestor relationship
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
            EditorUtility.DisplayDialog("Invalid Avatar", "One or more selected meshes are not descendants of the Avatar. Ensure Avatar is ancestor of the mesh.", "OK");
            return;
          }
        }
        Generate();
      }
    }
    EditorGUI.EndDisabledGroup();
  }

  private void Generate()
  {
    // Propose parameter prefix adjustments to avoid conflicts, but don't apply them immediately.
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
      if (names == null || sels == null) continue;
      if (string.IsNullOrWhiteSpace(proposedPrefixes[mi]))
        proposedPrefixes[mi] = (entry.renderer != null ? entry.renderer.gameObject.name : "mesh") + "_";

      string basePrefix = proposedPrefixes[mi];
      string attemptPrefix = basePrefix;
      int suffix = 1;
      bool collides = false;
      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var pname = "BS_" + attemptPrefix + SanitizeName(names[j]);
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
            var pname = "BS_" + attemptPrefix + SanitizeName(names[j]);
            if (usedParams.Contains(pname)) { collides = true; break; }
          }
        } while (collides);
        proposals.Add($"Entry #{mi}: '{basePrefix}' -> '{attemptPrefix}'");
        proposedPrefixes[mi] = attemptPrefix;
      }

      // reserve names for proposed prefix
      for (int j = 0; j < names.Count; j++)
      {
        if (!sels[j]) continue;
        var pname = "BS_" + proposedPrefixes[mi] + SanitizeName(names[j]);
        usedParams.Add(pname);
      }
    }

    if (proposals.Count > 0)
    {
      var msg = "The following prefix proposals were generated to avoid parameter name collisions:\n\n" + string.Join("\n", proposals) + "\n\nChoose an action:";
      bool autoApply = EditorUtility.DisplayDialog("Parameter Prefix Conflicts", msg, "Auto-apply", "Edit Prefixes");
      if (autoApply)
      {
        // apply proposed prefixes
        for (int i = 0; i < proposedPrefixes.Count; i++)
        {
          if (i < meshEntries.Count) meshEntries[i].prefix = proposedPrefixes[i];
        }
        foreach (var s in proposals) Debug.Log(s + " (auto-applied)");
      }
      else
      {
        // let user edit prefixes manually
        EditorUtility.DisplayDialog("Edit Prefixes", "Please edit the prefixes in the list and press Generate again.", "OK");
        return;
      }
    }

    // Compute total number of clips to generate across enabled meshes
    int totalClips = 0;
    foreach (var entry in meshEntries)
    {
      if (!entry.enabled) continue;
      var names = entry.blendshapeNames;
      var sels = entry.selected;
      if (names == null || sels == null) continue;
      for (int j = 0; j < names.Count; j++) if (sels[j]) totalClips++;
    }

    if (totalClips == 0)
    {
      EditorUtility.DisplayDialog("Nothing to generate", "No blendshape clips selected for export.", "OK");
      return;
    }

    int clipIndex = 0;
    for (int mi = 0; mi < meshEntries.Count; mi++)
    {
      var entry = meshEntries[mi];
      if (!entry.enabled) continue;
      var mr = entry.renderer;
      var names = entry.blendshapeNames;
      var sels = entry.selected;
      if (names == null || sels == null) continue;

      var usePrefix = (mi < proposedPrefixes.Count) ? proposedPrefixes[mi] : entry.prefix;
      bool cancelled = GenerateForMesh(mr, names, sels, clipIndex, totalClips, usePrefix);
      if (cancelled)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogWarning("Generation cancelled by user.");
        return;
      }

      for (int j = 0; j < names.Count; j++) if (sels[j]) clipIndex++;
    }
    EditorUtility.ClearProgressBar();
  }

  private bool GenerateForMesh(SkinnedMeshRenderer mr, List<string> names, List<bool> sels, int clipIndexOffset, int totalClips, string usePrefix)
  {
    string meshName = mr != null ? mr.gameObject.name : "Mesh";
    string meshSubFolder = Path.Combine(outputFolder, meshName).Replace("\\", "/");
    // Controller file base name: if user provided a controllerName, use it (filename only),
    // otherwise default to the mesh name. Controller files are placed per-mesh folder so
    // filename collisions are not a problem.
    string baseControllerNameForFile = string.IsNullOrWhiteSpace(controllerName) ? meshName : Path.GetFileNameWithoutExtension(controllerName);
    string controllerPath = Path.Combine(meshSubFolder, baseControllerNameForFile + ".controller").Replace("\\", "/");
    // Menu name policy: always use the mesh name for the first-level submenu. This avoids
    // any risk of cross-mesh replacement and organizes the Modular Avatar menu per-mesh.
    string baseControllerNameForMenu = meshName;
    string controllerDir = controllerPath.Substring(0, controllerPath.LastIndexOf('/'));
    if (!AssetDatabase.IsValidFolder(controllerDir))
    {
      // Recursively create any missing directories
      string[] parts = controllerDir.Split('/');
      string cur = "Assets";
      for (int i = 1; i < parts.Length; i++)
      {
        string next = cur + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(next))
          AssetDatabase.CreateFolder(cur, parts[i]);
        cur = next;
      }
    }

    // Check whether the controller already exists
    if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
    {
      if (!EditorUtility.DisplayDialog("Overwrite?", "Controller already exists. Overwrite?", "Yes", "No"))
        return false;
      AssetDatabase.DeleteAsset(controllerPath);
    }

    // create controller
    var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

    // Remove default BaseLayer
    if (controller.layers.Length > 0)
    {
      controller.RemoveLayer(0);
    }

    string meshPrefix = !string.IsNullOrEmpty(usePrefix) ? usePrefix : (mr != null ? mr.gameObject.name + "_" : "");

    // Precompute final binding path: use the Avatar-to-Target relative path (Target must be a descendant of the Avatar)
    string computedRelative = null;
    if (avatarRoot != null && mr != null)
    {
      computedRelative = GetRelativePath(avatarRoot.transform, mr.transform);
    }
    string finalBindingPath = computedRelative ?? "";

    if (names == null || sels == null) return false;

    int localCount = 0;
    for (int i = 0; i < names.Count; i++)
    {
      if (!sels[i]) continue;
      var name = names[i];
      float globalProgress = (float)(clipIndexOffset + localCount) / Math.Max(1, totalClips);
      if (EditorUtility.DisplayCancelableProgressBar("Generating Blendshape Clips", $"{meshName}: {localCount + 1}/{names.Count} - {name}", globalProgress))
      {
        EditorUtility.ClearProgressBar();
        Debug.LogWarning("Generation cancelled by user during clip creation.");
        return true;
      }
      localCount++;
      string safeName = SanitizeName(name);
      string clipPath = Path.Combine(meshSubFolder, meshPrefix + safeName + ".anim").Replace("\\", "/");

      // Create a linear AnimationClip (0->100 over 1 second) and set it to loop
      var clip = new AnimationClip();
      clip.name = safeName;
      clip.wrapMode = WrapMode.Loop;

      // Configure loop setting (compatible across Unity versions)
      var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
      clipSettings.loopTime = true;
      AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

      // Use the precomputed finalBindingPath
      var binding = new EditorCurveBinding();
      binding.path = finalBindingPath;
      binding.type = typeof(SkinnedMeshRenderer);
      binding.propertyName = blendshapePropertyPrefix + name;

      var curve = new AnimationCurve();
      curve.AddKey(new Keyframe(0f, 0f));
      curve.AddKey(new Keyframe(1f, 100f));
      for (int k = 0; k < curve.length; k++)
      {
        AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
        AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
      }
      AnimationUtility.SetEditorCurve(clip, binding, curve);
      AssetDatabase.CreateAsset(clip, clipPath);
      // record created asset
      lastGeneratedAssets.Add(clipPath);

      // add a float parameter for future use (prefix 'BS_' first to avoid collisions and keep stable naming)
      string paramName = "BS_" + meshPrefix + safeName;
      if (!controller.parameters.Any(p => p.name == paramName))
      {
        controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
      }

      var stateMachine = new AnimatorStateMachine();
      stateMachine.name = safeName;
      AssetDatabase.AddObjectToAsset(stateMachine, controllerPath); // serialize into controller asset

      var state = stateMachine.AddState(safeName);
      state.motion = clip;
      state.timeParameterActive = true;
      state.timeParameter = paramName;

      var layer = new AnimatorControllerLayer();
      layer.name = safeName;
      layer.defaultWeight = 1f;
      layer.stateMachine = stateMachine;

      controller.AddLayer(layer);
    }

    // Create Modular Avatar menu structure under the Avatar (multi-level). Each leaf gets a ModularAvatarMenuItem.
    try
    {
      CreateMAMenu(avatarRoot, baseControllerNameForMenu, controllerPath, meshPrefix, names, sels);
    }
    catch (Exception)
    {
      // menu creation failing shouldn't block controller generation
    }

    EditorUtility.SetDirty(controller);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    lastGeneratedAssets.Add(controllerPath);

    string shownPath = computedRelative != null ? (computedRelative + " (relative to Avatar)") : "(bound to target object)";
    Debug.Log("Generated Animator Controller and clips in: " + meshSubFolder + " Binding path used: '" + shownPath + "'");
    return false;
  }

  // Prefix suggestion feature removed.

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
    var arr = s.Select(c => (char.IsLetterOrDigit(c) ? c : '_')).ToArray();
    return new string(arr);
  }

  // Create a Modular Avatar menu hierarchy under the given avatar root. The menu root will be
  // named <baseControllerName>_Menu. For each selected blendshape, a path of GameObjects is created
  // from the name split by '/', and a ModularAvatarMenuItem is attached on the leaf.
  private void CreateMAMenu(GameObject avatarRoot, string baseControllerName, string controllerPath, string meshPrefix, List<string> blendshapeNames, List<bool> selected)
  {
    if (avatarRoot == null) return;

    // 顶层菜单固定为 Blendshapes_Menu，Installer 只放在最上级
    string topMenuName = "Blendshapes_Menu";
    var existingTop = avatarRoot.transform.Find(topMenuName);
    GameObject topMenuGO;
    if (existingTop != null)
    {
      // 顶级菜单已存在：保留，不要删除
      topMenuGO = existingTop.gameObject;
    }
    else
    {
      // 不存在则创建顶级并添加 Installer
      topMenuGO = new GameObject(topMenuName);
      Undo.RegisterCreatedObjectUndo(topMenuGO, "Create MA Top Menu Root");
      topMenuGO.transform.SetParent(avatarRoot.transform, false);
      topMenuGO.AddComponent<ModularAvatarMenuInstaller>();
    }

    // Ensure top-level has a ModularAvatarMenuItem marked as SubMenu (no parameter/value set)
    try
    {
      var topItem = topMenuGO.GetComponent<ModularAvatarMenuItem>();
      if (topItem == null) topItem = topMenuGO.AddComponent<ModularAvatarMenuItem>();
      try
      {
        topItem.label = "Blendshapes";
        topItem.PortableControl.Type = PortableControlType.SubMenu;
        topItem.automaticValue = true;
        topItem.MenuSource = SubmenuSource.Children;
        // Do not set topItem.PortableControl.Parameter or Value for submenus
      }
      catch { }
    }
    catch { }

    // 在顶层下创建一个子菜单，名字为 baseControllerName
    // 如果同名子菜单已存在则删除它（我们只替换第一层子菜单）
    var existingController = topMenuGO.transform.Find(baseControllerName);
    if (existingController != null)
    {
      Undo.DestroyObjectImmediate(existingController.gameObject);
    }
    var controllerMenuGO = new GameObject(baseControllerName);
    Undo.RegisterCreatedObjectUndo(controllerMenuGO, "Create Controller Submenu");
    controllerMenuGO.transform.SetParent(topMenuGO.transform, false);

    // 标记 controllerMenuGO 为子菜单（添加一个 ModularAvatarMenuItem Type=SubMenu）
    var controllerMenuItem = controllerMenuGO.GetComponent<ModularAvatarMenuItem>();
    if (controllerMenuItem == null) controllerMenuItem = controllerMenuItem = controllerMenuGO.AddComponent<ModularAvatarMenuItem>();
    try
    {
      controllerMenuItem.PortableControl.Type = PortableControlType.SubMenu;
      controllerMenuItem.automaticValue = true;
      controllerMenuItem.MenuSource = SubmenuSource.Children;
    }
    catch (Exception)
    {
      // ignore compatibility issues
    }

    // 为每个选中的 blendshape 创建菜单项（按 '/' 切分为多级），放到 controllerMenuGO 下
    for (int i = 0; i < blendshapeNames.Count; i++)
    {
      if (!selected[i]) continue;
      var fullName = blendshapeNames[i];
      string safeName = SanitizeName(fullName);
      string paramName = "BS_" + meshPrefix + SanitizeName(fullName);

      var parts = fullName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      Transform parent = controllerMenuGO.transform;
      for (int p = 0; p < parts.Length; p++)
      {
        var part = parts[p].Trim();
        if (string.IsNullOrEmpty(part)) continue;
        var child = parent.Find(part);
        if (child == null)
        {
          var go = new GameObject(part);
          Undo.RegisterCreatedObjectUndo(go, "Create MA Menu Item");
          go.transform.SetParent(parent, false);
          child = go.transform;
        }
        // For intermediate nodes (not leaf), ensure they are marked as SubMenu and have no parameter
        var isLeaf = (p == parts.Length - 1);
        var childGo = child.gameObject;
        if (!isLeaf)
        {
          var midItem = childGo.GetComponent<ModularAvatarMenuItem>();
          if (midItem == null) midItem = childGo.AddComponent<ModularAvatarMenuItem>();
          try
          {
            midItem.PortableControl.Type = PortableControlType.SubMenu;
            midItem.automaticValue = true;
            midItem.MenuSource = SubmenuSource.Children;
            // Ensure no parameter/value are set on submenu items
            try { midItem.PortableControl.Parameter = ""; } catch { }
          }
          catch { }
        }
        parent = child;
      }

      // 在叶节点添加 ModularAvatarMenuItem（若已存在则复用）
      var item = parent.GetComponent<ModularAvatarMenuItem>();
      if (item == null) item = parent.gameObject.AddComponent<ModularAvatarMenuItem>();
      // Configure the leaf menu item to point at the generated parameter and set sensible defaults.
      try
      {
        item.PortableControl.Type = PortableControlType.RadialPuppet;
        item.PortableControl.SubParameters = ImmutableList.Create(paramName);

        // Menu item flags: keep defaults consistent with Modular Avatar expectations
        item.isSaved = true;
        item.isSynced = true;
        item.isDefault = false;
        item.automaticValue = true;
        item.MenuSource = SubmenuSource.Children;
      }
      catch (Exception)
      {
        // If PortableControl isn't available (very old package variant), ignore silently.
      }
    }

    // Add a ModularAvatarMergeAnimator on the first-level controller submenu so the generated controller
    // will be merged into the avatar's FX layer at build time. Use try/catch to remain
    // compatible with package version differences.
    try
    {
      var merge = controllerMenuGO.GetComponent<ModularAvatarMergeAnimator>();
      if (merge == null) merge = controllerMenuGO.AddComponent<ModularAvatarMergeAnimator>();

      // Load the generated controller asset and assign it.
      var runtimeController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
      if (runtimeController != null)
      {
        merge.animator = runtimeController;
      }

      // Prefer relative path mode so bindings remain relative to the avatar when possible.
      merge.pathMode = MergeAnimatorPathMode.Relative;
      merge.mergeAnimatorMode = MergeAnimatorMode.Append;

      // Set the relativePathRoot to the avatar root so any relative bindings resolve correctly.
      if (avatarRoot != null)
      {
        try { merge.relativePathRoot.Set(avatarRoot); } catch { }
      }

      // Mark that we should match avatar write defaults if available.
      try { merge.matchAvatarWriteDefaults = true; } catch { }

      // Try to explicitly set the target layer to FX. If VRC SDK symbols are available,
      // use the strongly-typed enum; otherwise fall back to a reflection-based attempt.
#if MA_VRCSDK3_AVATARS
      try
      {
        merge.layerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX;
      }
      catch { }
#else
      try
      {
        var layerTypeProp = merge.GetType().GetField("layerType");
        if (layerTypeProp != null)
        {
          var enumType = layerTypeProp.FieldType;
          var fxVal = Enum.Parse(enumType, "FX");
          layerTypeProp.SetValue(merge, fxVal);
        }
      }
      catch { /* ignore if not present or enum name differs */ }
#endif
    }
    catch (Exception)
    {
      // If the package version doesn't provide ModularAvatarMergeAnimator, skip silently.
    }

    // Ensure ModularAvatarParameters exists on the top-level menu and add missing parameter configs only.
    try
    {
      var paramsComp = topMenuGO.GetComponent<ModularAvatarParameters>();
      if (paramsComp == null) paramsComp = topMenuGO.AddComponent<ModularAvatarParameters>();

      // Build a set of existing parameter names/prefixes
      var existing = new HashSet<string>(StringComparer.Ordinal);
      foreach (var pc in paramsComp.parameters)
      {
        if (!string.IsNullOrEmpty(pc.nameOrPrefix)) existing.Add(pc.nameOrPrefix);
      }

      // For each selected blendshape, add a ParameterConfig only if missing
      for (int i = 0; i < blendshapeNames.Count; i++)
      {
        if (!selected[i]) continue;
        var fullName = blendshapeNames[i];
        string paramName = "BS_" + meshPrefix + SanitizeName(fullName);
        if (existing.Contains(paramName)) continue; // already present, skip

        var newPc = new ParameterConfig();
        newPc.nameOrPrefix = paramName;
        newPc.remapTo = "";
        newPc.internalParameter = false;
        newPc.isPrefix = false;
        newPc.syncType = ParameterSyncType.Float;
        newPc.localOnly = false;
        newPc.defaultValue = 0f;
        newPc.saved = true;
        newPc.hasExplicitDefaultValue = false;

        paramsComp.parameters.Add(newPc);
      }
    }
    catch (Exception)
    {
      // If the package version doesn't expose ModularAvatarParameters or ParameterConfig fields differ, skip.
    }
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

  // Populate meshRenderers and per-mesh blendshape name/selection lists based on avatarRoot
  private void RefreshMeshes()
  {
    RefreshMeshes(true, null);
  }

  private void RefreshMeshes(bool includeInactive, string nameFilter)
  {
    meshEntries.Clear();

    if (avatarRoot == null) return;

    // Find all SkinnedMeshRenderer components under the avatar root
    var srs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
    foreach (var sr in srs)
    {
      if (!string.IsNullOrEmpty(nameFilter) && !sr.gameObject.name.Contains(nameFilter)) continue;
      var entry = new MeshEntry();
      entry.renderer = sr;

      var names = new List<string>();
      var sels = new List<bool>();
      var mesh = sr.sharedMesh;
      if (mesh != null)
      {
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
          names.Add(mesh.GetBlendShapeName(i));
          sels.Add(true);
        }
      }
      entry.blendshapeNames = names;
      entry.selected = sels;
      entry.scroll = Vector2.zero;

      // default enable and foldout states
      entry.enabled = true;
      entry.foldout = false;

      // default prefix
      entry.prefix = sr != null ? sr.gameObject.name + "_" : "mesh_" + meshEntries.Count + "_";

      meshEntries.Add(entry);
    }
  }

  private void OnEnable()
  {
    // Ensure meshes list is populated if Avatar was set previously
    if (avatarRoot != null)
      RefreshMeshes();
  }
}
