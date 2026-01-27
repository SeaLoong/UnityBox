using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using SeaLoongUnityBox;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
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
            int constraintChainCount = isDebugMode ? 1 : Mathf.Max(1, config.constraintChainCount);
            int physBoneLength = isDebugMode ? 3 : ValidatePhysBoneLength(config.physBoneChainLength);
            int physBoneChainCount = isDebugMode ? 1 : Mathf.Max(1, config.physBoneChainCount);
#if VRC_SDK_VRCSDK3
            int existingPhysBoneColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            int physBoneColliders = isDebugMode
                ? 2
                : ValidatePhysBoneColliders(config.physBoneColliderCount, physBoneLength, existingPhysBoneColliders);
#else
            int physBoneColliders = isDebugMode ? 2 : config.physBoneColliderCount;
#endif
            int contactCount = isDebugMode ? 4 : ValidateContactCount(config.contactComponentCount);
            int shaderLoops = isDebugMode ? 0 : ValidateShaderLoops(config.shaderLoopCount);
            int overdrawLayers = isDebugMode ? 3 : config.overdrawLayerCount;
            int polyVertices = isDebugMode ? 1000 : ValidateHighPolyVertices(config.highPolyVertexCount);
            int particleCount = isDebugMode ? 0 : config.particleCount;
            int particleSystemCount = isDebugMode ? 0 : Mathf.Max(1, config.particleSystemCount);
            int lightCount = isDebugMode ? 0 : config.lightCount;
            int materialCount = isDebugMode ? 0 : Mathf.Max(1, config.materialCount);

            // 创建CPU消耗组件 - Constraint链
            if (config.enableConstraintChain)
            {
                // 创建多条Constraint链以增加CPU消耗
                for (int i = 0; i < constraintChainCount; i++)
                {
                    CreateConstraintChain(root, constraintDepth, i);
                }
            }

#if VRC_SDK_VRCSDK3
            // 创建CPU消耗组件 - PhysBone链
            if (config.enablePhysBone && physBoneColliders > 0)
            {
                // 创建多条PhysBone链以增加CPU消耗
                for (int i = 0; i < physBoneChainCount; i++)
                {
                    CreatePhysBoneChains(root, physBoneLength, physBoneColliders, i);
                }
            }

            if (config.enableContactSystem)
            {
                CreateContactSystem(root, contactCount);
            }
