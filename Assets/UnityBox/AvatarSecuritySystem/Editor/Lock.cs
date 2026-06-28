using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
using VRC.SDK3.Avatars.Components;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class Lock
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSComponent config;
        private readonly VRCAvatarDescriptor descriptor;
        public Lock(AnimatorController controller, GameObject avatarRoot, ASSComponent config, VRCAvatarDescriptor descriptor)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
            this.descriptor = descriptor;
        }
        public void Generate()
        {
            // 确保 Blend Tree 和过渡条件依赖的参数存在
            Utils.AddParameterIfNotExists(controller, PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);

            var layer = Utils.CreateLayer(LAYER_LOCK, 1f);
            bool useWdOn = ResolveWriteDefaults();
            var remoteState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Remote") : "Remote",
                new Vector3(200, 0, 0));
            remoteState.writeDefaultValues = useWdOn;
            var remoteClip = CreateRemoteClip(useWdOn);
            remoteState.motion = remoteClip;
            Utils.AddSubAsset(controller, remoteClip);

            var lockedState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("LockedA") : "LockedA",
                new Vector3(200, 100, 0));
            lockedState.writeDefaultValues = useWdOn;
            AnimatorState lockedShadowB = null, lockedShadowC = null, preLockState = null;
            if (Obfuscator.DecoyStatesEnabled)
            {
                Utils.AddParameterIfNotExists(controller, "_SysBypassChk",
                    AnimatorControllerParameterType.Bool, false);
                Utils.AddParameterIfNotExists(controller, "_PwHashCacheV",
                    AnimatorControllerParameterType.Float, defaultFloat: 0f);
                lockedShadowB = layer.stateMachine.AddState(
                    Obfuscator.IsEnabled ? Obfuscator.State("LockedB") : "LockedB",
                    new Vector3(350, 100, 0));
                lockedShadowB.writeDefaultValues = useWdOn;
                lockedShadowC = layer.stateMachine.AddState(
                    Obfuscator.IsEnabled ? Obfuscator.State("LockedC") : "LockedC",
                    new Vector3(500, 100, 0));
                lockedShadowC.writeDefaultValues = useWdOn;
                preLockState = layer.stateMachine.AddState(
                    Obfuscator.IsEnabled ? Obfuscator.State("PreLock") : "PreLock",
                    new Vector3(200, 0, 0));
                preLockState.writeDefaultValues = useWdOn;
            }
            var unlockedState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Unlocked") : "Unlocked",
                new Vector3(200, 200, 0));
            unlockedState.writeDefaultValues = useWdOn;
            layer.stateMachine.defaultState = remoteState;
            var lockClip = CreateLockClip(useWdOn);
            var unlockClip = CreateUnlockClip(useWdOn);
            Utils.AddSubAsset(controller, unlockClip);
            lockedState.motion = lockClip;
            Utils.AddSubAsset(controller, lockClip);
            if (Obfuscator.DecoyStatesEnabled && lockedShadowB != null)
            {
                lockedShadowB.motion = lockClip;
                lockedShadowC.motion = lockClip;
                preLockState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            }
            // Remote → Locked: 所有客户端初始进入锁定
            var remoteToLocked = Utils.CreateTransition(remoteState, lockedState);
            remoteToLocked.hasExitTime = false;
            remoteToLocked.duration = 0f;
            remoteToLocked.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            // Remote → Unlocked: 如果密码已正确则直接跳过锁定
            var toUnlockedDirect = Utils.CreateTransition(remoteState, unlockedState);
            toUnlockedDirect.hasExitTime = false;
            toUnlockedDirect.duration = 0f;
            toUnlockedDirect.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            if (Obfuscator.DecoyStatesEnabled && preLockState != null)
            {
                var toPreLock = Utils.CreateTransition(remoteState, preLockState);
                toPreLock.hasExitTime = false;
                toPreLock.duration = 0f;
                Utils.AddIsLocalCondition(toPreLock, controller, isTrue: true);
                toPreLock.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                var preLockToLocked = Utils.CreateTransition(preLockState, lockedState,
                    hasExitTime: true, exitTime: 0.01f);
                preLockToLocked.duration = 0f;
            }
            // Locked → Unlocked（显式过渡，本地有效时作为快路径）
            var toUnlocked = Utils.CreateTransition(lockedState, unlockedState);
            toUnlocked.hasExitTime = false;
            toUnlocked.duration = 0f;
            toUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            var unlockedToRemote = Utils.CreateTransition(unlockedState, remoteState);
            unlockedToRemote.hasExitTime = false;
            unlockedToRemote.duration = 0f;
            unlockedToRemote.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            if (Obfuscator.DecoyStatesEnabled && lockedShadowB != null)
            {
                var lockedAToB = Utils.CreateTransition(lockedState, lockedShadowB,
                    hasExitTime: true, exitTime: 999f);
                lockedAToB.duration = 0.1f;
                lockedAToB.AddCondition(AnimatorConditionMode.If, 0, "_SysBypassChk");
                var lockedBToC = Utils.CreateTransition(lockedShadowB, lockedShadowC,
                    hasExitTime: true, exitTime: 999f);
                lockedBToC.duration = 0.1f;
                lockedBToC.AddCondition(AnimatorConditionMode.If, 0, "_SysBypassChk");
                var lockedCToA = Utils.CreateTransition(lockedShadowC, lockedState,
                    hasExitTime: true, exitTime: 0.01f);
                lockedCToA.duration = 0f;
                var remoteLoop = Utils.CreateTransition(remoteState, remoteState,
                    hasExitTime: true, exitTime: 999f);
                remoteLoop.duration = 0f;
            }
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log("[ASS] Initial lock layer created");
            controller.AddLayer(layer);
        }
        #region Private Methods
        private AnimationClip CreateLockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = Obfuscator.IsEnabled ? Obfuscator.Clip("Lock") : "ASS_Lock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            var zeroScale = Vector3.zero;
            Debug.Log($"[ASS] Lock animation created (WD {(useWdOn ? "On" : "Off")} mode)");
            if (avatarRoot.transform.Find(GO_OVERLAY) != null)
            {
                clip.SetCurve(GO_OVERLAY, typeof(GameObject), "m_IsActive", enableCurve);
                Debug.Log("[ASS] Lock animation: enabled overlay (fullscreen mask + progress bar)");
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
                    string childPath = AnimationUtility.CalculateTransformPath(child, avatarRoot.transform);
                    clip.SetCurve(childPath, typeof(GameObject), "m_IsActive", disableCurve);
                    SetTransformScaleInClip(clip, childPath, zeroScale);
                    hiddenCount++;
                }
                Debug.Log($"[ASS] Lock animation: hidden {hiddenCount} root child objects (IsActive=0 + Scale=0)");
            }
            return clip;
        }
        private AnimationClip CreateRemoteClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = Obfuscator.IsEnabled ? Obfuscator.Clip("Remote") : "ASS_Remote" };
            SetGameObjectActiveInClip(clip, GO_OVERLAY, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);
            Debug.Log($"[ASS] Remote state animation created (WD {(useWdOn ? "On" : "Off")}): hide overlay and defense objects");
            return clip;
        }
        private AnimationClip CreateUnlockClip(bool useWdOn)
        {
            var clip = new AnimationClip { name = Obfuscator.IsEnabled ? Obfuscator.Clip("Unlock") : "ASS_Unlock" };
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            Debug.Log($"[ASS] Unlock animation created (WD {(useWdOn ? "On" : "Off")} mode)");
            SetGameObjectActiveInClip(clip, GO_OVERLAY, false);
            SetGameObjectActiveInClip(clip, GO_DEFENSE_ROOT, false);
            if (avatarRoot.transform.Find(GO_AUDIO_WARNING) != null)
                clip.SetCurve(GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", enableCurve);
            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", enableCurve);
            if (!useWdOn && config.disableRootChildren)
                WriteRestoreValues(clip);
            Debug.Log("[ASS] Unlock animation created (empty animation, allows objects to restore original state)");
            return clip;
        }
        private void WriteRestoreValues(AnimationClip clip)
        {
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            int restoredCount = 0;
            foreach (Transform child in avatarRoot.transform)
            {
                if (IsASSObject(child)) continue;
                string childPath = AnimationUtility.CalculateTransformPath(child, avatarRoot.transform);
                clip.SetCurve(childPath, typeof(GameObject), "m_IsActive",
                    child.gameObject.activeSelf ? enableCurve : disableCurve);
                SetTransformScaleInClip(clip, childPath, child.localScale);
                restoredCount++;
            }
            Debug.Log($"[ASS] WD Off restore: {restoredCount} root child objects (IsActive + Scale)");
        }
        private bool ResolveWriteDefaults()
        {
            if (config.writeDefaultsMode == ASSComponent.WriteDefaultsMode.On)
                return true;
            if (config.writeDefaultsMode == ASSComponent.WriteDefaultsMode.Off)
                return false;
            var externalWd = TryResolveFromExternalTools();
            if (externalWd.HasValue)
                return externalWd.Value;
            var controllers = new HashSet<AnimatorController>();
            if (descriptor != null)
            {
                foreach (var animLayer in descriptor.baseAnimationLayers
                    .Concat(descriptor.specialAnimationLayers))
                {
                    if (animLayer.isDefault) continue;
                    if (!(animLayer.animatorController is AnimatorController ac) || ac == null) continue;
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
        private bool? TryResolveFromExternalTools()
        {
            foreach (var mb in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb.GetType().FullName != "VF.Model.VRCFury") continue;
                var content = mb.GetType().GetField("content")?.GetValue(mb);
                if (content == null || content.GetType().Name != "FixWriteDefaults") continue;
                var modeName = content.GetType().GetField("mode")?.GetValue(content)?.ToString();
                switch (modeName)
                {
                    case "ForceOn":
                        Debug.Log("[ASS] WD Auto → On (VRCFury FixWriteDefaults = ForceOn)");
                        return true;
                    case "ForceOff":
                        Debug.Log("[ASS] WD Auto → Off (VRCFury FixWriteDefaults = ForceOff)");
                        return false;
                    case "Auto":
                        Debug.Log("[ASS] 检测到 VRCFury FixWriteDefaults = Auto，VRCFury 已统一 controller WD，扫描确认");
                        return null;
                    case "Disabled":
                        Debug.Log("[ASS] VRCFury FixWriteDefaults = Disabled，回退到 ASS 自动检测");
                        return null;
                }
            }
            return null;
        }
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
            if (_assObjectNames == null)
                _assObjectNames = new HashSet<string>
                {
                    GO_OVERLAY, GO_AUDIO_WARNING,
                    GO_AUDIO_SUCCESS, GO_DEFENSE_ROOT,
                    GO_OVERLAY_MESH
                };
            Transform current = obj;
            while (current != null && current != avatarRoot.transform)
            {
                if (_assObjectNames.Contains(current.name))
                    return true;
                current = current.parent;
            }
            return false;
        }
        private HashSet<string> _assObjectNames;
        private static void SetGameObjectActiveInClip(AnimationClip clip, string objectPath, bool isActive)
        {
            var curve = AnimationCurve.Constant(0f, 1f / 60f, isActive ? 1f : 0f);
            clip.SetCurve(objectPath, typeof(GameObject), "m_IsActive", curve);
        }
        private static void SetTransformScaleInClip(AnimationClip clip, string objectPath, Vector3 scale)
        {
            var curveX = AnimationCurve.Constant(0f, 1f / 60f, scale.x);
            var curveY = AnimationCurve.Constant(0f, 1f / 60f, scale.y);
            var curveZ = AnimationCurve.Constant(0f, 1f / 60f, scale.z);
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalScale.x", curveX);
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalScale.y", curveY);
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalScale.z", curveZ);
        }
        #endregion
    }
}
