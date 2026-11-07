using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class StandaloneSecurityBuilder
    {
        /// <summary>Builds a standalone Unity controller. The caller supplies gesture input through Animator parameters.</summary>
        public static AnimatorController Build(GameObject root, ASSConfigData config, string controllerPath)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config = config.Clone();
            config.defaultEnableDefense = false;
            config.disableDefense = true;
            if (!config.IsPasswordValid() || config.gesturePassword == null || config.gesturePassword.Count == 0)
                throw new ArgumentException(I18n.T("build.invalid_password"), nameof(config));
            if (!controllerPath.StartsWith("Assets/", StringComparison.Ordinal) || !controllerPath.EndsWith(".controller", StringComparison.Ordinal))
                throw new ArgumentException(I18n.T("build.invalid_output_path"), nameof(controllerPath));
            string folder = Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            Obfuscator.Initialize(root.name, config.disableObfuscation, config.enableDecoyLayers,
                config.enableDecoyStates, folder);
            Constants.ApplyObfuscation();
            var animator = root.GetComponent<Animator>();
            var source = animator != null ? animator.runtimeAnimatorController : null;
            var controller = source == null
                ? AnimatorController.CreateAnimatorControllerAtPath(controllerPath)
                : ControllerGraphCopy.CreateEditableCopy(source, controllerPath);
            RemovePreviouslyGeneratedLayers(controller, root.name);
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            if (controller.layers[0].stateMachine.states.Length == 0)
            {
                var idle = controller.layers[0].stateMachine.AddState("Idle");
                idle.writeDefaultValues = true;
                idle.motion = Utils.GetOrCreateEmptyClip(folder, Constants.SHARED_EMPTY_CLIP_FILE);
            }
            bool hadLocalParameter = controller.parameters.Any(parameter => parameter.name == Constants.PARAM_IS_LOCAL);
            Utils.EnsureInputParameters(controller);
            if (!hadLocalParameter)
            {
                var parameters = controller.parameters;
                parameters.Single(parameter => parameter.name == Constants.PARAM_IS_LOCAL).defaultBool = true;
                controller.parameters = parameters;
            }
            if (config.successSound == null) config.successSound = Resources.Load<AudioClip>(Constants.AUDIO_PASSWORD_SUCCESS);
            if (config.warningBeep == null) config.warningBeep = Resources.Load<AudioClip>(Constants.AUDIO_COUNTDOWN_WARNING);
            new Feedback(root, config).Generate();
            var adapter = UnityAnimatorBehaviourAdapter.Instance;
            new GesturePassword(controller, root, config, adapter).Generate();
            var countdown = new Countdown(controller, root, config, adapter);
            countdown.Generate();
            countdown.GenerateAudioLayer();
            AnimatorLifecycle.Generate(controller, adapter, adapter);
            var input = root.GetComponent<StandaloneSecurityInput>() ?? root.AddComponent<StandaloneSecurityInput>();
            input.passwordParameter = Constants.PARAM_PASSWORD_CORRECT;
            input.timeoutParameter = Constants.PARAM_TIME_UP;
            input.restartParameter = Constants.PARAM_RESTART;
            Utils.SaveControllerGraph(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void RemovePreviouslyGeneratedLayers(AnimatorController controller, string avatarName)
        {
            var names = new[]
            {
                (key: "LAYER_LOCK", clean: "ASS_Lock"),
                (key: "LAYER_PASSWORD_INPUT", clean: "ASS_PasswordInput"),
                (key: "LAYER_COUNTDOWN", clean: "ASS_Countdown"),
                (key: "LAYER_AUDIO", clean: "ASS_Audio"),
                (key: "LAYER_LIFECYCLE", clean: "ASS_Lifecycle"),
                (key: "LAYER_DEFENSE", clean: "ASS_Defense"),
            };
            var kept = controller.layers.Where(layer => !Constants.IsASSManagedLayerName(layer.name)
                && !names.Any(name => layer.name == Obfuscator.GetObfuscatedLayerNameForCleanup(name.key, name.clean, avatarName))).ToArray();
            controller.layers = kept;
        }
    }
}
