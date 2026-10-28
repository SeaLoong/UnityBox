using UnityEngine;
using System;
using System.Collections.Generic;

namespace UnityBox.PrefabDiffPatcher.Tests
{
    // Registered as an Editor-only runtime script so native AddComponent has a MonoScript.
    public sealed class ExposedReferenceFixture : MonoBehaviour, ISerializationCallbackReceiver
    {
        public ExposedReference<GameObject> target;
        [SerializeReference] public ReferenceGraph graph;
        [SerializeField] private List<int> keys = new List<int>();
        [SerializeField] private List<UnityEngine.Object> values = new List<UnityEngine.Object>();
        [NonSerialized] public Dictionary<int, UnityEngine.Object> lookup = new Dictionary<int, UnityEngine.Object>();

        public void OnBeforeSerialize()
        {
            keys.Clear(); values.Clear();
            foreach (var pair in lookup) { keys.Add(pair.Key); values.Add(pair.Value); }
        }
        public void OnAfterDeserialize()
        {
            lookup = new Dictionary<int, UnityEngine.Object>();
            for (int i = 0; i < keys.Count; i++) lookup[keys[i]] = values[i];
        }
    }

    [Serializable]
    public sealed class ReferenceGraph
    {
        public string label;
        public UnityEngine.Object target;
        [SerializeReference] public ReferenceGraph next;
        [SerializeReference] public ReferenceGraph alias;
    }
}
