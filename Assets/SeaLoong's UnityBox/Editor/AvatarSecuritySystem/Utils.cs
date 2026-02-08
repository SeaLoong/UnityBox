using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCAnimatorPlayAudio = VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Animator 工具类 - 提供创建和操作 AnimatorController 的辅助方法
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// 共享的空 AnimationClip（节省文件大小）
        /// </summary>
        private static AnimationClip _sharedEmptyClip;

        public static AnimationClip SharedEmptyClip
        {
            get
            {
                if (_sharedEmptyClip != null)
                {
                    return _sharedEmptyClip;
                }

                _sharedEmptyClip = LoadOrCreateSharedEmptyClip();
                return _sharedEmptyClip;
            }
        }

        private static AnimationClip LoadOrCreateSharedEmptyClip()
        {
            string path = $"{Constants.ASSET_FOLDER}/{Constants.SHARED_EMPTY_CLIP_NAME}";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

            if (clip != null)
            {
                return clip;
            }

            return CreateAndSaveEmptyClip(path);
        }

        private static AnimationClip CreateAndSaveEmptyClip(string path)
        {
            var newClip = new AnimationClip { legacy = false };
            ConfigureEmptyClipSettings(newClip);

            System.IO.Directory.CreateDirectory(Constants.ASSET_FOLDER);
            AssetDatabase.CreateAsset(newClip, path);

            return newClip;
        }

        private static void ConfigureEmptyClipSettings(AnimationClip clip)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        /// <summary>
        /// 创建一个新的 AnimatorControllerLayer
        /// </summary>
        public static AnimatorControllerLayer CreateLayer(string name, float defaultWeight = 1f)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = name,
                hideFlags = HideFlags.HideInHierarchy
            };

            var layer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = defaultWeight,
                stateMachine = stateMachine
            };

            return layer;
        }

        /// <summary>
        /// 添加参数（如果不存在）
        /// </summary>
        public static void AddParameterIfNotExists(AnimatorController controller, string name,
            AnimatorControllerParameterType type, bool defaultBool = false, int defaultInt = 0,
            float defaultFloat = 0f)
        {
            if (ParameterExists(controller, name))
            {
                return;
            }

            var parameter = CreateAnimatorParameter(name, type, defaultBool, defaultInt, defaultFloat);
            controller.AddParameter(parameter);
        }

        private static bool ParameterExists(AnimatorController controller, string name) =>
            controller.parameters.Any(p => p.name == name);

        private static AnimatorControllerParameter CreateAnimatorParameter(string name,
            AnimatorControllerParameterType type, bool defaultBool, int defaultInt, float defaultFloat)
        {
            return new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultBool = defaultBool,
                defaultInt = defaultInt,
                defaultFloat = defaultFloat
            };
        }

        /// <summary>
        /// 创建状态转换
        /// </summary>
        public static AnimatorStateTransition CreateTransition(AnimatorState from, AnimatorState to,
            bool hasExitTime = false, float exitTime = 0f, float duration = 0f)
        {
            var transition = from.AddTransition(to);
            ConfigureTransition(transition, hasExitTime, exitTime, duration);
            return transition;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, bool hasExitTime,
            float exitTime, float duration)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = duration;
            transition.hasFixedDuration = true;
        }

        /// <summary>
        /// 批量配置 Any State 转换的公共属性
        /// </summary>
        private static void ConfigureAnyStateTransition(AnimatorStateTransition transition, float duration = 0f)
        {
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.hasFixedDuration = true;
        }

        /// <summary>
        /// 创建 Any State 转换
        /// </summary>
        public static AnimatorStateTransition CreateAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState to, float duration = 0f)
        {
            var transition = stateMachine.AddAnyStateTransition(to);
            ConfigureAnyStateTransition(transition, duration);
            return transition;
        }

        /// <summary>
        /// 获取 GameObject 相对于 Avatar Root 的路径
        /// </summary>
        public static string GetRelativePath(GameObject root, GameObject target)
        {
            if (root == target) return "";

            var parts = new List<string>();
            var t = target.transform;

            while (t != null && t != root.transform)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            if (t == null)
            {
                Debug.LogError($"Target {target.name} is not a child of root {root.name}");
                return "";
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// 复用空 Clip 优化状态
        /// </summary>
        public static void OptimizeStates(AnimatorStateMachine stateMachine)
        {
            var emptyClip = SharedEmptyClip;
            
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.motion == null)
                {
                    childState.state.motion = emptyClip;
                }
            }

            // 递归处理子状态机
            foreach (var childMachine in stateMachine.stateMachines)
            {
                OptimizeStates(childMachine.stateMachine);
            }
        }

        /// <summary>
        /// 添加子资产到 Controller（自动检查有效性和重复）
        /// </summary>
        public static void AddSubAsset(AnimatorController controller, Object asset)
        {
            if (asset == null || controller == null || IsAssetExternalOrDuplicate(asset, controller))
                return;

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            AssetDatabase.AddObjectToAsset(asset, controllerPath);
        }

        private static bool IsAssetExternalOrDuplicate(Object asset, AnimatorController controller)
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string assetPath = AssetDatabase.GetAssetPath(asset);
            
            if (!string.IsNullOrEmpty(assetPath) && assetPath != controllerPath) return true;
            return AssetDatabase.LoadAllAssetsAtPath(controllerPath).Any(existing => existing == asset);
        }

        /// <summary>
        /// 批量添加子资产
        /// </summary>
        public static void AddSubAssets(AnimatorController controller, params Object[] assets)
        {
            foreach (var asset in assets)
                AddSubAsset(controller, asset);
        }

        /// <summary>
        /// 保存资产
        /// </summary>
        public static void SaveAndRefresh() => AssetDatabase.SaveAssets();

        /// <summary>
        /// 记录优化统计信息
        /// </summary>
        public static void LogOptimizationStats(AnimatorController controller, string systemName = "ASS")
        {
            var stats = GatherOptimizationStats(controller);
            LogOptimizationStatistics(stats, systemName);
        }

        private class OptimizationStats
        {
            public int StateCount { get; set; }
            public int TransitionCount { get; set; }
            public int BlendTreeCount { get; set; }
            public long FileSizeBytes { get; set; }
        }

        private static OptimizationStats GatherOptimizationStats(AnimatorController controller)
        {
            int stateCount = 0;
            int transitionCount = 0;
            int blendTreeCount = 0;

            foreach (var layer in controller.layers)
            {
                CountStates(layer.stateMachine, ref stateCount, ref transitionCount, ref blendTreeCount);
            }

            long fileSize = GetControllerFileSize(controller);

            return new OptimizationStats
            {
                StateCount = stateCount,
                TransitionCount = transitionCount,
                BlendTreeCount = blendTreeCount,
                FileSizeBytes = fileSize
            };
        }

        private static long GetControllerFileSize(AnimatorController controller)
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath))
            {
                return 0;
            }

            var fileInfo = new System.IO.FileInfo(controllerPath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }

        private static void LogOptimizationStatistics(OptimizationStats stats, string systemName)
        {
            float fileSizeKB = stats.FileSizeBytes / 1024f;
            float avgSizePerState = stats.StateCount > 0 ? stats.FileSizeBytes / stats.StateCount / 1024f : 0;

            string message = $"[{systemName}] 优化统计:\n" +
                            $"  状态数: {stats.StateCount}\n" +
                            $"  转换数: {stats.TransitionCount}\n" +
                            $"  BlendTree数: {stats.BlendTreeCount}\n" +
                            $"  文件大小: {fileSizeKB:F2} KB\n" +
                            $"  平均每状态: {avgSizePerState:F2} KB";

            Debug.Log(message);
        }

        private static void CountStates(AnimatorStateMachine sm, ref int states, ref int transitions, ref int blendTrees)
        {
            states += sm.states.Length;
            transitions += sm.anyStateTransitions.Length;

            foreach (var state in sm.states)
            {
                transitions += state.state.transitions.Length;
                if (state.state.motion is BlendTree)
                    blendTrees++;
            }

            foreach (var childMachine in sm.stateMachines)
            {
                CountStates(childMachine.stateMachine, ref states, ref transitions, ref blendTrees);
            }
        }

        /// <summary>
        /// 在状态上添加 VRC Animator Layer Control 行为（用于控制单个层的权重）
        /// </summary>
        public static void AddLayerControlBehaviour(AnimatorState state, int layerIndex, float goalWeight, float blendDuration = 0f)
        {
            ConfigureLayerControl(state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>(),
                layerIndex, goalWeight, blendDuration);
        }

        /// <summary>
        /// 在状态上添加多个层权重控制行为
        /// </summary>
        public static void AddMultiLayerControlBehaviour(AnimatorState state, int[] layerIndices, float goalWeight, float blendDuration = 0f)
        {
            foreach (int layerIndex in layerIndices)
                ConfigureLayerControl(state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>(),
                    layerIndex, goalWeight, blendDuration);
        }
        
        private static void ConfigureLayerControl(VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl behaviour,
            int layerIndex, float goalWeight, float blendDuration)
        {
            behaviour.layer = layerIndex;
            behaviour.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX;
            behaviour.goalWeight = goalWeight;
            behaviour.blendDuration = blendDuration;
        }

        /// <summary>
        /// 在状态上添加 VRC Avatar Parameter Driver 行为（单个参数）
        /// </summary>
        public static void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false)
        {
            AddParameterDriverBehaviourInternal(state, new Dictionary<string, float> { { parameterName, value } }, localOnly);
        }

        /// <summary>
        /// 在状态上添加多个参数驱动
        /// </summary>
        public static void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
        {
            AddParameterDriverBehaviourInternal(state, parameters, localOnly);
        }
        
        private static void AddParameterDriverBehaviourInternal(AnimatorState state, Dictionary<string, float> parameters, bool localOnly)
        {
            var behaviour = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            behaviour.localOnly = localOnly;
            behaviour.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();
            
            foreach (var kvp in parameters)
                behaviour.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter
                {
                    name = kvp.Key,
                    value = kvp.Value,
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set
                });
        }

        private const float AUDIO_SOURCE_VOLUME = 0.5f;

        /// <summary>
        /// 在状态上添加 VRC Animator Play Audio 行为
        /// </summary>
        public static void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip)
        {
            if (clip == null) return;

            var behaviour = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            behaviour.SourcePath = audioSourcePath;
            behaviour.Clips = new AudioClip[] { clip };
            behaviour.PlayOnEnter = true;
            behaviour.StopOnEnter = false;
            behaviour.StopOnExit = false;
            behaviour.PlayOnExit = false;
            behaviour.Loop = false;
            behaviour.Volume = new Vector2(AUDIO_SOURCE_VOLUME, AUDIO_SOURCE_VOLUME);
            behaviour.VolumeApplySettings = VRCAnimatorPlayAudio.ApplySettings.ApplyIfStopped;
        }
    }
}
