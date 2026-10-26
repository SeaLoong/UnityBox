using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Builds a tiny AssetBundle, replaces a path string in the built bytes,
    /// then loads the modified bundle without reimporting it into the project.
    /// This simulates the relevant part of a Hotswap operation.
    /// </summary>
    public static class AssetBundlePathBindingProbeBuilder
    {
        private const string Folder = "Assets/UnityBox/AvatarSecuritySystem/Tests";
        private const string BundleFolder = Folder + "/Bundles";
        private const string PrefabPath = Folder + "/BundlePathProbe.prefab";
        private const string ClipPath = Folder + "/BundlePathProbe.anim";
        private const string ControllerPath = Folder + "/BundlePathProbe.controller";
        private const string ScenePath = Folder + "/BundlePathProbeRunner.unity";
        private const string BundleName = "ass_path_probe";
        private const string OldMarkerName = "ASS_Marker_OLD";
        private const string NewMarkerName = "ASS_Marker_NEW";
        private const string PlainMarkerName = "PlainMarker";
        private const string StateName = "PathProbe";

        [MenuItem("Tools/UnityBox/ASS/Animation Path Binding Probe/Build & Run: Modified Bundle (Hotswap Sim)")]
        private static void BuildAndRunModified() => BuildAndRun(loadModified: true);

        [MenuItem("Tools/UnityBox/ASS/Animation Path Binding Probe/Build & Run: Original Bundle (Control)")]
        private static void BuildAndRunOriginal() => BuildAndRun(loadModified: false);

        private static void BuildAndRun(bool loadModified)
        {
            EnsureFolder();
            CleanupGeneratedAssets();

            var clip = CreateClip();
            var controller = CreateController(clip);
            var prefab = CreatePrefab(controller);
            SetBundleName(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string outputPath = Path.Combine(Application.dataPath, "UnityBox/AvatarSecuritySystem/Tests/Bundles");
            Directory.CreateDirectory(outputPath);
            string builtPath = Path.Combine(outputPath, BundleName);
            string modifiedPath = Path.Combine(outputPath, BundleName + "_modified");
            DeleteFileIfExists(builtPath);
            DeleteFileIfExists(modifiedPath);

            var manifest = BuildPipeline.BuildAssetBundles(
                outputPath,
                BuildAssetBundleOptions.UncompressedAssetBundle,
                EditorUserBuildSettings.activeBuildTarget);
            if (manifest == null || !File.Exists(builtPath))
                throw new InvalidOperationException("[ASS Probe] AssetBundle build failed.");

            int replacements = ReplaceEqualLengthBytes(builtPath, modifiedPath, OldMarkerName, NewMarkerName);
            Debug.Log($"[ASS Probe] Built bundle: {builtPath}");
            Debug.Log($"[ASS Probe] Modified bundle: {modifiedPath}; string replacements={replacements}");
            if (replacements == 0)
                throw new InvalidOperationException("[ASS Probe] The old marker string was not found in the built AssetBundle.");

            LogBundleDiagnostics(builtPath, "original");
            LogBundleDiagnostics(modifiedPath, "modified");

            // The bundle is loaded by a runtime component at play-mode start so Unity
            // never reimports or re-serializes the modified bytes.
            string bundleToLoad = loadModified ? BundleName + "_modified" : BundleName;
            CreateRunnerScene(bundleToLoad);
            Debug.Log($"[ASS Probe] Runner scene will load '{bundleToLoad}' at runtime. Entering Play Mode.");
            EditorApplication.isPlaying = true;
        }

        private static void CreateRunnerScene(string bundleFileName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var runnerObject = new GameObject("ASS_BundleProbeRunner");
            var runner = runnerObject.AddComponent<BundleProbeSceneRunner>();
            if (runner == null)
                throw new InvalidOperationException("[ASS Probe] Runner component could not be added.");
            runner.bundleFileName = bundleFileName;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip CreateClip()
        {
            var clip = new AnimationClip
            {
                name = "BundlePathBindingProbe"
            };
            clip.SetCurve(
                OldMarkerName,
                typeof(Transform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0f, 1f, 0.2f, 2f));
            // Internal control: its path contains no ID string, so a Hotswap-style
            // global replacement must never touch it. If this one stops animating,
            // the harness itself is broken rather than the protection working.
            clip.SetCurve(
                PlainMarkerName,
                typeof(Transform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0f, 1f, 0.2f, 2f));
            AssetDatabase.CreateAsset(clip, ClipPath);
            return clip;
        }

        private static RuntimeAnimatorController CreateController(AnimationClip clip)
        {
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                throw new InvalidOperationException("[ASS Probe] Failed to create AnimatorController.");

            var layer = controller.layers[0];
            var state = layer.stateMachine.AddState(StateName);
            state.motion = clip;
            layer.stateMachine.defaultState = state;
            controller.layers = new[] { layer };
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject CreatePrefab(RuntimeAnimatorController controller)
        {
            var root = new GameObject("ASS_BundlePathBindingProbe");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var marker = new GameObject(OldMarkerName);
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one;

            var plainMarker = new GameObject(PlainMarkerName);
            plainMarker.transform.SetParent(root.transform, false);
            plainMarker.transform.localScale = Vector3.one;

            var probe = root.AddComponent<UnityBox.AvatarSecuritySystem.AnimationPathBindingProbe>();
            if (probe == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("[ASS Probe] Runtime probe component could not be added.");
            }
            probe.marker = marker.transform;
            probe.plainMarker = plainMarker.transform;
            probe.animator = animator;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null)
                throw new InvalidOperationException("[ASS Probe] Failed to save probe Prefab.");
            return prefab;
        }

        private static void SetBundleName(GameObject prefab)
        {
            var importer = AssetImporter.GetAtPath(PrefabPath);
            if (importer == null)
                throw new InvalidOperationException("[ASS Probe] Prefab importer was not found.");
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
        }

        private static int ReplaceEqualLengthBytes(string sourcePath, string targetPath, string oldValue, string newValue)
        {
            byte[] oldBytes = System.Text.Encoding.ASCII.GetBytes(oldValue);
            byte[] newBytes = System.Text.Encoding.ASCII.GetBytes(newValue);
            if (oldBytes.Length != newBytes.Length)
                throw new InvalidOperationException("[ASS Probe] Replacement strings must have equal byte length.");

            byte[] bytes = File.ReadAllBytes(sourcePath);
            int count = 0;
            for (int i = 0; i <= bytes.Length - oldBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < oldBytes.Length; j++)
                {
                    if (bytes[i + j] != oldBytes[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                if (count < 12)
                {
                    int contextStart = Math.Max(0, i - 48);
                    int contextEnd = Math.Min(bytes.Length, i + oldBytes.Length + 48);
                    Debug.Log($"[ASS Probe] Occurrence #{count} at 0x{i:X}: \"{ToPrintableAscii(bytes, contextStart, contextEnd)}\"");
                }

                Buffer.BlockCopy(newBytes, 0, bytes, i, newBytes.Length);
                count++;
                i += oldBytes.Length - 1;
            }

            int newOccurrences = CountOccurrences(bytes, newBytes);
            Debug.Log($"[ASS Probe] Replacement summary: replaced={count}, '{newValue}' occurrences after replace={newOccurrences}");
            File.WriteAllBytes(targetPath, bytes);
            return count;
        }

        private static int CountOccurrences(byte[] bytes, byte[] pattern)
        {
            int count = 0;
            for (int i = 0; i <= bytes.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (bytes[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;
                count++;
                i += pattern.Length - 1;
            }
            return count;
        }

        private static string ToPrintableAscii(byte[] bytes, int start, int end)
        {
            var builder = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char character = (char)bytes[i];
                builder.Append(character >= 32 && character < 127 ? character : '.');
            }
            return builder.ToString();
        }

        private static void LogBundleDiagnostics(string bundlePath, string label)
        {
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Debug.LogWarning($"[ASS Probe] Could not open {label} bundle for inspection: {bundlePath}");
                return;
            }

            try
            {
                var clips = bundle.LoadAllAssets<AnimationClip>();
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        Debug.Log($"[ASS Probe] {label} clip '{clip.name}' binding: path='{binding.path}', property='{binding.propertyName}'");
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        Debug.Log($"[ASS Probe] {label} clip '{clip.name}' pptr binding: path='{binding.path}', property='{binding.propertyName}'");
                    }
                }

                var prefabs = bundle.LoadAllAssets<GameObject>();
                for (int i = 0; i < prefabs.Length; i++)
                {
                    DumpHierarchy(prefabs[i].transform, $"[ASS Probe] {label} hierarchy: ", 0);
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void DumpHierarchy(Transform transform, string prefix, int depth)
        {
            if (transform == null || depth > 6) return;
            Debug.Log($"{prefix}'{transform.name}'");
            for (int i = 0; i < transform.childCount; i++)
            {
                DumpHierarchy(transform.GetChild(i), prefix, depth + 1);
            }
        }

        private static void EnsureFolder()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "UnityBox/AvatarSecuritySystem/Tests/Bundles"));
            AssetDatabase.Refresh();
        }

        private static void CleanupGeneratedAssets()
        {
            foreach (string path in new[] { PrefabPath, ClipPath, ControllerPath, ScenePath })
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
