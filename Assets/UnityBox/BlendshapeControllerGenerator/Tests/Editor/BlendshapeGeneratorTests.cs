using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BlendshapeGeneratorTests
{
  private BlendshapeControllerGenerator window;
  private GameObject avatar;
  private Scene previousScene;
  private Scene testScene;
  private string folder;
  private readonly List<Mesh> meshes = new List<Mesh>();

  [SetUp]
  public void SetUp()
  {
    previousScene = SceneManager.GetActiveScene();
    testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
    SceneManager.SetActiveScene(testScene);
    avatar = new GameObject("Avatar");
    window = ScriptableObject.CreateInstance<BlendshapeControllerGenerator>();
    folder = "Assets/__BlendshapeTests_" + Guid.NewGuid().ToString("N");
    SetField("avatarRoot", avatar);
    SetField("outputFolder", folder);
    SetField("parameterPrefix", "Test_");
    SetField("floatCurve", AnimationCurve.Linear(0, 0, 1, 1));
    SetField("floatCurveDuration", 1f);
    SetField("floatValueScale", 100f);
  }

  [TearDown]
  public void TearDown()
  {
    EditorUtility.ClearProgressBar();
    if (window != null) UnityEngine.Object.DestroyImmediate(window);
    if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
    if (testScene.IsValid()) EditorSceneManager.CloseScene(testScene, true);
    foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
    meshes.Clear();
    if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void BoolInversionChangesFalseAndTrueWeights(bool invert)
  {
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    Generate(renderer, "Bool", invert, "Face_");
    var merge = avatar.GetComponentInChildren<ModularAvatarMergeAnimator>();
    var controller = (AnimatorController)merge.animator;
    var machine = controller.layers.Single().stateMachine;
    Assert.That(Weight(machine.defaultState.motion), Is.EqualTo(invert ? 100f : 0f));
    var trueTransition = machine.anyStateTransitions.Single(t => t.conditions[0].mode == AnimatorConditionMode.If);
    Assert.That(Weight(trueTransition.destinationState.motion), Is.EqualTo(invert ? 0f : 100f));
    Assert.That(machine.states.All(state => !state.state.writeDefaultValues), Is.True);
    Assert.That(machine.anyStateTransitions.All(t => !t.canTransitionToSelf), Is.True);
    Assert.That(merge.pathMode, Is.EqualTo(MergeAnimatorPathMode.Relative));
  }

  [Test]
  public void SameNamedMeshesGetSeparateAssetsMenusAndBindings()
  {
    var firstParent = new GameObject("A"); firstParent.transform.SetParent(avatar.transform);
    var secondParent = new GameObject("B"); secondParent.transform.SetParent(avatar.transform);
    Generate(Renderer(firstParent.transform, "Face", "Smile"), "Float", false, "Face_");
    Generate(Renderer(secondParent.transform, "Face", "Smile"), "Float", false, "Face2_");
    var merges = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>();
    Assert.That(merges, Has.Length.EqualTo(2));
    Assert.That(merges.Select(m => AssetDatabase.GetAssetPath(m.animator)).Distinct().Count(), Is.EqualTo(2));
    var paths = merges.SelectMany(m => m.animator.animationClips)
      .SelectMany(AnimationUtility.GetCurveBindings).Select(b => b.path).Distinct().ToArray();
    Assert.That(paths, Is.EquivalentTo(new[] { "A/Face", "B/Face" }));
  }

  [Test]
  public void RegenerationReplacesParameterTypeAndKeepsOldAssetsForUndo()
  {
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    Generate(renderer, "Bool", false, "Face_");
    var oldPath = AssetDatabase.GetAssetPath(avatar.GetComponentInChildren<ModularAvatarMergeAnimator>().animator);
    // Reproduce the legacy generator's declarations on the shared menu root.
    var oldParameters = avatar.GetComponentInChildren<ModularAvatarParameters>();
    var top = avatar.transform.Find("Blendshapes Menu").gameObject;
    top.AddComponent<ModularAvatarParameters>().parameters.AddRange(oldParameters.parameters);
    UnityEngine.Object.DestroyImmediate(oldParameters);
    Generate(renderer, "Float", false, "Face_");
    Assert.That(avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>(), Has.Length.EqualTo(1));
    var parameters = avatar.GetComponentsInChildren<ModularAvatarParameters>().SelectMany(p => p.parameters).ToList();
    Assert.That(parameters, Has.Count.EqualTo(1));
    Assert.That(parameters[0].syncType, Is.EqualTo(ParameterSyncType.Float));
    Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(oldPath), Is.Not.Null);
  }

  [Test]
  public void ParentShapeNameAndNestedShapeProduceSeparateControls()
  {
    Generate(Renderer(avatar.transform, "Face", "Smile", "Smile/Big"), "Float", false, "Face_");
    var items = avatar.GetComponentsInChildren<ModularAvatarMenuItem>();
    Assert.That(items.Count(item => item.PortableControl.Type == PortableControlType.RadialPuppet), Is.EqualTo(2));
    Assert.That(items.Where(item => item.PortableControl.Type == PortableControlType.RadialPuppet)
      .Select(item => item.PortableControl.SubParameters[0]).Distinct().Count(), Is.EqualTo(2));
  }

  [Test]
  public void SanitizedShapeNamesNeverShareParameters()
  {
    var names = new[] { "A/B", "A_B", "A_B_2" };
    var result = (string[])InvokeStatic("BuildParameterSuffixes", (object)names);
    Assert.That(result.Distinct().Count(), Is.EqualTo(3));
    Assert.That(result[2], Is.EqualTo("A_B_2"));
  }

  [TestCase("Assets/../Outside", false)]
  [TestCase("/tmp/Assets", false)]
  [TestCase("Assets\\Output", false)]
  [TestCase("Assets//Output", false)]
  [TestCase("Assets/./Output", false)]
  [TestCase("Assets/Output:Other", false)]
  [TestCase("Assets/Output", true)]
  public void OutputMustStayInsideAssets(string path, bool expected)
  {
    Assert.That((bool)InvokeStatic("IsSafeOutputFolder", path), Is.EqualTo(expected));
  }

  [Test]
  public void ZeroValueScalePreservesStepTangentsWithoutCreatingNaN()
  {
    var source = new AnimationCurve(
      new Keyframe(0f, 0f, float.PositiveInfinity, float.PositiveInfinity),
      new Keyframe(1f, 1f, float.PositiveInfinity, float.PositiveInfinity));
    var scaled = (AnimationCurve)InvokeStatic("BuildScaledCurve", source, 2f, 0f);

    Assert.That(scaled.keys.Select(key => key.time), Is.EqualTo(new[] { 0f, 2f }));
    Assert.That(scaled.keys.All(key => key.value == 0f), Is.True);
    Assert.That(scaled.keys.All(key => !float.IsNaN(key.inTangent) &&
      !float.IsNaN(key.outTangent)), Is.True);
    Assert.That(scaled.keys.All(key => float.IsPositiveInfinity(key.inTangent) &&
      float.IsPositiveInfinity(key.outTangent)), Is.True);
    Assert.That(scaled.Evaluate(1f), Is.EqualTo(0f));
  }

  private SkinnedMeshRenderer Renderer(Transform parent, string name, params string[] shapes)
  {
    var go = new GameObject(name); go.transform.SetParent(parent, false);
    var renderer = go.AddComponent<SkinnedMeshRenderer>();
    var mesh = new Mesh { vertices = new[] { Vector3.zero } };
    foreach (var shape in shapes)
      mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.up }, new[] { Vector3.zero }, new[] { Vector3.zero });
    meshes.Add(mesh);
    renderer.sharedMesh = mesh;
    return renderer;
  }

  private void Generate(SkinnedMeshRenderer renderer, string type, bool invert, string prefix)
  {
    var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName).ToList();
    var enumType = typeof(BlendshapeControllerGenerator).GetNestedType("BlendParamType", BindingFlags.NonPublic);
    var types = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(enumType));
    foreach (var name in names) types.Add(Enum.Parse(enumType, type));
    var method = typeof(BlendshapeControllerGenerator).GetMethod("GenerateForMesh", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.That((bool)method.Invoke(window, new object[] { renderer, names,
      Enumerable.Repeat(true, names.Count).ToList(), types,
      Enumerable.Repeat(invert, names.Count).ToList(), Enumerable.Repeat(0f, names.Count).ToList(),
      Enumerable.Repeat(100f, names.Count).ToList(), 0, names.Count * 2, prefix }), Is.False);
  }

  private static float Weight(Motion motion)
  {
    var clip = (AnimationClip)motion;
    return AnimationUtility.GetEditorCurve(clip, AnimationUtility.GetCurveBindings(clip).Single()).Evaluate(0);
  }

  private void SetField(string name, object value)
  {
    typeof(BlendshapeControllerGenerator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, value);
  }

  private static object InvokeStatic(string name, params object[] arguments)
  {
    return typeof(BlendshapeControllerGenerator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);
  }
}
