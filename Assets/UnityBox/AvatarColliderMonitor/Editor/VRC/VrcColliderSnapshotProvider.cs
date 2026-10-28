using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.AvatarColliderMonitor
{
  [InitializeOnLoad]
  public static class VrcColliderSnapshotProvider
  {
    private static FieldInfo[] fields;
    static VrcColliderSnapshotProvider() => Register();

    public static void Register()
    {
      AvatarColliderMonitor.CollectAdditionalColliders -= Capture;
      AvatarColliderMonitor.CollectAdditionalColliders += Capture;
    }

    public static FieldInfo[] GetColliderFields()
    {
      if (fields == null)
        fields = typeof(VRCAvatarDescriptor)
          .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          .Where(field => field.Name.StartsWith("collider_") &&
            field.FieldType == typeof(VRCAvatarDescriptor.ColliderConfig))
          .OrderBy(field => field.MetadataToken).ToArray();
      return fields;
    }

    public static string FieldToKey(FieldInfo field) => "VRC/" + field.Name.Substring("collider_".Length);

    [Serializable]
    private sealed class PhysBoneConfiguration
    {
      public string component;
      public Matrix4x4 localToWorld;
      public bool activeInHierarchy, enabled;
    }

    private static void Capture(GameObject root, ColliderSnapshot snapshot)
    {
      var descriptor = root.GetComponent<VRCAvatarDescriptor>();
      if (descriptor != null)
        foreach (var field in GetColliderFields())
        {
          var config = (VRCAvatarDescriptor.ColliderConfig)field.GetValue(descriptor);
          snapshot.Add(FieldToKey(field), "VRC · " + ObjectNames.NicifyVariableName(field.Name.Substring("collider_".Length)),
            config.transform, JsonUtility.ToJson(config));
        }
      foreach (var collider in root.GetComponentsInChildren<VRCPhysBoneCollider>(true))
      {
        var target = collider.rootTransform != null ? collider.rootTransform : collider.transform;
        var configuration = new PhysBoneConfiguration
        {
          component = EditorJsonUtility.ToJson(collider), localToWorld = target.localToWorldMatrix,
          activeInHierarchy = collider.gameObject.activeInHierarchy, enabled = collider.enabled,
        };
        snapshot.Add("VRC/PhysBone/" + collider.GetInstanceID(),
          "PhysBoneCollider · " + ColliderSnapshot.CapturePath(collider.transform), target,
          JsonUtility.ToJson(configuration));
      }
    }
  }

  public class AvatarColliderMonitorPreCallback : IVRCSDKPreprocessAvatarCallback
  {
    public int callbackOrder => int.MinValue;
    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
      AvatarColliderMonitor.RecordSnapshot(avatarGameObject, "VRChatSDK", "EarlyPreprocess");
      return true;
    }
  }

  public class AvatarColliderMonitorLateCallback : IVRCSDKPreprocessAvatarCallback
  {
    public int callbackOrder => int.MaxValue;
    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
      AvatarColliderMonitor.RecordSnapshot(avatarGameObject, "VRChatSDK", "LatePreprocess");
      return true;
    }
  }
}