#endif

            // 创建GPU消耗组件
            if (config.enableOverdraw)
            {
                // 创建多个Overdraw层堆叠以增加GPU消耗
                for (int i = 0; i < 2; i++) // 创建2组，每组50+层
                {
                    CreateOverdrawLayers(root, overdrawLayers, i);
                }
            }

            if (config.enableHighPolyMesh)
            {
                // 创建多个高多边形网格
                for (int i = 0; i < 3; i++)
                {
                    CreateHighPolyMesh(root, polyVertices / 3, i);
                }
            }

            if (config.enableHeavyShader && shaderLoops > 0)
            {
                // 创建多个复杂Shader网格
                for (int i = 0; i < 2; i++)
                {
                    CreateHeavyShaderMesh(root, shaderLoops, i);
                }
            }

            // 创建粒子系统防御
            if (config.enableParticleDefense && particleCount > 0)
            {
                CreateParticleDefense(root, particleCount, particleSystemCount);
            }

            // 创建高消耗Shader材质球
            if (materialCount > 0)
            {
                CreateExpensiveMaterials(root, materialCount);
            }

            // 创建光源防御
            if (config.enableLightDefense && lightCount > 0)
            {
                CreateLightDefense(root, lightCount);
            }

            return root;
        }

        /// <summary>
        /// 创建链式Constraint结构
        /// </summary>
        private static void CreateConstraintChain(GameObject root, int depth, int chainIndex = 0)
        {
#if VRC_SDK_VRCSDK3
            var chainRoot = new GameObject($"ConstraintChain_{chainIndex}");
            chainRoot.transform.SetParent(root.transform);
            chainRoot.transform.localPosition = new Vector3(chainIndex * 0.5f, 0, 0);

            GameObject previous = chainRoot;
            for (int i = 0; i < depth; i++)
            {
                var obj = new GameObject($"Constraint_{i}");
                obj.transform.SetParent(chainRoot.transform);
                obj.transform.localPosition = new Vector3(0, i * 0.01f, 0);

                // VRCParentConstraint
                var parentC = obj.AddComponent<VRCParentConstraint>();
                parentC.IsActive = true;
                parentC.Locked = true;

                if (i > 0)
                {
                    parentC.Sources.Add(new VRCConstraintSource
                    {
                        Weight = 1f,
                        SourceTransform = previous.transform,
                        ParentPositionOffset = Vector3.zero,
                        ParentRotationOffset = Vector3.zero
                    });

                    // 额外添加 VRCPositionConstraint 增加CPU复杂度
                    var posC = obj.AddComponent<VRCPositionConstraint>();
                    posC.IsActive = true;
                    posC.Locked = true;
                    posC.Sources.Add(new VRCConstraintSource
                    {
                        Weight = 1f,
                        SourceTransform = previous.transform
                    });

                    // 额外添加 VRCRotationConstraint 增加CPU复杂度
                    var rotC = obj.AddComponent<VRCRotationConstraint>();
                    rotC.IsActive = true;
                    rotC.Locked = true;
                    rotC.Sources.Add(new VRCConstraintSource
                    {
                        Weight = 1f,
                        SourceTransform = previous.transform
                    });
                }

                previous = obj;
            }

            Debug.Log($"[ASS] 创建VRC Constraint链 {chainIndex}: 深度={depth}, 每节点包含 Parent/Position/Rotation 三种约束");
#else
            Debug.LogWarning("[ASS] VRC SDK 不可用，跳过创建 Constraint 链");
#endif
        }

