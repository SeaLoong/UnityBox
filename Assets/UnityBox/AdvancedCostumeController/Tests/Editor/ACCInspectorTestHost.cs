using UnityEditor;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController.Tests
{
  internal sealed class ACCInspectorTestHost : EditorWindow
  {
    internal UnityEditor.Editor[] Inspectors;
    internal int DrawCount;
    private Vector2 scroll;

    private void OnGUI()
    {
      scroll = EditorGUILayout.BeginScrollView(scroll);
      foreach (var inspector in Inspectors ?? new UnityEditor.Editor[0])
        if (inspector != null) inspector.OnInspectorGUI();
      EditorGUILayout.EndScrollView();
      DrawCount++;
    }
  }
}
