using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class Processor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1026;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Debug.Log($"[ASS] OnPreprocessAvatar called (callbackOrder={callbackOrder})");
            try
            {
                return ProcessAvatar(avatarGameObject);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ASS] Avatar processing failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
        private bool ProcessAvatar(GameObject avatarGameObject)
        {
            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogError("[ASS] VRCAvatarDescriptor not found");
                return true;
            }
            var assConfig = avatarGameObject.GetComponent<ASSComponent>();
            if (assConfig == null)
            {
                Debug.Log("[ASS] No valid AvatarSecuritySystem component found, skipping");
                return true;
            }
            Obfuscator.Initialize(avatarGameObject.name,
                disableObfuscation: assConfig.disableObfuscation,
                enableDecoyLayers: assConfig.enableDecoyLayers,
                enableDecoyStates: assConfig.enableDecoyStates,
                generatedFolder: ASSET_FOLDER);
            Constants.ApplyObfuscation();
            if (!assConfig.defaultEnableDefense)
            {
                if (!assConfig.IsPasswordValid())
                {
                    Debug.LogWarning("[ASS] Password configuration is invalid");
                    return false;
                }
                if (assConfig.gesturePassword == null || assConfig.gesturePassword.Count == 0)
                {
                    Debug.Log("[ASS] Password is empty (0 digits), ASS is disabled. Skipping generation.");
                    return true;
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode && !assConfig.enabledInPlaymode)
            {
                Debug.Log("[ASS] Play mode disabled, skipping");
                return true;
            }
            Debug.Log("[ASS] Starting to generate security system...");
            var fxController = GetFXController(descriptor);
            Utils.EnsureBuiltInVRCParameters(fxController,
                ensureIsLocal: true,
                ensureGestureParameters: true);
            LoadAudioResources(assConfig);
            new Feedback(avatarGameObject, assConfig).Generate();
            var isPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
            new Lock(fxController, avatarGameObject, assConfig, descriptor).Generate();
            if (!assConfig.defaultEnableDefense)
            {
                new GesturePassword(fxController, avatarGameObject, assConfig).Generate();
            }
            else
            {
                Debug.Log("[ASS] Default enable defense mode: skipping gesture password layer generation");
            }
            if (!assConfig.defaultEnableDefense)
            {
                var countdown = new Countdown(fxController, avatarGameObject, assConfig);
                countdown.Generate();
                countdown.GenerateAudioLayer();
            }
            else
            {
                Debug.Log("[ASS] Default enable defense mode: skipping countdown layer");
            }
            if (!assConfig.disableDefense)
            {
                try
                {
                    new Defense(fxController, avatarGameObject, assConfig, isDebugMode: isPlayMode).Generate();
                    if (isPlayMode)
                    {
                        Debug.Log("[ASS] Play mode: Added simplified defense layer");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ASS] Defense layer creation failed: {ex.Message}");
                }
            }
            if (Obfuscator.IsEnabled)
            {
                var instructionalClips = GenerateInstructionalClips(fxController);
                GenerateDecoyLayer(fxController, instructionalClips);
                InjectFakeStatesIntoRealLayers(fxController, instructionalClips);
            }
            RegisterASSParameters(descriptor, assConfig);
            Utils.SaveAndRefresh();
            Utils.LogOptimizationStats(fxController);
            Debug.Log("[ASS] Security system generation complete!");
            return true;
        }
        private void RegisterASSParameters(VRCAvatarDescriptor descriptor, ASSComponent assConfig)
        {
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters == null)
            {
                Debug.LogWarning("[ASS] VRCExpressionParameters not found, cannot register parameters");
                return;
            }
            var existingParams = expressionParameters.parameters?.ToList()
                ?? new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>();
            existingParams.RemoveAll(p => p.name == PARAM_PASSWORD_CORRECT);
            var assParams = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_PASSWORD_CORRECT,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true
                }
            };
            bool needTimeUp = !(assConfig != null && assConfig.defaultEnableDefense);
            if (needTimeUp)
            {
                existingParams.RemoveAll(p => p.name == PARAM_TIME_UP);
                assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = PARAM_TIME_UP,
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false
                });
            }
            if (Obfuscator.IsEnabled)
            {
                var decoyParams = Obfuscator.GetDecoyParameters();
                foreach (var decoy in decoyParams)
                {
                    VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType vrcType;
                    switch (decoy.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float;
                            break;
                        case AnimatorControllerParameterType.Int:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int;
                            break;
                        default:
                            vrcType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool;
                            break;
                    }
                    if (existingParams.Any(p => p.name == decoy.name) ||
                        assParams.Any(p => p.name == decoy.name))
                        continue;
                    assParams.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                    {
                        name = decoy.name,
                        valueType = vrcType,
                        defaultValue = decoy.defaultValue,
                        saved = ((uint)decoy.name.GetHashCode() & 1) == 0,
                        networkSynced = false
                    });
                }
            }
            assParams.AddRange(existingParams);
            expressionParameters.parameters = assParams.ToArray();
            EditorUtility.SetDirty(expressionParameters);
            Debug.Log($"[ASS] Registered ASS parameters: " +
                     $"{PARAM_PASSWORD_CORRECT}(synced)" +
                     (needTimeUp ? $", {PARAM_TIME_UP}(local)" : " (no TimeUp)") +
                     (Obfuscator.IsEnabled ? " + decoys" : ""));
        }
        private AnimatorController GetFXController(VRCAvatarDescriptor descriptor)
        {
            var fxLayer = descriptor.baseAnimationLayers
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            AnimatorController controller = null;
            if (fxLayer.animatorController != null && fxLayer.animatorController is AnimatorController)
            {
                controller = fxLayer.animatorController as AnimatorController;
                Debug.Log($"[ASS] Using existing FX Controller: {controller.name}");
            }
            else
            {
                string path = $"{ASSET_FOLDER}/{CONTROLLER_NAME}";
                System.IO.Directory.CreateDirectory(ASSET_FOLDER);
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                var layers = descriptor.baseAnimationLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    {
                        layers[i].animatorController = controller;
                        layers[i].isDefault = false;
                        break;
                    }
                }
                descriptor.baseAnimationLayers = layers;
                Debug.Log($"[ASS] Created new FX Controller: {path}");
            }
            return controller;
        }
        private void LoadAudioResources(ASSComponent config)
        {
            config.successSound = Resources.Load<AudioClip>(AUDIO_PASSWORD_SUCCESS);
            config.warningBeep = Resources.Load<AudioClip>(AUDIO_COUNTDOWN_WARNING);
            EnsureAudioLoadInBackground(config.successSound);
            EnsureAudioLoadInBackground(config.warningBeep);
        }
        private static void InjectFakeStatesIntoRealLayers(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyStatesEnabled) return;
            var decoyParams = Obfuscator.GetDecoyParameters();
            if (decoyParams == null || decoyParams.Count == 0) return;
            // 确保守卫参数在控制器中存在
            string guardParam = Obfuscator.IsEnabled ? Obfuscator.Param("Guard") : "_ASS_Guard";
            Utils.AddParameterIfNotExists(controller, guardParam,
                AnimatorControllerParameterType.Bool, false);
            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            int injectedCount = 0;
            foreach (var layer in controller.layers)
            {
                bool isAssLayer = layer.name == Constants.LAYER_LOCK
                    || layer.name == Constants.LAYER_PASSWORD_INPUT
                    || layer.name == Constants.LAYER_COUNTDOWN
                    || layer.name == Constants.LAYER_AUDIO
                    || layer.name == Constants.LAYER_DEFENSE;
                if (!isAssLayer) continue;
                if (layer.stateMachine == null) continue;
                // 每层独立 seed 偏移，使假状态的拓扑/参数/位置各不相同
                uint layerOffset = (uint)layer.name.GetHashCode();
                Obfuscator.InjectFakeStates(layer.stateMachine, decoyParams, emptyClip,
                    instructionalClips, layerOffset);
                injectedCount++;
            }
            if (injectedCount > 0)
                Debug.Log($"[ASS] Obfuscator: Injected fake states into {injectedCount} real layers");
        }
        private static void EnsureAudioLoadInBackground(AudioClip clip)
        {
            if (clip == null) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            bool changed = false;
            if (!importer.loadInBackground) { importer.loadInBackground = true; changed = true; }
            var settings = importer.defaultSampleSettings;
            if (settings.quality > 0.01f) { settings.quality = 0.01f; importer.defaultSampleSettings = settings; changed = true; }
            if (changed) importer.SaveAndReimport();
        }
        private static List<AnimationClip> GenerateInstructionalClips(AnimatorController controller)
        {
            if (!Obfuscator.DecoyLayersEnabled && !Obfuscator.DecoyStatesEnabled)
                return null;
            int clipCount = 5; // 增至 5 个，丰富伪装
            var clipNames = Obfuscator.GetInstructionalClipNames(clipCount);
            var bindingPaths = Obfuscator.GetInstructionalBindingPaths(clipCount * 3); // 更多虚假路径
            if (clipNames.Length == 0) return null;
            var clips = new List<AnimationClip>();
            var dummyCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            var fakeChildPaths = new[]
            {
                "_Generated/Validation/PreCheck",
                "_Diagnostics/Runtime/SafetyScan",
                "_BuildCache/PostProcess/Verify",
                "_Temp/AutoFix/WriteDefaults",
                "_Assets/IntegrityCheck/Passed",
                "__MA/AuditTrail/NoIssuesFound",
                "_Trace/LayerCompile/Complete",
            };
            for (int i = 0; i < clipNames.Length; i++)
            {
                var clip = new AnimationClip
                {
                    name = clipNames[i],
                    legacy = false
                };
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                int primaryPathIdx = (i * 2) % bindingPaths.Length;
                string primaryPath = bindingPaths[primaryPathIdx];
                clip.SetCurve(primaryPath, typeof(GameObject), "m_IsActive", dummyCurve);
                int fakePathIdx = i % fakeChildPaths.Length;
                string fakePath = fakeChildPaths[fakePathIdx];
                clip.SetCurve(fakePath, typeof(GameObject), "m_IsActive", enableCurve);
                if (i % 3 == 0)
                {
                    string extraPath = fakeChildPaths[(i + 3) % fakeChildPaths.Length];
                    clip.SetCurve(extraPath, typeof(GameObject), "m_IsActive", dummyCurve);
                }
                Utils.AddSubAsset(controller, clip);
                clips.Add(clip);
            }
            Debug.Log($"[ASS] Obfuscator: Generated {clips.Count} instructional clips with fake object paths");
            return clips;
        }
        private static void GenerateDecoyLayer(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyLayersEnabled) return;
            var decoyLayer = Obfuscator.GetDecoyLayer();
            if (decoyLayer == null) return;

            // 诱饵层用独立的 seed 生成自己的伪随机
            uint rng = Obfuscator.GetDecoyLayerSeed();

            var decoyParams = Obfuscator.GetDecoyParameters();
            foreach (var decoy in decoyParams)
                Utils.AddParameterIfNotExists(controller, decoy.name, decoy.type);

            var layer = Utils.CreateLayer(decoyLayer.layerName, 1f);
            var sm = layer.stateMachine;
            var emptyClip = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);

            var defaultState = sm.AddState("Default", new Vector3(300, 0, 0));
            defaultState.motion = emptyClip;
            defaultState.writeDefaultValues = true;
            sm.defaultState = defaultState;

            var fakeStates = new List<AnimatorState>();
            var instructionalNames = Obfuscator.GetInstructionalStateNames(decoyLayer.states.Length);
            int instrNameIdx = 0;

            for (int i = 0; i < decoyLayer.states.Length; i++)
            {
                bool useInstructional = (Obfuscator.RngInt(ref rng, 0, 2) == 0)
                    && instructionalClips != null && instructionalClips.Count > 0
                    && instrNameIdx < instructionalNames.Length;
                string stateName = useInstructional
                    ? instructionalNames[instrNameIdx++]
                    : decoyLayer.states[i];

                float x = Obfuscator.RngRange(ref rng, 50, 900);
                float y = Obfuscator.RngRange(ref rng, -400, 300);
                var fakeState = sm.AddState(stateName, new Vector3(x, y, 0));

                if (useInstructional)
                {
                    int clipIdx = Obfuscator.RngInt(ref rng, 0, instructionalClips.Count - 1);
                    fakeState.motion = instructionalClips[clipIdx];
                }
                else
                {
                    fakeState.motion = emptyClip;
                }
                fakeState.writeDefaultValues = Obfuscator.RngInt(ref rng, 0, 1) == 0;
                fakeStates.Add(fakeState);
            }

            var boolGuards = decoyParams
                .Where(p => p.type == AnimatorControllerParameterType.Bool)
                .ToList();

            for (int i = 0; i < fakeStates.Count; i++)
            {
                var trans = Utils.CreateTransition(defaultState, fakeStates[i],
                    hasExitTime: true, exitTime: Obfuscator.RngRange(ref rng, 100, 5000));
                trans.duration = Obfuscator.RngRange(ref rng, 0, 0.3f);

                if (boolGuards.Count > 0)
                {
                    var guard = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                    trans.AddCondition(AnimatorConditionMode.If, 0, guard.name);
                }
                if (Obfuscator.RngInt(ref rng, 0, 1) == 0)
                {
                    int fg = Obfuscator.RngInt(ref rng, 0, 7);
                    trans.AddCondition(AnimatorConditionMode.Equals, fg,
                        Obfuscator.RngInt(ref rng, 0, 1) == 0
                            ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                }
            }

            for (int i = 0; i < fakeStates.Count; i++)
            {
                int connections = Obfuscator.RngInt(ref rng, 1, 3);
                var seen = new HashSet<int>();
                for (int c = 0; c < connections; c++)
                {
                    int t = Obfuscator.RngInt(ref rng, 0, fakeStates.Count - 1);
                    if (t == i || !seen.Add(t)) continue;

                    var trans = fakeStates[i].AddTransition(fakeStates[t]);
                    trans.hasExitTime = true;
                    trans.exitTime = Obfuscator.RngRange(ref rng, 0.05f, 5f);
                    trans.duration = Obfuscator.RngRange(ref rng, 0, 0.3f);
                    trans.hasFixedDuration = true;

                    if (Obfuscator.RngInt(ref rng, 0, 1) == 0)
                    {
                        int fg = Obfuscator.RngInt(ref rng, 0, 7);
                        trans.AddCondition(AnimatorConditionMode.Greater, fg - 1,
                            Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                        trans.AddCondition(AnimatorConditionMode.Less, fg + 1,
                            Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT);
                    }

                    if (decoyParams.Count > 0)
                    {
                        var dp = decoyParams[Obfuscator.RngInt(ref rng, 0, decoyParams.Count - 1)];
                        if (dp.type == AnimatorControllerParameterType.Bool)
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, dp.name);
                        else if (dp.type == AnimatorControllerParameterType.Float)
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                                Obfuscator.RngRange(ref rng, -10, 10), dp.name);
                        else
                            trans.AddCondition(Obfuscator.RngInt(ref rng, 0, 1) == 0
                                ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                                Obfuscator.RngInt(ref rng, -5, 10), dp.name);
                    }
                    if (boolGuards.Count > 0)
                    {
                        var guard2 = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                        trans.AddCondition(AnimatorConditionMode.If, 0, guard2.name);
                    }
                }
            }

            // 部分假状态回到 default
            for (int i = 0; i < fakeStates.Count; i++)
            {
                if (Obfuscator.RngInt(ref rng, 0, 2) == 0) continue;
                var retTrans = Utils.CreateTransition(fakeStates[i], defaultState,
                    hasExitTime: true, exitTime: Obfuscator.RngRange(ref rng, 100, 5000));
                retTrans.duration = Obfuscator.RngRange(ref rng, 0, 0.2f);
                if (boolGuards.Count > 0)
                {
                    var guard = boolGuards[Obfuscator.RngInt(ref rng, 0, boolGuards.Count - 1)];
                    retTrans.AddCondition(AnimatorConditionMode.If, 0, guard.name);
                }
            }

            sm.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, sm);
            controller.AddLayer(layer);
            Debug.Log($"[ASS] Obfuscator: Added decoy layer \"{decoyLayer.layerName}\" "
                + $"({fakeStates.Count} fake states, seed=0x{rng:X8})");
        }
    }
}
