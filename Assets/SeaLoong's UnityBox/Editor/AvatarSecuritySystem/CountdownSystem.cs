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
        /// 创建倒计时层（简化版：2个状态）
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

            // 添加TimeUp参数（所有模式都需要）
            ASSAnimatorUtils.AddParameterIfNotExists(controller, ASSConstants.PARAM_TIME_UP,
                AnimatorControllerParameterType.Bool, defaultBool: false);

            // 创建倒计时动画（完整倒计时，包含警告阶段）
            var countdownClip = CreateCountdownClip(duration, avatarRoot);
            ASSAnimatorUtils.AddSubAsset(controller, countdownClip);

            // 状态：Countdown（倒计时进行中）
            var countdownState = layer.stateMachine.AddState("Countdown", new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;
            layer.stateMachine.defaultState = countdownState;

            // 循环模式下，在Countdown状态进入时重置TimeUp参数
            if (isLooping)
            {
#if VRC_SDK_VRCSDK3
                ASSAnimatorUtils.AddParameterDriverBehaviour(countdownState, ASSConstants.PARAM_TIME_UP, 0f, localOnly: true);
#endif
            }

            // 状态：Unlocked（密码正确，停止倒计时）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 150, 0));
            unlockedState.motion = ASSAnimatorUtils.SharedEmptyClip;

            // 状态：TimeUp（倒计时结束，设置参数）
            var timeUpState = layer.stateMachine.AddState("TimeUp", new Vector3(200, 250, 0));
            timeUpState.motion = ASSAnimatorUtils.SharedEmptyClip;
            
#if VRC_SDK_VRCSDK3
            ASSAnimatorUtils.AddParameterDriverBehaviour(timeUpState, ASSConstants.PARAM_TIME_UP, 1f, localOnly: true);
#endif
            
            // Countdown → TimeUp（倒计时结束）
            var toTimeUp = ASSAnimatorUtils.CreateTransition(countdownState, timeUpState,
                hasExitTime: true, exitTime: 1.0f);
            toTimeUp.duration = 0f;

            // 根据模式创建不同的后续转换
            if (isLooping)
            {
                // 循环模式：TimeUp → Countdown（回到开始形成循环）
                var timeUpToCountdown = ASSAnimatorUtils.CreateTransition(timeUpState, countdownState,
                    hasExitTime: true, exitTime: 0f);
                timeUpToCountdown.duration = 0f;
            }
            
            // 转换：密码正确 → 解锁（所有状态都可以转到Unlocked）
            var countdownToUnlocked = ASSAnimatorUtils.CreateTransition(countdownState, unlockedState);
            countdownToUnlocked.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);
            
            var timeUpToUnlocked = ASSAnimatorUtils.CreateTransition(timeUpState, unlockedState);
            timeUpToUnlocked.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            string logMessage = isLooping 
                ? ASSI18n.T("log.debug_mode") + " - 循环倒计时用于显示测试"
                : string.Format(ASSI18n.T("log.countdown_layer_created"), duration, config.warningThreshold);
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
        /// 创建独立的警告音效层（在警告阶段循环播放音效）
        /// </summary>
        public static AnimatorControllerLayer CreateWarningAudioLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config,
            bool isLooping = false)
        {
            var layer = ASSAnimatorUtils.CreateLayer("ASS_WarningAudio", 1f);
            float warningThreshold = config.warningThreshold;
            float duration = config.countdownDuration;
            float warningStartTime = duration - warningThreshold;

            // 状态：Waiting（等待警告时间，播放与倒计时同步的空动画）
            var waitingState = layer.stateMachine.AddState("Waiting", new Vector3(200, 50, 0));
            // 加上0.1秒延迟，确保10次1秒循环后(20.1+10=30.1)不会在TimeUp前触发第11次
            var waitingClip = CreateWaitingClip(warningStartTime + 0.1f);  
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            layer.stateMachine.defaultState = waitingState;
            ASSAnimatorUtils.AddSubAsset(controller, waitingClip);

            // 状态：WarningBeep（播放警告音，1秒动画自循环）
            var beepState = layer.stateMachine.AddState("WarningBeep", new Vector3(200, 150, 0));
            var beepClip = CreateWarningLoopClip(1f);
            beepState.motion = beepClip;
            beepState.writeDefaultValues = true;
            ASSAnimatorUtils.AddSubAsset(controller, beepClip);

#if VRC_SDK_VRCSDK3
            // 添加音效播放行为（每次进入状态时播放）
            if (config.warningBeep != null)
            {
                ASSAnimatorUtils.AddPlayAudioBehaviour(beepState, 
                    ASSConstants.GO_WARNING_AUDIO, 
                    config.warningBeep);
            }
#endif

            // 状态：Stop（停止音效）
            var stopState = layer.stateMachine.AddState("Stop", new Vector3(200, 250, 0));
            stopState.motion = ASSAnimatorUtils.SharedEmptyClip;
            stopState.writeDefaultValues = true;

            // 转换：Waiting → WarningBeep（当Waiting动画播放完毕）
            var waitingToBeep = ASSAnimatorUtils.CreateTransition(waitingState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            waitingToBeep.duration = 0f;

            // 转换：WarningBeep → Stop（当TimeUp时停止，优先级高于自循环）
            var beepToStop = ASSAnimatorUtils.CreateTransition(beepState, stopState);
            beepToStop.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_TIME_UP);
            beepToStop.duration = 0f;

            // WarningBeep自循环（每秒一次，触发音效）
            var beepLoop = ASSAnimatorUtils.CreateTransition(beepState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            beepLoop.duration = 0f;

            // 循环模式：Stop → Waiting（自动回到等待状态形成循环，仅在密码未正确时）
            if (isLooping)
            {
                var stopToWaiting = ASSAnimatorUtils.CreateTransition(stopState, waitingState,
                    hasExitTime: true, exitTime: 0f);
                stopToWaiting.AddCondition(AnimatorConditionMode.IfNot, 0, ASSConstants.PARAM_PASSWORD_CORRECT);
                stopToWaiting.duration = 0f;
            }

            // 添加密码正确时的转换
            var waitingToStop = ASSAnimatorUtils.CreateTransition(waitingState, stopState);
            waitingToStop.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            var beepToStopOnUnlock = ASSAnimatorUtils.CreateTransition(beepState, stopState);
            beepToStopOnUnlock.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            string logMessage = isLooping
                ? $"[ASS] Created warning audio layer (looping): waiting={warningStartTime}s, beeping={warningThreshold}s"
                : $"[ASS] Created warning audio layer: waiting={warningStartTime}s, beeping={warningThreshold}s";
            Debug.Log(logMessage);
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

            // 添加一个虚拟曲线来确保动画有正确的长度
            // 使用一个不存在的路径，这样不会影响任何实际对象
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);
            
            Debug.Log($"[ASS] Created waiting clip: duration={duration}s");
            return clip;
        }

        /// <summary>
        /// 创建警告循环动画（1秒空动画）
        /// </summary>
        private static AnimationClip CreateWarningLoopClip(float duration)
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_WarningLoop",
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 添加虚拟曲线确保动画长度
            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);

            Debug.Log($"[ASS] Created warning loop animation: duration={duration}s");
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
