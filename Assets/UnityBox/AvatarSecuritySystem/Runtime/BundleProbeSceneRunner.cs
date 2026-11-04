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
        [SerializeField] public bool expectModifiedBundle = true;

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

            var instance = Instantiate(prefab);
            var probe = instance.GetComponent<AnimationPathBindingProbe>();
            if (probe == null)
            {
                Destroy(instance);
                Debug.LogError("[ASS Probe] Loaded Prefab has no path-binding probe component.");
                return;
            }
            // Start runs after Instantiate returns, so set the expectation before
            // the probe begins rather than inferring success from observed failure.
            probe.mode = expectModifiedBundle
                ? AnimationPathBindingProbe.ProbeMode.ModifiedBundle
                : AnimationPathBindingProbe.ProbeMode.OriginalBundle;
            Debug.Log($"[ASS Probe] Loaded bundle '{bundleFileName}' and instantiated '{prefabAssetName}'; mode={probe.mode}.");
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
