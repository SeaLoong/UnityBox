using System;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    /// <summary>Optional, typed platform services. The core assembly never references an avatar SDK.</summary>
    public interface IAnimatorOptimizerPlatformAdapter
    {
        GameObject FindAvatarRoot(Component component);
        IAnimatorOptimizerControllerTarget ResolvePlayableTarget(GameObject root, AnimatorOptimizerComponent.PlayableLayer layer);
        IAnimatorOptimizerLayerControl GetLayerControl(StateMachineBehaviour behaviour);
    }

    public interface IAnimatorOptimizerControllerTarget
    {
        AnimatorController Controller { get; }
        Transform BindingRoot { get; }
        void Assign(AnimatorController controller);
        Action CaptureRestore();
    }

    public interface IAnimatorOptimizerLayerControl
    {
        StateMachineBehaviour Behaviour { get; }
        int LayerIndex { get; set; }
        AnimatorOptimizerComponent.PlayableLayer? PlayableLayer { get; }
    }

    public static class AnimatorOptimizerPlatform
    {
        public static IAnimatorOptimizerPlatformAdapter Adapter { get; private set; }

        public static void Register(IAnimatorOptimizerPlatformAdapter adapter)
        {
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public static GameObject FindAvatarRoot(Component component) =>
            component == null ? null : Adapter?.FindAvatarRoot(component) ?? component.gameObject;

        public static IAnimatorOptimizerLayerControl GetLayerControl(StateMachineBehaviour behaviour) =>
            behaviour == null ? null : Adapter?.GetLayerControl(behaviour);

        // Called only on build clones (SDK/NDMF) or on Unity's build-time scene copy.
        // Keeping the serialized component SDK-independent preserves its script and assembly identity.
        public static void StripConfigurationComponents(GameObject root)
        {
            if (root == null) return;
            foreach (var component in root.GetComponentsInChildren<AnimatorOptimizerComponent>(true))
                UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
