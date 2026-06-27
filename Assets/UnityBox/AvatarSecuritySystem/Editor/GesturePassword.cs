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
        private readonly ASSComponent config;
        private uint _avatarHash;
        public GesturePassword(AnimatorController controller, GameObject avatarRoot, ASSComponent config)
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
                ensureIsLocal: false,
                ensureGestureParameters: true);
            Utils.AddParameterIfNotExists(controller, PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);
            uint avatarHash = (uint)avatarRoot.name.GetHashCode();
            _avatarHash = avatarHash;
            var waitState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Wait_Input") : "Wait_Input",
                new Vector3(50, 50, 0));
            waitState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            layer.stateMachine.defaultState = waitState;
            var timeUpFailedState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("TimeUp_Failed") : "TimeUp_Failed",
                new Vector3(50, -50, 0));
            timeUpFailedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            var anyToFailed = Utils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            var successState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Success") : "Password_Success",
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            successState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);
                string holdingName = Obfuscator.IsEnabled
                    ? Obfuscator.State($"Hold_{i + 1}")
                    : $"Step_{i + 1}_Holding";
                var holdingState = layer.stateMachine.AddState(holdingName,
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                string holdClipName = Obfuscator.IsEnabled
                    ? Obfuscator.Clip($"Hold_{i + 1}")
                    : $"ASS_Hold_{i + 1}";
                var holdClip = CreateHoldClip(holdClipName, gestureMaxHoldTime);
                holdingState.motion = holdClip;
                Utils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);
                if (isLastStep)
                {
                    stepConfirmedStates.Add(null);
                    stepErrorToleranceStates.Add(null);
                    continue;
                }
                string confirmedName = Obfuscator.IsEnabled
                    ? Obfuscator.State($"Confirm_{i + 1}")
                    : $"Step_{i + 1}_Confirmed";
                var confirmedState = layer.stateMachine.AddState(confirmedName,
                    new Vector3(50 + (i + 1) * 350, 150, 0));
                confirmedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
                stepConfirmedStates.Add(confirmedState);
                string toleranceName = Obfuscator.IsEnabled
                    ? Obfuscator.State($"Tolerance_{i + 1}")
                    : $"Step_{i + 1}_ErrorTolerance";
                var errorToleranceState = layer.stateMachine.AddState(toleranceName,
                    new Vector3(50 + (i + 1) * 350, 250, 0));
                string toleranceClipName = Obfuscator.IsEnabled
                    ? Obfuscator.Clip($"Tolerance_{i + 1}")
                    : $"ASS_Tolerance_{i + 1}";
                var toleranceClip = CreateHoldClip(toleranceClipName, gestureErrorTolerance);
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
                AddGestureConditions(holdToConfirm, gestureValue, gestureParam);
                holdToConfirm.duration = 0f;
                var holdTimeout = Utils.CreateTransition(holdingState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                holdTimeout.duration = 0f;
                // Error transitions with grace period: use hasExitTime=true with same exitTime
                // as holdToConfirm, preventing single-frame gesture glitches from
                // triggering immediate reset (which causes Wait↔Holding ping-pong)
                int hlNoiseLow = (int)((_avatarHash >> (gestureValue * 3)) & 1);
                int hlNoiseHigh = (int)((_avatarHash >> (gestureValue * 3 + 1)) & 1);
                var errLow = holdingState.AddTransition(waitState);
                errLow.hasExitTime = true;
                errLow.exitTime = holdExitTime;
                errLow.duration = 0f;
                errLow.hasFixedDuration = true;
                errLow.AddCondition(AnimatorConditionMode.Less, gestureValue - hlNoiseLow, gestureParam);
                var errHigh = holdingState.AddTransition(waitState);
                errHigh.hasExitTime = true;
                errHigh.exitTime = holdExitTime;
                errHigh.duration = 0f;
                errHigh.hasFixedDuration = true;
                errHigh.AddCondition(AnimatorConditionMode.Greater, gestureValue + hlNoiseHigh, gestureParam);
                if (i == 0)
                {
                    var firstTransition = Utils.CreateTransition(waitState, holdingState);
                    Utils.AddIsLocalCondition(firstTransition, controller, isTrue: true);
                    firstTransition.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                    AddGestureConditions(firstTransition, gestureValue, gestureParam);
                }
                else
                {
                    var previousConfirmed = stepConfirmedStates[i - 1];
                    var correctTransition = Utils.CreateTransition(previousConfirmed, holdingState);
                    ConfigureGestureTransition(correctTransition, gestureValue, gestureParam);
                }
                if (isLastStep) continue;
                var confirmedToNext = Utils.CreateTransition(confirmedState, stepHoldingStates[i + 1]);
                ConfigureGestureTransition(confirmedToNext, password[i + 1], gestureParam);
                var firstGesture = password[0];
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var confirmedRestartTransition = confirmedState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(confirmedRestartTransition, firstGesture, gestureParam);
                }
                // Stay in confirmedState if still holding a gesture that was accepted
                // by the confirmation transition. Must use the SAME gesture conditions
                // (AddGestureConditions with hash noise) as holdToConfirm to avoid
                // asymmetry: if a nearby gesture was accepted for entry, it must also
                // be accepted for staying, otherwise the state enters Confirmed then
                // immediately errors out → infinite Wait→Holding→Confirmed→Error→Wait loop.
                // Added BEFORE error transitions so it takes priority.
                var confirmedStay = confirmedState.AddTransition(confirmedState);
                confirmedStay.hasExitTime = false;
                confirmedStay.duration = 0f;
                confirmedStay.hasFixedDuration = true;
                AddGestureConditions(confirmedStay, gestureValue, gestureParam);
                // Error from confirmedState: check against the NEXT expected gesture (password[i+1]),
                // not the current one. The confirmedRestartTransition (to password[0]) is added
                // before these error transitions, so it takes priority for the restart gesture.
                AddErrorTransitions(confirmedState, errorToleranceState, password[i + 1], gestureParam);
                var toleranceCorrectTransition = errorToleranceState.AddTransition(stepHoldingStates[i + 1]);
                ConfigureGestureTransition(toleranceCorrectTransition, password[i + 1], gestureParam);
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var toleranceRestartTransition = errorToleranceState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(toleranceRestartTransition, firstGesture, gestureParam);
                }
                var toleranceTimeoutToWait = Utils.CreateTransition(errorToleranceState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                toleranceTimeoutToWait.duration = 0f;
            }
            if (Obfuscator.DecoyStatesEnabled && stepHoldingStates.Count > 0)
            {
                Utils.AddParameterIfNotExists(controller, "_VerbLogLvl",
                    AnimatorControllerParameterType.Bool, false);
                Utils.AddParameterIfNotExists(controller, "_ProfilerEn",
                    AnimatorControllerParameterType.Bool, false);
                var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
                var decoyHolds = new List<AnimatorState>();
                int decoyCount = Mathf.Min(3, password.Count);
                for (int d = 0; d < decoyCount; d++)
                {
                    string decoyName = Obfuscator.State($"Decoy_{d + 1}");
                    var decoyState = layer.stateMachine.AddState(decoyName,
                        new Vector3(-200, 200 + d * 80, 0));
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
                    int fakeGesture = unusedGestures[(d * 3 + password[0] + 1) % unusedGestures.Count];
                    var decoyEntry = Utils.CreateTransition(waitState, decoyState);
                    decoyEntry.AddCondition(AnimatorConditionMode.Equals, fakeGesture, gestureParam);
                    decoyEntry.AddCondition(AnimatorConditionMode.If, 0, "_VerbLogLvl");
                }
                for (int d = 0; d < decoyHolds.Count - 1; d++)
                {
                    var trans = decoyHolds[d].AddTransition(decoyHolds[d + 1]);
                    trans.hasExitTime = true;
                    trans.exitTime = 0.5f;
                    trans.duration = 0.1f;
                    int fg = 1 + ((d * 5 + 3) % 7);
                    trans.AddCondition(AnimatorConditionMode.Equals, fg, gestureParam);
                    trans.AddCondition(AnimatorConditionMode.If, 0, "_ProfilerEn"); // 永假守卫
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
                    selfLoop.AddCondition(AnimatorConditionMode.If, 0, "_ProfilerEn");
                }
            }
            if (config.successSound != null)
            {
                Utils.AddPlayAudioBehaviour(successState, 
                    GO_AUDIO_SUCCESS, 
                    config.successSound);
            }
            Utils.AddParameterDriverBehaviour(successState, PARAM_PASSWORD_CORRECT, 1f, localOnly: true);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log($"[ASS] Gesture password layer created with stability check: " +
                     $"password length={password.Count}, min hold={gestureHoldTime}s, max hold={gestureMaxHoldTime}s, error tolerance={gestureErrorTolerance}s");
            controller.AddLayer(layer);
        }
        private static AnimationClip CreateHoldClip(string name, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve(Obfuscator.DummyPath(), typeof(GameObject), "m_IsActive", dummyCurve);
            return clip;
        }
        private void ConfigureGestureTransition(AnimatorStateTransition transition,
            int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            AddGestureConditions(transition, expectedGesture, gestureParam);
        }
        private void AddGestureConditions(AnimatorStateTransition transition,
            int expectedGesture, string gestureParam)
        {
            int noiseLow = (int)((_avatarHash >> (expectedGesture * 3)) & 1);
            int noiseHigh = (int)((_avatarHash >> (expectedGesture * 3 + 1)) & 1);
            transition.AddCondition(AnimatorConditionMode.Greater, expectedGesture - 1 - noiseLow, gestureParam);
            transition.AddCondition(AnimatorConditionMode.Less, expectedGesture + 1 + noiseHigh, gestureParam);
        }
        private void AddErrorTransitions(AnimatorState holdingState, AnimatorState waitState,
            int expectedGesture, string gestureParam)
        {
            int noiseLow = (int)((_avatarHash >> (expectedGesture * 3)) & 1);
            int noiseHigh = (int)((_avatarHash >> (expectedGesture * 3 + 1)) & 1);
            var errLow = holdingState.AddTransition(waitState);
            errLow.hasExitTime = false;
            errLow.duration = 0f;
            errLow.hasFixedDuration = true;
            errLow.AddCondition(AnimatorConditionMode.Less, expectedGesture - noiseLow, gestureParam);
            var errHigh = holdingState.AddTransition(waitState);
            errHigh.hasExitTime = false;
            errHigh.duration = 0f;
            errHigh.hasFixedDuration = true;
            errHigh.AddCondition(AnimatorConditionMode.Greater, expectedGesture + noiseHigh, gestureParam);
        }
    }
}
