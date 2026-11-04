using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Removes authoring configuration from Unity's build-scene copy.</summary>
    public sealed class StandaloneConfigurationStripper : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var config in root.GetComponentsInChildren<ASSComponent>(true))
                Object.DestroyImmediate(config);
        }
    }
}
