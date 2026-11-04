using System.Collections.Generic;
using UnityEditor.Animations;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Keeps a curve-free supervisor active while completed feature layers release their weight.</summary>
    public static class AnimatorLifecycle
    {
        public const string TargetLayerPrefix = "ASS_TargetLayer/";

        public static void Generate(AnimatorController controller, IAnimatorBehaviourAdapter behaviours,
            IAnimatorLayerWeightAdapter weights)
        {
            Utils.AddParameterIfNotExists(controller, PARAM_RESTART, UnityEngine.AnimatorControllerParameterType.Bool, false);
            var layers = controller.layers;
            var lifecycle = Utils.CreateLayer(LAYER_LIFECYCLE, 1f);
            var locked = lifecycle.stateMachine.AddState("Armed");
            var unlocked = lifecycle.stateMachine.AddState("Unlocked");
            locked.writeDefaultValues = unlocked.writeDefaultValues = false;
            locked.motion = unlocked.motion = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
            lifecycle.stateMachine.defaultState = locked;
            behaviours.AddMultiParameterDriverBehaviour(locked, new Dictionary<string, float> { [PARAM_TIME_UP] = 0f, [PARAM_RESTART] = 0f }, localOnly: true);
            for (int i = 0; i < layers.Length; i++)
                if (IsASSManagedLayerName(layers[i].name))
                    weights.AddLayerWeightControl(locked, i, 1f, TargetLayerPrefix + layers[i].name);
            var unlock = Utils.CreateTransition(locked, unlocked);
            unlock.AddCondition(AnimatorConditionMode.If, 0f, PARAM_PASSWORD_CORRECT);
            var rearm = Utils.CreateTransition(unlocked, locked);
            rearm.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM_PASSWORD_CORRECT);
            var restart = Utils.CreateAnyStateTransition(lifecycle.stateMachine, locked);
            restart.canTransitionToSelf = true;
            restart.AddCondition(AnimatorConditionMode.If, 0f, PARAM_RESTART);
            Utils.AddIsLocalCondition(restart, controller, isTrue: true);
            Utils.AddSubAsset(controller, lifecycle.stateMachine);
            Utils.AddLayerAndGetIndex(controller, lifecycle);
        }
    }
}
