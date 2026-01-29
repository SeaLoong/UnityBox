using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
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
        // 缓存共享的 QuadMesh，避免重复创建
        private static Mesh _sharedQuadMesh;
        private static Mesh _sharedQuadMeshLarge;

        /// <summary>
        /// 获取共享的 QuadMesh（自动缓存）
        /// </summary>
        private static Mesh GetSharedQuadMesh(float size)
        {
            if (size > 1f)
            {
                if (_sharedQuadMeshLarge == null)
                {
                    _sharedQuadMeshLarge = CreateQuadMesh(size);
                }
                return _sharedQuadMeshLarge;
            }
            else
            {
                if (_sharedQuadMesh == null)
                {
                    _sharedQuadMesh = CreateQuadMesh(size);
                }
                return _sharedQuadMesh;
            }
        }

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
            // 防御等级为0时，跳过防御层创建
            if (config.defenseLevel <= 0)
            {
                Debug.Log("[ASS] 防御等级为0，跳过防御层创建");
                return null;
            }
             
            float levelMultiplier = Mathf.Clamp01((config.defenseLevel - 1) / 3f);

            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            // 状态：Inactive（防御未激活）
            var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(100, 50, 0));
            inactiveState.motion = AnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = inactiveState;

            // 状态：Active（防御激活）
            var activeState = layer.stateMachine.AddState("Active", new Vector3(100, 150, 0));
            activeState.motion = AnimatorUtils.SharedEmptyClip;
            
            // 转换条件：IsLocal && TimeUp
            var toActive = AnimatorUtils.CreateTransition(inactiveState, activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            try
            {
                CreateDefenseComponents(avatarRoot, config, isDebugMode, levelMultiplier);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateDefenseComponents调用失败: {e.Message}\n{e.StackTrace}");
                throw;
            }

            return layer;
        }

        #region Defense Components Creation

        /// <summary>
        /// 创建防御组件根对象及所有子组件
        /// 自动处理参数到VRChat限制，确保生成的Avatar仍可上传
        /// </summary>
        /// <param name="levelMultiplier">防御等级倍率 (0-1)，用于控制整体强度</param>
        private static GameObject CreateDefenseComponents(
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config,
            bool isDebugMode,
            float levelMultiplier = 1f)
        {
            // 查找或创建根对象
            var existingRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (existingRoot != null)
            {
                // 使用Destroy而不是DestroyImmediate，避免触发Editor刷新
                Object.Destroy(existingRoot.gameObject);
            }

            var root = new GameObject(Constants.GO_DEFENSE_ROOT);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            // 默认禁用，通过动画控制激活
            // 使用 m_IsActive 控制可以完全禁用对象，避免 PhysBone/Constraint 等组件消耗性能
            root.SetActive(false);

            // 根据防御等级和模式调整参数
            int constraintDepth, constraintChainCount;
            int physBoneLength, physBoneChainCount;
            int physBoneColliders;
            int contactCount;
            int shaderLoops;
            int overdrawLayers;
            int polyVertices;
            int particleCount, particleSystemCount;
            int lightCount;
            int materialCount;

            if (isDebugMode)
            {
                // 调试模式使用最小配置
                constraintDepth = 5;
                constraintChainCount = 1;
                physBoneLength = 3;
                physBoneChainCount = 1;
                physBoneColliders = 2;
                contactCount = 4;
                shaderLoops = 0;
                overdrawLayers = 3;
                polyVertices = 1000;
                particleCount = 0;
                particleSystemCount = 0;
                lightCount = 0;
                materialCount = 0;
            }
            else if (config.defenseLevel == 1)
            {
                // 防御等级1：极轻量级配置，避免Unity崩溃
                // 注意：等级1只启用基础CPU防御，不创建GPU相关组件
                constraintDepth = 10;
                constraintChainCount = 1;
                physBoneLength = 5;
                physBoneChainCount = 1;
                physBoneColliders = 5;
                contactCount = 10;
                // 等级1不启用Shader相关功能，将这些值设为0
                shaderLoops = 0;
                overdrawLayers = 10;
                polyVertices = 5000;
                particleCount = 1000;
                particleSystemCount = 1;
                lightCount = 1;
                materialCount = 0;  // 等级1不创建材质，避免不必要的GPU资源分配
                Debug.Log("[ASS] 防御等级1：使用极轻量级配置（仅CPU防御，无GPU组件）");
            }
            else if (config.defenseLevel == 2)
            {
                // 防御等级2：轻量级配置
                constraintDepth = ValidateConstraintDepth(Mathf.RoundToInt(config.constraintChainDepth * 0.25f));
                constraintChainCount = Mathf.Max(1, Mathf.RoundToInt(config.constraintChainCount * 0.25f));
                physBoneLength = ValidatePhysBoneLength(Mathf.RoundToInt(config.physBoneChainLength * 0.25f));
                physBoneChainCount = Mathf.Max(1, Mathf.RoundToInt(config.physBoneChainCount * 0.25f));
                int existingColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
                physBoneColliders = ValidatePhysBoneColliders(
                    Mathf.RoundToInt(config.physBoneColliderCount * 0.25f),
                    physBoneLength,
                    existingColliders);
                contactCount = Mathf.Min(Mathf.RoundToInt(config.contactComponentCount * 0.25f), Constants.CONTACT_MAX_COUNT);
                shaderLoops = ValidateShaderLoops(Mathf.RoundToInt(config.shaderLoopCount * 0.01f));
                overdrawLayers = ValidateOverdrawLayers(Mathf.RoundToInt(config.overdrawLayerCount * 0.01f));
                polyVertices = ValidateHighPolyVertices(Mathf.RoundToInt(config.highPolyVertexCount * 0.1f));
                particleCount = Mathf.RoundToInt(config.particleCount * 0.01f);
                particleSystemCount = Mathf.Max(1, Mathf.RoundToInt(config.particleSystemCount * 0.1f));
                lightCount = Mathf.RoundToInt(config.lightCount * 0.1f);
                materialCount = Mathf.Max(1, Mathf.RoundToInt(config.materialCount * 0.1f));
                Debug.Log("[ASS] 防御等级2：使用轻量级配置（25%强度）");
            }
            else
            {
                // 防御等级3-4：根据倍率调整配置
                float multiplier = levelMultiplier;
                constraintDepth = ValidateConstraintDepth(Mathf.RoundToInt(config.constraintChainDepth * multiplier));
                constraintChainCount = Mathf.Max(1, Mathf.RoundToInt(config.constraintChainCount * multiplier));
                physBoneLength = ValidatePhysBoneLength(Mathf.RoundToInt(config.physBoneChainLength * multiplier));
                physBoneChainCount = Mathf.Max(1, Mathf.RoundToInt(config.physBoneChainCount * multiplier));
                int existingColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
                physBoneColliders = ValidatePhysBoneColliders(
                    Mathf.RoundToInt(config.physBoneColliderCount * multiplier),
                    physBoneLength,
                    existingColliders);
                contactCount = Mathf.Min(Mathf.RoundToInt(config.contactComponentCount * multiplier), Constants.CONTACT_MAX_COUNT);
                shaderLoops = ValidateShaderLoops(Mathf.RoundToInt(config.shaderLoopCount * multiplier));
                overdrawLayers = ValidateOverdrawLayers(Mathf.RoundToInt(config.overdrawLayerCount * multiplier));
                polyVertices = ValidateHighPolyVertices(Mathf.RoundToInt(config.highPolyVertexCount * multiplier));
                particleCount = Mathf.RoundToInt(config.particleCount * multiplier);
                particleSystemCount = Mathf.Max(1, Mathf.RoundToInt(config.particleSystemCount * multiplier));
                lightCount = Mathf.RoundToInt(config.lightCount * multiplier);
                materialCount = Mathf.Max(1, Mathf.RoundToInt(config.materialCount * multiplier));
                Debug.Log($"[ASS] 防御等级{config.defenseLevel}：使用完整配置（{multiplier:P0}强度）");
            }

#if VRC_SDK_VRCSDK3
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
            
            // 限制防御系统创建的PhysBone数量（每条链1个PhysBone）
            int defensePhysBoneCount = Mathf.Min(physBoneChainCount, maxDefensePhysBones);
#else
            int defensePhysBoneCount = physBoneChainCount;
#endif

            // 创建CPU消耗组件 - Constraint链
            if (config.enableConstraintChain)
            {
                // 创建多条Constraint链以增加CPU消耗
                for (int i = 0; i < constraintChainCount; i++)
                {
                    CreateConstraintChain(root, constraintDepth, i);
                }
                 
                // 创建扩展Constraint链（使用ScaleConstraint增加CPU消耗）
                // 仅在等级3+且非调试模式下创建
                if (!isDebugMode && config.defenseLevel >= 3 && constraintChainCount > 0)
                {
                    CreateExtendedConstraintChains(root, Mathf.Min(constraintChainCount, 5), constraintDepth);
                }
            }

#if VRC_SDK_VRCSDK3
            // 创建CPU消耗组件 - PhysBone链
            if (config.enablePhysBone && physBoneColliders > 0 && defensePhysBoneCount > 0)
            {
                // 创建多条PhysBone链以增加CPU消耗
                for (int i = 0; i < defensePhysBoneCount; i++)
                {
                    CreatePhysBoneChains(root, physBoneLength, physBoneColliders, i);
                }
                 
                // 创建扩展PhysBone链（更复杂的物理配置，仅在还有余额时）
                // 仅在等级3+且非调试模式下创建
                int extendedPhysBoneCount = Mathf.Min(defensePhysBoneCount, 3);
                if (!isDebugMode && config.defenseLevel >= 3 && extendedPhysBoneCount > 0 && defensePhysBoneCount < physBoneChainCount)
                {
                    CreateExtendedPhysBoneChains(root, extendedPhysBoneCount, physBoneLength, physBoneColliders);
                }
            }

            if (config.enableContactSystem)
            {
                CreateContactSystem(root, contactCount);
                 
                // 创建扩展Contact系统（使用更多标签）
                // 仅在等级3+且非调试模式下创建
                int remainingContactBudget = Mathf.Max(0, Constants.CONTACT_MAX_COUNT - contactCount);
                int extendedContactCount = Mathf.Min(remainingContactBudget / 2, 50); // 最多50个额外Contact
                if (!isDebugMode && config.defenseLevel >= 3 && extendedContactCount > 0)
                {
                    CreateExtendedContactSystem(root, extendedContactCount);
                }
            }
#endif

            // 创建Overdraw透明层堆叠
            if (config.enableOverdraw)
            {
                // 创建多个Overdraw层堆叠以增加GPU消耗
                // 等级1只创建1组，等级2+创建2组
                int overdrawGroups = config.defenseLevel == 1 ? 1 : 2;
                for (int i = 0; i < overdrawGroups; i++)
                {
                    CreateOverdrawLayers(root, overdrawLayers, i);
                }

                // 创建扩展Overdraw层（更多组）
                // 仅在等级3+且非调试模式下创建
                if (!isDebugMode && config.defenseLevel >= 3 && overdrawLayers > 100)
                {
                    CreateExtendedOverdrawLayers(root, overdrawLayers / 2, 3);
                }
            }

            // 创建复杂Shader网格
            if (config.enableHeavyShader && shaderLoops > 0)
            {
                // 创建多个复杂Shader网格
                // 等级1只创建1个，等级2+创建2个
                int shaderMeshCount = config.defenseLevel == 1 ? 1 : 2;
                for (int i = 0; i < shaderMeshCount; i++)
                {
                    CreateHeavyShaderMesh(root, avatarRoot, shaderLoops, i);
                }
            }
            // 创建高多边形网格
            if (config.enableHighPolyMesh)
            {
                // 创建多个高多边形网格
                // 等级1只创建1个，等级2+创建3个
                int meshCount = config.defenseLevel == 1 ? 1 : 3;
                for (int i = 0; i < meshCount; i++)
                {
                    CreateHighPolyMesh(root, polyVertices / meshCount, i);
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
                CreateExpensiveMaterials(root, avatarRoot, materialCount, config.shaderLoopCount);
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
                    
                    // 使用SerializedObject设置属性，避免触发Editor GUI事件
                    // 添加try-catch防止SerializedObject操作失败
                    try
                    {
                        var parentCSer = new SerializedObject(parentC);
                        var isActiveProp = parentCSer.FindProperty("IsActive");
                        var lockedProp = parentCSer.FindProperty("Locked");
                        if (isActiveProp != null) isActiveProp.boolValue = true;
                        if (lockedProp != null) lockedProp.boolValue = true;
                        parentCSer.ApplyModifiedPropertiesWithoutUndo();
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ASS] 设置Constraint属性时出错: {ex.Message}");
                    }
                    
                    if (i > 0)
                    {
                        try
                        {
                            var posCSer = new SerializedObject(obj.GetComponent<VRCPositionConstraint>());
                            var posIsActive = posCSer.FindProperty("IsActive");
                            var posLocked = posCSer.FindProperty("Locked");
                            if (posIsActive != null) posIsActive.boolValue = true;
                            if (posLocked != null) posLocked.boolValue = true;
                            posCSer.ApplyModifiedPropertiesWithoutUndo();
                            
                            var rotCSer = new SerializedObject(obj.GetComponent<VRCRotationConstraint>());
                            var rotIsActive = rotCSer.FindProperty("IsActive");
                            var rotLocked = rotCSer.FindProperty("Locked");
                            if (rotIsActive != null) rotIsActive.boolValue = true;
                            if (rotLocked != null) rotLocked.boolValue = true;
                            rotCSer.ApplyModifiedPropertiesWithoutUndo();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[ASS] 设置Position/Rotation Constraint属性时出错: {ex.Message}");
                        }
                    }

                    previous = obj;
                }

                Debug.Log($"[ASS] 创建VRC Constraint链 {chainIndex}: 深度={depth}, 每节点包含 Parent/Position/Rotation 三种约束");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] 创建Constraint链时出错: {ex.Message}\n{ex.StackTrace}");
            }
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
                meshFilter.mesh = GetSharedQuadMesh(2f);

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
        /// 创建带复杂Shader的Mesh（调用合并后的方法）
        /// </summary>
        private static void CreateHeavyShaderMesh(GameObject root, GameObject avatarRoot, int loopCount, int shaderIndex = 0)
        {
            var meshObj = new GameObject($"HeavyShaderMesh_{shaderIndex}");
            meshObj.transform.SetParent(root.transform);
            meshObj.transform.localPosition = new Vector3(shaderIndex * 2f, 0, 0);

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.mesh = GetSharedQuadMesh(1f);

            // 在构建时生成Shader并创建材质（使用合并后的方法）
            var material = CreateDefenseShaderMaterial(avatarRoot, loopCount);
            
            // 防御性检查：确保材质创建成功
            if (material == null)
            {
                Debug.LogWarning($"[ASS] 无法创建防御Shader材质，跳过创建HeavyShaderMesh_{shaderIndex}");
                Object.DestroyImmediate(meshObj);
                return;
            }
            
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            meshRenderer.material = material;

            Debug.Log($"[ASS] 创建防御Shader {shaderIndex}: 循环={loopCount}");
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
                        mat.color = new Color(Random.Range(0.3f, 1f), Random.Range(0.3f, 1f), Random.Range(0.3f, 1f), 0.8f);
                        renderer.material = mat;
                    }
                    renderer.maxParticleSize = 2f;
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
                    
                    // 使用SerializedObject设置属性，避免触发Editor GUI事件
                    var parentCSer = new SerializedObject(parentC);
                    parentCSer.FindProperty("IsActive").boolValue = true;
                    parentCSer.FindProperty("Locked").boolValue = true;
                    parentCSer.ApplyModifiedPropertiesWithoutUndo();
                    
                    if (i > 0)
                    {
                        var posCSer = new SerializedObject(obj.GetComponent<VRCPositionConstraint>());
                        posCSer.FindProperty("IsActive").boolValue = true;
                        posCSer.FindProperty("Locked").boolValue = true;
                        posCSer.ApplyModifiedPropertiesWithoutUndo();
                        
                        var rotCSer = new SerializedObject(obj.GetComponent<VRCRotationConstraint>());
                        rotCSer.FindProperty("IsActive").boolValue = true;
                        rotCSer.FindProperty("Locked").boolValue = true;
                        rotCSer.ApplyModifiedPropertiesWithoutUndo();
                        
                        var scaleCSer = new SerializedObject(obj.GetComponent<VRCScaleConstraint>());
                        scaleCSer.FindProperty("IsActive").boolValue = true;
                        scaleCSer.FindProperty("Locked").boolValue = true;
                        scaleCSer.ApplyModifiedPropertiesWithoutUndo();
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

            // 创建Sender
            for (int i = 0; i < halfCount; i++)
            {
                var senderObj = new GameObject($"ExtendedSender_{i}");
                senderObj.transform.SetParent(contactRoot.transform);
                senderObj.transform.localPosition = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 2f),
                    Random.Range(-1f, 1f)
                );

                var sender = senderObj.AddComponent<VRCContactSender>();
                sender.shapeType = VRCContactSender.ShapeType.Capsule;
                sender.radius = Random.Range(0.5f, 1.5f);
                sender.height = Random.Range(1f, 3f);
                sender.collisionTags = tags.GetRange(0, Random.Range(3, 10));
                sender.localOnly = true;
            }

            // 创建Receiver
            for (int i = 0; i < halfCount; i++)
            {
                var receiverObj = new GameObject($"ExtendedReceiver_{i}");
                receiverObj.transform.SetParent(contactRoot.transform);
                receiverObj.transform.localPosition = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0f, 2f),
                    Random.Range(-1f, 1f)
                );

                var receiver = receiverObj.AddComponent<VRCContactReceiver>();
                receiver.shapeType = VRCContactReceiver.ShapeType.Capsule;
                receiver.radius = Random.Range(0.5f, 1.5f);
                receiver.height = Random.Range(1f, 3f);
                receiver.collisionTags = tags.GetRange(0, Random.Range(3, 10));
                receiver.localOnly = true;
            }

            Debug.Log($"[ASS] 创建扩展Contact系统: {halfCount} senders + {halfCount} receivers, 10个标签");
        }

        /// <summary>
        /// 创建多层透明Overdraw堆叠（扩展功能）
        /// </summary>
        private static void CreateExtendedOverdrawLayers(GameObject root, int layerCount, int groupCount)
        {
            for (int g = 0; g < groupCount; g++)
            {
                var overdrawRoot = new GameObject($"ExtendedOverdrawLayers_{g}");
                overdrawRoot.transform.SetParent(root.transform);
                overdrawRoot.transform.localPosition = new Vector3(g * 3f, 0, 0);

                var material = new Material(Shader.Find("Unlit/Transparent"));
                material.color = new Color(1, 1, 1, 0.3f);
                material.renderQueue = 3000;

                for (int i = 0; i < layerCount; i++)
                {
                    var layerObj = new GameObject($"Layer_{i}");
                    layerObj.transform.SetParent(overdrawRoot.transform);
                    layerObj.transform.localPosition = new Vector3(0, 0, i * 0.001f);
                    layerObj.transform.localScale = new Vector3(2f, 2f, 2f);

                    var meshFilter = layerObj.AddComponent<MeshFilter>();
                    meshFilter.mesh = GetSharedQuadMesh(2f);

                    var meshRenderer = layerObj.AddComponent<MeshRenderer>();
                    meshRenderer.material = material;
                }

                Debug.Log($"[ASS] 创建扩展Overdraw层组 {g}: {layerCount}层");
            }
        }

        /// <summary>
        /// 创建防御Shader材质（统一方法）
        /// 合并了CreateHeavyShaderMaterial和CreateExpensiveMaterials的功能
        /// 支持创建单个或多个材质，支持固定或随机参数
        /// </summary>
        /// <param name="avatarRoot">Avatar根对象</param>
        /// <param name="materialCount">要创建的材质数量（1=单个材质）</param>
        /// <param name="useRandomParameters">是否使用随机参数</param>
        /// <param name="fixedLoopCount">固定循环次数（仅在useRandomParameters=false时使用）</param>
        /// <returns>创建的材质数组</returns>
        private static Material[] CreateDefenseMaterials(GameObject avatarRoot, int materialCount, bool useRandomParameters = true, int shaderLoopCount = 1000000)
        {
            var shader = CreateDefenseShader(avatarRoot);
            
            // 防御性检查：如果shader为null，跳过创建材质
            if (shader == null)
            {
                Debug.LogWarning("[ASS] 无法创建防御Shader，跳过材质创建");
                return new Material[0];
            }
            
            var materials = new Material[materialCount];
            
            for (int i = 0; i < materialCount; i++)
            {
                var material = new Material(shader);
                material.name = $"ASS_DefenseMaterial_{i}";
                
                if (useRandomParameters)
                {
                    // 随机参数配置 - 大幅降低上限以避免GPU/Shader编译崩溃
                    // 这些值在Shader中会被进一步限制（如LoopCount限制为256）
                    material.SetInt("_LoopCount", Random.Range(100, 1000));
                    material.SetFloat("_Intensity", Random.Range(10f, 100f));
                    material.SetFloat("_Complexity", Random.Range(100f, 1000f));
                    material.SetFloat("_ParallaxScale", Random.Range(1f, 10f));
                    material.SetFloat("_NoiseOctaves", Random.Range(4f, 16f));
                    material.SetFloat("_SamplingRate", Random.Range(8f, 32f));
                    material.SetFloat("_ColorPasses", Random.Range(5f, 20f));
                    material.SetFloat("_LightCount", Random.Range(2f, 8f));
                    material.SetFloat("_RayMarchSteps", Random.Range(4f, 16f));
                    material.SetFloat("_SubsurfaceScattering", Random.Range(1f, 8f));
                    material.SetFloat("_FractalIterations", Random.Range(8f, 32f));
                    material.SetFloat("_VolumetricSteps", Random.Range(4f, 16f));
                    material.SetFloat("_ParticleDensity", Random.Range(100f, 500f));
                    material.SetFloat("_GlobalIllumination", Random.Range(2f, 8f));
                    material.SetFloat("_CausticSamples", Random.Range(4f, 16f));
                    material.SetFloat("_ReflectionSamples", Random.Range(4f, 16f));
                    material.SetFloat("_ShadowSamples", Random.Range(4f, 8f));
                    material.SetFloat("_ParallaxIterations", Random.Range(4f, 16f));
                    material.SetFloat("_Turbulence", Random.Range(50f, 200f));
                    material.SetFloat("_CloudLayers", Random.Range(2f, 8f));
                    material.SetFloat("_MotionBlurStrength", Random.Range(0.1f, 0.5f));
                    material.SetFloat("_DepthOfFieldStrength", Random.Range(0.1f, 0.5f));
                    material.SetFloat("_ChromaticAberration", Random.Range(0.01f, 0.1f));
                    material.SetFloat("_LensFlareIntensity", Random.Range(1f, 5f));
                    material.SetFloat("_GrainStrength", Random.Range(0.1f, 0.5f));
                    material.SetFloat("_VignetteStrength", Random.Range(0.1f, 0.5f));
                    material.SetFloat("_MoireIntensity", Random.Range(0.1f, 1f));
                    material.SetFloat("_DitherStrength", Random.Range(0.1f, 0.5f));
                    material.SetFloat("_HologramIntensity", Random.Range(0.1f, 1f));
                    material.SetFloat("_Iridescence", Random.Range(0.1f, 1f));
                    material.SetFloat("_VelvetIntensity", Random.Range(0.1f, 1f));
                    material.SetFloat("_FresnelPower", Random.Range(1f, 5f));
                    material.SetFloat("_BumpMapStrength", Random.Range(0.1f, 1f));
                    material.SetFloat("_HeightMapStrength", Random.Range(0.1f, 1f));
                    material.SetFloat("_ParallaxIntensity", Random.Range(0.1f, 1f));
                    material.SetFloat("_DistortionStrength", Random.Range(0.1f, 2f));
                }
                else
                {
                    // 固定参数配置 - 大幅降低上限以避免GPU/Shader编译崩溃
                    // 使用更保守的值，确保在VRChat环境中稳定运行
                    // 这些值在Shader中会被进一步限制
                    material.SetInt("_LoopCount", Mathf.Clamp(shaderLoopCount, 0, 1000));
                    material.SetFloat("_Intensity", 50f);
                    material.SetFloat("_Complexity", 500f);
                    material.SetFloat("_ParallaxScale", 5f);
                    material.SetFloat("_NoiseOctaves", 8f);
                    material.SetFloat("_SamplingRate", 16f);
                    material.SetFloat("_ColorPasses", 10f);
                    material.SetFloat("_LightCount", 4f);
                    material.SetFloat("_RayMarchSteps", 8f);
                    material.SetFloat("_SubsurfaceScattering", 4f);
                    material.SetFloat("_FractalIterations", 16f);
                    material.SetFloat("_VolumetricSteps", 8f);
                    material.SetFloat("_ParticleDensity", 250f);
                    material.SetFloat("_GlobalIllumination", 4f);
                    material.SetFloat("_CausticSamples", 8f);
                    material.SetFloat("_ReflectionSamples", 8f);
                    material.SetFloat("_ShadowSamples", 4f);
                    material.SetFloat("_ParallaxIterations", 8f);
                    material.SetFloat("_Turbulence", 100f);
                    material.SetFloat("_CloudLayers", 4f);
                    material.SetFloat("_MotionBlurStrength", 0.3f);
                    material.SetFloat("_DepthOfFieldStrength", 0.3f);
                    material.SetFloat("_ChromaticAberration", 0.05f);
                    material.SetFloat("_LensFlareIntensity", 2f);
                    material.SetFloat("_GrainStrength", 0.3f);
                    material.SetFloat("_VignetteStrength", 0.3f);
                    material.SetFloat("_MoireIntensity", 0.5f);
                    material.SetFloat("_DitherStrength", 0.3f);
                    material.SetFloat("_HologramIntensity", 0.5f);
                    material.SetFloat("_Iridescence", 0.5f);
                    material.SetFloat("_VelvetIntensity", 0.5f);
                    material.SetFloat("_FresnelPower", 2f);
                    material.SetFloat("_BumpMapStrength", 0.5f);
                    material.SetFloat("_HeightMapStrength", 0.5f);
                    material.SetFloat("_ParallaxIntensity", 0.5f);
                    material.SetFloat("_DistortionStrength", 1f);
                }
                
                // 设置基本材质参数
                material.SetFloat("_Glossiness", Random.Range(0.3f, 1f));
                material.SetFloat("_Metallic", Random.Range(0.3f, 1f));
                material.SetFloat("_OcclusionStrength", Random.Range(0.5f, 5f));
                material.SetFloat("_EmissionIntensity", Random.Range(0.5f, 5f));
                material.SetFloat("_RimPower", Random.Range(2f, 16f));
                material.SetFloat("_DetailScale", Random.Range(5f, 50f));
                material.SetFloat("_NoiseScale", Random.Range(3f, 25f));
                material.SetFloat("_RefractionStrength", Random.Range(0.05f, 1f));
                material.SetFloat("_DispersionStrength", Random.Range(0.02f, 0.5f));
                material.SetFloat("_Anisotropy", Random.Range(0f, 1f));
                material.SetFloat("_ClearCoat", Random.Range(0f, 1f));
                material.SetFloat("_Sheen", Random.Range(0f, 1f));
                material.SetFloat("_Thickness", Random.Range(0.3f, 2f));
                material.SetFloat("_Transmission", Random.Range(0f, 1f));
                material.SetFloat("_Absorption", Random.Range(0f, 1f));
                
                // 设置颜色
                material.SetColor("_SubsurfaceColor", new Color(
                    Random.Range(0.5f, 1f),
                    Random.Range(0.2f, 0.6f),
                    Random.Range(0.2f, 0.6f),
                    1f
                ));
                material.SetColor("_BaseColor", new Color(
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f),
                    1f
                ));
                
                // 设置纹理（使用白色纹理作为默认）
                Texture2D defaultWhite = Texture2D.whiteTexture;
                material.SetTexture("_MainTex", defaultWhite);
                material.SetTexture("_NormalMap", defaultWhite);
                material.SetTexture("_HeightMap", defaultWhite);
                material.SetTexture("_DetailTex", defaultWhite);
                material.SetTexture("_NoiseTex", defaultWhite);
                
                materials[i] = material;
            }
            
            Debug.Log($"[ASS] 创建防御Shader材质: {materialCount}个，使用{(useRandomParameters ? "随机" : "固定")}参数");
            return materials;
        }
        
        /// <summary>
        /// 创建高消耗的Shader材质球及网格（使用统一方法）
        /// </summary>
        private static void CreateExpensiveMaterials(GameObject root, GameObject avatarRoot, int materialCount, int shaderLoopCount)
        {
            // 防御性检查：确保materialCount有效
            if (materialCount <= 0)
            {
                Debug.Log("[ASS] materialCount为0，跳过创建材质");
                return;
            }

            // 限制材质数量以避免GPU过载
            materialCount = Mathf.Min(materialCount, 3);
            if (materialCount < 1) materialCount = 1;

            GameObject materialRoot = null;
            List<GameObject> createdMeshes = new List<GameObject>();
            Material[] materials = null;

            try
            {
                materialRoot = new GameObject("ExpensiveMaterials");
                materialRoot.transform.SetParent(root.transform);

                // 使用统一方法创建材质
                materials = CreateDefenseMaterials(avatarRoot, materialCount, true, shaderLoopCount);

                // 防御性检查：确保材质数组不为空
                if (materials == null || materials.Length == 0)
                {
                    Debug.LogWarning("[ASS] 未能创建任何防御材质，跳过网格创建");
                    return;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    // 防御性检查：确保材质不为null
                    var material = materials[i];
                    if (material == null)
                    {
                        Debug.LogWarning($"[ASS] 材质 {i} 为null，跳过创建对应网格");
                        continue;
                    }

                    // 创建网格使用这个材质
                    var meshObj = new GameObject($"ASS_DefenseMesh_{i}");
                    meshObj.transform.SetParent(materialRoot.transform);
                    meshObj.transform.localPosition = new Vector3(i * 0.3f, 0, 0);
                    meshObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

                    var meshFilter = meshObj.AddComponent<MeshFilter>();
                    meshFilter.mesh = CreateComplexMesh();

                    var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                    meshRenderer.material = material;

                    createdMeshes.Add(meshObj);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateExpensiveMaterials失败: {e.Message}\n{e.StackTrace}");
                // 清理已创建的对象
                CleanupCreatedObjects(materialRoot, createdMeshes, materials);
            }
        }

        /// <summary>
        /// 清理创建的对象和资源
        /// </summary>
        private static void CleanupCreatedObjects(GameObject root, List<GameObject> meshObjects, Material[] materials)
        {
            try
            {
                // 清理材质
                if (materials != null)
                {
                    foreach (var material in materials)
                    {
                        if (material != null)
                        {
                            Object.DestroyImmediate(material);
                        }
                    }
                }

                // 清理网格对象
                if (meshObjects != null)
                {
                    foreach (var meshObj in meshObjects)
                    {
                        if (meshObj != null)
                        {
                            // 清理网格
                            var meshFilter = meshObj.GetComponent<MeshFilter>();
                            if (meshFilter != null && meshFilter.mesh != null)
                            {
                                Object.DestroyImmediate(meshFilter.mesh);
                            }
                            Object.DestroyImmediate(meshObj);
                        }
                    }
                }

                // 清理根对象
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] 清理资源时出错: {e.Message}");
            }
        }

        /// <summary>
        /// 在构建时获取防御Shader
        /// 优先使用模板Shader，如果不存在则回退到Standard
        /// </summary>
        private static Shader CreateDefenseShader(GameObject avatarRoot)
        {
            // 查找自定义DefenseShader
            Shader defenseShader = null;
            try
            {
                defenseShader = Shader.Find("SeaLoong/DefenseShader");
            }
            catch
            {
                defenseShader = null;
            }
             
            if (defenseShader != null)
            {
                Debug.Log("[ASS] 使用自定义DefenseShader");
                return defenseShader;
            }
             
            // 回退到Standard Shader
            Shader standardShader = null;
            try
            {
                standardShader = Shader.Find("Standard");
            }
            catch
            {
                standardShader = null;
            }
             
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
        /// 创建复杂的网格（高顶点数）
        /// </summary>
        private static Mesh CreateComplexMesh()
        {
            var mesh = new Mesh { name = "ComplexMesh" };

            try
            {
                // 降低网格复杂度以避免GPU过载
                int subdivisions = 10;
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
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateComplexMesh失败: {e.Message}\n{e.StackTrace}");
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                    mesh = null;
                }
            }

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

            // 注意：Shader参数通过材质直接设置，不需要通过动画控制
            // 因为Shader参数在材质创建时已设置，运行时无法通过动画修改

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
        /// 防御Shader的GPU成本分析：
        /// 
        /// ========== GPU 成本分析 ==========
        /// 
        /// 1. 视差映射 (ParallaxMapping) - 640-2560层:
        ///    - 平均采样层数：1600 层
        ///    - 每层采样 _HeightMap 1-2 次
        ///    - 总计：1600+ 次高度贴图采样
        /// 
        /// 2. 主循环计算 (ExpensiveColorComputation) - 可配置循环次数：
        ///    - 循环次数：_LoopCount（默认50万，最多100万）
        ///    - 每次循环包含：
        ///      * tex2D() × 7 次（MainTex×3, NormalMap, HeightMap, DetailTex, NoiseTex）
        ///      * sin/cos/tan 三角函数
        ///      * sqrt/exp/log/pow/atan/sinh/cosh/asin/acos 数学运算
        ///      * normalize() × 2 次
        ///      * dot() × 2 次
        ///    - 单次循环成本：~40+ 条 GPU 指令
        ///    - 总循环成本：_LoopCount × 40 指令
        /// 
        /// 3. FBM 噪声计算 - 128 层:
        ///    - 128 层噪声迭代
        ///    - 每层包含多次 hash 和 lerp 计算
        ///    - GPU 成本：~2560 条指令
        /// 
        /// 4. 复杂法线计算 (UnpackNormalComplex) - 5 次迭代：
        ///    - 5 次迭代
        ///    - 每次包含 normalize + 采样
        ///    - GPU 成本：~150 条指令
        /// 
        /// 5. 光线步进 (Ray Marching) - 64 步：
        ///    - 64 步迭代
        ///    - 每步包含高度采样和距离计算
        ///    - GPU 成本：~320 条指令
        /// 
        /// 6. 次表面散射 (Subsurface Scattering) - 8 次迭代：
        ///    - 8 次迭代
        ///    - 每次包含光照计算
        ///    - GPU 成本：~80 条指令
        /// 
        /// 7. 多光源照明 (CalculateLighting) - 32 伪光源：
        ///    - 主光源：1 次完整 Blinn-Phong 计算
        ///    - 伪光源：32 个循环
        ///    - 总成本：~800 条指令
        /// 
        /// 8. 多次色彩空间转换：
        ///    - RGB↔HSV 转换 × _ColorPasses 次（默认100次）
        ///    - 每次转换 40+ 条指令
        ///    - 总成本：100 × 40 = 4000+ 条指令
        /// 
        /// ========== 总体 GPU 成本计算 ==========
        /// 
        /// 对于配置：_LoopCount = 500000
        /// 
        /// 成本分解：
        /// - 基础操作：200+ 条指令
        /// - 主循环：500000 × 40 = 20,000,000 条指令
        /// - 视差映射：1600 采样 × 20+ 指令 = 32,000 条指令
        /// - FBM 噪声：2560 条指令
        /// - 法线计算：150 条指令
        /// - 光线步进：320 条指令
        /// - 次表面散射：80 条指令
        /// - 光照计算：800 条指令
        /// - 色彩转换：4000 条指令
        /// 
        /// 总 GPU 指令：~20,040,000 条指令
        /// 总纹理采样：
        ///   - 循环中采样：500000 × 7 = 350万次
        ///   - 视差映射：1600 次
        ///   - 法线计算：5 次
        ///   - 总计：~350万次采样
        /// 
        /// 性能影响：
        /// - 简单 GPU（集成显卡）：50-200ms 延迟
        /// - 中等 GPU（GTX 1060）：20-80ms 延迟
        /// - 高端 GPU（RTX 3080）：10-30ms 延迟
        /// - 移动设备：无法实时运行
        /// 
        /// ========== 性能破坏效果 ==========
        /// 单个使用防御Shader的材质就能：
        /// - 降低帧率 50% (1080p 60fps → 30fps)
        /// - 让集成显卡卡到 1fps
        /// - 造成 VRChat 中的明显性能问题
        /// 
        /// 多个材质叠加 (防御系统有 500 个)：
        /// - 理论叠加 GPU 成本：500 × 20M = 100 亿条指令
        /// - 现实效果：完全冻结 GPU
        /// - 实际结果：进入 Avatar 的玩家直接掉帧到 0
        /// </summary>
        private static int ValidateShaderLoops(int requested)
        {
            int maxLoops = Constants.SHADER_LOOP_MAX_COUNT; // 300万循环（大幅增加）
            int validValue = Mathf.Clamp(requested, 0, maxLoops);
            
            if (validValue > 100000)
            {
                Debug.Log($"[ASS] 防御Shader {validValue} - GPU成本分析：" +
                         $"\n  【主循环 - {validValue}次迭代】" +
                         $"\n  • {validValue}次循环 × (7采样 + 40指令) = {validValue * 47}条GPU指令" +
                         $"\n  • Complexity(5000) 乘积：{validValue} × 5000 = {validValue * 5000}次数学运算" +
                         $"\n  【视差映射 - 640-2560层】" +
                         $"\n  • ParallaxScale = 50.0 → 640-2560层陡峭迭代" +
                         $"\n  • 每层 1-2次 HeightMap 采样 = 1600+次纹理采样" +
                         $"\n  【复杂法线计算 - 5层迭代】" +
                         $"\n  • 5层迭代 × (normalize + 采样 + 向量运算) = GPU密集" +
                         $"\n  【光照计算 - 32伪光源】" +
                         $"\n  • 主光 + 32个循环 × (sin/cos/tan + dot + pow + 距离衰减)" +
                         $"\n  • 每个伪光 ~25条指令 = 800条总指令" +
                         $"\n  【FBM噪声 - 128层Perlin】" +
                         $"\n  • 128层Perlin噪声 × 20+指令 = 2560条指令" +
                         $"\n  【光线步进 - 64步】" +
                         $"\n  • 64步迭代 × 5指令 = 320条指令" +
                         $"\n  【次表面散射 - 8次迭代】" +
                         $"\n  • 8次迭代 × 10指令 = 80条指令" +
                         $"\n  【色彩空间转换 - ColorPasses×100】" +
                         $"\n  • RGB→HSV→RGB × 100次迭代" +
                         $"\n  • 每次转换 40+ 条指令 = 4000+ 条指令" +
                         $"\n  【总GPU成本计算】" +
                         $"\n  • 主循环指令：{validValue} × 47 = {validValue * 47}条指令" +
                         $"\n  • 视差采样：1600 × 20 = 32,000条指令" +
                         $"\n  • FBM噪声：2560条指令" +
                         $"\n  • 光线步进：320条指令" +
                         $"\n  • 次表面散射：80条指令" +
                         $"\n  • 色彩转换：4000条指令" +
                         $"\n  • 纹理总采样：{validValue} × 7 = {validValue * 7}次采样" +
                         $"\n  • 总指令数：~{validValue * 47 + 40000}条指令" +
                         $"\n  • 总采样数：~{validValue * 7 + 1600}次采样" +
                         $"\n  【性能影响】" +
                         $"\n  • 集成显卡：50-200ms延迟，可能卡顿" +
                         $"\n  • GTX 1060：20-80ms延迟" +
                         $"\n  • RTX 3080：10-30ms延迟" +
                         $"\n  • 移动设备：无法实时运行");
            }
            return validValue;
        }

        /// <summary>
        /// 验证高多边形顶点数，确保不超过500k
        /// </summary>
        /// <summary>
        /// 验证高多边形顶点数（VRChat无硬限制，仅由文件大小限制）
        /// 分散到多个Mesh避免单Mesh超过65k顶点
        /// 估算：每个顶点约32字节（位置+法线+UV+切线）
        /// 25MB限制下，顶点数据最大约500MB（但实际Unity会有更多开销）
        /// 为安全起见，限制单Avatar总顶点在100万以内
        /// </summary>
        private static int ValidateHighPolyVertices(int requested)
        {
            // VRChat Avatar 文件大小限制约25MB
            // 估算每个顶点需要约32字节（position + normal + uv + tangent + index）
            // 25MB / 32字节 ≈ 80万顶点（仅网格数据）
            // 考虑到Unity开销和安全边际，限制为50万顶点
            int maxVerticesPerAvatar = 500000;
            
            int validValue = Mathf.Clamp(requested, 50000, maxVerticesPerAvatar);
            
            if (requested > maxVerticesPerAvatar)
            {
                Debug.Log($"[ASS] 高多边形顶点数 {requested} 超过安全限制 {maxVerticesPerAvatar}，已调整为 {validValue}");
            }
            
            return validValue;
        }

        /// <summary>
        /// 验证Overdraw层数（无硬限制，但受文件大小限制）
        /// 每个Quad有4个顶点，约64字节数据
        /// 25MB限制下，理论最大约40万层
        /// 为安全起见，限制为5万层
        /// </summary>
        private static int ValidateOverdrawLayers(int requested)
        {
            int maxLayers = 50000;
            int validValue = Mathf.Clamp(requested, 5, maxLayers);
            
            if (requested > maxLayers)
            {
                Debug.Log($"[ASS] Overdraw层数 {requested} 超过安全限制 {maxLayers}，已调整为 {validValue}");
            }
            
            return validValue;
        }

        /// <summary>
        /// 估算防御系统生成的网格总顶点数
        /// 用于确保不超出文件大小限制
        /// </summary>
        private static int EstimateTotalDefenseVertices(int overdrawLayers, int polyVertices, int heavyShaderMeshes)
        {
            // Overdraw: 每层4顶点
            int overdrawVertices = overdrawLayers * 4;
            
            // 高多边形网格: 已分散到多个Mesh
            int polyMeshVertices = polyVertices;
            
            // Heavy Shader: 每个Quad 4顶点
            int shaderVertices = heavyShaderMeshes * 4;
            
            return overdrawVertices + polyMeshVertices + shaderVertices;
        }

        #endregion

        #region Shader Creation

        /// <summary>
        /// 创建高消耗Shader材质（合并后的方法）
        /// 在构建时生成Shader，Shader使用其Properties中定义的极高默认值
        /// 包含所有GPU密集功能，用于创建单个或多个材质
        /// 现在使用统一的CreateDefenseMaterials方法
        /// </summary>
        private static Material CreateDefenseShaderMaterial(GameObject avatarRoot, int loopCount)
        {
            var materials = CreateDefenseMaterials(avatarRoot, 1, false, loopCount);
            // 防御性检查：确保数组不为空
            if (materials == null || materials.Length == 0)
            {
                Debug.LogWarning("[ASS] CreateDefenseShaderMaterial: 未能创建材质，返回null");
                return null;
            }
            return materials[0];
        }

        
        #endregion
    }
}