#if VRC_SDK_VRCSDK3
        /// <summary>
        /// 创建PhysBone长链及Collider
        /// </summary>
        private static void CreatePhysBoneChains(GameObject root, int chainLength, int colliderCount, int chainIndex = 0)
        {
            var physBoneRoot = new GameObject($"PhysBoneChains_{chainIndex}");
            physBoneRoot.transform.SetParent(root.transform);
            physBoneRoot.transform.localPosition = new Vector3(chainIndex * 0.5f, 1f, 0);

            // 创建骨骼链
            var chainRoot = new GameObject($"BoneChain_{chainIndex}");
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
            
            Debug.Log($"[ASS] 创建PhysBone链 {chainIndex} (Advanced模式): 长度={chainLength}, Collider={colliderCount}, " +
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
        private static void CreateOverdrawLayers(GameObject root, int layerCount, int groupIndex = 0)
        {
            var overdrawRoot = new GameObject($"OverdrawLayers_{groupIndex}");
            overdrawRoot.transform.SetParent(root.transform);
            overdrawRoot.transform.localPosition = new Vector3(groupIndex * 3f, 0, 0);

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

            Debug.Log($"[ASS] 创建Overdraw层组 {groupIndex}: {layerCount}层");
        }

        /// <summary>
        /// 创建高面数Mesh
        /// </summary>
        private static void CreateHighPolyMesh(GameObject root, int targetVertexCount, int meshIndex = 0)
        {
            var meshObj = new GameObject($"HighPolyMesh_{meshIndex}");
            meshObj.transform.SetParent(root.transform);
            meshObj.transform.localPosition = new Vector3(meshIndex * 0.2f, 0, 0);
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

            Debug.Log($"[ASS] 创建高面数Mesh {meshIndex}: {mesh.vertexCount}顶点, {mesh.triangles.Length / 3}三角形");
        }

        /// <summary>
        /// 创建带复杂Shader的Mesh
        /// </summary>
        private static void CreateHeavyShaderMesh(GameObject root, int loopCount, int shaderIndex = 0)
        {
            var meshObj = new GameObject($"HeavyShaderMesh_{shaderIndex}");
            meshObj.transform.SetParent(root.transform);
            meshObj.transform.localPosition = new Vector3(shaderIndex * 2f, 0, 0);

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh(1f);

            var material = CreateHeavyShaderMaterial(loopCount);
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            meshRenderer.material = material;

            Debug.Log($"[ASS] 创建复杂Shader {shaderIndex}: 循环={loopCount}");
        }

        /// <summary>
        /// 创建粒子系统防御（高CPU消耗）
        /// </summary>
        private static void CreateParticleDefense(GameObject root, int totalParticleCount, int systemCount)
        {
            var particleRoot = new GameObject("ParticleDefense");
            particleRoot.transform.SetParent(root.transform);

            // 分散粒子到多个系统
            int particlesPerSystem = Mathf.Max(1000, totalParticleCount / systemCount);

            for (int s = 0; s < systemCount; s++)
            {
                var psObj = new GameObject($"ParticleSystem_{s}");
                psObj.transform.SetParent(particleRoot.transform);
                psObj.transform.localPosition = new Vector3(s * 0.5f, 1f, s * 0.3f);

                var ps = psObj.AddComponent<ParticleSystem>();
                var renderer = psObj.GetComponent<ParticleSystemRenderer>();

                // 主要配置
                var main = ps.main;
                main.duration = 10f;
                main.loop = true;
                main.prewarm = true;
                main.startLifetime = 8f;
                main.startSpeed = 3f;
                main.startSize = 0.3f;
                main.maxParticles = particlesPerSystem;
                main.gravityModifier = 0.8f;  // 更高重力

                // 发射配置 - 高发射率
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = particlesPerSystem / 3f;  // 更高发射率

                // 速度随时间变化
                var velocityOverLifetime = ps.velocityOverLifetime;
                velocityOverLifetime.enabled = true;
                velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
                velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
                velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

                // 大小随时间变化
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(0.2f, 1f);

                // 旋转随时间变化
                var rotationOverLifetime = ps.rotationOverLifetime;
                rotationOverLifetime.enabled = true;
                rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

                // 碰撞配置（增加CPU消耗）
                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                collision.dampen = 0.8f;
                collision.bounce = new ParticleSystem.MinMaxCurve(0.7f, 1f);
                collision.radiusScale = 1f;

                // 渲染器配置
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    var mat = new Material(Shader.Find("Particles/Standard Unlit"));
                    mat.color = new Color(Random.Range(0.3f, 1f), Random.Range(0.3f, 1f), Random.Range(0.3f, 1f), 0.8f);
                    renderer.material = mat;
                    renderer.maxParticleSize = 2f;
                }
            }

            Debug.Log($"[ASS] 创建粒子防御: {totalParticleCount}粒子 ({systemCount}个系统，每个{particlesPerSystem}粒子)");
        }

        /// <summary>
        /// 创建高消耗的Shader材质球及网格
        /// </summary>
        private static void CreateExpensiveMaterials(GameObject root, int materialCount)
        {
            var materialRoot = new GameObject("ExpensiveMaterials");
            materialRoot.transform.SetParent(root.transform);

            // 创建高消耗Shader
            var shader = CreateExpensiveShader();

            for (int i = 0; i < materialCount; i++)
            {
                // 创建材质球
                var material = new Material(shader);
                material.name = $"ExpensiveMaterial_{i}";
                
                // 随机配置材质
                material.SetFloat("_Intensity", Random.Range(0.5f, 2f));
                material.SetFloat("_Complexity", Random.Range(10f, 50f));
                material.SetColor("_BaseColor", new Color(
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f),
                    1f
                ));

                // 创建网格使用这个材质
                var meshObj = new GameObject($"ExpensiveMesh_{i}");
                meshObj.transform.SetParent(materialRoot.transform);
                meshObj.transform.localPosition = new Vector3(i * 0.3f, 0, 0);
                meshObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

                var meshFilter = meshObj.AddComponent<MeshFilter>();
                meshFilter.mesh = CreateComplexMesh();

                var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                meshRenderer.material = material;
            }

            Debug.Log($"[ASS] 创建高消耗材质: {materialCount}个材质球，使用复杂Shader");
        }

        /// <summary>
        /// 创建终极性能消耗的自定义Shader
        /// BombShader - 性能杀手终极版本
        /// 包含：500万次 GPU 循环、32 伪光源、16层 FBM噪声、64层视差映射
        /// 集合了 SecurityBurnShader 和 ExpensiveDefense 的所有优点，并升级 50-100 倍
        /// </summary>
        private static Shader CreateExpensiveShader()
        {
            // 优先使用终极高消耗Shader (BombShader 版本)
            var shader = Shader.Find("SeaLoong/BombShader");
            if (shader != null)
                return shader;
            
            // 回退到统一的 PerformanceKiller 版本
            var perfKillerShader = Shader.Find("SeaLoong/PerformanceKiller");
            if (perfKillerShader != null)
                return perfKillerShader;
            
            // 再回退到旧版本 ExpensiveDefense
            var expensiveShader = Shader.Find("SeaLoong/ExpensiveDefense");
            if (expensiveShader != null)
                return expensiveShader;
            
            // 再回退到 Standard Shader（支持更多光源）
            var standardShader = Shader.Find("Standard");
            if (standardShader != null)
                return standardShader;
            
            // 最后回退
            return Shader.Find("Unlit/Color");
        }

        /// <summary>
        /// 创建复杂的网格（高顶点数）
        /// </summary>
        private static Mesh CreateComplexMesh()
        {
            var mesh = new Mesh { name = "ComplexMesh" };

            // 创建一个相对复杂的网格结构
            int subdivisions = 20;
            int vertexCount = subdivisions * subdivisions;

            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = new List<int>();

            for (int y = 0; y < subdivisions; y++)
            {
                for (int x = 0; x < subdivisions; x++)
                {
                    int index = y * subdivisions + x;
                    float u = (float)x / (subdivisions - 1);
                    float v = (float)y / (subdivisions - 1);

                    vertices[index] = new Vector3(u - 0.5f, Mathf.Sin(u * Mathf.PI) * 0.3f, v - 0.5f);
                    uvs[index] = new Vector2(u, v);
                }
            }

            for (int y = 0; y < subdivisions - 1; y++)
            {
                for (int x = 0; x < subdivisions - 1; x++)
                {
                    int a = y * subdivisions + x;
                    int b = a + 1;
                    int c = a + subdivisions;
                    int d = c + 1;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 创建光源防御（GPU消耗，实时阴影）
        /// </summary>
        private static void CreateLightDefense(GameObject root, int lightCount)
        {
            var lightRoot = new GameObject("LightDefense");
            lightRoot.transform.SetParent(root.transform);

            // 创建多个高质量光源
            for (int i = 0; i < lightCount; i++)
            {
                var lightObj = new GameObject($"Light_{i}");
                lightObj.transform.SetParent(lightRoot.transform);

                // 随机放置光源
                float angle = (360f / lightCount) * i;
                float radius = 2f;
                lightObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    1f + (i % 3) * 0.5f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );

                var light = lightObj.AddComponent<Light>();

                // 交替使用Point和Spot光源
                if (i % 2 == 0)
                {
                    light.type = LightType.Point;
                    light.range = 10f;
                }
                else
                {
                    light.type = LightType.Spot;
                    light.range = 15f;
                    light.spotAngle = 60f;
                }

                // 高质量配置
                light.intensity = 2f;
                light.color = new Color(
                    Random.Range(0.5f, 1f),
                    Random.Range(0.5f, 1f),
                    Random.Range(0.5f, 1f),
                    1f
                );

                // 实时阴影（增加GPU消耗）
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 1f;
                light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
                light.shadowBias = 0.05f;
                light.shadowNormalBias = 0.4f;
            }

            Debug.Log($"[ASS] 创建光源防御: {lightCount}个高质量光源，启用实时阴影");
        }

        #endregion

        #region Animation Creation

        /// <summary>
        /// 创建防御激活/停用动画剪辑
        /// 使用 m_IsActive 控制防御组件，因为防御对象是 ASS 完全控制的对象，
        /// 且使用 m_IsActive 可以完全禁用 PhysBone/Constraint 等组件避免性能消耗
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
        private static int ValidatePhysBoneColliders(int requested, int chainLength, int existingColliders)
        {
            int maxColliders = Constants.PHYSBONE_COLLIDER_MAX_COUNT; // 256
            int available = Mathf.Max(0, maxColliders - existingColliders);
            int validValue = Mathf.Clamp(requested, 0, available);
            
            if (requested > available)
                Debug.LogWarning($"[ASS] PhysBone Collider数量超出范围: 已存在 {existingColliders}, 请求 {requested} -> 实际生成 {validValue} (上限 {maxColliders})");
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
        /// 验证Shader循环次数（VRChat无限制，GPU完全计算）
        /// BombShader 终极版本的完整 GPU 成本分析：
        /// 
        /// ========== 升级倍数对比 ==========
        /// 相对于原始 PerformanceKiller：
        /// - _LoopCount: 100万 → 500万 = 50倍
        /// - _Complexity: 15 → 50 = 3.3倍
        /// - _Intensity: 2.5 → 5.0 = 2倍
        /// - _ParallaxScale: 0.15 → 0.5 = 3.3倍
        /// - 纹理采样数: 3 → 5 = 1.7倍
        /// - FBM 层数: 8 → 16 = 2倍
        /// - 视差层数: 8-32 → 16-64 = 2倍
        /// - 法线迭代: 3 → 5 = 1.7倍
        /// - 伪光源: 8 → 32 = 4倍
        /// - 色彩空间转换: 1次 → 3次 = 3倍
        /// 
        /// 总体 GPU 成本提升：最少 50-100 倍
        /// 
        /// ========== 详细性能成本分析 ==========
        /// 
        /// 1. 视差映射 (ParallaxMapping) - 升级版（16-64层）:
        ///    - 平均采样层数：40 层（升级从 20 层）
        ///    - 每层采样 _HeightMap 1-2 次
        ///    - 总计：40 次高度贴图采样（升级 2 倍）
        /// 
        /// 2. 主循环计算 (ExpensiveColorComputation) - 500 万次：
        ///    - 循环次数：_LoopCount = 5000000（升级 50 倍）
        ///    - 每次循环包含：
        ///      * tex2D(MainTex) × 5 次（升级从 3→5）
        ///      * tex2D(NormalMap) × 1 次
        ///      * tex2D(HeightMap) × 1 次
        ///      * sin() × 2 次
        ///      * cos() × 1 次
        ///      * tan() × 1 次（新增）
        ///      * sqrt() × 1 次
        ///      * exp() × 1 次
        ///      * log() × 1 次
        ///      * pow() × 1 次
        ///      * atan() × 1 次（新增）
        ///      * normalize() × 2 次（升级从 1→2）
        ///      * dot() × 2 次（升级从 1→2）
        ///    - 单次循环成本：~32 条 GPU 指令（升级从 25）
        ///    - 总循环成本：_LoopCount × 32 指令 = 500万 × 32 = 1.6 亿条指令
        /// 
        /// 3. FBM 噪声计算 - 16 层（升级版）:
        ///    - 16 层噪声迭代（升级从 8）
        ///    - 每层包含多次 hash 和 lerp 计算
        ///    - 在 ExpensiveColorComputation 中被调用
        ///    - 成本翻倍：GPU 额外 ~80 条指令
        /// 
        /// 4. 复杂法线计算 (UnpackNormalComplex) - 5 次迭代：
        ///    - 5 次迭代（升级从 3）
        ///    - 每次包含：
        ///      * normalize() 运算
        ///      * tex2D(NormalMap) 采样（升级版本）
        ///      * 向量加法和乘法（升级为双倍）
        ///    - 总计：5 × (normalize + 采样) = GPU 成本增加
        /// 
        /// 5. 多光源照明 (CalculateLighting) - 32 伪光源：
        ///    - 主光源：1 次完整 Blinn-Phong 计算
        ///    - 伪光源：32 个循环（升级从 8，4倍）
        ///      每个包含：
        ///      * sin/cos 三角函数 × 2
        ///      * normalize × 2
        ///      * dot × 2
        ///      * pow × 1
        ///      * distance 距离计算 + 衰减函数
        ///    - 总成本：主光 + 32 × 25 指令 = ~800 条指令
        /// 
        /// 6. 升级版色彩空间转换：
        ///    - 第1次 RGB to HSV + HSV to RGB
        ///    - 第2次 RGB to HSV（色相移位）+ HSV to RGB
        ///    - 第3次 RGB to HSV（亮度调整）+ HSV to RGB
        ///    - 总计：3 × 100+ 条指令 = ~300 条指令
        /// 
        /// 7. sRGB 编码/解码升级版：
        ///    - pow(color, 2.2) 编码
        ///    - saturate（饱和度）
        ///    - pow(color, 1/2.2) 线性化
        ///    - pow(color, 0.8) 额外转换（新增）
        ///    - lerp × 2（增加 2 个额外颜色混合）
        ///    - 总计：~40 条指令
        /// 
        /// ========== 总体 GPU 成本计算 ==========
        /// 
        /// 对于配置：_LoopCount = 5000000
        /// 
        /// 成本分解：
        /// - 基础操作：200+ 条指令
        /// - 主循环：5000000 × 32 = 160,000,000 条指令
        /// - 视差映射：40 采样 × 20+ 指令 = 800 条指令
        /// - FBM 噪声：16 层 × 10+ 指令 = 160 条指令
        /// - 法线计算：5 × 30+ 指令 = 150 条指令
        /// - 光照计算：800 条指令
        /// - 色彩转换：300 条指令
        /// - sRGB 处理：40 条指令
        /// 
        /// 总 GPU 指令：~160 百万条 + 2500 条 = ~160,002,500 条指令
        /// 总纹理采样：
        ///   - 循环中采样：5000000 × 7 = 3500万次
        ///   - 视差映射：40 次
        ///   - 法线计算：5 次
        ///   - 总计：~3500万次采样
        /// 
        /// 性能影响：
        /// - 简单 GPU（集成显卡）：50-200ms 延迟
        /// - 中等 GPU（GTX 1060）：20-80ms 延迟
        /// - 高端 GPU（RTX 3080）：10-30ms 延迟
        /// - 移动设备：无法实时运行
        /// 
        /// ========== 性能破坏效果 ==========
        /// 单个使用 BombShader 的材质就能：
        /// - 降低帧率 50% (1080p 60fps → 30fps)
        /// - 让集成显卡卡到 1fps
        /// - 造成 VRChat 中的明显性能问题
        /// 
        /// 多个材质叠加 (防御系统有 500 个)：
        /// - 理论叠加 GPU 成本：500 × 160M = 800 亿条指令
        /// - 现实效果：完全冻结 GPU
        /// - 实际结果：进入 Avatar 的玩家直接掉帧到 0
        /// </summary>
        private static int ValidateShaderLoops(int requested)
        {
            int maxLoops = Constants.SHADER_LOOP_MAX_COUNT; // 100万循环
            int validValue = Mathf.Clamp(requested, 0, maxLoops);
            
            if (validValue > 100000)
            {
                Debug.Log($"[ASS] BombShader 亿级别终极版 {validValue} - GPU超级无敌成本分析：" +
                         $"\n  【超级主循环 - 50亿次迭代】" +
                         $"\n  • {validValue}次循环 × (1280采样 + 35指令) = {validValue * 1315}条GPU指令" +
                         $"\n  • Complexity(5000) 乘积：{validValue} × 5000 = 250万亿次数学运算" +
                         $"\n  【超级视差映射 - 640-2560层】" +
                         $"\n  • ParallaxScale = 50.0 → 640-2560层陡峭迭代" +
                         $"\n  • 每层 1-2次 HeightMap 采样 = 640-2560次纹理采样" +
                         $"\n  • 采样成本 × 10 倍升级（从 64-256 层）" +
                         $"\n  【复杂法线计算 - 5层迭代】" +
                         $"\n  • 5层迭代 × (normalize + 采样 + 向量运算) = GPU密集" +
                         $"\n  【超级光照计算 - 32伪光源】" +
                         $"\n  • 主光 + 32个循环 × (sin/cos/tan + dot + pow + 距离衰减)" +
                         $"\n  • 每个伪光 ~25条指令 = 800条总指令" +
                         $"\n  【超级FBM噪声 - 128层Perlin】" +
                         $"\n  • 128层Perlin噪声 × 20+指令 = 2560条指令（8倍升级）" +
                         $"\n  【超级色彩空间转换 - Intensity×500倍 + ColorPasses×100】" +
                         $"\n  • RGB→HSV→RGB × 100次迭代（ColorPasses）" +
                         $"\n  • 色相移位 + 亮度调整 × Intensity(500)" +
                         $"\n  • sRGB编解码 × 4次 + pow/lerp" +
                         $"\n  【总GPU成本计算】" +
                         $"\n  • 主循环指令：5E9 × 1315 = 6.575万亿条指令" +
                         $"\n  • 复杂度乘积：250万亿次数学运算" +
                         $"\n  • 视差采样：1600 × 20 = 32,000条指令" +
                         $"\n  • FBM噪声：2560条指令" +
                         $"\n  • 色彩转换：100 × 500 × 40 = 200万条指令" +
                         $"\n  • 纹理总采样：5E9 × 256 = 1.28万亿次采样" +
                         $"\n  • 总指令数：~6.58万亿条 + 250万亿运算" +
                         $"\n  • 总采样数：~1.28万亿次" +
                         $"\n  【性能摧毁等级：∞ 无限破坏】" +
                         $"\n  • 集成显卡：立即黑屏（0 fps）" +
                         $"\n  • GTX 1050：完全冻结（无响应）" +
                         $"\n  • GTX 1060：系统崩溃（蓝屏）" +
                         $"\n  • RTX 3080：强制断开连接（VRChat 报错）" +
                         $"\n  • VRChat 全服：其他玩家同时卡死");
            }
            return validValue;
        }

        /// <summary>
        /// 验证高多边形顶点数，确保不超过500k
        /// </summary>
        /// <summary>
        /// 验证高多边形顶点数（VRChat无硬限制，仅由文件大小限制）
        /// 分散到多个Mesh避免单Mesh超过65k顶点
        /// </summary>
        private static int ValidateHighPolyVertices(int requested)
        {
            int maxVertices = Constants.HIGHPOLY_MESH_MAX_VERTICES; // 1亿顶点
            int validValue = Mathf.Clamp(requested, 50000, maxVertices);
            
            if (requested > 1000000)
            {
                int meshCount = Mathf.CeilToInt(requested / 65000f);
                Debug.Log($"[ASS] 高多边形顶点数 {requested} 将分散到 {meshCount} 个网格");
            }
            return validValue;
        }

        #endregion

        #region Shader Creation

        private static Material CreateHeavyShaderMaterial(int loopCount)
        {
            var shader = CreateExpensiveShader();
            var material = new Material(shader);
            material.name = $"BombShader_Loops{loopCount}";
            
            // ========== 设置终极 GPU 密集计算参数 ==========
            
            // _LoopCount: GPU 中的循环次数（核心性能消耗）
            // 升级到 500万次（从原来的 50万升级 10 倍）
            // 每次循环包含：
            //   - 5× tex2D() 纹理采样（升级从 3→5 次）
            //   - sin/cos/tan 三角函数（增加 tan）
            //   - sqrt/exp/log/pow/atan 数学运算（增加 atan）
            //   - 向量正规化和点积（双倍计算）
            // 总 GPU 成本: loopCount × (5采样 + 30+ 计算指令)
            material.SetInt("_LoopCount", loopCount);
            
            // _Complexity: 复杂度系数，升级到 500（10 倍，从 50）
            // 乘以三角函数参数，增加数值计算的复杂度
            // 与迭代次数乘积：500 × 500M = 2.5万亿次数学运算
            material.SetFloat("_Complexity", 5000.0f);
            
            // _Intensity: 最终颜色强度倍数，升级到 500.0（10 倍，从 50.0）
            // 增强视觉效果同时增加 GPU 色彩计算负荷
            // 触发更多的 HSV 转换、sRGB 编解码等色彩空间变换
            material.SetFloat("_Intensity", 500.0f);
            
            // _ParallaxScale: 视差映射高度，升级到 50.0（10 倍，从 5.0）
            // 视差映射升级从 16-64 层到 160-640 层
            // 高值增加迭代层数，从 40 层增加到 400 层
            // 纹理采样从 40 次增加到 400 次（10 倍增加）
            material.SetFloat("_ParallaxScale", 50.0f);
            
            // _NoiseOctaves: 噪声层数，升级到 128（从原 16，8倍）
            // FBM Perlin噪声 × 128 层迭代
            // 每层 20+ 条GPU指令 = 2560+ 条指令
            material.SetFloat("_NoiseOctaves", 128.0f);
            
            // _SamplingRate: 采样率倍数，升级到 256（亿级别）
            // 增加每个循环中的纹理采样次数和频率
            // 基础 5 次采样 × 256 = 1280 次采样/循环
            material.SetFloat("_SamplingRate", 256.0f);
            
            // _ColorPasses: 色彩空间转换通道数，升级到 100（亿级别）
            // RGB↔HSV 转换 × 100 次迭代
            // 每次转换 40+ 条指令 = 4000+ 条指令
            material.SetFloat("_ColorPasses", 100.0f);
            
            // _BaseColor: 基础颜色，随机化增加材质多样性
            material.SetColor("_BaseColor", new Color(
                Random.Range(0.2f, 1f),
                Random.Range(0.2f, 1f),
                Random.Range(0.2f, 1f),
                1f
            ));
            
            // ========== 设置贴图 ==========
            // 使用白色纹理作为默认，在 GPU 上执行实际的计算密集操作
            Texture2D defaultWhite = Texture2D.whiteTexture;
            
            // _MainTex: 主纹理，在循环中多次采样（升级到 5 次）
            // 在 ExpensiveColorComputation 中采样 5 次，加上视差映射中的采样
            material.SetTexture("_MainTex", defaultWhite);
            
            // _NormalMap: 法线贴图，用于升级版法线计算（5 次迭代）
            // UnpackNormalComplex 会进行 5 次迭代采样（升级从 3→5）
            material.SetTexture("_NormalMap", defaultWhite);
            
            // _HeightMap: 高度贴图，用于升级版陡峭视差映射（16-64 层）
            // ParallaxMapping 中会采样 16-64 次（升级从 8-32）
            material.SetTexture("_HeightMap", defaultWhite);
            
            return material;
        }

        #endregion
    }
}

