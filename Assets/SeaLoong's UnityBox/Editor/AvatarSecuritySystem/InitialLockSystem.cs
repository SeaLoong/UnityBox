using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimationClipGenerator;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
#endif

#if NDMF_AVAILABLE
using nadena.dev.ndmf;
#endif

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 初始锁定系统生成器
    /// 功能：
    /// 1. 解锁时通过VRC层权重控制禁用其他ASS层
    /// 2. 参数驱动：锁定时设为反转值，解锁时恢复
    /// 3. 材质锁定：锁定时清空材质槽
    /// </summary>
    public static class InitialLockSystem
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
            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_INITIAL_LOCK, 1f);

            // 创建遮挡 Mesh（基本功能，无论防护等级都需要）
            // 必须在创建动画之前创建，这样动画才能引用到它
            var occlusionMesh = CreateOcclusionMesh(avatarRoot, config);

            // 创建状态
            // Remote 状态：其他玩家看到的默认状态（遮挡 Mesh 隐藏，Avatar 正常显示）
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, 0, 0));
            remoteState.writeDefaultValues = true;
            var remoteClip = CreateRemoteClip(avatarRoot, config);
            remoteState.motion = remoteClip;
            AnimatorUtils.AddSubAsset(controller, remoteClip);
            
            // Locked 状态：本地玩家锁定时看到的状态（遮挡 Mesh 显示，Avatar 隐藏）
            var lockedState = layer.stateMachine.AddState("Locked", new Vector3(200, 100, 0));
            lockedState.writeDefaultValues = true;
            
            // Unlocked 状态：本地玩家解锁后看到的状态（遮挡 Mesh 隐藏，UI 隐藏）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 200, 0));
            unlockedState.writeDefaultValues = true;

            layer.stateMachine.defaultState = remoteState;

            // 生成锁定动画（显示遮挡 Mesh + 可选隐藏 Renderer）
            var lockClip = CreateLockClip(avatarRoot, config);
            lockedState.motion = lockClip;
            AnimatorUtils.AddSubAsset(controller, lockClip);

            // 生成解锁动画（隐藏UI + 隐藏遮挡Mesh）
            var unlockClip = CreateUnlockClip(avatarRoot, config);
            unlockedState.motion = unlockClip;
            AnimatorUtils.AddSubAsset(controller, unlockClip);

            // 转换：Remote → Unlocked（如果已保存的密码状态为正确，直接进入解锁）
            var toUnlockedDirect = AnimatorUtils.CreateTransition(remoteState, unlockedState);
            toUnlockedDirect.hasExitTime = false;
            toUnlockedDirect.duration = 0f;
            toUnlockedDirect.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            toUnlockedDirect.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_PASSWORD_CORRECT);
            
            // 转换：Remote → Locked（IsLocal 时且密码未正确，进入锁定状态）
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
            
            // 转换：Locked → Unlocked（密码正确时解锁）
            var toUnlocked = AnimatorUtils.CreateTransition(lockedState, unlockedState);
            toUnlocked.hasExitTime = false;
            toUnlocked.duration = 0f;
            toUnlocked.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_PASSWORD_CORRECT);

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
        /// 配置解锁状态的层权重控制
        /// 必须在所有层添加到控制器后调用
        /// </summary>
        public static void ConfigureLayerWeightControl(
            AnimatorController controller,
            LockLayerResult lockResult,
            string[] assLayerNames)
        {
#if VRC_SDK_VRCSDK3
            // 找到所有ASS层的索引（排除锁定层本身）
            var layerIndices = new List<int>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                string layerName = controller.layers[i].name;
                // 排除锁定层本身（锁定层权重必须始终为1才能播放锁定/解锁动画）
                if (layerName == Constants.LAYER_INITIAL_LOCK)
                    continue;
                    
                if (assLayerNames.Contains(layerName))
                {
                    layerIndices.Add(i);
                    Debug.Log($"[ASS] 层权重控制：添加层 '{layerName}' (索引 {i})");
                }
            }

            if (layerIndices.Count > 0)
            {
                // 解锁状态：将所有ASS层权重设为0（禁用ASS效果）
                AnimatorUtils.AddMultiLayerControlBehaviour(
                    lockResult.UnlockedState,
                    layerIndices.ToArray(),
                    goalWeight: 0f,
                    blendDuration: 0f);

                // 锁定状态：将所有ASS层权重设为1（启用ASS效果）
                AnimatorUtils.AddMultiLayerControlBehaviour(
                    lockResult.LockedState,
                    layerIndices.ToArray(),
                    goalWeight: 1f,
                    blendDuration: 0f);

                Debug.Log($"[ASS] 已配置层权重控制：{layerIndices.Count} 个层");
            }
            else
            {
                Debug.LogWarning("[ASS] 层权重控制：未找到需要控制的层");
                Debug.Log($"[ASS] 传入的层名称列表: {string.Join(", ", assLayerNames)}");
                Debug.Log($"[ASS] 控制器中的层: {string.Join(", ", controller.layers.Select(l => l.name))}");
            }
