using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 锁定系统生成器
    /// 功能：
    /// 1. 锁定/解锁状态机（远端/锁定/解锁三态）
    /// 2. 参数驱动：锁定时设为反转值，解锁时恢复
    /// 3. 材质锁定：锁定时清空材质槽
    /// </summary>
    public class Lock
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly AvatarSecuritySystemComponent config;

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
        /// </summary>
        public void Generate()
        {
            var layer = Utils.CreateLayer(LAYER_LOCK, 1f);

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

            Debug.Log("[ASS] Initial lock layer created");

            controller.AddLayer(layer);
        }

        #region Private Methods

        private AnimationClip CreateLockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Lock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            
            Debug.Log($"[ASS] Lock animation created (WD {(useWdOn ? "On" : "Off")} mode)");

            // ASS_UI 同时承担遮挡和进度条显示（使用全屏覆盖 Shader）
            if (avatarRoot.transform.Find(GO_UI) != null)
            {
                clip.SetCurve(GO_UI, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log("[ASS] Lock animation: enabled UI (fullscreen overlay + progress bar)");
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
                    Debug.Log($"[ASS] Lock animation: \"{child.name}\" (IsActive=0)");
                    hiddenCount++;
                }
                
                Debug.Log($"[ASS] Lock animation: hidden {hiddenCount} root child objects");
            }

            return clip;
        }

        private AnimationClip CreateRemoteClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Remote" };
            
            SetGameObjectActiveInClip(clip, GO_UI, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            
            // WD Off: 显式恢复所有被修改的属性
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);
            
            Debug.Log($"[ASS] Remote state animation created (WD {(useWdOn ? "On" : "Off")}): hide UI and defense objects");
            return clip;
        }

        private AnimationClip CreateUnlockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = "ASS_Unlock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);

            Debug.Log($"[ASS] Unlock animation created (WD {(useWdOn ? "On" : "Off")} mode)");

            SetGameObjectActiveInClip(clip, GO_UI, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            
            if (avatarRoot.transform.Find(GO_AUDIO_WARNING) != null)
                clip.SetCurve(GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);

            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            
            // WD On: 不显式恢复，由 WD 自动恢复默认值，避免破坏其他插件
            // WD Off: 显式写回所有被修改属性的原始值
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);

            Debug.Log("[ASS] Unlock animation created (empty animation, allows objects to restore original state)");
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
            
            Debug.Log($"[ASS] WD Off restore: {restoredCount} root child objects IsActive=1");
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

        private bool IsASSObject(Transform obj)
        {
            var assObjectNames = new HashSet<string>
            {
                GO_ASS_ROOT, GO_UI, GO_AUDIO_WARNING,
                GO_AUDIO_SUCCESS, GO_PARTICLES, GO_DEFENSE_ROOT
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

        #endregion
    }
}
