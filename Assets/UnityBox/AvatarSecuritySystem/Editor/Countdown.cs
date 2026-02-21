using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 倒计时系统生成器
    /// 功能：创建倒计时层和警告音效层，超时触发防御，密码正确停止倒计时
    /// </summary>
    public class Countdown
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSComponent config;

        public Countdown(AnimatorController controller, GameObject avatarRoot, ASSComponent config)
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
            remoteState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // Countdown（倒计时进行中）
            var countdownState = layer.stateMachine.AddState("Countdown", new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;

            // Unlocked（密码正确，停止倒计时）
            var unlockedState = layer.stateMachine.AddState("Unlocked", new Vector3(200, 150, 0));
            unlockedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);

            // Remote → Unlocked（已解锁状态，跳过倒计时；PasswordCorrect 是 networkSynced，远端也会同步）
            var remoteToUnlocked = Utils.CreateTransition(remoteState, unlockedState);
            remoteToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            // Remote → Countdown（仅本地且未解锁时）
            var toCountdown = Utils.CreateTransition(remoteState, countdownState);
            Utils.AddIsLocalCondition(toCountdown, controller, isTrue: true);
            toCountdown.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);

            // TimeUp（倒计时结束，触发防御但保持 UI 显示作为遮罩）
            var timeUpState = layer.stateMachine.AddState("TimeUp", new Vector3(200, 250, 0));
            timeUpState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
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

            Debug.Log($"[ASS] Countdown layer created, duration: {duration}s, warning threshold: {config.warningThreshold:F1}s");
            
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
            remoteState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;

            // Waiting（等待警告时间）
            var waitingState = layer.stateMachine.AddState("Waiting", new Vector3(200, 50, 0));
            var waitingClip = CreateDummyClip("ASS_AudioWaiting", warningStartTime + 0.1f);
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, waitingClip);

            // Stop（停止音效）
            var stopState = layer.stateMachine.AddState("Stop", new Vector3(200, 250, 0));
            stopState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_NAME);
            stopState.writeDefaultValues = true;
            
            // Remote → Stop（已解锁状态，跳过音效；PasswordCorrect 是 networkSynced，远端也会同步）
            var remoteToStop = Utils.CreateTransition(remoteState, stopState);
            remoteToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);

            // Remote → Waiting（仅本地且未解锁时）
            var toWaiting = Utils.CreateTransition(remoteState, waitingState);
            Utils.AddIsLocalCondition(toWaiting, controller, isTrue: true);
            toWaiting.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);

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

            Debug.Log($"[ASS] Warning audio layer created: waiting={warningStartTime}s, beeping={warningThreshold}s");
            
            controller.AddLayer(layer);
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

            // 驱动 Overlay 上 MeshRenderer 的材质属性 _C9D4（从 1 到 0）
            string overlayPath = $"{GO_UI}/Overlay";
            AnimationCurve progressCurve = AnimationCurve.Linear(0f, 1f, duration, 0f);
            clip.SetCurve(overlayPath, typeof(MeshRenderer), "material._C9D4", progressCurve);

            Debug.Log($"[ASS] Countdown animation created: duration={duration}s, path={overlayPath}");
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
