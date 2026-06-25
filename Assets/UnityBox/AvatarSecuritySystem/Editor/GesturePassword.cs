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
            var waitState = layer.stateMachine.AddState("Wait_Input", new Vector3(50, 50, 0));
            waitState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            layer.stateMachine.defaultState = waitState;

            // 时间到失败状态
            var timeUpFailedState = layer.stateMachine.AddState("TimeUp_Failed", new Vector3(50, -50, 0));
            timeUpFailedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // Any State → TimeUp_Failed
            var anyToFailed = Utils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);

            // 成功状态（需要在循环前声明，因为循环中会引用）
            var successState = layer.stateMachine.AddState("Password_Success", 
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            successState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // 第一阶段：创建所有状态
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);

                var holdingState = layer.stateMachine.AddState($"Step_{i + 1}_Holding", 
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                // Holding: clip 时长 = gestureMaxHoldTime（整步超时）
                // 确认点位于 holdTime/maxHoldTime 比例处
                var holdClip = CreateHoldClip($"ASS_Hold_{i + 1}", gestureMaxHoldTime);
                holdingState.motion = holdClip;
                Utils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);

                if (isLastStep)
                {
                    stepConfirmedStates.Add(null);
                    stepErrorToleranceStates.Add(null);
                    continue;
                }

                var confirmedState = layer.stateMachine.AddState($"Step_{i + 1}_Confirmed", 
                    new Vector3(50 + (i + 1) * 350, 150, 0));
                // Confirmed: 空 clip，仅做逻辑中转，无超时
                confirmedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
                stepConfirmedStates.Add(confirmedState);

                var errorToleranceState = layer.stateMachine.AddState($"Step_{i + 1}_ErrorTolerance", 
                    new Vector3(50 + (i + 1) * 350, 250, 0));
                var toleranceClip = CreateHoldClip($"ASS_Tolerance_{i + 1}", gestureErrorTolerance);
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
                holdToConfirm.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                holdToConfirm.duration = 0f;

                // Holding → Wait（超时：整步最大时间用完，exitTime=1.0）
                var holdTimeout = Utils.CreateTransition(holdingState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                holdTimeout.duration = 0f;

                // Holding → Wait（错误手势，包括 Idle(0) 均视为错误）
                var holdingErrorToWait = holdingState.AddTransition(waitState);
                ConfigureErrorTransition(holdingErrorToWait, gestureValue, gestureParam);

                if (i == 0)
                {
                    // Wait → Step_1_Holding（首个手势匹配即进入，Idle(0) 也参与匹配）
                    var firstTransition = Utils.CreateTransition(waitState, holdingState);
                    Utils.AddIsLocalCondition(firstTransition, controller, isTrue: true);
                    firstTransition.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
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

                // Confirmed → ErrorTolerance（任何非预期手势，包括 Idle(0)）
                var confirmedToTolerance = confirmedState.AddTransition(errorToleranceState);
                ConfigureErrorTransition(confirmedToTolerance, gestureValue, gestureParam);

                // Confirmed → Step_1_Holding（手势匹配首位密码时重新开始）
                var firstGesture = password[0];
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var confirmedRestartTransition = confirmedState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(confirmedRestartTransition, firstGesture, gestureParam);
                }

                // ErrorTolerance → Step_{i+1}_Holding（容错内纠正为下一正确手势）
                var toleranceCorrectTransition = errorToleranceState.AddTransition(stepHoldingStates[i + 1]);
                ConfigureGestureTransition(toleranceCorrectTransition, password[i + 1], gestureParam);

                // ErrorTolerance → Step_1_Holding（容错内改回首位手势，重新开始）
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
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);

            return clip;
        }
        
        private static void ConfigureErrorTransition(AnimatorStateTransition transition, int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.NotEqual, expectedGesture, gestureParam);
        }
        
        private static void ConfigureGestureTransition(AnimatorStateTransition transition, int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.Equals, expectedGesture, gestureParam);
        }
    }
}
