using NUnit.Framework;
using UnityEngine;

namespace UnityBox.AvatarColliderMonitor.Tests
{
    public class ColliderHistoryWorkflowTests
    {
        private GameObject root;
        private bool previousLogs;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("HistoryRoot");
            previousLogs = AvatarColliderMonitor.LogChanges;
            AvatarColliderMonitor.LogChanges = false;
            AvatarColliderMonitor.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            AvatarColliderMonitor.Clear();
            AvatarColliderMonitor.LogChanges = previousLogs;
        }

        [Test]
        public void PreviewReusesSnapshotUntilRefreshAndThenCapturesChangedValues()
        {
            var collider = root.AddComponent<BoxCollider>();
            var preview = new ColliderSnapshotPreview();
            var first = preview.Get(root);
            collider.size = Vector3.one * 3;
            Assert.That(preview.Get(root), Is.SameAs(first));
            preview.Invalidate();
            var refreshed = preview.Get(root);
            Assert.That(refreshed, Is.Not.SameAs(first));
            Assert.That(refreshed.HasChangedFrom(first, "Unity/" + collider.GetInstanceID()), Is.True);
            Assert.That(preview.Get(null), Is.Null);
        }

        [Test]
        public void HistoryComparisonsAndStatisticsAreCachedUntilHistoryChanges()
        {
            var collider = root.AddComponent<SphereCollider>();
            AvatarColliderMonitor.RecordSnapshot(root, "Manual", "Before");
            collider.radius = 2;
            AvatarColliderMonitor.RecordSnapshot(root, "Manual", "After");
            var first = AvatarColliderMonitor.GetHistoryComparisons();
            Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(first[0].Previous, Is.Null);
            Assert.That(first[1].ChangedKeys, Contains.Item("Unity/" + collider.GetInstanceID()));
            Assert.That(AvatarColliderMonitor.GetHistoryComparisons(), Is.SameAs(first));
            var counts = AvatarColliderMonitor.GetChangeStatistics();
            Assert.That(AvatarColliderMonitor.GetChangeStatistics(), Is.SameAs(counts));
            Assert.That(AvatarColliderMonitor.TotalChanges, Is.EqualTo(1));
            Assert.That(counts[0].Count, Is.EqualTo(1));
            AvatarColliderMonitor.Clear();
            Assert.That(AvatarColliderMonitor.GetHistoryComparisons(), Is.Empty);
            Assert.That(AvatarColliderMonitor.TotalChanges, Is.Zero);
        }

        [Test]
        public void ExportImportPreservesFrozenHistoryAndBoundsWithoutReattachingLiveObjects()
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(2, 4, 6);
            root.transform.position = new Vector3(10, 20, 30);
            Physics.SyncTransforms();
            AvatarColliderMonitor.RecordSnapshot(root, "Manual", "Export");
            var before = AvatarColliderMonitor.GetSnapshots()[0];
            string key = "Unity/" + collider.GetInstanceID();
            string json = AvatarColliderMonitor.ExportHistoryJson();
            AvatarColliderMonitor.Clear();
            AvatarColliderMonitor.ImportHistoryJson(json);
            var after = AvatarColliderMonitor.GetSnapshots()[0];
            Assert.That(after.GetColliderPath(key), Is.EqualTo(before.GetColliderPath(key)));
            Assert.That(after.configurations[key], Is.EqualTo(before.configurations[key]));
            Assert.That(after.timestamp, Is.EqualTo(before.timestamp));
            Assert.That(after.worldBounds[key], Is.EqualTo(before.worldBounds[key]));
            Assert.That(after.worldBounds[key].center, Is.EqualTo(root.transform.position));
            Assert.That(after.worldBounds[key].size, Is.EqualTo(collider.size));
            Assert.That(after.colliders[key], Is.Null);
            Assert.That(after.sessionId, Is.EqualTo(before.sessionId));
            Assert.That(after.HasChangedFrom(before, key), Is.False);
        }

        [Test]
        public void MeshGeometryChangesAreDetectedWithTheSameSharedMeshReference()
        {
            var mesh = new Mesh { name = "MutableColliderMesh" };
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                var collider = root.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                Physics.SyncTransforms();
                var before = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "Before");
                mesh.vertices = new[] { Vector3.zero, Vector3.right * 4, Vector3.up * 3 };
                mesh.RecalculateBounds();
                collider.sharedMesh = null; collider.sharedMesh = mesh;
                Physics.SyncTransforms();
                var after = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "After");
                string key = "Unity/" + collider.GetInstanceID();
                Assert.That(after.worldBounds[key].size, Is.Not.EqualTo(before.worldBounds[key].size));
                Assert.That(after.HasChangedFrom(before, key), Is.True);
                Assert.That(collider.sharedMesh, Is.SameAs(mesh));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ImportedOtherSessionDoesNotJoinCurrentRootDespiteMatchingInstanceId()
        {
            var collider = root.AddComponent<BoxCollider>();
            var old = AvatarColliderMonitor.CaptureSnapshot(root, "Manual", "Old");
            old.sessionId = "another-editor-session";
            AvatarColliderMonitor.GetSnapshots().Add(old);
            collider.size = Vector3.one * 2;
            AvatarColliderMonitor.RecordSnapshot(root, "Manual", "New");
            Assert.That(AvatarColliderMonitor.GetPreviousSnapshot(1), Is.Null);
            Assert.That(AvatarColliderMonitor.GetHistoryComparisons()[1].Previous, Is.Null);
            Assert.That(AvatarColliderMonitor.TotalChanges, Is.Zero);
        }

        [Test]
        public void HistoryLimitAndImportedAppendKeepNewestThousandRecords()
        {
            for (int i = 0; i < 1005; i++)
                AvatarColliderMonitor.RecordSnapshot(root, "Manual", i.ToString());
            Assert.That(AvatarColliderMonitor.GetSnapshots(), Has.Count.EqualTo(1000));
            Assert.That(AvatarColliderMonitor.GetSnapshots()[0].timing, Is.EqualTo("5"));
            string json = AvatarColliderMonitor.ExportHistoryJson();
            AvatarColliderMonitor.ImportHistoryJson(json, true);
            Assert.That(AvatarColliderMonitor.GetSnapshots(), Has.Count.EqualTo(1000));
            Assert.That(AvatarColliderMonitor.GetSnapshots()[999].timing, Is.EqualTo("1004"));
        }
    }
}
