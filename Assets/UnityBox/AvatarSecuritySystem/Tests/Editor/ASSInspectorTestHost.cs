using UnityEditor;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    internal sealed class ASSInspectorTestHost : EditorWindow
    {
        internal UnityEditor.Editor Inspector;
        internal int DrawCount;
        private Vector2 scroll;

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (Inspector != null) Inspector.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
            DrawCount++;
        }
    }
}
