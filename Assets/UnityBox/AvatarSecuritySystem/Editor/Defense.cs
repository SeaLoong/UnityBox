using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 防御系统生成器 - 基于性能消耗的 Avatar 防盗保护
    /// 
    /// 在倒计时结束且密码未输入正确时激活，通过消耗盗用者客户端的 CPU/GPU 资源实现防护。
    /// 
    /// CPU 防御机制：
    /// - VRCConstraint 链（深层嵌套约束链消耗 CPU 求解时间）
    /// - VRCPhysBone 链（长骨骼链 + 多碰撞器消耗物理模拟资源）
    /// - VRCContact 系统（大量 Sender/Receiver 对消耗碰撞检测资源）
    /// 
    /// GPU 防御机制：
    /// - 高循环次数 Shader 材质（自定义 DefenseShader 的密集计算）
    /// - 高面数网格（高密度球体 Mesh 增加顶点处理负担）
    /// - 粒子系统（大量粒子增加排序和渲染开销）
    /// - 实时光源（多个 Soft Shadow 光源增加阴影计算开销）
    /// 
    /// 防御等级：
    /// 0 = 不创建防御组件（仅密码系统）
    /// 1 = 仅 CPU 防御（Constraint + PhysBone + Contact）
    /// 2 = CPU + GPU 防御（含扩展链和显存纹理）
    /// </summary>
    public class Defense
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly AvatarSecuritySystemComponent config;
        private readonly bool isDebugMode;

        public Defense(AnimatorController controller, GameObject avatarRoot, AvatarSecuritySystemComponent config, bool isDebugMode = false)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
            this.isDebugMode = isDebugMode;
        }

        /// <summary>
        /// 生成防御层并添加到控制器
        /// 如果禁用防御或防御等级为0，不生成任何内容
        /// </summary>
        public void Generate()
        {
            if (config.disableDefense)
            {
                Debug.Log("[ASS] 禁用防御选项已勾选，跳过防御层创建（仅测试密码系统）");
                return;
            }

            if (config.defenseLevel <= 0)
            {
                Debug.Log("[ASS] 防御等级为0，跳过防御层创建");
                return;
            }

            float levelMultiplier = Mathf.Clamp01((config.defenseLevel - 1) / 1f);

            var layer = Utils.CreateLayer(Constants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            // 状态：Inactive（防御未激活）
            var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(100, 50, 0));
            inactiveState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            layer.stateMachine.defaultState = inactiveState;

            // 状态：Active（防御激活）— 使用激活动画启用防御根对象
            var activeState = layer.stateMachine.AddState("Active", new Vector3(100, 150, 0));
            var activateClip = new AnimationClip { name = "ASS_DefenseActivate" };
            activateClip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive",
                AnimationCurve.Constant(0f, 1f / 60f, 1f));
            Utils.AddSubAsset(controller, activateClip);
            activeState.motion = activateClip;

            // 转换条件：IsLocal && TimeUp
            var toActive = Utils.CreateTransition(inactiveState, activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);

            try
            {
                CreateDefenseComponents(levelMultiplier);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateDefenseComponents调用失败: {e.Message}\n{e.StackTrace}");
                throw;
            }

            controller.AddLayer(layer);
        }

        #region Defense Components Creation

        /// <summary>
        /// 创建防御组件根对象及所有子组件
        /// 根据防御等级自动配置：
        /// - 等级0: 不创建任何防御组件
        /// - 等级1: 仅CPU防御
        /// - 等级2: CPU+GPU防御（含扩展链和显存纹理）
        /// </summary>
        private GameObject CreateDefenseComponents(float levelMultiplier = 1f)
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
            root.SetActive(false);

            // 根据防御等级计算参数（内部已固定参数）
            var parameters = CalculateDefenseParams(levelMultiplier);

            // 根据防御等级决定启用哪些防御类型（调试模式和正常模式逻辑一致）
            bool enableCpu = config.defenseLevel >= 1;  // 等级1及以上启用CPU防御
            bool enableGpu = config.defenseLevel >= 2;  // 等级2及以上启用GPU防御

            // 检查现有的PhysBone数量，确保总数量不超过256
            int existingPhysBones = 0;
            try
            {
                existingPhysBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true).Length;
            }
            catch
            {
                existingPhysBones = 0;
            }
            int maxDefensePhysBones = Mathf.Max(0, Constants.PHYSBONE_MAX_COUNT - existingPhysBones);
            int defensePhysBoneCount = Mathf.Min(parameters.PhysBoneChainCount, maxDefensePhysBones);

            // ==================== CPU 防御 ====================
            if (enableCpu)
            {
                // --- 约束链 ---
                if (parameters.ConstraintChainCount > 0)
                {
                    for (int i = 0; i < parameters.ConstraintChainCount; i++)
                    {
                        CreateConstraintChain(root, parameters.ConstraintDepth, i);
                    }

                    // 扩展约束链（等级2时创建）
                    if (!isDebugMode && config.defenseLevel >= 2)
                    {
                        CreateExtendedConstraintChains(root, Mathf.Min(parameters.ConstraintChainCount, 5), parameters.ConstraintDepth);
                    }
                }

                // --- PhysBone链 ---
                if (parameters.PhysBoneColliders > 0 && defensePhysBoneCount > 0)
                {
                    for (int i = 0; i < defensePhysBoneCount; i++)
                    {
                        CreatePhysBoneChains(root, parameters.PhysBoneLength, parameters.PhysBoneColliders, i);
                    }

                    // 扩展PhysBone链（等级2时创建）
                    int extendedPhysBoneCount = Mathf.Min(defensePhysBoneCount, 3);
                    if (!isDebugMode && config.defenseLevel >= 2 && extendedPhysBoneCount > 0)
                    {
                        CreateExtendedPhysBoneChains(root, extendedPhysBoneCount, parameters.PhysBoneLength, parameters.PhysBoneColliders);
                    }
                }

                // --- Contact系统 ---
                if (parameters.ContactCount > 0)
                {
                    CreateContactSystem(root, parameters.ContactCount);

                    // 扩展Contact系统（等级2时创建）
                    int remainingContactBudget = Mathf.Max(0, Constants.CONTACT_MAX_COUNT - parameters.ContactCount);
                    int extendedContactCount = Mathf.Min(remainingContactBudget / 2, 50);
                    if (!isDebugMode && config.defenseLevel >= 2 && extendedContactCount > 0)
                    {
                        CreateExtendedContactSystem(root, extendedContactCount);
                    }
                }
            }

            // ==================== GPU 防御 ====================
            if (enableGpu)
            {
                // --- 材质防御 ---
                if (parameters.MaterialCount > 0)
                {
                    CreateMaterialDefense(root, parameters);
                }

                // --- 粒子防御 ---
                if (parameters.ParticleCount > 0)
                {
                    CreateParticleDefense(root, parameters.ParticleCount, parameters.ParticleSystemCount);
                }

                // --- 光源防御 ---
                if (parameters.LightCount > 0)
                {
                    CreateLightDefense(root, parameters.LightCount);
                }
            }

            return root;
        }

        /// <summary>
        /// 创建材质防御系统（使用防御 Shader + 高面数网格）
        /// </summary>
        private void CreateMaterialDefense(
            GameObject root,
            DefenseParams parameters)
        {
            var materialDefenseRoot = new GameObject("MaterialDefense");
            materialDefenseRoot.transform.SetParent(root.transform);
            materialDefenseRoot.transform.localPosition = Vector3.zero;

            // 创建多个使用防御材质的高面数网格（每个独立材质 + 独立大纹理）
            int verticesPerMesh = parameters.PolyVertices / Mathf.Max(1, parameters.MaterialCount);

            for (int i = 0; i < parameters.MaterialCount; i++)
            {
                // 每个网格创建独立材质，携带独立大纹理以消耗显存
                var defenseMaterial = CreateDefenseMaterial(i);
                if (defenseMaterial == null)
                {
                    Debug.LogWarning($"[ASS] 无法创建防御材质 #{i}，跳过");
                    continue;
                }

                var meshObj = new GameObject($"DefenseMesh_{i}");
                meshObj.transform.SetParent(materialDefenseRoot.transform);
                meshObj.transform.localPosition = new Vector3(i * 0.2f, 0, 0);
                meshObj.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

                var mesh = CreateHighDensitySphereMesh(verticesPerMesh);

                var meshFilter = meshObj.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;

                var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = defenseMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                meshRenderer.receiveShadows = true;
                meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                meshRenderer.allowOcclusionWhenDynamic = false;
            }

            Debug.Log($"[ASS] 创建材质防御: {parameters.MaterialCount} 个网格, 每个 {verticesPerMesh} 顶点");
        }

        /// <summary>
        /// 创建防御材质（使用防御 Shader）
        /// 为每个材质生成独立的大尺寸程序化纹理以消耗显存
        /// </summary>
        private static Material CreateDefenseMaterial(int seed = 0)
        {
            var shader = CreateDefenseShader();
            if (shader == null)
            {
                Debug.LogWarning("[ASS] 无法创建防御 Shader，跳过材质创建");
                return null;
            }

            var material = new Material(shader);
            material.name = $"ASS_DefenseMaterial_{seed}";
            material.renderQueue = 3000;

            // 为16个纹理槽位生成大尺寸程序化纹理，消耗显存
            string[] texProps = { "_xA0", "_xA1", "_xA2", "_xA3", "_xA4", "_xA5", "_xA6", "_xA7",
                                  "_xA8", "_xA9", "_xAA", "_xAB", "_xAC", "_xAD", "_xAE", "_xAF" };
            for (int i = 0; i < texProps.Length; i++)
            {
                var tex = GenerateLargeProceduralTexture(4096, seed * 100 + i);
                material.SetTexture(texProps[i], tex);
            }

            return material;
        }

        /// <summary>
        /// 生成大尺寸程序化纹理（RGBA32, 4096x4096 = 64MB per texture）
        /// 每个纹理使用不同的种子确保内容唯一，防止GPU去重优化
        /// </summary>
        private static Texture2D GenerateLargeProceduralTexture(int size, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = $"ASS_ProceduralTex_{seed}";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            // 使用伪随机算法填充像素，确保每个纹理内容不同
            uint state = (uint)(seed * 2654435761 + 1);
            for (int j = 0; j < pixels.Length; j++)
            {
                // xorshift32
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                pixels[j] = new Color32(
                    (byte)(state & 0xFF),
                    (byte)((state >> 8) & 0xFF),
                    (byte)((state >> 16) & 0xFF),
                    255
                );
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false); // 生成 mipmap
            return tex;
        }

        /// <summary>
        /// 防御参数结构体
        /// </summary>
        private readonly struct DefenseParams
        {
            public readonly int ConstraintDepth;
            public readonly int ConstraintChainCount;
            public readonly int PhysBoneLength;
            public readonly int PhysBoneChainCount;
            public readonly int PhysBoneColliders;
            public readonly int ContactCount;
            public readonly int PolyVertices;
            public readonly int ParticleCount;
            public readonly int ParticleSystemCount;
            public readonly int LightCount;
            public readonly int MaterialCount;

            public DefenseParams(
                int constraintDepth, int constraintChainCount,
                int physBoneLength, int physBoneChainCount, int physBoneColliders,
                int contactCount, int polyVertices,
                int particleCount, int particleSystemCount, int lightCount, int materialCount)
            {
                ConstraintDepth = constraintDepth;
                ConstraintChainCount = constraintChainCount;
                PhysBoneLength = physBoneLength;
                PhysBoneChainCount = physBoneChainCount;
                PhysBoneColliders = physBoneColliders;
                ContactCount = contactCount;
                PolyVertices = polyVertices;
                ParticleCount = particleCount;
                ParticleSystemCount = particleSystemCount;
                LightCount = lightCount;
                MaterialCount = materialCount;
            }
        }

        /// <summary>
        /// 根据防御等级计算参数
        /// 等级0: 仅密码系统（不创建防御组件）
        /// 等级1: 密码+CPU防御
        /// 等级2: 密码+CPU防御+GPU防御（含扩展链和显存纹理）
        /// 调试模式: 根据等级生成对应防御机制，但使用最小参数值
        /// </summary>
        private DefenseParams CalculateDefenseParams(float levelMultiplier)
        {
            // 调试模式：根据等级生成对应防御机制，但使用最小参数值
            if (isDebugMode)
            {
                // 等级0：不生成防御
                if (config.defenseLevel <= 0)
                {
                    Debug.Log("[ASS] 调试模式 - 等级0：不生成防御组件");
                    return new DefenseParams(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                }
                
                // 等级1：仅CPU防御（最小参数）
                if (config.defenseLevel == 1)
                {
                    Debug.Log("[ASS] 调试模式 - 等级1：CPU防御（最小参数）");
                    return new DefenseParams(
                        constraintDepth: 3,
                        constraintChainCount: 1,
                        physBoneLength: 3,
                        physBoneChainCount: 1,
                        physBoneColliders: 2,
                        contactCount: 4,
                        polyVertices: 0,
                        particleCount: 0,
                        particleSystemCount: 0,
                        lightCount: 0,
                        materialCount: 0
                    );
                }
                
                // 等级2：CPU+GPU防御（最小参数）
                Debug.Log($"[ASS] 调试模式 - 等级{config.defenseLevel}：CPU+GPU防御（最小参数）");
                return new DefenseParams(
                    constraintDepth: 3,
                    constraintChainCount: 1,
                    physBoneLength: 3,
                    physBoneChainCount: 1,
                    physBoneColliders: 2,
                    contactCount: 4,
                    polyVertices: 1000,
                    particleCount: 100,
                    particleSystemCount: 1,
                    lightCount: 1,
                    materialCount: 1
                );
            }

            // 等级0：仅密码系统，不需要防御组件
            // 这里返回全0参数，CreateDefenseComponents会被跳过
            
            // 等级1：密码+CPU防御
            if (config.defenseLevel == 1)
            {
                Debug.Log("[ASS] 防御等级1：密码+CPU防御（无GPU）");
                return new DefenseParams(
                    constraintDepth: 100,              // CPU: 约束深度
                    constraintChainCount: 10,          // CPU: 约束链数
                    physBoneLength: 256,               // CPU: PhysBone长度
                    physBoneChainCount: 10,            // CPU: PhysBone链数
                    physBoneColliders: 256,            // CPU: 碰撞器数
                    contactCount: 200,                 // CPU: Contact数
                    polyVertices: 0,                   // GPU: 关闭
                    particleCount: 0,                  // GPU: 关闭
                    particleSystemCount: 0,            // GPU: 关闭
                    lightCount: 0,                     // GPU: 关闭
                    materialCount: 0                   // GPU: 关闭
                );
            }

            // 等级2：密码+CPU防御+GPU防御
            Debug.Log("[ASS] 防御等级2：密码+CPU+GPU防御");
            return new DefenseParams(
                constraintDepth: 100,              // CPU: 约束深度
                constraintChainCount: 10,          // CPU: 约束链数
                physBoneLength: 256,               // CPU: PhysBone长度
                physBoneChainCount: 10,            // CPU: PhysBone链数
                physBoneColliders: 256,            // CPU: 碰撞器数
                contactCount: 200,                 // CPU: Contact数
                polyVertices: 200000,              // GPU: 顶点数
                particleCount: 100000,             // GPU: 粒子数
                particleSystemCount: 20,           // GPU: 粒子系统数
                lightCount: 30,                    // GPU: 光源数
                materialCount: 20                  // GPU: 材质数（每个独立大纹理）
            );
        }

        /// <summary>
        /// 创建链式Constraint结构
        /// </summary>
        private static void CreateConstraintChain(GameObject root, int depth, int chainIndex = 0)
        {
            try
            {
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
                        posC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        // 额外添加 VRCRotationConstraint 增加CPU复杂度
                        var rotC = obj.AddComponent<VRCRotationConstraint>();
                        rotC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });
                    }

                    // 使用SerializedObject设置属性
                    SetConstraintProperties(parentC, true, true);

                    if (i > 0)
                    {
                        SetConstraintProperties(obj.GetComponent<VRCPositionConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCRotationConstraint>(), true, true);
                    }

                    previous = obj;
                }

                Debug.Log($"[ASS] 创建VRC Constraint链 {chainIndex}: 深度={depth}, 每节点包含 Parent/Position/Rotation 三种约束");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] 创建Constraint链时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 设置Constraint组件属性
        /// </summary>
        private static void SetConstraintProperties<T>(T constraint, bool isActive, bool isLocked) where T : VRCConstraintBase
        {
            if (constraint == null) return;

            try
            {
                var serialized = new SerializedObject(constraint);
                var isActiveProp = serialized.FindProperty("IsActive");
                var lockedProp = serialized.FindProperty("Locked");
                if (isActiveProp != null) isActiveProp.boolValue = isActive;
                if (lockedProp != null) lockedProp.boolValue = isLocked;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ASS] 设置Constraint属性时出错: {ex.Message}");
            }
        }

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

            // 创建Collider（使用固定位置分布）
            for (int i = 0; i < colliderCount; i++)
            {
                var colliderObj = new GameObject($"Collider_{i}");
                colliderObj.transform.SetParent(physBoneRoot.transform);
                // 使用固定的圆形分布
                float angle = (float)i / colliderCount * Mathf.PI * 2f;
                colliderObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle),
                    (float)i / colliderCount * 2f,
                    Mathf.Sin(angle)
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

            // 创建Sender（使用固定位置分布）
            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"Sender_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
                // 使用固定的圆形分布
                float angle = (float)i / halfCount * Mathf.PI * 2f;
                senderObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle),
                    (float)i / halfCount * 2f,
                    Mathf.Sin(angle)
                );

                var sender = senderObj.AddComponent<VRCContactSender>();
                sender.shapeType = VRCContactSender.ShapeType.Capsule;
                sender.radius = 1.0f;
                sender.height = 2f;
                sender.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
                sender.localOnly = true;
            }

            // 创建Receiver（使用固定位置分布）
            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"Receiver_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
                // 使用固定的圆形分布（偏移半个角度）
                float angle = ((float)i / halfCount + 0.5f / halfCount) * Mathf.PI * 2f;
                receiverObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * 0.8f,
                    (float)i / halfCount * 2f + 0.1f,
                    Mathf.Sin(angle) * 0.8f
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
                    var particleShader = Shader.Find("Particles/Standard Unlit");
                    if (particleShader == null)
                    {
                        particleShader = Shader.Find("Standard");
                    }
                    if (particleShader != null)
                    {
                        var mat = new Material(particleShader);
                        // 使用基于索引的固定颜色
                        float hue = (float)s / systemCount;
                        mat.color = Color.HSVToRGB(hue, 0.7f, 0.9f);
                        renderer.sharedMaterial = mat;
                    }
                    renderer.maxParticleSize = 2f;
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    renderer.allowOcclusionWhenDynamic = false;
                }
            }

            Debug.Log($"[ASS] 创建粒子防御: {totalParticleCount}粒子 ({systemCount}个系统，每个{particlesPerSystem}粒子)");
        }

        /// <summary>
        /// 创建额外CPU密集Constraint链（扩展功能）
        /// 每条链使用更多的约束类型增加CPU消耗
        /// </summary>
        private static void CreateExtendedConstraintChains(GameObject root, int chainCount, int depth)
        {
            for (int c = 0; c < chainCount; c++)
            {
                var chainRoot = new GameObject($"ExtendedConstraintChain_{c}");
                chainRoot.transform.SetParent(root.transform);
                chainRoot.transform.localPosition = new Vector3(c * 0.5f, 0, 0);

                GameObject previous = chainRoot;
                for (int i = 0; i < depth; i++)
                {
                    var obj = new GameObject($"Constraint_{i}");
                    obj.transform.SetParent(chainRoot.transform);
                    obj.transform.localPosition = new Vector3(0, i * 0.01f, 0);

                    // VRCParentConstraint
                    var parentC = obj.AddComponent<VRCParentConstraint>();

                    if (i > 0)
                    {
                        parentC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform,
                            ParentPositionOffset = Vector3.zero,
                            ParentRotationOffset = Vector3.zero
                        });

                        // VRCPositionConstraint
                        var posC = obj.AddComponent<VRCPositionConstraint>();
                        posC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        // VRCRotationConstraint
                        var rotC = obj.AddComponent<VRCRotationConstraint>();
                        rotC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        // ScaleConstraint（额外增加CPU消耗）
                        var scaleC = obj.AddComponent<VRCScaleConstraint>();
                        scaleC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });
                    }

                    // 使用SerializedObject设置属性
                    SetConstraintProperties(parentC, true, true);

                    if (i > 0)
                    {
                        SetConstraintProperties(obj.GetComponent<VRCPositionConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCRotationConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCScaleConstraint>(), true, true);
                    }

                    previous = obj;
                }

                Debug.Log($"[ASS] 创建扩展Constraint链 {c}: 深度={depth}, 4种约束类型");
            }
        }

        /// <summary>
        /// 创建PhysBone长链及Collider（扩展功能）
        /// 使用更复杂的物理配置增加CPU消耗
        /// </summary>
        private static void CreateExtendedPhysBoneChains(GameObject root, int chainCount, int chainLength, int colliderCount)
        {
            for (int c = 0; c < chainCount; c++)
            {
                var physBoneRoot = new GameObject($"ExtendedPhysBoneChains_{c}");
                physBoneRoot.transform.SetParent(root.transform);
                physBoneRoot.transform.localPosition = new Vector3(c * 0.5f, 1f, 0);

                // 创建骨骼链
                var chainRoot = new GameObject($"BoneChain_{c}");
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

                // 限制
                physBone.limitType = VRCPhysBone.LimitType.Angle;
                physBone.maxAngleX = 45f;
                physBone.maxAngleZ = 45f;
                physBone.limitRotation = new Vector3(15, 30, 15);

                // 拉伸
                physBone.maxStretch = 0.5f;

                // 抓取相关
                physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
                physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
                physBone.grabMovement = 0.8f;
                physBone.snapToHand = true;

                // 参数驱动
                physBone.parameter = "";
                physBone.isAnimated = false;

                var collidersList = new List<VRCPhysBoneCollider>();

                // 创建Collider（使用固定位置分布）
                for (int i = 0; i < colliderCount; i++)
                {
                    var colliderObj = new GameObject($"Collider_{i}");
                    colliderObj.transform.SetParent(physBoneRoot.transform);
                    // 使用固定的圆形分布
                    float angle = (float)i / colliderCount * Mathf.PI * 2f;
                    colliderObj.transform.localPosition = new Vector3(
                        Mathf.Cos(angle),
                        (float)i / colliderCount * 2f,
                        Mathf.Sin(angle)
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

                Debug.Log($"[ASS] 创建扩展PhysBone链 {c} (Advanced模式): 长度={chainLength}, Collider={colliderCount}");
            }
        }

        /// <summary>
        /// 创建扩展Contact Sender/Receiver系统（扩展功能）
        /// 使用更多标签和更复杂的碰撞配置
        /// </summary>
        private static void CreateExtendedContactSystem(GameObject root, int componentCount)
        {
            var contactRoot = new GameObject("ExtendedContactSystem");
            contactRoot.transform.SetParent(root.transform);

            int halfCount = componentCount / 2;

            // 扩展标签列表
            var tags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5", "Tag6", "Tag7", "Tag8", "Tag9", "Tag10" };

            // 创建Sender（使用固定位置分布）
            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"ExtendedSender_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
                // 使用固定的圆形分布
                float angle = (float)i / halfCount * Mathf.PI * 2f;
                senderObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle),
                    (float)i / halfCount * 2f,
                    Mathf.Sin(angle)
                );

                var sender = senderObj.AddComponent<VRCContactSender>();
                sender.shapeType = VRCContactSender.ShapeType.Capsule;
                sender.radius = 1.0f;
                sender.height = 2f;
                sender.collisionTags = tags;
                sender.localOnly = true;
            }

            // 创建Receiver（使用固定位置分布）
            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"ExtendedReceiver_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
                // 使用固定的圆形分布（偏移半个角度）
                float angle = ((float)i / halfCount + 0.5f / halfCount) * Mathf.PI * 2f;
                receiverObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * 0.8f,
                    (float)i / halfCount * 2f + 0.1f,
                    Mathf.Sin(angle) * 0.8f
                );

                var receiver = receiverObj.AddComponent<VRCContactReceiver>();
                receiver.shapeType = VRCContactReceiver.ShapeType.Capsule;
                receiver.radius = 1.0f;
                receiver.height = 2f;
                receiver.collisionTags = tags;
                receiver.localOnly = true;
            }

            Debug.Log($"[ASS] 创建扩展Contact系统: {halfCount} senders + {halfCount} receivers, 10个标签");
        }

        /// <summary>
        /// 在构建时获取防御Shader
        /// 优先使用自定义的 UnityBox/ASS_DefenseShader，不存在则回退到 Standard
        /// </summary>
        private static Shader CreateDefenseShader()
        {
            Shader defenseShader = Shader.Find("UnityBox/ASS_DefenseShader");

            if (defenseShader != null)
            {
                Debug.Log("[ASS] 使用自定义DefenseShader");
                return defenseShader;
            }

            Shader standardShader = Shader.Find("Standard");

            if (standardShader == null)
            {
                Debug.LogError("[ASS] 无法找到Standard Shader");
            }
            else
            {
                Debug.Log("[ASS] 使用Standard Shader（自定义Shader未找到）");
            }
            return standardShader;
        }

        /// <summary>
        /// 创建光源防御（GPU消耗，实时阴影）
        /// 在防御根节点下生成多个高质量 Point/Spot 光源，均开启 Soft Shadow
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
                // 使用基于索引的固定颜色
                float hue = (float)i / lightCount;
                light.color = Color.HSVToRGB(hue, 0.5f, 1f);

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

        #region Mesh Generation Utilities

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
            mesh.SetTriangles(triangles, 0);

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
    }
}

