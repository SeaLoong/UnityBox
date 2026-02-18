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
                CreateDefenseComponents();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateDefenseComponents调用失败: {e.Message}\n{e.StackTrace}");
                throw;
            }

            controller.AddLayer(layer);
        }

        private GameObject CreateDefenseComponents()
        {
            var existingRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (existingRoot != null)
                Object.DestroyImmediate(existingRoot.gameObject);

            int existingPhysBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true).Length;
            int existingColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            int existingConstraints = avatarRoot.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            int existingContacts = avatarRoot.GetComponentsInChildren<VRCContactSender>(true).Length
                                 + avatarRoot.GetComponentsInChildren<VRCContactReceiver>(true).Length;
            int existingRigidbodies = avatarRoot.GetComponentsInChildren<Rigidbody>(true).Length;
            int existingCloth = avatarRoot.GetComponentsInChildren<Cloth>(true).Length;
            int existingAnimators = avatarRoot.GetComponentsInChildren<Animator>(true).Length;

            var root = new GameObject(Constants.GO_DEFENSE_ROOT);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(false);

            var parameters = ComputeDefenseParams();

            bool enableCpu = config.defenseLevel >= 1;
            bool enableGpu = config.defenseLevel >= 2;

            int constraintBudget = Mathf.Max(0, Constants.CONSTRAINT_MAX_COUNT - existingConstraints);
            int pbBudget = Mathf.Max(0, Constants.PHYSBONE_MAX_COUNT - existingPhysBones);
            int colliderBudget = Mathf.Max(0, Constants.PHYSBONE_COLLIDER_MAX_COUNT - existingColliders);

            int existingCollisionChecks = 0;
            foreach (var pb in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                int colCount = 0;
                if (pb.colliders != null)
                    foreach (var col in pb.colliders)
                        if (col != null) colCount++;
                if (colCount == 0) continue;
                Transform pbRoot = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                existingCollisionChecks += colCount * CountTransformsInHierarchy(pbRoot);
            }
            int colliderCheckBudget = Mathf.Max(0, Constants.PHYSBONE_COLLIDER_CHECK_MAX_COUNT - existingCollisionChecks);
            int contactBudget = Mathf.Max(0, Constants.CONTACT_MAX_COUNT - existingContacts);
            int rigidbodyBudget = Mathf.Max(0, Constants.RIGIDBODY_MAX_COUNT - existingRigidbodies);
            int clothBudget = Mathf.Max(0, Constants.CLOTH_MAX_COUNT - existingCloth);
            int animatorBudget = Mathf.Max(0, Constants.ANIMATOR_MAX_COUNT - existingAnimators);

            if (enableCpu)
            {
                if (parameters.ConstraintChainCount > 0 && constraintBudget > 0)
                    FillConstraintChains(root, parameters.ConstraintChainCount, parameters.ConstraintDepth, constraintBudget);

                if (parameters.PhysBoneChainCount > 0 && pbBudget > 0)
                {
                    int chainBudget = Mathf.Min(parameters.PhysBoneChainCount, pbBudget);
                    int colliderBudgetRemaining = Mathf.Min(parameters.PhysBoneColliders, colliderBudget);
                    int checksRemaining = colliderCheckBudget;
                    int chainsCreated = 0;

                    while (chainsCreated < chainBudget)
                    {
                        int remainingChains = chainBudget - chainsCreated;

                        // 逐条分配 collider（均分剩余）
                        int collidersForThis = colliderBudgetRemaining / remainingChains;

                        // 由 collision check 预算均分后约束链长
                        int checkAllowance = checksRemaining / remainingChains;
                        int chainLength = parameters.PhysBoneLength;
                        if (collidersForThis > 0 && checkAllowance > 0)
                        {
                            chainLength = Mathf.Min(chainLength, checkAllowance / collidersForThis);
                        }
                        else if (collidersForThis > 0)
                        {
                            collidersForThis = 0;
                        }
                        chainLength = Mathf.Max(1, chainLength);

                        CreatePhysBoneChain(root, chainLength, collidersForThis, chainsCreated);

                        int checksUsed = collidersForThis * chainLength;
                        checksRemaining -= checksUsed;
                        colliderBudgetRemaining -= collidersForThis;
                        chainsCreated++;
                    }
                }

                if (parameters.ContactCount > 0 && contactBudget > 0)
                    FillContacts(root, Mathf.Min(parameters.ContactCount, contactBudget));

                if (parameters.AnimatorComponentCount > 0 && animatorBudget > 0)
                    CreateAnimatorComponents(root, Mathf.Min(parameters.AnimatorComponentCount, animatorBudget));
            }

            if (enableGpu)
            {
                if (parameters.MaterialCount > 0)
                    CreateMaterialComponents(root, parameters);

                if (parameters.ParticleCount > 0)
                    CreateParticleComponents(root, parameters.ParticleSystemCount, parameters.ParticleCount);

                if (parameters.LightCount > 0)
                    CreateLightComponents(root, parameters.LightCount);

                if (parameters.PhysXRigidbodyCount > 0 && rigidbodyBudget > 0)
                    CreatePhysXComponents(root, Mathf.Min(parameters.PhysXRigidbodyCount, rigidbodyBudget), parameters.PhysXColliderCount);

                if (parameters.ClothComponentCount > 0 && clothBudget > 0)
                    CreateClothComponents(root, Mathf.Min(parameters.ClothComponentCount, clothBudget));
            }

            return root;
        }

        private void CreateMaterialComponents(GameObject root, DefenseParams parameters)
        {
            var materialRoot = new GameObject("MaterialDefense");
            materialRoot.transform.SetParent(root.transform);
            materialRoot.transform.localPosition = Vector3.zero;

            int baseVerticesPerMesh = parameters.PolyVertices / Mathf.Max(1, parameters.MaterialCount);
            int extraVertices = parameters.PolyVertices % Mathf.Max(1, parameters.MaterialCount);

            int texturePoolSize = parameters.MaterialCount;
            var texturePool = new RenderTexture[texturePoolSize];
            for (int t = 0; t < texturePoolSize; t++)
                texturePool[t] = CreateRenderTexture(t);

            for (int i = 0; i < parameters.MaterialCount; i++)
            {
                var material = CreateMaterial(i, texturePool);
                if (material == null)
                {
                    Debug.LogWarning($"[ASS] 无法创建防御材质 #{i}，跳过");
                    continue;
                }

                var meshObj = new GameObject($"Mesh_{i}");
                meshObj.transform.SetParent(materialRoot.transform);
                meshObj.transform.localPosition = Vector3.zero;
                meshObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

                int verticesForThisMesh = baseVerticesPerMesh + (i < extraVertices ? 1 : 0);
                var mesh = GenerateSphereMesh(verticesForThisMesh);

                var meshFilter = meshObj.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;

                var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                meshRenderer.receiveShadows = true;
                meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                meshRenderer.allowOcclusionWhenDynamic = false;
            }

        }

        private static Material CreateMaterial(int seed, RenderTexture[] texturePool)
        {
            var shader = GetDefenseShader();
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
                material.SetTexture(texProps[i], texturePool[(seed * 3 + i) % texturePool.Length]);

            return material;
        }

        private static RenderTexture CreateRenderTexture(int seed)
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
            public readonly int PhysXRigidbodyCount;
            public readonly int PhysXColliderCount;
            public readonly int ClothComponentCount;
            public readonly int AnimatorComponentCount;
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

        private DefenseParams ComputeDefenseParams()
        {
            if (isDebugMode)
            {
                if (config.defenseLevel <= 0)
                {
                    return new DefenseParams(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                }

                if (config.defenseLevel == 1)
                {
                    return new DefenseParams(
                        constraintDepth: 1,
                        constraintChainCount: 1,
                        physBoneLength: 1,
                        physBoneChainCount: 1,
                        physBoneColliders: 1,
                        physXRigidbodyCount: 0,
                        physXColliderCount: 0,
                        clothComponentCount: 0,
                        animatorComponentCount: 1,
                        contactCount: 1,
                        polyVertices: 0,
                        particleCount: 0,
                        particleSystemCount: 0,
                        lightCount: 0,
                        materialCount: 0
                    );
                }

                return new DefenseParams(
                    constraintDepth: 1,
                    constraintChainCount: 1,
                    physBoneLength: 1,
                    physBoneChainCount: 1,
                    physBoneColliders: 1,
                    physXRigidbodyCount: 1,
                    physXColliderCount: 1,
                    clothComponentCount: 1,
                    animatorComponentCount: 1,
                    contactCount: 1,
                    polyVertices: 100,
                    particleCount: 1,
                    particleSystemCount: 1,
                    lightCount: 1,
                    materialCount: 1
                );
            }

            if (config.defenseLevel == 1)
            {
                return new DefenseParams(
                    constraintDepth: Constants.CONSTRAINT_MAX_COUNT,
                    constraintChainCount: Constants.CONSTRAINT_MAX_COUNT,
                    physBoneLength: Constants.PHYSBONE_MAX_COUNT,
                    physBoneChainCount: Constants.PHYSBONE_MAX_COUNT,
                    physBoneColliders: Constants.PHYSBONE_COLLIDER_MAX_COUNT,
                    physXRigidbodyCount: 0,
                    physXColliderCount: 0,
                    clothComponentCount: 0,
                    animatorComponentCount: Constants.ANIMATOR_MAX_COUNT,
                    contactCount: Constants.CONTACT_MAX_COUNT,
                    polyVertices: 0,
                    particleCount: 0,
                    particleSystemCount: 0,
                    lightCount: 0,
                    materialCount: 0
                );
            }

            return new DefenseParams(
                constraintDepth: Constants.CONSTRAINT_MAX_COUNT,
                constraintChainCount: Constants.CONSTRAINT_MAX_COUNT,
                physBoneLength: Constants.PHYSBONE_MAX_COUNT,
                physBoneChainCount: Constants.PHYSBONE_MAX_COUNT,
                physBoneColliders: Constants.PHYSBONE_COLLIDER_MAX_COUNT,
                physXRigidbodyCount: Constants.RIGIDBODY_MAX_COUNT,
                physXColliderCount: Constants.RIGIDBODY_COLLIDER_MAX_COUNT,
                clothComponentCount: Constants.CLOTH_MAX_COUNT,
                animatorComponentCount: Constants.ANIMATOR_MAX_COUNT,
                contactCount: Constants.CONTACT_MAX_COUNT,
                polyVertices: Constants.POLY_VERTICES_MAX_COUNT,
                particleCount: Constants.PARTICLE_MAX_COUNT,
                particleSystemCount: Constants.PARTICLE_SYSTEM_MAX_COUNT,
                lightCount: Constants.LIGHT_MAX_COUNT,
                materialCount: Constants.MATERIAL_MAX_COUNT
            );
        }

        private int FillConstraintChains(GameObject root, int chainCount, int maxDepth, int budget)
        {
            int used = 0;

            for (int c = 0; c < chainCount && used < budget; c++)
            {
                var chainRoot = new GameObject($"Chain_{c}");
                chainRoot.transform.SetParent(root.transform);
                chainRoot.transform.localPosition = Vector3.zero;

                GameObject previous = chainRoot;
                int actualDepth = 0;

                for (int i = 0; i < maxDepth; i++)
                {
                    if (used + 1 > budget) break;

                    var node = new GameObject($"C_{i}");
                    node.transform.SetParent(chainRoot.transform);
                    node.transform.localPosition = new Vector3(0, i * 0.01f, 0);

                    var constraint = node.AddComponent<VRCParentConstraint>();
                    constraint.Sources.Add(new VRCConstraintSource
                    {
                        Weight = 1f,
                        SourceTransform = previous.transform,
                        ParentPositionOffset = Vector3.zero,
                        ParentRotationOffset = Vector3.zero
                    });
                    ActivateConstraint(constraint, true, true);

                    used++;
                    actualDepth++;
                    previous = node;
                }


            }

            return used;
        }

        private static void ActivateConstraint<T>(T constraint, bool isActive, bool isLocked) where T : VRCConstraintBase
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

        private static void CreatePhysBoneChain(GameObject root, int chainLength, int colliderCount, int chainIndex)
        {
            var physBoneRoot = new GameObject($"PhysBone_{chainIndex}");
            physBoneRoot.transform.SetParent(root.transform);
            physBoneRoot.transform.localPosition = Vector3.zero;

            var chainRoot = new GameObject($"BoneChain_{chainIndex}");
            chainRoot.transform.SetParent(physBoneRoot.transform);
            chainRoot.transform.localPosition = Vector3.zero;

            Transform previous = chainRoot.transform;
            for (int i = 0; i < chainLength - 1; i++)
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
                colliderObj.transform.localPosition = Vector3.zero;

                var collider = colliderObj.AddComponent<VRCPhysBoneCollider>();
                collider.shapeType = VRCPhysBoneCollider.ShapeType.Capsule;
                collider.radius = 0.3f;
                collider.height = 1.0f;
                collider.insideBounds = true;
                collider.bonesAsSpheres = false;

                collidersList.Add(collider);
            }

            physBone.colliders = collidersList.ConvertAll(x => x as VRCPhysBoneColliderBase);
        }

        private static void FillContacts(GameObject root, int componentCount)
        {
            var contactRoot = new GameObject("ContactSystem");
            contactRoot.transform.SetParent(root.transform);

            int senderCount = (componentCount + 1) / 2;
            int receiverCount = componentCount / 2;

            for (int i = 0; i < senderCount; i++)
            {
                var senderObj = new GameObject($"S_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
                senderObj.transform.localPosition = Vector3.zero;

                var sender = senderObj.AddComponent<VRCContactSender>();
                sender.shapeType = VRCContactSender.ShapeType.Capsule;
                sender.radius = 1.0f;
                sender.height = 2f;
                sender.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
                sender.localOnly = true;
            }

            for (int i = 0; i < receiverCount; i++)
            {
                var receiverObj = new GameObject($"R_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
                receiverObj.transform.localPosition = Vector3.zero;

                var receiver = receiverObj.AddComponent<VRCContactReceiver>();
                receiver.shapeType = VRCContactReceiver.ShapeType.Capsule;
                receiver.radius = 1.0f;
                receiver.height = 2f;
                receiver.collisionTags = new List<string> { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
                receiver.localOnly = true;
            }

        }

        private static void CreateParticleComponents(GameObject root, int systemBudget, int particleBudget)
        {
            var particleRoot = new GameObject("ParticleDefense");
            particleRoot.transform.SetParent(root.transform);

            // 根据多边形预算动态计算粒子 Mesh 复杂度
            // 先用最小 subdivisions=2 计算最少三角面数（8面），反推粒子预算上限
            int meshSubdivisions = 2;
            int meshTriangles = meshSubdivisions * meshSubdivisions * 2; // 8

            if (particleBudget > 0 && particleBudget <= Constants.MESH_PARTICLE_MAX_POLYGONS / meshTriangles)
            {
                // 粒子数不多，可以提高 Mesh 复杂度
                int maxTrisPerParticle = Constants.MESH_PARTICLE_MAX_POLYGONS / particleBudget;
                meshSubdivisions = Mathf.Clamp(Mathf.FloorToInt(Mathf.Sqrt(maxTrisPerParticle / 2f)), 2, 200);
                meshTriangles = meshSubdivisions * meshSubdivisions * 2;
            }
            else if (particleBudget > 0)
            {
                // 粒子数太多，即使最简 Mesh 也会超多边形预算，需要削减粒子总数
                particleBudget = Constants.MESH_PARTICLE_MAX_POLYGONS / meshTriangles;
            }

            int meshVertexTarget = meshSubdivisions * meshSubdivisions * 6;
            var sharedParticleMesh = GenerateSphereMesh(meshVertexTarget);
            var sharedSubEmitterMesh = GenerateSphereMesh(meshVertexTarget);

            int systemsUsed = 0;
            int particlesUsed = 0;

            var mainSystems = new List<ParticleSystem>();
            var mainObjects = new List<GameObject>();

            // 第一轮：顺序填充主粒子系统
            while (systemsUsed < systemBudget && particlesUsed < particleBudget)
            {
                int remaining = particleBudget - particlesUsed;
                int remainingSystems = systemBudget - systemsUsed;
                int particlesForThis = remaining / remainingSystems;
                if (particlesForThis <= 0) break;

                int s = mainSystems.Count;
                var psObj = new GameObject($"PS_{s}");
                psObj.transform.SetParent(particleRoot.transform);
                psObj.transform.localPosition = Vector3.zero;

                var ps = psObj.AddComponent<ParticleSystem>();
                var renderer = psObj.GetComponent<ParticleSystemRenderer>();

                var main = ps.main;
                main.duration = 10f;
                main.loop = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    Color.HSVToRGB((float)s / systemBudget, 0.8f, 1f),
                    Color.HSVToRGB(((float)s / systemBudget + 0.3f) % 1f, 0.6f, 0.8f)
                );
                main.maxParticles = particlesForThis;
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

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = particlesForThis / 2f;
                emission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, (short)(particlesForThis / 10), (short)(particlesForThis / 5), 3, 0.5f),
                    new ParticleSystem.Burst(2f, (short)(particlesForThis / 8), (short)(particlesForThis / 4), 2, 1f)
                });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = (s % 3 == 0) ? ParticleSystemShapeType.Sphere :
                                  (s % 3 == 1) ? ParticleSystemShapeType.Cone :
                                                  ParticleSystemShapeType.Box;
                shape.radius = 2f;
                shape.angle = 45f;
                shape.randomDirectionAmount = 0.5f;
                shape.randomPositionAmount = 1f;

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

                var forceOverLifetime = ps.forceOverLifetime;
                forceOverLifetime.enabled = true;
                forceOverLifetime.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.y = new ParticleSystem.MinMaxCurve(-1f, 3f);
                forceOverLifetime.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.randomized = true;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.HSVToRGB((float)s / systemBudget, 1f, 1f), 0f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemBudget + 0.5f) % 1f, 1f, 1f), 0.5f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemBudget + 0.8f) % 1f, 0.8f, 0.6f), 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(1f, 0.7f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.separateAxes = true;
                sizeOverLifetime.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                sizeOverLifetime.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.2f));
                sizeOverLifetime.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));

                var rotationOverLifetime = ps.rotationOverLifetime;
                rotationOverLifetime.enabled = true;
                rotationOverLifetime.separateAxes = true;
                rotationOverLifetime.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-360f, 360f);

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
                collision.collidesWith = ~0;
                collision.sendCollisionMessages = true;
                collision.multiplyColliderForceByCollisionAngle = true;
                collision.multiplyColliderForceByParticleSize = true;
                collision.multiplyColliderForceByParticleSpeed = true;

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

                var textureSheet = ps.textureSheetAnimation;
                textureSheet.enabled = true;
                textureSheet.mode = ParticleSystemAnimationMode.Grid;
                textureSheet.numTilesX = 4;
                textureSheet.numTilesY = 4;
                textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
                textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);
                textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 15f);
                textureSheet.cycleCount = 3;

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

                var inheritVelocity = ps.inheritVelocity;
                inheritVelocity.enabled = true;
                inheritVelocity.mode = ParticleSystemInheritVelocityMode.Current;
                inheritVelocity.curve = new ParticleSystem.MinMaxCurve(0.5f);

                var lifetimeBySpeed = ps.lifetimeByEmitterSpeed;
                lifetimeBySpeed.enabled = true;
                lifetimeBySpeed.curve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
                lifetimeBySpeed.range = new Vector2(0f, 10f);

                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = sharedParticleMesh;
                    renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;

                    var particleShader = Shader.Find("Standard");
                    if (particleShader == null)
                        particleShader = Shader.Find("Particles/Standard Unlit");

                    if (particleShader != null)
                    {
                        var mat = new Material(particleShader);
                        float hue = (float)s / systemBudget;
                        mat.color = Color.HSVToRGB(hue, 0.7f, 0.9f);
                        mat.SetFloat("_Metallic", 0.8f);
                        mat.SetFloat("_Glossiness", 0.9f);
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", Color.HSVToRGB(hue, 0.5f, 0.3f));
                        renderer.sharedMaterial = mat;

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

                mainSystems.Add(ps);
                mainObjects.Add(psObj);
                systemsUsed++;
                particlesUsed += particlesForThis;
            }

            // 第二轮：为每个主系统顺序填充子粒子系统
            int mainCount = mainSystems.Count;
            for (int s = 0; s < mainCount && systemsUsed < systemBudget && particlesUsed < particleBudget; s++)
            {
                int remaining = particleBudget - particlesUsed;
                int remainingSubs = Mathf.Min(mainCount - s, systemBudget - systemsUsed);
                int subParticles = remaining / Mathf.Max(1, remainingSubs);
                if (subParticles <= 0) break;

                var ps = mainSystems[s];
                var psObj = mainObjects[s];

                var subEmitterObj = new GameObject($"SubEmitter_{s}");
                subEmitterObj.transform.SetParent(psObj.transform);
                subEmitterObj.transform.localPosition = Vector3.zero;
                var subPs = subEmitterObj.AddComponent<ParticleSystem>();
                var subRenderer = subEmitterObj.GetComponent<ParticleSystemRenderer>();

                var subMain = subPs.main;
                subMain.duration = 2f;
                subMain.loop = true;
                subMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                subMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
                subMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.maxParticles = subParticles;
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
                        subMat.color = Color.HSVToRGB(((float)s / mainCount + 0.5f) % 1f, 1f, 1f);
                        subMat.EnableKeyword("_EMISSION");
                        subMat.SetColor("_EmissionColor", Color.white * 2f);
                        subRenderer.sharedMaterial = subMat;
                        subRenderer.trailMaterial = subMat;
                    }
                    subRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    subRenderer.enableGPUInstancing = true;
                }

                var subEmitters = ps.subEmitters;
                subEmitters.enabled = true;
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor);
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);

                systemsUsed++;
                particlesUsed += subParticles;
            }

        }

        private static Shader GetDefenseShader()
        {
            Shader defenseShader = Shader.Find("UnityBox/ASS_DefenseShader");
            if (defenseShader != null)
                return defenseShader;

            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
                Debug.LogError("[ASS] 无法找到Standard Shader");

            return standardShader;
        }

        private static void CreateLightComponents(GameObject root, int lightCount)
        {
            var lightRoot = new GameObject("LightDefense");
            lightRoot.transform.SetParent(root.transform);

            for (int i = 0; i < lightCount; i++)
            {
                var lightObj = new GameObject($"L_{i}");
                lightObj.transform.SetParent(lightRoot.transform);
                lightObj.transform.localPosition = Vector3.zero;

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

        }

        private static Mesh GenerateSphereMesh(int targetVertexCount)
        {
            var mesh = new Mesh { name = "ASS_Mesh" };

            int subdivisions = Mathf.CeilToInt(Mathf.Sqrt(targetVertexCount / 6f));
            subdivisions = Mathf.Clamp(subdivisions, 2, 200);

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

        private static void CreatePhysXComponents(GameObject root, int rigidbodyCount, int colliderCount)
        {
            var physXRoot = new GameObject("PhysXDefense");
            physXRoot.transform.SetParent(root.transform);

            for (int i = 0; i < rigidbodyCount; i++)
            {
                var rbObj = new GameObject($"Rigidbody_{i}");
                rbObj.transform.SetParent(physXRoot.transform);
                rbObj.transform.localPosition = Vector3.zero;

                var rb = rbObj.AddComponent<Rigidbody>();
                rb.mass = 100f;
                rb.drag = 50f;
                rb.angularDrag = 50f;
                rb.useGravity = false;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.constraints = RigidbodyConstraints.FreezeAll;

                int collidersPerBody = Mathf.Max(1, colliderCount / Mathf.Max(1, rigidbodyCount));
                for (int j = 0; j < collidersPerBody; j++)
                {
                    var colliderObj = new GameObject($"Collider_{j}");
                    colliderObj.transform.SetParent(rbObj.transform);
                    colliderObj.transform.localPosition = Vector3.zero;

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

        }

        private static void CreateClothComponents(GameObject root, int clothCount)
        {
            var clothRoot = new GameObject("ClothDefense");
            clothRoot.transform.SetParent(root.transform);

            for (int c = 0; c < clothCount; c++)
            {
                var clothObj = new GameObject($"Cloth_{c}");
                clothObj.transform.SetParent(clothRoot.transform);
                clothObj.transform.localPosition = Vector3.zero;

                var meshFilter = clothObj.AddComponent<MeshFilter>();
                var mesh = new Mesh { name = $"ClothMesh_{c}" };

                // 根据顶点预算动态计算 Cloth 网格复杂度
                int verticesPerCloth = Constants.TOTAL_CLOTH_VERTICES_MAX / Mathf.Max(1, clothCount);
                int gridSizePlus1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Sqrt(verticesPerCloth)), 3, 500);
                int gridSize = gridSizePlus1 - 1;
                Vector3[] vertices = new Vector3[gridSizePlus1 * gridSizePlus1];
                int[] triangles = new int[gridSize * gridSize * 6];

                for (int x = 0; x <= gridSize; x++)
                {
                    for (int y = 0; y <= gridSize; y++)
                    {
                        int idx = x * gridSizePlus1 + y;
                        vertices[idx] = new Vector3((float)x / gridSize, (float)y / gridSize, 0);
                    }
                }

                int triIdx = 0;
                for (int x = 0; x < gridSize; x++)
                {
                    for (int y = 0; y < gridSize; y++)
                    {
                        int v0 = x * gridSizePlus1 + y;
                        int v1 = v0 + 1;
                        int v2 = v0 + gridSizePlus1;
                        int v3 = v2 + 1;

                        triangles[triIdx++] = v0;
                        triangles[triIdx++] = v2;
                        triangles[triIdx++] = v1;

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

                var meshRenderer = clothObj.AddComponent<SkinnedMeshRenderer>();
                meshRenderer.sharedMesh = mesh;

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

        }

        private static void CreateAnimatorComponents(GameObject root, int animatorCount)
        {
            var animatorRoot = new GameObject("AnimatorDefense");
            animatorRoot.transform.SetParent(root.transform);

            for (int i = 0; i < animatorCount; i++)
            {
                var animObj = new GameObject($"Animator_{i}");
                animObj.transform.SetParent(animatorRoot.transform);

                var animator = animObj.AddComponent<Animator>();
                var animController = new AnimatorController();
                animController.name = $"DefenseController_{i}";
                animator.runtimeAnimatorController = animController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

        }

        private static int CountTransformsInHierarchy(Transform root)
        {
            int count = 1;
            for (int i = 0; i < root.childCount; i++)
                count += CountTransformsInHierarchy(root.GetChild(i));
            return count;
        }
    }
}

