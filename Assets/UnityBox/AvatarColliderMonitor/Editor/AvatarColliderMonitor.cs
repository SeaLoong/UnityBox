using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.AvatarColliderMonitor
{
  public class ColliderSnapshot
  {
    // Retain the existing fields for consumers of recorded snapshots.
    public string phase;
    public string timing;
    public string avatarName;
    public int avatarInstanceId;
    public DateTime timestamp;
    public Dictionary<string, Transform> colliders = new Dictionary<string, Transform>();
    public Dictionary<string, string> paths = new Dictionary<string, string>();
    public Dictionary<string, string> configurations = new Dictionary<string, string>();
    public Dictionary<string, int> transformIds = new Dictionary<string, int>();
    public Dictionary<string, string> displayNames = new Dictionary<string, string>();

    public void Add(string key, string label, Transform transform, string configuration)
    {
      colliders[key] = transform;
      paths[key] = CapturePath(transform);
      configurations[key] = configuration;
      transformIds[key] = transform != null ? transform.GetInstanceID() : 0;
      displayNames[key] = label;
    }

    public string GetColliderPath(string name) => paths.TryGetValue(name, out var path) ? path : "null";
    public string GetDisplayName(string key) => displayNames.TryGetValue(key, out var name) ? name : key;
    public IEnumerable<string> KeysComparedTo(ColliderSnapshot previous) =>
      previous != null && previous.avatarInstanceId == avatarInstanceId
        ? paths.Keys.Union(previous.paths.Keys)
        : paths.Keys;

    public bool HasChangedFrom(ColliderSnapshot previous, string key)
    {
      if (previous == null || previous.avatarInstanceId != avatarInstanceId) return false;
      return previous.paths.ContainsKey(key) != paths.ContainsKey(key) ||
        previous.GetColliderPath(key) != GetColliderPath(key) ||
        Value(previous.transformIds, key) != Value(transformIds, key) ||
        Value(previous.configurations, key) != Value(configurations, key);
    }

    private static T Value<T>(Dictionary<string, T> source, string key) =>
      source.TryGetValue(key, out var value) ? value : default;

    public static string CapturePath(Transform transform)
    {
      if (transform == null) return "null";
      var parts = new List<string>();
      for (var current = transform; current != null; current = current.parent)
        parts.Add(current.name);
      parts.Reverse();
      return string.Join("/", parts);
    }
  }

  public static class AvatarColliderMonitor
  {
    private static readonly List<ColliderSnapshot> snapshots = new List<ColliderSnapshot>();
    private const int MaximumSnapshots = 1000;
    private const string ENABLED_KEY = "AvatarColliderMonitor_Enabled";
    private const string LOG_CHANGES_KEY = "AvatarColliderMonitor_LogChanges";
    private const string VERBOSE_KEY = "AvatarColliderMonitor_Verbose";

    // Optional adapters add their domain-specific configuration without introducing
    // SDK types or mandatory SDK assemblies into the Unity-only monitor.
    public static event Action<GameObject, ColliderSnapshot> CollectAdditionalColliders;

    public static bool IsEnabled
    {
      get => EditorPrefs.GetBool(ENABLED_KEY, false);
      set => EditorPrefs.SetBool(ENABLED_KEY, value);
    }
    public static bool LogChanges
    {
      get => EditorPrefs.GetBool(LOG_CHANGES_KEY, true);
      set => EditorPrefs.SetBool(LOG_CHANGES_KEY, value);
    }
    public static bool VerboseLogging
    {
      get => EditorPrefs.GetBool(VERBOSE_KEY, false);
      set => EditorPrefs.SetBool(VERBOSE_KEY, value);
    }

    [Serializable]
    private class UnityColliderConfiguration
    {
      public string component;
      public Matrix4x4 localToWorld;
      public bool activeInHierarchy;
      public int layer;
    }

    public static ColliderSnapshot CaptureSnapshot(GameObject root, string phase, string timing)
    {
      if (root == null) return null;
      var snapshot = new ColliderSnapshot
      {
        phase = phase, timing = timing, timestamp = DateTime.Now,
        avatarName = root.name, avatarInstanceId = root.GetInstanceID()
      };
      foreach (var component in root.GetComponentsInChildren<Component>(true))
      {
        if (!(component is Collider) && !(component is Collider2D)) continue;
        var configuration = new UnityColliderConfiguration
        {
          component = EditorJsonUtility.ToJson(component),
          localToWorld = component.transform.localToWorldMatrix,
          activeInHierarchy = component.gameObject.activeInHierarchy,
          layer = component.gameObject.layer
        };
        snapshot.Add("Unity/" + component.GetInstanceID(),
          component.GetType().Name + " · " + ColliderSnapshot.CapturePath(component.transform),
          component.transform, JsonUtility.ToJson(configuration));
      }
      CollectAdditionalColliders?.Invoke(root, snapshot);
      return snapshot;
    }

    // Component inputs remain accepted, including descriptors from optional integrations.
    public static void RecordSnapshot(UnityEngine.Object target, string phase, string timing)
    {
      var root = target as GameObject ?? (target as Component)?.gameObject;
      if (root == null || (!IsEnabled && phase != "Manual")) return;
      var snapshot = CaptureSnapshot(root, phase, timing);
      snapshots.Add(snapshot);
      if (snapshots.Count > MaximumSnapshots) snapshots.RemoveAt(0);
      var previous = GetPreviousSnapshot(snapshots.Count - 1);
      if (LogChanges && previous != null)
      {
        var changes = snapshot.KeysComparedTo(previous)
          .Where(key => snapshot.HasChangedFrom(previous, key))
          .Select(key => $"  • {snapshot.GetDisplayName(key)}: {previous.GetColliderPath(key)} → {snapshot.GetColliderPath(key)} (reference, path or configuration changed)")
          .ToList();
        if (changes.Count > 0)
          Debug.LogWarning($"[Avatar Collider Monitor] Collider changes detected ({previous.phase}-{previous.timing} → {phase}-{timing}):\n" +
            string.Join("\n", changes) + "\nCompare the recorded samples to determine whether these changes are intentional.");
      }
      if (VerboseLogging) Debug.Log($"[Avatar Collider Monitor] {phase} - {timing}");
    }

    public static List<ColliderSnapshot> GetSnapshots() => snapshots;
    public static void Clear() => snapshots.Clear();
    public static ColliderSnapshot GetPreviousSnapshot(int index)
    {
      if (index < 0 || index >= snapshots.Count) return null;
      int rootId = snapshots[index].avatarInstanceId;
      for (int i = index - 1; i >= 0; i--)
        if (snapshots[i].avatarInstanceId == rootId) return snapshots[i];
      return null;
    }
  }

  public class UnityColliderMonitorSceneCallback : IProcessSceneWithReport
  {
    public int callbackOrder => int.MaxValue;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
      if (!scene.IsValid() || !AvatarColliderMonitor.IsEnabled) return;
      foreach (var root in scene.GetRootGameObjects())
        AvatarColliderMonitor.RecordSnapshot(root, "UnityScene", report != null ? "Build" : "ProcessScene");
    }
  }

  #region Editor Window
  public class AvatarColliderMonitorWindow : EditorWindow
  {
    [MenuItem("Tools/UnityBox/Avatar Collider Monitor")]
    public static void ShowWindow()
    {
      var window = GetWindow<AvatarColliderMonitorWindow>("Avatar Collider Monitor");
      window.minSize = new Vector2(800, 600);

    }

    private Vector2 scrollPosition;
    private Vector2 previewScrollPosition;
    private Vector2 snapshotsScrollPosition;
    private Vector2 statisticsScrollPosition;
    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    private bool showOnlyChanges = false;
    private string searchFilter = "";
    private GameObject selectedAvatar = null;

    private void OnGUI()
    {
      scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

      EditorGUILayout.BeginHorizontal();
      DrawSettings();
      EditorGUILayout.EndHorizontal();

      EditorGUILayout.BeginHorizontal();
      DrawPreview();
      DrawStatistics();
      EditorGUILayout.EndHorizontal();

      DrawSnapshots();
      EditorGUILayout.EndScrollView();
    }

    private void DrawSettings()
    {
      EditorGUILayout.BeginVertical("box");
      EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

      EditorGUI.BeginChangeCheck();
      selectedAvatar = (GameObject)EditorGUILayout.ObjectField("Root Object", selectedAvatar, typeof(GameObject), true);
      if (EditorGUI.EndChangeCheck() && selectedAvatar != null)
      {
        Selection.activeGameObject = selectedAvatar;
      }

      EditorGUI.BeginChangeCheck();
      var isEnabled = EditorGUILayout.Toggle("Enable Monitoring", AvatarColliderMonitor.IsEnabled);
      var logChanges = EditorGUILayout.Toggle("Log Changes", AvatarColliderMonitor.LogChanges);
      var verbose = EditorGUILayout.Toggle("Verbose Logging", AvatarColliderMonitor.VerboseLogging);

      if (EditorGUI.EndChangeCheck())
      {
        AvatarColliderMonitor.IsEnabled = isEnabled;
        AvatarColliderMonitor.LogChanges = logChanges;
        AvatarColliderMonitor.VerboseLogging = verbose;
      }

      EditorGUILayout.EndVertical();
    }

    private void DrawPreview()
    {
      EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width / 2 - 15));

      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
      GUILayout.FlexibleSpace();
      if (selectedAvatar != null)
      {
        if (GUILayout.Button("Refresh", GUILayout.Width(80)))
        {
          Repaint();
        }
        if (GUILayout.Button("Take Snapshot", GUILayout.Width(100)))
        {
          AvatarColliderMonitor.RecordSnapshot(selectedAvatar, "Manual", "User");
        }
      }
      EditorGUILayout.EndHorizontal();

      if (selectedAvatar == null)
      {
        EditorGUILayout.HelpBox("Select a root GameObject in Settings to preview Unity colliders and available integrations", MessageType.Info);
      }
      else
      {
        previewScrollPosition = EditorGUILayout.BeginScrollView(previewScrollPosition, GUILayout.Height(325));

        var preview = AvatarColliderMonitor.CaptureSnapshot(selectedAvatar, "Preview", "Current");
        foreach (var key in preview.paths.Keys)
        {
          var displayName = preview.GetDisplayName(key);
          var collider = preview.colliders[key];
          string value = collider != null ? collider.name : "<null>";

          EditorGUILayout.BeginHorizontal();
          EditorGUILayout.LabelField(displayName, GUILayout.Width(140));
          EditorGUILayout.SelectableLabel(value, GUILayout.Height(EditorGUIUtility.singleLineHeight));

          if (collider != null)
          {
            if (GUILayout.Button("→", GUILayout.Width(30)))
            {
              Selection.activeGameObject = collider.gameObject;
              EditorGUIUtility.PingObject(collider.gameObject);
            }
          }
          EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
      }

      EditorGUILayout.EndVertical();
    }

    private void DrawStatistics()
    {
      EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width / 2 - 15));
      EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

      var snapshots = AvatarColliderMonitor.GetSnapshots();
      if (snapshots.Count == 0)
      {
        EditorGUILayout.HelpBox("No monitoring data available", MessageType.Info);
        EditorGUILayout.EndVertical();
        return;
      }

      int totalChanges = 0;
      var changesByCollider = new Dictionary<string, int>();

      for (int i = 1; i < snapshots.Count; i++)
      {
        var prev = AvatarColliderMonitor.GetPreviousSnapshot(i);
        var curr = snapshots[i];

        foreach (var name in curr.KeysComparedTo(prev))
        {
          if (curr.HasChangedFrom(prev, name))
          {
            totalChanges++;
            if (!changesByCollider.ContainsKey(name))
              changesByCollider[name] = 0;
            changesByCollider[name]++;
          }
        }
      }

      EditorGUILayout.LabelField($"Total Changes: {totalChanges}");

      if (changesByCollider.Count > 0)
      {
        EditorGUILayout.LabelField("Changes by Collider:", EditorStyles.boldLabel);

        statisticsScrollPosition = EditorGUILayout.BeginScrollView(statisticsScrollPosition, GUILayout.Height(250));
        foreach (var kvp in changesByCollider.OrderByDescending(x => x.Value))
        {
          var displayName = snapshots.LastOrDefault(item => item.paths.ContainsKey(kvp.Key))?.GetDisplayName(kvp.Key) ?? kvp.Key;
          EditorGUILayout.LabelField($"{displayName}: {kvp.Value}");
        }
        EditorGUILayout.EndScrollView();
      }

      EditorGUILayout.EndVertical();
    }

    private void DrawSnapshots()
    {
      var snapshots = AvatarColliderMonitor.GetSnapshots();

      EditorGUILayout.BeginVertical("box");

      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Snapshot History", EditorStyles.boldLabel, GUILayout.Width(150));
      showOnlyChanges = EditorGUILayout.Toggle("Show Only Changes", showOnlyChanges, GUILayout.Width(150));
      GUILayout.FlexibleSpace();
      EditorGUILayout.LabelField($"Total: {snapshots.Count}", EditorStyles.label, GUILayout.Width(100));

      if (GUILayout.Button("Clear", GUILayout.Width(80)))
      {
        if (EditorUtility.DisplayDialog("Clear History", "Clear all monitoring records?", "OK", "Cancel"))
        {
          AvatarColliderMonitor.Clear();
          Repaint();
        }
      }
      EditorGUILayout.EndHorizontal();

      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
      searchFilter = EditorGUILayout.TextField(searchFilter, GUILayout.ExpandWidth(true));
      EditorGUILayout.EndHorizontal();

      if (snapshots.Count == 0)
      {
        EditorGUILayout.HelpBox("No snapshots recorded", MessageType.Info);
      }
      else
      {
        snapshotsScrollPosition = EditorGUILayout.BeginScrollView(snapshotsScrollPosition);

        for (int i = 0; i < snapshots.Count; i++)
        {
          var snapshot = snapshots[i];
          bool hasChanges = false;

          if (i > 0)
          {
            var prev = AvatarColliderMonitor.GetPreviousSnapshot(i);
            foreach (var colliderKey in snapshot.KeysComparedTo(prev))
            {
              if (snapshot.HasChangedFrom(prev, colliderKey))
              {
                hasChanges = true;
                break;
              }
            }
          }

          if (showOnlyChanges && !hasChanges && i > 0) continue;
          if (!string.IsNullOrEmpty(searchFilter) &&
              !snapshot.phase.ToLower().Contains(searchFilter.ToLower()) &&
              !snapshot.timing.ToLower().Contains(searchFilter.ToLower())) continue;

          DrawSnapshot(snapshot, i, hasChanges);
        }

        EditorGUILayout.EndScrollView();
      }

      EditorGUILayout.EndVertical();
    }

    private void DrawSnapshot(ColliderSnapshot snapshot, int index, bool hasChanges)
    {
      var bgColor = hasChanges ? new Color(0.8f, 0.4f, 0.2f, 0.3f) : new Color(0.2f, 0.2f, 0.2f, 0.3f);
      GUI.backgroundColor = bgColor;
      EditorGUILayout.BeginVertical("box");
      GUI.backgroundColor = Color.white;

      var key = $"snapshot_{index}";
      if (!foldouts.ContainsKey(key)) foldouts[key] = false;

      var icon = hasChanges ? "⚠️" : "✓";

      // Color the label text if has changes
      var labelStyle = new GUIStyle(EditorStyles.foldoutHeader);
      if (hasChanges)
      {
        labelStyle.normal.textColor = new Color(1f, 0.7f, 0.3f);
        labelStyle.onNormal.textColor = new Color(1f, 0.7f, 0.3f);
        labelStyle.active.textColor = new Color(1f, 0.7f, 0.3f);
        labelStyle.onActive.textColor = new Color(1f, 0.7f, 0.3f);
        labelStyle.focused.textColor = new Color(1f, 0.7f, 0.3f);
        labelStyle.onFocused.textColor = new Color(1f, 0.7f, 0.3f);
      }

      var label = $"{icon} #{index:D3} | {snapshot.avatarName} | {snapshot.phase} - {snapshot.timing} | {snapshot.timestamp:HH:mm:ss.fff}";

      foldouts[key] = EditorGUILayout.Foldout(foldouts[key], label, true, labelStyle);

      if (foldouts[key])
      {
        EditorGUI.indentLevel++;

        var previous = AvatarColliderMonitor.GetPreviousSnapshot(index);
        foreach (var colliderKey in snapshot.KeysComparedTo(previous))
        {
          var isDiff = snapshot.HasChangedFrom(previous, colliderKey);
          var displayName = snapshot.paths.ContainsKey(colliderKey)
            ? snapshot.GetDisplayName(colliderKey)
            : previous.GetDisplayName(colliderKey) + " (removed)";

          if (isDiff)
          {
            GUI.contentColor = new Color(1f, 0.7f, 0.3f);
            EditorGUILayout.LabelField($"→ {displayName}", snapshot.GetColliderPath(colliderKey));
            GUI.contentColor = Color.white;
          }
          else
          {
            EditorGUILayout.LabelField($"  {displayName}", snapshot.GetColliderPath(colliderKey));
          }
          if (snapshot.configurations.TryGetValue(colliderKey, out var configuration))
            EditorGUILayout.LabelField("Configuration", configuration, EditorStyles.wordWrappedLabel);
        }

        EditorGUI.indentLevel--;
      }

      EditorGUILayout.EndVertical();
    }

    private void OnInspectorUpdate() => Repaint();
  }
  #endregion
}
