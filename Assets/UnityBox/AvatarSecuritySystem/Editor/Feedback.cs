using UnityEngine;
using UnityEditor;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 视觉和音频反馈系统生成器
    /// 功能：全屏遮挡 + 倒计时进度条（使用自定义 Shader 直接渲染在摄像机上）
    /// </summary>
    public class Feedback
    {
        private readonly GameObject avatarGameObject;
        private readonly ASSComponent config;
        private GameObject uiGameObject;

        /// <summary>
        /// UI Shader 名称（全屏覆盖渲染）
        /// </summary>
        private const string UI_SHADER_NAME = "UnityBox/ASS_UI";
        private const string LOGO_RESOURCE_NAME = "Avatar Security System";
        private const string UI_OVERLAY_NAME = "Overlay";

        public Feedback(GameObject avatarGameObject, ASSComponent config)
        {
            this.avatarGameObject = avatarGameObject;
            this.config = config;
        }


        public void Generate()
        {
            if (config.hideUI)
            {
                RemoveExistingUIObject();
            }
            else
            {
                // 创建 UI 根对象
                CreateUIGameObject();

                // 清理旧 Overlay，避免重复生成导致多个同名子对象残留
                RemoveExistingUIOverlays();

                // 创建 UI Mesh（使用自定义 Shader 全屏渲染遮挡背景 + 进度条）
                CreateUIMesh();
            }

            // 创建音频对象
            CreateAudioObject(Constants.GO_AUDIO_SUCCESS);
            CreateAudioObject(Constants.GO_AUDIO_WARNING);
        }


        /// <summary>
        /// 创建 UI 根对象
        /// Shader 会直接渲染到摄像机全屏，不需要世界空间定位或头部绑定
        /// </summary>
        private GameObject CreateUIGameObject()
        {
            // 查找已有对象
            Transform existing = avatarGameObject.transform.Find(Constants.GO_UI);
            if (existing != null)
            {
                int removedDuplicates = 0;
                for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
                {
                    var child = avatarGameObject.transform.GetChild(i);
                    if (child == existing || child.name != Constants.GO_UI) continue;

                    Object.DestroyImmediate(child.gameObject);
                    removedDuplicates++;
                }

                existing.localPosition = Vector3.zero;
                existing.localRotation = Quaternion.identity;
                existing.localScale = Vector3.one;
                existing.gameObject.SetActive(false);
                if (removedDuplicates > 0)
                {
                    Debug.Log($"[ASS] Removed {removedDuplicates} duplicate UI root object(s)");
                }
                Debug.Log("[ASS] Using existing UI object");
                this.uiGameObject = existing.gameObject;
                return existing.gameObject;
            }

            // 创建根对象
            var uiGameObject = new GameObject(Constants.GO_UI);
            uiGameObject.SetActive(false);  // 默认禁用，只在 Locked 状态时由动画启用
            uiGameObject.transform.SetParent(avatarGameObject.transform, false);

            // 全屏渲染模式：不需要 VRCParentConstraint 绑定到头部
            // Shader 的顶点着色器会直接将顶点映射到裁剪空间全屏位置

            Debug.Log("[ASS] UI object created (fullscreen Shader overlay)");
            this.uiGameObject = uiGameObject;
            return uiGameObject;
        }

        /// <summary>
        /// 创建 UI Mesh：使用自定义 Shader 全屏渲染遮挡背景和进度条
        /// Shader 在顶点着色器中将 Quad 直接映射到裁剪空间覆盖整个屏幕
        /// 进度条通过动画驱动材质属性 _C9D4（1→0）
        /// </summary>
        private GameObject CreateUIMesh()
        {
            var meshObj = new GameObject(UI_OVERLAY_NAME);
            meshObj.transform.SetParent(uiGameObject.transform, false);
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.transform.localRotation = Quaternion.identity;
            meshObj.transform.localScale = Vector3.one;

            // 创建 Quad Mesh（顶点位置无所谓，Shader 会重新映射到全屏）
            var mesh = new Mesh { name = "ASS_UI_Quad" };
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
            // 设置较大 Bounds 防止 Unity 视锥体剔除（Shader 负责全屏映射）
            // 注意：不能太大否则会触发 VRChat 性能评估 Bounds VeryPoor
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            // 使用全屏覆盖 Shader
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            var shader = Shader.Find(UI_SHADER_NAME) ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader);
            material.SetColor("_A7F3", Color.white);
            material.SetColor("_B2E1", Color.red);
            material.SetFloat("_C9D4", 1f);  // 初始满进度

            // 加载并设置 Logo 纹理
            var logoTex = Resources.Load<Texture2D>(LOGO_RESOURCE_NAME);
            if (logoTex != null)
            {
                EnsureTextureOptimized(logoTex);
                material.SetTexture("_G4D9", logoTex);
            }

            meshRenderer.sharedMaterial = material;
            material.enableInstancing = true;

            material.renderQueue = 5000;

            // 禁用所有可能导致被剔除的功能
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.sortingOrder = 32767;  // 最高排序优先级

            Debug.Log("[ASS] UI Mesh overlay created (background + progress bar)");
            return meshObj;
        }

        private void RemoveExistingUIObject()
        {
            int removedCount = 0;
            for (int i = avatarGameObject.transform.childCount - 1; i >= 0; i--)
            {
                var child = avatarGameObject.transform.GetChild(i);
                if (child.name != Constants.GO_UI) continue;

                Object.DestroyImmediate(child.gameObject);
                removedCount++;
            }

            if (removedCount > 0)
            {
                Debug.Log($"[ASS] hideUI enabled, removed {removedCount} existing UI object(s)");
            }
        }

        private void RemoveExistingUIOverlays()
        {
            if (uiGameObject == null) return;

            int removedCount = 0;
            for (int i = uiGameObject.transform.childCount - 1; i >= 0; i--)
            {
                var child = uiGameObject.transform.GetChild(i);
                if (child.name != UI_OVERLAY_NAME) continue;

                Object.DestroyImmediate(child.gameObject);
                removedCount++;
            }

            if (removedCount > 0)
            {
                Debug.Log($"[ASS] Removed {removedCount} existing UI overlay object(s)");
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
