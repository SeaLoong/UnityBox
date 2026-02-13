using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 倒计时系统生成器
    /// 功能：创建倒计时层和警告音效层，超时触发防御，密码正确停止倒计时
    /// </summary>
    public class Countdown
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly AvatarSecuritySystemComponent config;

        public Countdown(AnimatorController controller, GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
        }

        /// <summary>
        /// 生成倒计时层，添加到控制器
        /// </summary>
        public void Generate()
        {
            var layer = Utils.CreateLayer(LAYER_COUNTDOWN, 1f);
            float duration = config.countdownDuration;

            Utils.AddParameterIfNotExists(controller, PARAM_TIME_UP,
                AnimatorControllerParameterType.Bool, defaultBool: false);

            var countdownClip = CreateCountdownClip(duration);
            Utils.AddSubAsset(controller, countdownClip);

            // Remote（其他玩家默认状态）
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, -50, 0));
            remoteState.motion = Utils.SharedEmptyClip;
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // Countdown（倒计时进行中）
            var countdownState = layer.stateMachine.AddState("Countdown", new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;

            // Remote → Countdown
            var toCountdown = Utils.CreateTransition(remoteState, countdownState);
            toCountdown.AddCondition(AnimatorConditionMode.If, 0, PARAM_IS_LOCAL);

            // Unlocked（密码正确，停止倒计时）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 150, 0));
            unlockedState.motion = Utils.SharedEmptyClip;

            // TimeUp（倒计时结束，隐藏进度条）
            var timeUpState = layer.stateMachine.AddState("TimeUp", new Vector3(200, 250, 0));
            var timeUpClip = CreateTimeUpClip();
            timeUpState.motion = timeUpClip;
            Utils.AddSubAsset(controller, timeUpClip);
            Utils.AddParameterDriverBehaviour(timeUpState, PARAM_TIME_UP, 1f, localOnly: true);
            
            // Countdown → TimeUp
            var toTimeUp = Utils.CreateTransition(countdownState, timeUpState,
                hasExitTime: true, exitTime: 1.0f);
            toTimeUp.duration = 0f;
            
            // 密码正确 → 解锁
            var countdownToUnlocked = Utils.CreateTransition(countdownState, unlockedState);
            countdownToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            
            var timeUpToUnlocked = Utils.CreateTransition(timeUpState, unlockedState);
            timeUpToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(string.Format(T("log.countdown_layer_created"), duration, config.warningThreshold));
            
            controller.AddLayer(layer);
        }

        /// <summary>
        /// 生成警告音效层，添加到控制器
        /// 需要在 Feedback 创建音频对象之后调用
        /// </summary>
        public void GenerateAudioLayer()
        {
            var layer = Utils.CreateLayer(LAYER_AUDIO, 1f);
            float warningThreshold = config.warningThreshold;
            float duration = config.countdownDuration;
            float warningStartTime = duration - warningThreshold;

            // Remote
            var remoteState = layer.stateMachine.AddState("Remote", new Vector3(200, -50, 0));
            remoteState.motion = Utils.SharedEmptyClip;
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // Waiting（等待警告时间）
            var waitingState = layer.stateMachine.AddState("Waiting", new Vector3(200, 50, 0));
            var waitingClip = CreateDummyClip("ASS_AudioWaiting", warningStartTime + 0.1f);
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, waitingClip);
            
            // Remote → Waiting
            var toWaiting = Utils.CreateTransition(remoteState, waitingState);
            toWaiting.AddCondition(AnimatorConditionMode.If, 0, PARAM_IS_LOCAL);

            // WarningBeep（播放警告音，1秒自循环）
            var beepState = layer.stateMachine.AddState("WarningBeep", new Vector3(200, 150, 0));
            var beepClip = CreateDummyClip("ASS_WarningLoop", 1f);
            beepState.motion = beepClip;
            beepState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, beepClip);

            if (config.warningBeep != null)
            {
                Utils.AddPlayAudioBehaviour(beepState, 
                    GO_AUDIO_WARNING, 
                    config.warningBeep);
            }

            // Stop（停止音效）
            var stopState = layer.stateMachine.AddState("Stop", new Vector3(200, 250, 0));
            stopState.motion = Utils.SharedEmptyClip;
            stopState.writeDefaultValues = true;

            // Waiting → WarningBeep
            var waitingToBeep = Utils.CreateTransition(waitingState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            waitingToBeep.duration = 0f;

            // WarningBeep → Stop（TimeUp时停止）
            var beepToStop = Utils.CreateTransition(beepState, stopState);
            beepToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            beepToStop.duration = 0f;

            // WarningBeep 自循环
            var beepLoop = Utils.CreateTransition(beepState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            beepLoop.duration = 0f;

            // 密码正确时停止
            var waitingToStop = Utils.CreateTransition(waitingState, stopState);
            waitingToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            var beepToStopOnUnlock = Utils.CreateTransition(beepState, stopState);
            beepToStopOnUnlock.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log($"[ASS] Created warning audio layer: waiting={warningStartTime}s, beeping={warningThreshold}s");
            
            controller.AddLayer(layer);
        }

        /// <summary>
        /// 倒计时结束后隐藏进度条 UI
        /// </summary>
        private AnimationClip CreateTimeUpClip()
        {
            var clip = new AnimationClip { name = "ASS_TimeUp", legacy = false };
            var disableCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            clip.SetCurve(GO_UI, typeof(GameObject), "m_IsActive", disableCurve);
            return clip;
        }

        private AnimationClip CreateCountdownClip(float duration)
        {
            var clip = new AnimationClip 
            { 
                name = "ASS_Countdown",
                legacy = false
            };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 驱动 Overlay 上 MeshRenderer 的材质属性 _Progress（从 1 到 0）
            string overlayPath = $"{GO_UI}/Overlay";
            AnimationCurve progressCurve = AnimationCurve.Linear(0f, 1f, duration, 0f);
            clip.SetCurve(overlayPath, typeof(MeshRenderer), "material._Progress", progressCurve);

            Debug.Log($"[ASS] Created countdown animation: duration={duration}s, path={overlayPath}, property=material._Progress");
            return clip;
        }

        private static AnimationClip CreateDummyClip(string name, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AnimationCurve dummyCurve = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve("__ASS_Dummy__", typeof(GameObject), "m_IsActive", dummyCurve);
            
            return clip;
        }
    }
}
