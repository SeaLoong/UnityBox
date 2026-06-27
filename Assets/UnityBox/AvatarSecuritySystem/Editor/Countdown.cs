using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
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
        public void Generate()
        {
            var layer = Utils.CreateLayer(LAYER_COUNTDOWN, 1f);
            float duration = config.countdownDuration;
            Utils.AddParameterIfNotExists(controller, PARAM_TIME_UP,
                AnimatorControllerParameterType.Bool, defaultBool: false);
            var countdownClip = CreateCountdownClip(duration);
            Utils.AddSubAsset(controller, countdownClip);
            var remoteState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Remote") : "Remote",
                new Vector3(200, -50, 0));
            remoteState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;
            var countdownState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Countdown") : "Countdown",
                new Vector3(200, 50, 0));
            countdownState.motion = countdownClip;
            countdownState.speed = 1f;
            countdownState.writeDefaultValues = true;
            var unlockedState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Unlocked") : "Unlocked",
                new Vector3(200, 150, 0));
            unlockedState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            var remoteToUnlocked = Utils.CreateTransition(remoteState, unlockedState);
            remoteToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            var toCountdown = Utils.CreateTransition(remoteState, countdownState);
            Utils.AddIsLocalCondition(toCountdown, controller, isTrue: true);
            toCountdown.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            var timeUpState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("TimeUp") : "TimeUp",
                new Vector3(200, 250, 0));
            timeUpState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            Utils.AddParameterDriverBehaviour(timeUpState, PARAM_TIME_UP, 1f, localOnly: true);
            var toTimeUp = Utils.CreateTransition(countdownState, timeUpState,
                hasExitTime: true, exitTime: 1.0f);
            toTimeUp.duration = 0f;
            var countdownToUnlocked = Utils.CreateTransition(countdownState, unlockedState);
            countdownToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            var timeUpToUnlocked = Utils.CreateTransition(timeUpState, unlockedState);
            timeUpToUnlocked.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            Debug.Log($"[ASS] Countdown layer created, duration: {duration}s, warning threshold: {config.warningThreshold:F1}s");
            controller.AddLayer(layer);
        }
        public void GenerateAudioLayer()
        {
            if (config.disableWarningSound)
            {
                Debug.Log("[ASS] disableWarningSound enabled, skipping warning audio layer");
                return;
            }
            var layer = Utils.CreateLayer(LAYER_AUDIO, 1f);
            float warningThreshold = config.warningThreshold;
            float duration = config.countdownDuration;
            float warningStartTime = duration - warningThreshold;
            var remoteState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Remote") : "Remote",
                new Vector3(200, -50, 0));
            remoteState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            remoteState.writeDefaultValues = true;
            layer.stateMachine.defaultState = remoteState;
            var waitingState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Waiting") : "Waiting",
                new Vector3(200, 50, 0));
            var waitingClip = CreateDummyClip(
                Obfuscator.IsEnabled ? Obfuscator.Clip("AudioWaiting") : "ASS_AudioWaiting",
                warningStartTime + 0.1f);
            waitingState.motion = waitingClip;
            waitingState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, waitingClip);
            var stopState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("Stop") : "Stop",
                new Vector3(200, 250, 0));
            stopState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            stopState.writeDefaultValues = true;
            var remoteToStop = Utils.CreateTransition(remoteState, stopState);
            remoteToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_PASSWORD_CORRECT);
            var toWaiting = Utils.CreateTransition(remoteState, waitingState);
            Utils.AddIsLocalCondition(toWaiting, controller, isTrue: true);
            toWaiting.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_PASSWORD_CORRECT);
            var beepState = layer.stateMachine.AddState(
                Obfuscator.IsEnabled ? Obfuscator.State("WarningBeep") : "WarningBeep",
                new Vector3(200, 150, 0));
            var beepClip = CreateDummyClip(
                Obfuscator.IsEnabled ? Obfuscator.Clip("WarningLoop") : "ASS_WarningLoop",
                1f);
            beepState.motion = beepClip;
            beepState.writeDefaultValues = true;
            Utils.AddSubAsset(controller, beepClip);
            if (config.warningBeep != null)
            {
                Utils.AddPlayAudioBehaviour(beepState, 
                    GO_AUDIO_WARNING, 
                    config.warningBeep);
            }
            var waitingToBeep = Utils.CreateTransition(waitingState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            waitingToBeep.duration = 0f;
            var beepToStop = Utils.CreateTransition(beepState, stopState);
            beepToStop.AddCondition(AnimatorConditionMode.If, 0, PARAM_TIME_UP);
            beepToStop.duration = 0f;
            var beepLoop = Utils.CreateTransition(beepState, beepState,
                hasExitTime: true, exitTime: 1.0f);
            beepLoop.duration = 0f;
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
                name = Obfuscator.IsEnabled ? Obfuscator.Clip("Countdown") : "ASS_Countdown",
                legacy = false
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string overlayPath = $"{GO_OVERLAY}/{Constants.GO_OVERLAY_MESH}";
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
            clip.SetCurve(Obfuscator.DummyPath(), typeof(GameObject), "m_IsActive", dummyCurve);
            return clip;
        }
    }
}
