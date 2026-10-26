using System;
using System.IO;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem
{
    /// <summary>
    /// Runtime loader for the generated AssetBundle path-binding probe scene.
    /// Loads a bundle from Tests/Bundles at play-mode start and instantiates its Prefab,
    /// so the byte-modified bundle is never reimported or re-serialized by the editor.
    /// </summary>
    public sealed class BundleProbeSceneRunner : MonoBehaviour
    {
        [SerializeField] public string bundleFileName = "ass_path_probe_modified";

        private AssetBundle bundle;

        private void Start()
        {
            string folder = Path.Combine(Application.dataPath, "UnityBox/AvatarSecuritySystem/Tests/Bundles");
            string path = Path.Combine(folder, bundleFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[ASS Probe] Bundle file not found: {path}. Run a build menu item first.");
                return;
            }

            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogError($"[ASS Probe] Failed to load bundle: {path}");
                return;
            }

            string prefabAssetName = null;
            foreach (string name in bundle.GetAllAssetNames())
            {
                if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    prefabAssetName = name;
                    break;
                }
            }
            if (prefabAssetName == null)
            {
                Debug.LogError("[ASS Probe] Bundle contains no Prefab asset.");
                return;
            }

            var prefab = bundle.LoadAsset<GameObject>(prefabAssetName);
            if (prefab == null)
            {
                Debug.LogError($"[ASS Probe] Failed to load Prefab '{prefabAssetName}'.");
                return;
            }

            Instantiate(prefab);
            Debug.Log($"[ASS Probe] Loaded bundle '{bundleFileName}' and instantiated '{prefabAssetName}'.");
        }

        private void OnDestroy()
        {
            if (bundle != null)
            {
                bundle.Unload(false);
                bundle = null;
            }
        }
    }
}
