using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class GesturePassword
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSConfigData config;

        public GesturePassword(AnimatorController controller, GameObject avatarRoot, ASSConfigData config)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
        }

        public void Generate()
        {
            float gestureHoldTime = config.gestureHoldTime;
            float gestureMaxHoldTime = config.gestureMaxHoldTime;
            float gestureErrorTolerance = config.gestureErrorTolerance;
            var layer = Utils.CreateLayer(LAYER_PASSWORD_INPUT, 1f);
            var password = config.gesturePassword;
            if (password == null || password.Count == 0)
            {
                Debug.LogError("[ASS] Password sequence is empty, unable to create password layer");
                controller.AddLayer(layer);
                return;
            }
            string gestureParam = config.useRightHand ? 
                PARAM_GESTURE_RIGHT : PARAM_GESTURE_LEFT;
            Utils.EnsureBuiltInVRCParameters(controller,
                ensureIsLocal: true,
                ensureGestureParameters: true);
            Utils.AddParameterIfNotExists(controller, PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);

            var waitState = layer.stateMachine.AddState(
                Obfuscator.State("Wait_Input"),
                new Vector3(50, 50, 0));
            waitState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            waitState.writeDefaultValues = false;
            layer.stateMachine.defaultState = waitState;
            var inputWaitingState = layer.stateMachine.AddState(
                Obfuscator.State("InputWaiting"),
                new Vector3(50, 100, 0));
            var inputWaitingClip = CreateInputWaitingClip();
            inputWaitingState.motion = inputWaitingClip;
            inputWaitingState.writeDefaultValues = false;
            Utils.AddSubAsset(controller, inputWaitingClip);
            var waitToInputWaiting = Utils.CreateTransition(waitState, inputWaitingState);
            Utils.AddIsLocalCondition(waitToInputWaiting, controller, isTrue: true);
            waitToInputWaiting.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            var timeUpFailedState = layer.stateMachine.AddState(
                Obfuscator.State("TimeUp_Failed"),
                new Vector3(50, -50, 0));
            timeUpFailedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            timeUpFailedState.writeDefaultValues = false;
            var anyToFailed = Utils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            var successState = layer.stateMachine.AddState(
                Obfuscator.State("Success", "Password_Success"),
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            var successClip = CreateSuccessClip();
            successState.motion = successClip;
            successState.writeDefaultValues = false;
            Utils.AddSubAsset(controller, successClip);
            // Completed 状态：密码已正确输入的稳定终点
            var completedState = layer.stateMachine.AddState(
                Obfuscator.State("Completed", "Password_Completed"),
                new Vector3(50 + (password.Count + 2) * 350, 150, 0));
            var completedClip = CreateCompletedClip();
            completedState.motion = completedClip;
            completedState.writeDefaultValues = false;
            Utils.AddSubAsset(controller, completedClip);
            var releaseState = layer.stateMachine.AddState(
                Obfuscator.State("Release", "Password_Release"),
                new Vector3(50 + (password.Count + 3) * 350, 150, 0));
            releaseState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            releaseState.writeDefaultValues = false;
            // 本地 Avatar 若带有已保存的解锁参数，也走 Completed→Release，
            // 让“加载即解锁”和“输入密码后解锁”的最终层状态一致；
            // 该过渡只从 Wait_Input 出发，不会打断正在播放的 Success 音效。
            var savedLocalToCompleted = Utils.CreateTransition(waitState, completedState);
            Utils.AddIsLocalCondition(savedLocalToCompleted, controller, isTrue: true);
            savedLocalToCompleted.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            // successState → Completed（Success 保持 1 秒后再进入终点状态）
            var toCompleted = Utils.CreateTransition(successState, completedState,
                hasExitTime: true, exitTime: 1f);
            toCompleted.duration = 0f;
            var completedToRelease = Utils.CreateTransition(completedState, releaseState,
                hasExitTime: true, exitTime: 1f);
            completedToRelease.duration = 0f;
            // AnyState → Completed：远端看到密码已正确时直接跳转到终点
            var anyToCompleted = Utils.CreateAnyStateTransition(layer.stateMachine, completedState);
            Utils.AddIsLocalCondition(anyToCompleted, controller, isTrue: false);
            anyToCompleted.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);
                var holdingState = layer.stateMachine.AddState(
                    Obfuscator.State($"Hold_{i + 1}", $"Step_{i + 1}_Holding"),
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                holdingState.writeDefaultValues = false;
                var holdClip = CreateHoldClip(
                    Obfuscator.Clip($"Hold_{i + 1}", $"ASS_Hold_{i + 1}"), gestureMaxHoldTime);
                holdingState.motion = holdClip;
                Utils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);
                if (isLastStep)
                {
                    stepConfirmedStates.Add(null);
                    stepErrorToleranceStates.Add(null);
                    continue;
                }
                var confirmedState = layer.stateMachine.AddState(
                    Obfuscator.State($"Confirm_{i + 1}", $"Step_{i + 1}_Confirmed"),
                    new Vector3(50 + (i + 1) * 350, 150, 0));
                confirmedState.writeDefaultValues = false;
                // Confirmed: clip = gestureMaxHoldTime, 超时回到 Wait
                var confirmedClip = CreateHoldClip(
                    Obfuscator.Clip($"Confirm_{i + 1}", $"ASS_Confirmed_{i + 1}"), gestureMaxHoldTime);
                confirmedState.motion = confirmedClip;
                Utils.AddSubAsset(controller, confirmedClip);
                stepConfirmedStates.Add(confirmedState);
                var errorToleranceState = layer.stateMachine.AddState(
                    Obfuscator.State($"Tolerance_{i + 1}", $"Step_{i + 1}_ErrorTolerance"),
                    new Vector3(50 + (i + 1) * 350, 250, 0));
                errorToleranceState.writeDefaultValues = false;
                var toleranceClip = CreateHoldClip(
                    Obfuscator.Clip($"Tolerance_{i + 1}", $"ASS_Tolerance_{i + 1}"), gestureErrorTolerance);
                errorToleranceState.motion = toleranceClip;
                Utils.AddSubAsset(controller, toleranceClip);
                stepErrorToleranceStates.Add(errorToleranceState);
            }
            for (int i = 0; i < password.Count; i++)
            {
                int gestureValue = password[i];
                bool isLastStep = (i == password.Count - 1);
                var holdingState = stepHoldingStates[i];
                var confirmedState = stepConfirmedStates[i];
                var errorToleranceState = stepErrorToleranceStates[i];
                float holdExitTime = (gestureHoldTime > 0f && gestureMaxHoldTime > 0f)
                    ? Mathf.Clamp01(gestureHoldTime / gestureMaxHoldTime)
                    : 1.0f;
                var holdToConfirm = Utils.CreateTransition(holdingState, 
                    isLastStep ? successState : confirmedState,
                    hasExitTime: true, exitTime: holdExitTime);
                holdToConfirm.AddCondition(AnimatorConditionMode.Greater, gestureValue - 1, gestureParam);
                holdToConfirm.AddCondition(AnimatorConditionMode.Less, gestureValue + 1, gestureParam);
                holdToConfirm.duration = 0f;

                var holdTimeout = Utils.CreateTransition(holdingState, inputWaitingState,
                    hasExitTime: true, exitTime: 1.0f);
                holdTimeout.duration = 0f;

                // Holding → Wait（手势不匹配时超时重置，与确认同时触发防抖动）
                var holdingError = holdingState.AddTransition(inputWaitingState);
                holdingError.hasExitTime = true;
                holdingError.exitTime = holdExitTime;
                holdingError.duration = 0f;
                holdingError.hasFixedDuration = true;
                holdingError.AddCondition(AnimatorConditionMode.Less, gestureValue, gestureParam);
                holdingError.AddCondition(AnimatorConditionMode.Greater, gestureValue, gestureParam);

                if (i == 0)
                {
                    var firstTransition = Utils.CreateTransition(inputWaitingState, holdingState);
                    Utils.AddIsLocalCondition(firstTransition, controller, isTrue: true);
                    firstTransition.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                    firstTransition.AddCondition(AnimatorConditionMode.Greater, gestureValue - 1, gestureParam);
                    firstTransition.AddCondition(AnimatorConditionMode.Less, gestureValue + 1, gestureParam);
                }
                if (isLastStep) continue;
                var confirmedToNext = Utils.CreateTransition(confirmedState, stepHoldingStates[i + 1]);
                ConfigureGestureTransition(confirmedToNext, password[i + 1], gestureParam);

                // Confirmed → Wait（超时：gestureMaxHoldTime clip 播完则重置）
                var confirmedTimeout = Utils.CreateTransition(confirmedState, inputWaitingState,
                    hasExitTime: true, exitTime: 1.0f);
                confirmedTimeout.duration = 0f;

                // Error from confirmedState: check against the NEXT expected gesture (password[i+1]),
                // but exclude the JUST-CONFIRMED gesture (password[i]) via NotEqual so the user
                // can hold it without immediately erroring.
                var confirmedErrLow = confirmedState.AddTransition(errorToleranceState);
                confirmedErrLow.hasExitTime = false;
                confirmedErrLow.duration = 0f;
                confirmedErrLow.hasFixedDuration = true;
                confirmedErrLow.AddCondition(AnimatorConditionMode.Less, password[i + 1], gestureParam);
                confirmedErrLow.AddCondition(AnimatorConditionMode.NotEqual, gestureValue, gestureParam);
                var confirmedErrHigh = confirmedState.AddTransition(errorToleranceState);
                confirmedErrHigh.hasExitTime = false;
                confirmedErrHigh.duration = 0f;
                confirmedErrHigh.hasFixedDuration = true;
                confirmedErrHigh.AddCondition(AnimatorConditionMode.Greater, password[i + 1], gestureParam);
                confirmedErrHigh.AddCondition(AnimatorConditionMode.NotEqual, gestureValue, gestureParam);
                var toleranceCorrectTransition = errorToleranceState.AddTransition(stepHoldingStates[i + 1]);
                ConfigureGestureTransition(toleranceCorrectTransition, password[i + 1], gestureParam);

                var toleranceTimeoutToWait = Utils.CreateTransition(errorToleranceState, inputWaitingState,
                    hasExitTime: true, exitTime: 1.0f);
                toleranceTimeoutToWait.duration = 0f;
            }
            if (Obfuscator.DecoyStatesEnabled && stepHoldingStates.Count > 0)
            {
                uint rng = Obfuscator.GetContextSeed("GestureDecoy");
                var guards = Obfuscator.GetGuardParamNames(2, "GestureDecoy");
                string guardA = guards[0], guardB = guards[1];
                Utils.AddParameterIfNotExists(controller, guardA,
                    AnimatorControllerParameterType.Bool, false);
                Utils.AddParameterIfNotExists(controller, guardB,
                    AnimatorControllerParameterType.Bool, false);
                var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
                var decoyHolds = new List<AnimatorState>();
                int decoyCount = Mathf.Clamp(Obfuscator.RngInt(ref rng, 2, 4), 2, password.Count);
                for (int d = 0; d < decoyCount; d++)
                {
                    string decoyName = Obfuscator.State($"GDecoy_{d + 1}");
                    var decoyState = layer.stateMachine.AddState(decoyName,
                        new Vector3(-200 + d * 30, 200 + d * 80, 0));
                    decoyState.motion = emptyClip;
                    decoyState.writeDefaultValues = false;
                    decoyHolds.Add(decoyState);
                    var usedGestures = new HashSet<int>(password);
                    var unusedGestures = new List<int>();
                    for (int g = 0; g <= 7; g++)
                        if (!usedGestures.Contains(g))
                            unusedGestures.Add(g);
                    if (unusedGestures.Count == 0)
                        for (int g = 0; g <= 7; g++) unusedGestures.Add(g);
                    int fakeGesture = unusedGestures[Obfuscator.RngInt(ref rng, 0, unusedGestures.Count - 1)];
                    var guard = (d & 1) == 0 ? guardA : guardB;
                    var decoyEntry = Utils.CreateTransition(waitState, decoyState);
                    decoyEntry.AddCondition(AnimatorConditionMode.Equals, fakeGesture, gestureParam);
                    decoyEntry.AddCondition(AnimatorConditionMode.If, 0, guard);
                }
                for (int d = 0; d < decoyHolds.Count - 1; d++)
                {
                    var trans = decoyHolds[d].AddTransition(decoyHolds[d + 1]);
                    trans.hasExitTime = true;
                    trans.exitTime = 0.2f + Obfuscator.RngInt(ref rng, 0, 8) * 0.1f;
                    trans.duration = 0.05f + Obfuscator.RngInt(ref rng, 0, 3) * 0.05f;
                    int fakeG = Obfuscator.RngInt(ref rng, 0, 7);
                    var guard = (d & 1) == 0 ? guardA : guardB;
                    trans.AddCondition(AnimatorConditionMode.Equals, fakeG, gestureParam);
                    trans.AddCondition(AnimatorConditionMode.If, 0, guard);
                }
                if (decoyHolds.Count > 0)
                {
                    var deadEnd = Utils.CreateTransition(decoyHolds[decoyHolds.Count - 1], waitState,
                        hasExitTime: true, exitTime: 1f);
                    deadEnd.duration = 0f;
                }
                for (int i = 0; i < Mathf.Min(2, stepHoldingStates.Count); i++)
                {
                    var selfLoop = stepHoldingStates[i].AddTransition(stepHoldingStates[i]);
                    selfLoop.hasExitTime = true;
                    selfLoop.exitTime = 0.99f;
                    selfLoop.duration = 0f;
                    int safeGesture = 1 + ((i * 3 + password[i] + 2) % 7);
                    if (safeGesture == password[i]) safeGesture = (safeGesture % 7) + 1;
                    selfLoop.AddCondition(AnimatorConditionMode.Equals, safeGesture, gestureParam);
                    selfLoop.AddCondition(AnimatorConditionMode.If, 0, guardA);
                }
            }
            if (config.successSound != null)
            {
                Utils.AddPlayAudioBehaviour(successState, 
                    GO_AUDIO_SUCCESS, 
                    config.successSound);
            }
            Utils.AddParameterDriverBehaviour(successState, PARAM_PASSWORD_CORRECT, 1f, localOnly: false);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log($"[ASS] Gesture password layer created with stability check: " +
                     $"password length={password.Count}, min hold={gestureHoldTime}s, max hold={gestureMaxHoldTime}s, error tolerance={gestureErrorTolerance}s");
            int layerIndex = Utils.AddLayerAndGetIndex(controller, layer);
            Utils.AddDisableSelfLayerControl(releaseState, layerIndex, "ASS_PasswordInput/Release");
        }
        private AnimationClip CreateInputWaitingClip()
        {
            return CreateHoldClip(Obfuscator.Clip("InputWaiting", "ASS_InputWaiting"), 1f);
        }
        private AnimationClip CreateSuccessClip()
        {
            float successDuration = config.successSound != null
                ? Mathf.Max(1f / 60f, config.successSound.length)
                : 1f;
            var clip = CreateHoldClip(CLIP_PASSWORD_SUCCESS, successDuration);
            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
            {
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 1f / 60f, 1f));
            }
            return clip;
        }
        private AnimationClip CreateCompletedClip()
        {
            var clip = new AnimationClip
            {
                name = CLIP_PASSWORD_COMPLETED,
                legacy = false
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var disableCurve = AnimationCurve.Constant(0f, 1f, 0f);
            clip.SetCurve(Obfuscator.DummyPath(), typeof(GameObject), "m_IsActive", disableCurve);
            if (avatarRoot.transform.Find(GO_OVERLAY) != null)
                clip.SetCurve(GO_OVERLAY, typeof(GameObject), "m_IsActive", disableCurve);
            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive", disableCurve);
            if (avatarRoot.transform.Find(GO_AUDIO_WARNING) != null)
                clip.SetCurve(GO_AUDIO_WARNING, typeof(GameObject), "m_IsActive", disableCurve);
            return clip;
        }
        private AnimationClip CreateHoldClip(string name, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve(Obfuscator.DummyPath(), typeof(GameObject), "m_IsActive", dummyCurve);
            if (avatarRoot.transform.Find(GO_OVERLAY) != null)
            {
                clip.SetCurve(GO_OVERLAY, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, duration, 1f));
            }
            if (avatarRoot.transform.Find(GO_AUDIO_SUCCESS) != null)
            {
                // 预先在输入状态启用成功音效对象，确保进入 Success 时
                // VRCAnimatorPlayAudio 不会遇到尚未激活的 AudioSource。
                clip.SetCurve(GO_AUDIO_SUCCESS, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, duration, 1f));
            }
            return clip;
        }
        private void ConfigureGestureTransition(AnimatorStateTransition transition,
            int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.Greater, expectedGesture - 1, gestureParam);
            transition.AddCondition(AnimatorConditionMode.Less, expectedGesture + 1, gestureParam);
        }
    }
}
