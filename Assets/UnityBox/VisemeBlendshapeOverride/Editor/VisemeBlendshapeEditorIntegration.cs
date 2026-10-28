using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityBox.VisemeBlendshapeOverride
{
    /// <summary>Optional platform adapters supply defaults without changing explicit serialized overrides.</summary>
    public static class VisemeBlendshapeEditorIntegration
    {
        public sealed class Defaults
        {
            public SkinnedMeshRenderer Renderer;
            public IReadOnlyList<string> Blendshapes;
        }

        public static Func<VisemeBlendshapeOverrideComponent, Defaults> ResolveDefaults;

        public static Defaults GetDefaults(VisemeBlendshapeOverrideComponent component)
        {
            return ResolveDefaults?.Invoke(component) ?? new Defaults();
        }
    }
}
