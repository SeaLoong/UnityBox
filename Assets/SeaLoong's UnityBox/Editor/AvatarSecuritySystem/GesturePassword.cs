using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 手势密码验证系统生成器
    /// 功能：通过 VRChat 手势参数检测密码输入
    /// </summary>
    public class GesturePassword
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly AvatarSecuritySystemComponent config;

        public GesturePassword(AnimatorController controller, GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
        }

        /// <summary>
        /// 创建手势密码验证层并添加到控制器
        /// 实现尾部序列匹配：用户输入的最后N位满足密码即可通过
        /// 增强特性：手势稳定时间、忽略Idle手势、容错机制
        /// 时间到后强制进入失败状态，禁止继续输入
        /// </summary>
        public void Generate()
        {
            float gestureHoldTime = config.gestureHoldTime;
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

            // 第一阶段：创建所有状态
            var stepHoldingStates = new List<AnimatorState>();
            var stepConfirmedStates = new List<AnimatorState>();
            var stepErrorToleranceStates = new List<AnimatorState>();
            
            for (int i = 0; i < password.Count; i++)
            {
                bool isLastStep = (i == password.Count - 1);

                var holdingState = layer.stateMachine.AddState($"Step_{i + 1}_Holding", 
                    new Vector3(50 + (i + 1) * 350, 50, 0));
                var holdClip = CreateHoldClip($"ASS_Hold_{i + 1}", gestureHoldTime);
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

                if (!isLastStep)
                {
                    var holdToConfirm = Utils.CreateTransition(holdingState, confirmedState,
                        hasExitTime: true, exitTime: 1.0f);
                    holdToConfirm.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    holdToConfirm.duration = 0f;
                }

                ConfigureIdleLoopTransition(Utils.CreateTransition(holdingState, holdingState), gestureParam);

                var holdingErrorToWait = holdingState.AddTransition(waitState);
                ConfigureErrorTransition(holdingErrorToWait, gestureValue, gestureParam);

                if (i == 0)
                {
                    var firstTransition = Utils.CreateTransition(waitState, holdingState);
                    firstTransition.AddCondition(AnimatorConditionMode.Equals, gestureValue, gestureParam);
                    firstTransition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
                }
                else
                {
                    var previousConfirmed = stepConfirmedStates[i - 1];
                    var correctTransition = Utils.CreateTransition(previousConfirmed, holdingState);
                    ConfigureGestureTransition(correctTransition, gestureValue, gestureParam);
                }

                if (isLastStep) continue;

                ConfigureIdleLoopTransition(Utils.CreateTransition(confirmedState, confirmedState), gestureParam);

                var confirmedToTolerance = confirmedState.AddTransition(errorToleranceState);
                ConfigureErrorTransition(confirmedToTolerance, gestureValue, gestureParam);

                var firstGesture = password[0];
                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var confirmedRestartTransition = confirmedState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(confirmedRestartTransition, firstGesture, gestureParam);
                }

                if (!isLastStep)
                {
                    var nextHoldingState = stepHoldingStates[i + 1];
                    var toleranceCorrectTransition = errorToleranceState.AddTransition(nextHoldingState);
                    int nextGesture = password[i + 1];
                    ConfigureGestureTransition(toleranceCorrectTransition, nextGesture, gestureParam);
                }

                if (firstGesture != gestureValue && stepHoldingStates.Count > 0)
                {
                    var toleranceRestartTransition = errorToleranceState.AddTransition(stepHoldingStates[0]);
                    ConfigureGestureTransition(toleranceRestartTransition, firstGesture, gestureParam);
                }

                ConfigureIdleLoopTransition(Utils.CreateTransition(errorToleranceState, errorToleranceState), gestureParam);

                var toleranceTimeoutToWait = Utils.CreateTransition(errorToleranceState, waitState,
                    hasExitTime: true, exitTime: 1.0f);
                toleranceTimeoutToWait.duration = 0f;
            }

            // 成功状态
            var successState = layer.stateMachine.AddState("Password_Success", 
                new Vector3(50 + (password.Count + 1) * 350, 150, 0));
            successState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            if (config.successSound != null)
            {
                Utils.AddPlayAudioBehaviour(successState, 
                    GO_AUDIO_SUCCESS, 
                    config.successSound);
            }
            
            Utils.AddParameterDriverBehaviour(successState, PARAM_PASSWORD_CORRECT, 1f, localOnly: true);

            // 最后一步 Holding → Success
            var lastHoldingState = stepHoldingStates[password.Count - 1];
            var finalTransition = Utils.CreateTransition(lastHoldingState, successState,
                hasExitTime: true, exitTime: 1.0f);
            finalTransition.AddCondition(AnimatorConditionMode.Equals, password[password.Count - 1], gestureParam);
            finalTransition.duration = 0f;

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log($"[ASS] Gesture password layer created with stability check: " +
                     $"password length={password.Count}, hold time={gestureHoldTime}s, error tolerance={gestureErrorTolerance}s");

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
        
        private static void ConfigureIdleLoopTransition(AnimatorStateTransition transition, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.Equals, 0, gestureParam);
        }
        
        private static void ConfigureErrorTransition(AnimatorStateTransition transition, int expectedGesture, string gestureParam)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.AddCondition(AnimatorConditionMode.NotEqual, expectedGesture, gestureParam);
            transition.AddCondition(AnimatorConditionMode.NotEqual, 0, gestureParam);
        }
        
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
