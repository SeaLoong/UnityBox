using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using SeaLoongUnityBox;
using UnityEngine.Animations;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
#endif
using VRC.Dynamics;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 防御系统生成器 - 基于性能消耗的新一代防御
    /// 功能：倒计时结束时激活的防御机制
    /// 
    /// 防御机制包括：
    /// CPU消耗：Constraint链、PhysBone、Contact组件
    /// GPU消耗：复杂Shader、Overdraw堆叠、高面数Mesh
    /// 混淆机制：大量虚假Animator状态
    /// </summary>
    public static class DefenseSystem
    {
        private const string DEFENSE_ROOT_NAME = "__ASS_Defense__";

        /// <summary>
        /// 创建防御层
        /// </summary>
        /// <param name="isDebugMode">是否是调试模式（生成简化版）</param>
        public static AnimatorControllerLayer CreateDefenseLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config,
            bool isDebugMode = false)
        {
            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            // 状态：Inactive（防御未激活）
            var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(100, 50, 0));
            inactiveState.motion = AnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = inactiveState;

            // 状态：Active（防御激活）
            var activeState = layer.stateMachine.AddState("Active", new Vector3(100, 150, 0));
            
            // 创建防御组件并生成动画
            var defenseRoot = CreateDefenseComponents(avatarRoot, config, isDebugMode);
            var activateClip = CreateDefenseActivationClip(avatarRoot, defenseRoot, config, true, isDebugMode);
            var deactivateClip = CreateDefenseActivationClip(avatarRoot, defenseRoot, config, false, isDebugMode);
            
            AnimatorUtils.AddSubAsset(controller, activateClip);
            AnimatorUtils.AddSubAsset(controller, deactivateClip);
            
            activeState.motion = activateClip;
            inactiveState.motion = deactivateClip;

            // 生成混淆状态（简化版50个，完整版1000个）
            int stateCount = isDebugMode ? 50 : 1000;
            GenerateDecoyStates(layer.stateMachine, stateCount);

            // 转换条件：IsLocal && TimeUp
            var toActive = AnimatorUtils.CreateTransition(inactiveState, activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            string modeText = isDebugMode ? "调试模式（简化）" : "完整模式";
            Debug.Log($"[ASS] 防御层已创建 ({modeText}): 混淆状态={stateCount}");
            return layer;
        }

        #region Defense Components Creation

        /// <summary>
        /// 创建防御组件根对象及所有子组件
        /// </summary>
        private static GameObject CreateDefenseComponents(GameObject avatarRoot, AvatarSecuritySystemComponent config, bool isDebugMode)
        {
            // 查找或创建根对象
            var existingRoot = avatarRoot.transform.Find(DEFENSE_ROOT_NAME);
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot.gameObject);
            }

            var root = new GameObject(DEFENSE_ROOT_NAME);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(false); // 默认禁用

            // 根据模式调整参数
            int constraintDepth = isDebugMode ? 5 : config.constraintChainDepth;
            int physBoneLength = isDebugMode ? 3 : config.physBoneChainLength;
            int physBoneColliders = isDebugMode ? 2 : config.physBoneColliderCount;
            int contactCount = isDebugMode ? 4 : config.contactComponentCount;
            int shaderLoops = isDebugMode ? 0 : config.shaderLoopCount;
            int overdrawLayers = isDebugMode ? 3 : config.overdrawLayerCount;
            int polyVertices = isDebugMode ? 1000 : config.highPolyVertexCount;

            // 创建CPU消耗组件
            if (config.enableConstraintChain)
            {
                CreateConstraintChain(root, constraintDepth);
            }

#if VRC_SDK_VRCSDK3
            if (config.enablePhysBone)
            {
                CreatePhysBoneChains(root, physBoneLength, physBoneColliders);
            }

            if (config.enableContactSystem)
            {
                CreateContactSystem(root, contactCount);
            }
#endif

            // 创建GPU消耗组件
            if (config.enableOverdraw)
            {
                CreateOverdrawLayers(root, overdrawLayers);
            }

            if (config.enableHighPolyMesh)
            {
                CreateHighPolyMesh(root, polyVertices);
            }

            if (config.enableHeavyShader && shaderLoops > 0)
            {
                CreateHeavyShaderMesh(root, shaderLoops);
            }

            return root;
        }

        /// <summary>
        /// 创建链式Constraint结构
        /// </summary>
        private static void CreateConstraintChain(GameObject root, int depth)
        {
            var chainRoot = new GameObject("ConstraintChain");
            chainRoot.transform.SetParent(root.transform);

            GameObject previous = chainRoot;
            for (int i = 0; i < depth; i++)
            {
                var obj = new GameObject($"Constraint_{i}");
                obj.transform.SetParent(chainRoot.transform);
                obj.transform.localPosition = new Vector3(0, i * 0.01f, 0);

                var constraint = obj.AddComponent<ParentConstraint>();
                constraint.constraintActive = true;
                constraint.weight = 1f;
                constraint.locked = true;

                if (i > 0)
                {
                    var source = new ConstraintSource
                    {
                        sourceTransform = previous.transform,
                        weight = 1f
                    };
                    constraint.AddSource(source);
                    constraint.SetTranslationOffset(0, Vector3.zero);
                    constraint.SetRotationOffset(0, Vector3.zero);
                }

                previous = obj;
            }

            Debug.Log($"[ASS] 创建Constraint链: 深度={depth}");
        }

