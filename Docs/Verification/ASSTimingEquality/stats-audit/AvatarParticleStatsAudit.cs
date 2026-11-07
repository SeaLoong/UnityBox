using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;
using Object = UnityEngine.Object;

/// <summary>Small, non-simulating objects for comparing actual SDK statistic categories.</summary>
public static class AvatarParticleStatsAudit
{
    private const string Output = "/workspace/unitybox-setup/unity/ass-equal-stats-audit/result.json";

    [Serializable]
    private sealed class Observation
    {
        public string scenario;
        public string root;
        public long sdk_particle_system_count;
        public long sdk_total_combined_max_particle_count;
        public int components_including_inactive;
        public int components_active_in_hierarchy;
        public long declared_capacity_including_inactive;
        public long live_particles_including_inactive;
    }

    [Serializable]
    private sealed class ParticleFields
    {
        public string name;
        public int max_particles;
        public bool active_self;
        public bool emission_enabled;
        public bool play_on_awake;
        public int live_particles;
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string unity_version;
        public string sdk_stats_assembly;
        public string api = "new AvatarPerformanceStats(false); AvatarPerformance.CalculatePerformanceStats(root.name, root, stats, false)";
        public string platform = "Desktop (isMobilePlatform=false)";
        public string initialization = "AvatarPerformanceStats.Initialize(), matching SDK EnvConfig.LoadEditorResources (EnvConfig.cs:1267)";
        public string setup = "Two ordinary stopped systems, capacities 7 and 11; emission and playOnAwake disabled. Clone adds one stopped system of capacity 3. No simulation, Defense, shaders, animation or build callbacks.";
        public List<Observation> observations = new List<Observation>();
        public List<ParticleFields> source_before = new List<ParticleFields>();
        public List<ParticleFields> source_after = new List<ParticleFields>();
        public bool source_fields_unchanged;
        public bool source_statistics_unchanged_after_clone;
        public string failure;
    }

    public static void Execute()
    {
        var report = new Report
        {
            unity_version = Application.unityVersion,
            sdk_stats_assembly = typeof(AvatarPerformanceStats).Assembly.FullName,
        };
        GameObject source = null;
        GameObject clone = null;
        try
        {
            // SDK editor setup normally initializes its resource-backed rating tables here.
            AvatarPerformanceStats.Initialize();
            source = new GameObject("Ordinary source avatar");
            source.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var first = AddStoppedSystem(source, "First ordinary system", 7);
            var second = AddStoppedSystem(source, "Second ordinary system", 11);
            report.source_before = CaptureFields(source);
            var allActive = Measure("all-active", source);
            report.observations.Add(allActive);

            second.gameObject.SetActive(false);
            report.observations.Add(Measure("one-child-inactive", source));
            second.gameObject.SetActive(true);

            clone = Object.Instantiate(source);
            clone.name = "Isolated ordinary clone";
            AddStoppedSystem(clone, "Additional ordinary system", 3);
            var sourceOnly = Measure("source-root-only-after-clone", source);
            report.observations.Add(sourceOnly);
            report.observations.Add(Measure("isolated-clone-with-additional-system", clone));

            report.source_after = CaptureFields(source);
            Require(report.source_before.Count == 2 && report.source_after.Count == 2,
                "The source must retain its original two components.");
            for (int i = 0; i < report.source_before.Count; i++)
                Require(JsonUtility.ToJson(report.source_before[i]) == JsonUtility.ToJson(report.source_after[i]),
                    "Source particle fields changed: " + report.source_before[i].name);
            Require(first.main.maxParticles == 7 && second.main.maxParticles == 11,
                "Source capacities must remain 7 and 11.");
            report.source_fields_unchanged = true;
            Require(allActive.sdk_particle_system_count == sourceOnly.sdk_particle_system_count
                && allActive.sdk_total_combined_max_particle_count == sourceOnly.sdk_total_combined_max_particle_count,
                "An independent clone must not change the source root's SDK particle statistics.");
            report.source_statistics_unchanged_after_clone = true;
            foreach (var observation in report.observations)
            {
                Require(observation.sdk_particle_system_count >= 0 && observation.sdk_total_combined_max_particle_count >= 0,
                    "SDK particle counters must be nonnegative: " + observation.scenario);
                Require(observation.live_particles_including_inactive == 0,
                    "The audit must keep every system stopped with zero live particles.");
                Require(observation.components_active_in_hierarchy <= observation.components_including_inactive,
                    "Active components cannot exceed all components.");
            }
            Require(report.observations[3].components_including_inactive == 3,
                "Only the clone should contain the additional small component.");
            report.passed = true;
        }
        catch (Exception error)
        {
            report.failure = error.ToString();
            throw;
        }
        finally
        {
            if (clone != null) Object.DestroyImmediate(clone);
            if (source != null) Object.DestroyImmediate(source);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, JsonUtility.ToJson(report, true) + "\n");
        }
    }

    private static ParticleSystem AddStoppedSystem(GameObject root, string name, int capacity)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        var particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.maxParticles = capacity;
        var emission = particles.emission;
        emission.enabled = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static Observation Measure(string scenario, GameObject root)
    {
        var stats = new AvatarPerformanceStats(false);
        AvatarPerformance.CalculatePerformanceStats(root.name, root, stats, false);
        var observation = new Observation
        {
            scenario = scenario,
            root = root.name,
            sdk_particle_system_count = Convert.ToInt64(stats.particleSystemCount),
            sdk_total_combined_max_particle_count = Convert.ToInt64(stats.particleTotalCount),
        };
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            observation.components_including_inactive++;
            if (particles.gameObject.activeInHierarchy) observation.components_active_in_hierarchy++;
            observation.declared_capacity_including_inactive += particles.main.maxParticles;
            observation.live_particles_including_inactive += particles.particleCount;
        }
        return observation;
    }

    private static List<ParticleFields> CaptureFields(GameObject source)
    {
        var fields = new List<ParticleFields>();
        foreach (var particles in source.GetComponentsInChildren<ParticleSystem>(true))
            fields.Add(new ParticleFields
            {
                name = particles.name,
                max_particles = particles.main.maxParticles,
                active_self = particles.gameObject.activeSelf,
                emission_enabled = particles.emission.enabled,
                play_on_awake = particles.main.playOnAwake,
                live_particles = particles.particleCount,
            });
        return fields;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
