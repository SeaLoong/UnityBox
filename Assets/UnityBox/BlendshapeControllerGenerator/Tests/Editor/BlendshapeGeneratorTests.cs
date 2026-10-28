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
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

public class BlendshapeGeneratorTests
{
  private BlendshapeControllerGenerator window;
  private GameObject avatar;
  private Scene testScene;
  private string folder;
  private readonly List<Mesh> meshes = new List<Mesh>();
  private readonly List<ScriptableObject> transientAssets = new List<ScriptableObject>();

  [SetUp]
  public void SetUp()
  {
    testScene = EditorSceneManager.NewPreviewScene();
    avatar = new GameObject("Avatar");
    SceneManager.MoveGameObjectToScene(avatar, testScene);
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
    if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
    foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
    meshes.Clear();
    foreach (var asset in transientAssets) UnityEngine.Object.DestroyImmediate(asset);
    transientAssets.Clear();
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
    var firstParent = Child(avatar.transform, "A");
    var secondParent = Child(avatar.transform, "B");
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
  public void ExistingAvatarParametersAreIncludedInThe256BitBudget()
  {
    var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
    descriptor.expressionParameters = ExpressionParameters(32);
    var error = Assert.Throws<TargetInvocationException>(() =>
      Generate(Renderer(avatar.transform, "Face", "Smile"), "Bool", false, "Face_"));
    Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
    Assert.That(error.InnerException.Message, Does.Contain("257/256"));
    Assert.That(avatar.transform.Find("Blendshapes Menu"), Is.Null);
    Assert.That(descriptor.expressionParameters.parameters.Length, Is.EqualTo(32));
  }

  [Test]
  public void RegenerationRecomputesBudgetFromTheReplacementType()
  {
    var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
    descriptor.expressionParameters = ExpressionParameters(31);
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    Generate(renderer, "Float", false, "Face_");
    Generate(renderer, "Bool", false, "Face_");
    var declared = avatar.GetComponentsInChildren<ModularAvatarParameters>().SelectMany(item => item.parameters).ToArray();
    Assert.That(declared.Length, Is.EqualTo(1));
    Assert.That(declared[0].syncType, Is.EqualTo(ParameterSyncType.Bool));
    Assert.That(avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>().Length, Is.EqualTo(1));
  }

  [Test]
  public void InstallingControlsSupportsOneStepUndoAndRedo()
  {
    Generate(Renderer(avatar.transform, "Face", "Smile"), "Float", false, "Face_");
    var controller = avatar.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
    Undo.FlushUndoRecordObjects();
    Undo.PerformUndo();
    Assert.That(avatar.transform.Find("Blendshapes Menu"), Is.Null);
    Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(controller)), Is.Not.Null);
    Undo.PerformRedo();
    Assert.That(avatar.GetComponentInChildren<ModularAvatarMergeAnimator>().animator, Is.EqualTo(controller));
  }

  [Test]
  public void OverBudgetReplacementRestoresTheInstalledController()
  {
    var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
    descriptor.expressionParameters = ExpressionParameters(31);
    var renderer = Renderer(avatar.transform, "Face", "Smile");
    Generate(renderer, "Bool", false, "Face_");
    var original = avatar.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
    descriptor.expressionParameters = ExpressionParameters(32);
    var error = Assert.Throws<TargetInvocationException>(() => Generate(renderer, "Float", false, "Face_"));
    Assert.That(error.InnerException.Message, Does.Contain("264/256"));
    var current = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>();
    Assert.That(current.Length, Is.EqualTo(1));
    Assert.That(current[0].animator, Is.EqualTo(original));
    Assert.That(avatar.GetComponentInChildren<ModularAvatarParameters>().parameters.Single().syncType, Is.EqualTo(ParameterSyncType.Bool));
  }

  [Test]
  public void RemovingOneRendererControlsKeepsTheOtherAndSupportsUndo()
  {
    Generate(Renderer(avatar.transform, "Face", "Smile"), "Float", false, "Face_");
    Generate(Renderer(avatar.transform, "Body", "Slim"), "Float", false, "Body_");
    var target = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>().Single(merge =>
      merge.animator.animationClips.SelectMany(AnimationUtility.GetCurveBindings).Any(binding => binding.path == "Face"));
    new ModularAvatarBlendshapeInstaller().Uninstall(avatar, new[] { target.gameObject.name });
    var remaining = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>();
    Assert.That(remaining.Length, Is.EqualTo(1));
    Assert.That(remaining[0].animator.animationClips.SelectMany(AnimationUtility.GetCurveBindings).All(binding => binding.path == "Body"), Is.True);
    Undo.FlushUndoRecordObjects();
    Undo.PerformUndo();
    Assert.That(avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>().Length, Is.EqualTo(2));
  }

  private VRCExpressionParameters ExpressionParameters(int floats)
  {
    var result = ScriptableObject.CreateInstance<VRCExpressionParameters>();
    transientAssets.Add(result);
    result.parameters = Enumerable.Range(0, floats).Select(index => new VRCExpressionParameters.Parameter
    {
      name = "Existing" + index, valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true
    }).ToArray();
    return result;
  }

  private static GameObject Child(Transform parent, string name)
  {
    var child = new GameObject(name);
    SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
    child.transform.SetParent(parent, false);
    return child;
  }

  private SkinnedMeshRenderer Renderer(Transform parent, string name, params string[] shapes)
  {
    var go = Child(parent, name);
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
    var types = (IList)System.Activator.CreateInstance(typeof(List<>).MakeGenericType(enumType));
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
