using UnityEngine;

namespace UnityBox.PrefabDiffPatcher.Tests
{
    // Registered as an Editor-only runtime script so native AddComponent has a MonoScript.
    public sealed class ExposedReferenceFixture : MonoBehaviour
    {
        public ExposedReference<GameObject> target;
    }
}