#endif
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
#if VRC_SDK_VRCSDK3
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
                // 锁定状态：将所有非ASS层权重设为0（锁定FX功能）
                AnimatorUtils.AddMultiLayerControlBehaviour(
                    lockResult.LockedState,
                    nonAssLayerIndices.ToArray(),
                    goalWeight: 0f,
                    blendDuration: 0f);

                // 解锁状态：将所有非ASS层权重设为1（恢复FX功能）
                AnimatorUtils.AddMultiLayerControlBehaviour(
                    lockResult.UnlockedState,
                    nonAssLayerIndices.ToArray(),
                    goalWeight: 1f,
                    blendDuration: 0f);

                Debug.Log($"[ASS] 已配置FX层锁定：{nonAssLayerIndices.Count} 个非ASS层");
            }
            else
            {
                Debug.LogWarning("[ASS] FX层锁定：未找到非ASS层");
            }
#endif
        }

        /// <summary>
        /// 创建锁定动画
        /// 始终显示遮挡 Mesh
        /// 如果启用 disableRootChildren，会隐藏：
        /// 1. Avatar Root 的直接子对象（非 ASS 对象）
        /// 2. 所有 Renderer 对象（用于隐藏骨骼上的 Mesh，因为 Humanoid Rig 骨骼无法被动画控制）
        /// </summary>
        private static AnimationClip CreateLockClip(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            var clip = new AnimationClip { name = "ASS_Lock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);

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
            Transform feedbackAudio = avatarRoot.transform.Find(Constants.GO_FEEDBACK_AUDIO);
            if (feedbackAudio != null)
            {
                clip.SetCurve(Constants.GO_FEEDBACK_AUDIO, typeof(GameObject), "m_IsActive", enableCurve);
            }
            
            Transform warningAudio = avatarRoot.transform.Find(Constants.GO_WARNING_AUDIO);
            if (warningAudio != null)
            {
                clip.SetCurve(Constants.GO_WARNING_AUDIO, typeof(GameObject), "m_IsActive", enableCurve);
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
                
                // 1. 隐藏 Avatar Root 的直接子对象（非 ASS 对象）
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child, avatarRoot.transform))
                        continue;
                    
                    string path = child.name;
                    if (hiddenPaths.Add(path))
                    {
                        AddScaleZeroCurves(clip, path);
                    }
                }
                
                Debug.Log($"[ASS] 锁定动画：隐藏 {hiddenPaths.Count} 个根子对象");
                
                // 2. 隐藏所有 Renderer 对象（用于隐藏骨骼上的 Mesh）
                // 因为 Humanoid Rig 骨骼的 Transform 被 Animator 控制，无法通过动画修改 Scale
                // 所以需要直接隐藏 Renderer 所在的对象
                int rendererCount = 0;
                var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    // 跳过 ASS 创建的对象
                    if (IsASSObject(renderer.transform, avatarRoot.transform))
                        continue;
                    
                    string path = GetRelativePath(avatarRoot.transform, renderer.transform);
                    if (hiddenPaths.Add(path))
                    {
                        AddScaleZeroCurves(clip, path);
                        rendererCount++;
                    }
                }
                
                Debug.Log($"[ASS] 锁定动画：隐藏 {rendererCount} 个额外渲染器对象");
                
                // 验证动画曲线
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Debug.Log($"[ASS] 动画曲线绑定数量: {bindings.Length}");
            }

            return clip;
        }
        
        /// <summary>
        /// 检查对象是否为 ASS 创建的对象
        /// </summary>
        private static bool IsASSObject(Transform obj, Transform avatarRoot)
        {
            var assObjectNames = new HashSet<string>
            {
                Constants.GO_ASS_ROOT,
                Constants.GO_UI_CANVAS,
                Constants.GO_FEEDBACK_AUDIO,
                Constants.GO_WARNING_AUDIO,
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
        /// 添加 Scale=0 动画曲线
        /// </summary>
        private static void AddScaleZeroCurves(AnimationClip clip, string path)
        {
            var zeroCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", zeroCurve);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", zeroCurve);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", zeroCurve);
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
            
#if VRC_SDK_VRCSDK3
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
#endif
            
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
        /// 计算 Avatar 的边界框
        /// </summary>
        private static Bounds CalculateAvatarBounds(GameObject avatarRoot)
        {
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(avatarRoot.transform.position, Vector3.one);
            }
            
            Bounds bounds = new Bounds();
            bool first = true;
            
            foreach (var renderer in renderers)
            {
                // 跳过 ASS 创建的对象
                if (IsASSObject(renderer.transform, avatarRoot.transform))
                    continue;
                
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            
            return bounds;
        }
        
        /// <summary>
        /// 创建球体 Mesh
        /// </summary>
        private static Mesh CreateSphereMesh(int longitudeSegments, int latitudeSegments)
        {
            var mesh = new Mesh { name = "ASS_OcclusionSphere" };
            
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            
            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float theta = lat * Mathf.PI / latitudeSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);
                
                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / longitudeSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);
                    
                    // 法线朝内（用于遮挡内部视角）
                    var normal = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                    vertices.Add(normal);
                    normals.Add(-normal); // 法线朝内
                }
            }
            
            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int current = lat * (longitudeSegments + 1) + lon;
                    int next = current + longitudeSegments + 1;
                    
                    // 逆时针三角形（从内部看是正面）
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }
            
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            
            return mesh;
        }

        /// <summary>
        /// 创建 Remote 状态动画
        /// 用于其他玩家看到的默认状态：遮挡 Mesh 隐藏，Avatar 正常显示
        /// 必须明确控制遮挡 Mesh 的 Scale，不能依赖 Write Defaults
        /// </summary>
        private static AnimationClip CreateRemoteClip(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            var clip = new AnimationClip { name = "ASS_Remote" };
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            
            // 隐藏遮挡 Mesh（m_IsActive=0）- 其他玩家不应该看到遮挡 Mesh
            Transform occlusionMesh = avatarRoot.transform.Find(Constants.GO_OCCLUSION_MESH);
            if (occlusionMesh != null)
            {
                clip.SetCurve(Constants.GO_OCCLUSION_MESH, typeof(GameObject), "m_IsActive", disableCurve);
            }
            
            // 隐藏防御对象（m_IsActive=0）- 其他玩家不应该看到防御效果
            Transform defenseRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (defenseRoot != null)
            {
                clip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive", disableCurve);
            }
            
            Debug.Log("[ASS] 创建 Remote 状态动画：隐藏遮挡 Mesh 和防御对象");
            return clip;
        }

        /// <summary>
        /// 创建解锁动画
        /// 隐藏 UI Canvas 和遮挡 Mesh（ASS 完全控制的组件）
        /// </summary>
        private static AnimationClip CreateUnlockClip(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            var clip = new AnimationClip { name = "ASS_Unlock" };
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);

            // 隐藏 UI Canvas（解锁后永久隐藏）
            Transform uiCanvas = avatarRoot.transform.Find(Constants.GO_UI_CANVAS);
            if (uiCanvas != null)
            {
                clip.SetCurve(Constants.GO_UI_CANVAS, typeof(GameObject), "m_IsActive", disableCurve);
            }
            
            // 隐藏遮挡 Mesh（m_IsActive=0）
            Transform occlusionMesh = avatarRoot.transform.Find(Constants.GO_OCCLUSION_MESH);
            if (occlusionMesh != null)
            {
                clip.SetCurve(Constants.GO_OCCLUSION_MESH, typeof(GameObject), "m_IsActive", disableCurve);
            }
            
            // 隐藏防御对象（m_IsActive=0）- 解锁后不需要防御效果
            Transform defenseRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (defenseRoot != null)
            {
                clip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive", disableCurve);
            }
            
            // 启用音效对象（解锁后恢复，我输遇手段可以听到音效）
            Transform feedbackAudio = avatarRoot.transform.Find(Constants.GO_FEEDBACK_AUDIO);
            if (feedbackAudio != null)
            {
                clip.SetCurve(Constants.GO_FEEDBACK_AUDIO, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 解锁动画：启用反馈音效");
            }
            
            Transform warningAudio = avatarRoot.transform.Find(Constants.GO_WARNING_AUDIO);
            if (warningAudio != null)
            {
                clip.SetCurve(Constants.GO_WARNING_AUDIO, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 解锁动画：启用警告音效");
            }

            Debug.Log(I18n.T("log.lock_unlock_animation_created"));
            return clip;
        }

        /// <summary>
        /// 获取相对路径
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";

            var path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }
    }
}
