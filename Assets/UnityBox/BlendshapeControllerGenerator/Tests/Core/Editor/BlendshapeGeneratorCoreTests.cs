using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BlendshapeGeneratorCoreTests
{
  private BlendshapeControllerGenerator window;
  private IBlendshapeAvatarInstaller originalInstaller;
  private GameObject avatar;
  private Scene previousScene;
  private Scene testScene;
  private string folder;
  private readonly List<Mesh> meshes = new List<Mesh>();

  [SetUp]
  public void SetUp()
  {
    originalInstaller = BlendshapeAvatarIntegration.Installer;
    BlendshapeAvatarIntegration.Installer = null;
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
    BlendshapeAvatarIntegration.Installer = originalInstaller;
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
  public void BoolControllerGeneratesWithoutAvatarIntegration(bool invert)
  {
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    Generate(renderer, "Bool", invert, "Face_");
    var controller = Controllers().Single();
    var machine = controller.layers.Single().stateMachine;
    Assert.That(controller.parameters.Single().type, Is.EqualTo(AnimatorControllerParameterType.Bool));
    Assert.That(Weight(machine.defaultState.motion), Is.EqualTo(invert ? 100f : 0f));
    var on = machine.anyStateTransitions.Single(t => t.conditions[0].mode == AnimatorConditionMode.If);
    Assert.That(Weight(on.destinationState.motion), Is.EqualTo(invert ? 0f : 100f));
    Assert.That(machine.states.All(state => !state.state.writeDefaultValues), Is.True);
    Assert.That(machine.anyStateTransitions.All(t => !t.canTransitionToSelf && !t.hasExitTime && t.duration == 0), Is.True);
    Assert.That(avatar.transform.childCount, Is.EqualTo(1), "Standalone generation creates assets, not menu objects.");
  }

  [Test]
  public void FloatControllerUsesNormalizedTimeAndTheRequestedCurve()
  {
    SetField("floatCurveDuration", 2f);
    SetField("floatValueScale", 60f);
    Generate(Renderer(avatar.transform, "Face", "Smile"), "Float", false, "Face_");
    var controller = Controllers().Single();
    var state = controller.layers.Single().stateMachine.defaultState;
    var clip = (AnimationClip)state.motion;
    var binding = AnimationUtility.GetCurveBindings(clip).Single();
    var curve = AnimationUtility.GetEditorCurve(clip, binding);
    Assert.That(state.timeParameterActive, Is.True);
    Assert.That(state.timeParameter, Is.EqualTo(controller.parameters.Single().name));
    Assert.That(controller.parameters.Single().type, Is.EqualTo(AnimatorControllerParameterType.Float));
    Assert.That(binding.path, Is.EqualTo("Face"));
    Assert.That(binding.propertyName, Is.EqualTo("blendShape.Smile"));
    Assert.That(clip.length, Is.EqualTo(2f).Within(0.001));
    Assert.That(curve.Evaluate(1f), Is.EqualTo(30f).Within(0.001));
    Assert.That(curve.Evaluate(2f), Is.EqualTo(60f).Within(0.001));
  }

  [Test]
  public void SameNamedMeshesKeepDistinctControllersAndBindingsWithoutIntegration()
  {
    var first = new GameObject("A"); first.transform.SetParent(avatar.transform);
    var second = new GameObject("B"); second.transform.SetParent(avatar.transform);
    Generate(Renderer(first.transform, "Face", "Smile"), "Float", false, "Face_");
    Generate(Renderer(second.transform, "Face", "Smile"), "Float", false, "Face2_");
    var controllers = Controllers();
    Assert.That(controllers, Has.Length.EqualTo(2));
    Assert.That(controllers.SelectMany(controller => controller.animationClips)
      .SelectMany(AnimationUtility.GetCurveBindings).Select(binding => binding.path).Distinct(),
      Is.EquivalentTo(new[] { "A/Face", "B/Face" }));
  }

  [Test]
  public void BindingToAnimationRootUsesEmptyRelativePath()
  {
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    SetField("avatarRoot", renderer.gameObject);
    Generate(renderer, "Float", false, "Face_");
    var binding = AnimationUtility.GetCurveBindings(Controllers().Single().animationClips.Single()).Single();
    Assert.That(binding.path, Is.Empty);
  }

  private AnimatorController[] Controllers()
  {
    return AssetDatabase.FindAssets("t:AnimatorController", new[] { folder })
      .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AnimatorController>).ToArray();
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
