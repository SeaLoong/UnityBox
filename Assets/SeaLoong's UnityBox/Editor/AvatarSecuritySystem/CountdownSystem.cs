using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using SeaLoongUnityBox;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.AnimatorUtils;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;
using VRC.SDK3.Avatars.Components;

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
        /// 创建倒计时层（简化版：3个状态）
        /// </summary>
        /// <param name="controller">Animator Controller</param>
        /// <param name="avatarRoot">Avatar根对象</param>
        /// <param name="config">配置组件</param>
        public static AnimatorControllerLayer CreateCountdownLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = CreateLayer(LAYER_COUNTDOWN, 1f);
            float duration = config.countdownDuration;

            // 添加TimeUp参数
            AddParameterIfNotExists(controller, PARAM_TIME_UP,
                AnimatorControllerParameterType.Bool, defaultBool: false);

            // 创建倒计时动画（完整倒计时，包含警告阶段）
            var countdownClip = CreateCountdownClip(duration, avatarRoot);
            AddSubAsset(controller, countdownClip);

            // 状态：Remote（其他玩家的默认状态，不进入倒计时）
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, -50, 0));
            remoteState.motion = SharedEmptyClip;
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // 状态：Countdown（倒计时进行中，仅本地玩家）
            var countdownState = layer.stateMachine.AddState("Countdown", new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;

            // 转换：Remote → Countdown（IsLocal 时进入倒计时）
            var toCountdown = CreateTransition(remoteState, countdownState);
            toCountdown.AddCondition(AnimatorConditionMode.If, 0, PARAM_IS_LOCAL);

            // 状态：Unlocked（密码正确，停止倒计时）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 150, 0));
            unlockedState.motion = SharedEmptyClip;

            // 状态：TimeUp（倒计时结束，设置参数）
            var timeUpState = layer.stateMachine.AddState("TimeUp", new Vector3(200, 250, 0));
            timeUpState.motion = SharedEmptyClip;
            
            AddParameterDriverBehaviour(timeUpState, PARAM_TIME_UP, 1f, localOnly: true);
            
            // Countdown → TimeUp（倒计时结束）
            var toTimeUp = CreateTransition(countdownState, timeUpState,
                hasExitTime: true, exitTime: 1.0f);
            toTimeUp.duration = 0f;
            
            // 转换：密码正确 → 解锁（所有状态都可以转到Unlocked）
            var countdownToUnlocked = CreateTransition(countdownState, unlockedState);
            countdownToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            
            var timeUpToUnlocked = CreateTransition(timeUpState, unlockedState);
            timeUpToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AddSubAsset(controller, layer.stateMachine);

            Debug.Log(string.Format(T("log.countdown_layer_created"), duration, config.warningThreshold));
            
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
            
            ConfigureAnimationClip(clip);

            // 创建进度条宽度动画（从满到空），3D条使用 localScale.x 控制长度
            string barPath = $"{GO_UI_CANVAS}/CountdownBar/Bar";
            AnimationCurve scaleCurve = AnimationCurve.Linear(0f, 1f, duration, 0f);
            clip.SetCurve(barPath, typeof(Transform), "m_LocalScale.x", scaleCurve);

            Debug.Log($"[ASS] Created countdown animation: duration={duration}s, path={barPath}");
            return clip;
        }

        /// <summary>
        /// 创建独立的警告音效层（在警告阶段循环播放音效）
        /// </summary>
        public static AnimatorControllerLayer CreateAudioLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            var layer = CreateLayer(LAYER_AUDIO, 1f);
            float warningThreshold = config.warningThreshold;
            float duration = config.countdownDuration;
            float warningStartTime = duration - warningThreshold;

            // 状态：Remote（其他玩家的默认状态，不播放警告音效）
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, -50, 0));
            remoteState.motion = SharedEmptyClip;
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // 状态：Waiting（等待警告时间，播放与倒计时同步的空动画）
            var waitingState = layer.stateMachine.AddState("Waiting", new Vector3(200, 50, 0));
            // 加上0.1秒延迟，确保10次1秒循环后(20.1+10=30.1)不会在TimeUp前触发第11次
            var waitingClip = CreateWaitingClip(warningStartTime + 0.1f);  
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            AddSubAsset(controller, waitingClip);
            
            // 转换：Remote → Waiting（IsLocal 时开始等待）
            var toWaiting = CreateTransition(remoteState, waitingState);
            toWaiting.AddCondition(AnimatorConditionMode.If, 0, PARAM_IS_LOCAL);

            // 状态：WarningBeep（播放警告音，1秒动画自循环）
            var beepState = layer.stateMachine.AddState("WarningBeep", new Vector3(200, 150, 0));
            var beepClip = CreateWarningLoopClip(1f);
            beepState.motion = beepClip;
            beepState.writeDefaultValues = true;
            AddSubAsset(controller, beepClip);

            // 添加音效播放行为（每次进入状态时播放）
            if (config.warningBeep != null)
            {
                AddPlayAudioBehaviour(beepState, 
                    GO_AUDIO_WARNING, 
                    config.warningBeep);
            }

            // 状态：Stop（停止音效）
            var stopState = layer.stateMachine.AddState("Stop", new Vector3(200, 250, 0));
            stopState.motion = SharedEmptyClip;
            stopState.writeDefaultValues = true;

            // 转换：Waiting → WarningBeep（当Waiting动画播放完毕）
            var waitingToBeep = CreateTransition(waitingState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            waitingToBeep.duration = 0f;

            // 转换：WarningBeep → Stop（当TimeUp时停止，优先级高于自循环）
            var beepToStop = CreateTransition(beepState, stopState);
            beepToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            beepToStop.duration = 0f;

            // WarningBeep自循环（每秒一次，触发音效）
            var beepLoop = CreateTransition(beepState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            beepLoop.duration = 0f;

            // 添加密码正确时的转换
            var waitingToStop = CreateTransition(waitingState, stopState);
            waitingToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            var beepToStopOnUnlock = CreateTransition(beepState, stopState);
            beepToStopOnUnlock.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AddSubAsset(controller, layer.stateMachine);

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
            
            ConfigureAnimationClip(clip);

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
        private static void ConfigureAnimationClip(AnimationClip clip)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
    }
}
