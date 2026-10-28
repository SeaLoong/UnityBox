using NUnit.Framework;
using UnityEngine;

namespace UnityBox.AvatarColliderMonitor.Tests
{
  public class ColliderSnapshotTests
  {
    private GameObject root;
    private bool enabledBefore;
    private bool logChangesBefore;
    private bool verboseBefore;

    [SetUp]
    public void SetUp()
    {
      enabledBefore = AvatarColliderMonitor.IsEnabled;
      logChangesBefore = AvatarColliderMonitor.LogChanges;
      verboseBefore = AvatarColliderMonitor.VerboseLogging;
      AvatarColliderMonitor.IsEnabled = false;
      AvatarColliderMonitor.LogChanges = false;
      AvatarColliderMonitor.VerboseLogging = false;
      AvatarColliderMonitor.Clear();
      root = new GameObject("SnapshotRoot");
    }

    [TearDown]
    public void TearDown()
    {
      Object.DestroyImmediate(root);
      AvatarColliderMonitor.Clear();
      AvatarColliderMonitor.IsEnabled = enabledBefore;
      AvatarColliderMonitor.LogChanges = logChangesBefore;
      AvatarColliderMonitor.VerboseLogging = verboseBefore;
    }

    private static string Key(Component collider) => "Unity/" + collider.GetInstanceID();

    private ColliderSnapshot Capture() => AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "Test");

    [Test]
    public void CapturesThreeDimensionalAndTwoDimensionalCollidersIncludingInactiveChildren()
    {
      var box = root.AddComponent<BoxCollider>();
      var sphere = root.AddComponent<SphereCollider>();
      var child = new GameObject("InactiveChild");
      child.transform.SetParent(root.transform);
      var capsule = child.AddComponent<CapsuleCollider>();
      // Unity prohibits mixing 2D and 3D physics components on one GameObject.
      // Keep both in the sampled hierarchy and exercise an inactive ancestor.
      var twoDimensionalChild = new GameObject("TwoDimensional");
      twoDimensionalChild.transform.SetParent(child.transform);
      var circle = twoDimensionalChild.AddComponent<CircleCollider2D>();
      child.SetActive(false);

      var snapshot = Capture();
      foreach (var collider in new Component[] { box, sphere, capsule, circle })
      {
        Assert.That(snapshot.paths, Contains.Key(Key(collider)));
        Assert.That(snapshot.colliders[Key(collider)], Is.SameAs(collider.transform));
        Assert.That(snapshot.configurations[Key(collider)], Is.Not.Empty);
      }
      Assert.That(snapshot.paths, Has.Count.EqualTo(4));
      Assert.That(snapshot.GetColliderPath(Key(circle)), Is.EqualTo("SnapshotRoot/InactiveChild/TwoDimensional"));
    }

    [Test]
    public void HistoryKeepsCapturedPathAfterTransformRenameAndDestruction()
    {
      var child = new GameObject("OriginalBone");
      child.transform.SetParent(root.transform);
      var collider = child.AddComponent<SphereCollider>();
      string key = Key(collider);
      AvatarColliderMonitor.RecordSnapshot(root, "Manual", "Before");
      var first = AvatarColliderMonitor.GetSnapshots()[0];
      child.name = "RenamedBone";
      AvatarColliderMonitor.RecordSnapshot(root, "Manual", "After");
      var second = AvatarColliderMonitor.GetSnapshots()[1];
      Assert.That(second.HasChangedFrom(first, key), Is.True);
      Object.DestroyImmediate(child);
      Assert.That(first.GetColliderPath(key), Is.EqualTo("SnapshotRoot/OriginalBone"));
      Assert.That(second.GetColliderPath(key), Is.EqualTo("SnapshotRoot/RenamedBone"));
    }

    [Test]
    public void ConfigurationChangesAreDetectedWithoutReplacingTransform()
    {
      var sphere = root.AddComponent<SphereCollider>();
      sphere.radius = 0.1f;
      var first = Capture();
      Assert.That(Capture().HasChangedFrom(first, Key(sphere)), Is.False);
      sphere.radius = 0.5f;
      Assert.That(Capture().HasChangedFrom(first, Key(sphere)), Is.True);
      Assert.That(first.configurations[Key(sphere)], Is.Not.EqualTo(Capture().configurations[Key(sphere)]));
    }

