using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace UnityBox.AvatarColliderMonitor.Tests
{
  public class VrcColliderSnapshotTests
  {
    private GameObject avatar;
    private bool enabledBefore;
    private bool logChangesBefore;

    [SetUp]
    public void SetUp()
    {
      enabledBefore = AvatarColliderMonitor.IsEnabled;
      logChangesBefore = AvatarColliderMonitor.LogChanges;
      AvatarColliderMonitor.IsEnabled = false;
      AvatarColliderMonitor.LogChanges = false;
      AvatarColliderMonitor.Clear();
      avatar = new GameObject("SnapshotAvatar");
      avatar.AddComponent<VRCAvatarDescriptor>();
      VrcColliderSnapshotProvider.Register();
    }

    [TearDown]
    public void TearDown()
    {
      Object.DestroyImmediate(avatar);
      AvatarColliderMonitor.Clear();
      AvatarColliderMonitor.IsEnabled = enabledBefore;
      AvatarColliderMonitor.LogChanges = logChangesBefore;
    }

    [Test]
    public void HistoryKeepsCapturedPathAfterTransformRenameAndDestruction()
    {
      var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
      var bone = new GameObject("OriginalBone");
      bone.transform.SetParent(avatar.transform);
      var field = VrcColliderSnapshotProvider.GetColliderFields().First();
      var config = (VRCAvatarDescriptor.ColliderConfig)field.GetValue(descriptor);
      config.transform = bone.transform;
      field.SetValue(descriptor, config);
      AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "Before");
      var first = AvatarColliderMonitor.GetSnapshots()[0];
      string key = VrcColliderSnapshotProvider.FieldToKey(field);
      bone.name = "RenamedBone";
      AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "After");
      var second = AvatarColliderMonitor.GetSnapshots()[1];
      Assert.That(second.HasChangedFrom(first, key), Is.True);
      Object.DestroyImmediate(bone);
      Assert.That(first.GetColliderPath(key), Is.EqualTo("SnapshotAvatar/OriginalBone"));
      Assert.That(second.GetColliderPath(key), Is.EqualTo("SnapshotAvatar/RenamedBone"));
    }

    [Test]
    public void ConfigurationChangesAreDetectedWithoutReplacingTransform()
    {
      var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
      var field = VrcColliderSnapshotProvider.GetColliderFields().First();
      object config = field.GetValue(descriptor);
      var radius = typeof(VRCAvatarDescriptor.ColliderConfig).GetField("radius");
      Assert.That(radius, Is.Not.Null);
      radius.SetValue(config, 0.1f);
      field.SetValue(descriptor, config);
      AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "Before");
      radius.SetValue(config, 0.5f);
      field.SetValue(descriptor, config);
      AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "After");
      var records = AvatarColliderMonitor.GetSnapshots();
      Assert.That(records[1].HasChangedFrom(records[0], VrcColliderSnapshotProvider.FieldToKey(field)), Is.True);
    }

    [Test]
    public void InterleavedAvatarsAreComparedOnlyWithTheirOwnHistory()
    {
      var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
      var other = new GameObject("OtherAvatar");
      try
      {
        var otherDescriptor = other.AddComponent<VRCAvatarDescriptor>();
        AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "First");
        AvatarColliderMonitor.RecordSnapshot(otherDescriptor, "Manual", "Other");
        AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "Again");
        var records = AvatarColliderMonitor.GetSnapshots();
        Assert.That(AvatarColliderMonitor.GetPreviousSnapshot(1), Is.Null);
        Assert.That(AvatarColliderMonitor.GetPreviousSnapshot(2), Is.SameAs(records[0]));
        foreach (var field in VrcColliderSnapshotProvider.GetColliderFields())
          Assert.That(records[1].HasChangedFrom(records[0], VrcColliderSnapshotProvider.FieldToKey(field)), Is.False);
      }
      finally { Object.DestroyImmediate(other); }
    }

    [Test]
    public void PhysBoneColliderCapturesRadiusAndResolvedRootWithoutDescriptor()
    {
      var root = new GameObject("PhysicsRoot");
      try
      {
        var child = new GameObject("PhysBone"); child.transform.SetParent(root.transform, false);
        var target = new GameObject("Target"); target.transform.SetParent(root.transform, false);
        var collider = child.AddComponent<VRCPhysBoneCollider>();
        collider.rootTransform = target.transform;
        collider.radius = .1f;
        string key = "VRC/PhysBone/" + collider.GetInstanceID();
        var first = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "Before");
        Assert.That(first.GetColliderPath(key), Is.EqualTo("PhysicsRoot/Target"));
        collider.radius = .5f;
        var second = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "After");
        Assert.That(second.HasChangedFrom(first, key), Is.True);
        target.transform.localPosition = Vector3.one;
        var moved = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "Moved");
        Assert.That(moved.HasChangedFrom(second, key), Is.True);
      }
      finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void DisabledPhysBoneColliderUnderInactiveRootRemainsInSnapshot()
    {
      var collider = avatar.AddComponent<VRCPhysBoneCollider>();
      collider.enabled = false;
      avatar.SetActive(false);
      string key = "VRC/PhysBone/" + collider.GetInstanceID();
      var first = AvatarColliderMonitor.CaptureSnapshot(avatar, "Manual", "Disabled");
      Assert.That(first.paths, Contains.Key(key));
      avatar.SetActive(true);
      Assert.That(AvatarColliderMonitor.CaptureSnapshot(avatar, "Manual", "Enabled")
        .HasChangedFrom(first, key), Is.True);
    }

    [Test]
    public void DisabledAutomaticMonitoringStillAllowsManualSnapshots()
    {
      new AvatarColliderMonitorPreCallback().OnPreprocessAvatar(avatar);
      new AvatarColliderMonitorLateCallback().OnPreprocessAvatar(avatar);
      Assert.That(AvatarColliderMonitor.GetSnapshots(), Is.Empty);
      AvatarColliderMonitor.RecordSnapshot(avatar.GetComponent<VRCAvatarDescriptor>(), "Manual", "User");
      Assert.That(AvatarColliderMonitor.GetSnapshots(), Has.Count.EqualTo(1));
    }
  }
}
