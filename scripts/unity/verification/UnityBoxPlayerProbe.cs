using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class UnityBoxPlayerProbe : MonoBehaviour
{
    [Serializable] public sealed class Input { public string animator_path, name, type; public float value; }
    [Serializable] public sealed class Check { public string path, property, parameter; public int index; public float expected, tolerance = 0.001f; }
    [Serializable] public sealed class Step { public int frames = 1; public float delta_time = 1f / 60f; public Input[] inputs = Array.Empty<Input>(); public Check[] checks = Array.Empty<Check>(); }
    [Serializable] public sealed class Scenario { public string name; public string[] animator_paths; public Step[] steps; }
    [Serializable] public sealed class Result { public string unity_version; public bool passed; public int scenarios, frames, checks; public string[] providers; public List<string> failures = new List<string>(); public List<Measurement> measurements = new List<Measurement>(); public long mono_heap_bytes; }
    [Serializable] public sealed class Measurement { public string plugin = "StandalonePlayer", scenario, correctness; public int size, samples = 11, animator_layers, clip_count; public double median_ms, p95_ms, min_ms, max_ms; public long allocated_bytes_per_op = -1; public long controller_memory_bytes; }

    [SerializeField] private List<string> scenariosJson = new List<string>();
    public string[] providers = Array.Empty<string>();
    public string[] configurationTypes = Array.Empty<string>();
    private Result activeResult;

    public int ScenarioCount => scenariosJson.Count;

    public void AddScenarioJson(string json)
    {
        var scenario = JsonUtility.FromJson<Scenario>(json);
        var checks = 0;
        foreach (var step in scenario.steps) checks += step.checks.Length;
        if (string.IsNullOrEmpty(scenario.name) || scenario.animator_paths.Length == 0 || checks == 0)
            throw new InvalidOperationException("Player scenario needs a name, Animator and output checks");
        scenariosJson.Add(json);
    }

    private Transform Find(string path) => string.IsNullOrEmpty(path) ? transform : transform.Find(path);

    private void Awake()
    {
        Application.logMessageReceived += HandleError;
        // The probe supplies every evaluation tick; Unity's automatic animation update
        // must not consume Entry or advance a clip between the specified inputs.
        foreach (var animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
    }

    private IEnumerator Start()
    {
        var result = new Result { unity_version = Application.unityVersion, providers = providers };
        activeResult = result;
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && Array.IndexOf(configurationTypes, component.GetType().FullName) >= 0)
                result.failures.Add("Configuration component survived player build: " + component.GetType().FullName);
        foreach (var json in scenariosJson)
        {
            var scenario = JsonUtility.FromJson<Scenario>(json);
            var animators = new List<Animator>();
            foreach (var path in scenario.animator_paths)
            {
                var target = Find(path);
                var animator = target == null ? null : target.GetComponent<Animator>();
                if (animator == null) throw new InvalidOperationException("Missing Animator: " + path);
                animator.Rebind();
                animator.enabled = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animators.Add(animator);
            }
            foreach (var step in scenario.steps)
            {
                foreach (var input in step.inputs)
                {
                    var animator = Find(input.animator_path).GetComponent<Animator>();
                    switch (input.type)
                    {
                        case "bool": animator.SetBool(input.name, input.value != 0); break;
                        case "int": animator.SetInteger(input.name, (int)input.value); break;
                        case "float": animator.SetFloat(input.name, input.value); break;
                        case "trigger": animator.SetTrigger(input.name); break;
                        default: throw new InvalidOperationException("Input type: " + input.type);
                    }
                }
                for (var frame = 0; frame < step.frames; frame++)
                {
                    foreach (var animator in animators) animator.Update(step.delta_time);
                    result.frames++;
                    yield return null;
                }
                foreach (var check in step.checks)
                {
                    result.checks++;
                    var actual = Read(check);
                    if (float.IsNaN(actual) || Mathf.Abs(actual - check.expected) > check.tolerance)
                        result.failures.Add(scenario.name + ": " + check.path + "/" + check.property +
                            " expected=" + check.expected + " actual=" + actual);
                }
            }
            result.scenarios++;
            foreach (var animator in animators)
                result.measurements.Add(Measure(scenario.name + "/" + animator.name, new List<Animator> { animator }));
        }
        result.mono_heap_bytes = GC.GetTotalMemory(true);
        result.passed = result.failures.Count == 0 && result.scenarios > 0 && result.checks > 0;
        Save(result);
        Debug.Log("UNITYBOX_PLAYER_RESULT " + JsonUtility.ToJson(result));
        Application.Quit(result.passed ? 0 : 1);
    }

    private void HandleError(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
        var result = activeResult ?? new Result { unity_version = Application.unityVersion, providers = providers };
        result.passed = false;
        result.failures.Add(message + "\n" + stack);
        Save(result);
        Application.Quit(1);
    }

    private static void Save(Result result)
    {
        var arguments = Environment.GetCommandLineArgs();
        File.WriteAllText(arguments[Array.IndexOf(arguments, "-unityboxReport") + 1], JsonUtility.ToJson(result, true));
    }

    private float Read(Check check)
    {
        var target = Find(check.path);
        switch (check.property)
        {
            case "localPosition.x": return target.localPosition.x;
            case "localPosition.y": return target.localPosition.y;
            case "localPosition.z": return target.localPosition.z;
            case "localScale.x": return target.localScale.x;
            case "localScale.y": return target.localScale.y;
            case "localScale.z": return target.localScale.z;
            case "blendshape": return target.GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(check.index);
            case "active": return target.gameObject.activeSelf ? 1 : 0;
            case "active_in_hierarchy": return target.gameObject.activeInHierarchy ? 1 : 0;
            case "parameter.float": return target.GetComponent<Animator>().GetFloat(check.parameter);
            case "parameter.int": return target.GetComponent<Animator>().GetInteger(check.parameter);
            case "parameter.bool": return target.GetComponent<Animator>().GetBool(check.parameter) ? 1 : 0;
            default: throw new InvalidOperationException("Check property: " + check.property);
        }
    }

    private static Measurement Measure(string name, List<Animator> animators)
    {
        const int iterations = 200;
        var samples = new double[11];
        for (var i = 0; i < 50; i++) foreach (var animator in animators) animator.Update(1f / 60f);
        var timer = new Stopwatch();
        for (var sample = 0; sample < samples.Length; sample++)
        {
            timer.Restart();
            for (var i = 0; i < iterations; i++) foreach (var animator in animators) animator.Update(1f / 60f);
            timer.Stop();
            samples[sample] = timer.Elapsed.TotalMilliseconds / iterations;
        }
        Array.Sort(samples);
        long memory = 0;
        foreach (var animator in animators) memory += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(animator.runtimeAnimatorController);
        return new Measurement { scenario = name, size = animators.Count, animator_layers = animators[0].layerCount,
            clip_count = animators[0].runtimeAnimatorController.animationClips.Length, median_ms = samples[5], p95_ms = samples[10],
            min_ms = samples[0], max_ms = samples[10], controller_memory_bytes = memory > 0 ? memory : -1,
            correctness = "Scenario output checks passed before timing; milliseconds per Animator batch update" };
    }
}