    [Test]
    public void TwoDimensionalConfigurationChangesAreDetected()
    {
      var collider = root.AddComponent<BoxCollider2D>();
      var first = Capture();
      collider.size = new Vector2(2, 3);
      Assert.That(Capture().HasChangedFrom(first, Key(collider)), Is.True);
    }

    [Test]
    public void AddedAndRemovedComponentsOnTheSameTransformAreDetectedIndependently()
    {
      var box = root.AddComponent<BoxCollider>();
      var first = Capture();
      var sphere = root.AddComponent<SphereCollider>();
      string sphereKey = Key(sphere);
      var added = Capture();
      Assert.That(added.HasChangedFrom(first, sphereKey), Is.True);
      Assert.That(added.HasChangedFrom(first, Key(box)), Is.False);
      Object.DestroyImmediate(sphere);
      var removed = Capture();
      Assert.That(removed.KeysComparedTo(added), Contains.Item(sphereKey));
      Assert.That(removed.HasChangedFrom(added, sphereKey), Is.True);
      Assert.That(removed.HasChangedFrom(added, Key(box)), Is.False);
    }

    [Test]
    public void TransformActivityLayerAndEnabledStateChangesAreDetected()
    {
      var collider = root.AddComponent<BoxCollider>();
      var first = Capture();
      root.transform.localScale = Vector3.one * 2;
      Assert.That(Capture().HasChangedFrom(first, Key(collider)), Is.True);
      first = Capture();
      root.layer = 2;
      Assert.That(Capture().HasChangedFrom(first, Key(collider)), Is.True);
      first = Capture();
      collider.enabled = false;
      Assert.That(Capture().HasChangedFrom(first, Key(collider)), Is.True);
      first = Capture();
      root.SetActive(false);
      Assert.That(Capture().HasChangedFrom(first, Key(collider)), Is.True);
    }

    [Test]
    public void InterleavedRootsAreComparedOnlyWithTheirOwnHistory()
    {
      var collider = root.AddComponent<BoxCollider>();
      var other = new GameObject("OtherRoot");
      try
      {
        other.AddComponent<BoxCollider>();
        AvatarColliderMonitor.RecordSnapshot(root, "Manual", "First");
        AvatarColliderMonitor.RecordSnapshot(other, "Manual", "Other");
        AvatarColliderMonitor.RecordSnapshot(root, "Manual", "Again");
        var records = AvatarColliderMonitor.GetSnapshots();
        Assert.That(AvatarColliderMonitor.GetPreviousSnapshot(1), Is.Null);
        Assert.That(AvatarColliderMonitor.GetPreviousSnapshot(2), Is.SameAs(records[0]));
        Assert.That(records[1].HasChangedFrom(records[0], Key(collider)), Is.False);
      }
      finally { Object.DestroyImmediate(other); }
    }

    [Test]
    public void DisabledAutomaticMonitoringStillAllowsManualSnapshotsFromComponent()
    {
      var collider = root.AddComponent<BoxCollider>();
      AvatarColliderMonitor.RecordSnapshot(root, "UnityScene", "Build");
      new UnityColliderMonitorSceneCallback().OnProcessScene(root.scene, null);
      Assert.That(AvatarColliderMonitor.GetSnapshots(), Is.Empty);
      AvatarColliderMonitor.RecordSnapshot(collider, "Manual", "User");
      Assert.That(AvatarColliderMonitor.GetSnapshots(), Has.Count.EqualTo(1));
      Assert.That(AvatarColliderMonitor.GetSnapshots()[0].paths, Contains.Key(Key(collider)));
    }

    [Test]
    public void OptionalProviderAddsFrozenConfigurationAndCanBeUnregistered()
    {
      System.Action<GameObject, ColliderSnapshot> provider = (target, snapshot) =>
        snapshot.Add("Custom/Collider", "Custom", target.transform, "captured configuration");
      AvatarColliderMonitor.CollectAdditionalColliders += provider;
      try
      {
        var snapshot = Capture();
        Assert.That(snapshot.GetColliderPath("Custom/Collider"), Is.EqualTo("SnapshotRoot"));
        Assert.That(snapshot.configurations["Custom/Collider"], Is.EqualTo("captured configuration"));
      }
      finally { AvatarColliderMonitor.CollectAdditionalColliders -= provider; }
      Assert.That(Capture().paths.ContainsKey("Custom/Collider"), Is.False);
    }
  }
}
