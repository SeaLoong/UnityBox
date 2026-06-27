using UnityEngine;
using UnityEditor;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class Feedback
    {
        private readonly GameObject avatarGameObject;
        private readonly ASSComponent config;
        private GameObject overlayRootObject;
        private const string OVERLAY_SHADER_NAME = "UnityBox/ASS_Overlay";
        private const string LOGO_RESOURCE_NAME = "Avatar Security System";
        public Feedback(GameObject avatarGameObject, ASSComponent config)
        {
            this.avatarGameObject = avatarGameObject;
            this.config = config;
        }
        public void Generate()
        {
            if (config.disableOverlay)
            {
                RemoveExistingOverlayObject();
            }
            else
            {
                CreateOverlayRoot();
                RemoveExistingOverlayMeshes();
                CreateOverlayMesh();
            }
            CreateAudioObject(Constants.GO_AUDIO_SUCCESS);
            if (!config.disableWarningSound)
            {
                CreateAudioObject(Constants.GO_AUDIO_WARNING);
            }
            else
            {
                RemoveExistingWarningAudio();
            }
        }
        private GameObject CreateOverlayRoot()
        {
            Transform existing = avatarGameObject.transform.Find(Constants.GO_OVERLAY);
            if (existing != null)
            {
                int removedDuplicates = 0;
                for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
                {
                    var child = avatarGameObject.transform.GetChild(i);
                    if (child == existing || child.name != Constants.GO_OVERLAY) continue;
                    Object.DestroyImmediate(child.gameObject);
                    removedDuplicates++;
                }
                existing.localPosition = Vector3.zero;
                existing.localRotation = Quaternion.identity;
                existing.localScale = Vector3.one;
                existing.gameObject.SetActive(false);
                if (removedDuplicates > 0)
                {
                    Debug.Log($"[ASS] Removed {removedDuplicates} duplicate overlay root object(s)");
                }
                Debug.Log("[ASS] Using existing overlay object");
                this.overlayRootObject = existing.gameObject;
                return existing.gameObject;
            }
            var overlayObj = new GameObject(Constants.GO_OVERLAY);
            overlayObj.SetActive(false);
            overlayObj.transform.SetParent(avatarGameObject.transform, false);
            Debug.Log("[ASS] Overlay root created (fullscreen Shader overlay)");
            this.overlayRootObject = overlayObj;
            return overlayObj;
        }
        private GameObject CreateOverlayMesh()
        {
            var meshObj = new GameObject(Constants.GO_OVERLAY_MESH);
            meshObj.transform.SetParent(overlayRootObject.transform, false);
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.transform.localRotation = Quaternion.identity;
            meshObj.transform.localScale = Vector3.one;
            var mesh = new Mesh { name = "ASS_Overlay_Quad" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            var shader = Obfuscator.GetObfuscatedShader(OVERLAY_SHADER_NAME)
                ?? Shader.Find(OVERLAY_SHADER_NAME)
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader);
            material.SetColor("_A7F3", Color.white);
            material.SetColor("_B2E1", Color.red);
            material.SetFloat("_C9D4", 1f);  // 初始满进度
            var logoTex = Resources.Load<Texture2D>(LOGO_RESOURCE_NAME);
            if (logoTex != null)
            {
                EnsureTextureOptimized(logoTex);
                material.SetTexture("_G4D9", logoTex);
            }
            meshRenderer.sharedMaterial = material;
            material.enableInstancing = true;
            material.renderQueue = 5000;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.sortingOrder = 32767;  // 最高排序优先级
            Debug.Log("[ASS] Fullscreen overlay mesh created (mask + progress bar)");
            return meshObj;
        }
        private void RemoveExistingOverlayObject()
        {
            int removedCount = 0;
            for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
            {
                var child = avatarGameObject.transform.GetChild(i);
                if (child.name != Constants.GO_OVERLAY) continue;
                Object.DestroyImmediate(child.gameObject);
                removedCount++;
            }
            if (removedCount > 0)
            {
                Debug.Log($"[ASS] disableOverlay enabled, removed {removedCount} existing overlay object(s)");
            }
        }
        private void RemoveExistingOverlayMeshes()
        {
            if (overlayRootObject == null) return;
            int removedCount = 0;
            for (int i = overlayRootObject.transform.childCount - 1; i >= 0; i--)
            {
                var child = overlayRootObject.transform.GetChild(i);
                if (child.name != Constants.GO_OVERLAY_MESH) continue;
                Object.DestroyImmediate(child.gameObject);
                removedCount++;
            }
            if (removedCount > 0)
            {
                Debug.Log($"[ASS] Removed {removedCount} existing overlay mesh object(s)");
            }
        }
        private void RemoveExistingWarningAudio()
        {
            int removedCount = 0;
            for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
            {
                var child = avatarGameObject.transform.GetChild(i);
                if (child.name != Constants.GO_AUDIO_WARNING) continue;
                Object.DestroyImmediate(child.gameObject);
                removedCount++;
            }
            if (removedCount > 0)
            {
                Debug.Log($"[ASS] disableWarningSound enabled, removed {removedCount} existing warning audio object(s)");
            }
        }
        private const float AUDIO_SOURCE_VOLUME = 0.5f;
        private const int AUDIO_SOURCE_PRIORITY = 0;
        private GameObject CreateAudioObject(string objectName)
        {
            Transform existing = avatarGameObject.transform.Find(objectName);
            if (existing != null)
            {
                for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
                {
                    var child = avatarGameObject.transform.GetChild(i);
                    if (child == existing || child.name != objectName) continue;
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            var obj = existing != null ? existing.gameObject : new GameObject(objectName);
            if (existing == null)
                obj.transform.SetParent(avatarGameObject.transform, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            var audioSources = obj.GetComponents<AudioSource>();
            AudioSource audioSource;
            if (audioSources.Length == 0)
            {
                audioSource = obj.AddComponent<AudioSource>();
            }
            else
            {
                audioSource = audioSources[0];
                for (int i = audioSources.Length - 1; i >= 1; i--)
                {
                    Object.DestroyImmediate(audioSources[i]);
                }
            }
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = AUDIO_SOURCE_VOLUME;
            audioSource.priority = AUDIO_SOURCE_PRIORITY;
            Debug.Log($"[ASS] AudioSource {(existing == null ? "created" : "reused")}: {objectName}");
            return obj;
        }
        private static void EnsureTextureOptimized(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool changed = false;
            if (importer.maxTextureSize > 512) { importer.maxTextureSize = 512; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (!importer.crunchedCompression) { importer.crunchedCompression = true; changed = true; }
            if (importer.anisoLevel > 0) { importer.anisoLevel = 0; changed = true; }
            if (changed) importer.SaveAndReimport();
        }
    }
}
