using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace UnityBox.AnimatorOptimizer
{
    // Unity supplies a build-time scene copy; authoring scenes and prefabs remain untouched.
    public sealed class AnimatorOptimizerSceneBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
                AnimatorOptimizerPlatform.StripConfigurationComponents(root);
        }
    }
}