#if VRC_SDK_VRCSDK3
        /// <summary>
        /// 创建PhysBone长链及Collider
        /// </summary>
        private static void CreatePhysBoneChains(GameObject root, int chainLength, int colliderCount)
        {
            var physBoneRoot = new GameObject("PhysBoneChains");
            physBoneRoot.transform.SetParent(root.transform);

            // 创建骨骼链
            var chainRoot = new GameObject("BoneChain");
            chainRoot.transform.SetParent(physBoneRoot.transform);
            chainRoot.transform.localPosition = Vector3.zero;

            Transform previous = chainRoot.transform;
            for (int i = 0; i < chainLength; i++)
            {
                var bone = new GameObject($"Bone_{i}");
                bone.transform.SetParent(previous);
                bone.transform.localPosition = new Vector3(0, 0.1f, 0);
                previous = bone.transform;
            }

            // 添加PhysBone组件
            var physBone = chainRoot.AddComponent<VRCPhysBone>();
            physBone.integrationType = VRCPhysBone.IntegrationType.Simplified;
            physBone.pull = 0.8f;
            physBone.spring = 0.8f;
            physBone.stiffness = 0.5f;
            physBone.gravity = 0.5f;
            physBone.immobile = 0.3f;
            var collidersList = new List<VRCPhysBoneCollider>();

            // 创建Collider
            for (int i = 0; i < colliderCount; i++)
            {
                var colliderObj = new GameObject($"Collider_{i}");
                colliderObj.transform.SetParent(physBoneRoot.transform);
                colliderObj.transform.localPosition = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 2f),
                    Random.Range(-1f, 1f)
                );

                var collider = colliderObj.AddComponent<VRCPhysBoneCollider>();
                collider.shapeType = VRCPhysBoneCollider.ShapeType.Capsule;
                collider.radius = 0.1f;
                collider.height = 0.5f;

                collidersList.Add(collider);
            }

            Debug.Log($"[ASS] 创建PhysBone链: 长度={chainLength}, Collider={colliderCount}");
            
                    // 将colliders分配给PhysBone
                    // VRCPhysBoneCollider继承自VRCPhysBoneColliderBase，可以直接赋值
                    physBone.colliders = collidersList.ConvertAll(x => x as VRCPhysBoneColliderBase);
        }

        /// <summary>
        /// 创建Contact Sender/Receiver系统
        /// </summary>
        private static void CreateContactSystem(GameObject root, int componentCount)
        {
            var contactRoot = new GameObject("ContactSystem");
            contactRoot.transform.SetParent(root.transform);

            int halfCount = componentCount / 2;

            // 创建Sender
            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"Sender_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
                senderObj.transform.localPosition = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 2f),
                    Random.Range(-1f, 1f)
                );

                var sender = senderObj.AddComponent<VRCContactSender>();
                sender.shapeType = VRCContactSender.ShapeType.Capsule;
                sender.radius = 0.5f;
                sender.height = 1f;
                sender.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3" };
            }

            // 创建Receiver
            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"Receiver_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
                receiverObj.transform.localPosition = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 2f),
                    Random.Range(-1f, 1f)
                );

                var receiver = receiverObj.AddComponent<VRCContactReceiver>();
                receiver.shapeType = VRCContactReceiver.ShapeType.Capsule;
                receiver.radius = 0.5f;
                receiver.height = 1f;
                receiver.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3" };
            }

            Debug.Log($"[ASS] 创建Contact系统: {halfCount} senders + {halfCount} receivers");
        }
