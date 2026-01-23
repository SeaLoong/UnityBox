using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using SeaLoongUnityBox;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 手势密码验证系统生成器
    /// 功能：通过 VRChat 手势参数检测密码输入
    /// </summary>
    public static class GesturePasswordSystem
    {
        /// <summary>
        /// 创建手势密码验证层
        /// 实现尾部序列匹配：用户输入的最后N位满足密码即可通过（例如：密码456，输入123456也正确）
        /// 增强特性：
        /// 1. 手势稳定时间：需要保持手势一定时间才计入
        /// 2. 忽略Idle手势(0)：允许在正确手势之间短暂回到Idle
        /// 4. 容错机制：短时间输入错误手势后仍有机会继续，而不是立即重置
        /// 使用 VRC State Behaviours 播放音效和设置参数
        /// 时间到后强制进入失败状态，禁止继续输入
        /// </summary>
        public static AnimatorControllerLayer CreatePasswordLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            // 从配置中读取时间参数
            float gestureHoldTime = config.gestureHoldTime;
            float gestureErrorTolerance = config.gestureErrorTolerance;

            var layer = AnimatorUtils.CreateLayer(Constants.LAYER_PASSWORD_INPUT, 1f);
            var password = config.gesturePassword;
            
            if (password == null || password.Count == 0)
            {
                Debug.LogError(I18n.T("log.password_empty"));
                return layer;
            }

            string gestureParam = config.useRightHand ? 
                Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT;

            // 添加必要的参数
            AnimatorUtils.AddParameterIfNotExists(controller, Constants.PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);
            AnimatorUtils.AddParameterIfNotExists(controller, Constants.PARAM_PASSWORD_ERROR,
                AnimatorControllerParameterType.Trigger);

            // 创建初始等待状态（也作为任意位置的起始状态）
            var waitState = layer.stateMachine.AddState("Wait_Input", new Vector3(50, 50, 0));
            waitState.motion = AnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = waitState;

            // 创建时间到失败状态（禁止输入）
            var timeUpFailedState = layer.stateMachine.AddState("TimeUp_Failed", new Vector3(50, -50, 0));
            timeUpFailedState.motion = AnimatorUtils.SharedEmptyClip;

#if VRC_SDK_VRCSDK3
            // 播放错误音效
            if (config.errorSound != null)
            {
                AnimatorUtils.AddPlayAudioBehaviour(timeUpFailedState, 
                    Constants.GO_FEEDBACK_AUDIO, 
                    config.errorSound);
            }
#endif

            // Any State → TimeUp_Failed（时间到后强制进入失败状态）
            var anyToFailed = AnimatorUtils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            // 创建密码输入状态链（每个步骤分为Holding、Confirmed和ErrorTolerance三个状态）
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            
            for (int i = 0; i < password.Count; i++)
            {
                int gestureValue = password[i];
                bool isLastStep = (i == password.Count - 1);
                
                // 创建Holding状态（手势保持中，需要稳定时间）
                var holdingState = layer.stateMachine.AddState($"Step_{i + 1}_Holding", 
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                var holdClip = CreateHoldClip($"ASS_Hold_{i + 1}", gestureHoldTime);
                holdingState.motion = holdClip;
                AnimatorUtils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);

                // 创建Confirmed状态（手势已确认，等待下一位输入）
                var confirmedState = layer.stateMachine.AddState($"Step_{i + 1}_Confirmed", 
                    new Vector3(50 + (i + 1) * 350, 150, 0));
                confirmedState.motion = AnimatorUtils.SharedEmptyClip;
                stepConfirmedStates.Add(confirmedState);

                // 创建ErrorTolerance状态（容错缓冲：短暂误触后仍可继续）
                var errorToleranceState = layer.stateMachine.AddState($"Step_{i + 1}_ErrorTolerance", 
                    new Vector3(50 + (i + 1) * 350, 250, 0));
                var toleranceClip = CreateHoldClip($"ASS_Tolerance_{i + 1}", gestureErrorTolerance);
                errorToleranceState.motion = toleranceClip;
                AnimatorUtils.AddSubAsset(controller, toleranceClip);
                stepErrorToleranceStates.Add(errorToleranceState);

#if VRC_SDK_VRCSDK3
                // 在Confirmed状态播放提示音（如果配置了且不是最后一步）
                if (config.stepSuccessSound != null && !isLastStep)
                {
                    AnimatorUtils.AddPlayAudioBehaviour(confirmedState, 
                        Constants.GO_FEEDBACK_AUDIO, 
                        config.stepSuccessSound);
                }
#endif

                // Holding → Confirmed（手势保持足够时间后确认）
                var holdToConfirm = AnimatorUtils.CreateTransition(holdingState, confirmedState,
                    hasExitTime: true, exitTime: 1.0f);
                holdToConfirm.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                holdToConfirm.duration = 0f;

                // Holding状态内的自循环（保持当前手势时继续持有）
                var holdingSelfLoop = AnimatorUtils.CreateTransition(holdingState, holdingState,
                    hasExitTime: true, exitTime: 1.0f);
                holdingSelfLoop.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                holdingSelfLoop.duration = 0f;

                // 从上一个状态转换到当前Holding状态
                if (i == 0)
                {
                    // 第一步：从waitState开始（手势不是Idle）
                    var firstTransition = AnimatorUtils.CreateTransition(waitState, holdingState);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    firstTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam); // 排除Idle
                }
                else
                {
                    // 后续步骤：从上一个Confirmed状态转换
                    var previousConfirmed = stepConfirmedStates[i - 1];
                    var correctTransition = AnimatorUtils.CreateTransition(previousConfirmed, holdingState);
                    correctTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    correctTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam); // 排除Idle
                    
                    // 从上一个ErrorTolerance状态也可以转到当前Holding（容错期内输入正确下一位）
                    var previousTolerance = stepErrorToleranceStates[i - 1];
                    var toleranceCorrectTransition = AnimatorUtils.CreateTransition(previousTolerance, holdingState);
                    toleranceCorrectTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    toleranceCorrectTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                    toleranceCorrectTransition.duration = 0f;
                    
                    // Confirmed状态允许短暂回到Idle而不重置（自循环）
                    var confirmedIdleLoop = AnimatorUtils.CreateTransition(previousConfirmed, previousConfirmed);
                    confirmedIdleLoop.AddCondition(AnimatorConditionMode.Equals, 0, gestureParam);
                    confirmedIdleLoop.duration = 0f;
                }

                // 错误手势处理：进入容错状态或根据尾部匹配重新开始
                CreateGestureErrorTransitions(holdingState, confirmedState, errorToleranceState, waitState,
                    stepHoldingStates, gestureParam, i, password, gestureValue);
            }

            // 最后一步成功：设置密码正确标志
            var successState = layer.stateMachine.AddState("Password_Success", 
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            successState.motion = AnimatorUtils.SharedEmptyClip;

#if VRC_SDK_VRCSDK3
            // 播放成功音效
            if (config.successSound != null)
            {
                AnimatorUtils.AddPlayAudioBehaviour(successState, 
                    Constants.GO_FEEDBACK_AUDIO, 
                    config.successSound);
            }
#endif
            
            // 设置密码正确参数（使用 Parameter Driver）
            AnimatorUtils.AddParameterDriverBehaviour(successState, Constants.PARAM_PASSWORD_CORRECT, 1f, localOnly: true);

            // 最后一步Confirmed → Success
            var finalTransition = AnimatorUtils.CreateTransition(
                stepConfirmedStates[stepConfirmedStates.Count - 1], successState);
            finalTransition.AddCondition(AnimatorConditionMode.Equals, password[password.Count - 1], gestureParam);
            
            // 最后一步ErrorTolerance → Success（容错期内输入最后一位也算成功）
            var finalToleranceTransition = AnimatorUtils.CreateTransition(
                stepErrorToleranceStates[stepErrorToleranceStates.Count - 1], successState);
            finalToleranceTransition.AddCondition(AnimatorConditionMode.Equals, password[password.Count - 1], gestureParam);
            finalToleranceTransition.duration = 0f;

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log($"[ASS] Gesture password layer created with stability check: " +
                     $"password length={password.Count}, hold time={gestureHoldTime}s, error tolerance={gestureErrorTolerance}s");
            return layer;
        }

        /// <summary>
        /// 创建手势保持动画剪辑（空动画，用于稳定时间检测）
        /// </summary>
        private static AnimationClip CreateHoldClip(string name, float duration)
        {
            var clip = new AnimationClip 
            { 
                name = name,
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 添加虚拟曲线确保动画长度
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);

            return clip;
        }

        /// <summary>
        /// 创建手势错误转换（带容错机制）
        /// 1. 忽略Idle手势(0)
        /// 2. 允许尾部匹配（可以从任意步骤跳到密码第一位重新开始）
        /// 3. 错误手势进入容错状态，在容错期内可以纠正继续，超时后才重置
        /// </summary>
        private static void CreateGestureErrorTransitions(
            AnimatorState holdingState,
            AnimatorState confirmedState,
            AnimatorState errorToleranceState,
            AnimatorState waitState,
            List<AnimatorState> allHoldingStates,
            string gestureParam,
            int currentIndex,
            List<int> password,
            int currentGesture)
        {
            int firstGesture = password[0];
            int nextGesture = currentIndex < password.Count - 1 ? password[currentIndex + 1] : -1;

            // ===== Holding状态错误处理 =====
            
            // 1. 如果手势变成Idle，保持在Holding（允许短暂松开）
            var holdingIdleLoop = AnimatorUtils.CreateTransition(holdingState, holdingState);
            holdingIdleLoop.AddCondition(AnimatorConditionMode.Equals, 0, gestureParam);
            holdingIdleLoop.duration = 0f;

            // 2. 如果手势是密码第一位且不是当前步骤，跳到第一步（尾部匹配）
            if (firstGesture != currentGesture && allHoldingStates.Count > 0)
            {
                var holdingRestartTransition = holdingState.AddTransition(allHoldingStates[0]);
                holdingRestartTransition.hasExitTime = false;
                holdingRestartTransition.duration = 0f;
                holdingRestartTransition.AddCondition(AnimatorConditionMode.Equals, firstGesture, gestureParam);
                holdingRestartTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                holdingRestartTransition.hasFixedDuration = true;
            }
            
            // 3. 其他错误手势回到Wait
            var holdingErrorTransition = holdingState.AddTransition(waitState);
            holdingErrorTransition.hasExitTime = false;
            holdingErrorTransition.duration = 0f;
            holdingErrorTransition.AddCondition(AnimatorConditionMode.NotEqual, currentGesture, gestureParam);
            holdingErrorTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam); // 不是Idle
            if (nextGesture >= 0)
            {
                holdingErrorTransition.AddCondition(AnimatorConditionMode.NotEqual, nextGesture, gestureParam);
            }
            if (firstGesture != currentGesture)
            {
                holdingErrorTransition.AddCondition(AnimatorConditionMode.NotEqual, firstGesture, gestureParam);
            }
            holdingErrorTransition.hasFixedDuration = true;

            // ===== Confirmed状态错误处理（容错核心） =====
            
            // 1. 如果手势是密码第一位，跳到第一步（尾部匹配，任何时候都可以重新开始）
            if (firstGesture != currentGesture && allHoldingStates.Count > 0)
            {
                var confirmedRestartTransition = confirmedState.AddTransition(allHoldingStates[0]);
                confirmedRestartTransition.hasExitTime = false;
                confirmedRestartTransition.duration = 0f;
                confirmedRestartTransition.AddCondition(AnimatorConditionMode.Equals, firstGesture, gestureParam);
                confirmedRestartTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                confirmedRestartTransition.hasFixedDuration = true;
            }

            // 2. 错误手势进入容错状态（不是Idle、不是下一位、不是第一位）
            var confirmedToTolerance = confirmedState.AddTransition(errorToleranceState);
            confirmedToTolerance.hasExitTime = false;
            confirmedToTolerance.duration = 0f;
            confirmedToTolerance.AddCondition(AnimatorConditionMode.NotEqual, currentGesture, gestureParam);
            confirmedToTolerance.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam); // 不是Idle
            if (nextGesture >= 0)
            {
                confirmedToTolerance.AddCondition(AnimatorConditionMode.NotEqual, nextGesture, gestureParam);
            }
            if (firstGesture != currentGesture)
            {
                confirmedToTolerance.AddCondition(AnimatorConditionMode.NotEqual, firstGesture, gestureParam);
            }
            confirmedToTolerance.hasFixedDuration = true;

            // ===== ErrorTolerance状态处理（容错期） =====
            
            // 1. 容错期允许Idle（自循环，不计时）
            var toleranceIdleLoop = AnimatorUtils.CreateTransition(errorToleranceState, errorToleranceState);
            toleranceIdleLoop.AddCondition(AnimatorConditionMode.Equals, 0, gestureParam);
            toleranceIdleLoop.duration = 0f;

            // 2. 容错期内输入密码第一位，重新开始（尾部匹配）
            if (allHoldingStates.Count > 0)
            {
                var toleranceRestartTransition = errorToleranceState.AddTransition(allHoldingStates[0]);
                toleranceRestartTransition.hasExitTime = false;
                toleranceRestartTransition.duration = 0f;
                toleranceRestartTransition.AddCondition(AnimatorConditionMode.Equals, firstGesture, gestureParam);
                toleranceRestartTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                toleranceRestartTransition.hasFixedDuration = true;
            }

            // 3. 容错期超时或继续错误手势，回到Wait
            var toleranceToWait = AnimatorUtils.CreateTransition(errorToleranceState, waitState,
                hasExitTime: true, exitTime: 1.0f);
            toleranceToWait.duration = 0f;
            
            // 4. 容错期内输入其他错误手势（非Idle、非下一位、非第一位），立即回Wait
            var toleranceErrorToWait = errorToleranceState.AddTransition(waitState);
            toleranceErrorToWait.hasExitTime = false;
            toleranceErrorToWait.duration = 0f;
            toleranceErrorToWait.AddCondition(AnimatorConditionMode.NotEqual, currentGesture, gestureParam);
            toleranceErrorToWait.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
            if (nextGesture >= 0)
            {
                toleranceErrorToWait.AddCondition(AnimatorConditionMode.NotEqual, nextGesture, gestureParam);
            }
            toleranceErrorToWait.AddCondition(AnimatorConditionMode.NotEqual, firstGesture, gestureParam);
            toleranceErrorToWait.hasFixedDuration = true;
        }

    }
}
