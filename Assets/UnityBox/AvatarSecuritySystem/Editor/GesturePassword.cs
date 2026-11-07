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
        private readonly IAnimatorBehaviourAdapter behaviours;

        public GesturePassword(AnimatorController controller, GameObject avatarRoot, ASSConfigData config, IAnimatorBehaviourAdapter behaviours)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config.Clone();
            this.behaviours = behaviours ?? throw new System.ArgumentNullException(nameof(behaviours));
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
                Debug.LogError("[ASS] " + I18n.T("build.invalid_password"));
                controller.AddLayer(layer);
                return;
            }
            string gestureParam = config.useRightHand ? 
                PARAM_GESTURE_RIGHT : PARAM_GESTURE_LEFT;
            Utils.EnsureInputParameters(controller,
                ensureIsLocal: true,
                ensureGestureParameters: true);
            Utils.AddParameterIfNotExists(controller, PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);
            Utils.AddParameterIfNotExists(controller, PARAM_RESTART,
                AnimatorControllerParameterType.Bool, false);

            var waitState = layer.stateMachine.AddState(
                Obfuscator.State("Wait_Input"),
                new Vector3(50, 50, 0));
            waitState.motion = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
            waitState.writeDefaultValues = true;
            layer.stateMachine.defaultState = waitState;
            var inputWaitingState = layer.stateMachine.AddState(
                Obfuscator.State("InputWaiting"),
                new Vector3(50, 100, 0));
            var inputWaitingClip = CreateInputWaitingClip();
            inputWaitingState.motion = inputWaitingClip;
            inputWaitingState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, inputWaitingClip);
            var restarted = Utils.CreateAnyStateTransition(layer.stateMachine, inputWaitingState);
            restarted.AddCondition(AnimatorConditionMode.If, 0f, PARAM_RESTART);
            Utils.AddIsLocalCondition(restarted, controller, isTrue: true);
            var waitToInputWaiting = Utils.CreateTransition(waitState, inputWaitingState);
            Utils.AddIsLocalCondition(waitToInputWaiting, controller, isTrue: true);
            waitToInputWaiting.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            var timeUpFailedState = layer.stateMachine.AddState(
                Obfuscator.State("TimeUp_Failed"),
                new Vector3(50, -50, 0));
            timeUpFailedState.motion = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
            timeUpFailedState.writeDefaultValues = true;
            var retry = Utils.CreateTransition(timeUpFailedState, inputWaitingState);
            retry.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM_TIME_UP);
            var anyToFailed = Utils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            anyToFailed.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            var successState = layer.stateMachine.AddState(
                Obfuscator.State("Success", "Password_Success"),
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            var successClip = CreateSuccessClip();
            successState.motion = successClip;
            successState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, successClip);
            // Completed 状态：密码已正确输入的稳定终点
            var completedState = layer.stateMachine.AddState(
                Obfuscator.State("Completed", "Password_Completed"),
                new Vector3(50 + (password.Count + 2) * 350, 150, 0));
            var completedClip = CreateCompletedClip();
            completedState.motion = completedClip;
            completedState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, completedClip);
            var releaseState = layer.stateMachine.AddState(
                Obfuscator.State("Release", "Password_Release"),
                new Vector3(50 + (password.Count + 3) * 350, 150, 0));
            releaseState.motion = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
            releaseState.writeDefaultValues = true;
            var rearm = Utils.CreateTransition(releaseState, waitState);
            rearm.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM_PASSWORD_CORRECT);
            rearm.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM_TIME_UP);
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
            var stepInterruptedStates = new List<AnimatorState>();
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);
                var holdingState = layer.stateMachine.AddState(
                    Obfuscator.State($"Hold_{i + 1}", $"Step_{i + 1}_Holding"),
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                holdingState.writeDefaultValues = true;
                var holdClip = CreateHoldClip(
                    Obfuscator.Clip($"Hold_{i + 1}", $"ASS_Hold_{i + 1}"), gestureMaxHoldTime);
                holdingState.motion = holdClip;
                Utils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);
                var interruptedState = layer.stateMachine.AddState(
                    Obfuscator.State($"Interrupted_{i + 1}", $"Step_{i + 1}_Interrupted"),
                    new Vector3(50 + (i + 1) * 350, -50, 0));
                interruptedState.writeDefaultValues = true;
                var interruptedClip = CreateHoldClip(
                    Obfuscator.Clip($"Interrupted_{i + 1}", $"ASS_Interrupted_{i + 1}"),
                    Mathf.Max(gestureErrorTolerance, 1f / 60f));
                interruptedState.motion = interruptedClip;
                Utils.AddSubAsset(controller, interruptedClip);
                stepInterruptedStates.Add(interruptedState);
                if (isLastStep)
                {
                    stepConfirmedStates.Add(null);
                    stepErrorToleranceStates.Add(null);
                    continue;
                }
                var confirmedState = layer.stateMachine.AddState(
                    Obfuscator.State($"Confirm_{i + 1}", $"Step_{i + 1}_Confirmed"),
                    new Vector3(50 + (i + 1) * 350, 150, 0));
                confirmedState.writeDefaultValues = true;
                // Confirmed: clip = gestureMaxHoldTime, 超时回到 Wait
                var confirmedClip = CreateHoldClip(
                    Obfuscator.Clip($"Confirm_{i + 1}", $"ASS_Confirmed_{i + 1}"), gestureMaxHoldTime);
                confirmedState.motion = confirmedClip;
                Utils.AddSubAsset(controller, confirmedClip);
                stepConfirmedStates.Add(confirmedState);
                bool needsRepeatRelease = password[i + 1] == password[i];
                var errorToleranceState = layer.stateMachine.AddState(
                    needsRepeatRelease
                        ? Obfuscator.State($"RepeatRelease_{i + 1}", $"Step_{i + 1}_ReleaseForRepeat")
                        : Obfuscator.State($"Tolerance_{i + 1}", $"Step_{i + 1}_ErrorTolerance"),
                    new Vector3(50 + (i + 1) * 350, 250, 0));
                errorToleranceState.writeDefaultValues = true;
                var toleranceClip = CreateHoldClip(
                    Obfuscator.Clip($"Tolerance_{i + 1}", $"ASS_Tolerance_{i + 1}"),
                    needsRepeatRelease ? gestureMaxHoldTime : Mathf.Max(gestureErrorTolerance, 1f / 60f));
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
                var interruptedState = stepInterruptedStates[i];
                float holdExitTime = gestureHoldTime / gestureMaxHoldTime;

                // Expiration and mismatches win over acceptance at a sampled frame boundary.
                var holdTimeout = Utils.CreateTransition(holdingState, inputWaitingState,
                    hasExitTime: true, exitTime: 1f);
                holdTimeout.duration = 0f;
                foreach (var mismatch in new[] { AnimatorConditionMode.Less, AnimatorConditionMode.Greater })
                {
                    var holdingError = Utils.CreateTransition(holdingState,
                        gestureErrorTolerance > 0f ? interruptedState : inputWaitingState);
                    holdingError.AddCondition(mismatch, gestureValue, gestureParam);
                }
                var holdToConfirm = Utils.CreateTransition(holdingState,
                    isLastStep ? successState : confirmedState,
                    hasExitTime: true, exitTime: holdExitTime);
                holdToConfirm.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                holdToConfirm.duration = 0f;

                var interruptedTimeout = Utils.CreateTransition(interruptedState, inputWaitingState,
                    hasExitTime: true, exitTime: 1f);
                interruptedTimeout.duration = 0f;
                var resumed = Utils.CreateTransition(interruptedState, holdingState);
                ConfigureGestureTransition(resumed, gestureValue, gestureParam);

                if (i == 0)
                {
                    var firstTransition = Utils.CreateTransition(inputWaitingState, holdingState);
                    Utils.AddIsLocalCondition(firstTransition, controller, isTrue: true);
                    firstTransition.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                }
                if (isLastStep) continue;

                var confirmedTimeout = Utils.CreateTransition(confirmedState, inputWaitingState,
                    hasExitTime: true, exitTime: 1f);
                confirmedTimeout.duration = 0f;
                // Identical adjacent entries need a release and a fresh hold; one
                // long pose cannot count as several password entries.
                if (password[i + 1] != gestureValue)
                {
                    var confirmedToNext = Utils.CreateTransition(confirmedState, stepHoldingStates[i + 1]);
                    ConfigureGestureTransition(confirmedToNext, password[i + 1], gestureParam);
                }
                foreach (var mismatch in new[] { AnimatorConditionMode.Less, AnimatorConditionMode.Greater })
                {
                    bool repeatedEntry = password[i + 1] == gestureValue;
                    var transition = Utils.CreateTransition(confirmedState,
                        repeatedEntry || gestureErrorTolerance > 0f ? errorToleranceState : inputWaitingState);
                    transition.AddCondition(mismatch, password[i + 1], gestureParam);
                    transition.AddCondition(AnimatorConditionMode.NotEqual, gestureValue, gestureParam);
                    if (repeatedEntry)
                    {
                        transition.AddCondition(AnimatorConditionMode.Greater, -1f, gestureParam);
                        transition.AddCondition(AnimatorConditionMode.Less, 8f, gestureParam);
                    }
                }
                var toleranceTimeout = Utils.CreateTransition(errorToleranceState, inputWaitingState,
                    hasExitTime: true, exitTime: 1f);
                toleranceTimeout.duration = 0f;
                var nextAfterTolerance = Utils.CreateTransition(errorToleranceState, stepHoldingStates[i + 1]);
                ConfigureGestureTransition(nextAfterTolerance, password[i + 1], gestureParam);
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
                var emptyClip = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
                var decoyHolds = new List<AnimatorState>();
                int decoyCount = Mathf.Clamp(Obfuscator.RngInt(ref rng, 2, 4), 2, password.Count);
                for (int d = 0; d < decoyCount; d++)
                {
                    string decoyName = Obfuscator.State($"GDecoy_{d + 1}");
                    var decoyState = layer.stateMachine.AddState(decoyName,
                        new Vector3(-200 + d * 30, 200 + d * 80, 0));
                    decoyState.motion = emptyClip;
                    decoyState.writeDefaultValues = true;
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
                behaviours.AddPlayAudioBehaviour(successState,
                    GO_AUDIO_SUCCESS, 
                    config.successSound);
            }
            behaviours.AddParameterDriverBehaviour(successState, PARAM_PASSWORD_CORRECT, 1f, localOnly: false);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log($"[ASS] Gesture password layer created with stability check: " +
                     $"password length={password.Count}, min hold={gestureHoldTime}s, max hold={gestureMaxHoldTime}s, error tolerance={gestureErrorTolerance}s");
            int layerIndex = Utils.AddLayerAndGetIndex(controller, layer);
            behaviours.AddDisableSelfLayerControl(releaseState, layerIndex, "ASS_PasswordInput/Release");
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
