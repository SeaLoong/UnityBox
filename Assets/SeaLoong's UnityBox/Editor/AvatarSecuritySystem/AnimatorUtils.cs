using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#endif

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// Animator 工具类 - 提供创建和操作 AnimatorController 的辅助方法
    /// </summary>
    public static class AnimatorUtils
    {
        /// <summary>
        /// 共享的空 AnimationClip（节省文件大小）
        /// </summary>
        private static AnimationClip _sharedEmptyClip;
        public static AnimationClip SharedEmptyClip
        {
            get
            {
                if (_sharedEmptyClip == null)
                {
                    string path = $"{Constants.ASSET_FOLDER}/{Constants.SHARED_EMPTY_CLIP_NAME}";
                    _sharedEmptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    
                    if (_sharedEmptyClip == null)
                    {
                        _sharedEmptyClip = new AnimationClip { legacy = false };
                        var settings = AnimationUtility.GetAnimationClipSettings(_sharedEmptyClip);
                        settings.loopTime = false;
                        AnimationUtility.SetAnimationClipSettings(_sharedEmptyClip, settings);
                        
                        System.IO.Directory.CreateDirectory(Constants.ASSET_FOLDER);
                        AssetDatabase.CreateAsset(_sharedEmptyClip, path);
                    }
                }
                return _sharedEmptyClip;
            }
        }

        /// <summary>
        /// 创建一个新的 AnimatorControllerLayer
        /// </summary>
        public static AnimatorControllerLayer CreateLayer(string name, float defaultWeight = 1f)
        {
            var layer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = defaultWeight,
                stateMachine = new AnimatorStateMachine
                {
                    name = name,
                    hideFlags = HideFlags.HideInHierarchy
                }
            };
            return layer;
        }

        /// <summary>
        /// 添加 Bool 参数（如果不存在）
        /// </summary>
        public static void AddParameterIfNotExists(AnimatorController controller, string name, AnimatorControllerParameterType type, bool defaultBool = false, int defaultInt = 0, float defaultFloat = 0f)
        {
            if (controller.parameters.Any(p => p.name == name))
                return;

            var param = new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultBool = defaultBool,
                defaultInt = defaultInt,
                defaultFloat = defaultFloat
            };
            controller.AddParameter(param);
        }

        /// <summary>
        /// 创建状态转换
        /// </summary>
        public static AnimatorStateTransition CreateTransition(AnimatorState from, AnimatorState to, bool hasExitTime = false, float exitTime = 0f, float duration = 0f)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = duration;
            transition.hasFixedDuration = true;
            return transition;
        }

        /// <summary>
        /// 创建 Any State 转换
        /// </summary>
        public static AnimatorStateTransition CreateAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState to, float duration = 0f)
        {
            var transition = stateMachine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.hasFixedDuration = true;
            return transition;
        }

        /// <summary>
        /// 创建退出转换
        /// </summary>
        public static AnimatorStateTransition CreateExitTransition(AnimatorState from, float exitTime = 0.95f)
        {
            var transition = from.AddExitTransition();
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = 0f;
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
        /// 创建 Direct BlendTree（用于大量状态压缩）
        /// </summary>
        public static BlendTree CreateDirectBlendTree(string name, AnimationClip[] clips, string[] parameters)
        {
            var blendTree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy
            };

            var children = new ChildMotion[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                children[i] = new ChildMotion
                {
                    motion = clips[i],
                    directBlendParameter = parameters[i],
                    timeScale = 1f
                };
            }
            blendTree.children = children;

            return blendTree;
        }

        /// <summary>
        /// 添加子资产到 Controller
        /// </summary>
        public static void AddSubAsset(AnimatorController controller, Object asset)
        {
            if (asset == null || controller == null) return;
            
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath)) return;

            // 检查asset是否已经是一个独立的文件（如SharedEmptyClip）
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(assetPath) && assetPath != controllerPath)
            {
                // 这个asset已经是独立文件，不需要添加为子资源
                return;
            }

            // 检查asset是否已经被添加到当前controller
            Object[] existingAssets = AssetDatabase.LoadAllAssetsAtPath(controllerPath);
            foreach (var existing in existingAssets)
            {
                if (existing == asset)
                {
                    // 已经存在，不需要重复添加
                    return;
                }
            }

            AssetDatabase.AddObjectToAsset(asset, controllerPath);
        }

        /// <summary>
        /// 批量添加子资产
        /// </summary>
        public static void AddSubAssets(AnimatorController controller, params Object[] assets)
        {
            foreach (var asset in assets)
            {
                AddSubAsset(controller, asset);
            }
        }

        /// <summary>
        /// 保存并刷新资产
        /// </summary>
        public static void SaveAndRefresh()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 记录优化统计信息
        /// </summary>
        public static void LogOptimizationStats(AnimatorController controller, string systemName = "ASS")
        {
            int stateCount = 0;
            int transitionCount = 0;
            int blendTreeCount = 0;

            foreach (var layer in controller.layers)
            {
                CountStates(layer.stateMachine, ref stateCount, ref transitionCount, ref blendTreeCount);
            }

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            long fileSize = 0;
            if (!string.IsNullOrEmpty(controllerPath))
            {
                var fileInfo = new System.IO.FileInfo(controllerPath);
                if (fileInfo.Exists)
                    fileSize = fileInfo.Length;
            }

            Debug.Log($"[{systemName}] 优化统计:\n" +
                      $"  状态数: {stateCount}\n" +
                      $"  转换数: {transitionCount}\n" +
                      $"  BlendTree数: {blendTreeCount}\n" +
                      $"  文件大小: {fileSize / 1024f:F2} KB\n" +
                      $"  平均每状态: {(stateCount > 0 ? fileSize / (float)stateCount / 1024f : 0):F2} KB");
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

#if VRC_SDK_VRCSDK3
        /// <summary>
        /// 创建或获取AudioSource GameObject
        /// </summary>
        public static GameObject SetupAudioSource(GameObject avatarRoot, string audioObjectName)
        {
            Transform audioTransform = avatarRoot.transform.Find(audioObjectName);
            GameObject audioObject;
            
            if (audioTransform == null)
            {
                audioObject = new GameObject(audioObjectName);
                audioObject.transform.SetParent(avatarRoot.transform, false);
                audioObject.transform.localPosition = Vector3.zero;
                audioObject.SetActive(true); // 确保AudioSource默认启用
                
                var audioSource = audioObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.spatialBlend = 0f; // 2D sound
                audioSource.volume = 1f;
                
                Debug.Log($"[ASS] 已创建 AudioSource: {audioObjectName}");
            }
            else
            {
                audioObject = audioTransform.gameObject;
                audioObject.SetActive(true); // 确保AudioSource启用
                if (audioObject.GetComponent<AudioSource>() == null)
                {
                    var audioSource = audioObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.loop = false;
                    audioSource.spatialBlend = 0f;
                    audioSource.volume = 1f;
                }
                Debug.Log($"[ASS] 使用现有 AudioSource: {audioObjectName}");
            }
            
            return audioObject;
        }

        /// <summary>
        /// 在状态上添加 VRC Animator Play Audio 行为
        /// </summary>
        public static void AddPlayAudioBehaviour(AnimatorState state, string audioSourcePath, AudioClip clip)
        {
            if (clip == null) return;

#if VRC_SDK_VRCSDK3
            var behaviour = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            behaviour.SourcePath = audioSourcePath;
            behaviour.Clips = new AudioClip[] { clip };
            behaviour.StopOnEnter = false;
            behaviour.PlayOnEnter = true;
            behaviour.StopOnExit = false;
            behaviour.PlayOnExit = false;
            behaviour.Loop = false;
#endif
        }

        /// <summary>
        /// 在状态上添加 VRC Animator Layer Control 行为
        /// 用于控制指定层的权重
        /// </summary>
        /// <param name="state">目标状态</param>
        /// <param name="layerIndex">要控制的层索引</param>
        /// <param name="goalWeight">目标权重 (0-1)</param>
        /// <param name="blendDuration">过渡时间（秒）</param>
        public static void AddLayerControlBehaviour(AnimatorState state, int layerIndex, float goalWeight, float blendDuration = 0f)
        {
#if VRC_SDK_VRCSDK3
            var behaviour = state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>();
            behaviour.layer = layerIndex;
            behaviour.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX;
            behaviour.goalWeight = goalWeight;
            behaviour.blendDuration = blendDuration;
#endif
        }

        /// <summary>
        /// 在状态上添加多个层权重控制行为
        /// </summary>
        /// <param name="state">目标状态</param>
        /// <param name="layerIndices">要控制的层索引列表</param>
        /// <param name="goalWeight">目标权重 (0-1)</param>
        /// <param name="blendDuration">过渡时间（秒）</param>
        public static void AddMultiLayerControlBehaviour(AnimatorState state, int[] layerIndices, float goalWeight, float blendDuration = 0f)
        {
#if VRC_SDK_VRCSDK3
            foreach (int layerIndex in layerIndices)
            {
                var behaviour = state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl>();
                behaviour.layer = layerIndex;
                behaviour.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX;
                behaviour.goalWeight = goalWeight;
                behaviour.blendDuration = blendDuration;
            }
#endif
        }

        /// <summary>
        /// 在状态上添加 VRC Avatar Parameter Driver 行为
        /// </summary>
        public static void AddParameterDriverBehaviour(AnimatorState state, string parameterName, float value, bool localOnly = false)
        {
            var behaviour = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            behaviour.localOnly = localOnly;
            behaviour.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>
            {
                new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter
                {
                    name = parameterName,
                    value = value,
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set
                }
            };
        }

        /// <summary>
        /// 在状态上添加多个参数驱动
        /// </summary>
        public static void AddMultiParameterDriverBehaviour(AnimatorState state, Dictionary<string, float> parameters, bool localOnly = false)
        {
            var behaviour = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            behaviour.localOnly = localOnly;
            behaviour.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();

            foreach (var kvp in parameters)
            {
                behaviour.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter
                {
                    name = kvp.Key,
                    value = kvp.Value,
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set
                });
            }
        }
#endif
    }
}
