using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Generates the build-local decoy graph shared by Unity and Avatar builds.</summary>
    public static class ObfuscationGraph
    {
        private const string DecoyOwnershipTag = "UnityBox.ASS.Decoy";

        public static bool IsGeneratedDecoyLayer(AnimatorControllerLayer layer) =>
            layer?.stateMachine?.defaultState?.tag == DecoyOwnershipTag;

        public static void Generate(AnimatorController controller, GameObject avatarRoot)
        {
            if (!Obfuscator.IsEnabled) return;
            var clips = GenerateInstructionalClips(controller, avatarRoot);
            GenerateDecoyLayer(controller, clips);
            InjectFakeStatesIntoRealLayers(controller, clips);
        }

        public static List<(string name, AnimatorControllerParameterType type, float defaultValue)>
            PrepareDecoyParameters(AnimatorController controller)
        {
            // Every generated condition must refer to a declared parameter even
            // when decoy layers are disabled and only fake states are requested.
            Utils.EnsureInputParameters(controller, ensureIsLocal: false);
            var parameters = Obfuscator.GetDecoyParameters();
            foreach (var parameter in parameters)
                Utils.AddParameterIfNotExists(controller, parameter.name, parameter.type,
                    defaultBool: parameter.defaultValue > 0.5f,
                    defaultInt: (int)parameter.defaultValue,
                    defaultFloat: parameter.defaultValue);
            if (!parameters.Any(p => p.type == AnimatorControllerParameterType.Bool))
            {
                string guard = Obfuscator.Param("Guard", "_ASS_Guard");
                Utils.AddParameterIfNotExists(controller, guard, AnimatorControllerParameterType.Bool, false);
                parameters.Add((guard, AnimatorControllerParameterType.Bool, 0f));
            }
            return parameters;
        }
        public static void InjectFakeStatesIntoRealLayers(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyStatesEnabled) return;
            var decoyParams = PrepareDecoyParameters(controller);
            if (decoyParams.Count == 0) return;
            var emptyClip = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);
            int injectedCount = 0;
            foreach (var layer in controller.layers)
            {
                if (!Constants.IsASSManagedLayerName(layer.name)) continue;
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
        public static List<AnimationClip> GenerateInstructionalClips(AnimatorController controller, GameObject avatarRoot = null)
        {
            if (!Obfuscator.DecoyLayersEnabled && !Obfuscator.DecoyStatesEnabled)
                return null;
            const int clipCount = 5;
            var clipNames = Obfuscator.GetInstructionalClipNames(clipCount);
            var bindingPaths = Obfuscator.GetInstructionalBindingPaths(clipCount * 3);
            if (clipNames.Length == 0) return null;
            var clips = new List<AnimationClip>();
            var dummyCurve = AnimationCurve.Constant(0f, 1f / 60f, 0f);
            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            string marker = "__ASS_Metadata_" + Obfuscator.GetContextSeed("RestrictionMetadata").ToString("x8");
            string metadataRoot = marker;
            if (avatarRoot != null)
                for (int suffix = 1; avatarRoot.transform.Find(metadataRoot) != null; suffix++)
                    metadataRoot = marker + "_" + suffix;
            bindingPaths = bindingPaths.Select(path => metadataRoot + "/" + path).ToArray();
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
                int fakePathIdx = (primaryPathIdx + 1) % bindingPaths.Length;
                string fakePath = bindingPaths[fakePathIdx];
                clip.SetCurve(fakePath, typeof(GameObject), "m_IsActive", enableCurve);
                if (i % 3 == 0)
                {
                    string extraPath = bindingPaths[(primaryPathIdx + 2) % bindingPaths.Length];
                    clip.SetCurve(extraPath, typeof(GameObject), "m_IsActive", dummyCurve);
                }
                Utils.AddSubAsset(controller, clip);
                clips.Add(clip);
            }
            Debug.Log($"[ASS] Obfuscator: Generated {clips.Count} instructional clips with fake object paths");
            return clips;
        }
        public static void GenerateDecoyLayer(AnimatorController controller,
            List<AnimationClip> instructionalClips = null)
        {
            if (!Obfuscator.DecoyLayersEnabled) return;
            var decoyLayer = Obfuscator.GetDecoyLayer();
            if (decoyLayer == null) return;

            // 诱饵层用独立的 seed 生成自己的伪随机
            uint rng = Obfuscator.GetDecoyLayerSeed();

            var decoyParams = PrepareDecoyParameters(controller);

            var layer = Utils.CreateLayer(decoyLayer.layerName, 1f);
            var sm = layer.stateMachine;
            var emptyClip = Utils.GetOrCreateEmptyClip(Obfuscator.GeneratedFolder, SHARED_EMPTY_CLIP_FILE);

            var defaultState = sm.AddState(Obfuscator.State("DecoyDefault", "Default"), new Vector3(300, 0, 0));
            defaultState.tag = DecoyOwnershipTag;
            defaultState.motion = emptyClip;
            // 诱饵层同样使用 WD On，保持 ASS 新生成状态的一致语义。
            defaultState.writeDefaultValues = true;
            sm.defaultState = defaultState;

            var fakeStates = new List<AnimatorState>();
            var instructionalNames = Obfuscator.GetInstructionalStateNames(decoyLayer.states.Length);
            int instrNameIdx = 0;

            for (int i = 0; i < decoyLayer.states.Length; i++)
            {
                bool useInstructional = (i == 0 || Obfuscator.RngInt(ref rng, 0, 2) == 0)
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
                // 诱饵状态只模拟拓扑，不写入未绑定的真实属性。
                fakeState.writeDefaultValues = true;
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
            Obfuscator.RegisterSkipSecondPassLayerName(layer.name);
            controller.AddLayer(layer);
            Debug.Log($"[ASS] Obfuscator: Added decoy layer \"{decoyLayer.layerName}\" "
                + $"({fakeStates.Count} fake states, seed=0x{rng:X8})");
        }
    }
}
