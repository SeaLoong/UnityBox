using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class BlendshapeRuntimeTests
{
  private GameObject root;
  private BlendshapeControllerGenerator window;
  private IBlendshapeAvatarInstaller previousInstaller;
  private Scene scene;
  private string folder;
  private readonly List<Mesh> meshes = new List<Mesh>();

  [SetUp]
  public void SetUp()
  {
    previousInstaller = BlendshapeAvatarIntegration.Installer;
    BlendshapeAvatarIntegration.Installer = null;
    scene = EditorSceneManager.NewPreviewScene();
    root = new GameObject("Blendshape Runtime");
    SceneManager.MoveGameObjectToScene(root, scene);
    folder = "Assets/__BlendshapeRuntime_" + Guid.NewGuid().ToString("N");
    window = CreateWindow(root, folder);
  }

  [TearDown]
  public void TearDown()
  {
    BlendshapeAvatarIntegration.Installer = previousInstaller;
    EditorUtility.ClearProgressBar();
    Object.DestroyImmediate(window);
    EditorSceneManager.ClosePreviewScene(scene);
    foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
    if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void RealAnimatorUpdatesBooleanAndInvertedWeights(bool invert)
  {
    var renderer = CreateRenderer(root, "Face", 1); meshes.Add(renderer.sharedMesh);
    var controller = Generate(window, renderer, true, invert, "Face_");
    var animator = ConfigureAnimator(root, controller);
    var parameter = controller.parameters.Single().name;
    foreach (bool value in new[] { false, true, false, true })
    {
      animator.SetBool(parameter, value);
      Advance(animator);
      Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(value != invert ? 100f : 0f).Within(0.02f));
    }
  }

  [Test]
  public void RealAnimatorSamplesFloatCurveAtRequestedNormalizedTimes()
  {
    var renderer = CreateRenderer(root, "Face", 1); meshes.Add(renderer.sharedMesh);
    var controller = Generate(window, renderer, false, false, "Face_");
    var animator = ConfigureAnimator(root, controller);
    foreach (float value in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f, 0.1f })
    {
      animator.SetFloat(controller.parameters.Single().name, value);
      Advance(animator);
      Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(value * 100f).Within(0.02f));
    }
  }

  [Test]
  public void CombinedControllerRunsIndependentRenderersAfterAssetReload()
  {
    var first = CreateRenderer(root, "Face", 1); meshes.Add(first.sharedMesh);
    var second = CreateRenderer(root, "Body", 1); meshes.Add(second.sharedMesh);
    var firstController = Generate(window, first, false, false, "Face_");
    var secondController = Generate(window, second, true, true, "Body_");
    string firstJson = EditorJsonUtility.ToJson(firstController);
    string secondJson = EditorJsonUtility.ToJson(secondController);
    var combined = BlendshapeControllerComposer.Combine(new[] { firstController, secondController }, folder + "/Combined.controller");
    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(combined), ImportAssetOptions.ForceUpdate);
    combined = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Combined.controller");
    Assert.That(combined.layers.All(layer => layer.syncedLayerIndex == -1), Is.True);
    var animator = ConfigureAnimator(root, combined);
    animator.SetFloat(firstController.parameters.Single().name, 0.65f);
    animator.SetBool(secondController.parameters.Single().name, true);
    Advance(animator);
    Assert.That(first.GetBlendShapeWeight(0), Is.EqualTo(65f).Within(0.02f));
    Assert.That(second.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.02f));
    animator.SetBool(secondController.parameters.Single().name, false);
    Advance(animator);
    Assert.That(first.GetBlendShapeWeight(0), Is.EqualTo(65f).Within(0.02f));
    Assert.That(second.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.02f));
    Assert.That(EditorJsonUtility.ToJson(firstController), Is.EqualTo(firstJson));
    Assert.That(EditorJsonUtility.ToJson(secondController), Is.EqualTo(secondJson));
  }

  [Test]
  public void CombiningControllersReportsDuplicateAnimationBindingsBeforeWritingOutput()
  {
    var renderer = CreateRenderer(root, "Face", 1); meshes.Add(renderer.sharedMesh);
    var first = Generate(window, renderer, false, false, "First_");
    var second = Generate(window, renderer, false, false, "Second_");
    Assert.That(() => BlendshapeControllerComposer.Combine(new[] { first, second }, folder + "/Conflict.controller"),
      Throws.ArgumentException.With.Message.Contains("same binding"));
    Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Conflict.controller"), Is.Null);
  }

  [Test]
  public void CombiningControllersReportsConflictingParameterTypes()
  {
    var firstRenderer = CreateRenderer(root, "Face", 1); meshes.Add(firstRenderer.sharedMesh);
    var secondRenderer = CreateRenderer(root, "Body", 1); meshes.Add(secondRenderer.sharedMesh);
    var first = Generate(window, firstRenderer, false, false, "Shared_");
    var second = Generate(window, secondRenderer, true, false, "Shared_");
    Assert.That(() => BlendshapeControllerComposer.Combine(new[] { first, second }, folder + "/Conflict.controller"),
      Throws.ArgumentException.With.Message.Contains("different types"));
  }

  [Test]
  public void CombinedOutputKeepsTheInputAssetAndItsGuid()
  {
    var renderer = CreateRenderer(root, "Face", 1); meshes.Add(renderer.sharedMesh);
    var controller = Generate(window, renderer, false, false, "Face_");
    string path = AssetDatabase.GetAssetPath(controller);
    string guid = AssetDatabase.AssetPathToGUID(path);
    string serialized = EditorJsonUtility.ToJson(controller);
    Assert.That(() => BlendshapeControllerComposer.Combine(new[] { controller }, path),
      Throws.ArgumentException.With.Message.Contains("different from the input"));
    Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
    Assert.That(EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<AnimatorController>(path)), Is.EqualTo(serialized));
  }

  [Test]
  public void WindowGeneratesCombinedControllerAndAssignsWithUndo()
  {
    var first = CreateRenderer(root, "Face", 1); meshes.Add(first.sharedMesh);
    var second = CreateRenderer(root, "Body", 1); meshes.Add(second.sharedMesh);
    SetEntries(first, second);
    typeof(BlendshapeControllerGenerator).GetMethod("GenerateInternal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
    Assert.That(window.GeneratedController.parameters.Length, Is.EqualTo(2));
    Assert.That(window.GeneratedController.layers.Length, Is.EqualTo(2));
    var original = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Original.controller");
    var animator = root.AddComponent<Animator>();
    animator.runtimeAnimatorController = original;
    Undo.IncrementCurrentGroup();
    window.AssignGeneratedController();
    Undo.FlushUndoRecordObjects();
    Assert.That(animator.runtimeAnimatorController, Is.EqualTo(window.GeneratedController));
    Undo.PerformUndo();
    Assert.That(animator.runtimeAnimatorController, Is.EqualTo(original));
    Undo.PerformRedo();
    Assert.That(animator.runtimeAnimatorController, Is.EqualTo(window.GeneratedController));
  }

  [Test]
  public void WindowReportsRepeatedSelectionOfTheSameBlendshapeBeforeGeneration()
  {
    var renderer = CreateRenderer(root, "Face", 1); meshes.Add(renderer.sharedMesh);
    SetEntries(renderer, renderer);
    object[] arguments = { null };
    bool valid = (bool)typeof(BlendshapeControllerGenerator).GetMethod("ValidateGeneration", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, arguments);
    Assert.That(valid, Is.False);
    Assert.That((string)arguments[0], Does.Contain("selected more than once"));
    Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
  }

  private void SetEntries(params SkinnedMeshRenderer[] renderers)
  {
    var entryType = typeof(BlendshapeControllerGenerator).GetNestedType("MeshEntry", BindingFlags.NonPublic);
    var parameterType = typeof(BlendshapeControllerGenerator).GetNestedType("BlendParamType", BindingFlags.NonPublic);
    var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
    foreach (var renderer in renderers)
    {
      var entry = Activator.CreateInstance(entryType, true);
      var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName).ToList();
      var types = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(parameterType));
      foreach (var name in names) types.Add(Enum.Parse(parameterType, "Float"));
      entryType.GetField("renderer").SetValue(entry, renderer);
      entryType.GetField("blendshapeNames").SetValue(entry, names);
      entryType.GetField("selected").SetValue(entry, Enumerable.Repeat(true, names.Count).ToList());
      entryType.GetField("paramTypes").SetValue(entry, types);
      entryType.GetField("prefix").SetValue(entry, renderer.name + "_");
      entries.Add(entry);
    }
    Set(window, "meshEntries", entries);
  }

  [TestCase(4)]
  [TestCase(16)]
  [TestCase(64)]
  [Category("UnityBoxPerformance")]
  public void MeasurePersistentControllerGeneration(int shapes)
  {
    var renderer = CreateRenderer(root, "Face", shapes); meshes.Add(renderer.sharedMesh);
    Generate(window, renderer, false, false, "Face_");
    var timings = new List<double>();
    bool allocationsAvailable = HasAllocationCounter();
    long allocated = 0;
    for (int sample = 0; sample < 5; sample++)
    {
      long before = GC.GetAllocatedBytesForCurrentThread();
      var timer = Stopwatch.StartNew();
      var controller = Generate(window, renderer, false, false, "Face_");
      timer.Stop();
      allocated += GC.GetAllocatedBytesForCurrentThread() - before;
      timings.Add(timer.Elapsed.TotalMilliseconds);
      Assert.That(controller.layers.Length, Is.EqualTo(shapes));
      Assert.That(controller.animationClips.Length, Is.EqualTo(shapes));
    }
    timings.Sort();
    UnityEngine.Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Benchmark
    {
      plugin = "BlendshapeControllerGenerator", scenario = "persistent_float_controller_generation", size = shapes,
      samples = timings.Count, median_ms = timings[2], p95_ms = timings[4], min_ms = timings[0], max_ms = timings[4],
      allocated_bytes_per_op = allocationsAvailable ? allocated / timings.Count : -1, correctness = "one independent layer, parameter and clip per selected shape"
    }));
    var finalController = (AnimatorController)typeof(BlendshapeControllerGenerator).GetField("lastMeshController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
    var animator = ConfigureAnimator(root, finalController);
    string input = finalController.parameters[0].name;
    for (int frame = 0; frame < 128; frame++) animator.Update(1f / 60f);
    timings.Clear(); allocated = 0;
    const int framesPerSample = 512;
    for (int sample = 0; sample < 9; sample++)
    {
      long before = GC.GetAllocatedBytesForCurrentThread();
      var timer = Stopwatch.StartNew();
      for (int frame = 0; frame < framesPerSample; frame++)
      {
        animator.SetFloat(input, (frame % 101) / 100f);
        animator.Update(1f / 60f);
      }
      timer.Stop();
      timings.Add(timer.Elapsed.TotalMilliseconds / framesPerSample);
      allocated += GC.GetAllocatedBytesForCurrentThread() - before;
    }
    Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo((framesPerSample - 1) % 101).Within(0.02f));
    timings.Sort();
    UnityEngine.Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Benchmark
    {
      plugin = "BlendshapeControllerGenerator", scenario = "animator_update", size = shapes,
      samples = timings.Count, median_ms = timings[4], p95_ms = timings[8], min_ms = timings[0], max_ms = timings[8],
      allocated_bytes_per_op = allocationsAvailable ? allocated / (timings.Count * framesPerSample) : -1, correctness = "real Animator updates normalized Float input and all generated layers"
    }));
  }

  // Called by the shared standalone Player probe; all meshes/controllers become build assets.
  public static void ConfigurePlayerProbe(GameObject probeRoot, string assetFolder)
  {
    var target = new GameObject("Blendshape");
    target.transform.SetParent(probeRoot.transform, false);
    var face = CreateRenderer(target, "Face", 1);
    var body = CreateRenderer(target, "Body", 1);
    AssetDatabase.CreateAsset(face.sharedMesh, assetFolder + "/BlendshapeFace.asset");
    AssetDatabase.CreateAsset(body.sharedMesh, assetFolder + "/BlendshapeBody.asset");
    var generator = CreateWindow(target, assetFolder + "/Blendshapes");
    Set(generator, "installAvatarControls", false);
    try
    {
      var first = Generate(generator, face, false, false, "Face_");
      var second = Generate(generator, body, true, true, "Body_");
      var combined = BlendshapeControllerComposer.Combine(new[] { first, second }, assetFolder + "/BlendshapesCombined.controller");
      ConfigureAnimator(target, combined);
      probeRoot.SendMessage("AddScenarioJson", JsonUtility.ToJson(new Scenario
      {
        name = "Blendshape Float and inverted Bool across combined renderers", animator_paths = new[] { "Blendshape" },
        steps = new[]
        {
          Step(0.25f, false, 25f, 100f), Step(0.75f, true, 75f, 0f), Step(1f, false, 100f, 100f), Step(0f, true, 0f, 0f)
        }
      }), SendMessageOptions.RequireReceiver);
    }
    finally { Object.DestroyImmediate(generator); }
  }

  private static ScenarioStep Step(float value, bool toggle, float first, float second) => new ScenarioStep
  {
    frames = 4, delta_time = 1f / 60f,
    inputs = new[]
    {
      new Input { animator_path = "Blendshape", name = "Test_Face_Shape000", type = "float", value = value },
      new Input { animator_path = "Blendshape", name = "Test_Body_Shape000", type = "bool", value = toggle ? 1f : 0f }
    },
    checks = new[]
    {
      new Check { path = "Blendshape/Face", property = "blendshape", index = 0, expected = first, tolerance = 0.02f },
      new Check { path = "Blendshape/Body", property = "blendshape", index = 0, expected = second, tolerance = 0.02f }
    }
  };

  private static BlendshapeControllerGenerator CreateWindow(GameObject target, string output)
  {
    var result = ScriptableObject.CreateInstance<BlendshapeControllerGenerator>();
    Set(result, "avatarRoot", target); Set(result, "outputFolder", output); Set(result, "parameterPrefix", "Test_");
    Set(result, "floatCurve", AnimationCurve.Linear(0, 0, 1, 1)); Set(result, "floatCurveDuration", 1f); Set(result, "floatValueScale", 100f); Set(result, "floatCurveLoop", false);
    return result;
  }

  private static AnimatorController Generate(BlendshapeControllerGenerator generator, SkinnedMeshRenderer renderer,
    bool boolean, bool invert, string prefix)
  {
    var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName).ToList();
    var enumType = typeof(BlendshapeControllerGenerator).GetNestedType("BlendParamType", BindingFlags.NonPublic);
    var types = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(enumType));
    foreach (var name in names) types.Add(Enum.Parse(enumType, boolean ? "Bool" : "Float"));
    bool cancelled = (bool)typeof(BlendshapeControllerGenerator).GetMethod("GenerateForMesh", BindingFlags.Instance | BindingFlags.NonPublic)
      .Invoke(generator, new object[] { renderer, names, Enumerable.Repeat(true, names.Count).ToList(), types,
        Enumerable.Repeat(invert, names.Count).ToList(), Enumerable.Repeat(0f, names.Count).ToList(),
        Enumerable.Repeat(100f, names.Count).ToList(), 0, names.Count * (boolean ? 2 : 1), prefix });
    Assert.That(cancelled, Is.False);
    return (AnimatorController)typeof(BlendshapeControllerGenerator).GetField("lastMeshController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(generator);
  }

  private static SkinnedMeshRenderer CreateRenderer(GameObject parent, string name, int count)
  {
    var node = new GameObject(name);
    SceneManager.MoveGameObjectToScene(node, parent.scene);
    node.transform.SetParent(parent.transform, false);
    var renderer = node.AddComponent<SkinnedMeshRenderer>();
    var mesh = new Mesh { vertices = new[] { Vector3.zero }, bounds = new Bounds(Vector3.zero, Vector3.one * 10f) };
    for (int index = 0; index < count; index++)
      mesh.AddBlendShapeFrame("Shape" + index.ToString("D3"), 100f, new[] { Vector3.up }, new[] { Vector3.zero }, new[] { Vector3.zero });
    renderer.sharedMesh = mesh;
    renderer.updateWhenOffscreen = true;
    return renderer;
  }

  private static Animator ConfigureAnimator(GameObject target, AnimatorController controller)
  {
    var animator = target.AddComponent<Animator>();
    animator.runtimeAnimatorController = controller;
    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    animator.Rebind();
    Advance(animator);
    return animator;
  }

  private static void Advance(Animator animator) { for (int i = 0; i < 4; i++) animator.Update(1f / 60f); }
  private static void Set(BlendshapeControllerGenerator generator, string name, object value) =>
    typeof(BlendshapeControllerGenerator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(generator, value);

  private static bool HasAllocationCounter()
  {
    long before = GC.GetAllocatedBytesForCurrentThread();
    var probe = new byte[4096];
    long after = GC.GetAllocatedBytesForCurrentThread();
    GC.KeepAlive(probe);
    return after > before;
  }

  [Serializable] private class Benchmark { public string plugin, scenario, correctness; public int size, samples; public double median_ms, p95_ms, min_ms, max_ms; public long allocated_bytes_per_op; }
  [Serializable] private class Scenario { public string name; public string[] animator_paths; public ScenarioStep[] steps; }
  [Serializable] private class ScenarioStep { public int frames; public float delta_time; public Input[] inputs; public Check[] checks; }
  [Serializable] private class Input { public string animator_path, name, type; public float value; }
  [Serializable] private class Check { public string path, property; public int index; public float expected, tolerance; }
}
