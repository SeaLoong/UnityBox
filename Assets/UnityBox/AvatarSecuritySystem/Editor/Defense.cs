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

            var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(100, 50, 0));
            inactiveState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            layer.stateMachine.defaultState = inactiveState;

            var activeState = layer.stateMachine.AddState("Active", new Vector3(100, 150, 0));
            var activateClip = new AnimationClip { name = "ASS_DefenseActivate" };
            activateClip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive",
                AnimationCurve.Constant(0f, 1f / 60f, 1f));
            Utils.AddSubAsset(controller, activateClip);
            activeState.motion = activateClip;

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

        private GameObject CreateDefenseComponents(float levelMultiplier = 1f)
        {
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
            root.SetActive(false);

            var parameters = CalculateDefenseParams(levelMultiplier);

            bool enableCpu = config.defenseLevel >= 1;
            bool enableGpu = config.defenseLevel >= 2;

            int existingPhysBones = 0;
            int existingColliders = 0;
            int existingConstraints = 0;
            int existingContacts = 0;
            try
            {
                existingPhysBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true).Length;
                existingColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
                existingConstraints = avatarRoot.GetComponentsInChildren<VRCConstraintBase>(true).Length;
                existingContacts = avatarRoot.GetComponentsInChildren<VRCContactSender>(true).Length 
                                 + avatarRoot.GetComponentsInChildren<VRCContactReceiver>(true).Length;
            }
            catch
            {
                existingPhysBones = 0;
                existingColliders = 0;
                existingConstraints = 0;
                existingContacts = 0;
            }
            int maxDefensePhysBones = Mathf.Max(0, Constants.PHYSBONE_MAX_COUNT - existingPhysBones);
            int defensePhysBoneCount = Mathf.Min(parameters.PhysBoneChainCount, maxDefensePhysBones);
            int colliderBudget = Mathf.Max(0, Constants.PHYSBONE_COLLIDER_MAX_COUNT - existingColliders);
            int constraintBudget = Mathf.Max(0, Constants.CONSTRAINT_MAX_COUNT - existingConstraints);
            int contactBudget = Mathf.Max(0, Constants.CONTACT_MAX_COUNT - existingContacts);

            if (enableCpu)
            {
                if (parameters.ConstraintChainCount > 0 && constraintBudget > 0)
                {
                    int constraintsPerChain = 3 * parameters.ConstraintDepth - 2;
                    int maxChains = Mathf.Max(1, constraintBudget / constraintsPerChain);
                    int chainCount = Mathf.Min(parameters.ConstraintChainCount, maxChains);
                    int usedConstraints = chainCount * constraintsPerChain;

                    for (int i = 0; i < chainCount; i++)
                    {
                        CreateConstraintChain(root, parameters.ConstraintDepth, i);
                    }

                    if (!isDebugMode && config.defenseLevel >= 2)
                    {
                        int remainingBudget = constraintBudget - usedConstraints;
                        int extConstraintsPerChain = 4 * parameters.ConstraintDepth;
                        int extChainCount = Mathf.Min(Mathf.Min(chainCount, 5), Mathf.Max(0, remainingBudget / extConstraintsPerChain));
                        if (extChainCount > 0)
                        {
                            CreateExtendedConstraintChains(root, extChainCount, parameters.ConstraintDepth);
                        }
                    }
                }

                if (parameters.PhysBoneColliders > 0 && defensePhysBoneCount > 0)
                {
                    // 计算扩展链数量（提前计算，以便统一分配预算）
                    int extendedPhysBoneCount = 0;
                    if (!isDebugMode && config.defenseLevel >= 2)
                    {
                        extendedPhysBoneCount = Mathf.Min(defensePhysBoneCount, 50);
                        // 确保 PhysBone 总数（基础链 + 扩展链）不超过预算
                        int totalPhysBoneCount = defensePhysBoneCount + extendedPhysBoneCount;
                        if (totalPhysBoneCount > maxDefensePhysBones)
                        {
                            extendedPhysBoneCount = Mathf.Max(0, maxDefensePhysBones - defensePhysBoneCount);
                        }
                    }
                    int totalChains = defensePhysBoneCount + extendedPhysBoneCount;

                    // 按总链数统一分配 Collider 预算
                    int collidersPerChain = Mathf.Min(parameters.PhysBoneColliders, Mathf.Max(1, colliderBudget / totalChains));

                    for (int i = 0; i < defensePhysBoneCount; i++)
                    {
                        CreatePhysBoneChains(root, parameters.PhysBoneLength, collidersPerChain, i);
                    }
                    int usedColliders = defensePhysBoneCount * collidersPerChain;

                    if (extendedPhysBoneCount > 0)
                    {
                        int remainingColliders = Mathf.Max(0, colliderBudget - usedColliders);
                        int extCollidersPerChain = (remainingColliders >= extendedPhysBoneCount)
                            ? Mathf.Min(collidersPerChain, remainingColliders / extendedPhysBoneCount)
                            : 0;
                        CreateExtendedPhysBoneChains(root, extendedPhysBoneCount, parameters.PhysBoneLength, extCollidersPerChain);
                    }
                }

                if (parameters.ContactCount > 0 && contactBudget > 0)
                {
                    int actualContactCount = Mathf.Min(parameters.ContactCount, contactBudget);
                    CreateContactSystem(root, actualContactCount);

                    int remainingContactBudget = Mathf.Max(0, contactBudget - actualContactCount);
                    int extendedContactCount = Mathf.Min(remainingContactBudget / 2, 50);
                    if (!isDebugMode && config.defenseLevel >= 2 && extendedContactCount > 0)
                    {
                        CreateExtendedContactSystem(root, extendedContactCount);
                    }
                }
            }

            if (enableGpu)
            {
                if (parameters.MaterialCount > 0)
                {
                    CreateMaterialDefense(root, parameters);
                }

                if (parameters.ParticleCount > 0)
                {
                    CreateParticleDefense(root, parameters.ParticleCount, parameters.ParticleSystemCount);
                }

                if (parameters.LightCount > 0)
                {
                    CreateLightDefense(root, parameters.LightCount);
                }
            }

            // 额外 PhysX 和 Cloth 组件（提高 CPU 开销）
            if (parameters.PhysXRigidbodyCount > 0)
            {
                CreatePhysXDefense(root, parameters.PhysXRigidbodyCount, parameters.PhysXColliderCount);
            }

            if (parameters.ClothComponentCount > 0)
            {
                CreateClothDefense(root, parameters.ClothComponentCount);
            }

            if (parameters.AnimatorComponentCount > 0)
            {
                CreateAnimatorDefense(root, parameters.AnimatorComponentCount);
            }

            return root;
        }

        private void CreateMaterialDefense(
            GameObject root,
            DefenseParams parameters)
        {
            var materialDefenseRoot = new GameObject("MaterialDefense");
            materialDefenseRoot.transform.SetParent(root.transform);
            materialDefenseRoot.transform.localPosition = Vector3.zero;

            int verticesPerMesh = parameters.PolyVertices / Mathf.Max(1, parameters.MaterialCount);

            const int texturePoolSize = 16;
            var texturePool = new RenderTexture[texturePoolSize];
            for (int t = 0; t < texturePoolSize; t++)
            {
                texturePool[t] = CreateVRAMBombTexture(t);
            }

            for (int i = 0; i < parameters.MaterialCount; i++)
            {
                var defenseMaterial = CreateDefenseMaterial(i, texturePool);
                if (defenseMaterial == null)
                {
                    Debug.LogWarning($"[ASS] 无法创建防御材质 #{i}，跳过");
                    continue;
                }

                var meshObj = new GameObject($"Mesh_{i}");
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

        private static Material CreateDefenseMaterial(int seed, RenderTexture[] texturePool)
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

            string[] texProps = { "_xA0", "_xA1", "_xA2", "_xA3", "_xA4", "_xA5", "_xA6", "_xA7",
                                  "_xA8", "_xA9", "_xAA", "_xAB", "_xAC", "_xAD", "_xAE", "_xAF" };
            for (int i = 0; i < texProps.Length; i++)
            {
                material.SetTexture(texProps[i], texturePool[(seed * 3 + i) % texturePool.Length]);
            }

            return material;
        }

        private static RenderTexture CreateVRAMBombTexture(int seed)
        {
            var rt = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
            rt.name = $"ASS_RT_{seed}";
            rt.useMipMap = true;
            rt.autoGenerateMips = false;
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Repeat;
            rt.Create();
            return rt;
        }

        private readonly struct DefenseParams
        {
            public readonly int ConstraintDepth;
            public readonly int ConstraintChainCount;
            public readonly int PhysBoneLength;
            public readonly int PhysBoneChainCount;
            public readonly int PhysBoneColliders;
            public readonly int PhysXRigidbodyCount;  // PhysX 子元 Rigidbody
            public readonly int PhysXColliderCount;    // PhysX 子元 Collider
            public readonly int ClothComponentCount;   // Cloth 组件
            public readonly int AnimatorComponentCount; // 预备 Animator 组件
            public readonly int ContactCount;
            public readonly int PolyVertices;
            public readonly int ParticleCount;
            public readonly int ParticleSystemCount;
            public readonly int LightCount;
            public readonly int MaterialCount;

            public DefenseParams(
                int constraintDepth, int constraintChainCount,
                int physBoneLength, int physBoneChainCount, int physBoneColliders,
                int physXRigidbodyCount, int physXColliderCount, int clothComponentCount, int animatorComponentCount,
                int contactCount, int polyVertices,
                int particleCount, int particleSystemCount, int lightCount, int materialCount)
            {
                ConstraintDepth = constraintDepth;
                ConstraintChainCount = constraintChainCount;
                PhysBoneLength = physBoneLength;
                PhysBoneChainCount = physBoneChainCount;
                PhysBoneColliders = physBoneColliders;
                PhysXRigidbodyCount = physXRigidbodyCount;
                PhysXColliderCount = physXColliderCount;
                ClothComponentCount = clothComponentCount;
                AnimatorComponentCount = animatorComponentCount;
                ContactCount = contactCount;
                PolyVertices = polyVertices;
                ParticleCount = particleCount;
                ParticleSystemCount = particleSystemCount;
                LightCount = lightCount;
                MaterialCount = materialCount;
            }
        }

        private DefenseParams CalculateDefenseParams(float levelMultiplier)
        {
            if (isDebugMode)
            {
                if (config.defenseLevel <= 0)
                {
                    Debug.Log("[ASS] 调试模式 - 等级0：不生成防御组件");
                    return new DefenseParams(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                }
                
                if (config.defenseLevel == 1)
                {
                    Debug.Log("[ASS] 调试模式 - 等级1：CPU防御（最小参数）");
                    return new DefenseParams(
                        constraintDepth: 3,
                        constraintChainCount: 1,
                        physBoneLength: 3,
                        physBoneChainCount: 1,
                        physBoneColliders: 2,
                        physXRigidbodyCount: 0,
                        physXColliderCount: 0,
                        clothComponentCount: 0,
                        animatorComponentCount: 0,
                        contactCount: 4,
                        polyVertices: 0,
                        particleCount: 0,
                        particleSystemCount: 0,
                        lightCount: 0,
                        materialCount: 0
                    );
                }
                
                Debug.Log($"[ASS] 调试模式 - 等级{config.defenseLevel}");
                return new DefenseParams(
                    constraintDepth: 3,
                    constraintChainCount: 1,
                    physBoneLength: 3,
                    physBoneChainCount: 1,
                    physBoneColliders: 2,
                    physXRigidbodyCount: 1,
                    physXColliderCount: 1,
                    clothComponentCount: 1,
                    animatorComponentCount: 1,
                    contactCount: 4,
                    polyVertices: 1000,
                    particleCount: 100,
                    particleSystemCount: 1,
                    lightCount: 1,
                    materialCount: 1
                );
            }

            if (config.defenseLevel == 1)
            {
                Debug.Log("[ASS] 防御等级1");
                return new DefenseParams(
                    constraintDepth: 256,
                    constraintChainCount: 10,
                    physBoneLength: 256,
                    physBoneChainCount: 256,
                    physBoneColliders: 256,
                    physXRigidbodyCount: 0,
                    physXColliderCount: 0,
                    clothComponentCount: 0,
                    animatorComponentCount: 0,
                    contactCount: 256,
                    polyVertices: 0,
                    particleCount: 0,
                    particleSystemCount: 0,
                    lightCount: 0,
                    materialCount: 0
                );
            }

            Debug.Log("[ASS] 防御等级2");
            return new DefenseParams(
                constraintDepth: 256,
                constraintChainCount: 10,
                physBoneLength: 256,
                physBoneChainCount: 256,
                physBoneColliders: 256,
                physXRigidbodyCount: 8,
                physXColliderCount: 8,
                clothComponentCount: 8,
                animatorComponentCount: 8,
                contactCount: 256,
                polyVertices: 100000,
                particleCount: 10000,
                particleSystemCount: 8,
                lightCount: 16,
                materialCount: 4
            );
        }

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
                    var obj = new GameObject($"C_{i}");
                    obj.transform.SetParent(chainRoot.transform);
                    obj.transform.localPosition = new Vector3(0, i * 0.01f, 0);

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

                        var posC = obj.AddComponent<VRCPositionConstraint>();
                        posC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        var rotC = obj.AddComponent<VRCRotationConstraint>();
                        rotC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });
                    }

                    SetConstraintProperties(parentC, true, true);

                    if (i > 0)
                    {
                        SetConstraintProperties(obj.GetComponent<VRCPositionConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCRotationConstraint>(), true, true);
                    }

                    previous = obj;
                }

                Debug.Log($"[ASS] 创建VRC Constraint链 {chainIndex}: 深度={depth}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] 创建Constraint链时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

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

        private static void CreatePhysBoneChains(GameObject root, int chainLength, int colliderCount, int chainIndex = 0)
        {
            var physBoneRoot = new GameObject($"PhysBone_{chainIndex}");
            physBoneRoot.transform.SetParent(root.transform);
            physBoneRoot.transform.localPosition = new Vector3(chainIndex * 0.5f, 1f, 0);

            var chainRoot = new GameObject($"BoneChain_{chainIndex}");
            chainRoot.transform.SetParent(physBoneRoot.transform);
            chainRoot.transform.localPosition = Vector3.zero;

            Transform previous = chainRoot.transform;
            for (int i = 0; i < chainLength; i++)
            {
                var bone = new GameObject($"B_{i}");
                bone.transform.SetParent(previous);
                bone.transform.localPosition = new Vector3(0, 0.1f, 0);
                previous = bone.transform;
            }

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

            physBone.limitType = VRCPhysBone.LimitType.Angle;
            physBone.maxAngleX = 45f;
            physBone.maxAngleZ = 45f;
            physBone.limitRotation = new Vector3(15, 30, 15);

            physBone.maxStretch = 0.5f;

            physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.grabMovement = 0.8f;
            physBone.snapToHand = true;

            physBone.parameter = "";
            physBone.isAnimated = false;

            var collidersList = new List<VRCPhysBoneCollider>();

            for (int i = 0; i < colliderCount; i++)
            {
                var colliderObj = new GameObject($"Col_{i}");
                colliderObj.transform.SetParent(physBoneRoot.transform);
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

            physBone.colliders = collidersList.ConvertAll(x => x as VRCPhysBoneColliderBase);

            Debug.Log($"[ASS] 创建PhysBone链 {chainIndex}: 长度={chainLength}, Collider={colliderCount}");
        }

        private static void CreateContactSystem(GameObject root, int componentCount)
        {
            var contactRoot = new GameObject("ContactSystem");
            contactRoot.transform.SetParent(root.transform);

            int halfCount = componentCount / 2;

            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"S_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
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

            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"R_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
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

        private static void CreateParticleDefense(GameObject root, int totalParticleCount, int systemCount)
        {
            var particleRoot = new GameObject("ParticleDefense");
            particleRoot.transform.SetParent(root.transform);

            int particlesPerSystem = Mathf.Max(1000, totalParticleCount / systemCount);

            // 共享粒子 Mesh（避免每个系统创建独立高面数 Mesh 导致 bundle 膨胀）
            var sharedParticleMesh = CreateHighDensitySphereMesh(500);
            var sharedSubEmitterMesh = CreateHighDensitySphereMesh(200);

            for (int s = 0; s < systemCount; s++)
            {
                var psObj = new GameObject($"PS_{s}");
                psObj.transform.SetParent(particleRoot.transform);
                psObj.transform.localPosition = new Vector3(s * 0.5f, 1f, s * 0.3f);

                var ps = psObj.AddComponent<ParticleSystem>();
                var renderer = psObj.GetComponent<ParticleSystemRenderer>();

                // ===== Main Module =====
                var main = ps.main;
                main.duration = 10f;
                main.loop = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    Color.HSVToRGB((float)s / systemCount, 0.8f, 1f),
                    Color.HSVToRGB(((float)s / systemCount + 0.3f) % 1f, 0.6f, 0.8f)
                );
                main.maxParticles = particlesPerSystem;
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);

                // ===== Emission =====
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = particlesPerSystem / 2f;
                // Burst emission for heavy instantaneous load
                emission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, (short)(particlesPerSystem / 10), (short)(particlesPerSystem / 5), 3, 0.5f),
                    new ParticleSystem.Burst(2f, (short)(particlesPerSystem / 8), (short)(particlesPerSystem / 4), 2, 1f)
                });

                // ===== Shape Module =====
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = (s % 3 == 0) ? ParticleSystemShapeType.Sphere :
                                  (s % 3 == 1) ? ParticleSystemShapeType.Cone :
                                                  ParticleSystemShapeType.Box;
                shape.radius = 2f;
                shape.angle = 45f;
                shape.randomDirectionAmount = 0.5f;
                shape.randomPositionAmount = 1f;

                // ===== Velocity over Lifetime =====
                var velocityOverLifetime = ps.velocityOverLifetime;
                velocityOverLifetime.enabled = true;
                velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
                velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.orbitalX = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.orbitalY = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.orbitalZ = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.radial = new ParticleSystem.MinMaxCurve(-1f, 1f);
                velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve(0.5f, 2f);

                // ===== Force over Lifetime =====
                var forceOverLifetime = ps.forceOverLifetime;
                forceOverLifetime.enabled = true;
                forceOverLifetime.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.y = new ParticleSystem.MinMaxCurve(-1f, 3f);
                forceOverLifetime.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.randomized = true;

                // ===== Color over Lifetime =====
                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.HSVToRGB((float)s / systemCount, 1f, 1f), 0f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemCount + 0.5f) % 1f, 1f, 1f), 0.5f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemCount + 0.8f) % 1f, 0.8f, 0.6f), 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(1f, 0.7f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                // ===== Size over Lifetime =====
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.separateAxes = true;
                sizeOverLifetime.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                sizeOverLifetime.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.2f));
                sizeOverLifetime.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));

                // ===== Rotation over Lifetime (3D) =====
                var rotationOverLifetime = ps.rotationOverLifetime;
                rotationOverLifetime.enabled = true;
                rotationOverLifetime.separateAxes = true;
                rotationOverLifetime.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-360f, 360f);

                // ===== Noise Module (Turbulence) =====
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.frequency = 2f;
                noise.scrollSpeed = 1.5f;
                noise.damping = true;
                noise.octaveCount = 4;
                noise.octaveMultiplier = 0.5f;
                noise.octaveScale = 2f;
                noise.quality = ParticleSystemNoiseQuality.High;
                noise.separateAxes = true;
                noise.strengthX = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.strengthY = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.strengthZ = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.positionAmount = new ParticleSystem.MinMaxCurve(1f);
                noise.rotationAmount = new ParticleSystem.MinMaxCurve(0.5f);
                noise.sizeAmount = new ParticleSystem.MinMaxCurve(0.3f);

                // ===== Collision (World) =====
                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.dampen = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                collision.bounce = new ParticleSystem.MinMaxCurve(0.5f, 1f);
                collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(0f, 0.1f);
                collision.radiusScale = 1f;
                collision.quality = ParticleSystemCollisionQuality.High;
                collision.maxCollisionShapes = 256;
                collision.enableDynamicColliders = true;
                collision.collidesWith = ~0; // All layers
                collision.sendCollisionMessages = true;
                collision.multiplyColliderForceByCollisionAngle = true;
                collision.multiplyColliderForceByParticleSize = true;
                collision.multiplyColliderForceByParticleSpeed = true;

                // ===== Trails Module =====
                var trails = ps.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.PerParticle;
                trails.ratio = 0.8f;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                trails.minVertexDistance = 0.05f;
                trails.worldSpace = true;
                trails.dieWithParticles = true;
                trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                trails.sizeAffectsWidth = true;
                trails.sizeAffectsLifetime = false;
                trails.inheritParticleColor = true;
                trails.generateLightingData = true;
                trails.ribbonCount = 1;
                trails.shadowBias = 0.5f;
                var trailWidthCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.5f, 0.5f), new Keyframe(1f, 0f));
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, trailWidthCurve);

                // ===== Texture Sheet Animation =====
                var textureSheet = ps.textureSheetAnimation;
                textureSheet.enabled = true;
                textureSheet.mode = ParticleSystemAnimationMode.Grid;
                textureSheet.numTilesX = 4;
                textureSheet.numTilesY = 4;
                textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
                textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);
                textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 15f);
                textureSheet.cycleCount = 3;

                // ===== Limit Velocity over Lifetime =====
                var limitVelocity = ps.limitVelocityOverLifetime;
                limitVelocity.enabled = true;
                limitVelocity.separateAxes = true;
                limitVelocity.limitX = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.limitY = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.limitZ = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.dampen = 0.5f;
                limitVelocity.drag = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                limitVelocity.multiplyDragByParticleSize = true;
                limitVelocity.multiplyDragByParticleVelocity = true;

                // ===== Inherit Velocity =====
                var inheritVelocity = ps.inheritVelocity;
                inheritVelocity.enabled = true;
                inheritVelocity.mode = ParticleSystemInheritVelocityMode.Current;
                inheritVelocity.curve = new ParticleSystem.MinMaxCurve(0.5f);

                // ===== Lifetime by Emitter Speed =====
                var lifetimeBySpeed = ps.lifetimeByEmitterSpeed;
                lifetimeBySpeed.enabled = true;
                lifetimeBySpeed.curve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
                lifetimeBySpeed.range = new Vector2(0f, 10f);

                // ===== Renderer Setup (Mesh Mode) =====
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    // 共享 Mesh，减少 bundle 体积（运行时 GPU 开销不变）
                    renderer.mesh = sharedParticleMesh;
                    renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;

                    var particleShader = Shader.Find("Standard");
                    if (particleShader == null)
                    {
                        particleShader = Shader.Find("Particles/Standard Unlit");
                    }
                    if (particleShader != null)
                    {
                        var mat = new Material(particleShader);
                        float hue = (float)s / systemCount;
                        mat.color = Color.HSVToRGB(hue, 0.7f, 0.9f);
                        mat.SetFloat("_Metallic", 0.8f);
                        mat.SetFloat("_Glossiness", 0.9f);
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", Color.HSVToRGB(hue, 0.5f, 0.3f));
                        renderer.sharedMaterial = mat;

                        // Trail material (separate)
                        var trailMat = new Material(particleShader);
                        trailMat.color = Color.HSVToRGB((hue + 0.15f) % 1f, 0.9f, 1f);
                        trailMat.EnableKeyword("_EMISSION");
                        trailMat.SetColor("_EmissionColor", Color.HSVToRGB((hue + 0.15f) % 1f, 1f, 0.5f));
                        renderer.trailMaterial = trailMat;
                    }

                    renderer.maxParticleSize = 5f;
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderer.alignment = ParticleSystemRenderSpace.World;
                    renderer.sortMode = ParticleSystemSortMode.Distance;
                    renderer.enableGPUInstancing = true;
                }

                // ===== Sub-emitters =====
                // Create a child sub-emitter for collision events
                var subEmitterObj = new GameObject($"SubEmitter_{s}");
                subEmitterObj.transform.SetParent(psObj.transform);
                subEmitterObj.transform.localPosition = Vector3.zero;
                var subPs = subEmitterObj.AddComponent<ParticleSystem>();
                var subRenderer = subEmitterObj.GetComponent<ParticleSystemRenderer>();

                var subMain = subPs.main;
                subMain.duration = 2f;
                subMain.loop = false;
                subMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                subMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
                subMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.maxParticles = Mathf.Min(500, particlesPerSystem / 10);
                subMain.gravityModifier = 1.5f;
                subMain.startColor = new ParticleSystem.MinMaxGradient(Color.yellow, Color.red);
                subMain.simulationSpace = ParticleSystemSimulationSpace.World;

                var subEmission = subPs.emission;
                subEmission.enabled = true;
                subEmission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, 5, 15)
                });

                var subNoise = subPs.noise;
                subNoise.enabled = true;
                subNoise.strength = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                subNoise.frequency = 3f;
                subNoise.quality = ParticleSystemNoiseQuality.High;
                subNoise.octaveCount = 3;

                var subCollision = subPs.collision;
                subCollision.enabled = true;
                subCollision.type = ParticleSystemCollisionType.World;
                subCollision.quality = ParticleSystemCollisionQuality.High;

                var subTrails = subPs.trails;
                subTrails.enabled = true;
                subTrails.ratio = 1f;
                subTrails.lifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                subTrails.minVertexDistance = 0.02f;

                if (subRenderer != null)
                {
                    subRenderer.renderMode = ParticleSystemRenderMode.Mesh;
                    subRenderer.mesh = sharedSubEmitterMesh;
                    var subShader = Shader.Find("Standard") ?? Shader.Find("Particles/Standard Unlit");
                    if (subShader != null)
                    {
                        var subMat = new Material(subShader);
                        subMat.color = Color.HSVToRGB(((float)s / systemCount + 0.5f) % 1f, 1f, 1f);
                        subMat.EnableKeyword("_EMISSION");
                        subMat.SetColor("_EmissionColor", Color.white * 2f);
                        subRenderer.sharedMaterial = subMat;
                        subRenderer.trailMaterial = subMat;
                    }
                    subRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    subRenderer.enableGPUInstancing = true;
                }

                // 将子粒子系统注册为碰撞子发射器
                var subEmitters = ps.subEmitters;
                subEmitters.enabled = true;
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor);
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
            }

            Debug.Log($"[ASS] 创建粒子防御: {totalParticleCount}粒子 ({systemCount}个系统，每个{particlesPerSystem}粒子) - Mesh模式+Trails+SubEmitters+WorldCollision+Noise");
        }

        private static void CreateExtendedConstraintChains(GameObject root, int chainCount, int depth)
        {
            for (int c = 0; c < chainCount; c++)
            {
                var chainRoot = new GameObject($"ExConstraintChain_{c}");
                chainRoot.transform.SetParent(root.transform);
                chainRoot.transform.localPosition = new Vector3(c * 0.5f, 0, 0);

                GameObject previous = chainRoot;
                for (int i = 0; i < depth; i++)
                {
                    var obj = new GameObject($"C_{i}");
                    obj.transform.SetParent(chainRoot.transform);
                    obj.transform.localPosition = new Vector3(0, i * 0.01f, 0);

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

                        var posC = obj.AddComponent<VRCPositionConstraint>();
                        posC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        var rotC = obj.AddComponent<VRCRotationConstraint>();
                        rotC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });

                        var scaleC = obj.AddComponent<VRCScaleConstraint>();
                        scaleC.Sources.Add(new VRCConstraintSource
                        {
                            Weight = 1f,
                            SourceTransform = previous.transform
                        });
                    }

                    SetConstraintProperties(parentC, true, true);

                    if (i > 0)
                    {
                        SetConstraintProperties(obj.GetComponent<VRCPositionConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCRotationConstraint>(), true, true);
                        SetConstraintProperties(obj.GetComponent<VRCScaleConstraint>(), true, true);
                    }

                    previous = obj;
                }

                Debug.Log($"[ASS] 创建扩展Constraint链 {c}: 深度={depth}");
            }
        }

        private static void CreateExtendedPhysBoneChains(GameObject root, int chainCount, int chainLength, int colliderCount)
        {
            for (int c = 0; c < chainCount; c++)
            {
                var physBoneRoot = new GameObject($"ExPhysBone_{c}");
                physBoneRoot.transform.SetParent(root.transform);
                physBoneRoot.transform.localPosition = new Vector3(c * 0.5f, 1f, 0);

                var chainRoot = new GameObject($"ExBoneChain_{c}");
                chainRoot.transform.SetParent(physBoneRoot.transform);
                chainRoot.transform.localPosition = Vector3.zero;

                Transform previous = chainRoot.transform;
                for (int i = 0; i < chainLength; i++)
                {
                    var bone = new GameObject($"B_{i}");
                    bone.transform.SetParent(previous);
                    bone.transform.localPosition = new Vector3(0, 0.1f, 0);
                    previous = bone.transform;
                }

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

                physBone.limitType = VRCPhysBone.LimitType.Angle;
                physBone.maxAngleX = 45f;
                physBone.maxAngleZ = 45f;
                physBone.limitRotation = new Vector3(15, 30, 15);

                physBone.maxStretch = 0.5f;

                physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
                physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
                physBone.grabMovement = 0.8f;
                physBone.snapToHand = true;

                physBone.parameter = "";
                physBone.isAnimated = false;

                var collidersList = new List<VRCPhysBoneCollider>();

                for (int i = 0; i < colliderCount; i++)
                {
                    var colliderObj = new GameObject($"Col_{i}");
                    colliderObj.transform.SetParent(physBoneRoot.transform);
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

                physBone.colliders = collidersList.ConvertAll(x => x as VRCPhysBoneColliderBase);

                Debug.Log($"[ASS] 创建扩展PhysBone链 {c}: 长度={chainLength}, Collider={colliderCount}");
            }
        }

        private static void CreateExtendedContactSystem(GameObject root, int componentCount)
        {
            var contactRoot = new GameObject("ExContactSystem");
            contactRoot.transform.SetParent(root.transform);

            int halfCount = componentCount / 2;

            var tags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5", "Tag6", "Tag7", "Tag8", "Tag9", "Tag10" };

            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"ExS_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
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

            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"ExR_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
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

            Debug.Log($"[ASS] 创建扩展Contact系统: {halfCount} senders + {halfCount} receivers");
        }

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

        private static void CreateLightDefense(GameObject root, int lightCount)
        {
            var lightRoot = new GameObject("LightDefense");
            lightRoot.transform.SetParent(root.transform);

            for (int i = 0; i < lightCount; i++)
            {
                var lightObj = new GameObject($"L_{i}");
                lightObj.transform.SetParent(lightRoot.transform);

                float angle = (360f / lightCount) * i;
                float radius = 2f;
                lightObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    1f + (i % 3) * 0.5f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );

                var light = lightObj.AddComponent<Light>();

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

                light.intensity = 2f;
                float hue = (float)i / lightCount;
                light.color = Color.HSVToRGB(hue, 0.5f, 1f);

                light.shadows = LightShadows.Soft;
                light.shadowStrength = 1f;
                light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
                light.shadowBias = 0.05f;
                light.shadowNormalBias = 0.4f;
            }

            Debug.Log($"[ASS] 创建光源防御: {lightCount}个光源");
        }

        private static Mesh CreateHighDensitySphereMesh(int targetVertexCount)
        {
            var mesh = new Mesh { name = "ASS_Mesh" };

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

        /// <summary>
        /// 创建 PhysX Rigidbody + Collider 防御
        /// 利用物理引擎计算增加 CPU 开销
        /// </summary>
        private static void CreatePhysXDefense(GameObject root, int rigidbodyCount, int colliderCount)
        {
            var physXRoot = new GameObject("PhysXDefense");
            physXRoot.transform.SetParent(root.transform);

            for (int i = 0; i < rigidbodyCount; i++)
            {
                var rbObj = new GameObject($"Rigidbody_{i}");
                rbObj.transform.SetParent(physXRoot.transform);
                rbObj.transform.localPosition = new Vector3(i * 0.5f, 0, 0);

                var rb = rbObj.AddComponent<Rigidbody>();
                rb.mass = 100f;
                rb.drag = 50f;
                rb.angularDrag = 50f;
                rb.useGravity = false;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.constraints = RigidbodyConstraints.FreezeAll;

                // 添加 Collider
                int collidersPerBody = Mathf.Max(1, colliderCount / Mathf.Max(1, rigidbodyCount));
                for (int j = 0; j < collidersPerBody; j++)
                {
                    var colliderObj = new GameObject($"Collider_{j}");
                    colliderObj.transform.SetParent(rbObj.transform);
                    colliderObj.transform.localPosition = new Vector3(j * 0.2f, 0, 0);

                    if (j % 2 == 0)
                    {
                        var boxCollider = colliderObj.AddComponent<BoxCollider>();
                        boxCollider.size = new Vector3(0.5f, 0.5f, 0.5f);
                    }
                    else
                    {
                        var sphereCollider = colliderObj.AddComponent<SphereCollider>();
                        sphereCollider.radius = 0.25f;
                    }
                }
            }

            Debug.Log($"[ASS] 创建 PhysX 防御: {rigidbodyCount} Rigidbodies");
        }

        /// <summary>
        /// 创建 Cloth 布料防御
        /// 布料模拟是 CPU 密集操作，可有效增加 CPU 开销
        /// </summary>
        private static void CreateClothDefense(GameObject root, int clothCount)
        {
            var clothRoot = new GameObject("ClothDefense");
            clothRoot.transform.SetParent(root.transform);

            for (int c = 0; c < clothCount; c++)
            {
                var clothObj = new GameObject($"Cloth_{c}");
                clothObj.transform.SetParent(clothRoot.transform);
                clothObj.transform.localPosition = new Vector3(c * 1f, 0, 0);

                // 创建布料网格（简单四边形网格）
                var meshFilter = clothObj.AddComponent<MeshFilter>();
                var mesh = new Mesh { name = $"ClothMesh_{c}" };

                // 创建 10x10 网格
                Vector3[] vertices = new Vector3[121]; // 11x11
                int[] triangles = new int[200 * 6];    // 10x10 * 2个三角形 * 3个顶点

                for (int x = 0; x <= 10; x++)
                {
                    for (int y = 0; y <= 10; y++)
                    {
                        int idx = x * 11 + y;
                        vertices[idx] = new Vector3(x * 0.1f, y * 0.1f, 0);
                    }
                }

                int triIdx = 0;
                for (int x = 0; x < 10; x++)
                {
                    for (int y = 0; y < 10; y++)
                    {
                        int v0 = x * 11 + y;
                        int v1 = v0 + 1;
                        int v2 = v0 + 11;
                        int v3 = v2 + 1;

                        // 第一个三角形
                        triangles[triIdx++] = v0;
                        triangles[triIdx++] = v2;
                        triangles[triIdx++] = v1;

                        // 第二个三角形
                        triangles[triIdx++] = v1;
                        triangles[triIdx++] = v2;
                        triangles[triIdx++] = v3;
                    }
                }

                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                meshFilter.mesh = mesh;

                // 添加 SkinnedMeshRenderer（为了支持 Cloth）
                var meshRenderer = clothObj.AddComponent<SkinnedMeshRenderer>();
                meshRenderer.sharedMesh = mesh;

                // 添加 Cloth 组件
                var cloth = clothObj.AddComponent<Cloth>();
#if UNITY_2021_2_OR_NEWER
                cloth.clothSolverFrequency = 240f;
#else
                cloth.solverFrequency = 240f;
#endif
                cloth.stiffnessFrequency = 1f;
                cloth.useGravity = false;
                cloth.damping = 0.9f;
                cloth.selfCollisionDistance = 0f;
                cloth.selfCollisionStiffness = 0.2f;
                cloth.worldVelocityScale = 0f;
                cloth.friction = 0.5f;
                cloth.collisionMassScale = 0.5f;
#if UNITY_2021_2_OR_NEWER
                cloth.enableContinuousCollision = true;
#else
                cloth.useContinuousCollision = true;
#endif
            }

            Debug.Log($"[ASS] 创建 Cloth 防御: {clothCount} 布料");
        }

        /// <summary>
        /// 创建额外 Animator 组件防御
        /// 多个 Animator 会增加动画系统开销，特别是当它们互相同步参数时
        /// </summary>
        private static void CreateAnimatorDefense(GameObject root, int animatorCount)
        {
            var animatorRoot = new GameObject("AnimatorDefense");
            animatorRoot.transform.SetParent(root.transform);

            for (int i = 0; i < animatorCount; i++)
            {
                var animObj = new GameObject($"DefenseAnimator_{i}");
                animObj.transform.SetParent(animatorRoot.transform);

                var animator = animObj.AddComponent<Animator>();
                // 创建空的 RuntimeAnimatorController（占用资源但无实际动画）
                var controller = new AnimatorController();
                controller.name = $"DefenseController_{i}";
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            Debug.Log($"[ASS] 创建 Animator 防御: {animatorCount} 组件");
        }

    }
}

