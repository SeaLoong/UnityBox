using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimationClipGenerator;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if NDMF_AVAILABLE
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;
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

#if VRC_SDK_VRCSDK3
            // 添加参数驱动
            if (config.invertParameters)
            {
                // 锁定状态：设置参数为反转值
                var lockedParams = CollectInvertedParameters(avatarRoot);
                if (lockedParams.Count > 0)
                {
                    AnimatorUtils.AddMultiParameterDriverBehaviour(lockedState, lockedParams, localOnly: true);
                }

                // 解锁状态：恢复参数为默认值
                var unlockedParams = CollectDefaultParameters(avatarRoot);
                if (unlockedParams.Count > 0)
                {
                    AnimatorUtils.AddMultiParameterDriverBehaviour(unlockedState, unlockedParams, localOnly: true);
                }
            }
#endif

            // 转换：Remote → Locked（IsLocal 时进入锁定状态）
            var toLocal = AnimatorUtils.CreateTransition(remoteState, lockedState);
            toLocal.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_IS_LOCAL);
            
            // 转换：Locked → Unlocked（密码正确时解锁）
            var toUnlocked = AnimatorUtils.CreateTransition(lockedState, unlockedState);
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
        /// 创建锁定动画
        /// 始终显示遮挡 Mesh
        /// 如果启用 disableRootChildren，会隐藏：
        /// 1. Avatar Root 的直接子对象（非 ASS 对象）
        /// 2. 所有 Renderer 对象（用于隐藏骨骼上的 Mesh，因为 Humanoid Rig 骨骼无法被动画控制）
        /// </summary>
        private static AnimationClip CreateLockClip(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            var clip = new AnimationClip { name = "ASS_Lock" };

            // 显示遮挡 Mesh（基本功能，始终执行）
            // 使用 m_IsActive 控制，因为这是 ASS 完全控制的对象
            Transform occlusionMesh = avatarRoot.transform.Find(Constants.GO_OCCLUSION_MESH);
            if (occlusionMesh != null)
            {
                var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
                clip.SetCurve(Constants.GO_OCCLUSION_MESH, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log($"[ASS] 锁定动画：显示遮挡 Mesh");
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
        /// 添加 Scale=1 动画曲线（显示对象）
        /// </summary>
        private static void AddScaleOneCurves(AnimationClip clip, string path)
        {
            var oneCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", oneCurve);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", oneCurve);
            clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", oneCurve);
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
                Object.DestroyImmediate(existing.gameObject);
            }
            
            var meshObj = new GameObject(Constants.GO_OCCLUSION_MESH);
            meshObj.transform.SetParent(avatarRoot.transform, false);
            
            // 计算 Avatar 的边界
            var bounds = CalculateAvatarBounds(avatarRoot);
            
            // 设置位置为边界中心（相对于 Avatar Root）
            meshObj.transform.localPosition = bounds.center - avatarRoot.transform.position;
            
            // 创建球体 Mesh
            var meshFilter = meshObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateSphereMesh(32, 16);
            
            // 设置缩放以覆盖整个 Avatar
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            float scale = maxExtent * 1.2f; // 增加 20% 确保完全覆盖
            meshObj.transform.localScale = new Vector3(scale, scale, scale);
            
            // 添加 MeshRenderer 并设置材质
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();
            
            // 使用不透明白色材质
            var material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.white;
            meshRenderer.sharedMaterial = material;
            
            // 默认禁用，锁定时通过动画启用
            // 使用 m_IsActive 控制，因为这是 ASS 完全控制的对象
            meshObj.SetActive(false);
            
            Debug.Log($"[ASS] 创建遮挡 Mesh：中心={bounds.center}, 大小={scale}");
            
            return meshObj;
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

            // 隐藏 UI Canvas
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

        /// <summary>
        /// 获取需要锁定的目标对象（Avatar Root 的直接子对象）
        /// 排除 ASS 创建的对象和系统组件
#if VRC_SDK_VRCSDK3
        /// <summary>
        /// 收集所有参数的默认值
        /// </summary>
        private static Dictionary<string, float> CollectDefaultParameters(GameObject avatarRoot)
        {
            var parameters = new Dictionary<string, float>();

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || descriptor.expressionParameters == null)
                return parameters;

            foreach (var param in descriptor.expressionParameters.parameters)
            {
                if (IsVRChatBuiltInParameter(param.name))
                    continue;

                // 跳过ASS自己的参数
                if (param.name.StartsWith("ASS_"))
                    continue;

                parameters[param.name] = param.defaultValue;
            }

            return parameters;
        }

        /// <summary>
        /// 收集所有参数的反转值
        /// </summary>
        private static Dictionary<string, float> CollectInvertedParameters(GameObject avatarRoot)
        {
            var parameters = new Dictionary<string, float>();

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || descriptor.expressionParameters == null)
                return parameters;

            foreach (var param in descriptor.expressionParameters.parameters)
            {
                if (IsVRChatBuiltInParameter(param.name))
                    continue;

                // 跳过ASS自己的参数
                if (param.name.StartsWith("ASS_"))
                    continue;

                float invertedValue = InvertParameterValue(param.defaultValue, param.valueType);
                parameters[param.name] = invertedValue;
            }

            Debug.Log($"[ASS] 已收集 {parameters.Count} 个参数用于反转");
            return parameters;
        }
