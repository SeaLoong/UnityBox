using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 锁定系统生成器
    /// 功能：
    /// 1. 解锁时通过VRC层权重控制禁用其他ASS层
    /// 2. 参数驱动：锁定时设为反转值，解锁时恢复
    /// 3. 材质锁定：锁定时清空材质槽
    /// </summary>
    public class Lock
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly AvatarSecuritySystemComponent config;

        /// <summary>
        /// 存储锁定层的状态引用，用于后续配置层权重控制
        /// </summary>
        public class LockLayerResult
        {
            public AnimatorControllerLayer Layer;
            public AnimatorState LockedState;
            public AnimatorState UnlockedState;
        }

        /// <summary>
        /// Generate() 执行后存储的结果，用于后续配置层权重控制
        /// </summary>
        public LockLayerResult Result { get; private set; }

        private readonly VRCAvatarDescriptor descriptor;

        public Lock(AnimatorController controller, GameObject avatarRoot, AvatarSecuritySystemComponent config, VRCAvatarDescriptor descriptor)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
            this.descriptor = descriptor;
        }

        /// <summary>
        /// 生成锁定层并添加到控制器
        /// 注意：层权重控制需要在所有层添加到控制器后调用 ConfigureLockLayerWeight / LockFxLayerWeights
        /// </summary>
        public void Generate()
        {
            var layer = Utils.CreateLayer(LAYER_LOCK, 1f);

            var lockLayerMask = CreateLockLayerMask();
            if (lockLayerMask != null)
            {
                layer.avatarMask = lockLayerMask;
                Utils.AddSubAsset(controller, lockLayerMask);
            }

            // 创建遮挡 Mesh（必须在创建动画之前，这样动画才能引用到它）
            CreateOcclusionMesh();

            bool useWdOn = ResolveWriteDefaults();

            // Remote 状态：其他玩家看到的默认状态
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, 0, 0));
            remoteState.writeDefaultValues = useWdOn;
            var remoteClip = CreateRemoteClip(useWdOn);
            remoteState.motion = remoteClip;
            Utils.AddSubAsset(controller, remoteClip);
            
            // Locked 状态：本地玩家锁定时
            var lockedState = layer.stateMachine.AddState("Locked", new Vector3(200, 100, 0));
            lockedState.writeDefaultValues = useWdOn;
            
            // Unlocked 状态：本地玩家解锁后
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 200, 0));
            unlockedState.writeDefaultValues = useWdOn;

            layer.stateMachine.defaultState = remoteState;

            var lockClip = CreateLockClip(useWdOn);
            lockedState.motion = lockClip;
            Utils.AddSubAsset(controller, lockClip);

            var unlockClip = CreateUnlockClip(useWdOn);
            unlockedState.motion = unlockClip;
            Utils.AddSubAsset(controller, unlockClip);

            // Remote → Unlocked（密码正确时，所有玩家都进入解锁状态）
            var toUnlockedDirect = Utils.CreateTransition(remoteState, unlockedState);
            toUnlockedDirect.hasExitTime = false;
            toUnlockedDirect.duration = 0f;
            toUnlockedDirect.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            
            // Remote → Locked（仅本地玩家：IsLocal=true 且密码未正确）
            var toLocked = Utils.CreateTransition(remoteState, lockedState);
            toLocked.hasExitTime = false;
            toLocked.duration = 0f;
            toLocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_IS_LOCAL);
            toLocked.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            
            // Locked → Locked（循环锁定，直到密码正确）
            var lockedLoop = Utils.CreateTransition(lockedState, lockedState);
            lockedLoop.hasExitTime = true;
            lockedLoop.exitTime = 0f;
            lockedLoop.duration = 0f;
            lockedLoop.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            
            // Locked → Unlocked（本地玩家密码正确时解锁）
            var toUnlocked = Utils.CreateTransition(lockedState, unlockedState);
            toUnlocked.hasExitTime = false;
            toUnlocked.duration = 0f;
            toUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            
            // Unlocked → Remote（密码被重置时返回初始状态）
            var unlockedToRemote = Utils.CreateTransition(unlockedState, remoteState);
            unlockedToRemote.hasExitTime = false;
            unlockedToRemote.duration = 0f;
            unlockedToRemote.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(T("log.lock_layer_created"));

            Result = new LockLayerResult
            {
                Layer = layer,
                LockedState = lockedState,
                UnlockedState = unlockedState
            };

            controller.AddLayer(layer);
        }

        /// <summary>
        /// 配置锁定层自身权重
        /// Locked 状态：权重=1，Unlocked 状态：权重=0
        /// </summary>
        public void ConfigureLockLayerWeight()
        {
            if (Result?.Layer == null) return;

            int lockLayerIndex = -1;
            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (controller.layers[i].name == LAYER_LOCK)
                {
                    lockLayerIndex = i;
                    break;
                }
            }

            if (lockLayerIndex < 0)
            {
                Debug.LogWarning("[ASS] 锁定层权重控制：未找到ASS_Lock层");
                return;
            }

            Utils.AddLayerControlBehaviour(Result.LockedState, lockLayerIndex, goalWeight: 1f, blendDuration: 0f);
            Utils.AddLayerControlBehaviour(Result.UnlockedState, lockLayerIndex, goalWeight: 0f, blendDuration: 0f);
        }

        /// <summary>
        /// 锁定 FX 层权重
        /// Locked 状态下将所有非 ASS 层权重设为 0，Unlocked 状态下设为 1
        /// </summary>
        public void LockFxLayerWeights(string[] assLayerNames)
        {
            if (Result?.Layer == null) return;

            var assLayerSet = new HashSet<string>(assLayerNames);
            var nonAssLayerIndices = new List<int>();

            for (int i = 0; i < controller.layers.Length; i++)
            {
                string layerName = controller.layers[i].name;
                if (assLayerSet.Contains(layerName))
                    continue;

                nonAssLayerIndices.Add(i);
                Debug.Log($"[ASS] FX层锁定：添加非ASS层 '{layerName}' (索引 {i})");
            }

            if (nonAssLayerIndices.Count > 0)
            {
                Utils.AddMultiLayerControlBehaviour(
                    Result.LockedState, nonAssLayerIndices.ToArray(),
                    goalWeight: 0f, blendDuration: 0f);
                Utils.AddMultiLayerControlBehaviour(
                    Result.UnlockedState, nonAssLayerIndices.ToArray(),
                    goalWeight: 1f, blendDuration: 0f);

                Debug.Log($"[ASS] 已配置FX层锁定：{nonAssLayerIndices.Count} 个非ASS层");
            }
            else
            {
                Debug.LogWarning("[ASS] FX层锁定：未找到非ASS层");
            }
        }

        #region Private Methods

        private AvatarMask CreateLockLayerMask()
        {
            if (avatarRoot == null) return null;

            var mask = new AvatarMask { name = "ASS_LockLayerMask" };
            mask.AddTransformPath(avatarRoot.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
                mask.SetTransformActive(i, false);

            var allowedPaths = new HashSet<string>();

            AddAllowedPath(allowedPaths, GO_UI);
            AddAllowedPath(allowedPaths, GO_OCCLUSION_MESH);
            AddAllowedPath(allowedPaths, GO_DEFENSE_ROOT);
            AddAllowedPath(allowedPaths, GO_AUDIO_WARNING);
            AddAllowedPath(allowedPaths, GO_AUDIO_SUCCESS);

            if (config.disableRootChildren)
            {
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child))
                        continue;
                    allowedPaths.Add(Utils.GetRelativePath(avatarRoot, child.gameObject));
                }
            }

            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                if (string.IsNullOrEmpty(path) || allowedPaths.Contains(path))
                    mask.SetTransformActive(i, true);
            }

            return mask;
        }

        private void AddAllowedPath(HashSet<string> allowedPaths, string objectName)
        {
            var t = avatarRoot.transform.Find(objectName);
            if (t != null)
                allowedPaths.Add(Utils.GetRelativePath(avatarRoot, t.gameObject));
        }

        private AnimationClip CreateLockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Lock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            
            Debug.Log($"[ASS] 创建锁定动画 (WD {(useWdOn ? "On" : "Off")} 模式)");

            if (avatarRoot.transform.Find(GO_UI) != null)
            {
                clip.SetCurve(GO_UI, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 锁定动画：启用 UI Canvas");
            }
            
            if (avatarRoot.transform.Find(GO_OCCLUSION_MESH) != null)
            {
                clip.SetCurve(GO_OCCLUSION_MESH, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 锁定动画：显示遮挡 Mesh");
            }
            
            if (avatarRoot.transform.Find(GO_AUDIO_WARNING) != null)
                clip.SetCurve(GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);

            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            
            if (avatarRoot.transform.Find(GO_DEFENSE_ROOT) != null)
                clip.SetCurve(GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive", disableCurve);

            if (config.disableRootChildren)
            {
                int hiddenCount = 0;
                
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child)) continue;
                    
                    clip.SetCurve(child.name, typeof(GameObject), "m_IsActive", disableCurve);
                    Debug.Log($"[ASS] 锁定动画: \"{child.name}\" (IsActive=0)");
                    hiddenCount++;
                }
                
                Debug.Log($"[ASS] 锁定动画：隐藏 {hiddenCount} 个根子对象");
            }

            return clip;
        }

        private AnimationClip CreateRemoteClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Remote" };
            
            SetGameObjectActiveInClip(clip, GO_OCCLUSION_MESH, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            
            // WD Off: 显式恢复所有被修改的属性
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);
            
            Debug.Log($"[ASS] 创建 Remote 状态动画 (WD {(useWdOn ? "On" : "Off")})：隐藏遮挡 Mesh 和防御对象");
            return clip;
        }

        private AnimationClip CreateUnlockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Unlock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);

            Debug.Log($"[ASS] 创建解锁动画 (WD {(useWdOn ? "On" : "Off")} 模式)");

            SetGameObjectActiveInClip(clip, GO_UI, false);
            SetGameObjectActiveInClip(clip, GO_OCCLUSION_MESH, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            
            if (avatarRoot.transform.Find(GO_AUDIO_WARNING) != null)
                clip.SetCurve(GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);

            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            
            // WD On: 不显式恢复，由 WD 自动恢复默认值，避免破坏其他插件
            // WD Off: 显式写回所有被修改属性的原始值
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);

            Debug.Log(T("log.lock_unlock_animation_created"));
            return clip;
        }
        
        /// <summary>
        /// WD Off 模式：显式写回所有被修改属性的原始值
        /// 所有根子对象: 恢复 m_IsActive=1
        /// </summary>
        private void WriteRestoreValues(AnimationClip clip)
        {
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            int restoredCount = 0;
            
            foreach (Transform child in avatarRoot.transform)
            {
                if (IsASSObject(child)) continue;
                
                clip.SetCurve(child.name, typeof(GameObject), "m_IsActive", enableCurve);
                restoredCount++;
            }
            
            Debug.Log($"[ASS] WD Off 恢复：{restoredCount} 个根子对象 IsActive=1");
        }

        /// <summary>
        /// 解析 WD 模式：Auto 时参照 VRCFury / Modular Avatar 方案检测已有控制器的 WD 设置
        /// 规则：
        /// 1. 跳过 isDefault 的 Playable Layer（未自定义，无分配控制器）
        /// 2. 跳过 animatorController 为 null 的层（参考 Av3 Manager）
        /// 3. 跳过 VRChat 内置控制器（名称以 vrc_ 开头）
        /// 4. 跳过 ASS 自己生成的层（ASS_ 前缀）
        /// 5. 跳过 Additive 层（必须始终 WD On）
        /// 6. 跳过 Direct BlendTree 单状态层（必须始终 WD On，参考 Modular Avatar IsWriteDefaultsRequiredLayer）
        /// 7. 跳过没有 Motion 的空状态（不含有效动画，WD 设置无意义）
        /// 8. 只要存在任何 WD Off 状态就使用 WD Off
        /// 9. 全部为 WD On（或无有效状态）才使用 WD On
        /// </summary>
        private bool ResolveWriteDefaults()
        {
            if (config.writeDefaultsMode == AvatarSecuritySystemComponent.WriteDefaultsMode.On)
                return true;
            if (config.writeDefaultsMode == AvatarSecuritySystemComponent.WriteDefaultsMode.Off)
                return false;
            
            // 收集所有 Playable Layer 的 AnimatorController（去重）
            var controllers = new HashSet<AnimatorController>();
            if (descriptor != null)
            {
                foreach (var animLayer in descriptor.baseAnimationLayers
                    .Concat(descriptor.specialAnimationLayers))
                {
                    if (animLayer.isDefault) continue;
                    if (!(animLayer.animatorController is AnimatorController ac) || ac == null) continue;
                    // 跳过 VRChat 内置控制器（vrc_AvatarV3*）
                    if (ac.name.StartsWith("vrc_")) continue;
                    controllers.Add(ac);
                }
            }
            controllers.Add(controller);
            
            bool hasWdOff = false;
            
            foreach (var ac in controllers)
            {
                foreach (var layer in ac.layers)
                {
                    if (layer.name.StartsWith("ASS_")) continue;
                    if (layer.blendingMode == AnimatorLayerBlendingMode.Additive) continue;
                    if (layer.stateMachine == null) continue;
                    if (IsWriteDefaultsRequiredLayer(layer)) continue;
                    
                    if (HasWdOffState(layer.stateMachine))
                    {
                        hasWdOff = true;
                        break;
                    }
                }
                if (hasWdOff) break;
            }
            
            bool useWdOn = !hasWdOff;
            Debug.Log($"[ASS] WD Auto → {(useWdOn ? "On" : "Off")}");
            return useWdOn;
        }

        /// <summary>
        /// 判断一个层是否必须始终 WD On（参考 Modular Avatar IsWriteDefaultsRequiredLayer）
        /// 条件：Additive 层，或单状态无转换且 Motion 为 Direct BlendTree 的层
        /// </summary>
        private static bool IsWriteDefaultsRequiredLayer(AnimatorControllerLayer layer)
        {
            if (layer.blendingMode == AnimatorLayerBlendingMode.Additive) return true;
            
            var sm = layer.stateMachine;
            if (sm == null) return false;
            if (sm.stateMachines.Length != 0) return false;
            if (sm.states.Length != 1) return false;
            if (sm.anyStateTransitions.Length != 0) return false;
            
            var defaultState = sm.defaultState;
            if (defaultState == null) return false;
            if (defaultState.transitions.Length != 0) return false;
            if (!(defaultState.motion is BlendTree bt)) return false;
            
            return HasDirectBlendTree(bt);
        }

        /// <summary>
        /// 递归检查 BlendTree 中是否有 Direct 类型
        /// </summary>
        private static bool HasDirectBlendTree(BlendTree bt)
        {
            if (bt.blendType == BlendTreeType.Direct) return true;
            foreach (var child in bt.children)
            {
                if (child.motion is BlendTree childBt && HasDirectBlendTree(childBt))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 递归扫描状态机，检查是否存在 WD Off 状态
        /// 跳过无 Motion 的空状态和 Direct BlendTree 状态
        /// </summary>
        private static bool HasWdOffState(AnimatorStateMachine sm)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.motion == null) continue;
                if (state.motion is BlendTree bt && bt.blendType == BlendTreeType.Direct)
                    continue;
                if (!state.writeDefaultValues)
                    return true;
            }
            
            foreach (var childSm in sm.stateMachines)
            {
                if (HasWdOffState(childSm.stateMachine))
                    return true;
            }
            
            return false;
        }

        private void CreateOcclusionMesh()
        {
            var existing = avatarRoot.transform.Find(GO_OCCLUSION_MESH);
            if (existing != null)
                Object.Destroy(existing.gameObject);
            
            var meshObj = new GameObject(GO_OCCLUSION_MESH);
            meshObj.transform.SetParent(avatarRoot.transform, false);
            
            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateOcclusionQuad();
            
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader);
            material.color = Color.white;
            meshRenderer.sharedMaterial = material;
            
            var animator = avatarRoot.GetComponent<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null)
            {
                var constraint = meshObj.AddComponent<VRCParentConstraint>();
                constraint.Sources.Add(new VRCConstraintSource
                {
                    Weight = 1f,
                    SourceTransform = head,
                    ParentPositionOffset = new Vector3(0f, 0f, 0.18f),
                    ParentRotationOffset = Vector3.zero
                });
                
                var constraintSer = new SerializedObject(constraint);
                constraintSer.FindProperty("IsActive").boolValue = true;
                constraintSer.FindProperty("Locked").boolValue = true;
                constraintSer.ApplyModifiedPropertiesWithoutUndo();
            }
            
            meshObj.SetActive(false);
            Debug.Log($"[ASS] 创建遮挡 Mesh：四边形平面，大小=0.5x0.5，绑定到头部");
        }

        private bool IsASSObject(Transform obj)
        {
            var assObjectNames = new HashSet<string>
            {
                GO_ASS_ROOT, GO_UI, GO_AUDIO_WARNING,
                GO_AUDIO_SUCCESS, GO_PARTICLES, GO_DEFENSE_ROOT,
                GO_OCCLUSION_MESH
            };
            
            Transform current = obj;
            while (current != null && current != avatarRoot.transform)
            {
                if (assObjectNames.Contains(current.name))
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static void SetGameObjectActiveInClip(AnimationClip clip, string objectPath, bool isActive)
        {
            var curve = AnimationCurve.Constant(0f, 1f / 60f, isActive ? 1f : 0f);
            clip.SetCurve(objectPath, typeof(GameObject), "m_IsActive", curve);
        }

        private static Mesh CreateOcclusionQuad()
        {
            var mesh = new Mesh { name = "OcclusionQuad" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
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

        #endregion
    }
}
