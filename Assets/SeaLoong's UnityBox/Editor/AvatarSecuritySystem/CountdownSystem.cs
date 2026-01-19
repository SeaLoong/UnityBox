using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using SeaLoongUnityBox;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 倒计时系统生成器
    /// 功能：创建倒计时层，超时触发防御，密码正确停止倒计时
    /// 集成视觉和音频警告反馈
    /// </summary>
    public static class CountdownSystem
    {
        /// <summary>
        /// 创建倒计时层（带音频和视觉警告）
        /// </summary>
        /// <param name="controller">Animator Controller</param>
        /// <param name="avatarRoot">Avatar根对象</param>
        /// <param name="config">配置组件</param>
        /// <param name="isLooping">是否循环模式（调试用）</param>
        public static AnimatorControllerLayer CreateCountdownLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config,
            bool isLooping = false)
        {
            var layer = ASSAnimatorUtils.CreateLayer(ASSConstants.LAYER_COUNTDOWN, 1f);
            float duration = config.countdownDuration;
            float warningThreshold = config.warningThreshold;

            // 非循环模式需要TimeUp参数
            if (!isLooping)
            {
                ASSAnimatorUtils.AddParameterIfNotExists(controller, ASSConstants.PARAM_TIME_UP,
                    AnimatorControllerParameterType.Bool, defaultBool: false);
            }

            // 创建倒计时动画
            var countdownClip = CreateCountdownClip(duration, avatarRoot);
            ASSAnimatorUtils.AddSubAsset(controller, countdownClip);

            // 状态：Countdown（倒计时进行中）
            var countdownState = layer.stateMachine.AddState("Countdown", new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;
            layer.stateMachine.defaultState = countdownState;

            // 状态：Warning（最后N秒警告，继续播放进度条动画）
            var warningState = layer.stateMachine.AddState("Warning", new Vector3(200, 150, 0));
            
            // 创建警告阶段的完整倒计时动画
            var warningClip = CreateWarningCountdownClip(warningThreshold, duration, avatarRoot);
            warningState.motion = warningClip;
            warningState.speed = 1f;
            warningState.writeDefaultValues = true;
            ASSAnimatorUtils.AddSubAsset(controller, warningClip);

            // 状态：Unlocked（密码正确，停止倒计时）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 250, 0));
            unlockedState.motion = ASSAnimatorUtils.SharedEmptyClip;

            // 转换：倒计时 → 警告状态（基于时间的自动转换）
            float warningStartNormalized = (duration - warningThreshold) / duration;
            var toWarning = ASSAnimatorUtils.CreateTransition(countdownState, warningState, 
                hasExitTime: true, exitTime: warningStartNormalized);
            toWarning.duration = 0f;

            // 根据模式创建不同的警告后续逻辑
            if (isLooping)
            {
                // 循环模式：Warning → 倒计时
                var warningToCountdown = ASSAnimatorUtils.CreateTransition(warningState, countdownState,
                    hasExitTime: true, exitTime: 1.0f);
                warningToCountdown.duration = 0f;
            }
            else
            {
                // 正常模式：创建TimeUp状态和转换
                var timeUpState = layer.stateMachine.AddState("TimeUp", new Vector3(200, 350, 0));
                timeUpState.motion = ASSAnimatorUtils.SharedEmptyClip;
                ASSAnimatorUtils.AddParameterDriverBehaviour(timeUpState, ASSConstants.PARAM_TIME_UP, 1f, localOnly: true);

                // 转换：Warning → 超时
                var warningToTimeUp = ASSAnimatorUtils.CreateTransition(warningState, timeUpState,
                    hasExitTime: true, exitTime: 1.0f);
                warningToTimeUp.duration = 0f;
            }
            
            // 转换：任意状态 → 解锁（密码正确）
            var countdownToUnlocked = ASSAnimatorUtils.CreateTransition(countdownState, unlockedState);
            countdownToUnlocked.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);
            
            var warningToUnlocked = ASSAnimatorUtils.CreateTransition(warningState, unlockedState);
            warningToUnlocked.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            string logMessage = isLooping 
                ? ASSI18n.T("log.debug_mode") + " - 循环倒计时用于显示测试（带警告音效）"
                : string.Format(ASSI18n.T("log.countdown_layer_created"), duration, warningThreshold);
            Debug.Log(logMessage);
            
            return layer;
        }

        /// <summary>
        /// 创建倒计时动画剪辑（控制进度条fillAmount）
        /// </summary>
        private static AnimationClip CreateCountdownClip(float duration, GameObject avatarRoot)
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_Countdown",
                legacy = false
            };
            
            // 设置动画长度为duration
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 创建进度条宽度动画（从满到空）
            string barPath = $"{ASSConstants.GO_UI_CANVAS}/CountdownBar/Bar";
            AnimationCurve anchorCurve = AnimationCurve.Linear(0f, 1f, duration, 0f);
            clip.SetCurve(barPath, typeof(RectTransform), "m_AnchorMax.x", anchorCurve);

            Debug.Log($"[ASS] Created countdown animation: duration={duration}s, path={barPath}");
            return clip;
        }

        /// <summary>
        /// 创建警告阶段的倒计时动画（继续更新进度条）
        /// </summary>
        private static AnimationClip CreateWarningCountdownClip(float warningThreshold, float totalDuration, GameObject avatarRoot)
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_WarningCountdown",
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 创建进度条动画：从startValue到0
            string barPath = $"{ASSConstants.GO_UI_CANVAS}/CountdownBar/Bar";
            float startValue = warningThreshold / totalDuration;
            AnimationCurve anchorCurve = AnimationCurve.Linear(0f, startValue, warningThreshold, 0f);
            clip.SetCurve(barPath, typeof(RectTransform), "m_AnchorMax.x", anchorCurve);

            Debug.Log($"[ASS] Created warning countdown animation: duration={warningThreshold}s, startValue={startValue:F2}");
            return clip;
        }

        /// <summary>
        /// 创建独立的警告音效层（在警告阶段循环播放音效）
        /// </summary>
        public static AnimatorControllerLayer CreateWarningAudioLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = ASSAnimatorUtils.CreateLayer("ASS_WarningAudio", 1f);
            float warningThreshold = config.warningThreshold;
            float duration = config.countdownDuration;
            float warningStartTime = duration - warningThreshold;

            // 状态：Waiting（等待警告时间，播放与倒计时同步的空动画）
            var waitingState = layer.stateMachine.AddState("Waiting", new Vector3(200, 50, 0));
            var waitingClip = CreateWaitingClip(warningStartTime);  // 创建与警告前时间相同长度的动画
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            layer.stateMachine.defaultState = waitingState;
            ASSAnimatorUtils.AddSubAsset(controller, waitingClip);

            // 状态：WarningBeep（播放警告音）
            var beepState = layer.stateMachine.AddState("WarningBeep", new Vector3(200, 150, 0));
            var beepClip = CreateWarningLoopClip();
            beepState.motion = beepClip;
            beepState.writeDefaultValues = true;
            ASSAnimatorUtils.AddSubAsset(controller, beepClip);

