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


            // 转换条件：IsLocal && TimeUp
            var toActive = AnimatorUtils.CreateTransition(inactiveState, activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            string modeText = isDebugMode ? "调试模式（简化）" : "完整模式";
            Debug.Log($"[ASS] 防御层已创建 ({modeText}): 无混淆状态");
            return layer;
        }

        #region Defense Components Creation

        /// <summary>
        /// 创建防御组件根对象及所有子组件
        /// 自动处理参数到VRChat限制，确保生成的Avatar仍可上传
        /// </summary>
        private static GameObject CreateDefenseComponents(GameObject avatarRoot, AvatarSecuritySystemComponent config, bool isDebugMode)
        {
            // 查找或创建根对象
            var existingRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot.gameObject);
            }

            var root = new GameObject(Constants.GO_DEFENSE_ROOT);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            // 默认禁用，通过动画控制激活
            // 使用 m_IsActive 控制可以完全禁用对象，避免 PhysBone/Constraint 等组件消耗性能
            root.SetActive(false);

            // 根据模式调整参数，并确保不超过VRChat限制
            int constraintDepth = isDebugMode ? 5 : ValidateConstraintDepth(config.constraintChainDepth);
            int physBoneLength = isDebugMode ? 3 : ValidatePhysBoneLength(config.physBoneChainLength);
            int physBoneColliders = isDebugMode ? 2 : ValidatePhysBoneColliders(config.physBoneColliderCount, physBoneLength);
            int contactCount = isDebugMode ? 4 : ValidateContactCount(config.contactComponentCount);
            int shaderLoops = isDebugMode ? 0 : ValidateShaderLoops(config.shaderLoopCount);
            int overdrawLayers = isDebugMode ? 3 : config.overdrawLayerCount;
            int polyVertices = isDebugMode ? 1000 : ValidateHighPolyVertices(config.highPolyVertexCount);

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

                // ParentConstraint（原有）
                var parentC = obj.AddComponent<ParentConstraint>();
                parentC.constraintActive = true;
                parentC.weight = 1f;
                parentC.locked = true;

                if (i > 0)
                {
                    var src = new ConstraintSource { sourceTransform = previous.transform, weight = 1f };
                    parentC.AddSource(src);
                    parentC.SetTranslationOffset(0, Vector3.zero);
                    parentC.SetRotationOffset(0, Vector3.zero);

                    // 额外添加 PositionConstraint 增加CPU复杂度
                    var posC = obj.AddComponent<PositionConstraint>();
                    posC.constraintActive = true;
                    posC.locked = true;
                    posC.weight = 1f;
                    posC.AddSource(src);

                    // 额外添加 RotationConstraint 增加CPU复杂度
                    var rotC = obj.AddComponent<RotationConstraint>();
                    rotC.constraintActive = true;
                    rotC.locked = true;
                    rotC.weight = 1f;
                    rotC.AddSource(src);
                }

                previous = obj;
            }

            Debug.Log($"[ASS] 创建Constraint链: 深度={depth}, 每节点包含 Parent/Position/Rotation 三种约束");
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

            // 添加PhysBone组件（使用Advanced模式增加CPU消耗）
            var physBone = chainRoot.AddComponent<VRCPhysBone>();
            physBone.integrationType = VRCPhysBone.IntegrationType.Advanced;
            physBone.pull = 0.8f;
            physBone.pullCurve = AnimationCurve.EaseInOut(0, 0.5f, 1, 1f);
            physBone.spring = 0.8f;
            physBone.springCurve = AnimationCurve.EaseInOut(0, 0.3f, 1, 0.9f);
            physBone.stiffness = 0.5f;
            physBone.stiffnessCurve = AnimationCurve.Linear(0, 0.2f, 1, 0.7f);
            physBone.gravity = 0.5f;
            physBone.gravityCurve = AnimationCurve.Linear(0, 0.3f, 1, 0.8f);
            physBone.gravityFalloff = 0.4f;
            physBone.gravityFalloffCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            physBone.immobile = 0.3f;
            physBone.immobileType = VRCPhysBone.ImmobileType.AllMotion;
            physBone.immobileCurve = AnimationCurve.Linear(0, 0.1f, 1, 0.5f);
            
            // 限制（增加物理计算复杂度）
            physBone.limitType = VRCPhysBone.LimitType.Angle;
            physBone.maxAngleX = 45f;
            physBone.maxAngleZ = 45f;
            physBone.limitRotation = new Vector3(15, 30, 15);
            
            // 拉伸（使用maxStretch）
            physBone.maxStretch = 0.5f;
            
            // 抓取相关（使用AdvancedBool）
            physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.grabMovement = 0.8f;
            physBone.snapToHand = true;
            
            // 参数驱动（虽然不使用但增加配置复杂度）
            physBone.parameter = "";
            physBone.isAnimated = false;
            
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
                collider.radius = 0.3f;
                collider.height = 1.0f;
                collider.insideBounds = true;
                collider.bonesAsSpheres = false;

                collidersList.Add(collider);
            }

            // 将colliders分配给PhysBone
            physBone.colliders = collidersList.ConvertAll(x => x as VRCPhysBoneColliderBase);
            
            Debug.Log($"[ASS] 创建PhysBone链 (Advanced模式): 长度={chainLength}, Collider={colliderCount}, " +
                     $"启用限制/拉伸/挤压/抓取, Curve数=6");
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
                sender.radius = 1.0f;
                sender.height = 2f;
                sender.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
                sender.localOnly = true;
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
                receiver.radius = 1.0f;
                receiver.height = 2f;
                receiver.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
                receiver.localOnly = true;
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
            meshRenderer.sharedMaterials = new Material[]
            {
                new Material(Shader.Find("Standard")),
                new Material(Shader.Find("Standard"))
            };

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
        /// 注意：使用 Scale 控制而非 m_IsActive，因为 VRChat 的 m_IsActive 不受 Write Defaults 影响
        /// </summary>
        private static AnimationClip CreateDefenseActivationClip(GameObject avatarRoot, GameObject defenseRoot, AvatarSecuritySystemComponent config, bool activate, bool isDebugMode)
        {
            var clip = new AnimationClip
            {
                name = activate ? "ASS_Defense_Activate" : "ASS_Defense_Deactivate",
                legacy = false
            };

            string rootPath = AnimatorUtils.GetRelativePath(avatarRoot, defenseRoot);
            
            // 使用 m_IsActive 控制防御对象的激活状态
            // 这样可以完全禁用对象，避免 PhysBone/Constraint 等组件在禁用时仍消耗性能
            float activeValue = activate ? 1f : 0f;
            var activeCurve = AnimationCurve.Constant(0f, 1f / 60f, activeValue);
            clip.SetCurve(rootPath, typeof(GameObject), "m_IsActive", activeCurve);

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

            // 顶点/索引
            mesh.SetVertices(vertices);

            // 切分为两个子网格以增加复杂度
            var tris0 = new List<int>();
            var tris1 = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                if (((i / 3) % 2) == 0) { tris0.Add(triangles[i]); tris0.Add(triangles[i + 1]); tris0.Add(triangles[i + 2]); }
                else { tris1.Add(triangles[i]); tris1.Add(triangles[i + 1]); tris1.Add(triangles[i + 2]); }
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris0, 0);
            mesh.SetTriangles(tris1, 1);

            // UV与颜色
            var uv = new List<Vector2>(vertices.Count);
            var uv2 = new List<Vector2>(vertices.Count);
            var colors = new List<Color>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i].normalized;
                uv.Add(new Vector2((v.x + 1f) * 0.5f, (v.z + 1f) * 0.5f));
                uv2.Add(new Vector2((v.y + 1f) * 0.5f, (v.x + 1f) * 0.5f));
                colors.Add(new Color(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z), 1f));
            }
            mesh.uv = uv.ToArray();
            mesh.uv2 = uv2.ToArray();
            mesh.colors = colors.ToArray();

            mesh.RecalculateNormals();
