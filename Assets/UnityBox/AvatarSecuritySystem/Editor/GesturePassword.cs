using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 手势密码验证系统生成器
    /// 功能：通过 VRChat 手势参数检测密码输入
    /// </summary>
    public class GesturePassword
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSComponent config;

        public GesturePassword(AnimatorController controller, GameObject avatarRoot, ASSComponent config)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
        }

        /// <summary>
        /// 创建手势密码验证层并添加到控制器
        /// 实现尾部序列匹配：用户输入的最后N位满足密码即可通过
        /// 
        /// 时间参数分配：
        ///   - Holding 状态：clip 时长 = gestureMaxHoldTime（单步总超时）
        ///     退出确认点在 holdTime/maxHoldTime 比例处（最小保持阈值）
        ///     超时退出在 exitTime=1.0（整步超时）
        ///   - Confirmed 状态：空 clip（0 时长，仅做逻辑中转，无额外超时）
        ///   - ErrorTolerance 状态：clip 时长 = gestureErrorTolerance（容错窗口）
        /// 
        /// Idle（手势值 0）不再有自循环逻辑，视为确定手势值参与条件判断。
        /// 密码配置已支持 0 值（Idle）。
        /// </summary>
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

            // 初始等待状态
            var waitState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Wait_Input") : "Wait_Input",
                new Vector3(50, 50, 0));
            waitState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            layer.stateMachine.defaultState = waitState;

            // 时间到失败状态
            var timeUpFailedState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("TimeUp_Failed") : "TimeUp_Failed",
                new Vector3(50, -50, 0));
            timeUpFailedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // Any State → TimeUp_Failed
            var anyToFailed = Utils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);

            // 成功状态（需要在循环前声明，因为循环中会引用）
            var successState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Success") : "Password_Success",
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            successState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // 第一阶段：创建所有状态
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
                // Holding: clip 时长 = gestureMaxHoldTime（整步超时）
                // 确认点位于 holdTime/maxHoldTime 比例处
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
                // Confirmed: 空 clip，仅做逻辑中转，无超时
                confirmedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
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

            // 第二阶段：创建所有转换
            for (int i = 0; i < password.Count; i++)
            {
                int gestureValue = password[i];
                bool isLastStep = (i == password.Count - 1);
                
                var holdingState = stepHoldingStates[i];
                var confirmedState = stepConfirmedStates[i];
                var errorToleranceState = stepErrorToleranceStates[i];

                // Holding → Confirmed (或 → Success 最后一步)
                // 退出点在 holdTime/maxHoldTime 比例处，手势保持超过最小阈值即确认
                float holdExitTime = (gestureHoldTime > 0f && gestureMaxHoldTime > 0f)
                    ? Mathf.Clamp01(gestureHoldTime / gestureMaxHoldTime)
                    : 1.0f;
                var holdToConfirm = Utils.CreateTransition(holdingState, 
                    isLastStep ? successState : confirmedState,
                    hasExitTime: true, exitTime: holdExitTime);
                // Greater(gv-1)+Less(gv+1) ≡ Equals(gv)，隐藏密码精确值
                holdToConfirm.AddCondition(AnimatorConditionMode.Greater, gestureValue - 1, gestureParam);
                holdToConfirm.AddCondition(AnimatorConditionMode.Less, gestureValue + 1, gestureParam);
                holdToConfirm.duration = 0f;

                // Holding → Wait（超时）
                var holdTimeout = Utils.CreateTransition(holdingState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                holdTimeout.duration = 0f;

                // Holding → Wait（错误手势：拆成 Less(gv)+Greater(gv) 替代 NotEqual）
                AddErrorTransitions(holdingState, waitState, gestureValue, gestureParam);

                if (i == 0)
                {
                    // Wait → Step_1_Holding（Greater+Less 替代 Equals）
                    var firstTransition = Utils.CreateTransition(waitState, holdingState);
                    Utils.AddIsLocalCondition(firstTransition, controller, isTrue: true);
                    firstTransition.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                    firstTransition.AddCondition(AnimatorConditionMode.Greater, gestureValue - 1, gestureParam);
                    firstTransition.AddCondition(AnimatorConditionMode.Less, gestureValue + 1, gestureParam);
                }
                else
                {
                    var previousConfirmed = stepConfirmedStates[i - 1];
                    var correctTransition = Utils.CreateTransition(previousConfirmed, holdingState);
                    ConfigureGestureTransition(correctTransition, gestureValue, gestureParam);
                }

                if (isLastStep) continue;

                // Confirmed → Step_{i+1}_Holding（下一正确手势）
                var confirmedToNext = Utils.CreateTransition(confirmedState, stepHoldingStates[i + 1]);
                ConfigureGestureTransition(confirmedToNext, password[i + 1], gestureParam);

                // Confirmed → Step_1_Holding（手势匹配首位密码时重新开始）
                // 必须在 ErrorTolerance 之前添加，确保重启优先于错误
                var firstGesture = password[0];
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var confirmedRestartTransition = confirmedState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(confirmedRestartTransition, firstGesture, gestureParam);
                }

                // Confirmed → ErrorTolerance（拆成 Less+Greater 替代 NotEqual）
                AddErrorTransitions(confirmedState, errorToleranceState, gestureValue, gestureParam);

                // ErrorTolerance → Step_{i+1}_Holding（容错内纠正为下一正确手势）
                var toleranceCorrectTransition = errorToleranceState.AddTransition(stepHoldingStates[i + 1]);
                ConfigureGestureTransition(toleranceCorrectTransition, password[i + 1], gestureParam);

                // ErrorTolerance → Step_1_Holding（容错内改回首位手势，重新开始）
                // 同样必须在 timeout 之前添加
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var toleranceRestartTransition = errorToleranceState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(toleranceRestartTransition, firstGesture, gestureParam);
                }

                // ErrorTolerance → Wait（容错时间用完，超时退出）
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

                var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
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

                    // 假入口：使用确定性与所有密码位不同的手势值
                    int fakeGesture = 1 + ((d * 3 + (int)(password[0]) + 1) % 7);
                    int safetyCount = 0;
                    while (password.Contains(fakeGesture) && safetyCount < 8)
                    {
                        fakeGesture = (fakeGesture % 7) + 1;
                        safetyCount++;
                    }
                    var decoyEntry = Utils.CreateTransition(waitState, decoyState);
                    decoyEntry.AddCondition(AnimatorConditionMode.Equals, fakeGesture, gestureParam);
                    decoyEntry.AddCondition(AnimatorConditionMode.If, 0, "_VerbLogLvl");
                }

                // 假状态之间链式转换（条件包含永假守卫）
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

                // 最后一个假状态 → Wait（死端）
                if (decoyHolds.Count > 0)
                {
                    var deadEnd = Utils.CreateTransition(decoyHolds[decoyHolds.Count - 1], waitState,
                        hasExitTime: true, exitTime: 1f);
                    deadEnd.duration = 0f;
                }

                // 在真 Hold 状态上添加自循环（双保险：手势≠密码 + 永假守卫参数）
                for (int i = 0; i < Mathf.Min(2, stepHoldingStates.Count); i++)
                {
                    var selfLoop = stepHoldingStates[i].AddTransition(stepHoldingStates[i]);
                    selfLoop.hasExitTime = true;
                    selfLoop.exitTime = 0.99f;
                    selfLoop.duration = 0f;
                    // 手势值确保与当前密码位不同
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
        
        private static void ConfigureGestureTransition(AnimatorStateTransition transition,
            int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            // Greater (gv-1) AND Less (gv+1) ≡ Equals (gv) for integer 0-7
            transition.AddCondition(AnimatorConditionMode.Greater, expectedGesture - 1, gestureParam);
            transition.AddCondition(AnimatorConditionMode.Less, expectedGesture + 1, gestureParam);
        }

        private static void AddErrorTransitions(AnimatorState holdingState, AnimatorState waitState,
            int expectedGesture, string gestureParam)
        {
            // 转换 1: 手势 < 预期值 → Wait
            var errLow = holdingState.AddTransition(waitState);
            errLow.hasExitTime = false;
            errLow.duration = 0f;
            errLow.hasFixedDuration = true;
            errLow.AddCondition(AnimatorConditionMode.Less, expectedGesture, gestureParam);

            // 转换 2: 手势 > 预期值 → Wait
            var errHigh = holdingState.AddTransition(waitState);
            errHigh.hasExitTime = false;
            errHigh.duration = 0f;
            errHigh.hasFixedDuration = true;
            errHigh.AddCondition(AnimatorConditionMode.Greater, expectedGesture, gestureParam);
        }
    }
}
