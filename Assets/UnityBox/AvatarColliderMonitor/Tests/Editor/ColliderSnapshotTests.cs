using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AvatarColliderMonitor.Tests
{
  public class ColliderSnapshotTests
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
      var field = AvatarColliderMonitor.GetColliderFields().First();
      var config = (VRCAvatarDescriptor.ColliderConfig)field.GetValue(descriptor);
      config.transform = bone.transform;
      field.SetValue(descriptor, config);
      AvatarColliderMonitor.RecordSnapshot(descriptor, "Manual", "Before");
      var first = AvatarColliderMonitor.GetSnapshots()[0];
      string key = AvatarColliderMonitor.FieldToKey(field);
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
      var field = AvatarColliderMonitor.GetColliderFields().First();
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
      Assert.That(records[1].HasChangedFrom(records[0], AvatarColliderMonitor.FieldToKey(field)), Is.True);
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
        foreach (var field in AvatarColliderMonitor.GetColliderFields())
          Assert.That(records[1].HasChangedFrom(records[0], AvatarColliderMonitor.FieldToKey(field)), Is.False);
      }
      finally { Object.DestroyImmediate(other); }
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
