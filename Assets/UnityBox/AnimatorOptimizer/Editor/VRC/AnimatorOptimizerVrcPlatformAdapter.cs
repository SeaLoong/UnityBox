using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.AnimatorOptimizer
{
    [InitializeOnLoad]
    public sealed class AnimatorOptimizerVrcPlatformAdapter : IAnimatorOptimizerPlatformAdapter
    {
        static AnimatorOptimizerVrcPlatformAdapter()
        {
            AnimatorOptimizerPlatform.Register(new AnimatorOptimizerVrcPlatformAdapter());
        }

        public GameObject FindAvatarRoot(Component component) =>
            component != null ? component.GetComponentInParent<VRCAvatarDescriptor>()?.gameObject : null;

        public IAnimatorOptimizerControllerTarget ResolvePlayableTarget(GameObject root, AnimatorOptimizerComponent.PlayableLayer layer)
        {
            var descriptor = root != null ? root.GetComponent<VRCAvatarDescriptor>() : null;
            return descriptor == null ? null : new PlayableTarget(descriptor, layer);
        }

        public IAnimatorOptimizerLayerControl GetLayerControl(StateMachineBehaviour behaviour) =>
            behaviour is VRC_AnimatorLayerControl control ? new LayerControl(control) : null;

        private sealed class LayerControl : IAnimatorOptimizerLayerControl
        {
            private readonly VRC_AnimatorLayerControl control;
            public LayerControl(VRC_AnimatorLayerControl control) { this.control = control; }
            public StateMachineBehaviour Behaviour => control;
            public int LayerIndex { get => control.layer; set => control.layer = value; }
            public AnimatorOptimizerComponent.PlayableLayer? PlayableLayer => control.playable switch
            {
                VRC_AnimatorLayerControl.BlendableLayer.FX => AnimatorOptimizerComponent.PlayableLayer.FX,
                VRC_AnimatorLayerControl.BlendableLayer.Gesture => AnimatorOptimizerComponent.PlayableLayer.Gesture,
                VRC_AnimatorLayerControl.BlendableLayer.Action => AnimatorOptimizerComponent.PlayableLayer.Action,
                VRC_AnimatorLayerControl.BlendableLayer.Additive => AnimatorOptimizerComponent.PlayableLayer.Additive,
                _ => null,
            };
        }

        private sealed class PlayableTarget : IAnimatorOptimizerControllerTarget
        {
            private readonly VRCAvatarDescriptor descriptor;
            private readonly bool isBase;
            private readonly VRCAvatarDescriptor.AnimLayerType type;

            public PlayableTarget(VRCAvatarDescriptor descriptor, AnimatorOptimizerComponent.PlayableLayer layer)
            {
                this.descriptor = descriptor;
                isBase = layer <= AnimatorOptimizerComponent.PlayableLayer.FX;
                type = layer switch
                {
                    AnimatorOptimizerComponent.PlayableLayer.Base => VRCAvatarDescriptor.AnimLayerType.Base,
                    AnimatorOptimizerComponent.PlayableLayer.Additive => VRCAvatarDescriptor.AnimLayerType.Additive,
                    AnimatorOptimizerComponent.PlayableLayer.Gesture => VRCAvatarDescriptor.AnimLayerType.Gesture,
                    AnimatorOptimizerComponent.PlayableLayer.Action => VRCAvatarDescriptor.AnimLayerType.Action,
                    AnimatorOptimizerComponent.PlayableLayer.FX => VRCAvatarDescriptor.AnimLayerType.FX,
                    AnimatorOptimizerComponent.PlayableLayer.Sitting => VRCAvatarDescriptor.AnimLayerType.Sitting,
                    AnimatorOptimizerComponent.PlayableLayer.TPose => VRCAvatarDescriptor.AnimLayerType.TPose,
                    _ => VRCAvatarDescriptor.AnimLayerType.IKPose,
                };
            }

            private VRCAvatarDescriptor.CustomAnimLayer[] Layers =>
                descriptor == null ? null : isBase ? descriptor.baseAnimationLayers : descriptor.specialAnimationLayers;

            public Transform BindingRoot => descriptor != null ? descriptor.transform : null;

            public AnimatorController Controller
            {
                get
                {
                    if (descriptor == null || !descriptor.customizeAnimationLayers || Layers == null) return null;
                    var index = Array.FindIndex(Layers, layer => layer.type == type);
                    return index < 0 || Layers[index].isDefault ? null : Layers[index].animatorController as AnimatorController;
                }
            }

            public void Assign(AnimatorController controller)
            {
                if (descriptor == null || !descriptor.customizeAnimationLayers)
                    throw new InvalidOperationException("Target Avatar no longer has custom animation layers enabled.");
                var layers = Layers;
                var index = layers == null ? -1 : Array.FindIndex(layers, layer => layer.type == type);
                if (index < 0) throw new InvalidOperationException("Target Playable Layer is missing.");
                layers[index].animatorController = controller;
                layers[index].isDefault = false;
                if (isBase) descriptor.baseAnimationLayers = layers;
                else descriptor.specialAnimationLayers = layers;
                EditorUtility.SetDirty(descriptor);
            }

            public Action CaptureRestore()
            {
                var custom = descriptor.customizeAnimationLayers;
                var baseLayers = descriptor.baseAnimationLayers == null ? null :
                    (VRCAvatarDescriptor.CustomAnimLayer[])descriptor.baseAnimationLayers.Clone();
                var specialLayers = descriptor.specialAnimationLayers == null ? null :
                    (VRCAvatarDescriptor.CustomAnimLayer[])descriptor.specialAnimationLayers.Clone();
                return () =>
                {
                    if (descriptor == null) return;
                    descriptor.customizeAnimationLayers = custom;
                    descriptor.baseAnimationLayers = baseLayers;
                    descriptor.specialAnimationLayers = specialLayers;
                    EditorUtility.SetDirty(descriptor);
                };
            }
        }
    }
}
