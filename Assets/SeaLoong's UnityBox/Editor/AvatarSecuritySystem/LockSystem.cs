using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimationClipGenerator;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;
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
    public static class LockSystem
    {
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
        /// 创建初始锁定层
        /// 注意：层权重控制需要在所有层添加到控制器后再配置
        /// </summary>
        public static LockLayerResult CreateLockLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_LOCK, 1f);

            // 为锁定层创建 Transform Mask，避免覆盖其他层的 Transform 动画
            var lockLayerMask = CreateLockLayerMask(avatarRoot, config);
            if (lockLayerMask != null)
            {
                layer.avatarMask = lockLayerMask;
                AnimatorUtils.AddSubAsset(controller, lockLayerMask);
            }

            // 创建遮挡 Mesh（基本功能，无论防护等级都需要）
            // 必须在创建动画之前创建，这样动画才能引用到它
            var occlusionMesh = CreateOcclusionMesh(avatarRoot, config);

            // 根据配置决定 Write Defaults 模式
            bool useWdOn = config.writeDefaultsMode == AvatarSecuritySystemComponent.WriteDefaultsMode.On;

            // 创建状态
            // Remote 状态：其他玩家看到的默认状态（遮挡 Mesh 隐藏，Avatar 正常显示）
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, 0, 0));
            remoteState.writeDefaultValues = useWdOn;
            var remoteClip = CreateRemoteClip(avatarRoot, config, useWdOn);
            remoteState.motion = remoteClip;
            AnimatorUtils.AddSubAsset(controller, remoteClip);
            
            // Locked 状态：本地玩家锁定时看到的状态（遮挡 Mesh 显示，Avatar 隐藏）
            var lockedState = layer.stateMachine.AddState("Locked", new Vector3(200, 100, 0));
            lockedState.writeDefaultValues = useWdOn;
            
            // Unlocked 状态：本地玩家解锁后看到的状态（遮挡 Mesh 隐藏，UI 隐藏）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 200, 0));
            unlockedState.writeDefaultValues = useWdOn;

            layer.stateMachine.defaultState = remoteState;

            // 生成锁定动画（显示遮挡 Mesh + 可选隐藏 Renderer）
            var lockClip = CreateLockClip(avatarRoot, config, useWdOn);
            lockedState.motion = lockClip;
            AnimatorUtils.AddSubAsset(controller, lockClip);

            // 生成解锁动画（隐藏UI + 隐藏遮挡Mesh）
            // WD Off 模式下需要显式写入恢复值
            var unlockClip = CreateUnlockClip(avatarRoot, config, useWdOn);
            unlockedState.motion = unlockClip;
            AnimatorUtils.AddSubAsset(controller, unlockClip);

            // 转换：Remote → Unlocked（密码正确时，所有玩家都进入解锁状态，实现同步）
            var toUnlockedDirect = AnimatorUtils.CreateTransition(remoteState, unlockedState);
            toUnlockedDirect.hasExitTime = false;
            toUnlockedDirect.duration = 0f;
            toUnlockedDirect.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_PASSWORD_CORRECT);
            
            // 转换：Remote → Locked（仅本地玩家：IsLocal=true 且密码未正确时进入锁定状态）
            var toLocked = AnimatorUtils.CreateTransition(remoteState, lockedState);
            toLocked.hasExitTime = false;
            toLocked.duration = 0f;
            toLocked.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toLocked.AddCondition(AnimatorConditionMode.IfNot, 0, Constants.PARAM_PASSWORD_CORRECT);
            
            // 转换：Locked → Locked（循环锁定，直到密码正确）
            var lockedLoop = AnimatorUtils.CreateTransition(lockedState, lockedState);
            lockedLoop.hasExitTime = true;
            lockedLoop.exitTime = 0f;
            lockedLoop.duration = 0f;
            lockedLoop.AddCondition(AnimatorConditionMode.IfNot, 0, Constants.PARAM_PASSWORD_CORRECT);
            
            // 转换：Locked → Unlocked（本地玩家密码正确时解锁）
            var toUnlocked = AnimatorUtils.CreateTransition(lockedState, unlockedState);
            toUnlocked.hasExitTime = false;
            toUnlocked.duration = 0f;
            toUnlocked.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_PASSWORD_CORRECT);
            
            // 转换：Unlocked → Remote（密码被重置时，所有玩家返回初始状态）
            var unlockedToRemote = AnimatorUtils.CreateTransition(unlockedState, remoteState);
            unlockedToRemote.hasExitTime = false;
            unlockedToRemote.duration = 0f;
            unlockedToRemote.AddCondition(AnimatorConditionMode.IfNot, 0, Constants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(I18n.T("log.lock_layer_created"));

            return new LockLayerResult
            {
                Layer = layer,
                LockedState = lockedState,
                UnlockedState = unlockedState
            };
        }

        /// <summary>
        /// 创建锁定层使用的 Transform Mask
        /// 仅启用被 ASS 锁定层动画影响的 Transform，避免覆盖其他层
        /// </summary>
        private static AvatarMask CreateLockLayerMask(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (avatarRoot == null)
                return null;

            var mask = new AvatarMask { name = "ASS_LockLayerMask" };

            // 添加完整层级，随后禁用全部再按需启用
            mask.AddTransformPath(avatarRoot.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                mask.SetTransformActive(i, false);
            }

            var allowedPaths = new HashSet<string>();

            // ASS 自己控制的对象
            AddAllowedPath(allowedPaths, avatarRoot, Constants.GO_UI_CANVAS);
            AddAllowedPath(allowedPaths, avatarRoot, Constants.GO_OCCLUSION_MESH);
            AddAllowedPath(allowedPaths, avatarRoot, Constants.GO_DEFENSE_ROOT);
            AddAllowedPath(allowedPaths, avatarRoot, Constants.GO_AUDIO_WARNING);
            AddAllowedPath(allowedPaths, avatarRoot, Constants.GO_AUDIO_SUCCESS);

            // 锁定时缩放的根子对象
            if (config.disableRootChildren)
            {
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child, avatarRoot.transform))
                        continue;

                    allowedPaths.Add(AnimatorUtils.GetRelativePath(avatarRoot, child.gameObject));
                }
            }

            // 启用根节点与允许路径
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                if (string.IsNullOrEmpty(path))
                {
                    mask.SetTransformActive(i, true);
                    continue;
                }

                if (allowedPaths.Contains(path))
                {
                    mask.SetTransformActive(i, true);
                }
            }

            return mask;
        }

        private static void AddAllowedPath(HashSet<string> allowedPaths, GameObject avatarRoot, string objectName)
        {
            var t = avatarRoot.transform.Find(objectName);
            if (t != null)
            {
                allowedPaths.Add(AnimatorUtils.GetRelativePath(avatarRoot, t.gameObject));
            }
        }

        /// <summary>
        /// 配置锁定层自身权重
        /// Locked 状态：权重=1（生效）
        /// Unlocked 状态：权重=0（释放Transform影响，避免阻断恢复）
        /// </summary>
        public static void ConfigureLockLayerWeight(
            AnimatorController controller,
            LockLayerResult lockResult)
        {
            if (controller == null || lockResult?.Layer == null)
                return;

            int lockLayerIndex = -1;
            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (controller.layers[i].name == Constants.LAYER_LOCK)
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

            AnimatorUtils.AddLayerControlBehaviour(lockResult.LockedState, lockLayerIndex, goalWeight: 1f, blendDuration: 0f);
            AnimatorUtils.AddLayerControlBehaviour(lockResult.UnlockedState, lockLayerIndex, goalWeight: 0f, blendDuration: 0f);
        }

        /// <summary>
        /// 锁定 FX 层权重
        /// 在 Locked 状态下将所有非 ASS 层的权重设为 0，在 Unlocked 状态下设为 1
        /// </summary>
        public static void LockFxLayerWeights(
            AnimatorController controller,
            LockLayerResult lockResult,
            string[] assLayerNames)
        {
            var assLayerSet = new HashSet<string>(assLayerNames);
            var nonAssLayerIndices = new List<int>();

            for (int i = 0; i < controller.layers.Length; i++)
            {
                string layerName = controller.layers[i].name;
                // 排除 ASS 层（包括锁定层）
                if (assLayerSet.Contains(layerName))
                    continue;

                nonAssLayerIndices.Add(i);
                Debug.Log($"[ASS] FX层锁定：添加非ASS层 '{layerName}' (索引 {i})");
            }

            if (nonAssLayerIndices.Count > 0)
            {
                ApplyLayerWeightControl(lockResult, nonAssLayerIndices.ToArray());
                Debug.Log($"[ASS] 已配置FX层锁定：{nonAssLayerIndices.Count} 个非ASS层");
            }
            else
            {
                Debug.LogWarning("[ASS] FX层锁定：未找到非ASS层");
            }
        }

        /// <summary>
        /// 创建锁定动画
        /// 始终显示遮挡 Mesh
        /// 如果启用 disableRootChildren，会隐藏：
        /// 1. Avatar Root 的直接子对象（非 ASS 对象）
        /// 2. 所有 Renderer 对象（用于隐藏骨骼上的 Mesh，因为 Humanoid Rig 骨骼无法被动画控制）
        /// </summary>
        /// <param name="avatarRoot">Avatar 根对象</param>
        /// <param name="config">ASS 配置</param>
        /// <param name="useWdOn">true = WD On 模式, false = WD Off 模式</param>
        private static AnimationClip CreateLockClip(GameObject avatarRoot, AvatarSecuritySystemComponent config, bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Lock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            
            Debug.Log($"[ASS] 创建锁定动画 (WD {(useWdOn ? "On" : "Off")} 模式)");

            // 本地锁定状态：启用 UI Canvas 和遮挡 Mesh
            Transform uiCanvas = avatarRoot.transform.Find(Constants.GO_UI_CANVAS);
            if (uiCanvas != null)
            {
                clip.SetCurve(Constants.GO_UI_CANVAS, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 锁定动画：启用 UI Canvas");
            }
            
            // 显示遮挡 Mesh（基本功能，始终执行）
            // 使用 m_IsActive 控制，因为这是 ASS 完全控制的对象
            Transform occlusionMesh = avatarRoot.transform.Find(Constants.GO_OCCLUSION_MESH);
            if (occlusionMesh != null)
            {
                clip.SetCurve(Constants.GO_OCCLUSION_MESH, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 锁定动画：显示遮挡 Mesh");
            }
            
            // 启用音效对象（Local时可以接收反馈，具体是否播放由其他层控制）
            Transform warningAudio = avatarRoot.transform.Find(Constants.GO_AUDIO_WARNING);
            if (warningAudio != null)
            {
                clip.SetCurve(Constants.GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);
            }

            Transform successAudio = avatarRoot.transform.Find(Constants.GO_AUDIO_SUCCESS);
            if (successAudio != null)
            {
                clip.SetCurve(Constants.GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            }
            
            // 隐藏防御对象（锁定时不需要防御）
            Transform defenseRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (defenseRoot != null)
            {
                clip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive", disableCurve);
            }

            // 可选：隐藏对象
            if (config.disableRootChildren)
            {
                var hiddenPaths = new HashSet<string>();
                var zeroCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
                var farCurve = AnimationCurve.Constant(0f, 1f / 60f, -9999f);
                
                // 查找 Avatar 的 Animator 以获取骨骼信息
                var animator = avatarRoot.GetComponent<Animator>();
                Transform armatureRoot = null;
                if (animator != null && animator.isHuman)
                {
                    // 获取 Hips 骨骼的根（通常是 Armature）
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null)
                    {
                        armatureRoot = hips.parent;
                        while (armatureRoot != null && armatureRoot.parent != avatarRoot.transform)
                        {
                            armatureRoot = armatureRoot.parent;
                        }
                    }
                }
                
                // 对所有根子对象使用 Scale=0
                // 非 Armature 对象依赖 WD 恢复
                // Armature 需要在解锁动画中显式恢复（Scale + Position + m_IsActive）
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child, avatarRoot.transform))
                        continue;
                    
                    string path = child.name;
                    bool isArmature = (armatureRoot != null && child == armatureRoot);
                    
                    if (hiddenPaths.Add(path))
                    {
                        // Scale = 0
                        clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", zeroCurve);
                        clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", zeroCurve);
                        clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", zeroCurve);
                        
                        // Armature 额外控制 Position 和 m_IsActive
                        if (isArmature)
                        {
                            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", farCurve);
                            clip.SetCurve(path, typeof(GameObject), "m_IsActive", zeroCurve);
                        }
                        
                        Debug.Log($"[ASS] 添加隐藏曲线: \"{path}\"{(isArmature ? " [Armature: Scale+Position+Active]" : " (Scale=0)")}");
                    }
                }
                
                Debug.Log($"[ASS] 锁定动画：隐藏 {hiddenPaths.Count} 个根子对象");
            }

            return clip;
        }
        
        private static bool IsASSObject(Transform obj, Transform avatarRoot)
        {
            var assObjectNames = new HashSet<string>
            {
                Constants.GO_ASS_ROOT,
                Constants.GO_UI_CANVAS,
                Constants.GO_AUDIO_WARNING,
                Constants.GO_AUDIO_SUCCESS,
                Constants.GO_PARTICLES,
                Constants.GO_DEFENSE_ROOT,
                Constants.GO_OCCLUSION_MESH
            };
            
            Transform current = obj;
            while (current != null && current != avatarRoot)
            {
                if (assObjectNames.Contains(current.name))
                    return true;
                current = current.parent;
            }
            return false;
        }
        
        /// <summary>
        /// 创建遮挡 Mesh（覆盖整个 Avatar，仅本地可见）
        /// 使用一个简单的球体 Mesh 包裹住 Avatar
        /// </summary>
        private static GameObject CreateOcclusionMesh(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            // 查找或创建遮挡 Mesh
            var existing = avatarRoot.transform.Find(Constants.GO_OCCLUSION_MESH);
            if (existing != null)
            {
                // 使用Destroy而不是DestroyImmediate，避免触发Editor刷新
                Object.Destroy(existing.gameObject);
            }
            
            var meshObj = new GameObject(Constants.GO_OCCLUSION_MESH);
            meshObj.transform.SetParent(avatarRoot.transform, false);
            
            // 创建正方形平面遮罩，刚好覆盖摄像机视角
            // 大小：0.5x0.5（刚好覆盖标准FOV）
            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateOcclusionQuad();
            
            // 初始位置在根部，由VRCConstraint控制
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            
            // 添加 MeshRenderer 并设置材质
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            
            // 使用不透明白色材质
            var material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.white;
            meshRenderer.sharedMaterial = material;
            
            // VR模式：绑定到头部，始终挡住视角
            var animator = avatarRoot.GetComponent<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null)
            {
                var constraint = meshObj.AddComponent<VRCParentConstraint>();
                var source = new VRCConstraintSource
                {
                    Weight = 1f,
                    SourceTransform = head,
                    ParentPositionOffset = new Vector3(0f, 0f, 0.18f),  // 头部前方18cm
                    ParentRotationOffset = Vector3.zero
                };
                constraint.Sources.Add(source);
                
                // 使用SerializedObject设置属性，避免触发Editor GUI事件
                var constraintSer = new SerializedObject(constraint);
                constraintSer.FindProperty("IsActive").boolValue = true;
                constraintSer.FindProperty("Locked").boolValue = true;
                constraintSer.ApplyModifiedPropertiesWithoutUndo();
            }
            
            // 默认禁用，锁定时通过动画启用
            meshObj.SetActive(false);
            
            Debug.Log($"[ASS] 创建遮挡 Mesh：四边形平面，大小=0.5x0.5，绑定到头部");
            
            return meshObj;
        }

        /// <summary>
        /// 创建正方形遮罩网格（覆盖摄像机视角）
        /// </summary>
        private static Mesh CreateOcclusionQuad()
        {
            var mesh = new Mesh { name = "OcclusionQuad" };
            
            // 创建正方形平面
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
        
        /// <summary>
        /// 创建 Remote 状态动画
        /// 用于其他玩家看到的默认状态：遮挡 Mesh 隐藏，Avatar 正常显示
        /// 必须明确控制遮挡 Mesh 的 Scale，不能依赖 Write Defaults
        /// </summary>
        /// <param name="useWdOn">true = WD On 模式, false = WD Off 模式</param>
        private static AnimationClip CreateRemoteClip(GameObject avatarRoot, AvatarSecuritySystemComponent config, bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Remote" };
            
            // 隐藏遮挡 Mesh 和防御对象
            SetGameObjectActiveInClip(clip, Constants.GO_OCCLUSION_MESH, false);
            SetGameObjectActiveInClip(clip, Constants.GO_DEFENSE_ROOT, false);
            
            // WD Off 模式：需要显式写入所有 Avatar 子对象的正常状态
            if (!useWdOn && config.disableRootChildren)
            {
                WriteRootChildrenRestoreValues(clip, avatarRoot, config);
            }
            
            Debug.Log($"[ASS] 创建 Remote 状态动画 (WD {(useWdOn ? "On" : "Off")})：隐藏遮挡 Mesh 和防御对象");
            return clip;
        }

        /// <summary>
        /// 创建解锁动画
        /// 隐藏 UI Canvas 和遮挡 Mesh（ASS 完全控制的组件）
        /// </summary>
        /// <param name="useWdOn">true = WD On 模式, false = WD Off 模式</param>
        private static AnimationClip CreateUnlockClip(GameObject avatarRoot, AvatarSecuritySystemComponent config, bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Unlock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);

            Debug.Log($"[ASS] 创建解锁动画 (WD {(useWdOn ? "On" : "Off")} 模式)");

            // 隐藏 UI Canvas、遮挡 Mesh 和防御对象
            SetGameObjectActiveInClip(clip, Constants.GO_UI_CANVAS, false);
            SetGameObjectActiveInClip(clip, Constants.GO_OCCLUSION_MESH, false);
            SetGameObjectActiveInClip(clip, Constants.GO_DEFENSE_ROOT, false);
            
            // 启用音效对象（解锁后恢复，密码正确可以听到音效）
            Transform warningAudio = avatarRoot.transform.Find(Constants.GO_AUDIO_WARNING);
            if (warningAudio != null)
            {
                clip.SetCurve(Constants.GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);
            }

            Transform successAudio = avatarRoot.transform.Find(Constants.GO_AUDIO_SUCCESS);
            if (successAudio != null)
            {
                clip.SetCurve(Constants.GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            }

            if (warningAudio != null || successAudio != null)
            {
                Debug.Log($"[ASS] 解锁动画：启用音效");
            }
            
            // 恢复 Avatar 子对象
            if (config.disableRootChildren)
            {
                if (useWdOn)
                {
                    // WD On 模式：仅恢复 Armature（因为 Humanoid Rig 会覆盖，WD 不可靠）
                    // 其他根子对象依赖 WD 自动恢复
                    WriteArmatureRestoreValues(clip, avatarRoot);
                }
                else
                {
                    // WD Off 模式：需要显式写入所有根子对象的恢复值
                    WriteRootChildrenRestoreValues(clip, avatarRoot, config);
                }
            }

            Debug.Log(I18n.T("log.lock_unlock_animation_created"));
            return clip;
        }
        
        /// <summary>
        /// 写入 Armature 的恢复值（WD On 模式专用）
        /// 因为 Humanoid Rig 会覆盖骨骼 Transform，WD 不可靠
        /// </summary>
        private static void WriteArmatureRestoreValues(AnimationClip clip, GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return;
                
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
                return;
                
            Transform armatureRoot = hips.parent;
            while (armatureRoot != null && armatureRoot.parent != avatarRoot.transform)
            {
                armatureRoot = armatureRoot.parent;
            }
            
            if (armatureRoot == null)
                return;
                
            string path = armatureRoot.name;
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            
            // 读取 Armature 的原始值
            Vector3 originalScale = armatureRoot.localScale;
            Vector3 originalPosition = armatureRoot.localPosition;
            
            // Scale = 原始值
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0f, 1f / 60f, originalScale.x));
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0f, 1f / 60f, originalScale.y));
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, 1f / 60f, originalScale.z));
            
            // Position = 原始值
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.x));
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.y));
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.z));
            
            // m_IsActive = 1
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", enableCurve);
            
            Debug.Log($"[ASS] Armature 恢复：\"{path}\" (Scale={originalScale}, Position={originalPosition})");
        }
        
        /// <summary>
        /// 写入所有根子对象的恢复值（WD Off 模式专用）
        /// 显式写入每个对象的原始 Scale、Position 和 Active 状态
        /// </summary>
        private static void WriteRootChildrenRestoreValues(AnimationClip clip, GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var restoredCount = 0;
            
            // 获取 Armature 根节点
            var animator = avatarRoot.GetComponent<Animator>();
            Transform armatureRoot = null;
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    armatureRoot = hips.parent;
                    while (armatureRoot != null && armatureRoot.parent != avatarRoot.transform)
                    {
                        armatureRoot = armatureRoot.parent;
                    }
                }
            }
            
            foreach (Transform child in avatarRoot.transform)
            {
                if (IsASSObject(child, avatarRoot.transform))
                    continue;
                
                string path = child.name;
                bool isArmature = (armatureRoot != null && child == armatureRoot);
                
                // 读取原始值
                Vector3 originalScale = child.localScale;
                Vector3 originalPosition = child.localPosition;
                
                // Scale = 原始值
                clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0f, 1f / 60f, originalScale.x));
                clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0f, 1f / 60f, originalScale.y));
                clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, 1f / 60f, originalScale.z));
                
                // Armature 额外控制 Position 和 m_IsActive
                if (isArmature)
                {
                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.x));
                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.y));
                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0f, 1f / 60f, originalPosition.z));
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", enableCurve);
                }
                
                restoredCount++;
            }
            
            Debug.Log($"[ASS] WD Off 恢复：{restoredCount} 个根子对象");
        }
        
        /// <summary>
        /// 在动画片段中设置 GameObject 的活动状态
        /// </summary>
        private static void SetGameObjectActiveInClip(AnimationClip clip, string objectPath, bool isActive)
        {
            var curve = AnimationCurve.Constant(0f, 1f / 60f, isActive ? 1f : 0f);
            clip.SetCurve(objectPath, typeof(GameObject), "m_IsActive", curve);
        }
        
        /// <summary>
        /// 获取控制器中匹配名称的层索引列表
        /// </summary>
        private static List<int> GetLayerIndicesByNames(
            AnimatorController controller, 
            string[] layerNames, 
            string excludeLayer = null,
            string logPrefix = "")
        {
            var indices = new List<int>();
            var nameSet = new HashSet<string>(layerNames);
            
            for (int i = 0; i < controller.layers.Length; i++)
            {
                string layerName = controller.layers[i].name;
                
                if (excludeLayer != null && layerName == excludeLayer)
                    continue;
                    
                if (nameSet.Contains(layerName))
                {
                    indices.Add(i);
                    if (!string.IsNullOrEmpty(logPrefix))
                        Debug.Log($"[ASS] {logPrefix}：添加层 '{layerName}' (索引 {i})");
                }
            }
            
            return indices;
        }
        
        /// <summary>
        /// 应用层权重控制到两个状态（锁定和解锁）
        /// Locked状态：层权重=0（禁用其他ASS层）
        /// Unlocked状态：层权重=1（启用其他ASS层）
        /// </summary>
        private static void ApplyLayerWeightControl(
            LockLayerResult lockResult,
            int[] layerIndices)
        {
            // 锁定状态：将所有层权重设为0
            AnimatorUtils.AddMultiLayerControlBehaviour(
                lockResult.LockedState,
                layerIndices,
                goalWeight: 0f,
                blendDuration: 0f);

            // 解锁状态：将所有层权重设为1
            AnimatorUtils.AddMultiLayerControlBehaviour(
                lockResult.UnlockedState,
                layerIndices,
                goalWeight: 1f,
                blendDuration: 0f);
        }
    }
}