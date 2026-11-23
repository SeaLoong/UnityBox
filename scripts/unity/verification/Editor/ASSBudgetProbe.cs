// Temporary verification probe for ASS defense budget saturation (light/particle/mesh).
// Runs via -executeMethod ASSBudgetProbe.Run in an isolated SDK fixture. Not shipped in the VPM package.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using VRC.SDK3.Avatars.Components;

public static class ASSBudgetProbe
{
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("BudgetProbeAvatar");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRCAvatarDescriptor>();

            var users = new GameObject("UserParticles");
            users.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateNormals();
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("UserBillboard" + i);
                go.transform.SetParent(users.transform, false);
                var m = go.AddComponent<ParticleSystem>().main; m.maxParticles = 1234;
            }
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("UserMesh" + i);
                go.transform.SetParent(users.transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                var m = ps.main; m.maxParticles = 500;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh;
            }
            var lightGo = new GameObject("UserLight");
            lightGo.transform.SetParent(users.transform, false);
            lightGo.AddComponent<Light>();
            Debug.Log("BUDGET existing particles=7170 meshTris=1000 lights=1");

            var config = new ASSConfigData
            {
                disableDefense = false,
                disableOverlay = true,
                disableWarningSound = true,
                disableObfuscation = true,
                gesturePassword = new System.Collections.Generic.List<int> { 1, 2 },
            };
            bool ok = Processor.ProcessAvatar(root, false, config);
            Debug.Log("BUDGET processed=" + ok);

            var statsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "AvatarPerformanceStats");
            var perfType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "AvatarPerformance");
            if (statsType == null || perfType == null) { Debug.LogError("BUDGET SDK perf types missing"); EditorApplication.Exit(1); return; }
            var stats = Activator.CreateInstance(statsType, new object[] { false });
            var calc = perfType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "CalculatePerformanceStats");
            var args = new object[calc.GetParameters().Length];
            for (int i = 0; i < args.Length; i++)
            {
                var t = calc.GetParameters()[i].ParameterType;
                if (t == typeof(string)) args[i] = "BudgetProbe";
                else if (t == typeof(GameObject)) args[i] = root;
                else if (t == typeof(bool)) args[i] = false;
                else if (t.IsInstanceOfType(stats)) args[i] = stats;
                else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
            }
            calc.Invoke(null, args);
            foreach (var f in statsType.GetFields())
                if (f.Name == "lightCount" || f.Name == "particleTotalCount" || f.Name == "particleMaxMeshPolyCount")
                    Debug.Log("BUDGET SDKSTAT " + f.Name + "=" + f.GetValue(stats) + " intMax=" + int.MaxValue);
            Debug.Log("BUDGET done");
        }
        catch (Exception e) { Debug.LogError("BUDGET failed: " + e); }
        EditorApplication.Exit(0);
    }
}
