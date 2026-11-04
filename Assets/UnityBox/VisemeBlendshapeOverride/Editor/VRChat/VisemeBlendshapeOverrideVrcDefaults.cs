using UnityEditor;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.VisemeBlendshapeOverride
{
    [InitializeOnLoad]
    internal static class VisemeBlendshapeOverrideVrcDefaults
    {
        static VisemeBlendshapeOverrideVrcDefaults()
        {
            VisemeBlendshapeEditorIntegration.ResolveDefaults = component =>
            {
                var descriptor = component != null ? component.GetComponent<VRCAvatarDescriptor>() : null;
                return descriptor == null ? null : new VisemeBlendshapeEditorIntegration.Defaults
                {
                    Renderer = descriptor.VisemeSkinnedMesh,
                    Blendshapes = descriptor.VisemeBlendShapes,
                };
            };
        }
    }
}