#if VRC_SDK_VRCSDK3
            // 添加音效播放行为
            if (config.warningBeep != null)
            {
                ASSAnimatorUtils.AddPlayAudioBehaviour(beepState, 
                    ASSConstants.GO_WARNING_AUDIO, 
                    config.warningBeep);
            }
#endif

            // WarningBeep自循环（每秒一次）
            var beepLoop = ASSAnimatorUtils.CreateTransition(beepState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            beepLoop.duration = 0f;

            // 状态：Stop（停止音效）
            var stopState = layer.stateMachine.AddState("Stop", new Vector3(200, 250, 0));
            stopState.motion = ASSAnimatorUtils.SharedEmptyClip;
            stopState.writeDefaultValues = true;

            // 转换：Waiting → WarningBeep（当Waiting动画播放完毕）
            var waitingToBeep = ASSAnimatorUtils.CreateTransition(waitingState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            waitingToBeep.duration = 0f;

            // 转换：WarningBeep → Stop（当警告时间结束）
            var beepToStop = ASSAnimatorUtils.CreateTransition(beepState, stopState,
                hasExitTime: true, exitTime: warningThreshold);
            beepToStop.duration = 0f;

            // 添加密码正确时的转换
            var waitingToStop = ASSAnimatorUtils.CreateTransition(waitingState, stopState);
            waitingToStop.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            var beepToStopOnUnlock = ASSAnimatorUtils.CreateTransition(beepState, stopState);
            beepToStopOnUnlock.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log($"[ASS] Created warning audio layer: waiting={warningStartTime}s, beeping={warningThreshold}s");
            return layer;
        }

        /// <summary>
        /// 创建等待动画（空动画，用于时间同步）
        /// </summary>
        private static AnimationClip CreateWaitingClip(float duration)
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_AudioWaiting",
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 创建一个简单的空动画，只是为了有正确的时长
            // 可以添加一个虚拟曲线来确保动画有正确的长度
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            
            Debug.Log($"[ASS] Created waiting clip: duration={duration}s");
            return clip;
        }

        /// <summary>
        /// 创建警告循环动画（1秒空动画，用于触发音效）
        /// </summary>
        private static AnimationClip CreateWarningLoopClip()
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_WarningLoop",
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            Debug.Log("[ASS] Created warning loop animation: 1s interval");
            return clip;
        }

        /// <summary>
        /// 创建循环倒计时层（无限时间模式，用于显示测试）
        /// </summary>
        public static AnimatorControllerLayer CreateLoopingCountdownLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            return CreateCountdownLayer(controller, avatarRoot, config, isLooping: true);
        }
    }
}
