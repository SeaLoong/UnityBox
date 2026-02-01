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
        /// 3. 容错机制：短时间输入错误手势后仍有机会继续，而不是立即重置
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

            // 创建初始等待状态（也作为任意位置的起始状态）
            var waitState = layer.stateMachine.AddState("Wait_Input", new Vector3(50, 50, 0));
            waitState.motion = AnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = waitState;

            // 创建时间到失败状态（禁止输入）
            // 倒计时结束后防御系统会启动，不需要在密码系统给失败反馈
            var timeUpFailedState = layer.stateMachine.AddState("TimeUp_Failed", new Vector3(50, -50, 0));
            timeUpFailedState.motion = AnimatorUtils.SharedEmptyClip;

            // Any State → TimeUp_Failed（时间到后强制进入失败状态）
            var anyToFailed = AnimatorUtils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);

            // 第一阶段：创建所有状态
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);

                // 创建Holding状态（手势保持中，需要稳定时间）
                var holdingState = layer.stateMachine.AddState($"Step_{i + 1}_Holding", 
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                var holdClip = CreateHoldClip($"ASS_Hold_{i + 1}", gestureHoldTime);
                holdingState.motion = holdClip;
                AnimatorUtils.AddSubAsset(controller, holdClip);
                stepHoldingStates.Add(holdingState);

                // 最后一步不需要 Confirmed/ErrorTolerance 状态
                if (isLastStep)
                {
                    stepConfirmedStates.Add(null);
                    stepErrorToleranceStates.Add(null);
                    continue;
                }

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
            }

            // 第二阶段：创建所有转换
            for (int i = 0; i < password.Count; i++)
            {
                int gestureValue = password[i];
                bool isLastStep = (i == password.Count - 1);
                
                var holdingState = stepHoldingStates[i];
                var confirmedState = stepConfirmedStates[i];
                var errorToleranceState = stepErrorToleranceStates[i];

                // ===== Holding → Confirmed/Success（手势保持0.15s后确认）=====
                if (!isLastStep)
                {
                    // 非最后一步：Holding → Confirmed
                    var holdToConfirm = AnimatorUtils.CreateTransition(holdingState, confirmedState,
                        hasExitTime: true, exitTime: 1.0f);
                    holdToConfirm.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    holdToConfirm.duration = 0f;
                }
                // 最后一步的 Holding → Success 转换将在创建 Success 状态后添加

                // Holding → Holding（Idle自循环，允许短暂松开）
                ConfigureIdleLoopTransition(AnimatorUtils.CreateTransition(holdingState, holdingState), gestureParam);

                // Holding → Wait（错误手势立即回退，不是当前手势）
                var holdingErrorToWait = holdingState.AddTransition(waitState);
                ConfigureErrorTransition(holdingErrorToWait, gestureValue, gestureParam);

                // ===== Wait和前续状态 → Holding（进入当前步骤）=====
                if (i == 0)
                {
                    // 第一步：从waitState开始
                    var firstTransition = AnimatorUtils.CreateTransition(waitState, holdingState);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    firstTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                }
                else
                {
                    // 后续步骤：只能从上一个Confirmed转入（通过正确手势）
                    var previousConfirmed = stepConfirmedStates[i - 1];
                    var correctTransition = AnimatorUtils.CreateTransition(previousConfirmed, holdingState);
                    ConfigureGestureTransition(correctTransition, gestureValue, gestureParam);
                }

                // 最后一步不需要 Confirmed 和 ErrorTolerance 的处理
                if (isLastStep) continue;

                // ===== Confirmed状态处理 =====
                // Confirmed → Confirmed（Idle自循环，允许松开）
                ConfigureIdleLoopTransition(AnimatorUtils.CreateTransition(confirmedState, confirmedState), gestureParam);

                // Confirmed → ErrorTolerance（错误手势进入容错0.3s）
                var confirmedToTolerance = confirmedState.AddTransition(errorToleranceState);
                ConfigureErrorTransition(confirmedToTolerance, gestureValue, gestureParam);

                // Confirmed → Holding_1（尾部匹配：用户按密码第一位）
                var firstGesture = password[0];
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var confirmedRestartTransition = confirmedState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(confirmedRestartTransition, firstGesture, gestureParam);
                }

                // ===== ErrorTolerance状态处理（0.3s容错缓冲）=====
                // 关键：ErrorTolerance只有两个出口：超时回Wait，或收到正确纠正
                
                // ErrorTolerance → Holding_{i+1}（收到正确下一位）
                if (!isLastStep)
                {
                    var nextHoldingState = stepHoldingStates[i + 1];
                    var toleranceCorrectTransition = errorToleranceState.AddTransition(nextHoldingState);
                    int nextGesture = password[i + 1];
                    ConfigureGestureTransition(toleranceCorrectTransition, nextGesture, gestureParam);
                }

                // ErrorTolerance → Holding_1（尾部匹配：用户按密码第一位）
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var toleranceRestartTransition = errorToleranceState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(toleranceRestartTransition, firstGesture, gestureParam);
                }

                // ErrorTolerance → Idle自循环（允许松开手指）
                ConfigureIdleLoopTransition(AnimatorUtils.CreateTransition(errorToleranceState, errorToleranceState), gestureParam);

                // ErrorTolerance → Wait（0.3s超时后回到等待，这是唯一的超时出口）
                var toleranceTimeoutToWait = AnimatorUtils.CreateTransition(errorToleranceState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                toleranceTimeoutToWait.duration = 0f;
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
                    Constants.GO_AUDIO, 
                    config.successSound);
            }
#endif
            
            // 设置密码正确参数（使用 Parameter Driver）
            // 注意：localOnly=true 只影响驱动行为的执行位置
            // 参数同步由 VRCExpressionParameters 中的 networkSynced 属性控制
            AnimatorUtils.AddParameterDriverBehaviour(successState, Constants.PARAM_PASSWORD_CORRECT, 1f, localOnly: true);

            // 最后一步 Holding → Success（手势保持0.15s后直接成功）
            var lastHoldingState = stepHoldingStates[password.Count - 1];
            var finalTransition = AnimatorUtils.CreateTransition(lastHoldingState, successState,
                hasExitTime: true, exitTime: 1.0f);
            finalTransition.AddCondition(AnimatorConditionMode.Equals, password[password.Count - 1], gestureParam);
            finalTransition.duration = 0f;

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
            var clip = new AnimationClip { name = name, legacy = false };
            ConfigureClipSettings(clip);

            // 添加虚拟曲线确保动画长度
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);

            return clip;
        }
        
        private static void ConfigureClipSettings(AnimationClip clip)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        
        /// <summary>
        /// 配置空闲循环转换（允许用户松开手指）
        /// </summary>
        private static void ConfigureIdleLoopTransition(AnimatorStateTransition transition, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.Equals, 0, gestureParam);
        }
        
        /// <summary>
        /// 配置错误/不匹配的手势转换
        /// </summary>
        private static void ConfigureErrorTransition(AnimatorStateTransition transition, int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.NotEqual, expectedGesture, gestureParam);
            transition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
        }
        
        /// <summary>
        /// 配置正确的手势转换
        /// </summary>
        private static void ConfigureGestureTransition(AnimatorStateTransition transition, int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.Equals, expectedGesture, gestureParam);
            transition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
        }
    }
}
