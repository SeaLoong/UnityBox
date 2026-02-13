using UnityEngine;
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
        private readonly AvatarSecuritySystemComponent config;
        private GameObject uiGameObject;

        /// <summary>
        /// UI Shader 名称（全屏覆盖渲染）
        /// </summary>
        private const string UI_SHADER_NAME = "UnityBox/AvatarSecuritySystem/UI";
        private const string LOGO_RESOURCE_NAME = "Avatar Security System";

        public Feedback(GameObject avatarGameObject, AvatarSecuritySystemComponent config)
        {
            this.avatarGameObject = avatarGameObject;
            this.config = config;
        }


        public void Generate()
        {
            // 创建 UI 根对象
            CreateUIGameObject();

            // 创建 UI Mesh（使用自定义 Shader 全屏渲染遮挡背景 + 进度条）
            CreateUIMesh();

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
        /// 进度条通过动画驱动材质属性 _Progress（1→0）
        /// </summary>
        private GameObject CreateUIMesh()
        {
            var meshObj = new GameObject("Overlay");
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
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            // 使用全屏覆盖 Shader
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            var shader = Shader.Find(UI_SHADER_NAME) ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader);
            material.SetColor("_BackgroundColor", Color.white);
            material.SetColor("_BarColor", Color.red);
            material.SetFloat("_Progress", 1f);  // 初始满进度

            // 加载并设置 Logo 纹理
            var logoTex = Resources.Load<Texture2D>(LOGO_RESOURCE_NAME);
            if (logoTex != null)
                material.SetTexture("_LogoTex", logoTex);

            meshRenderer.sharedMaterial = material;

            Debug.Log("[ASS] UI Mesh overlay created (background + progress bar)");
            return meshObj;
        }


        private const float AUDIO_SOURCE_VOLUME = 0.5f;
        private const int AUDIO_SOURCE_PRIORITY = 0;

        private GameObject CreateAudioObject(string objectName)
        {
            var obj = new GameObject(objectName);
            obj.transform.SetParent(avatarGameObject.transform, false);
            obj.transform.localPosition = Vector3.zero;
            var audioSource = obj.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = AUDIO_SOURCE_VOLUME;
            audioSource.priority = AUDIO_SOURCE_PRIORITY;
            Debug.Log($"[ASS] AudioSource created: {objectName}");
            return obj;
        }

    }
}