#endif

        /// <summary>
        /// 反转 Avatar 参数默认值（通过 Modular Avatar Parameters）
        /// 注意：这个功能需要在 Avatar 上添加 ModularAvatarParameters 组件
        /// </summary>
        public static void InvertAvatarParameters(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (!config.invertParameters)
                return;

#if NDMF_AVAILABLE && MODULAR_AVATAR_AVAILABLE
            // 查找或创建 ModularAvatarParameters 组件
            var maParams = avatarRoot.GetComponentInChildren<nadena.dev.modular_avatar.core.ModularAvatarParameters>(true);

            if (maParams == null)
            {
                var paramsGO = new GameObject("ASS_ParameterInverter");
                paramsGO.transform.SetParent(avatarRoot.transform);
                maParams = paramsGO.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
            }

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null) return;

            foreach (var param in expressionParameters.parameters)
            {
                if (IsVRChatBuiltInParameter(param.name))
                    continue;

                var invertedParam = new nadena.dev.modular_avatar.core.ParameterConfig
                {
                    nameOrPrefix = param.name,
                    syncType = ConvertToMAParameterSyncType(param.valueType),
                    defaultValue = InvertParameterValue(param.defaultValue, param.valueType),
                    saved = param.saved,
                    hasExplicitDefaultValue = true
                };

                maParams.parameters.Add(invertedParam);
            }

            EditorUtility.SetDirty(maParams);
            Debug.Log(string.Format(I18n.T("log.lock_parameters_inverted"), expressionParameters.parameters.Length));
#else
            Debug.LogWarning(I18n.T("log.lock_ma_missing"));
#endif
        }

        private static bool IsVRChatBuiltInParameter(string name)
        {
            var builtIns = new HashSet<string>
            {
                "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureRight",
                "GestureLeftWeight", "GestureRightWeight", "AngularY",
                "VelocityX", "VelocityY", "VelocityZ", "Upright", "Grounded",
                "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation",
                "Earmuffs", "ScaleModified", "ScaleFactor", "ScaleFactorInverse",
                "EyeHeightAsMeters", "EyeHeightAsPercent"
            };
            return builtIns.Contains(name);
        }

#if NDMF_AVAILABLE && MODULAR_AVATAR_AVAILABLE
        private static nadena.dev.modular_avatar.core.ParameterSyncType ConvertToMAParameterSyncType(
            VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType valueType)
        {
            switch (valueType)
            {
                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int:
                    return nadena.dev.modular_avatar.core.ParameterSyncType.Int;
                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float:
                    return nadena.dev.modular_avatar.core.ParameterSyncType.Float;
                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool:
                    return nadena.dev.modular_avatar.core.ParameterSyncType.Bool;
                default:
                    return nadena.dev.modular_avatar.core.ParameterSyncType.NotSynced;
            }
        }
#endif

        private static float InvertParameterValue(float value,
#if NDMF_AVAILABLE
            VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType valueType
#else
            int valueType
#endif
        )
        {
#if NDMF_AVAILABLE
            switch (valueType)
            {
                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool:
                    return value > 0.5f ? 0f : 1f;

                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int:
                    int intVal = Mathf.RoundToInt(value);
                    return intVal == 0 ? 255f : 0f;

                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float:
                    return 1f - Mathf.Clamp01(value);

                default:
                    return value;
            }
#else
            return 1f - Mathf.Clamp01(value);
#endif
        }
    }
}
