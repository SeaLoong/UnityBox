using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.VisemeBlendshapeOverride
{
    public sealed class VisemeSceneBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<VisemeBlendshapeOverrideComponent>(true))
                Object.DestroyImmediate(component);
        }
    }
}