#endif

        /// <summary>
        /// 创建Overdraw透明层堆叠
        /// </summary>
        private static void CreateOverdrawLayers(GameObject root, int layerCount)
        {
            var overdrawRoot = new GameObject("OverdrawLayers");
            overdrawRoot.transform.SetParent(root.transform);

            var material = new Material(Shader.Find("Unlit/Transparent"));
            material.color = new Color(1, 1, 1, 0.5f);
            material.renderQueue = 3000;

            for (int i = 0; i < layerCount; i++)
            {
                var layerObj = new GameObject($"Layer_{i}");
                layerObj.transform.SetParent(overdrawRoot.transform);
                layerObj.transform.localPosition = new Vector3(0, 0, i * 0.001f);

                var meshFilter = layerObj.AddComponent<MeshFilter>();
                meshFilter.mesh = CreateQuadMesh(2f);

                var meshRenderer = layerObj.AddComponent<MeshRenderer>();
                meshRenderer.material = material;
            }

            Debug.Log($"[ASS] 创建Overdraw层: {layerCount}层");
        }

        /// <summary>
        /// 创建高面数Mesh
        /// </summary>
        private static void CreateHighPolyMesh(GameObject root, int targetVertexCount)
        {
            var meshObj = new GameObject("HighPolyMesh");
            meshObj.transform.SetParent(root.transform);
            meshObj.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

            var mesh = CreateHighDensitySphereMesh(targetVertexCount);
            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            meshRenderer.material = new Material(Shader.Find("Standard"));

            Debug.Log($"[ASS] 创建高面数Mesh: {mesh.vertexCount}顶点, {mesh.triangles.Length / 3}三角形");
        }

        /// <summary>
        /// 创建带复杂Shader的Mesh
        /// </summary>
        private static void CreateHeavyShaderMesh(GameObject root, int loopCount)
        {
            var meshObj = new GameObject("HeavyShaderMesh");
            meshObj.transform.SetParent(root.transform);

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh(1f);

            var material = CreateHeavyShaderMaterial(loopCount);
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            meshRenderer.material = material;

            Debug.Log($"[ASS] 创建复杂Shader: 循环={loopCount}");
        }

        #endregion

        #region Animation Creation

        /// <summary>
        /// 创建防御激活/停用动画剪辑
        /// </summary>
        private static AnimationClip CreateDefenseActivationClip(GameObject avatarRoot, GameObject defenseRoot, AvatarSecuritySystemComponent config, bool activate, bool isDebugMode)
        {
            var clip = new AnimationClip
            {
                name = activate ? "ASS_Defense_Activate" : "ASS_Defense_Deactivate",
                legacy = false
            };

            string rootPath = AnimatorUtils.GetRelativePath(avatarRoot, defenseRoot);
            
            // 控制根对象Active状态
            var curve = AnimationCurve.Constant(0f, 1f / 60f, activate ? 1f : 0f);
            clip.SetCurve(rootPath, typeof(GameObject), "m_IsActive", curve);

            // 控制Shader参数
            if (config.enableHeavyShader && !isDebugMode)
            {
                string shaderMeshPath = $"{rootPath}/HeavyShaderMesh";
                var loopCurve = AnimationCurve.Constant(0f, 1f / 60f, activate ? config.shaderLoopCount : 0f);
                clip.SetCurve(shaderMeshPath, typeof(MeshRenderer), "material._LoopCount", loopCurve);
            }

            return clip;
        }

        #endregion

        #region Decoy States Generation

        /// <summary>
        /// 生成大量混淆状态
        /// </summary>
        private static void GenerateDecoyStates(AnimatorStateMachine stateMachine, int stateCount)
        {
            var sharedClip = AnimatorUtils.SharedEmptyClip;

            for (int i = 0; i < stateCount; i++)
            {
                int x = (i % 10) * 200 + 400;
                int y = (i / 10) * 100 + 50;

                var decoyState = stateMachine.AddState($"Decoy_{i}", new Vector3(x, y, 0));
                decoyState.motion = sharedClip;
                decoyState.writeDefaultValues = true;
            }

            Debug.Log($"[ASS] 生成混淆状态: {stateCount}个");
        }

        #endregion

        #region Mesh Generation Utilities

        private static Mesh CreateQuadMesh(float size)
        {
            var mesh = new Mesh { name = "Quad" };
            
            mesh.vertices = new Vector3[]
            {
                new Vector3(-size/2, -size/2, 0),
                new Vector3(size/2, -size/2, 0),
                new Vector3(size/2, size/2, 0),
                new Vector3(-size/2, size/2, 0)
            };

            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateHighDensitySphereMesh(int targetVertexCount)
        {
            var mesh = new Mesh { name = "HighPolySphere" };

            int subdivisions = Mathf.CeilToInt(Mathf.Sqrt(targetVertexCount / 6f));
            subdivisions = Mathf.Clamp(subdivisions, 10, 200);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int lat = 0; lat <= subdivisions; lat++)
            {
                float theta = lat * Mathf.PI / subdivisions;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= subdivisions; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / subdivisions;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 vertex = new Vector3(
                        cosPhi * sinTheta,
                        cosTheta,
                        sinPhi * sinTheta
                    );
                    vertices.Add(vertex);
                }
            }

            for (int lat = 0; lat < subdivisions; lat++)
            {
                for (int lon = 0; lon < subdivisions; lon++)
                {
                    int first = (lat * (subdivisions + 1)) + lon;
                    int second = first + subdivisions + 1;

                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);

                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        #endregion

        #region Shader Creation

        private static Material CreateHeavyShaderMaterial(int loopCount)
        {
            var shader = Shader.Find("Standard");
            var material = new Material(shader);
            material.SetInt("_LoopCount", loopCount);
            return material;
        }

        #endregion
    }
}
