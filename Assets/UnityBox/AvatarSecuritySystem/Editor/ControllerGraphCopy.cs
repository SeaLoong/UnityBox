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
                    case AnimatorStateMachine _: copy = new AnimatorStateMachine(); break;
                    case AnimatorState _: copy = new AnimatorState(); break;
                    case AnimatorStateTransition _: copy = new AnimatorStateTransition(); break;
                    case AnimatorTransition _: copy = new AnimatorTransition(); break;
                    case BlendTree _: copy = new BlendTree(); break;
                    case StateMachineBehaviour behaviour: copy = Object.Instantiate(behaviour); break;
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
