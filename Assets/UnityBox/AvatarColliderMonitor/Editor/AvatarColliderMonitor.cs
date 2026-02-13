using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

#if NDMF_AVAILABLE
using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(UnityBox.AvatarColliderMonitor.AvatarColliderMonitorPlugin))]
#endif

namespace UnityBox.AvatarColliderMonitor
{
  #region NDMF Plugin
#if NDMF_AVAILABLE
  public class AvatarColliderMonitorPlugin : Plugin<AvatarColliderMonitorPlugin>
  {
public override string QualifiedName => "top.sealoong.unitybox.avatar-collider-monitor";
    public override string DisplayName => "Avatar Collider Monitor";

    protected override void Configure()
    {
      try
      {
        // Respect global enabled flag: do not inject phases when monitoring is disabled
        if (!AvatarColliderMonitor.IsEnabled)
        {
          Debug.Log("[Avatar Collider Monitor] NDMF monitoring disabled by user settings; skipping phase registration.");
          return;
        }

        // Discover all BuildPhase values via reflection instead of hardcoding
        var phaseType = typeof(BuildPhase);
        var phaseFields = phaseType.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == phaseType)
            .OrderBy(f => ((BuildPhase)f.GetValue(null)).GetHashCode()) // Try to preserve order
            .ToArray();

        if (phaseFields.Length == 0)
        {
          Debug.LogWarning("[Avatar Collider Monitor] No BuildPhase values found");
          return;
        }

        Debug.Log($"[Avatar Collider Monitor] NDMF monitoring enabled ({phaseFields.Length} phases)");

        foreach (var phaseField in phaseFields)
        {
          var phase = (BuildPhase)phaseField.GetValue(null);
          var phaseName = phaseField.Name;

          InPhase(phase).Run($"Monitor_{phaseName}_Start", ctx =>
          {
            var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (AvatarColliderMonitor.VerboseLogging)
              Debug.Log($"[Avatar Collider Monitor] Snapshot at NDMF-{phaseName}-Start");
            AvatarColliderMonitor.RecordSnapshot(descriptor, $"NDMF-{phaseName}", "Start");
          });

          InPhase(phase).Run($"Monitor_{phaseName}_End", ctx =>
          {
            var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (AvatarColliderMonitor.VerboseLogging)
              Debug.Log($"[Avatar Collider Monitor] Snapshot at NDMF-{phaseName}-End");
            AvatarColliderMonitor.RecordSnapshot(descriptor, $"NDMF-{phaseName}", "End");
          });
        }
      }
      catch (System.Exception e)
      {
        Debug.LogWarning($"[Avatar Collider Monitor] NDMF monitoring setup failed: {e.Message}");
      }
    }
  }
