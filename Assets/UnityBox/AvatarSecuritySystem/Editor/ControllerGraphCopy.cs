using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Creates a persistent graph copy and resolves AnimatorOverrideController motions.</summary>
    public static class ControllerGraphCopy
    {
        // CopyAsset remaps references inside one file. A state stored in a second
        // NDMF container can still point back into the original controller.
        internal static void IsolateExternalStateGraph(AnimatorController copy, string sourcePath)
        {
            string copyPath = AssetDatabase.GetAssetPath(copy);
            Dictionary<long, Object> copiedById = null;
            var replacements = new Dictionary<Object, Object>();
            var visited = new HashSet<Object> { copy };
            var pending = new Stack<Object>();
            pending.Push(copy);

            while (pending.Count > 0)
            {
                using (var serialized = new SerializedObject(pending.Pop()))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var target = property.objectReferenceValue;
                        if (!(target is AnimatorStateMachine || target is AnimatorState ||
                              target is AnimatorTransitionBase || target is StateMachineBehaviour)) continue;

                        var owned = Resolve(target);
                        if (owned != target) property.objectReferenceValue = owned;
                        if (visited.Add(owned)) pending.Push(owned);
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            Object Resolve(Object target)
            {
                if (visited.Contains(target)) return target;
                if (replacements.TryGetValue(target, out var owned)) return owned;
                string path = AssetDatabase.GetAssetPath(target);
                if (path == copyPath) return target;
                if (path == sourcePath)
                {
                    if (copiedById == null)
                        copiedById = AssetDatabase.LoadAllAssetsAtPath(copyPath).Where(asset => asset).ToDictionary(LocalId);
                    owned = copiedById[LocalId(target)];
                }
                else
                {
                    owned = NewStateGraphNode(target);
                    EditorUtility.CopySerialized(target, owned);
                    owned.name = target.name;
                    AssetDatabase.AddObjectToAsset(owned, copy);
                }
                replacements.Add(target, owned);
                return owned;
            }
        }

        private static Object NewStateGraphNode(Object source)
        {
            switch (source)
            {
                case AnimatorStateMachine _: return new AnimatorStateMachine();
                case AnimatorState _: return new AnimatorState();
                case AnimatorStateTransition _: return new AnimatorStateTransition();
                case AnimatorTransition _: return new AnimatorTransition();
                case StateMachineBehaviour behaviour: return Object.Instantiate(behaviour);
                default: throw new ArgumentException("Expected an animator state graph object.", nameof(source));
            }
        }

        private static long LocalId(Object asset)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long id)) return id;
            throw new InvalidOperationException($"Cannot identify controller subasset '{asset.name}'.");
        }

        public static AnimatorController CreateEditableCopy(RuntimeAnimatorController source, string targetPath)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!targetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Choose a new controller path under Assets/.", nameof(targetPath));
            if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
                throw new ArgumentException("Choose a new controller asset path.", nameof(targetPath));

            var overrides = new List<AnimatorOverrideController>();
            var current = source;
            while (current is AnimatorOverrideController overridden)
            {
                overrides.Add(overridden);
                current = overridden.runtimeAnimatorController;
            }
            if (!(current is AnimatorController original))
                throw new ArgumentException("The source needs an AnimatorController base.", nameof(source));

            var effectiveClips = original.animationClips.Where(clip => clip != null).Distinct()
                .ToDictionary(clip => clip, clip => clip);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            for (var index = overrides.Count - 1; index >= 0; index--)
            {
                pairs.Clear();
                overrides[index].GetOverrides(pairs);
                var replacements = pairs.Where(pair => pair.Key != null && pair.Value != null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var key in effectiveClips.Keys.ToArray())
                    if (replacements.TryGetValue(key, out var replacement) ||
                        replacements.TryGetValue(effectiveClips[key], out replacement))
                        effectiveClips[key] = replacement;
            }

            var result = new AnimatorController { name = original.name };
            AssetDatabase.CreateAsset(result, targetPath);
            var copies = new Dictionary<Object, Object> { [original] = result };
            try
            {
                CopyFields(original, result);
                AssetDatabase.SaveAssets();
                return result;
            }
            catch
            {
                AssetDatabase.DeleteAsset(targetPath);
                throw;
            }

            Object CopyReference(Object value, bool applyClipOverrides)
            {
                if (value == null) return null;
                if (applyClipOverrides && value is AnimationClip clip && effectiveClips.TryGetValue(clip, out var replacement))
                    value = replacement;
                if (copies.TryGetValue(value, out var existing)) return existing;

                Object copy;
                switch (value)
                {
                    case AnimatorStateMachine _:
                    case AnimatorState _:
                    case AnimatorTransitionBase _:
                    case StateMachineBehaviour _:
                        copy = NewStateGraphNode(value);
                        break;
                    case BlendTree _: copy = new BlendTree(); break;
                    case AnimationClip _ when !EditorUtility.IsPersistent(value): copy = new AnimationClip(); break;
                    case AvatarMask _ when !EditorUtility.IsPersistent(value): copy = new AvatarMask(); break;
                    default: return value;
                }

                copies.Add(value, copy);
                AssetDatabase.AddObjectToAsset(copy, result);
                CopyFields(value, copy);
                return copy;
            }

            void CopyFields(Object from, Object to)
            {
                EditorUtility.CopySerialized(from, to);
                to.name = from.name;
                using (var serialized = new SerializedObject(to))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var mapped = CopyReference(property.objectReferenceValue,
                            from is AnimatorState || from is BlendTree || from is AnimatorController);
                        if (mapped != property.objectReferenceValue) property.objectReferenceValue = mapped;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(to);
            }
        }
    }
}
