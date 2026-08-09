using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
using VRC.SDK3.Avatars.Components;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class Lock
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSConfigData config;
        public Lock(AnimatorController controller, GameObject avatarRoot, ASSConfigData config, VRCAvatarDescriptor descriptor)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
        }
        public void Generate()
        {
            if (config == null || !config.disableRootChildren)
            {
                Debug.Log("[ASS] Avatar lock disabled, skipping ASS_Lock layer generation");
                return;
            }

            // 确保 Blend Tree 和过渡条件依赖的参数存在
            Utils.AddParameterIfNotExists(controller, PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);

            var layer = Utils.CreateLayer(LAYER_LOCK, 1f);
            var remoteState = layer.stateMachine.AddState(
                Obfuscator.State("Remote"),
                new Vector3(200, 0, 0));
            // Remote 只关闭 ASS 自身遮罩，不应通过 WD 重置用户对象的 active/scale。
            remoteState.writeDefaultValues = false;
            var remoteClip = CreateRemoteClip();
            remoteState.motion = remoteClip;
            Utils.AddSubAsset(controller, remoteClip);

            var lockedState = layer.stateMachine.AddState(
                Obfuscator.State("LockedA"),
                new Vector3(200, 100, 0));
            lockedState.writeDefaultValues = false;
            var lockClip = CreateLockClip();
            Utils.AddSubAsset(controller, lockClip);
            lockedState.motion = lockClip;
            AnimatorState preLockState = null;
            List<AnimatorState> extraShadows = null;
            uint decoySeed = 0;
            if (Obfuscator.DecoyStatesEnabled)
            {
                decoySeed = Obfuscator.GetContextSeed("LockDecoy");
                uint rng = decoySeed;
                var guards = Obfuscator.GetGuardParamNames(2, "LockDecoy");
                string guardParamA = guards[0], guardParamB = guards[1];
                Utils.AddParameterIfNotExists(controller, guardParamA,
                    AnimatorControllerParameterType.Bool, false);
                Utils.AddParameterIfNotExists(controller, guardParamB,
                    AnimatorControllerParameterType.Bool, false);
                // 随机 1~4 个额外影子状态
                int shadowCount = Obfuscator.RngInt(ref rng, 1, 4);
                extraShadows = new List<AnimatorState>();
                float xBase = 350f;
                for (int s = 0; s < shadowCount; s++)
                {
                    var shadow = layer.stateMachine.AddState(
                        Obfuscator.State($"Shd_{s + 1}"),
                        new Vector3(xBase + s * 100, 100 + ((s & 1) * 50), 0));
                    shadow.writeDefaultValues = false;
                    shadow.motion = lockClip;
                    extraShadows.Add(shadow);
                }
                // preLock 作为诱饵（始终存在）
                preLockState = layer.stateMachine.AddState(
                    Obfuscator.State("PreLock"),
                    new Vector3(200, 0, 0));
                preLockState.writeDefaultValues = false;
            }
            var unlockedState = layer.stateMachine.AddState(
                Obfuscator.State("Unlocked"),
                new Vector3(200, 200, 0));
            // 稳定解锁状态不应继续写用户对象属性，避免抢占外部插件的 active/scale 控制权。
            unlockedState.writeDefaultValues = false;
            unlockedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            var unlockRestoreState = layer.stateMachine.AddState(
                Obfuscator.State("UnlockRestore"),
                new Vector3(200, 250, 0));
            // Restore clip 只采样一次恢复缩放，完成后立即进入上面的释放状态。
            unlockRestoreState.writeDefaultValues = false;
            layer.stateMachine.defaultState = remoteState;
            var unlockClip = CreateUnlockClip();
            Utils.AddSubAsset(controller, unlockClip);
            unlockRestoreState.motion = unlockClip;
            if (Obfuscator.DecoyStatesEnabled && extraShadows != null)
            {
                uint rng = decoySeed ^ 0xBEEF;
                string guardA = null, guardB = null;
                // 重新获取 guard 参数（与创建时一致）
                {
                    var reGuards = Obfuscator.GetGuardParamNames(2, "LockDecoy");
                    guardA = reGuards[0];
                    guardB = reGuards[1];
                }
                preLockState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
                // 每个影子状态赋值为 lockClip
                foreach (var sh in extraShadows)
                    sh.motion = lockClip;
                // 构建随机器械链：LockedA → Shd_1 → Shd_2 → ... → LockedA
                var prevState = lockedState;
                for (int i = 0; i < extraShadows.Count; i++)
                {
                    var guard = (i & 1) == 0 ? guardA : guardB;
                    var trans = Utils.CreateTransition(prevState, extraShadows[i],
                        hasExitTime: true, exitTime: 500f + Obfuscator.RngInt(ref rng, 0, 999));
                    trans.duration = 0.05f + Obfuscator.RngInt(ref rng, 0, 5) * 0.01f;
                    trans.AddCondition(AnimatorConditionMode.If, 0, guard);
                    prevState = extraShadows[i];
                }
                // 最后一个影子指回 LockedA
                if (extraShadows.Count > 0)
                {
                    var backTrans = Utils.CreateTransition(extraShadows[extraShadows.Count - 1], lockedState,
                        hasExitTime: true, exitTime: 0.01f);
                    backTrans.duration = 0f;
                }
                // Remote 自循环（exitTime 随机）
                var remoteLoop = Utils.CreateTransition(remoteState, remoteState,
                    hasExitTime: true, exitTime: 500f + Obfuscator.RngInt(ref rng, 0, 999));
                remoteLoop.duration = 0f;
            }
            // Remote → Locked: 仅本地进入隐藏锁定
            var remoteToLocked = Utils.CreateTransition(remoteState, lockedState);
            Utils.AddIsLocalCondition(remoteToLocked, controller, isTrue: true);
            remoteToLocked.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            // Remote → UnlockRestore: 如果密码已正确则直接跳过锁定
            var toUnlockRestoreDirect = Utils.CreateTransition(remoteState, unlockRestoreState);
            toUnlockRestoreDirect.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            if (Obfuscator.DecoyStatesEnabled && preLockState != null)
            {
                var toPreLock = Utils.CreateTransition(remoteState, preLockState);
                Utils.AddIsLocalCondition(toPreLock, controller, isTrue: true);
                toPreLock.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                var preLockToLocked = Utils.CreateTransition(preLockState, lockedState,
                    hasExitTime: true, exitTime: 0.01f);
                preLockToLocked.duration = 0f;
            }
            // Locked → UnlockRestore（解锁）
            var lockedToUnlockRestore = Utils.CreateTransition(lockedState, unlockRestoreState);
            lockedToUnlockRestore.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            // 恢复 clip 播放一个采样周期后，进入不写用户对象属性的稳定解锁状态。
            var unlockRestoreToUnlocked = Utils.CreateTransition(
                unlockRestoreState, unlockedState, hasExitTime: true, exitTime: 1f);
            unlockRestoreToUnlocked.duration = 0f;
            var unlockedToRemote = Utils.CreateTransition(unlockedState, remoteState);
            unlockedToRemote.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log("[ASS] Initial lock layer created");
            int layerIndex = Utils.AddLayerAndGetIndex(controller, layer);
            Utils.AddDisableSelfLayerControl(unlockedState, layerIndex, "ASS_Lock/Unlocked");
        }
        #region Private Methods
        private AnimationClip CreateLockClip()
        {
            var clip = new AnimationClip { name = CLIP_LOCK };
            var zeroScale = Vector3.zero;
            Debug.Log("[ASS] Lock animation created (WD Off, explicit scale bindings only)");
            // Lock clip 只隐藏身体；Overlay/音频由 PasswordInput 和 Audio 层管理。
            // defense 由 Defense 层独立管理，Lock 层不碰
            if (config.disableRootChildren)
            {
                int hiddenCount = 0;
                foreach (Transform child in avatarRoot.transform)
                {
                    if (IsASSObject(child)) continue;
                    string childPath = AnimationUtility.CalculateTransformPath(child, avatarRoot.transform);
                    // Lock 层固定使用 WD Off，仅通过根子对象缩放隐藏 Avatar。
                    // 不动画化 m_IsActive，避免覆盖衣柜/道具系统运行时计算的对象开关。
                    SetTransformScaleInClip(clip, childPath, zeroScale);
                    hiddenCount++;
                }
                Debug.Log($"[ASS] Lock animation: hidden {hiddenCount} root child objects (Scale=0 only, WD Off mode)");
            }
            return clip;
        }
        private AnimationClip CreateRemoteClip()
        {
            var clip = new AnimationClip { name = CLIP_REMOTE };
            // Overlay 与音频对象由 PasswordInput / Audio 层管理，Lock 层不写入。
            Debug.Log("[ASS] Remote state animation created (WD Off, no user-object feedback bindings)");
            return clip;
        }
        private AnimationClip CreateUnlockClip()
        {
            var clip = new AnimationClip { name = CLIP_UNLOCK };
            Debug.Log("[ASS] Unlock animation created (one-shot scale restore, WD Off mode)");
            if (config.disableRootChildren)
                WriteRestoreValues(clip);
            return clip;
        }
        private void WriteRestoreValues(AnimationClip clip)
        {
            int restoredCount = 0;
            foreach (Transform child in avatarRoot.transform)
            {
                if (IsASSObject(child)) continue;
                string childPath = AnimationUtility.CalculateTransformPath(child, avatarRoot.transform);
                // 只恢复 ASS 写入的缩放；active/scale 的后续控制权交还给外部动画层。
                SetTransformScaleInClip(clip, childPath, child.localScale);
                restoredCount++;
            }
            Debug.Log($"[ASS] UnlockRestore restore: {restoredCount} root child objects (Scale only, WD Off)");
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
