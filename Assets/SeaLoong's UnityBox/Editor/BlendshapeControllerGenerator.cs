using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 生成器：为指定 SkinnedMeshRenderer 的若干 Blendshape 生成 AnimationClip 和 AnimatorController，每个Blendshape一个Layer，float参数控制。
public class BlendshapeControllerGenerator : EditorWindow
{
  private SkinnedMeshRenderer targetRenderer;
  private GameObject avatarRoot;
  private Vector2 _scroll;
  private string blendshapePropertyPrefix = "blendShape.";
  private string outputFolder = "Assets/SeaLoong's UnityBox/GeneratedBlendshapes";
  private List<string> blendshapeNames = new List<string>();
  private List<bool> selected;
  private string controllerName = "";


  [MenuItem("Tools/SeaLoong's UnityBox/Blendshape Controller Generator")]
  public static void ShowWindow()
  {
    GetWindow<BlendshapeControllerGenerator>("Blendshape Controller Generator");
  }

  private void OnGUI()
  {
    EditorGUILayout.LabelField("Blendshape Controller Generator", EditorStyles.boldLabel);

    var prevAvatarRoot = avatarRoot;
    var prevRenderer = targetRenderer;

    avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar", avatarRoot, typeof(GameObject), true);
    if (avatarRoot != null && avatarRoot != prevAvatarRoot)
    {
      targetRenderer = avatarRoot.GetComponentInChildren<SkinnedMeshRenderer>();
    }

    targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target SkinnedMeshRenderer", targetRenderer, typeof(SkinnedMeshRenderer), true);
    if (targetRenderer != null && targetRenderer != prevRenderer)
    {
      controllerName = targetRenderer.gameObject.name;
      RefreshBlendshapes();
    }

    EditorGUILayout.HelpBox("Avatar 为必需项。\n绑定路径使用 Avatar 到 Target 的相对路径（Target 必须是 Avatar 的子对象）。\n工具不会创建或修改任何 Animator 组件。", MessageType.Info);

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

    if (GUILayout.Button("Refresh Blendshapes"))
    {
      RefreshBlendshapes();
    }

    if (blendshapeNames.Count > 0)
    {
      EditorGUILayout.LabelField("Blendshapes", EditorStyles.boldLabel);
      EditorGUILayout.BeginHorizontal();
      if (GUILayout.Button("全选", GUILayout.MaxWidth(60)))
      {
        for (int i = 0; i < selected.Count; i++) selected[i] = true;
      }
      if (GUILayout.Button("全不选", GUILayout.MaxWidth(60)))
      {
        for (int i = 0; i < selected.Count; i++) selected[i] = false;
      }
      EditorGUILayout.EndHorizontal();
      _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
      for (int i = 0; i < blendshapeNames.Count; i++)
      {
        selected[i] = EditorGUILayout.ToggleLeft(blendshapeNames[i], selected[i]);
      }
      EditorGUILayout.EndScrollView();
    }

    // 检查 Avatar 与 Target 的关系
    string computedRelativePreview = null;
    if (avatarRoot != null && targetRenderer != null)
      computedRelativePreview = GetRelativePath(avatarRoot.transform, targetRenderer.transform);

    if (avatarRoot == null)
    {
      EditorGUILayout.HelpBox("请指定 Avatar（作为路径基准）。", MessageType.Error);
    }
    else if (targetRenderer != null && computedRelativePreview == null)
    {
      EditorGUILayout.HelpBox("Target 不在所选 Avatar 下。请确保 Avatar 是其祖先。", MessageType.Error);
    }

    EditorGUILayout.Space();
    EditorGUILayout.HelpBox("生成器将为每个勾选的 Blendshape 创建一个 AnimationClip（0->100），并生成对应的 Animator Controller。", MessageType.Info);

    // Only enable generation when avatarRoot and target relationships are valid
    bool avatarOk = avatarRoot != null && targetRenderer != null && computedRelativePreview != null;
    EditorGUI.BeginDisabledGroup(!avatarOk || blendshapeNames.Count == 0);
    if (GUILayout.Button("Generate Controller"))
    {
      if (targetRenderer == null)
      {
        EditorUtility.DisplayDialog("Missing fields", "请指定 SkinnedMeshRenderer。", "OK");
      }
      else
      {
        string computed = GetRelativePath(avatarRoot.transform, targetRenderer.transform);
        if (computed == null)
        {
          EditorUtility.DisplayDialog("Invalid Avatar", "Avatar 必须是 Target 的祖先。", "OK");
        }
        else
        {
          Generate();
        }
      }
    }
    EditorGUI.EndDisabledGroup();
  }

