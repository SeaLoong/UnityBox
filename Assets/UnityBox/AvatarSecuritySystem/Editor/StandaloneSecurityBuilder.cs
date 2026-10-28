using System;
using System.IO;
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
                throw new ArgumentException("Supply one or more gesture values from 0 through 7.", nameof(config));
            if (!controllerPath.StartsWith("Assets/", StringComparison.Ordinal) || !controllerPath.EndsWith(".controller", StringComparison.Ordinal))
                throw new ArgumentException("Choose a .controller path below Assets.", nameof(controllerPath));
            string folder = Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            Obfuscator.Initialize(root.name, config.disableObfuscation, config.enableDecoyLayers,
                config.enableDecoyStates, folder);
            Constants.ApplyObfuscation();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var idle = controller.layers[0].stateMachine.AddState("Idle");
            idle.writeDefaultValues = false;
            idle.motion = Utils.GetOrCreateEmptyClip(folder, Constants.SHARED_EMPTY_CLIP_FILE);
            Utils.EnsureInputParameters(controller);
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == Constants.PARAM_IS_LOCAL) parameters[i].defaultBool = true;
            controller.parameters = parameters;
            if (config.successSound == null) config.successSound = Resources.Load<AudioClip>(Constants.AUDIO_PASSWORD_SUCCESS);
            if (config.warningBeep == null) config.warningBeep = Resources.Load<AudioClip>(Constants.AUDIO_COUNTDOWN_WARNING);
            new Feedback(root, config).Generate();
            var adapter = UnityAnimatorBehaviourAdapter.Instance;
            new Lock(controller, root, config, adapter).Generate();
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
    }
}
