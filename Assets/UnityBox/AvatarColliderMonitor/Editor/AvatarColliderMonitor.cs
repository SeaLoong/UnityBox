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
    public string sessionId;
    public Dictionary<string, Bounds> worldBounds = new Dictionary<string, Bounds>();
    public Dictionary<string, Transform> colliders = new Dictionary<string, Transform>();
    public Dictionary<string, string> paths = new Dictionary<string, string>();
    public Dictionary<string, string> configurations = new Dictionary<string, string>();
    public Dictionary<string, int> transformIds = new Dictionary<string, int>();
    public Dictionary<string, string> displayNames = new Dictionary<string, string>();

    public void Add(string key, string label, Transform transform, string configuration, string capturedPath = null)
    {
      colliders[key] = transform;
      paths[key] = capturedPath ?? CapturePath(transform);
      configurations[key] = configuration;
      transformIds[key] = transform != null ? transform.GetInstanceID() : 0;
      displayNames[key] = label;
    }

    public string GetColliderPath(string name) => paths.TryGetValue(name, out var path) ? path : "null";
    public string GetDisplayName(string key) => displayNames.TryGetValue(key, out var name) ? name : key;
    public IEnumerable<string> KeysComparedTo(ColliderSnapshot previous) =>
      previous != null && previous.avatarInstanceId == avatarInstanceId && previous.sessionId == sessionId
        ? paths.Keys.Union(previous.paths.Keys)
        : paths.Keys;

    public bool HasChangedFrom(ColliderSnapshot previous, string key)
    {
      if (previous == null || previous.avatarInstanceId != avatarInstanceId || previous.sessionId != sessionId) return false;
      return previous.paths.ContainsKey(key) != paths.ContainsKey(key) ||
        previous.GetColliderPath(key) != GetColliderPath(key) ||
        Value(previous.transformIds, key) != Value(transformIds, key) ||
        Value(previous.configurations, key) != Value(configurations, key) ||
        Value(previous.worldBounds, key) != Value(worldBounds, key);
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

  public sealed class ColliderSnapshotComparison
  {
    public readonly ColliderSnapshot Snapshot;
    public readonly ColliderSnapshot Previous;
    public readonly string[] Keys;
    public readonly HashSet<string> ChangedKeys;
    public bool HasChanges => ChangedKeys.Count != 0;

    internal ColliderSnapshotComparison(ColliderSnapshot snapshot, ColliderSnapshot previous)
    {
      Snapshot = snapshot;
      Previous = previous;
      Keys = snapshot.KeysComparedTo(previous).ToArray();
      ChangedKeys = new HashSet<string>(Keys.Where(key => snapshot.HasChangedFrom(previous, key)));
    }
  }

  // Sampling happens when explicitly refreshed, not on each IMGUI Layout/Repaint event.
  public sealed class ColliderSnapshotPreview
  {
    private ColliderSnapshot current;
    private GameObject root;
    public void Invalidate() => current = null;
    public ColliderSnapshot Get(GameObject selected)
    {
      if (selected == null) { root = null; current = null; return null; }
      if (current == null || root != selected)
      {
        root = selected;
        current = AvatarColliderMonitor.CaptureSnapshot(selected, "Preview", "Current");
      }
      return current;
    }
  }

  public sealed class ColliderChangeCount
  {
    public string Key;
    public string Label;
    public int Count;
  }

  public static class AvatarColliderMonitor
  {
    private static readonly List<ColliderSnapshot> snapshots = new List<ColliderSnapshot>();
    private const int MaximumSnapshots = 1000;
    private static int revision;
    private static int comparisonRevision = -1;
    private static int comparisonCount = -1;
    private static List<ColliderSnapshotComparison> comparisons = new List<ColliderSnapshotComparison>();
    private static readonly string CurrentSession = GetSessionId();
    private static ColliderChangeCount[] changeStatistics = Array.Empty<ColliderChangeCount>();
    private static int totalChanges;
    public static int TotalChanges { get { GetHistoryComparisons(); return totalChanges; } }
    public static IReadOnlyList<ColliderChangeCount> GetChangeStatistics()
    {
      GetHistoryComparisons();
      return changeStatistics;
    }

    private static string GetSessionId()
    {
      const string key = "UnityBox.AvatarColliderMonitor.Session";
      string value = SessionState.GetString(key, "");
      if (value.Length == 0) { value = Guid.NewGuid().ToString("N"); SessionState.SetString(key, value); }
      return value;
    }
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
        avatarName = root.name, avatarInstanceId = root.GetInstanceID(), sessionId = CurrentSession
      };
      foreach (var component in root.GetComponentsInChildren<Collider>(true))
        AddUnityCollider(snapshot, component, component.bounds);
      foreach (var component in root.GetComponentsInChildren<Collider2D>(true))
        AddUnityCollider(snapshot, component, component.bounds);
      CollectAdditionalColliders?.Invoke(root, snapshot);
      return snapshot;
    }

    private static void AddUnityCollider(ColliderSnapshot snapshot, Component component, Bounds bounds)
    {
      var configuration = new UnityColliderConfiguration
      {
        component = EditorJsonUtility.ToJson(component),
        localToWorld = component.transform.localToWorldMatrix,
        activeInHierarchy = component.gameObject.activeInHierarchy,
        layer = component.gameObject.layer
      };
      string key = "Unity/" + component.GetInstanceID();
      string path = ColliderSnapshot.CapturePath(component.transform);
      snapshot.Add(key, component.GetType().Name + " · " + path, component.transform,
        JsonUtility.ToJson(configuration), path);
      snapshot.worldBounds[key] = bounds;
    }

    // Component inputs remain accepted, including descriptors from optional integrations.
    public static void RecordSnapshot(UnityEngine.Object target, string phase, string timing)
    {
      var root = target as GameObject ?? (target as Component)?.gameObject;
      if (root == null || (!IsEnabled && phase != "Manual")) return;
      var snapshot = CaptureSnapshot(root, phase, timing);
      snapshots.Add(snapshot);
      revision++;
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
    public static void Clear() { snapshots.Clear(); revision++; }

    public static IReadOnlyList<ColliderSnapshotComparison> GetHistoryComparisons()
    {
      if (comparisonRevision == revision && comparisonCount == snapshots.Count) return comparisons;
      var latest = new Dictionary<(int, string), ColliderSnapshot>();
      comparisons = new List<ColliderSnapshotComparison>(snapshots.Count);
      foreach (var snapshot in snapshots)
      {
        var key = (snapshot.avatarInstanceId, snapshot.sessionId);
        latest.TryGetValue(key, out var previous);
        comparisons.Add(new ColliderSnapshotComparison(snapshot, previous));
        latest[key] = snapshot;
      }
      var counts = new Dictionary<string, ColliderChangeCount>();
      totalChanges = 0;
      foreach (var comparison in comparisons)
        foreach (var key in comparison.ChangedKeys)
        {
          totalChanges++;
          if (!counts.TryGetValue(key, out var count))
            counts[key] = count = new ColliderChangeCount { Key = key,
              Label = comparison.Snapshot.paths.ContainsKey(key)
                ? comparison.Snapshot.GetDisplayName(key) : comparison.Previous.GetDisplayName(key) };
          count.Count++;
        }
      changeStatistics = counts.Values.OrderByDescending(value => value.Count)
        .ThenBy(value => value.Key, StringComparer.Ordinal).ToArray();
      comparisonRevision = revision;
      comparisonCount = snapshots.Count;
      return comparisons;
    }

    [Serializable] private sealed class HistoryData { public int schema = 1; public List<SnapshotData> snapshots = new List<SnapshotData>(); }
    [Serializable] private sealed class SnapshotData
    {
      public string phase, timing, avatarName, timestamp, sessionId;
      public int avatarInstanceId;
      public List<EntryData> entries = new List<EntryData>();
    }
    [Serializable] private sealed class EntryData
    {
      public string key, path, label, configuration;
      public int transformId;
      public Bounds worldBounds;
      public bool hasBounds;
    }

    public static string ExportHistoryJson()
    {
      var data = new HistoryData();
      foreach (var snapshot in snapshots)
      {
        var record = new SnapshotData { phase = snapshot.phase, timing = snapshot.timing,
          avatarName = snapshot.avatarName, avatarInstanceId = snapshot.avatarInstanceId,
          timestamp = snapshot.timestamp.ToString("O"), sessionId = snapshot.sessionId };
        foreach (var key in snapshot.paths.Keys)
          record.entries.Add(new EntryData { key = key, path = snapshot.paths[key],
            label = snapshot.GetDisplayName(key), configuration = snapshot.configurations[key],
            transformId = snapshot.transformIds[key],
            hasBounds = snapshot.worldBounds.TryGetValue(key, out var bounds), worldBounds = bounds });
        data.snapshots.Add(record);
      }
      return JsonUtility.ToJson(data, true);
    }

    public static void ImportHistoryJson(string json, bool append = false)
    {
      var data = JsonUtility.FromJson<HistoryData>(json);
      if (data.schema != 1) throw new ArgumentException("Unsupported collider history schema: " + data.schema);
      if (!append) snapshots.Clear();
      foreach (var record in data.snapshots)
      {
        var snapshot = new ColliderSnapshot { phase = record.phase, timing = record.timing,
          avatarName = record.avatarName, avatarInstanceId = record.avatarInstanceId,
          timestamp = DateTime.Parse(record.timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind),
          sessionId = record.sessionId };
        foreach (var entry in record.entries)
        {
          snapshot.colliders[entry.key] = null;
          snapshot.paths[entry.key] = entry.path;
          snapshot.displayNames[entry.key] = entry.label;
          snapshot.configurations[entry.key] = entry.configuration;
          snapshot.transformIds[entry.key] = entry.transformId;
          if (entry.hasBounds) snapshot.worldBounds[entry.key] = entry.worldBounds;
        }
        snapshots.Add(snapshot);
      }
      if (snapshots.Count > MaximumSnapshots) snapshots.RemoveRange(0, snapshots.Count - MaximumSnapshots);
      revision++;
    }
    public static ColliderSnapshot GetPreviousSnapshot(int index)
    {
      if (index < 0 || index >= snapshots.Count) return null;
      int rootId = snapshots[index].avatarInstanceId;
      string sessionId = snapshots[index].sessionId;
      for (int i = index - 1; i >= 0; i--)
        if (snapshots[i].avatarInstanceId == rootId && snapshots[i].sessionId == sessionId) return snapshots[i];
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
    private readonly ColliderSnapshotPreview preview = new ColliderSnapshotPreview();
    private double nextPreviewRefresh;
    private bool showBounds;

    private void OnEnable()
    {
      SceneView.duringSceneGui += DrawBounds;
      Undo.undoRedoPerformed += InvalidatePreview;
    }
    private void OnDisable()
    {
      SceneView.duringSceneGui -= DrawBounds;
      Undo.undoRedoPerformed -= InvalidatePreview;
    }
    private void OnHierarchyChange() => InvalidatePreview();
    private void InvalidatePreview() { preview.Invalidate(); Repaint(); }
    private void DrawBounds(SceneView view)
    {
      if (!showBounds || selectedAvatar == null) return;
      var snapshot = preview.Get(selectedAvatar);
      var color = Handles.color;
      Handles.color = Color.cyan;
      foreach (var bounds in snapshot.worldBounds.Values)
        Handles.DrawWireCube(bounds.center, bounds.size);
      Handles.color = color;
    }

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
          preview.Invalidate();
          Repaint();
        }
        if (GUILayout.Button("Take Snapshot", GUILayout.Width(100)))
        {
          AvatarColliderMonitor.RecordSnapshot(selectedAvatar, "Manual", "User");
        }
      }
      EditorGUILayout.EndHorizontal();

      showBounds = EditorGUILayout.Toggle("Scene Bounds", showBounds);
      if (selectedAvatar == null)
      {
        EditorGUILayout.HelpBox("Select a root GameObject in Settings to preview Unity colliders and available integrations", MessageType.Info);
      }
      else
      {
        previewScrollPosition = EditorGUILayout.BeginScrollView(previewScrollPosition, GUILayout.Height(325));

        var currentPreview = preview.Get(selectedAvatar);
        foreach (var key in currentPreview.paths.Keys)
        {
          var displayName = currentPreview.GetDisplayName(key);
          var collider = currentPreview.colliders[key];
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

      EditorGUILayout.LabelField($"Total Changes: {AvatarColliderMonitor.TotalChanges}");
      var statistics = AvatarColliderMonitor.GetChangeStatistics();
      if (statistics.Count > 0)
      {
        EditorGUILayout.LabelField("Changes by Collider:", EditorStyles.boldLabel);
        statisticsScrollPosition = EditorGUILayout.BeginScrollView(statisticsScrollPosition, GUILayout.Height(250));
        foreach (var count in statistics)
          EditorGUILayout.LabelField($"{count.Label}: {count.Count}");
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

      if (GUILayout.Button("Export JSON", GUILayout.Width(100)))
      {
        string path = EditorUtility.SaveFilePanel("Export collider history", "", "collider-history.json", "json");
        if (!string.IsNullOrEmpty(path)) System.IO.File.WriteAllText(path, AvatarColliderMonitor.ExportHistoryJson());
      }
      if (GUILayout.Button("Import JSON", GUILayout.Width(100)))
      {
        string path = EditorUtility.OpenFilePanel("Import collider history", "", "json");
        if (!string.IsNullOrEmpty(path)) AvatarColliderMonitor.ImportHistoryJson(System.IO.File.ReadAllText(path));
      }
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
          bool hasChanges = AvatarColliderMonitor.GetHistoryComparisons()[i].HasChanges;

          if (showOnlyChanges && !hasChanges && i > 0) continue;
          if (!string.IsNullOrEmpty(searchFilter) &&
              snapshot.phase.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
              snapshot.timing.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

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

        var comparison = AvatarColliderMonitor.GetHistoryComparisons()[index];
        var previous = comparison.Previous;
        foreach (var colliderKey in comparison.Keys)
        {
          var isDiff = comparison.ChangedKeys.Contains(colliderKey);
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

    private void OnInspectorUpdate()
    {
      if (EditorApplication.timeSinceStartup >= nextPreviewRefresh)
      {
        nextPreviewRefresh = EditorApplication.timeSinceStartup + 0.5;
        preview.Invalidate();
      }
      Repaint();
    }
  }
  #endregion
}
