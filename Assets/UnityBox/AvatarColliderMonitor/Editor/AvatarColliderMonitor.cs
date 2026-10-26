using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


namespace UnityBox.AvatarColliderMonitor
{
  #region VRChat SDK Callbacks
  public class AvatarColliderMonitorPreCallback : IVRCSDKPreprocessAvatarCallback
  {
    public int callbackOrder => int.MinValue;

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
      AvatarColliderMonitor.RecordSnapshot(
        avatarGameObject != null ? avatarGameObject.GetComponent<VRCAvatarDescriptor>() : null,
        "VRChatSDK", "EarlyPreprocess");
      return true;
    }
  }

  // PostprocessAvatar supplies no avatar and may run after the build clone has
  // been destroyed. Sample the actual callback object before serialization instead
  // of picking an unrelated avatar from the scene in a postprocess callback.
  public class AvatarColliderMonitorLateCallback : IVRCSDKPreprocessAvatarCallback
  {
    public int callbackOrder => int.MaxValue;

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
      AvatarColliderMonitor.RecordSnapshot(
        avatarGameObject != null ? avatarGameObject.GetComponent<VRCAvatarDescriptor>() : null,
        "VRChatSDK", "LatePreprocess");
      return true;
    }
  }
  #endregion

  #region Data Classes
  public class ColliderSnapshot
  {
    public string phase;
    public string timing;
    public string avatarName;
    public int avatarInstanceId;
    public System.DateTime timestamp;
    public Dictionary<string, Transform> colliders = new Dictionary<string, Transform>();
    public Dictionary<string, string> paths = new Dictionary<string, string>();
    public Dictionary<string, string> configurations = new Dictionary<string, string>();
    public Dictionary<string, int> transformIds = new Dictionary<string, int>();

    public string GetColliderPath(string name) => paths.TryGetValue(name, out var path) ? path : "null";

    public bool HasChangedFrom(ColliderSnapshot previous, string key)
    {
      if (previous == null || previous.avatarInstanceId != avatarInstanceId) return false;
      return previous.GetColliderPath(key) != GetColliderPath(key) ||
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
  #endregion

  #region Core Monitor
  public static class AvatarColliderMonitor
  {
    private static readonly List<ColliderSnapshot> snapshots = new List<ColliderSnapshot>();
    private const int MaximumSnapshots = 1000;
    // Collider fields are discovered dynamically from VRCAvatarDescriptor

    private const string ENABLED_KEY = "AvatarColliderMonitor_Enabled";
    private const string LOG_CHANGES_KEY = "AvatarColliderMonitor_LogChanges";
    private const string VERBOSE_KEY = "AvatarColliderMonitor_Verbose";

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

    // Cache of collider fields (in declaration order)
    public static FieldInfo[] cachedColliderFields;

    public static FieldInfo[] GetColliderFields()
    {
      if (cachedColliderFields != null) return cachedColliderFields;
      var type = typeof(VRCAvatarDescriptor);
      var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          .Where(f => f.Name.StartsWith("collider_") &&
            f.FieldType == typeof(VRCAvatarDescriptor.ColliderConfig))
          .OrderBy(f => f.MetadataToken)
          .ToArray();
      cachedColliderFields = fields;
      return cachedColliderFields;
    }

    public static string FieldToKey(FieldInfo f)
    {
      return f.Name.StartsWith("collider_") ? f.Name.Substring("collider_".Length) : f.Name;
    }

    // Convert keys like "fingerIndexL" -> "Finger Index L"
    public static string KeyToDisplayName(string key)
    {
      if (string.IsNullOrEmpty(key)) return key;
      // Insert space before uppercase letters and before trailing 'L'/'R' if attached
      var sb = new System.Text.StringBuilder();
      for (int i = 0; i < key.Length; i++)
      {
        var c = key[i];
        if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1])) sb.Append(' ');
        sb.Append(c);
      }
      var s = sb.ToString();
      // Separate trailing single-letter side marker if present
      if (s.Length > 1 && (s.EndsWith("L") || s.EndsWith("R")) && s[s.Length - 2] != ' ')
        s = s.Substring(0, s.Length - 1) + " " + s.Substring(s.Length - 1);
      // Capitalize first letter
      return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));
    }

    public static List<ColliderSnapshot> GetSnapshots() => snapshots;
    public static void Clear() => snapshots.Clear();

    public static ColliderSnapshot GetPreviousSnapshot(int index)
    {
      if (index < 0 || index >= snapshots.Count) return null;
      int avatarId = snapshots[index].avatarInstanceId;
      for (int i = index - 1; i >= 0; i--)
        if (snapshots[i].avatarInstanceId == avatarId) return snapshots[i];
      return null;
    }

    public static void RecordSnapshot(VRCAvatarDescriptor descriptor, string phase, string timing)
    {
      if (descriptor == null || (!IsEnabled && phase != "Manual")) return;

      var snapshot = new ColliderSnapshot
      {
        phase = phase,
        timing = timing,
        timestamp = System.DateTime.Now,
        avatarName = descriptor.name,
        avatarInstanceId = descriptor.GetInstanceID()
      };

      // Discover collider fields dynamically and record their transforms in descriptor order
      var fields = GetColliderFields();
      foreach (var f in fields)
      {
        try
        {
          var config = (VRCAvatarDescriptor.ColliderConfig)f.GetValue(descriptor);
          var t = config.transform;
          var key = FieldToKey(f);
          snapshot.colliders[key] = t;
          snapshot.paths[key] = ColliderSnapshot.CapturePath(t);
          snapshot.transformIds[key] = t != null ? t.GetInstanceID() : 0;
          snapshot.configurations[key] = JsonUtility.ToJson(config);
        }
        catch
        {
          // ignore individual missing fields
        }
      }

      snapshots.Add(snapshot);
      if (snapshots.Count > MaximumSnapshots) snapshots.RemoveAt(0);

      if (snapshots.Count > 1 && LogChanges)
      {
        var previous = GetPreviousSnapshot(snapshots.Count - 1);
        var changes = new List<string>();

        var fieldsCheck = GetColliderFields();
        foreach (var f in fieldsCheck)
        {
          var name = FieldToKey(f);
          if (snapshot.HasChangedFrom(previous, name))
          {
            changes.Add($"  • {name}: {previous.GetColliderPath(name)} → {snapshot.GetColliderPath(name)} (reference, path or configuration changed)");
          }
        }

        if (changes.Count > 0)
        {
          Debug.LogWarning($"[Avatar Collider Monitor] ⚠️ Collider changes detected ({previous.phase}-{previous.timing} → {phase}-{timing}):\n" +
              string.Join("\n", changes) +
              "\nCompare the recorded samples to determine whether these changes are intentional.");
        }
      }

      if (VerboseLogging)
      {
        Debug.Log($"[Avatar Collider Monitor] {phase} - {timing}");
      }
    }
  }
  #endregion

  #region Editor Window
  public class AvatarColliderMonitorWindow : EditorWindow
  {
[MenuItem("Tools/UnityBox/Avatar Collider Monitor")]
    public static void ShowWindow()
    {
      var window = GetWindow<AvatarColliderMonitorWindow>("Avatar Collider Monitor");
      window.minSize = new Vector2(800, 600);

#if NDMF_AVAILABLE
      if (AvatarColliderMonitor.VerboseLogging)
        Debug.Log("[Avatar Collider Monitor] Window opened (NDMF available)");
#else
      Debug.LogWarning("[Avatar Collider Monitor] NDMF not available - limited monitoring");
#endif
    }

    private Vector2 scrollPosition;
    private Vector2 previewScrollPosition;
    private Vector2 snapshotsScrollPosition;
    private Vector2 statisticsScrollPosition;
    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    private bool showOnlyChanges = false;
    private string searchFilter = "";
    private VRCAvatarDescriptor selectedAvatar = null;

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
      selectedAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", selectedAvatar, typeof(VRCAvatarDescriptor), true);
      if (EditorGUI.EndChangeCheck() && selectedAvatar != null)
      {
        Selection.activeGameObject = selectedAvatar.gameObject;
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
        EditorGUILayout.HelpBox("Select a VRCAvatarDescriptor in Settings to preview colliders", MessageType.Info);
      }
      else
      {
        previewScrollPosition = EditorGUILayout.BeginScrollView(previewScrollPosition, GUILayout.Height(325));

        var fields = AvatarColliderMonitor.GetColliderFields();
        foreach (var f in fields)
        {
          var key = AvatarColliderMonitor.FieldToKey(f);
          var displayName = AvatarColliderMonitor.KeyToDisplayName(key);
          var colliderConfig = (VRCAvatarDescriptor.ColliderConfig)f.GetValue(selectedAvatar);
          var collider = colliderConfig.transform;
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

      // Build display name map from descriptor fields (preserves descriptor order)
      var colliderFields = AvatarColliderMonitor.GetColliderFields();
      var colliderDisplayNames = colliderFields.ToDictionary(f => AvatarColliderMonitor.FieldToKey(f), f => AvatarColliderMonitor.KeyToDisplayName(AvatarColliderMonitor.FieldToKey(f)));

      for (int i = 1; i < snapshots.Count; i++)
      {
        var prev = AvatarColliderMonitor.GetPreviousSnapshot(i);
        var curr = snapshots[i];

        foreach (var name in colliderDisplayNames.Keys)
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
          var displayName = colliderDisplayNames.ContainsKey(kvp.Key) ? colliderDisplayNames[kvp.Key] : kvp.Key;
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
            foreach (var kvp in snapshot.colliders)
            {
              if (snapshot.HasChangedFrom(prev, kvp.Key))
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

        var colliderFields = AvatarColliderMonitor.GetColliderFields();
        var colliderDisplayNames = colliderFields.ToDictionary(f => AvatarColliderMonitor.FieldToKey(f), f => AvatarColliderMonitor.KeyToDisplayName(AvatarColliderMonitor.FieldToKey(f)));

        foreach (var kvp in snapshot.colliders)
        {
          var isDiff = false;
          if (index > 0)
          {
            var prevSnap = AvatarColliderMonitor.GetPreviousSnapshot(index);
            isDiff = snapshot.HasChangedFrom(prevSnap, kvp.Key);
          }

          var displayName = colliderDisplayNames.ContainsKey(kvp.Key) ? colliderDisplayNames[kvp.Key] : kvp.Key;

          if (isDiff)
          {
            GUI.contentColor = new Color(1f, 0.7f, 0.3f);
            EditorGUILayout.LabelField($"→ {displayName}", snapshot.GetColliderPath(kvp.Key));
            GUI.contentColor = Color.white;
          }
          else
          {
            EditorGUILayout.LabelField($"  {displayName}", snapshot.GetColliderPath(kvp.Key));
          }
          if (snapshot.configurations.TryGetValue(kvp.Key, out var configuration))
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
