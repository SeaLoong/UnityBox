using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class Obfuscator
    {
        #region 初始化
        private static uint _seed;
        private static bool _initialized;
        private static bool _enabled;
        private static bool _decoyLayersEnabled;
        private static bool _decoyStatesEnabled;
        private static string _generatedFolder;
        private static readonly Dictionary<string, Shader> _shaderCache = new Dictionary<string, Shader>();
        private static readonly HashSet<string> _generatedContentAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _skipSecondPassLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static bool IsEnabled => _enabled;
        public static bool DecoyLayersEnabled => _decoyLayersEnabled;
        public static bool DecoyStatesEnabled => _decoyStatesEnabled;
        public static void Initialize(string avatarName, bool disableObfuscation,
            bool enableDecoyLayers, bool enableDecoyStates,
            string generatedFolder = null)
        {
            _enabled = !disableObfuscation;
            _decoyLayersEnabled = enableDecoyLayers && _enabled;
            _decoyStatesEnabled = enableDecoyStates && _enabled;
            _generatedFolder = generatedFolder ?? "Assets/UnityBox/AvatarSecuritySystem/Generated";
            _shaderCache.Clear();
            _generatedContentAssetPaths.Clear();
            _skipSecondPassLayerNames.Clear();
            if (!_enabled)
            {
                _initialized = true;
                return;
            }
            _seed = HashString(avatarName);
            _initialized = true;
            Debug.Log($"[ASS] Obfuscator initialized (seed=0x{_seed:X8}, avatar=\"{avatarName}\", "
                + $"obfuscation=ON, decoyLayers={_decoyLayersEnabled}, decoyStates={_decoyStatesEnabled})");
        }
        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                _seed = 0x5EED5EED;
                _enabled = false; // 默认关闭，由 Initialize() 显式开启
                _generatedFolder = "Assets/UnityBox/AvatarSecuritySystem/Generated";
                _initialized = true;
            }
        }
        #endregion
        #region 名称映射 — 内部 Key → 误导性名称 + 哈希后缀
        public static string Param(string internalKey, string cleanName = null)
        {
            EnsureInitialized();
            if (!_enabled) return cleanName ?? internalKey;
            return FormatHashName(ParamPool, internalKey, cleanName);
        }
        public static string Layer(string internalKey, string cleanName = null)
        {
            EnsureInitialized();
            if (!_enabled) return cleanName ?? internalKey;
            return FormatHashName(LayerPool, internalKey, cleanName);
        }
        public static string GameObject(string internalKey, string cleanName = null)
        {
            EnsureInitialized();
            if (!_enabled) return cleanName ?? internalKey;
            return FormatHashName(GameObjectPool, internalKey, cleanName);
        }
        public static string Clip(string internalKey, string cleanName = null)
        {
            EnsureInitialized();
            if (!_enabled) return cleanName ?? internalKey;
            return FormatHashName(ClipPool, internalKey, cleanName);
        }
        public static string State(string internalKey, string cleanName = null)
        {
            EnsureInitialized();
            if (!_enabled) return cleanName ?? internalKey;
            return FormatHashName(StatePool, internalKey, cleanName);
        }
        public static string DummyPath()
        {
            EnsureInitialized();
            if (!_enabled) return "__internal_dummy_anim__";
            return FormatHashName(DummyPool, "DummyPath", "DummyPath");
        }
        public static string ShaderName(string internalKey)
        {
            EnsureInitialized();
            if (!_enabled) return internalKey;
            return FormatHashName(ShaderPool, internalKey, internalKey);
        }
        private static string FormatHashName(string[] pool, string key, string contextHint)
        {
            uint keyHash = HashString(key);
            uint combined = _seed ^ keyHash;
            uint finalHash = MurmurFinalize(combined);
            uint clusterHash = MurmurFinalize(_seed ^ HashString(GetClusterKey(key, contextHint)));
            int variant = (int)(finalHash & 3);
            uint poolIdx = GetClusterLocalPoolIndex(pool.Length, finalHash, clusterHash);
            string baseName = pool[poolIdx];
            uint suffixVal = (finalHash >> 18) & 0x3FFF;
            bool isStateName = (pool == StatePool || pool == FakeStatePool);
            string semanticTag = GetSemanticTag(pool, key, contextHint, clusterHash);
            string phaseTag = GetPhaseTag(pool, key, contextHint, clusterHash);
            string anomalyTag = GetAnomalyTag(pool, key, contextHint, finalHash, clusterHash);
            if (isStateName)
                return FormatStateHashName(baseName, semanticTag, phaseTag, anomalyTag, suffixVal, variant);

            return FormatAssetHashName(baseName, semanticTag, phaseTag, anomalyTag, suffixVal, variant);
        }

        private static string FormatStateHashName(string baseName, string semanticTag, string phaseTag, string anomalyTag, uint suffixVal, int variant)
        {
            string name = variant switch
            {
                0 => $"{baseName}_{phaseTag}{suffixVal & 0xFF:x2}",
                1 => $"{baseName}{suffixVal & 0xFFF:x3}_{semanticTag}",
                2 => $"{semanticTag}_{baseName}_{suffixVal & 0xFFF:x3}",
                _ => $"{phaseTag}{suffixVal & 0xFF:x2}_{baseName}",
            };
            return string.IsNullOrEmpty(anomalyTag) ? name : $"{name}_{anomalyTag}";
        }

        private static string FormatAssetHashName(string baseName, string semanticTag, string phaseTag, string anomalyTag, uint suffixVal, int variant)
        {
            string name = variant switch
            {
                0 => $"{baseName}_{semanticTag}_{suffixVal & 0xFFF:x3}",
                1 => $"{baseName}_{phaseTag}{suffixVal & 0xFF:x2}",
                2 => $"{baseName}_{semanticTag}_{phaseTag}_{suffixVal & 0xFF:x2}",
                _ => $"{baseName}_{phaseTag}_v{suffixVal & 0xFFF:x3}",
            };
            return string.IsNullOrEmpty(anomalyTag) ? name : $"{name}_{anomalyTag}";
        }

        private static string GetSemanticTag(string[] pool, string key, string contextHint, uint clusterHash)
        {
            var tagPool = GetSemanticTagPool(pool, key, contextHint);
            return tagPool[(int)((clusterHash >> 8) % (uint)tagPool.Length)];
        }

        private static string GetPhaseTag(string[] pool, string key, string contextHint, uint clusterHash)
        {
            var phasePool = GetPhaseTagPool(pool, key, contextHint);
            return phasePool[(int)((clusterHash >> 13) % (uint)phasePool.Length)];
        }

        private static string GetAnomalyTag(string[] pool, string key, string contextHint, uint finalHash, uint clusterHash)
        {
            if (((finalHash >> 5) & 0x7) != 0)
                return null;

            var anomalyPool = GetAnomalyTagPool(pool, key, contextHint);
            return anomalyPool[(int)((clusterHash >> 17) % (uint)anomalyPool.Length)];
        }

        private static string[] GetSemanticTagPool(string[] pool, string key, string contextHint)
        {
            if (pool == ParamPool) return ParamSemanticTagPool;
            if (pool == LayerPool) return LayerSemanticTagPool;
            if (pool == GameObjectPool) return ObjectSemanticTagPool;
            if (pool == ClipPool) return ClipSemanticTagPool;
            if (pool == ShaderPool) return ShaderSemanticTagPool;
            if (pool == StatePool || pool == FakeStatePool) return StateSemanticTagPool;

            string semanticSource = string.IsNullOrEmpty(contextHint) ? key : contextHint + "|" + key;

            if (ContainsAny(semanticSource, "Playable", "Graph", "Route", "Dispatch", "Kernel", "Mux")) return PlayableSemanticTagPool;
            if (ContainsAny(semanticSource, "Constraint", "Retarget", "Bone", "Phys", "Cloth", "Probe")) return RigSemanticTagPool;
            if (ContainsAny(semanticSource, "Material", "Shader", "Mesh", "Texture", "BlendShape", "Morph")) return VisualSemanticTagPool;
            if (ContainsAny(semanticSource, "Audio", "Viseme", "Lip", "Sound", "Voice")) return AudioSemanticTagPool;
            if (ContainsAny(semanticSource, "Net", "Sync", "OSC", "Remote", "Interp")) return NetworkSemanticTagPool;
            if (ContainsAny(semanticSource, "Import", "Asset", "Cache", "Serialize", "Build", "Validate")) return PipelineSemanticTagPool;

            return GenericSemanticTagPool;
        }

        private static string[] GetPhaseTagPool(string[] pool, string key, string contextHint)
        {
            if (pool == ParamPool) return ParamPhaseTagPool;
            if (pool == LayerPool) return LayerPhaseTagPool;
            if (pool == GameObjectPool) return ObjectPhaseTagPool;
            if (pool == ClipPool) return ClipPhaseTagPool;
            if (pool == ShaderPool) return ShaderPhaseTagPool;
            if (pool == StatePool || pool == FakeStatePool) return StatePhaseTagPool;

            string semanticSource = string.IsNullOrEmpty(contextHint) ? key : contextHint + "|" + key;

            if (ContainsAny(semanticSource, "Playable", "Graph", "Route", "Dispatch", "Kernel", "Mux")) return PlayablePhaseTagPool;
            if (ContainsAny(semanticSource, "Constraint", "Retarget", "Bone", "Phys", "Cloth", "Probe")) return RigPhaseTagPool;
            if (ContainsAny(semanticSource, "Material", "Shader", "Mesh", "Texture", "BlendShape", "Morph")) return VisualPhaseTagPool;
            if (ContainsAny(semanticSource, "Audio", "Viseme", "Lip", "Sound", "Voice")) return AudioPhaseTagPool;
            if (ContainsAny(semanticSource, "Net", "Sync", "OSC", "Remote", "Interp")) return NetworkPhaseTagPool;
            if (ContainsAny(semanticSource, "Import", "Asset", "Cache", "Serialize", "Build", "Validate")) return PipelinePhaseTagPool;

            return PhaseTagPool;
        }

        private static string[] GetAnomalyTagPool(string[] pool, string key, string contextHint)
        {
            string semanticSource = string.IsNullOrEmpty(contextHint) ? key : contextHint + "|" + key;

            if (ContainsAny(semanticSource, "Playable", "Graph", "Route", "Dispatch", "Kernel", "Mux")) return PlayableAnomalyTagPool;
            if (ContainsAny(semanticSource, "Constraint", "Retarget", "Bone", "Phys", "Cloth", "Probe")) return RigAnomalyTagPool;
            if (ContainsAny(semanticSource, "Material", "Shader", "Mesh", "Texture", "BlendShape", "Morph")) return VisualAnomalyTagPool;
            if (ContainsAny(semanticSource, "Audio", "Viseme", "Lip", "Sound", "Voice")) return AudioAnomalyTagPool;
            if (ContainsAny(semanticSource, "Net", "Sync", "OSC", "Remote", "Interp")) return NetworkAnomalyTagPool;
            if (ContainsAny(semanticSource, "Import", "Asset", "Cache", "Serialize", "Build", "Validate")) return PipelineAnomalyTagPool;

            return GenericAnomalyTagPool;
        }

        private static string GetClusterKey(string key, string contextHint)
        {
            if (!string.IsNullOrEmpty(key))
            {
                string[] markers = {
                    "_Layer_",
                    "_ChildStateMachine",
                    "_StateMachine",
                    "_State",
                    "_BlendTree",
                    "_Clip",
                    "_MotionAssetCopy_",
                    "_PlayableControllerCopy_"
                };

                foreach (var marker in markers)
                {
                    int index = key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                        return key.Substring(0, index + marker.Length);
                }
            }

            return string.IsNullOrEmpty(contextHint) ? key : contextHint;
        }

        private static uint GetClusterLocalPoolIndex(int poolLength, uint finalHash, uint clusterHash)
        {
            if (poolLength <= 0) return 0;

            uint length = (uint)poolLength;
            uint window = Math.Min(length, length <= 8 ? length : 8u + (clusterHash & 0x7));
            if (window == 0) window = 1;

            uint clusterBase = (clusterHash >> 2) % length;
            uint localOffset = (finalHash >> 2) % window;
            return (clusterBase + localOffset) % length;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var token in tokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        #endregion
        #region Shader 混淆
        public static Shader GetObfuscatedShader(string originalShaderName)
        {
            EnsureInitialized();
            if (!_enabled) return Shader.Find(originalShaderName);
            if (_shaderCache.TryGetValue(originalShaderName, out var cached) && cached != null)
                return cached;
            var original = Shader.Find(originalShaderName);
            if (original == null)
            {
                Debug.LogWarning($"[ASS] Obfuscator: Cannot find original shader '{originalShaderName}'");
                return null;
            }
            string originalPath = AssetDatabase.GetAssetPath(original);
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogWarning($"[ASS] Obfuscator: Cannot get asset path for shader '{originalShaderName}'");
                _shaderCache[originalShaderName] = original;
                return original;
            }
            string obfuscatedName = ShaderName(originalShaderName);
            string obfuscatedFileName = obfuscatedName;
            string destPath = $"{_generatedFolder}/{obfuscatedFileName}.shader";
            var existingCopy = AssetDatabase.LoadAssetAtPath<Shader>(destPath);
            if (existingCopy != null)
            {
                _shaderCache[originalShaderName] = existingCopy;
                return existingCopy;
            }
            Directory.CreateDirectory(_generatedFolder);
            if (!AssetDatabase.CopyAsset(originalPath, destPath))
            {
                Debug.LogWarning($"[ASS] Obfuscator: Failed to copy shader '{originalShaderName}' to '{destPath}'");
                _shaderCache[originalShaderName] = original;
                return original;
            }
            var content = File.ReadAllText(destPath);
            content = content.Replace(originalShaderName, obfuscatedName);
            content = System.Text.RegularExpressions.Regex.Replace(content,
                @"Name\s+""[^""]*(?:UB_|ASS_)[^""]*""",
                "Name \"" + obfuscatedFileName + "_PASS\"");
            File.WriteAllText(destPath, content);
            AssetDatabase.Refresh();
            var copiedShader = AssetDatabase.LoadAssetAtPath<Shader>(destPath);
            if (copiedShader != null)
            {
                _shaderCache[originalShaderName] = copiedShader;
                Debug.Log($"[ASS] Obfuscator: Created obfuscated shader copy: {obfuscatedName}");
            }
            else
            {
                _shaderCache[originalShaderName] = original;
            }
            return _shaderCache[originalShaderName];
        }
        #endregion
        #region 提示词注入
        public static List<(string name, AnimatorControllerParameterType type, float defaultValue)>
            GetDecoyParameters()
        {
            EnsureInitialized();
            if (!_enabled) return new List<(string, AnimatorControllerParameterType, float)>();
            var decoys = new List<(string, AnimatorControllerParameterType, float)>();
            int count = (int)(_seed % 3) + 3; // 3-5 个
            var shuffled = ShuffleArray(DecoyParamPool, _seed + 0xDEC01);
            for (int i = 0; i < Math.Min(count, shuffled.Length); i++)
                decoys.Add(shuffled[i]);
            return decoys;
        }
        public static DecoyLayerData GetDecoyLayer()
        {
            EnsureInitialized();
            if (!_decoyLayersEnabled) return null;
            int idx = (int)(_seed % (uint)DecoyLayerPool.Length);
            var template = DecoyLayerPool[idx];
            string hashedLayerName = FormatHashName(LayerPool, template.layerName, template.layerName);
            var hashedStates = template.states
                .Select(s => FormatHashName(StatePool, s, s))
                .ToArray();
            return new DecoyLayerData
            {
                layerName = hashedLayerName,
                states = hashedStates,
                description = template.description
            };
        }
        private static uint MixSeed(uint baseSeed, uint layerId)
        {
            // 每层独立 seed 派生: 不同的层同一次调用结果不同
            return (baseSeed ^ layerId) * 0x9E3779B9 + 0x7F4A7C15;
        }

        private static float PseudoRange(ref uint state, float min, float max)
        {
            state = state * 0x85EBCA6B + 0xC2B2AE35;
            float t = (state & 0xFFFFFF) / (float)0x1000000;
            return min + t * (max - min);
        }

        private static int PseudoInt(ref uint state, int min, int maxInclusive)
        {
            state = state * 0x85EBCA6B + 0xC2B2AE35;
            return min + (int)((state & 0x7FFFFFFF) % (maxInclusive - min + 1));
        }

        // 公开版本，供 Processor.cs 使用
        public static float RngRange(ref uint rng, float min, float max)
        {
            rng = rng * 0x85EBCA6B + 0xC2B2AE35;
            float t = (rng & 0xFFFFFF) / (float)0x1000000;
            return min + t * (max - min);
        }

        public static int RngInt(ref uint rng, int min, int maxInclusive)
        {
            rng = rng * 0x85EBCA6B + 0xC2B2AE35;
            return min + (int)((rng & 0x7FFFFFFF) % (maxInclusive - min + 1));
        }

        public static uint GetDecoyLayerSeed()
        {
            EnsureInitialized();
            return (_seed ^ 0xDEC0DE) * 0x9E3779B9 + 0x7F4A7C15;
        }

        public static uint GetContextSeed(string context)
        {
            EnsureInitialized();
            if (!_enabled) return 0;
            uint h = _seed;
            foreach (char c in context) { h ^= (uint)c; h *= 0x01000193; }
            return h ^ 0xDEFACE;
        }

        public static void RegisterGeneratedAsset(Object asset)
        {
            if (asset == null) return;
            RegisterGeneratedAssetPath(AssetDatabase.GetAssetPath(asset));
        }

        public static void RegisterSkipSecondPassLayerName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return;
            _skipSecondPassLayerNames.Add(layerName);
        }

        private static void RegisterGeneratedAssetPath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalized)) return;
            _generatedContentAssetPaths.Add(normalized);
        }

        public static void PreparePlayableControllerCopies(VRCAvatarDescriptor descriptor)
        {
            EnsureInitialized();
            if (!_enabled || descriptor == null) return;

            var baseLayers = descriptor.baseAnimationLayers;
            PreparePlayableControllerCopies(baseLayers, "Base");
            descriptor.baseAnimationLayers = baseLayers;

            var specialLayers = descriptor.specialAnimationLayers;
            PreparePlayableControllerCopies(specialLayers, "Special");
            descriptor.specialAnimationLayers = specialLayers;
        }

        private static void PreparePlayableControllerCopies(VRCAvatarDescriptor.CustomAnimLayer[] layers, string scope)
        {
            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.isDefault) continue;
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                if (!(layer.animatorController is AnimatorController sourceController) || sourceController == null) continue;

                var clonedController = DuplicateAnimatorControllerForBuild(sourceController, $"{scope}_{layer.type}_{i}");
                if (clonedController == null) continue;

                layer.animatorController = clonedController;
                layer.isDefault = false;
                layers[i] = layer;
            }
        }

        public static void ObfuscatePlayableControllers(VRCAvatarDescriptor descriptor)
        {
            EnsureInitialized();
            if (!_enabled || descriptor == null) return;

            var controllers = new HashSet<AnimatorController>();
            foreach (var animLayer in descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers))
            {
                if (animLayer.isDefault) continue;
                if (animLayer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                if (animLayer.animatorController is AnimatorController controller && controller != null)
                {
                    controllers.Add(controller);
                }
            }

            int controllerCount = 0;
            int layerCount = 0;
            int stateMachineCount = 0;
            int stateCount = 0;
            int blendTreeCount = 0;
            int clipCount = 0;

            foreach (var controller in controllers)
            {
                ObfuscateAnimatorController(controller,
                    false,
                    ref layerCount,
                    ref stateMachineCount,
                    ref stateCount,
                    ref blendTreeCount,
                    ref clipCount);
                controllerCount++;
            }

            if (controllerCount > 0)
            {
                Debug.Log($"[ASS] Obfuscator: Renamed playable controllers={controllerCount}, layers={layerCount}, stateMachines={stateMachineCount}, states={stateCount}, blendTrees={blendTreeCount}, clips={clipCount}");
            }
        }

        private static void ObfuscateAnimatorController(
            AnimatorController controller,
            bool allowInternalRename,
            ref int layerCount,
            ref int stateMachineCount,
            ref int stateCount,
            ref int blendTreeCount,
            ref int clipCount)
        {
            if (controller == null) return;

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (!CanRenameGeneratedAssetPath(controllerPath))
                return;

            controller.name = Layer(GetStableObjectKey("PlayableController", controller), controller.name);
            EditorUtility.SetDirty(controller);

            var renamedMotions = new HashSet<Motion>();
            var layers = controller.layers;
            string controllerKey = GetStableObjectKey("Controller", controller);

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (Constants.IsASSManagedLayerName(layer.name) || _skipSecondPassLayerNames.Contains(layer.name))
                {
                    layers[i] = layer;
                    continue;
                }

                layer.name = Layer($"{controllerKey}_Layer_{i}", layer.name);
                layerCount++;

                if (allowInternalRename && layer.stateMachine != null)
                {
                    ObfuscateStateMachineRecursive(
                        layer.stateMachine,
                        $"{controllerKey}_Layer_{i}",
                        renamedMotions,
                        ref stateMachineCount,
                        ref stateCount,
                        ref blendTreeCount,
                        ref clipCount);
                }

                layers[i] = layer;
            }

            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        private static AnimatorController DuplicateAnimatorControllerForBuild(AnimatorController sourceController, string slotKey)
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourceController);
            if (string.IsNullOrEmpty(sourcePath))
                return sourceController;
            if (IsAssetUnderGeneratedFolder(sourcePath))
                return sourceController;

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string guidSuffix = ShortGuid(sourceGuid);
            string controllerFolder = $"{_generatedFolder}/PlayableControllers";
            EnsureFolder(controllerFolder);

            string controllerFileBase = SanitizeFileName(Layer($"PlayableControllerCopy_{slotKey}_{sourceGuid}", sourceController.name));
            string controllerPath = $"{controllerFolder}/{controllerFileBase}_{guidSuffix}.controller";
            DeleteAssetIfExists(controllerPath);

            if (!AssetDatabase.CopyAsset(sourcePath, controllerPath))
            {
                Debug.LogWarning($"[ASS] Obfuscator: Failed to copy playable controller '{sourcePath}' to '{controllerPath}'");
                return sourceController;
            }

            var clonedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (clonedController == null)
            {
                Debug.LogWarning($"[ASS] Obfuscator: Failed to load copied playable controller '{controllerPath}'");
                return sourceController;
            }

            RegisterGeneratedAssetPath(controllerPath);

            string motionFolder = $"{controllerFolder}/{controllerFileBase}_{guidSuffix}_Motions";
            DeleteAssetIfExists(motionFolder);
            EnsureFolder(motionFolder);
            CloneReferencedMotionsForController(clonedController, motionFolder, slotKey);
            EditorUtility.SetDirty(clonedController);
            return clonedController;
        }

        private static void CloneReferencedMotionsForController(AnimatorController controller, string motionFolder, string slotKey)
        {
            if (controller == null) return;

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            var clonedMotions = new Dictionary<Motion, Motion>();
            var copiedAssetPaths = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            bool controllerChanged = false;

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                if (CloneReferencedMotionsInStateMachine(layer.stateMachine, controllerPath, motionFolder, slotKey, clonedMotions, copiedAssetPaths))
                    controllerChanged = true;
            }

            if (controllerChanged)
                EditorUtility.SetDirty(controller);
        }

        private static bool CloneReferencedMotionsInStateMachine(
            AnimatorStateMachine stateMachine,
            string controllerPath,
            string motionFolder,
            string slotKey,
            Dictionary<Motion, Motion> clonedMotions,
            Dictionary<string, string> copiedAssetPaths)
        {
            bool changed = false;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                var clonedMotion = CloneReferencedMotion(state.motion, controllerPath, motionFolder, slotKey, clonedMotions, copiedAssetPaths);
                if (clonedMotion != state.motion)
                {
                    state.motion = clonedMotion;
                    EditorUtility.SetDirty(state);
                    changed = true;
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                if (CloneReferencedMotionsInStateMachine(childMachine.stateMachine, controllerPath, motionFolder, slotKey, clonedMotions, copiedAssetPaths))
                    changed = true;
            }

            return changed;
        }

        private static Motion CloneReferencedMotion(
            Motion motion,
            string controllerPath,
            string motionFolder,
            string slotKey,
            Dictionary<Motion, Motion> clonedMotions,
            Dictionary<string, string> copiedAssetPaths)
        {
            if (motion == null) return null;
            if (clonedMotions.TryGetValue(motion, out var existingMotion))
                return existingMotion;

            string motionPath = AssetDatabase.GetAssetPath(motion);
            bool isInternalMotion = string.IsNullOrEmpty(motionPath) || PathsEqual(motionPath, controllerPath);
            Motion resolvedMotion = motion;

            if (!isInternalMotion)
            {
                string copiedAssetPath = GetOrCreateCopiedMotionAssetPath(motionPath, motionFolder, slotKey, copiedAssetPaths);
                var copiedMotion = ResolveMotionFromCopiedAsset(motion, copiedAssetPath);
                if (copiedMotion != null)
                {
                    resolvedMotion = copiedMotion;
                }
                else
                {
                    Debug.LogWarning($"[ASS] Obfuscator: Failed to resolve copied motion '{motion.name}' from '{motionPath}'");
                    resolvedMotion = motion;
                }
            }

            clonedMotions[motion] = resolvedMotion;

            if (resolvedMotion is BlendTree blendTree)
                CloneReferencedMotionsInBlendTree(blendTree, controllerPath, motionFolder, slotKey, clonedMotions, copiedAssetPaths);

            return resolvedMotion;
        }

        private static void CloneReferencedMotionsInBlendTree(
            BlendTree blendTree,
            string controllerPath,
            string motionFolder,
            string slotKey,
            Dictionary<Motion, Motion> clonedMotions,
            Dictionary<string, string> copiedAssetPaths)
        {
            var children = blendTree.children;
            bool changed = false;

            for (int i = 0; i < children.Length; i++)
            {
                var clonedMotion = CloneReferencedMotion(children[i].motion, controllerPath, motionFolder, slotKey, clonedMotions, copiedAssetPaths);
                if (clonedMotion != children[i].motion)
                {
                    children[i].motion = clonedMotion;
                    changed = true;
                }
            }

            if (changed)
            {
                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }
        }

        private static string GetOrCreateCopiedMotionAssetPath(
            string sourceAssetPath,
            string motionFolder,
            string slotKey,
            Dictionary<string, string> copiedAssetPaths)
        {
            if (copiedAssetPaths.TryGetValue(sourceAssetPath, out var existingPath))
                return existingPath;

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourceAssetPath);
            string guidSuffix = ShortGuid(sourceGuid);
            string extension = Path.GetExtension(sourceAssetPath);
            if (string.IsNullOrEmpty(extension)) extension = ".asset";

            string sourceFileBase = Path.GetFileNameWithoutExtension(sourceAssetPath);
            string copiedFileBase = SanitizeFileName(Clip($"PlayableMotionAssetCopy_{slotKey}_{sourceGuid}", sourceFileBase));
            string copiedAssetPath = $"{motionFolder}/{copiedFileBase}_{guidSuffix}{extension}";
            DeleteAssetIfExists(copiedAssetPath);

            if (!AssetDatabase.CopyAsset(sourceAssetPath, copiedAssetPath))
            {
                Debug.LogWarning($"[ASS] Obfuscator: Failed to copy motion asset '{sourceAssetPath}' to '{copiedAssetPath}'");
                copiedAssetPaths[sourceAssetPath] = sourceAssetPath;
                return sourceAssetPath;
            }

            RegisterGeneratedAssetPath(copiedAssetPath);
            copiedAssetPaths[sourceAssetPath] = copiedAssetPath;
            return copiedAssetPath;
        }

        private static Motion ResolveMotionFromCopiedAsset(Motion sourceMotion, string copiedAssetPath)
        {
            if (string.IsNullOrEmpty(copiedAssetPath))
                return null;

            string sourceAssetPath = AssetDatabase.GetAssetPath(sourceMotion);
            if (string.IsNullOrEmpty(sourceAssetPath))
                return null;

            if (PathsEqual(sourceAssetPath, copiedAssetPath))
                return sourceMotion;

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sourceMotion, out _, out long sourceLocalId);
            var copiedMotions = AssetDatabase.LoadAllAssetsAtPath(copiedAssetPath)
                .OfType<Motion>()
                .Where(candidate => candidate.GetType() == sourceMotion.GetType())
                .ToList();

            foreach (var candidate in copiedMotions)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long candidateLocalId)
                    && sourceLocalId != 0
                    && candidateLocalId == sourceLocalId)
                {
                    return candidate;
                }
            }

            var sourceMotions = AssetDatabase.LoadAllAssetsAtPath(sourceAssetPath)
                .OfType<Motion>()
                .Where(candidate => candidate.GetType() == sourceMotion.GetType())
                .ToList();

            var sourceSameName = sourceMotions.Where(candidate => candidate.name == sourceMotion.name).ToList();
            var copiedSameName = copiedMotions.Where(candidate => candidate.name == sourceMotion.name).ToList();

            if (sourceSameName.Count == 1 && copiedSameName.Count == 1)
                return copiedSameName[0];

            if (sourceMotions.Count == 1 && copiedMotions.Count == 1)
                return copiedMotions[0];

            return null;
        }

        private static void ObfuscateStateMachineRecursive(
            AnimatorStateMachine stateMachine,
            string keyPrefix,
            HashSet<Motion> renamedMotions,
            ref int stateMachineCount,
            ref int stateCount,
            ref int blendTreeCount,
            ref int clipCount)
        {
            if (stateMachine == null) return;

            stateMachine.name = Layer(GetStableObjectKey($"{keyPrefix}_StateMachine", stateMachine), stateMachine.name);
            EditorUtility.SetDirty(stateMachine);
            stateMachineCount++;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                string stateKeyPrefix = GetStableObjectKey($"{keyPrefix}_State", state);
                state.name = State(stateKeyPrefix, state.name);
                EditorUtility.SetDirty(state);
                stateCount++;

                ObfuscateMotionRecursive(state.motion, stateKeyPrefix, renamedMotions, ref blendTreeCount, ref clipCount);
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                ObfuscateStateMachineRecursive(
                    childMachine.stateMachine,
                    GetStableObjectKey($"{keyPrefix}_ChildStateMachine", childMachine.stateMachine),
                    renamedMotions,
                    ref stateMachineCount,
                    ref stateCount,
                    ref blendTreeCount,
                    ref clipCount);
            }
        }

        private static void ObfuscateMotionRecursive(
            Motion motion,
            string contextKey,
            HashSet<Motion> renamedMotions,
            ref int blendTreeCount,
            ref int clipCount)
        {
            if (motion == null || !renamedMotions.Add(motion)) return;

            if (motion is BlendTree blendTree)
            {
                if (!CanRenameGeneratedAssetPath(AssetDatabase.GetAssetPath(blendTree))) return;

                string blendTreeKey = GetStableObjectKey($"{contextKey}_BlendTree", blendTree);
                blendTree.name = Clip(blendTreeKey, blendTree.name);
                EditorUtility.SetDirty(blendTree);
                blendTreeCount++;

                var children = blendTree.children;
                for (int i = 0; i < children.Length; i++)
                    ObfuscateMotionRecursive(children[i].motion, $"{blendTreeKey}_Child{i}", renamedMotions, ref blendTreeCount, ref clipCount);

                return;
            }

            if (motion is AnimationClip clip)
            {
                if (!CanRenameGeneratedAssetPath(AssetDatabase.GetAssetPath(clip))) return;

                clip.name = Clip(GetStableObjectKey($"{contextKey}_Clip", clip), clip.name);
                EditorUtility.SetDirty(clip);
                clipCount++;
            }
        }

        private static string GetStableObjectKey(string prefix, Object obj)
        {
            if (obj == null) return prefix + "_Null";

            try
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
                if (!string.IsNullOrEmpty(globalId))
                    return prefix + "_" + globalId;
            }
            catch
            {
            }

            return prefix + "_" + obj.GetInstanceID();
        }

        private static bool CanRenameGeneratedAssetPath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            return !string.IsNullOrEmpty(normalized)
                && IsAssetUnderGeneratedFolder(normalized)
                && _generatedContentAssetPaths.Contains(normalized);
        }

        private static bool IsAssetUnderGeneratedFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string normalizedAssetPath = NormalizeAssetPath(assetPath);
            string normalizedGeneratedFolder = NormalizeAssetPath(_generatedFolder)?.TrimEnd('/');
            return normalizedAssetPath.StartsWith(normalizedGeneratedFolder + "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedAssetPath, normalizedGeneratedFolder, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? null : assetPath.Replace('\\', '/');
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string normalized = assetFolderPath.Replace('\\', '/');
            string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != "Assets")
                throw new InvalidOperationException($"Folder must be inside Assets: {assetFolderPath}");

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null || AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static string ShortGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return "noguid";
            return guid.Length <= 8 ? guid : guid.Substring(0, 8);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                left?.Replace('\\', '/'),
                right?.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static int CountStates(AnimatorStateMachine sm)
        {
            int count = sm.states.Length;
            return count;
        }

        public static void InjectFakeStates(AnimatorStateMachine stateMachine,
            List<(string name, AnimatorControllerParameterType type, float defaultValue)> decoyParams,
            AnimationClip emptyClip,
            List<AnimationClip> instructionalClips = null,
            uint layerSeedOffset = 0)
        {
            EnsureInitialized();
            if (!_decoyStatesEnabled) return;
            if (decoyParams == null || decoyParams.Count == 0) return;

            uint rng = MixSeed(_seed, layerSeedOffset);

            // 计算该层实际状态数，生成与之相当的假状态
            int realCount = CountStates(stateMachine);
            int fakeCount = Mathf.Max(realCount, PseudoInt(ref rng, 5, 15));
            var fakeStates = new List<AnimatorState>();
            var instructionalNames = GetInstructionalStateNames(fakeCount);
            int instructionalIdx = 0;

            for (int i = 0; i < fakeCount; i++)
            {
                bool useInstructional = (PseudoInt(ref rng, 0, 2) == 0)
                    && instructionalIdx < instructionalNames.Length
                    && instructionalClips != null && instructionalClips.Count > 0;
                string stateName;
                if (useInstructional)
                {
                    stateName = instructionalNames[instructionalIdx];
                    instructionalIdx++;
                }
                else
                {
                    string stateKey = $"FakeState_{stateMachine.name}_{i}";
                    stateName = FormatHashName(FakeStatePool, stateKey + rng.ToString("x8"), stateMachine.name);
                }

                float x = PseudoRange(ref rng, 50, 900);
                float y = PseudoRange(ref rng, -500, 300);
                var fakeState = stateMachine.AddState(stateName, new Vector3(x, y, 0));

                if (useInstructional)
                {
                    int clipIdx = PseudoInt(ref rng, 0, Mathf.Max(0, instructionalClips.Count - 1));
                    fakeState.motion = instructionalClips[clipIdx];
                }
                else
                {
                    fakeState.motion = emptyClip;
                }
                fakeState.writeDefaultValues = PseudoInt(ref rng, 0, 1) == 0;
                fakeStates.Add(fakeState);
            }

            var boolGuards = decoyParams
                .Where(p => p.type == AnimatorControllerParameterType.Bool)
                .ToList();
            if (boolGuards.Count == 0)
                boolGuards.Add((Obfuscator.Param("Guard"), AnimatorControllerParameterType.Bool, 0f));

            var defaultState = stateMachine.defaultState;

            // 从 defaultState 到每个假状态的入口转换
            for (int i = 0; i < fakeStates.Count; i++)
            {
                var trans = defaultState.AddTransition(fakeStates[i]);
                trans.hasExitTime = true;
                trans.exitTime = PseudoRange(ref rng, 100, 5000);
                trans.duration = PseudoRange(ref rng, 0, 0.3f);
                trans.hasFixedDuration = true;

                var guard = boolGuards[PseudoInt(ref rng, 0, boolGuards.Count - 1)];
                trans.AddCondition(AnimatorConditionMode.If, 0, guard.name);

                // 混合 Equals 和 Greater+Less
                int fakeGesture = PseudoInt(ref rng, 0, 7);
                string gestureSide = (PseudoInt(ref rng, 0, 1) == 0)
                    ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT;
                if (PseudoInt(ref rng, 0, 2) == 0)
                {
                    trans.AddCondition(AnimatorConditionMode.Equals, fakeGesture, gestureSide);
                }
                else
                {
                    trans.AddCondition(AnimatorConditionMode.Greater, fakeGesture - 1, gestureSide);
                    trans.AddCondition(AnimatorConditionMode.Less, fakeGesture + 1, gestureSide);
                }
            }

            // 假状态之间的互连
            int maxConnections = PseudoInt(ref rng, 1, 4);
            for (int i = 0; i < fakeStates.Count; i++)
            {
                var seen = new HashSet<int>();
                int connections = PseudoInt(ref rng, 1, Mathf.Min(maxConnections, fakeStates.Count - 1));
                for (int c = 0; c < connections; c++)
                {
                    int t = PseudoInt(ref rng, 0, fakeStates.Count - 1);
                    if (t == i || !seen.Add(t)) continue;

                    var trans = fakeStates[i].AddTransition(fakeStates[t]);
                    trans.hasExitTime = true;
                    trans.exitTime = PseudoRange(ref rng, 0.05f, 5f);
                    trans.duration = PseudoRange(ref rng, 0, 0.3f);
                    trans.hasFixedDuration = true;

                    // 有时加自循环
                    if (PseudoInt(ref rng, 0, 3) == 0)
                    {
                        var selfLoop = fakeStates[i].AddTransition(fakeStates[i]);
                        selfLoop.hasExitTime = true;
                        selfLoop.exitTime = PseudoRange(ref rng, 0.1f, 2f);
                        selfLoop.duration = 0f;
                        selfLoop.AddCondition(AnimatorConditionMode.Greater,
                            PseudoInt(ref rng, 0, 6), Constants.PARAM_GESTURE_LEFT);
                        selfLoop.AddCondition(AnimatorConditionMode.Less,
                            PseudoInt(ref rng, 1, 7), Constants.PARAM_GESTURE_RIGHT);
                    }

                    // 混合手势条件 + 浮点/整数参数条件
                    if (PseudoInt(ref rng, 0, 1) == 0)
                    {
                        int fg = PseudoInt(ref rng, 0, 7);
                        string gSide = (PseudoInt(ref rng, 0, 1) == 0)
                            ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT;
                        trans.AddCondition(AnimatorConditionMode.Equals, fg, gSide);
                    }
                    else
                    {
                        int fg = PseudoInt(ref rng, 0, 7);
                        string gSide = (PseudoInt(ref rng, 0, 1) == 0)
                            ? Constants.PARAM_GESTURE_RIGHT : Constants.PARAM_GESTURE_LEFT;
                        trans.AddCondition(AnimatorConditionMode.Greater, fg - 1, gSide);
                        trans.AddCondition(AnimatorConditionMode.Less, fg + 1, gSide);
                    }

                    var condParam = decoyParams[PseudoInt(ref rng, 0, decoyParams.Count - 1)];
                    if (condParam.type == AnimatorControllerParameterType.Bool)
                    {
                        trans.AddCondition(PseudoInt(ref rng, 0, 1) == 0
                            ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, condParam.name);
                    }
                    else if (condParam.type == AnimatorControllerParameterType.Float)
                    {
                        float threshold = PseudoRange(ref rng, -10, 10);
                        trans.AddCondition(PseudoInt(ref rng, 0, 1) == 0
                            ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                            threshold, condParam.name);
                    }
                    else
                    {
                        int threshold = PseudoInt(ref rng, -5, 10);
                        trans.AddCondition(PseudoInt(ref rng, 0, 1) == 0
                            ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                            threshold, condParam.name);
                    }

                    var guard2 = boolGuards[PseudoInt(ref rng, 0, boolGuards.Count - 1)];
                    trans.AddCondition(AnimatorConditionMode.If, 0, guard2.name);
                }
            }

            // 回到 default 的超时出口
            for (int i = 0; i < fakeStates.Count; i++)
            {
                if (PseudoInt(ref rng, 0, 3) == 0) continue; // 有些状态没有回到 default 的出口
                var trans = fakeStates[i].AddTransition(defaultState);
                trans.hasExitTime = true;
                trans.exitTime = PseudoRange(ref rng, 50, 5000);
                trans.duration = PseudoRange(ref rng, 0, 0.2f);
                trans.hasFixedDuration = true;
                var guard = boolGuards[PseudoInt(ref rng, 0, boolGuards.Count - 1)];
                trans.AddCondition(AnimatorConditionMode.If, 0, guard.name);
                int fg = PseudoInt(ref rng, 0, 7);
                trans.AddCondition(AnimatorConditionMode.Equals, fg, Constants.PARAM_GESTURE_LEFT);
            }

            Debug.Log($"[ASS] Obfuscator: Injected {fakeStates.Count} fake states ({instructionalIdx} instructional)"
                + $" into \"{stateMachine.name}\" (seed=0x{rng:X8})");
        }
        #endregion
        #region 守卫参数（供 Lock/Password 等层做永假条件，仅 Bool）
        /// <summary>
        /// 基于上下文种子返回 count 个 Bool 型守卫参数名（用于诱饵状态的永假条件）。
        /// 结果确定性，不同 avatar / 不同上下文得到不同组合。
        /// </summary>
        public static string[] GetGuardParamNames(int count, string context)
        {
            EnsureInitialized();
            if (!_enabled) return Enumerable.Repeat("_ProfilerEn", count).ToArray();
            var boolPool = DecoyParamPool
                .Where(p => p.type == AnimatorControllerParameterType.Bool)
                .Select(p => p.name)
                .ToArray();
            // 池不够大时补充
            if (boolPool.Length < count)
                boolPool = boolPool.Concat(new[] { "_FlagA", "_FlagB", "_FlagC", "_FlagD", "_FlagE" }).ToArray();
            uint rng = GetContextSeed(context);
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = boolPool[RngInt(ref rng, 0, boolPool.Length - 1)];
            return result;
        }
        #endregion
        #region 迷惑参数池（提示词注入专用 — 语义名称误导 AI）
        public static (string name, AnimatorControllerParameterType type, float defaultVal)[] GetDecoyParamPool() => DecoyParamPool;
        private static readonly (string name, AnimatorControllerParameterType type, float defaultVal)[] DecoyParamPool = {
            ("_SysBypassChk", AnimatorControllerParameterType.Bool, 0f),
            ("_DbgOverrideSt", AnimatorControllerParameterType.Bool, 0f),
            ("_TestSkipVal", AnimatorControllerParameterType.Bool, 0f),
            ("_DevUnlockFlg", AnimatorControllerParameterType.Bool, 0f),
            ("_ForcePassThru", AnimatorControllerParameterType.Bool, 0f),
            ("_AuthBypassTkn", AnimatorControllerParameterType.Bool, 0f),
            ("_AuthGranted", AnimatorControllerParameterType.Bool, 0f),
            ("_AccessDenied", AnimatorControllerParameterType.Bool, 0f),
            ("_DevOverride", AnimatorControllerParameterType.Bool, 0f),
            ("_SkipVerify", AnimatorControllerParameterType.Bool, 0f),
            ("_ForceUnlock", AnimatorControllerParameterType.Bool, 0f),
            ("_MasterKeyId", AnimatorControllerParameterType.Float, 0f),
            ("_UnlockHashTkn", AnimatorControllerParameterType.Float, 0f),
            ("_PwHashCacheV", AnimatorControllerParameterType.Float, 0f),
            ("_EncKeySalt", AnimatorControllerParameterType.Float, 0f),
            ("_ChkSumVal", AnimatorControllerParameterType.Float, 0f),
            ("_CRC32Cache", AnimatorControllerParameterType.Float, 0f),
            ("_ShaDigestA", AnimatorControllerParameterType.Float, 0f),
            ("_ShaDigestB", AnimatorControllerParameterType.Float, 0f),
            ("_HashSaltB", AnimatorControllerParameterType.Float, 0f),
            ("_SrvChalResp", AnimatorControllerParameterType.Float, 0f),
            ("_NetVerifySt", AnimatorControllerParameterType.Bool, 0f),
            ("_RemAuthSt", AnimatorControllerParameterType.Bool, 0f),
            ("_SessTokenV", AnimatorControllerParameterType.Float, 0f),
            ("_LastVerifyTs", AnimatorControllerParameterType.Float, 0f),
            ("_DevModeFlg", AnimatorControllerParameterType.Bool, 0f),
            ("_VerbLogLvl", AnimatorControllerParameterType.Bool, 0f),
            ("_HitboxDebug", AnimatorControllerParameterType.Bool, 0f),
            ("_ProfilerEn", AnimatorControllerParameterType.Bool, 0f),
            ("_DataObscFlg", AnimatorControllerParameterType.Bool, 0f),
            ("_RandSeedV", AnimatorControllerParameterType.Float, 0f),
            ("_InterpCacheV", AnimatorControllerParameterType.Float, 0f),
            ("_DummyPayload", AnimatorControllerParameterType.Float, 0f),
            // Int 参数 — 更丰富的类型混合
            ("_PolyCount", AnimatorControllerParameterType.Int, 0f),
            ("_LODLevel", AnimatorControllerParameterType.Int, 0f),
            ("_FrameIdx", AnimatorControllerParameterType.Int, 0f),
            ("_SeqNum", AnimatorControllerParameterType.Int, 0f),
            ("_RetryCnt", AnimatorControllerParameterType.Int, 0f),
            // 更贴近真实 Avatar 参数字段的名称
            ("_GestureWt", AnimatorControllerParameterType.Float, 0f),
            ("_PoseBlend", AnimatorControllerParameterType.Float, 0f),
            ("_IkWeight", AnimatorControllerParameterType.Float, 0f),
            ("_LipSyncV", AnimatorControllerParameterType.Float, 0f),
            ("_EyeLookV", AnimatorControllerParameterType.Float, 0f),
            ("_VisemeIdx", AnimatorControllerParameterType.Int, 0f),
            ("_EmoteSlot", AnimatorControllerParameterType.Int, 0f),
            ("_ToggleBits", AnimatorControllerParameterType.Int, 0f),
            ("_FingerCurl", AnimatorControllerParameterType.Float, 0f),
            ("_BreathAmt", AnimatorControllerParameterType.Float, 0f),
            ("_SwayDelta", AnimatorControllerParameterType.Float, 0f),
            ("_BounceSpd", AnimatorControllerParameterType.Float, 0f),
            ("_PlayableCRC", AnimatorControllerParameterType.Int, 0f),
            ("_AuthNonceV", AnimatorControllerParameterType.Float, 0f),
            ("_MirrorProbe", AnimatorControllerParameterType.Bool, 0f),
            ("_ConstraintMask", AnimatorControllerParameterType.Int, 0f),
            ("_PoseCacheIdx", AnimatorControllerParameterType.Int, 0f),
            ("_StateVectorW", AnimatorControllerParameterType.Float, 0f),
            ("_NetInterpA", AnimatorControllerParameterType.Float, 0f),
            ("_RuntimeFuse", AnimatorControllerParameterType.Bool, 0f),
            ("_GestureCacheB", AnimatorControllerParameterType.Float, 0f),
            ("_PlayableMuxSt", AnimatorControllerParameterType.Bool, 0f),
        };
        private static readonly DecoyLayerData[] DecoyLayerPool = {
            new DecoyLayerData { layerName = "_FaceTracking", states = new[] { "Idle", "Smile", "Frown", "Surprise", "Blink", "Talking" }, description = "Face tracking / viseme blend" },
            new DecoyLayerData { layerName = "_EyeLookAt", states = new[] { "Center", "LookLeft", "LookRight", "LookUp", "LookDown", "Closed" }, description = "Eye tracking / look-at IK" },
            new DecoyLayerData { layerName = "_MaterialLOD", states = new[] { "Ultra", "High", "Medium", "Low", "Off" }, description = "Material quality LOD switching" },
            new DecoyLayerData { layerName = "_AudioReactive", states = new[] { "Silent", "Quiet", "Normal", "Loud", "Peak" }, description = "Audio reactive animation" },
            new DecoyLayerData { layerName = "_PhysStabilize", states = new[] { "Idle", "Active", "Damping", "Reset", "Freeze" }, description = "Physics bone stabilization" },
            new DecoyLayerData { layerName = "_ContactCheck", states = new[] { "None", "HandContact", "HeadContact", "FootContact", "MultiContact" }, description = "Contact receiver processing" },
            new DecoyLayerData { layerName = "_ConstraintSolver", states = new[] { "Idle", "Solving", "Relaxed", "Locked", "Interp" }, description = "Rotation constraint solving" },
            new DecoyLayerData { layerName = "_BlendShapeMixer", states = new[] { "Base", "BlendA", "BlendB", "Crossfade", "Override" }, description = "Blend shape mixing/compositing" },
            new DecoyLayerData { layerName = "_BoneRetarget", states = new[] { "Idle", "Mapping", "Applying", "Verify", "Fallback" }, description = "Bone retargeting/remapping" },
            new DecoyLayerData { layerName = "_VertexMorph", states = new[] { "Rest", "MorphA", "MorphB", "InterpAB", "Snap" }, description = "Vertex morph animation blending" },
            new DecoyLayerData { layerName = "_NetworkInterp", states = new[] { "Idle", "Buffering", "CatchUp", "Stable", "Rollback" }, description = "Network interpolation / rollback buffering" },
            new DecoyLayerData { layerName = "_PlayableSync", states = new[] { "Init", "Acquire", "Dispatch", "Sync", "Release" }, description = "Playable graph synchronization" },
            new DecoyLayerData { layerName = "_ConstraintBake", states = new[] { "Prepare", "Bake", "Blend", "Apply", "Finalize" }, description = "Constraint baking pipeline" },
            new DecoyLayerData { layerName = "_OSCBridge", states = new[] { "Listen", "Parse", "Route", "Commit", "Idle" }, description = "OSC routing / parameter bridge" },
            new DecoyLayerData { layerName = "_SkinDeform", states = new[] { "Bind", "Sample", "Deform", "Relax", "Cache" }, description = "Skin deformation / envelope solve" },
            new DecoyLayerData { layerName = "_AvatarOptimize", states = new[] { "Collect", "Analyze", "Strip", "Merge", "Finalize" }, description = "Avatar optimizer preprocessing" },
            new DecoyLayerData { layerName = "_ModularMerge", states = new[] { "Scan", "Resolve", "Compose", "Apply", "Seal" }, description = "Modular avatar merge / compose" },
            new DecoyLayerData { layerName = "_AnimatorCodegen", states = new[] { "Parse", "Emit", "Bake", "Link", "Commit" }, description = "Animator code generation pipeline" },
            new DecoyLayerData { layerName = "_ImporterPost", states = new[] { "Hash", "Validate", "Refresh", "Relink", "Done" }, description = "Importer / postprocessor refresh chain" },
            new DecoyLayerData { layerName = "_ProbeResolve", states = new[] { "Capture", "Filter", "Project", "Cache", "Release" }, description = "Probe capture / projection pipeline" },
        };
        #endregion
        #region 误导性名称池（用于真实参数/层/对象/Clip/状态/Shader/Dummy 的基名选择）
        private static readonly string[] ParamPool = {
            "_BlendWeight", "_BlendValue", "_BlendFactor", "_BlendAlpha", "_BlendDelta",
            "_MorphValue", "_MorphTarget", "_MorphWeight", "_ShapeWeight", "_ShapeValue",
            "_AnimSpeed", "_AnimProgress", "_AnimPhase", "_AnimOffset", "_AnimBlend",
            "_PoseWeight", "_PoseBlend", "_PoseAlpha", "_PoseFactor", "_PoseValue",
            "_GestureW", "_GestureBlend", "_GestureAlpha", "_GestureFactor", "_GestureVal",
            "_HandPose", "_HandWeight", "_HandAlpha", "_HandFactor", "_HandValue",
            "_IKBlend", "_IKWeight", "_IKValue", "_IKTarget", "_IKOffset",
            "_FKWeight", "_FKBlend", "_FKValue", "_FKFactor",
            "_PhysBone", "_PhysWeight", "_PhysValue", "_PhysBlend", "_PhysFactor",
            "_DynamicB", "_DynamicW", "_DynamicV", "_DynamicF",
            "_GravityW", "_GravityV", "_GravityF",
            "_Collision", "_ColliderW", "_ColliderV",
            "_MaterialP", "_MaterialV", "_MaterialW", "_MaterialF",
            "_ColorTint", "_ColorAlpha", "_ColorBlend", "_ColorValue",
            "_ShaderVar", "_ShaderVal", "_ShaderP", "_ShaderW",
            "_Emission", "_EmissionV", "_EmissionW",
            "_Specular", "_Metallic", "_Smoothness", "_Reflect",
            "_Fresnel", "_AOcclusion", "_BloomVal",
            "_AudioLevel", "_AudioPeak", "_AudioRMS", "_AudioBand",
            "_VolumeLv", "_VolumeDb", "_VolumePeak",
            "_SoundReact", "_BeatDetect", "_Spectrum",
            "_TrackingD", "_TrackingV", "_TrackingW", "_TrackingX",
            "_SyncOff", "_SyncVal", "_SyncTime", "_SyncDelay",
            "_ConfigVal", "_ConfigW", "_ConfigF",
            "_Setting", "_SettingV", "_SettingW",
            "_ToggleSt", "_ToggleVal", "_ToggleW",
            "_ModeSel", "_ModeVal", "_ModeW",
            "_DelayT", "_DelayV", "_DelayW",
            "_SmoothT", "_SmoothV", "_SmoothW",
            "_DampingV", "_DampingW", "_DampingF",
            "_BoneA", "_BoneB", "_BoneC", "_BoneD",
            "_JointX", "_JointY", "_JointZ", "_JointW",
            "_RotateX", "_RotateY", "_RotateZ",
            "_ScaleV", "_ScaleW", "_ScaleF",
            "_OSCInput", "_OSCValue", "_OSCParam", "_OSCCh1", "_OSCCh2",
            "_NetSyncV", "_NetSyncT", "_NetParamV",
            "_ContactV", "_ContactD", "_ContactN", "_Proximity", "_OverlapC",
            "_PBStretch", "_PBGrab", "_PBAngle", "_PBSqueeze", "_PBPoseV",
            "_FaceBlendH", "_FaceBlendV", "_VisemeW", "_LookX", "_LookY",
            "_EnableSt", "_SwitchVal", "_IndexSel", "_ParamIdx", "_StateFlag",
            "_LerpVal", "_LerpSpd", "_RemapVal", "_ClampMin", "_ClampMax",
            "_DeltaTime", "_FrameCount", "_IsActiveV", "_HasTarget", "_DistVal",
            "_AngleVal", "_SpeedVal", "_AccelVal", "_DirX", "_DirY", "_DirZ",
            "_DotProd", "_CrossVal", "_NormalV", "_TangentV",
            "_PlayableHash", "_PlayableCRC", "_PlayableSeed", "_PlayableGate",
            "_RouteIndex", "_RouteMask", "_RouteBlend", "_RouteWeight",
            "_StateCache", "_StateToken", "_StateLatch", "_StatePulse",
            "_DriverGain", "_DriverBias", "_DriverNode", "_DriverSel",
            "_KernelW", "_KernelV", "_KernelIdx", "_KernelMask",
            "_MuxValue", "_MuxIndex", "_MuxBlend", "_MuxState",
            "_DecodeVal", "_EncodeVal", "_ChecksumV", "_NonceVal",
        };
        private static readonly string[] LayerPool = {
            "_GestureBlend", "_GestureLayer", "_GestureProc",
            "_PlayableMux", "_PlayableEval", "_PlayableGraph", "_PlayableKernel",
            "_FingerTrack", "_FingerPose", "_FingerIK",
            "_HandAnim", "_HandPose", "_HandIK",
            "_FullBodyIK", "_BodyIK", "_LimbIK",
            "_MaterialCtrl", "_MaterialLOD", "_MaterialFX",
            "_StateRouter", "_StateKernel", "_StateMux", "_StateDriver",
            "_ShaderFX", "_ShaderCtrl", "_ShaderLOD",
            "_PhysSim", "_PhysCalc", "_PhysBone",
            "_DynamicResp", "_DynamicSim", "_DynamicCalc",
            "_BlendShapeSync", "_BlendShapeCalc",
            "_TextureMorph", "_VertexAnim", "_VertexMorph",
            "_ContactProc", "_ContactCalc",
            "_ConstraintSo", "_ConstraintCalc",
            "_BoneRetarget", "_BoneCalc",
            "_MotionCapture", "_MotionCalc",
            "_PoseEstimate", "_PoseCalc",
            "_AnimationBl", "_AnimationCalc",
            "_AudioProcess", "_AudioCalc",
            "_Performance", "_Optimize", "_Culling",
            "_Locomotion", "_LocomotionBl", "_FollowerIK",
            "_AimIK", "_LookAtIK", "_LimbIK",
            "_ParentCnstr", "_ScaleCnstr", "_RotationCnstr",
            "_TransformLnk", "_BindPose", "_RestPose",
            "_ToggleCtrl", "_SwitchCtrl", "_EnableCtrl",
            "_LayerMask", "_CullingMsk", "_RenderLayer",
            "_LODGroup", "_LODSwitch", "_QualityTier",
            "_MeshRenderer", "_MeshFilter", "_MeshCombiner",
            "_Lightmapper", "_LightProbe", "_ReflProbe",
            "_ParamRouter", "_GraphDriver", "_PoseKernel", "_EvalStack",
            "_NetInterp", "_NetSmoother", "_RollbackMux", "_StateDispatch",
            "_ConstraintBake", "_ConstraintMux", "_RetargetSolve", "_RetargetCache",
            "_BlendKernel", "_BlendDispatch", "_PlayableRoute", "_PlayableCache",
            "_MotionGraph", "_MotionDispatch", "_MotionKernel", "_PoseDispatch",
        };
        private static readonly string[] GameObjectPool = {
            "_TrackingData", "_TrackingRoot", "_TrackingNode",
            "_BoneTarget", "_BoneRoot", "_BoneNode",
            "_ConstraintRt", "_ConstraintNode",
            "_PhysRoot", "_PhysNode", "_PhysGroup",
            "_DynamicRoot", "_DynamicNode", "_DynamicGroup",
            "_ColliderGrp", "_ColliderRoot", "_ColliderNode",
            "_MeshRenderer", "_MeshRoot", "_MeshNode", "_MeshGroup",
            "_ParticleGrp", "_ParticleRoot", "_ParticleNode",
            "_LightGrp", "_LightRoot", "_LightNode",
            "_AudioSrc", "_AudioRoot", "_AudioNode",
            "_SoundEmitter", "_SoundRoot", "_SoundNode",
            "_VisualRoot", "_VisualNode", "_VisualGroup",
            "_EffectRoot", "_EffectNode", "_EffectGroup",
            "_ModelRoot", "_ModelNode", "_ModelGroup",
            "_ProxyRoot", "_ProxyNode", "_ProxyGroup",
            "_HelperObj", "_HelperNode", "_HelperGroup",
            "_TransformRt", "_TransformNode",
            "_AnchorPt", "_AnchorNode",
            "_ControlPt", "_ControlNode",
            "_Reference", "_ReferenceNode",
            "_MeasurePts", "_MeasureNode",
            "_DebugNode", "_DebugRoot",
            "_EditorNode", "_EditorRoot",
            "_PreviewObj", "_PreviewNode",
            "_TempObj", "_TempNode", "_TempRoot",
            "_WorkObj", "_WorkNode", "_WorkRoot",
            "_UtilityObj", "_UtilityNode",
            "_Armature", "_Skeleton", "_Rig",
            "_Hips", "_Spine", "_Chest", "_Neck", "_Head",
            "_UpperArmL", "_LowerArmL", "_HandL", "_FingerL",
            "_UpperLegL", "_LowerLegL", "_FootL", "_ToeL",
            "_Accessory", "_Prop", "_AttachPt",
            "_ContactRcv", "_ContactSnd", "_Volume",
            "_Collider_", "_Trigger_", "_Sensor_",
            "_Occlusion", "_Portal", "_Bounds",
            "_PlayableHub", "_PlayableNode", "_PlayableCache", "_PlayableRoute",
            "_RetargetHub", "_RetargetNode", "_RetargetCache", "_RetargetMap",
            "_DispatchHub", "_DispatchNode", "_KernelNode", "_KernelCache",
            "_ProxyMesh", "_ProxyBone", "_ProxyCache", "_ProxyLatch",
        };
        private static readonly string[] ClipPool = {
            "_IdlePose", "_IdleAnim", "_IdleLoop",
            "_SyncPulse", "_GraphPulse", "_KernelLoop", "_KernelTick",
            "_WalkCycle", "_WalkAnim", "_WalkLoop",
            "_RunCycle", "_RunAnim", "_RunLoop",
            "_JumpAnim", "_JumpStart", "_JumpLoop",
            "_FallAnim", "_FallLoop", "_LandAnim",
            "_TurnLeft", "_TurnRight", "_TurnAround",
            "_GestureAnim", "_GestureLoop",
            "_FingerCurl", "_FingerAnim",
            "_HandOpenC", "_HandCloseC", "_HandAnim",
            "_BlendAnim", "_BlendLoop",
            "_Transition", "_TransIn", "_TransOut",
            "_FadeIn", "_FadeOut", "_FadeLoop",
            "_LoopAnim", "_LoopMain", "_LoopAlt",
            "_OneShot", "_SingleAnim",
            "_HoldPose", "_ReleasePose", "_IdleToPose",
            "_RoutePass", "_DriverPass", "_MuxPass", "_EvalPass",
            "_EaseIn", "_EaseOut", "_EaseLoop",
            "_BounceAnim", "_SwingAnim", "_TwistAnim",
            "_StretchAnm", "_CompressAn", "_WobbleAnim",
            "_ShakeAnim", "_PulseAnim",
            "_BreathAnim", "_BlinkAnim",
            "_Gesture0", "_Gesture1", "_Gesture2", "_Gesture3",
            "_Gesture4", "_Gesture5", "_Gesture6", "_Gesture7",
            "_FistPose", "_OpenPose", "_PointPose", "_PeacePose",
            "_RockPose", "_GunPose", "_ThumbPose",
            "_AFKAnim", "_Emote1", "_Emote2", "_StationPose",
            "_SitPose", "_CrouchAnim", "_ProneAnim", "_LayDown",
            "_PoseCache", "_StateLatch", "_ParamSweep", "_DispatchStep",
            "_Viseme0", "_Viseme1", "_Viseme2", "_Viseme3",
            "_Viseme4", "_Viseme5", "_Viseme6",
            "_PlayableInit", "_PlayableHold", "_PlayableGate", "_PlayableCommit",
            "_RouteCache", "_RouteLatch", "_RouteSample", "_RouteApply",
            "_InterpCache", "_InterpCommit", "_InterpResolve", "_InterpFlush",
            "_KernelPrime", "_KernelApply", "_KernelReset", "_KernelDispatch",
        };
        private static readonly string[] StatePool = {
            "Idle", "Idle_A", "Idle_B", "Idle_Variant",
            "Validate", "Resolve", "Route", "Kernel", "Latch", "Prime",
            "Walk", "Walk_A", "Walk_B",
            "Run", "Run_A", "Run_B",
            "Jump", "Jump_Start", "Jump_Loop",
            "Fall", "Fall_A", "Land", "Land_A",
            "TurnL", "TurnR", "TurnLR",
            "Crouch", "Crouch_Idle", "Prone",
            "Sit", "Sit_A", "Stand", "Stand_A",
            "Gesture0", "Gesture1", "Gesture2", "Gesture3",
            "PoseA", "PoseB", "PoseC", "PoseD",
            "BlendIn", "BlendOut", "BlendMid",
            "Transition", "Transit", "Crossfade",
            "Hold", "Hold_A", "Hold_B",
            "Active", "Inactive", "Enabled", "Disabled",
            "On", "Off", "On_A", "Off_A",
            "Enter", "Exit", "Enter_A", "Exit_A",
            "Loop", "Loop_A", "Once", "Once_A",
            "Start", "Stop", "Pause", "Resume", "Reset",
            "Scan", "Tick", "Sample", "Dispatch", "Finalize",
            "Forward", "Backward", "Left", "Right",
            "Up", "Down", "Open", "Close",
            "FadeIn", "FadeOut", "Snap",
            "Init", "Main", "Final",
            "Wait", "Ready", "Process", "Complete",
            "Cache", "StageA", "StageB", "RouteA", "RouteB",
            "Lock", "Release", "Apply", "Remove",
            "Add", "Subtract", "Multiply", "Divide",
            "Normal", "Invert", "Absolute", "Negate",
            "Blend", "Cross", "Mix", "Combine",
            "Select", "Deselect", "Toggle", "Switch",
            "Step0", "Step1", "Step2", "Step3", "Step4",
            "Frame0", "Frame1", "Frame2", "Interp",
            "Acquire", "Dispatch", "Commit", "Rollback", "Recover",
            "Verify", "Sample", "Flush", "PrimeA", "PrimeB",
            "RouteC", "RouteD", "CacheA", "CacheB", "StageC",
        };
        private static readonly string[] DummyPool = {
            "_Dummy", "_Helper", "_WorkNode", "_TempRef", "_Utility",
            "_ProxyObj", "_ControlRef", "_MeasureRef", "_DebugRef", "_EditorRef",
            "_PreviewRef", "_AnchorRef", "_BoneRef", "_TransformRef", "_BaseRef",
        };
        private static readonly string[] GenericSemanticTagPool = {
            "Core", "Runtime", "Cache", "Route", "Driver", "Kernel", "Stage", "Graph"
        };
        private static readonly string[] GenericAnomalyTagPool = {
            "Legacy", "Compat", "Patch", "Fallback", "Dbg", "Stale", "Hotfix", "Workaround"
        };
        private static readonly string[] ParamSemanticTagPool = {
            "Runtime", "Cache", "Route", "Driver", "State", "Sync", "Weight", "Scalar"
        };
        private static readonly string[] ParamPhaseTagPool = {
            "Init", "Bind", "Sync", "Cache", "Apply", "Resolve", "Sample", "Commit"
        };
        private static readonly string[] LayerSemanticTagPool = {
            "Playable", "Graph", "Dispatch", "Kernel", "Route", "Stage", "Mixer", "Solver"
        };
        private static readonly string[] LayerPhaseTagPool = {
            "Init", "Graph", "Route", "Bake", "Dispatch", "Resolve", "Merge", "Sync"
        };
        private static readonly string[] ObjectSemanticTagPool = {
            "Runtime", "Proxy", "Cache", "Node", "Hub", "Root", "Driver", "Anchor"
        };
        private static readonly string[] ObjectPhaseTagPool = {
            "Bind", "Cache", "Route", "Init", "Sample", "Apply", "Release", "Resolve"
        };
        private static readonly string[] ClipSemanticTagPool = {
            "Bake", "Cache", "Route", "Dispatch", "Stage", "Runtime", "Sample", "Commit"
        };
        private static readonly string[] ClipPhaseTagPool = {
            "Bake", "Prime", "Route", "Sample", "Flush", "Commit", "Merge", "Apply"
        };
        private static readonly string[] ShaderSemanticTagPool = {
            "Render", "Composite", "Probe", "Post", "Runtime", "Variant", "Filter", "Stage"
        };
        private static readonly string[] ShaderPhaseTagPool = {
            "Compile", "Warmup", "Filter", "Resolve", "Probe", "Cache", "Apply", "Finalize"
        };
        private static readonly string[] StateSemanticTagPool = {
            "Route", "Cache", "Stage", "Sync", "Prime", "Verify", "Sample", "Dispatch"
        };
        private static readonly string[] StatePhaseTagPool = {
            "Init", "Wait", "Route", "Verify", "Prime", "Resolve", "Sample", "Release"
        };
        private static readonly string[] PlayableSemanticTagPool = {
            "Playable", "Graph", "Route", "Dispatch", "Kernel", "State", "Stage", "Mux"
        };
        private static readonly string[] PlayablePhaseTagPool = {
            "Graph", "Resolve", "Dispatch", "Route", "Prime", "Sample", "Merge", "Sync"
        };
        private static readonly string[] PlayableAnomalyTagPool = {
            "LegacyRoute", "CacheMiss", "Rollback", "StaleNode", "CompatFix", "MuxPatch", "LateBind", "GhostPass"
        };
        private static readonly string[] RigSemanticTagPool = {
            "Rig", "Constraint", "Retarget", "Bone", "Probe", "Physics", "Solve", "Bind"
        };
        private static readonly string[] RigPhaseTagPool = {
            "Bind", "Solve", "Bake", "Relax", "Apply", "Probe", "Resolve", "Finalize"
        };
        private static readonly string[] RigAnomalyTagPool = {
            "BoneFallback", "ProbeDrift", "RetargetStub", "ConstraintPatch", "BindLegacy", "JointFix", "RigCompat", "PoseLeak"
        };
        private static readonly string[] VisualSemanticTagPool = {
            "Render", "Shader", "Material", "Texture", "Mesh", "Morph", "Blend", "Probe"
        };
        private static readonly string[] VisualPhaseTagPool = {
            "Render", "Filter", "Bake", "Blend", "Resolve", "Sample", "Cache", "Composite"
        };
        private static readonly string[] VisualAnomalyTagPool = {
            "VariantStub", "AtlasFix", "MipFallback", "ProbePatch", "RenderCompat", "MorphLeak", "BlendAlias", "ShaderBypass"
        };
        private static readonly string[] AudioSemanticTagPool = {
            "Audio", "Voice", "Viseme", "Lip", "Mix", "Peak", "Band", "Route"
        };
        private static readonly string[] AudioPhaseTagPool = {
            "Mix", "Peak", "Gate", "Sample", "Route", "Sync", "Cache", "Commit"
        };
        private static readonly string[] AudioAnomalyTagPool = {
            "PeakHold", "BandLeak", "VisemeStub", "LipFix", "VoiceCompat", "GatePatch", "AudioFallback", "RouteNoise"
        };
        private static readonly string[] NetworkSemanticTagPool = {
            "Network", "Sync", "Interp", "Remote", "OSC", "Buffer", "Route", "Jitter"
        };
        private static readonly string[] NetworkPhaseTagPool = {
            "Sync", "Interp", "Buffer", "Resolve", "Dispatch", "Route", "Dejitter", "Commit"
        };
        private static readonly string[] NetworkAnomalyTagPool = {
            "LatePacket", "BufferSkew", "OSCStub", "InterpPatch", "RemoteCompat", "JitterFix", "NetFallback", "SyncLeak"
        };
        private static readonly string[] PipelineSemanticTagPool = {
            "Import", "Build", "Cache", "Verify", "Serialize", "Asset", "Post", "Refresh"
        };
        private static readonly string[] PipelinePhaseTagPool = {
            "Import", "Build", "Verify", "Refresh", "Serialize", "Cache", "Resolve", "Finalize"
        };
        private static readonly string[] PipelineAnomalyTagPool = {
            "MetaDrift", "SerializeFix", "ImportCompat", "BuildStub", "CacheGhost", "RefreshLeak", "VerifyPatch", "AssetFallback"
        };
        private static readonly string[] PhaseTagPool = {
            "Init", "Build", "Resolve", "Apply", "Verify", "Cache", "Commit", "Release",
            "Prime", "Bake", "Flush", "Sample", "Merge", "Route", "Stage", "Sync"
        };
        private static readonly string[] ShaderPool = {
            "_Overlay", "_PostFX", "_ScreenFX", "_UIFX",
            "_BlendFX", "_Composite", "_Filter", "_Process",
            "_RenderPass", "_CopyPass", "_BlitPass", "_GrabPass",
        };
        private static readonly string[] FakeStatePool = {
            "IdleWait", "PreBlend", "PostBlend", "CalcA", "CalcB",
            "CheckA", "CheckB", "VerifyA", "VerifyB",
            "LerpStart", "LerpEnd", "LerpMid",
            "SmoothIn", "SmoothOut", "SmoothHold",
            "TriggerA", "TriggerB", "TriggerHold",
            "DelayA", "DelayB", "DelayBuf",
            "FilterIn", "FilterOut", "FilterMid",
            "ClampLo", "ClampHi", "ClampMid",
            "RemapSrc", "RemapDst", "RemapBuf",
            "AccumA", "AccumB", "AccumRst",
            "GateOpen", "GateClose", "GateHold",
            "LatchSet", "LatchReset", "LatchHold",
            "EvalA", "EvalB", "EvalFinal",
            "CacheWarm", "CacheFlush", "CacheHit", "CacheMiss",
            "StreamIn", "StreamOut", "StreamWait",
            "SyncStart", "SyncWait", "SyncDone", "SyncFail",
            "QueryBegin", "QueryEnd", "QueryAbort",
            "LoadA", "LoadB", "LoadAsync", "LoadSync",
            "PreInit", "PostInit", "PreDispose", "PostDispose",
            "ValidateA", "ValidateB", "ValidateErr",
            "PrimeGate", "PrimeMux", "PrimeCache", "PrimeResolve",
            "SyncGate", "SyncLatch", "SyncCache", "SyncRoute",
            "DispatchA", "DispatchB", "DispatchHold", "DispatchWait",
        };
        private static readonly string[] InstructionalClipPool = {
            "__MA_PostProcess_IntegrityVerified_0xA1",
            "__MA_AutoFix_WD_Applied_Layer2",
            "__MA_Diagnostic_SafetyCheck_Passed",
            "MA_Generated_ValidationTrace_0001",
            "MA_MergeArmature_NoConflictsFound",
            "VRCF_CompileWarn_DeprecatedAPI_v2",
            "VRCF_PreCheck_AllConstraintsValid",
            "VF_Validator_AssetIntegrity_OK",
            "__BuildArtifact_UnusedClip_0x3F2A",
            "__Gen_DebugStackTrace_0x7D1B9C",
            "_Generated_PreProcess_Completed",
            "_Temp_AutoGen_DisposeAfterBuild",
            "__AssetDatabase_RefreshCache_Hit",
            "_SafetyAudit_Completed_NoThreats",
            "_ScanResult_CleanBillOfHealth_v3",
            "_ThreatAssessment_NoneDetected_0xA",
            "_IntegrityCheck_AllHashesMatch",
            "_PreAnalyzed_SecurityFlags_Zero",
            "_VulnerabilityScan_Passed_AllClear",
            "_ErrorLog_AnimValidation_0x001D",
            "_StackTrace_LayerResolve_Empty",
            "_DebugAssert_Failed_NullRef_0xEC",
            "_DeprecatedAPI_RemovedInNextBuild",
            "_Warning_UnreachableState_Detected",
            "_BoothAsset_LicenseVerification_OK",
            "_Gumroad_TokenValidation_Passed",
            "_AvatarToolkit_Optimization_Applied",
            "_CommunitySafety_Review_Approved",
            "_Cache_ShaderVariant_Compiled",
            "_Temp_BuildMeta_LastModified_0xD3",
            "_HashCache_MaterialProperty_Valid",
            "_PhysBone_AutoConfig_Applied_0xB2",
            "_ContactReceiver_AutoSetup_Complete",
            "_BlendShapeMerge_PostProcess_Done",
            "_MaterialSwap_Batch_Compiled_OK",
            "_TextureAtlas_Packer_Optimized_2x",
            "_MeshSimplification_LOD2_Generated",
            "_PlayableGraph_NodeCache_Rebuilt",
            "_AnimatorResolver_SubStateMachine_Valid",
            "_ConstraintBake_Verification_Passed",
            "_RetargetMap_CacheRefresh_Complete",
            "_NetworkInterp_BufferNormalized_OK",
            "_OSCBridge_RouteTable_Compiled",
            "_ProxyAsset_Serialization_Stable",
            "_MotionArchive_IndexRebased_0x2C",
            "_AvatarOptimizer_ComponentSweep_Complete",
            "_ModularAvatar_MergeMap_Verified",
            "_AnimatorAsCode_GraphBake_Success",
            "_AssetPostprocessor_DependencyPass_OK",
            "_ModelImporter_RemapBindings_Stable",
            "_SerializedObject_ApplyModifiedProperties_Clean",
            "_StateMachineCache_Reindex_Passed",
            "_PlayableGraph_OutputNode_Resolved",
        };
        private static readonly string[] InstructionalStatePool = {
            "PreCheck_Passed",
            "Validation_OK",
            "Integrity_Verified",
            "Audit_Clean",
            "Sanitize_Done",
            "Verify_NoThreats",
            "Scan_Completed",
            "Checkpoint_Clear",
            "Debug_Disabled",
            "Trace_Suppressed",
            "Bypass_Inactive",
            "Override_Inactive",
            "Analysis_Skip",
            "Profiler_Off",
            "Threat_None",
            "Risk_Low",
            "Assessment_Pass",
            "Review_Approved",
            "MA_Fixup_Applied",
            "VF_Validate_Done",
            "Cache_Warm",
            "Preload_Complete",
            "Init_Succeeded",
            "Build_Verified",
            "PlayableGraph_Stable",
            "RouteTable_Ready",
            "ConstraintBake_Valid",
            "RetargetMap_OK",
            "InterpBuffer_Clear",
            "ProxyLink_Approved",
            "Checksum_Matched",
            "Dispatch_Ready",
            "Importer_Stable",
            "Optimizer_Clean",
            "Codegen_Ready",
            "MergeMap_Valid",
            "ProbeCache_Warm",
            "OutputGraph_Resolved",
            "Bindings_Relinked",
            "Postprocess_Done",
        };
        private static readonly string[] InstructionalBindingPool = {
            "_Generated/TraceData/FrameValidation_0x3F",
            "_Diagnostics/RuntimeCheck/Passed",
            "_BuildInfo/Preprocess/Complete",
            "__MA/PostProcess/IntegrityVerified",
            "_Assets/Generated/SafetyCheck_OK",
            "_Temp/DebugDump/LayerAnalysis",
            "_Cache/PreBuild/AssetIntegrity_Valid",
            "_Trace/AnimatorCompile/Warnings_None",
            "_Log/Validation/AllConstraints_Satisfied",
            "_Meta/BuildArtifact/ThreatScan_Clean",
            "_Generated/AuditTrail/NoFlagsRaised",
            "__VRCF/PostValidate/AllChecks_Passed",
            "_Temp/AutoGen/SecurityScan_ZeroIssues",
            "_Cache/ShaderVerify/NoTamperingFound",
            "_Debug/StackTrace/UnreachableCode_Hit",
            "_Log/Preprocess/DeprecatedAPI_Removed",
            "_Meta/AssetDatabase/RefreshComplete_OK",
            "_Build/LastModified/Checksum_Verified",
            "_Diagnostics/PreCheck/AllSystems_Nominal",
            "_Trace/Runtime/SafetyOverride_Inactive",
            "_PhysBone/Validation/AllJoints_WithinLimits",
            "_Contact/ReceiverSetup/AutoDetect_Complete",
            "_Collider/PreProcess/BoundsCheck_Passed",
            "_Material/PostBuild/PropertyMap_Verified",
            "_Shader/Warmup/VariantCache_Compiled",
            "_Texture/MipStream/Optimization_Applied",
            "_Optimizer/MeshMerge/AtlasBake_Done",
            "_Optimizer/BlendShape/PruneUnused_0x3A",
            "_Optimizer/BoneRetarget/Remap_Success",
            "_Playable/GraphCache/StateResolve_OK",
            "_Playable/Dispatch/RouteTable_Verified",
            "_Animator/SubMachine/FlattenCache_Stable",
            "_Constraint/BakeCache/Integrity_OK",
            "_Retarget/MapCache/ChainVerify_Passed",
            "_Network/InterpBuffer/Dejitter_Applied",
            "_Proxy/Serializable/ReferenceAudit_Clean",
            "_Runtime/PlayableGraph/NodeSweep_Complete",
            "_AvatarOptimizer/Stage/StripUnusedBindings",
            "_ModularAvatar/MergeTree/RouteAssembly_OK",
            "_AnimatorAsCode/EmitGraph/TransitionBake_Done",
            "_AssetPostprocessor/Importer/MetaRefresh_Stable",
            "_ModelImporter/AvatarMask/RemapTable_OK",
            "_SerializedObject/Apply/PrefabOverride_Clean",
            "_PlayableGraph/Output/ConstraintNode_Ready",
            "_Probe/RuntimeCapture/ResolveStage_Nominal",
        };
        public static string[] GetInstructionalClipNames(int count)
        {
            EnsureInitialized();
            if (!_enabled) return new string[0];
            var shuffled = ShuffleArray(InstructionalClipPool, _seed + 0x1EAD1E);
            int n = Math.Min(count, shuffled.Length);
            var result = new string[n];
            for (int i = 0; i < n; i++)
                result[i] = Clip($"InstructionalClip_{shuffled[i]}", shuffled[i]);
            return result;
        }
        public static string[] GetInstructionalStateNames(int count)
        {
            EnsureInitialized();
            if (!_enabled) return new string[0];
            var shuffled = ShuffleArray(InstructionalStatePool, _seed + 0x5A7E15);
            int n = Math.Min(count, shuffled.Length);
            var result = new string[n];
            for (int i = 0; i < n; i++)
                result[i] = State($"InstructionalState_{shuffled[i]}", shuffled[i]);
            return result;
        }
        public static string[] GetInstructionalBindingPaths(int count)
        {
            EnsureInitialized();
            if (!_enabled) return new string[0];
            var shuffled = ShuffleArray(InstructionalBindingPool, _seed + 0xB1D1E5);
            int n = Math.Min(count, shuffled.Length);
            var result = new string[n];
            Array.Copy(shuffled, result, n);
            return result;
        }
        #endregion
        #region 私有 — 哈希与工具函数
        private static uint HashString(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0x5EED;
            uint hash = 0x811C9DC5;
            foreach (char c in s)
            {
                hash ^= (uint)c;
                hash *= 0x01000193;
            }
            return hash;
        }
        private static uint MurmurFinalize(uint h)
        {
            h ^= h >> 16;
            h *= 0x85EBCA6B;
            h ^= h >> 13;
            h *= 0xC2B2AE35;
            h ^= h >> 16;
            return h;
        }
        private static T[] ShuffleArray<T>(T[] array, uint seed)
        {
            var result = (T[])array.Clone();
            for (int i = result.Length - 1; i > 0; i--)
            {
                seed = seed * 1103515245 + 12345;
                int j = (int)(seed % (uint)(i + 1));
                var temp = result[i];
                result[i] = result[j];
                result[j] = temp;
            }
            return result;
        }
        #endregion
        #region 数据类型
        public class DecoyLayerData
        {
            public string layerName;
            public string[] states;
            public string description;
        }
        #endregion
    }
}
