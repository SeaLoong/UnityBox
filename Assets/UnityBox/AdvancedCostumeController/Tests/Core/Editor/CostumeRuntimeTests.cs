using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityBox.AdvancedCostumeController.Tests
{
  public class CostumeRuntimeTests
  {
    private GameObject root;
    private Scene scene;
    private string folder;
    private ICostumeAvatarIntegration previousIntegration;

    [SetUp]
    public void SetUp()
    {
      previousIntegration = CostumeAvatarIntegration.Adapter;
      CostumeAvatarIntegration.Adapter = null;
      scene = EditorSceneManager.NewPreviewScene();
      root = new GameObject("Costume Runtime");
      SceneManager.MoveGameObjectToScene(root, scene);
      folder = "Assets/__CostumeRuntime_" + Guid.NewGuid().ToString("N");
    }

    [TearDown]
    public void TearDown()
    {
      CostumeAvatarIntegration.Adapter = previousIntegration;
      EditorSceneManager.ClosePreviewScene(scene);
      if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RealAnimatorSwitchesCostumesPartsAndBothMixerModes(bool independent)
    {
      var fixture = Configure(root, folder, independent);
      foreach (var step in BuildSteps(fixture))
      {
        foreach (var input in step.inputs) fixture.Animator.SetFloat(input.name, input.value);
        for (int frame = 0; frame < step.frames; frame++) fixture.Animator.Update(step.delta_time);
        foreach (var check in step.checks)
        {
          var path = check.path.Substring(root.name.Length + 1);
          var target = root.transform.Find(path).gameObject;
          Assert.That(target.activeSelf, Is.EqualTo(check.expected > 0.5f), check.path);
        }
      }
      Assert.That(fixture.Controller.layers.Skip(1).All(layer => layer.syncedLayerIndex == -1), Is.True);
    }

    [TestCase(2)]
    [TestCase(8)]
    [TestCase(24)]
    [Category("UnityBoxPerformance")]
    public void MeasurePersistentOutfitAndPartsGeneration(int count)
    {
      var outfits = Enumerable.Range(0, count).Select(index => CreateOutfit(root, "Outfit" + index.ToString("D3"))).ToList();
      var config = new ACCConfig { CostumesRoot = root, ParamPrefix = "Costumes", GeneratedFolder = folder, EnableParts = true };
      UnityCostumeGenerator.Generate(config, outfits);
      var timings = new List<double>();
      bool allocationsAvailable = HasAllocationCounter();
      long allocated = 0;
      AnimatorController finalController = null;
      for (int sample = 0; sample < 5; sample++)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var controller = UnityCostumeGenerator.Generate(config, outfits);
        finalController = controller;
        timer.Stop();
        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        timings.Add(timer.Elapsed.TotalMilliseconds);
        Assert.That(controller.parameters.Count(parameter => parameter.name.Contains("/Parts/")), Is.EqualTo(count * 2));
        Assert.That(controller.animationClips.Length, Is.EqualTo(count * 5));
      }
      timings.Sort();
      UnityEngine.Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Benchmark
      {
        plugin = "AdvancedCostumeController", scenario = "persistent_outfit_parts_generation", size = count,
        samples = timings.Count, median_ms = timings[2], p95_ms = timings[4], min_ms = timings[0], max_ms = timings[4],
        allocated_bytes_per_op = allocationsAvailable ? allocated / timings.Count : -1, correctness = "one outfit choice and two independently controlled parts per outfit"
      }));
      var animator = root.AddComponent<Animator>();
      animator.runtimeAnimatorController = finalController;
      animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      animator.Rebind();
      for (int frame = 0; frame < 128; frame++) animator.Update(1f / 60f);
      timings.Clear(); allocated = 0;
      const int framesPerSample = 512;
      for (int sample = 0; sample < 9; sample++)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (int frame = 0; frame < framesPerSample; frame++)
        {
          animator.SetFloat(config.MainParameterName, frame % count);
          animator.Update(1f / 60f);
        }
        timer.Stop();
        timings.Add(timer.Elapsed.TotalMilliseconds / framesPerSample);
        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
      }
      int selected = (framesPerSample - 1) % count;
      Assert.That(outfits.Select((outfit, index) => outfit.BaseObject.activeSelf == (index == selected)).All(value => value), Is.True);
      timings.Sort();
      UnityEngine.Debug.Log("UNITYBOX_BENCHMARK " + JsonUtility.ToJson(new Benchmark
      {
        plugin = "AdvancedCostumeController", scenario = "animator_update", size = count,
        samples = timings.Count, median_ms = timings[4], p95_ms = timings[8], min_ms = timings[0], max_ms = timings[8],
        allocated_bytes_per_op = allocationsAvailable ? allocated / (timings.Count * framesPerSample) : -1, correctness = "real Animator changes outfit choice while preserving independent part controls"
      }));
    }

    public static void ConfigurePlayerProbe(GameObject probeRoot, string assetFolder)
    {
      foreach (bool independent in new[] { false, true })
      {
        var target = Child(probeRoot, independent ? "ACCIndependent" : "ACCShared");
        var fixture = Configure(target, assetFolder + "/" + target.name, independent);
        probeRoot.SendMessage("AddScenarioJson", JsonUtility.ToJson(new Scenario
        {
          name = "ACC costume selection, Parts and " + (independent ? "independent" : "shared") + " Mixer",
          animator_paths = new[] { target.name }, steps = BuildSteps(fixture)
        }), SendMessageOptions.RequireReceiver);
      }
    }

    private static Fixture Configure(GameObject target, string outputFolder, bool independent)
    {
      var outfits = new List<OutfitData> { CreateOutfit(target, "First"), CreateOutfit(target, "Second") };
      var config = new ACCConfig
      {
        CostumesRoot = target, ParamPrefix = "Costumes", GeneratedFolder = outputFolder,
        EnableParts = true, EnableCustomMixer = true, UseIndependentMixerPartParameters = independent
      };
      var controller = UnityCostumeGenerator.Generate(config, outfits);
      var animator = target.AddComponent<Animator>();
      animator.runtimeAnimatorController = controller;
      animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      animator.Rebind();
      return new Fixture { Root = target, Config = config, Outfits = outfits, Animator = animator, Controller = controller };
    }

    private static ScenarioStep[] BuildSteps(Fixture fixture)
    {
      var commonInputs = new List<Input>();
      for (int outfitIndex = 0; outfitIndex < fixture.Outfits.Count; outfitIndex++)
      {
        var outfit = fixture.Outfits[outfitIndex];
        for (int partIndex = 0; partIndex < outfit.GetPartControls().Count; partIndex++)
        {
          var control = outfit.GetPartControls()[partIndex];
          float value = outfitIndex == partIndex ? 1f : 0f;
          commonInputs.Add(new Input { animator_path = fixture.Root.name, name = AnimationBuilder.BuildPartParamName(fixture.Config, outfit, control), type = "float", value = value });
        }
        if (fixture.Config.UseIndependentMixerPartParameters)
          for (int slotIndex = 0; slotIndex < outfit.GetMixerPartSlots().Count; slotIndex++)
          {
            var slot = outfit.GetMixerPartSlots()[slotIndex];
            commonInputs.Add(new Input { animator_path = fixture.Root.name, name = CostumeChoiceResolver.BuildMixerSlotParamName(fixture.Config, outfit, slot), type = "float", value = outfitIndex == slotIndex ? 1f : 0f });
          }
      }

      return new[]
      {
        Step(fixture, commonInputs, 0f, true, false), Step(fixture, commonInputs, 1f, false, true),
        Step(fixture, commonInputs, 2f, true, true), Step(fixture, commonInputs, 0f, true, false),
        Step(fixture, commonInputs, 1f, false, true)
      };
    }

    private static ScenarioStep Step(Fixture fixture, List<Input> common, float choice, bool first, bool second)
    {
      var inputs = new List<Input>(common)
      {
        new Input { animator_path = fixture.Root.name, name = fixture.Config.MainParameterName, type = "float", value = choice }
      };
      return new ScenarioStep
      {
        frames = 4, delta_time = 1f / 60f, inputs = inputs.ToArray(),
        checks = new[]
        {
          CreateCheck(fixture, "First", first), CreateCheck(fixture, "Second", second),
          CreateCheck(fixture, "First/Hat", true), CreateCheck(fixture, "First/Shirt", false),
          CreateCheck(fixture, "Second/Hat", false), CreateCheck(fixture, "Second/Shirt", true)
        }
      };
    }

    private static Check CreateCheck(Fixture fixture, string path, bool active) => new Check
    {
      path = fixture.Root.name + "/" + path, property = "active", expected = active ? 1f : 0f, tolerance = 0f
    };

    private static OutfitData CreateOutfit(GameObject parent, string name)
    {
      var outfit = Child(parent, name);
      var hat = Child(outfit, "Hat"); var shirt = Child(outfit, "Shirt");
      return new OutfitData { BaseObject = outfit, OutfitObject = outfit, RelativePath = name, Name = name, Parts = new List<GameObject> { hat, shirt } };
    }

    private static GameObject Child(GameObject parent, string name)
    {
      var result = new GameObject(name);
      SceneManager.MoveGameObjectToScene(result, parent.scene);
      result.transform.SetParent(parent.transform, false);
      return result;
    }

    private class Fixture { public GameObject Root; public ACCConfig Config; public List<OutfitData> Outfits; public Animator Animator; public AnimatorController Controller; }
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
    [Serializable] private class Check { public string path, property; public float expected, tolerance; }
  }
}
