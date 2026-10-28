using System;
using System.Linq;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class VrcPlayableControllers
    {
        public static void PreparePlayableControllerCopies(VRCAvatarDescriptor descriptor, bool fxOnly = false)
        {
            if (descriptor == null || !descriptor.customizeAnimationLayers) return;

            var baseLayers = descriptor.baseAnimationLayers;
            PreparePlayableControllerCopies(baseLayers, "Base", fxOnly);
            descriptor.baseAnimationLayers = baseLayers;

            var specialLayers = descriptor.specialAnimationLayers;
            PreparePlayableControllerCopies(specialLayers, "Special", fxOnly);
            descriptor.specialAnimationLayers = specialLayers;
        }

        public static void EnableCustomAnimationLayers(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return;
            if (!descriptor.customizeAnimationLayers)
            {
                // These serialized values were inactive. Enabling the FX output must
                // not reactivate stale controllers in any other playable slot.
                descriptor.baseAnimationLayers = AsDefaultLayers(descriptor.baseAnimationLayers);
                descriptor.specialAnimationLayers = AsDefaultLayers(descriptor.specialAnimationLayers);
            }

            var baseLayers = (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).ToList();
            if (!baseLayers.Any(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX))
                baseLayers.Add(new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = true
                });
            descriptor.baseAnimationLayers = baseLayers.ToArray();
            descriptor.customizeAnimationLayers = true;
        }

        private static VRCAvatarDescriptor.CustomAnimLayer[] AsDefaultLayers(VRCAvatarDescriptor.CustomAnimLayer[] source)
        {
            if (source == null) return Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var layers = (VRCAvatarDescriptor.CustomAnimLayer[])source.Clone();
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i].isDefault = true;
                layers[i].animatorController = null;
            }
            return layers;
        }

        private static void PreparePlayableControllerCopies(VRCAvatarDescriptor.CustomAnimLayer[] layers, string scope, bool fxOnly)
        {
            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.isDefault || (fxOnly && layer.type != VRCAvatarDescriptor.AnimLayerType.FX)) continue;
                if (!(layer.animatorController is AnimatorController sourceController) || sourceController == null) continue;

                var clonedController = Obfuscator.DuplicateAnimatorControllerForBuild(sourceController, $"{scope}_{layer.type}_{i}");
                if (clonedController == null) continue;

                layer.animatorController = clonedController;
                layer.isDefault = false;
                layers[i] = layer;
            }
        }

        public static void ObfuscatePlayableControllers(VRCAvatarDescriptor descriptor, bool hasNDMF)
        {
            if (descriptor == null || !descriptor.customizeAnimationLayers) return;
            var controllers = (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Concat(descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Where(layer => !layer.isDefault).Select(layer => layer.animatorController as AnimatorController);
            Obfuscator.ObfuscateControllers(controllers, hasNDMF);
        }
    }
}