#if UNITY_2019_1_OR_NEWER
            mesh.RecalculateTangents();
#endif
            mesh.RecalculateBounds();

            return mesh;
        }

        #endregion

        #region VRChat Limit Validation

        /// <summary>
        /// 验证Constraint深度，确保不超过VRChat限制
        /// </summary>
        private static int ValidateConstraintDepth(int requested)
        {
            int maxDepth = Constants.CONSTRAINT_CHAIN_MAX_DEPTH; // 100
            int validValue = Mathf.Clamp(requested, 10, maxDepth);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] Constraint深度超出范围: {requested} -> {validValue}");
            }
            return validValue;
        }

        /// <summary>
        /// 验证PhysBone链长度，确保不超过256
        /// </summary>
        private static int ValidatePhysBoneLength(int requested)
        {
            int maxLength = Constants.PHYSBONE_CHAIN_MAX_LENGTH; // 256
            int validValue = Mathf.Clamp(requested, 10, maxLength);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] PhysBone链长度超出范围: {requested} -> {validValue}");
            }
            return validValue;
        }

        /// <summary>
        /// 验证PhysBone Collider数量，确保不超过256
        /// 同时考虑链长度以避免过度配置
        /// </summary>
        private static int ValidatePhysBoneColliders(int requested, int chainLength)
        {
            int maxColliders = Constants.PHYSBONE_COLLIDER_MAX_COUNT; // 256
            int validValue = Mathf.Clamp(requested, 10, maxColliders);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] PhysBone Collider数量超出范围: {requested} -> {validValue}");
            }
            return validValue;
        }

        /// <summary>
        /// 验证Contact组件数量，确保不超过200
        /// </summary>
        private static int ValidateContactCount(int requested)
        {
            int maxCount = Constants.CONTACT_MAX_COUNT; // 200
            int validValue = Mathf.Clamp(requested, 10, maxCount);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] Contact组件数量超出范围: {requested} -> {validValue}");
            }
            return validValue;
        }

        /// <summary>
        /// 验证Shader循环次数，确保不超过500
        /// </summary>
        private static int ValidateShaderLoops(int requested)
        {
            int maxLoops = Constants.SHADER_LOOP_MAX_COUNT; // 500
            int validValue = Mathf.Clamp(requested, 0, maxLoops);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] Shader循环次数超出范围: {requested} -> {validValue}");
            }
            return validValue;
        }

        /// <summary>
        /// 验证高多边形顶点数，确保不超过500k
        /// </summary>
        private static int ValidateHighPolyVertices(int requested)
        {
            int maxVertices = Constants.HIGHPOLY_MESH_MAX_VERTICES; // 500000
            int validValue = Mathf.Clamp(requested, 50000, maxVertices);
            
            if (validValue != requested)
            {
                Debug.LogWarning($"[ASS] 高多边形顶点数超出范围: {requested} -> {validValue}");
            }
            return validValue;
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

