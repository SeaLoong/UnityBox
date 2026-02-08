using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 视觉和音频反馈系统生成器
    /// 功能：倒计时警告、错误提示、成功反馈、HUD显示
    /// </summary>
    public class Feedback
    {
        private readonly GameObject avatarGameObject;
        private readonly AvatarSecuritySystemComponent config;
        private GameObject uiGameObject;

        public Feedback(GameObject avatarGameObject, AvatarSecuritySystemComponent config)
        {
            this.avatarGameObject = avatarGameObject;
            this.config = config;
        }


        public void Generate()
        {
            // 创建或获取UI根对象
            CreateUIGameObject();

            // 创建倒计时条
            CreateCountdownBar();

            // 创建音频对象
            CreateAudioObject(Constants.GO_AUDIO_SUCCESS);
            CreateAudioObject(Constants.GO_AUDIO_WARNING);
        }


        /// <summary>
        /// 创建3D HUD（替代Canvas），在视角前显示倒计时条
        /// VRCConstraint绑定到头部，保证UI始终在视野内
        /// </summary>
        private GameObject CreateUIGameObject()
        {
            // 查找或创建Canvas
            Transform existingCanvas = avatarGameObject.transform.Find(Constants.GO_UI);
            if (existingCanvas != null)
            {
                Debug.Log(I18n.T("log.visual_existing_canvas"));
                return existingCanvas.gameObject;
            }

            // 创建根对象（直接挂在avatarGameObject）
            var uiGameObject = new GameObject(Constants.GO_UI);
            uiGameObject.SetActive(false);  // 默认禁用，只在Locked状态时启用
            uiGameObject.transform.SetParent(avatarGameObject.transform, false);

            // 绑定到头部，保证UI始终在视野范围内
            var animator = avatarGameObject.GetComponent<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null)
            {
                var constraint = uiGameObject.AddComponent<VRCParentConstraint>();
                constraint.Sources.Add(new VRCConstraintSource
                {
                    Weight = 1f,
                    SourceTransform = head,
                    ParentPositionOffset = new Vector3(0f, -0.02f, 0.15f),  // 头部下方2cm，前方15cm
                    ParentRotationOffset = Vector3.zero
                });
                constraint.Locked = true;
                constraint.IsActive = true;
            }

            Debug.Log(I18n.T("log.visual_canvas_created"));
            this.uiGameObject = uiGameObject;
            return uiGameObject;
        }

        /// <summary>
        /// 创建3D倒计时进度条（显示剩余时间）
        /// VRCConstraint绑定到头部，前方15cm处显示
        /// </summary>
        private GameObject CreateCountdownBar()
        {
            // 创建容器（挂在uiGameObject下）
            var containerObj = new GameObject("CountdownBar");
            containerObj.transform.SetParent(uiGameObject.transform, false);
            // 容器的位置（相对于头部，由uiGameObject的VRCParentConstraint已处理）
            containerObj.transform.localPosition = Vector3.zero;
            containerObj.transform.localRotation = Quaternion.identity;
            // 尺寸：15cm宽，3cm高，3cm厚（3D条）
            containerObj.transform.localScale = new Vector3(0.15f, 0.03f, 0.03f);
            containerObj.SetActive(true);

            // 背景（白色）
            var bgObj = CreateSimpleQuad("Background", containerObj.transform, new Vector3(0f, 0f, 0.001f), Vector3.one, new Color(1f, 1f, 1f, 1f));

            // 前景条（红色，通过localScale.x 控制长度）
            var barObj = CreateSimpleQuad("Bar", containerObj.transform, Vector3.zero, new Vector3(1f, 0.1f, 1f), new Color(1f, 0f, 0f, 1f));

            Debug.Log(I18n.T("log.visual_countdown_created"));
            return containerObj;
        }

        /// <summary>
        /// 手动创建简单Quad
        /// </summary>
        private GameObject CreateSimpleQuad(string name, Transform parent, Vector3 localPos, Vector3 localScale, Color color)
        {
            var quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localScale = localScale;
            quad.transform.localRotation = Quaternion.identity;
            var mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, 0.5f, 0)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };
            mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();

            var meshFilter = quad.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            var meshRenderer = quad.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = color };

            return quad;
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
            Debug.Log($"[ASS] 已创建 AudioSource: {objectName}");
            return obj;
        }

    }
}
