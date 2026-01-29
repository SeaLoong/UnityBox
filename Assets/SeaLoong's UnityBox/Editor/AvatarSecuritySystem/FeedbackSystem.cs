using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
#endif

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 视觉和音频反馈系统生成器
    /// 功能：倒计时警告、错误提示、成功反馈、HUD显示
    /// </summary>
    public static class FeedbackSystem
    {
        #region HUD Canvas创建

        /// <summary>
        /// 创建3D HUD（替代Canvas），在视角前显示倒计时条
        /// VRCConstraint绑定到头部，保证UI始终在视野内
        /// </summary>
        public static GameObject CreateHUDCanvas(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            // 查找或创建Canvas
            Transform existingCanvas = avatarRoot.transform.Find(Constants.GO_UI_CANVAS);
            if (existingCanvas != null)
            {
                Debug.Log(I18n.T("log.visual_existing_canvas"));
                return existingCanvas.gameObject;
            }

            // 创建根对象（直接挂在avatarRoot）
            var canvasObj = new GameObject(Constants.GO_UI_CANVAS);
            canvasObj.transform.SetParent(avatarRoot.transform, false);
            canvasObj.SetActive(false);  // 默认禁用，只在Locked状态时启用

#if VRC_SDK_VRCSDK3
            // 绑定到头部，保证UI始终在视野范围内
            var animator = avatarRoot.GetComponent<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null)
            {
                var constraint = canvasObj.AddComponent<VRCParentConstraint>();
                constraint.Sources.Add(new VRCConstraintSource
                {
                    Weight = 1f,
                    SourceTransform = head,
                    ParentPositionOffset = new Vector3(0f, -0.02f, 0.15f),  // 头部下方2cm，前方15cm
                    ParentRotationOffset = Vector3.zero
                });
                
                // 使用SerializedObject设置属性，避免触发Editor GUI事件
                var constraintSer = new SerializedObject(constraint);
                constraintSer.FindProperty("IsActive").boolValue = true;
                constraintSer.FindProperty("Locked").boolValue = true;
                constraintSer.ApplyModifiedPropertiesWithoutUndo();
            }
#endif

            Debug.Log(I18n.T("log.visual_canvas_created"));
            return canvasObj;
        }

        /// <summary>
        /// 创建3D倒计时进度条（显示剩余时间）
        /// VRCConstraint绑定到头部，前方15cm处显示
        /// </summary>
        public static GameObject CreateCountdownBar(GameObject canvasObj, AvatarSecuritySystemComponent config)
        {
            // 创建容器（挂在HUD canvas下）
            var containerObj = new GameObject("CountdownBar");
            containerObj.transform.SetParent(canvasObj.transform, false);
            
            // 容器的位置（相对于头部，由canvasObj的VRCParentConstraint已处理）
            containerObj.transform.localPosition = Vector3.zero;
            containerObj.transform.localRotation = Quaternion.identity;
            // 尺寸：15cm宽，3cm高，3cm厚（3D条）
            containerObj.transform.localScale = new Vector3(0.15f, 0.03f, 0.03f);
            containerObj.SetActive(true);

            // 背景（白色）- 手动创建Quad避免CreatePrimitive触发的Editor事件
            var bgObj = CreateSimpleQuad("Background", containerObj.transform, new Vector3(0f, 0f, 0.001f), Vector3.one);
            var bgRenderer = bgObj.GetComponent<MeshRenderer>();
            bgRenderer.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 1f, 1f, 1f) };

            // 前景条（红色，通过localScale.x 控制长度）
            var barObj = CreateSimpleQuad("Bar", containerObj.transform, Vector3.zero, new Vector3(1f, 0.1f, 1f));
            var barRenderer = barObj.GetComponent<MeshRenderer>();
            barRenderer.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0f, 0f, 1f) };

            Debug.Log(I18n.T("log.visual_countdown_created"));
            return containerObj;
        }

        /// <summary>
        /// 手动创建简单Quad，避免使用GameObject.CreatePrimitive触发Editor事件
        /// </summary>
        private static GameObject CreateSimpleQuad(string name, Transform parent, Vector3 localPos, Vector3 localScale)
        {
            var quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localScale = localScale;
            
            // 手动创建Quad mesh
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
            
            quad.AddComponent<MeshRenderer>();
            
            return quad;
        }

        #endregion
    }
}
