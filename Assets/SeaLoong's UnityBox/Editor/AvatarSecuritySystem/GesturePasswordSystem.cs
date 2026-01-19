using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using SeaLoongUnityBox;

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
        /// 移除冷却等待，允许快速连续输入
        /// 使用 VRC State Behaviours 播放音效和设置参数
        /// 时间到后强制进入失败状态，禁止继续输入
        /// </summary>
        public static AnimatorControllerLayer CreatePasswordLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = ASSAnimatorUtils.CreateLayer(ASSConstants.LAYER_PASSWORD_INPUT, 1f);
            var password = config.gesturePassword;
            
            if (password == null || password.Count == 0)
            {
                Debug.LogError(ASSI18n.T("log.password_empty"));
                return layer;
            }

            string gestureParam = config.useRightHand ? 
                ASSConstants.PARAM_GESTURE_RIGHT : ASSConstants.PARAM_GESTURE_LEFT;

            // 添加必要的参数
            ASSAnimatorUtils.AddParameterIfNotExists(controller, ASSConstants.PARAM_PASSWORD_CORRECT,
                AnimatorControllerParameterType.Bool, false);
            ASSAnimatorUtils.AddParameterIfNotExists(controller, ASSConstants.PARAM_PASSWORD_ERROR,
                AnimatorControllerParameterType.Trigger);

            // 创建初始等待状态（也作为任意位置的起始状态）
            var waitState = layer.stateMachine.AddState("Wait_Input", new Vector3(50, 50, 0));
            waitState.motion = ASSAnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = waitState;

            // 创建时间到失败状态（禁止输入）
            var timeUpFailedState = layer.stateMachine.AddState("TimeUp_Failed", new Vector3(50, -50, 0));
            timeUpFailedState.motion = ASSAnimatorUtils.SharedEmptyClip;

#if VRC_SDK_VRCSDK3
            // 播放错误音效
            if (config.errorSound != null)
            {
                ASSAnimatorUtils.AddPlayAudioBehaviour(timeUpFailedState, 
                    ASSConstants.GO_FEEDBACK_AUDIO, 
                    config.errorSound);
            }
#endif

            // Any State → TimeUp_Failed（时间到后强制进入失败状态）
            var anyToFailed = ASSAnimatorUtils.CreateAnyStateTransition(layer.stateMachine, timeUpFailedState);
            anyToFailed.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_TIME_UP);

            // 创建密码输入状态链（移除冷却等待，直接使用单个状态）
            var stepStates = new List<AnimatorState>();
            
            for (int i = 0; i < password.Count; i++)
            {
                int gestureValue = password[i];
                bool isLastStep = (i == password.Count - 1);
                
                // 创建步骤状态（单一状态，无冷却等待）
                var stepState = layer.stateMachine.AddState($"Step_{i + 1}", 
                    new Vector3(50 + (i + 1) * 250, 150, 0));
                stepState.motion = ASSAnimatorUtils.SharedEmptyClip;

#if VRC_SDK_VRCSDK3
                // 使用 VRC Play Audio 播放提示音（如果配置了且不是最后一步）
                if (config.stepSuccessSound != null && !isLastStep)
                {
                    ASSAnimatorUtils.AddPlayAudioBehaviour(stepState, 
                        ASSConstants.GO_FEEDBACK_AUDIO, 
                        config.stepSuccessSound);
                }
#else
                // 非 VRChat SDK 环境，使用动画播放音效
                if (config.stepSuccessSound != null && !isLastStep)
                {
                    var confirmClip = ASSAnimationClipGenerator.CreateAudioTriggerClip(
                        $"ASS_StepConfirm_{i + 1}",
                        ASSConstants.GO_FEEDBACK_AUDIO,
                        config.stepSuccessSound,
                        0.1f // 短暂音效
                    );
                    stepState.motion = confirmClip;
                    ASSAnimatorUtils.AddSubAsset(controller, confirmClip);
                }
#endif

                stepStates.Add(stepState);

                // 从上一个状态转换到当前步骤状态（不检查TimeUp，因为AnyState已经处理）
                if (i == 0)
                {
                    // 第一步：从 waitState 开始
                    var firstTransition = ASSAnimatorUtils.CreateTransition(waitState, stepState);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                }
                else
                {
                    // 后续步骤：从上一个步骤状态转换
                    var previousState = stepStates[i - 1];
                    var correctTransition = ASSAnimatorUtils.CreateTransition(previousState, stepState);
                    correctTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                }

                // 错误手势处理：回到 waitState 或可能开始新序列
                CreateTailMatchErrorTransitions(layer.stateMachine, stepState, waitState, 
                    stepStates, gestureParam, i < password.Count - 1 ? password[i + 1] : -1, 
                    password[0], gestureValue);
            }

            // 最后一步成功：设置密码正确标志
            var successState = layer.stateMachine.AddState("Password_Success", 
                new Vector3(50 + (password.Count + 1) * 250, 150, 0));
            successState.motion = ASSAnimatorUtils.SharedEmptyClip;

