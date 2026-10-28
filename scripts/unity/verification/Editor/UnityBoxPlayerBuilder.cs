using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnityBoxPlayerBuilder
{
    [Serializable] private sealed class Summary
    {
        public string result, output;
        public int total_errors, total_warnings;
        public long total_size_bytes;
        public double duration_seconds;
        public string[] hook_providers;
        public int source_configuration_components;
    }

    public static void Build()
    {
        var output = Environment.GetEnvironmentVariable("UNITYBOX_VERIFICATION_OUTPUT");
        Directory.CreateDirectory(output);
        const string generatedRoot = "Assets/UnityBoxVerificationGenerated";
        if (!AssetDatabase.IsValidFolder(generatedRoot)) AssetDatabase.CreateFolder("Assets", "UnityBoxVerificationGenerated");
        var runName = "Run_" + Path.GetFileName(output);
        AssetDatabase.CreateFolder(generatedRoot, runName);
        var folder = generatedRoot + "/" + runName;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("UnityBoxVerification");
        var probe = root.AddComponent<UnityBoxPlayerProbe>();
        AddBasicScenario(root, folder);
        foreach (var path in Directory.GetFiles("Library/ScriptAssemblies", "UnityBox.*Tests.dll"))
            Assembly.LoadFrom(Path.GetFullPath(path));
        var providers = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("UnityBox.")))
        foreach (var type in assembly.GetTypes())
        {
            var method = type.GetMethod("ConfigurePlayerProbe", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(GameObject), typeof(string) }, null);
            if (method == null) continue;
            var name = type.FullName.Replace('.', '_').Replace('+', '_');
            var target = folder + "/" + name;
            if (!AssetDatabase.IsValidFolder(target)) AssetDatabase.CreateFolder(folder, name);
            var before = probe.ScenarioCount;
            method.Invoke(null, new object[] { root, target });
            if (probe.ScenarioCount == before) throw new InvalidOperationException("Player provider produced no scenarios: " + type.FullName);
            providers.Add(type.FullName);
        }
        foreach (var expected in (Environment.GetEnvironmentVariable("UNITYBOX_REQUIRED_PLAYER_PROVIDERS") ?? "").Split(';'))
            if (expected.Length > 0 && !providers.Any(value => value == expected || value.EndsWith("." + expected, StringComparison.Ordinal)))
                throw new InvalidOperationException("Missing Player provider: " + expected);
        probe.providers = providers.ToArray();
        probe.configurationTypes = new[] { "UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent",
            "UnityBox.AvatarSecuritySystem.ASSComponent", "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent" };
        var configurations = new List<Component>();
        foreach (var name in probe.configurationTypes)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            if (type != null) new GameObject("Configuration_" + type.Name).transform.SetParent(root.transform, false);
            if (type != null) configurations.Add(root.transform.Find("Configuration_" + type.Name).gameObject.AddComponent(type));
        }
        AssetDatabase.SaveAssets();
        var scene = folder + "/PlayerVerification.unity";
        EditorSceneManager.SaveScene(root.scene, scene);
        var sourceSceneBytes = File.ReadAllBytes(scene);
        PlayerSettings.companyName = "UnityBox";
        PlayerSettings.productName = "UnityBoxVerification";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
        var logging = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityBox.BuildPipelineLogger.BuildPipelineLoggerSettings"))
            .FirstOrDefault(t => t != null)?.GetProperty("BuildPipeline");
        var originalLogging = logging?.GetValue(null);
        logging?.SetValue(null, true);
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene },
                locationPathName = Path.Combine(output, "UnityBoxPlayer"), target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None });
        }
        finally { if (logging != null) logging.SetValue(null, originalLogging); }
        // BuildPlayer invalidates pre-build scene object references. Reopen the
        // persisted authoring scene and inspect the newly loaded objects.
        if (!sourceSceneBytes.SequenceEqual(File.ReadAllBytes(scene)))
            throw new InvalidOperationException("Player build changed the saved source scene");
        var restoredScene = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        var sourceConfigurationCount = restoredScene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<MonoBehaviour>(true))
            .Count(component => component != null && Array.IndexOf(new[] {
                "UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent",
                "UnityBox.AvatarSecuritySystem.ASSComponent",
                "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent"
            }, component.GetType().FullName) >= 0);
        if (sourceConfigurationCount != configurations.Count)
            throw new InvalidOperationException("Player build changed the source scene's configuration components");
        var summary = new Summary { result = report.summary.result.ToString(), total_errors = (int)report.summary.totalErrors,
            total_warnings = (int)report.summary.totalWarnings, total_size_bytes = (long)report.summary.totalSize,
            duration_seconds = report.summary.totalTime.TotalSeconds, hook_providers = providers.ToArray(),
            output = report.summary.outputPath, source_configuration_components = sourceConfigurationCount };
        File.WriteAllText(Path.Combine(output, "build.json"), JsonUtility.ToJson(summary, true));
        var trace = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityBox.BuildPipelineLogger.BuildPipelineLoggerTrace"))
            .FirstOrDefault(t => t != null);
        (trace?.GetMethod("SaveLastCompleted", BindingFlags.Public | BindingFlags.Static) ??
         trace?.GetMethod("Save", BindingFlags.Public | BindingFlags.Static))?.Invoke(null,
            new object[] { Path.Combine(output, "pipeline-trace.json") });
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static void AddBasicScenario(GameObject root, string folder)
    {
        var target = new GameObject("Basic"); target.transform.SetParent(root.transform, false);
        var output = new GameObject("Value"); output.transform.SetParent(target.transform, false);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Basic.controller");
        controller.AddParameter("Value", AnimatorControllerParameterType.Float);
        var clip = new AnimationClip();
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Value", typeof(Transform), "m_LocalPosition.x"),
            AnimationCurve.Constant(0, 1, 3));
        AssetDatabase.CreateAsset(clip, folder + "/Basic.anim");
        controller.layers[0].stateMachine.AddState("Output").motion = clip;
        target.AddComponent<Animator>().runtimeAnimatorController = controller;
        root.SendMessage("AddScenarioJson", "{\"name\":\"Unity.PlayerSmoke\",\"animator_paths\":[\"Basic\"],\"steps\":[{\"frames\":2,\"delta_time\":0.0166667,\"inputs\":[],\"checks\":[{\"path\":\"Basic/Value\",\"property\":\"localPosition.x\",\"expected\":3,\"tolerance\":0.001}]}]}");
    }
}
