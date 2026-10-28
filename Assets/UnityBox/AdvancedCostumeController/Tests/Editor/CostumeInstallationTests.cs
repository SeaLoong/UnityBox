using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using nadena.dev.modular_avatar.core;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class CostumeInstallationTests
  {
    private GameObject root;
    private Scene scene;
    private string folder;
    private ACCConfig config;
    private List<OutfitData> outfits;

    [SetUp]
    public void SetUp()
    {
      scene = EditorSceneManager.NewPreviewScene();
      root = new GameObject("Costumes");
      SceneManager.MoveGameObjectToScene(root, scene);
      folder = "Assets/__ACCInstallation_" + Guid.NewGuid().ToString("N");
      config = new ACCConfig { CostumesRoot = root, ParamPrefix = "Costumes", GeneratedFolder = folder, EnableParts = true };
      outfits = Enumerable.Range(0, 3).Select(index =>
      {
        var outfit = Child(root, "Outfit" + index);
        return new OutfitData { BaseObject = outfit, OutfitObject = outfit, RelativePath = outfit.name, Name = outfit.name,
          Parts = new List<GameObject> { Child(outfit, "Hat") } };
      }).ToList();
    }

    [TearDown]
    public void TearDown()
    {
      EditorUtility.ClearProgressBar();
      EditorSceneManager.ClosePreviewScene(scene);
      if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
    }

    [Test]
    public void AvatarRegenerationKeepsAssetsAndRestoresControllerWithUndoRedo()
    {
      Generate();
      var oldController = root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
      string oldPath = AssetDatabase.GetAssetPath(oldController);
      Generate();
      var replacement = root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
      Assert.That(replacement, Is.Not.EqualTo(oldController));
      Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(oldPath), Is.EqualTo(oldController));
      Undo.FlushUndoRecordObjects();
      Undo.PerformUndo();
      Assert.That(root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator, Is.EqualTo(oldController));
      Undo.PerformRedo();
      Assert.That(root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator, Is.EqualTo(replacement));
      Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(oldPath), Is.EqualTo(oldController));
    }

    [Test]
    public void WholeAvatarBudgetFailureRestoresInstalledMenuAndKeepsPreviousBuild()
    {
      Generate();
      var oldController = root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
      string oldPath = AssetDatabase.GetAssetPath(oldController);
      var otherParameters = root.AddComponent<ModularAvatarParameters>();
      for (int index = 0; index < 32; index++) otherParameters.parameters.Add(new ParameterConfig
      {
        nameOrPrefix = "Other" + index, syncType = ParameterSyncType.Float, localOnly = false
      });
      Assert.That(() => Generate(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("/256"));
      Assert.That(root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator, Is.EqualTo(oldController));
      Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(oldPath), Is.EqualTo(oldController));
      Assert.That(AssetDatabase.GetSubFolders(config.GetResolvedGeneratedFolder()).Length, Is.EqualTo(1));
      Assert.That(otherParameters.parameters.Count, Is.EqualTo(32));
    }

    [Test]
    public void CompressionEncoderAndRemoteDecoderEnterTheirActualAnimatorStates()
    {
      config.EnableParameterCompression = true;
      Generate();
      var controller = (AnimatorController)root.GetComponentInChildren<ModularAvatarMergeAnimator>().animator;
      var layerIndex = Array.FindIndex(controller.layers, layer => layer.name.EndsWith("/Parameter Compression", StringComparison.Ordinal));
      Assert.That(layerIndex, Is.GreaterThanOrEqualTo(0));
      var machine = controller.layers[layerIndex].stateMachine;
      var animator = root.AddComponent<Animator>();
      animator.runtimeAnimatorController = controller;
      animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      animator.Rebind();
      var layout = new ChoiceParameterLayout(3, true);

      for (int value = 0; value < 3; value++)
      {
        animator.SetBool("IsLocal", true);
        animator.SetFloat(config.MainParameterName, value);
        for (int bit = 0; bit < layout.BitCount; bit++)
          animator.SetBool(layout.GetBitParameterName(config.MainParameterName, bit), (value & (1 << bit)) == 0);
        var encoder = machine.states.Select(state => state.state).Single(state => state.name == "Outfit Selection = " + value);
        AssertEnters(animator, layerIndex, encoder);
        var encoded = DriverValues(encoder);
        for (int bit = 0; bit < layout.BitCount; bit++)
          Assert.That(encoded[layout.GetBitParameterName(config.MainParameterName, bit)], Is.EqualTo((value & (1 << bit)) == 0 ? 0f : 1f));

        animator.SetBool("IsLocal", false);
        animator.SetFloat(config.MainParameterName, (value + 1) % 3);
        for (int bit = 0; bit < layout.BitCount; bit++)
          animator.SetBool(layout.GetBitParameterName(config.MainParameterName, bit), (value & (1 << bit)) != 0);
        var decoder = machine.states.Select(state => state.state).Single(state => state.name == "Outfit Selection = " + value + " (Remote)");
        AssertEnters(animator, layerIndex, decoder);
        Assert.That(DriverValues(decoder)[config.MainParameterName], Is.EqualTo(value));
      }
    }

    [Test]
    [Category("UnityBoxGraphics")]
    public void RenderedMaterialVariantIconsContainDistinctPixelsAndPreserveSource()
    {
      Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
      Directory.CreateDirectory(folder); AssetDatabase.Refresh();
      var shader = Shader.Find("Unlit/Color");
      Assert.That(shader, Is.Not.Null);
      var red = new Material(shader) { color = Color.red };
      var green = new Material(shader) { color = Color.green };
      AssetDatabase.CreateAsset(red, folder + "/Red.mat");
      AssetDatabase.CreateAsset(green, folder + "/Green.mat");
      var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
      SceneManager.MoveGameObjectToScene(cube, scene);
      cube.transform.SetParent(outfits[0].BaseObject.transform, false);
      var sourceRenderer = cube.GetComponent<MeshRenderer>();
      sourceRenderer.sharedMaterial = red;
      var variant = Child(root, "Green Variant").AddComponent<ACCVariantMaterialOverride>();
      variant.OutfitBase = outfits[0].BaseObject;
      variant.Replacements.Add(new ACCVariantMaterialOverride.MaterialReplacement { Source = red, Replacement = green });
      var menuRoot = Child(root, "Graphics Menu");
      var firstMenu = Child(menuRoot, "Red").AddComponent<ModularAvatarMenuItem>();
      var secondMenu = Child(menuRoot, "Green").AddComponent<ModularAvatarMenuItem>();
      var requestType = typeof(Generator).Assembly.GetType("UnityBox.AdvancedCostumeController.MenuIconRequest");
      var requests = (IList)System.Activator.CreateInstance(typeof(List<>).MakeGenericType(requestType));
      foreach (var definition in new[] { (Menu: firstMenu, Variant: (ACCVariantMaterialOverride)null, Key: "Red"), (Menu: secondMenu, Variant: variant, Key: "Green") })
      {
        var request = System.Activator.CreateInstance(requestType, true);
        requestType.GetField("MenuNode").SetValue(request, definition.Menu.gameObject);
        requestType.GetField("Targets").SetValue(request, new List<GameObject> { outfits[0].BaseObject });
        requestType.GetField("StableKey").SetValue(request, definition.Key);
        requestType.GetField("MaterialVariant").SetValue(request, definition.Variant);
        requests.Add(request);
      }
      var render = typeof(Generator).Assembly.GetType("UnityBox.AdvancedCostumeController.MenuIconGenerator").GetMethod("Generate", BindingFlags.Static | BindingFlags.Public);
      Func<int> capture = () => (int)render.Invoke(null, new object[] { root, menuRoot, folder, requests });
      Assert.That(capture(), Is.EqualTo(2));
      var timings = new List<double>();
      for (int sample = 0; sample < 5; sample++)
      {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(capture(), Is.EqualTo(2));
        timer.Stop(); timings.Add(timer.Elapsed.TotalMilliseconds);
      }
      Vector3 redPixels = AverageVisibleColor(firstMenu.PortableControl.Icon);
      Vector3 greenPixels = AverageVisibleColor(secondMenu.PortableControl.Icon);
      Assert.That(redPixels.x, Is.GreaterThan(redPixels.y + 0.5f));
      Assert.That(greenPixels.y, Is.GreaterThan(greenPixels.x + 0.5f));
      Assert.That(sourceRenderer.sharedMaterial, Is.EqualTo(red));
      Assert.That(cube.layer, Is.EqualTo(0));
      timings.Sort();
      Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new GraphicsBenchmark
      {
        plugin = "AdvancedCostumeController", scenario = "menu_icon_rendering_256px", size = 2, samples = 5,
        median_ms = timings[2], p95_ms = timings[4], min_ms = timings[0], max_ms = timings[4], allocated_bytes_per_op = -1,
        correctness = "two256x256RGBA icons show distinct red/green geometry; source material and layer retained"
      }));
    }

    private static Vector3 AverageVisibleColor(Texture2D icon)
    {
      Assert.That(icon, Is.Not.Null);
      var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
      try
      {
        Assert.That(decoded.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(icon))), Is.True);
        Assert.That(decoded.width, Is.EqualTo(256)); Assert.That(decoded.height, Is.EqualTo(256));
        var visible = decoded.GetPixels32().Where(pixel => pixel.a > 100).ToArray();
        Assert.That(visible.Length, Is.GreaterThan(100));
        return new Vector3((float)visible.Average(pixel => pixel.r) / 255f, (float)visible.Average(pixel => pixel.g) / 255f, (float)visible.Average(pixel => pixel.b) / 255f);
      }
      finally { UnityEngine.Object.DestroyImmediate(decoded); }
    }

    [Serializable] private class GraphicsBenchmark
    {
      public string plugin, scenario, correctness;
      public int size, samples;
      public double median_ms, p95_ms, min_ms, max_ms;
      public long allocated_bytes_per_op;
    }

    private void Generate()
    {
      var indices = outfits.Select((outfit, index) => (outfit.BaseObject, index)).ToDictionary(item => item.BaseObject, item => item.index);
      new Generator(config).Execute(outfits, indices, outfits[0], false);
    }

    private static void AssertEnters(Animator animator, int layer, AnimatorState state)
    {
      for (int frame = 0; frame < 16; frame++)
      {
        animator.Update(1f / 60f);
        if (animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == Animator.StringToHash(state.name)) return;
      }
      Assert.Fail("Animator did not enter " + state.name);
    }

    private static Dictionary<string, float> DriverValues(AnimatorState state)
    {
      var driver = state.behaviours.Single(behaviour => behaviour.GetType().Name == "VRCAvatarParameterDriver");
      var values = (IEnumerable)driver.GetType().GetField("parameters").GetValue(driver);
      return values.Cast<object>().ToDictionary(value => (string)value.GetType().GetField("name").GetValue(value),
        value => (float)value.GetType().GetField("value").GetValue(value));
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var result = new GameObject(name);
      SceneManager.MoveGameObjectToScene(result, parent.scene);
      result.transform.SetParent(parent.transform, false);
      return result;
    }
  }
}