#if VRC_SDK_VRCSDK3
            // 播放成功音效
            if (config.successSound != null)
            {
                ASSAnimatorUtils.AddPlayAudioBehaviour(successState, 
                    ASSConstants.GO_FEEDBACK_AUDIO, 
                    config.successSound);
            }
#endif
            
            // 设置密码正确参数（使用 Parameter Driver）
            successState.motion = ASSAnimatorUtils.SharedEmptyClip;
            ASSAnimatorUtils.AddParameterDriverBehaviour(successState, ASSConstants.PARAM_PASSWORD_CORRECT, 1f, localOnly: true);

            var finalTransition = ASSAnimatorUtils.CreateTransition(stepStates[stepStates.Count - 1], successState);
            finalTransition.AddCondition(AnimatorConditionMode.Equals, password[password.Count - 1], gestureParam);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(string.Format(ASSI18n.T("log.password_layer_created"), password.Count));
            return layer;
        }

        /// <summary>
        /// 创建尾部匹配模式的错误转换（优化版：使用NotEqual）
        /// 在当前步骤状态下，如果输入的手势不是当前手势（即手势改变），检查是否是密码第一位或下一步
        /// </summary>
        /// <param name="currentState">当前步骤状态</param>
        /// <param name="waitState">等待状态</param>
        /// <param name="allStepStates">所有步骤状态列表</param>
        /// <param name="gestureParam">手势参数名</param>
        /// <param name="nextGesture">下一步的正确手势（-1 表示已经是最后一步）</param>
        /// <param name="firstGesture">密码的第一位手势</param>
        /// <param name="currentGesture">当前步骤的手势</param>
        private static void CreateTailMatchErrorTransitions(
            AnimatorStateMachine stateMachine,
            AnimatorState currentState,
            AnimatorState waitState,
            List<AnimatorState> allStepStates,
            string gestureParam,
            int nextGesture,
            int firstGesture,
            int currentGesture)
        {
            // 如果是密码第一位且当前不是第一步，跳转到第一步（重新开始序列）
            if (firstGesture != currentGesture && allStepStates.Count > 0)
            {
                var restartTransition = currentState.AddTransition(allStepStates[0]);
                restartTransition.hasExitTime = false;
                restartTransition.duration = 0f;
                restartTransition.AddCondition(AnimatorConditionMode.Equals, firstGesture, gestureParam);
                restartTransition.hasFixedDuration = true;
            }
            
            // 对于所有不是当前手势、不是下一步、不是第一位的情况，回到waitState
            // 使用单个NotEqual条件即可（比多个Equal高效得多）
            var errorTransition = currentState.AddTransition(waitState);
            errorTransition.hasExitTime = false;
            errorTransition.duration = 0f;
            errorTransition.AddCondition(AnimatorConditionMode.NotEqual, currentGesture, gestureParam);
            // 如果有下一步，排除下一步（下一步有专门的正确转换）
            if (nextGesture >= 0)
            {
                errorTransition.AddCondition(AnimatorConditionMode.NotEqual, nextGesture, gestureParam);
            }
            // 如果第一位不同于当前位，排除第一位（第一位有重新开始转换）
            if (firstGesture != currentGesture && allStepStates.Count > 0)
            {
                errorTransition.AddCondition(AnimatorConditionMode.NotEqual, firstGesture, gestureParam);
            }
            errorTransition.hasFixedDuration = true;
        }

    }
}
