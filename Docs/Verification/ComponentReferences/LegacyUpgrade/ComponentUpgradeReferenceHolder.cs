using UnityEngine;

namespace UnityBox.ComponentUpgradeFixtureRuntime
{
    // A fixture-owned cross-component reference, not a plugin authoring component.
    public sealed class ComponentUpgradeReferenceHolder : MonoBehaviour
    {
        public MonoBehaviour[] references;
    }
}
