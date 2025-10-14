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
  private string rendererRelativePath = "";
  private Vector2 _scroll;
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

    var prevRenderer = targetRenderer;
    targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target SkinnedMeshRenderer", targetRenderer, typeof(SkinnedMeshRenderer), true);
    if (targetRenderer != null && targetRenderer != prevRenderer)
    {
      rendererRelativePath = GetTransformPath(targetRenderer.transform);
      if (string.IsNullOrWhiteSpace(controllerName))
        controllerName = targetRenderer.gameObject.name;
      RefreshBlendshapes();
    }
    rendererRelativePath = EditorGUILayout.TextField("Renderer Path", rendererRelativePath);

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
      _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200));
      for (int i = 0; i < blendshapeNames.Count; i++)
      {
        selected[i] = EditorGUILayout.ToggleLeft(blendshapeNames[i], selected[i]);
      }
      EditorGUILayout.EndScrollView();
    }

    EditorGUILayout.Space();
    EditorGUILayout.HelpBox(
        "生成器将为每个勾选的 Blendshape 创建一个 AnimationClip（线性从 0 到 100）。同时生成一个 Animator Controller（每个Blendshape一个Layer，float参数控制）。\nRenderer Path 用于指定动画曲线绑定到哪个SkinnedMeshRenderer（如 'Body/Head'），需与Animator结构一致。",
        MessageType.Info);

    if (GUILayout.Button("Generate Controller"))
    {
      if (targetRenderer == null || string.IsNullOrEmpty(rendererRelativePath))
      {
        EditorUtility.DisplayDialog("Missing fields", "请指定SkinnedMeshRenderer和Renderer Path。", "OK");
      }
      else
      {
        Generate();
      }
    }
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

    // 自动填充路径（只在刷新时覆盖）
    rendererRelativePath = GetTransformPath(targetRenderer.transform);
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

    string meshPrefix = targetRenderer != null ? targetRenderer.gameObject.name + "__" : "";
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

      var binding = new EditorCurveBinding();
      binding.path = rendererRelativePath;
      binding.type = typeof(SkinnedMeshRenderer);
      binding.propertyName = "blendShape." + name;

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

      // add a float parameter for future use
      string paramName = "BS_" + safeName;
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

    EditorUtility.DisplayDialog("Done", "Generated Animator Controller and clips in: \n" + outputFolder, "OK");
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
