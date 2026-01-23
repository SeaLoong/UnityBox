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
    /// 功能：禁用所有根级子对象，反转所有参数默认值
    /// </summary>
    public static class InitialLockSystem
    {
        /// <summary>
        /// 创建初始锁定层
        /// </summary>
        public static AnimatorControllerLayer CreateLockLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_INITIAL_LOCK, 1f);

            // 创建锁定状态
            var lockedState = layer.stateMachine.AddState("Locked", new Vector3(200, 50, 0));
            lockedState.writeDefaultValues = true; // 关键：启用Write Defaults
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 150, 0));
            unlockedState.writeDefaultValues = true; // 关键：启用Write Defaults

            layer.stateMachine.defaultState = lockedState;

            // 生成锁定动画
            if (config.disableRootChildren)
            {
                var lockClip = CreateObjectDisableClip(avatarRoot);
                lockedState.motion = lockClip;
                AnimatorUtils.AddSubAsset(controller, lockClip);
            }
            else
            {
                lockedState.motion = AnimatorUtils.SharedEmptyClip;
            }

            // 生成解锁动画
            var unlockClip = CreateObjectEnableClip(avatarRoot);
            unlockedState.motion = unlockClip;
            // SharedEmptyClip不需要添加为子资源，它是独立文件
            // AnimatorUtils.AddSubAsset会自动处理这种情况

            // 添加参数（已由密码系统添加 PASSWORD_CORRECT）
            // PARAM_UNLOCKED 由 PASSWORD_CORRECT 驱动，不需要单独参数

            // 转换：密码正确时解锁
            var toUnlocked = AnimatorUtils.CreateTransition(lockedState, unlockedState);
            toUnlocked.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(I18n.T("log.lock_layer_created"));
            return layer;
        }

        /// <summary>
        /// 创建对象禁用动画（禁用所有根级子对象）
        /// </summary>
        private static AnimationClip CreateObjectDisableClip(GameObject avatarRoot)
        {
            var targets = GetLockTargets(avatarRoot);
            var states = new bool[targets.Length];
            
            // 全部设置为 false（禁用）
            for (int i = 0; i < states.Length; i++)
                states[i] = false;

            var clip = AnimationClipGenerator.CreateGameObjectActiveClip(
                "ASS_ObjectsDisabled",
                avatarRoot,
                targets,
                states
            );

            return clip;
        }

        /// <summary>
        /// 创建解锁动画（禁用UI和音频，其他对象依赖Write Defaults恢复）
        /// 关键：解锁时不应该显式启用对象，而是移除锁定动画的影响
        /// 通过Write Defaults = true，对象会恢复到它们的默认状态（受其他层控制）
        /// 但需要显式禁用UI和音频系统
        /// </summary>
        private static AnimationClip CreateObjectEnableClip(GameObject avatarRoot)
        {
            // 创建一个动画，只禁用UI和音频对象
            var clip = new AnimationClip { name = "ASS_Unlock" };
            
            // 查找UI和音频对象
            Transform uiCanvas = avatarRoot.transform.Find(Constants.GO_UI_CANVAS);
            Transform feedbackAudio = avatarRoot.transform.Find(Constants.GO_FEEDBACK_AUDIO);
            Transform warningAudio = avatarRoot.transform.Find(Constants.GO_WARNING_AUDIO);
            
            // 禁用UI Canvas
            if (uiCanvas != null)
            {
                string uiPath = Constants.GO_UI_CANVAS;
                var uiCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f); // false
                clip.SetCurve(uiPath, typeof(GameObject), "m_IsActive", uiCurve);
            }
            
            // 禁用密码输入音频对象
            if (feedbackAudio != null)
            {
                string audioPath = Constants.GO_FEEDBACK_AUDIO;
                var audioCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f); // false
                clip.SetCurve(audioPath, typeof(GameObject), "m_IsActive", audioCurve);
            }
            
            // 禁用警告音频对象
            if (warningAudio != null)
            {
                string warningPath = Constants.GO_WARNING_AUDIO;
                var warningCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f); // false
                clip.SetCurve(warningPath, typeof(GameObject), "m_IsActive", warningCurve);
            }
            
            Debug.Log(I18n.T("log.lock_unlock_animation_created"));
            return clip;
        }

        /// <summary>
        /// 获取需要锁定的目标对象（Avatar Root 的直接子对象）
        /// </summary>
        private static GameObject[] GetLockTargets(GameObject avatarRoot)
        {
            var exclusions = new HashSet<string>
            {
                "VRCAvatarDescriptor",
                "Animator",
                "PipelineSaver",
                Constants.GO_ASS_ROOT, // 排除 ASS 系统自身
                Constants.GO_UI_CANVAS, // 排除反馈UI
                Constants.GO_FEEDBACK_AUDIO, // 排除密码输入音频
                Constants.GO_WARNING_AUDIO // 排除警告音频
            };

            var targets = new List<GameObject>();

            for (int i = 0; i < avatarRoot.transform.childCount; i++)
            {
                var child = avatarRoot.transform.GetChild(i).gameObject;
                
                // 排除特殊对象
                if (exclusions.Any(e => child.name.Contains(e)))
                    continue;

#if NDMF_AVAILABLE
                // 排除特定组件
                if (child.GetComponent<VRCAvatarDescriptor>() != null)
                    continue;
                if (child.GetComponent<Animator>() != null)
                    continue;
#endif

                targets.Add(child);
            }

            Debug.Log(string.Format(I18n.T("log.lock_targets_found"), targets.Count));
            return targets.ToArray();
        }

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
                // 创建新的 ModularAvatarParameters 组件
                var paramsGO = new GameObject("ASS_ParameterInverter");
                paramsGO.transform.SetParent(avatarRoot.transform);
                maParams = paramsGO.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
            }

            // 获取 VRCAvatarDescriptor 的参数
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null) return;

            // 反转每个参数的默认值
            foreach (var param in expressionParameters.parameters)
            {
                // 跳过 VRChat 内置参数
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

                // 添加到 MA Parameters
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
                "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation"
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
                    // Int 类型简单反转 0/1
                    int intVal = Mathf.RoundToInt(value);
                    return intVal == 0 ? 1f : 0f;
                    
                case VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float:
                    // Float 类型在 0-1 范围内反转
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