#endif
  #endregion

  #region VRChat SDK Callbacks
  // VRC SDK PreProcess/PostProcess callbacks, works alongside NDMF
  public class AvatarColliderMonitorPreCallback : IVRCSDKPreprocessAvatarCallback
  {
    public int callbackOrder => -10000; // Execute as early as possible

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
      var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
      if (descriptor != null)
      {
        if (AvatarColliderMonitor.VerboseLogging)
          Debug.Log("[Avatar Collider Monitor] Snapshot at VRChatSDK-PreProcess");
        AvatarColliderMonitor.RecordSnapshot(descriptor, "VRChatSDK", "PreProcess");
      }

      return true;
    }
  }

  public class AvatarColliderMonitorPostCallback : IVRCSDKPostprocessAvatarCallback
  {
    public int callbackOrder => 10000; // Execute as late as possible

    public void OnPostprocessAvatar()
    {
      var descriptor = GameObject.FindObjectOfType<VRCAvatarDescriptor>();
      if (descriptor != null)
      {
        if (AvatarColliderMonitor.VerboseLogging)
          Debug.Log("[Avatar Collider Monitor] Snapshot at VRChatSDK-PostProcess");
        AvatarColliderMonitor.RecordSnapshot(descriptor, "VRChatSDK", "PostProcess");
      }
    }
  }
  #endregion

  #region VRCFury Hooks
  // VRCFury monitor - dynamically tracks VRCFury build process via reflection
  public static class VRCFuryMonitorService
  {
    private static bool initialized = false;
    private static System.Type featureOrderType;
    private static System.Array allFeatureOrders;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
      if (!initialized)
      {
        // Always subscribe to hierarchy changes immediately
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Debug.Log("[Avatar Collider Monitor] VRCFury hierarchy monitoring subscribed");

        // Try to discover VRCFury assemblies (may not be loaded yet at Initialize time)
        TryDiscoverVRCFury();

        initialized = true;
      }
    }

    // Lazy discovery of VRCFury assemblies
    private static void TryDiscoverVRCFury()
    {
      try
      {
        var allAssemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        var vrcfuryAssemblies = allAssemblies.Where(a => a.GetName().Name.ToLower().Contains("vrcfury")).ToArray();

        if (vrcfuryAssemblies.Length > 0)
        {
          Debug.Log($"[Avatar Collider Monitor] Found {vrcfuryAssemblies.Length} VRCFury assemblies: " +
                    string.Join(", ", vrcfuryAssemblies.Select(a => a.GetName().Name)));

          var vrcfuryAssembly = allAssemblies.FirstOrDefault(a =>
              a.GetName().Name.Contains("vrcfury", System.StringComparison.OrdinalIgnoreCase) &&
              a.GetName().Name.Contains("editor", System.StringComparison.OrdinalIgnoreCase));

          if (vrcfuryAssembly != null)
          {
            featureOrderType = vrcfuryAssembly.GetType("VF.Feature.Base.FeatureOrder");
            if (featureOrderType != null && featureOrderType.IsEnum)
            {
              allFeatureOrders = System.Enum.GetValues(featureOrderType);
              Debug.Log($"[Avatar Collider Monitor] VRCFury FeatureOrder found ({allFeatureOrders.Length} phases)");
            }
          }
        }
        else
        {
          Debug.Log("[Avatar Collider Monitor] No VRCFury assemblies found (may load later)");
        }
      }
      catch (System.Exception e)
      {
        Debug.LogWarning($"[Avatar Collider Monitor] VRCFury discovery failed: {e.Message}");
      }
    }

    // Allow toggling monitoring at runtime
    private static bool subscribed = false;

    public static void SetActive(bool active)
    {
      // Monitoring state is now checked inside OnHierarchyChanged
      // This method is kept for compatibility but we always stay subscribed
    }

    private static VRCAvatarDescriptor lastMonitoredAvatar = null;
    private static bool isBuildingVRCFury = false;
    private static int snapshotCount = 0;
    private static HashSet<string> previousPhases = new HashSet<string>();

    private static void OnHierarchyChanged()
    {
      try
      {
        // Skip if monitoring is disabled
        if (!AvatarColliderMonitor.IsEnabled)
          return;

        // Lazy discover VRCFury if not found yet
        if (featureOrderType == null)
        {
          TryDiscoverVRCFury();
        }

        // Find all VRCFury components (VF.Component.VRCFury* or VRCFury*)
        var allMonoBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
        var vrcFuryComponents = allMonoBehaviours
            .Where(c => c != null && c.GetType().FullName != null)
            .Where(c =>
            {
              var fullName = c.GetType().FullName;
              return (fullName.Contains("VRCFury") || fullName.StartsWith("VF.Component.VRCFury"))
                     && !fullName.Contains("Test");
            })
            .ToArray();

        // Detect VRCFury build start (any VRCFury component exists + not building yet)
        if (vrcFuryComponents.Length > 0 && !isBuildingVRCFury)
        {
          isBuildingVRCFury = true;
          snapshotCount = 0;
          previousPhases.Clear();
          var go = vrcFuryComponents[0].gameObject;
          var descriptor = go.GetComponentInParent<VRCAvatarDescriptor>();
          if (descriptor != null)
          {
            lastMonitoredAvatar = descriptor;
            AvatarColliderMonitor.RecordSnapshot(descriptor, "VRCFury", "BuildStart");
            snapshotCount++;
            previousPhases.Add("BuildStart");

            Debug.Log($"[Avatar Collider Monitor] VRCFury build started on '{descriptor.name}' (found {vrcFuryComponents.Length} VRCFury components)");
          }
        }

        // Continue monitoring while building - dynamically discover all active VRCFury feature phases
        if (isBuildingVRCFury && vrcFuryComponents.Length > 0 && lastMonitoredAvatar != null)
        {
          var currentPhases = new HashSet<string>();

          // Discover all VRCFury.Feature.* or VF.Feature.* components currently active in the scene
          foreach (var mb in allMonoBehaviours)
          {
            if (mb != null && mb.GetType().FullName != null)
            {
              var fullName = mb.GetType().FullName;
              // Match VF.Feature.* or VRCFury.Feature.* components
              if ((fullName.Contains("Feature.") && (fullName.StartsWith("VF.") || fullName.StartsWith("VRCFury.")))
                  && !fullName.Contains("Base"))
              {
                // Extract phase name
                var phaseName = fullName;
                // Remove namespace prefixes
                if (phaseName.Contains("Feature."))
                {
                  phaseName = phaseName.Substring(phaseName.IndexOf("Feature.") + "Feature.".Length);
                }
                // Remove any trailing classes
                phaseName = phaseName.Split('+')[0];
                currentPhases.Add(phaseName);
              }
            }
          }

          // Take snapshot for any new phases detected
          foreach (var phase in currentPhases)
          {
            if (!previousPhases.Contains(phase))
            {
              AvatarColliderMonitor.RecordSnapshot(lastMonitoredAvatar, "VRCFury", phase);
              snapshotCount++;
              previousPhases.Add(phase);

              Debug.Log($"[Avatar Collider Monitor] VRCFury phase detected: {phase}");
            }
          }
        }

        // Detect VRCFury build end (VRCFury components gone + was building)
        if (vrcFuryComponents.Length == 0 && isBuildingVRCFury && lastMonitoredAvatar != null)
        {
          AvatarColliderMonitor.RecordSnapshot(lastMonitoredAvatar, "VRCFury", "BuildComplete");
          snapshotCount++;
          previousPhases.Add("BuildComplete");

          Debug.Log($"[Avatar Collider Monitor] VRCFury build completed ({snapshotCount} snapshots, phases: {string.Join(", ", previousPhases)})");

          isBuildingVRCFury = false;
          lastMonitoredAvatar = null;
          previousPhases.Clear();
        }
      }
      catch (System.Exception e)
      {
        Debug.LogWarning($"[Avatar Collider Monitor] VRCFury monitoring error: {e.Message}\n{e.StackTrace}");
      }
    }

    // Get key phase names for UI display
    public static string[] GetKeyPhases()
    {
      if (allFeatureOrders == null || allFeatureOrders.Length == 0)
        return new string[] { "BuildStart", "BuildComplete" };

      var keyPhases = new List<string> { "BuildStart" };

      var importantPhases = new[] {
                "AdvancedColliders", "GlobalColliders", "BoundingBoxFix",
                "CloneAllControllers", "FinalizeController", "Validation"
            };

      foreach (var phase in allFeatureOrders)
      {
        var phaseName = phase.ToString();
        if (importantPhases.Contains(phaseName))
        {
          keyPhases.Add(phaseName);
        }
      }

      keyPhases.Add("BuildComplete");
      return keyPhases.ToArray();
    }
  }
  #endregion

  #region Data Classes
  public class ColliderSnapshot
  {
    public string phase;
    public string timing;
    public System.DateTime timestamp;
    public Dictionary<string, Transform> colliders = new Dictionary<string, Transform>();

    public string GetColliderPath(string name)
    {
      if (!colliders.TryGetValue(name, out var t) || t == null)
        return "null";
      var parts = new List<string>();
      var current = t;
      while (current != null)
      {
        parts.Insert(0, current.name);
        current = current.parent;
      }
      return string.Join("/", parts);
    }
  }
  #endregion

  #region Core Monitor
  public static class AvatarColliderMonitor
  {
    private static List<ColliderSnapshot> snapshots = new List<ColliderSnapshot>();
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
          .Where(f => f.Name.StartsWith("collider_"))
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

    public static void RecordSnapshot(VRCAvatarDescriptor descriptor, string phase, string timing)
    {
      if (descriptor == null || (!IsEnabled && phase != "Manual")) return;

      var snapshot = new ColliderSnapshot
      {
        phase = phase,
        timing = timing,
        timestamp = System.DateTime.Now
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
        }
        catch
        {
          // ignore individual missing fields
        }
      }

      snapshots.Add(snapshot);

      if (snapshots.Count > 1 && LogChanges)
      {
        var previous = snapshots[snapshots.Count - 2];
        var changes = new List<string>();

        var fieldsCheck = GetColliderFields();
        foreach (var f in fieldsCheck)
        {
          var name = FieldToKey(f);
          if (previous.colliders.TryGetValue(name, out var prev) &&
              snapshot.colliders.TryGetValue(name, out var curr) &&
              prev != curr)
          {
            changes.Add($"  • {name}: {previous.GetColliderPath(name)} → {snapshot.GetColliderPath(name)}");
          }
        }

        if (changes.Count > 0)
        {
          Debug.LogWarning($"[Avatar Collider Monitor] ⚠️ Collider changes detected ({previous.phase}-{previous.timing} → {phase}-{timing}):\n" +
              string.Join("\n", changes) +
              "\n\n⚠️ WARNING: Collider modifications may affect plugins that depend on specific collider configurations.\n" +
              "Plugins like DynamicsAdvanceSetter or other collider-dependent tools may not work as expected.\n" +
              "Consider checking if these changes are intentional.");
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

      var isEnabled = EditorGUILayout.Toggle("Enable Monitoring", AvatarColliderMonitor.IsEnabled);
      var logChanges = EditorGUILayout.Toggle("Log Changes", AvatarColliderMonitor.LogChanges);
      var verbose = EditorGUILayout.Toggle("Verbose Logging", AvatarColliderMonitor.VerboseLogging);

      if (EditorGUI.EndChangeCheck())
      {
        AvatarColliderMonitor.IsEnabled = isEnabled;
        AvatarColliderMonitor.LogChanges = logChanges;
        AvatarColliderMonitor.VerboseLogging = verbose;
        // Toggle VRCFury monitoring subscription immediately
        try { VRCFuryMonitorService.SetActive(isEnabled); } catch { }
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
        var prev = snapshots[i - 1];
        var curr = snapshots[i];

        foreach (var name in colliderDisplayNames.Keys)
        {
          if (prev.colliders.TryGetValue(name, out var p) && curr.colliders.TryGetValue(name, out var c) && p != c)
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

        var statsScroll = EditorGUILayout.BeginScrollView(new Vector2(), GUILayout.Height(250));
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
            var prev = snapshots[i - 1];
            foreach (var kvp in snapshot.colliders)
            {
              if (prev.colliders.TryGetValue(kvp.Key, out var prevT) && prevT != kvp.Value)
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

      var label = $"{icon} #{index:D3} | {snapshot.phase} - {snapshot.timing} | {snapshot.timestamp:HH:mm:ss.fff}";

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
            var prevSnap = AvatarColliderMonitor.GetSnapshots()[index - 1];
            isDiff = prevSnap.colliders.TryGetValue(kvp.Key, out var prevT) && prevT != kvp.Value;
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
        }

        EditorGUI.indentLevel--;
      }

      EditorGUILayout.EndVertical();
    }

    private void OnInspectorUpdate() => Repaint();
  }
  #endregion
}