  private void RefreshBlendshapes()
  {
    blendshapeNames.Clear();
    selected = new List<bool>();
    if (targetRenderer == null)
    {
      EditorUtility.DisplayDialog("No target", "Please assign a SkinnedMeshRenderer first.", "OK");
      return;
    }

    var mesh = targetRenderer.sharedMesh;
    if (mesh == null)
    {
      EditorUtility.DisplayDialog("No mesh", "The target renderer has no sharedMesh assigned.", "OK");
      return;
    }

    int count = mesh.blendShapeCount;
    for (int i = 0; i < count; i++)
    {
      var name = mesh.GetBlendShapeName(i);
      blendshapeNames.Add(name);
      selected.Add(true);
    }
  }

  private void Generate()
  {
    // 确保父目录存在
    string meshName = targetRenderer != null ? targetRenderer.gameObject.name : "Mesh";
    string meshSubFolder = Path.Combine(outputFolder, meshName).Replace("\\", "/");
    string baseControllerName = string.IsNullOrWhiteSpace(controllerName) ? meshName : Path.GetFileNameWithoutExtension(controllerName);
    string controllerPath = Path.Combine(meshSubFolder, baseControllerName + ".controller").Replace("\\", "/");
    string controllerDir = controllerPath.Substring(0, controllerPath.LastIndexOf('/'));
    if (!AssetDatabase.IsValidFolder(controllerDir))
    {
      // 递归创建所有父目录
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

    // 检查controller是否已存在
    if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
    {
      if (!EditorUtility.DisplayDialog("Overwrite?", "Controller already exists. Overwrite?", "Yes", "No"))
        return;
      AssetDatabase.DeleteAsset(controllerPath);
    }

    var mesh = targetRenderer.sharedMesh;
    // create controller
    var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

    // 删除默认BaseLayer
    if (controller.layers.Length > 0)
    {
      controller.RemoveLayer(0);
    }

    string meshPrefix = targetRenderer != null ? targetRenderer.gameObject.name + "_" : "";

    // 预先计算最终绑定路径：使用 avatarRoot 到 target 的相对路径（目标必须为 avatarRoot 的子孙）
    string computedRelative = null;
    if (avatarRoot != null && targetRenderer != null)
    {
      computedRelative = GetRelativePath(avatarRoot.transform, targetRenderer.transform);
    }
    string finalBindingPath = computedRelative ?? "";

    for (int i = 0; i < blendshapeNames.Count; i++)
    {
      if (!selected[i]) continue;
      var name = blendshapeNames[i];
      string safeName = SanitizeName(name);
      string clipPath = Path.Combine(meshSubFolder, meshPrefix + safeName + ".anim").Replace("\\", "/");

      // 创建一个线性动画clip（0->100，1秒），并设置为循环
      var clip = new AnimationClip();
      clip.name = safeName;
      clip.wrapMode = WrapMode.Loop;

      // 设置循环属性（兼容所有Unity版本）
      var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
      clipSettings.loopTime = true;
      AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

      // 使用预先计算的 finalBindingPath
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

      // add a float parameter for future use (prefix with mesh/gameobject name to avoid collisions)
      string paramName = meshPrefix + "BS_" + safeName;
      if (!controller.parameters.Any(p => p.name == paramName))
      {
        controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
      }

      // 创建Layer和StateMachine
      var stateMachine = new AnimatorStateMachine();
      stateMachine.name = safeName;
      AssetDatabase.AddObjectToAsset(stateMachine, controllerPath); // 关键：序列化到Controller资源

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

    EditorUtility.SetDirty(controller);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    string shownPath = computedRelative != null ? (computedRelative + " (relative to Avatar)") : "(bound to target object)";
    EditorUtility.DisplayDialog("Done",
      "Generated Animator Controller and clips in: \n" + outputFolder + "\nBinding path used: '" + shownPath + "'",
      "OK");
  }

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
}
