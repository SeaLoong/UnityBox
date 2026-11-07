using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>Generates ordinary Unity build-scene controllers, then removes authoring components.</summary>
    public sealed class StandaloneConfigurationStripper : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report != null) ProcessSceneForBuild(scene);
        }

        public static int ProcessSceneForBuild(Scene scene)
        {
            var authoring = new List<ASSComponent>();
            foreach (var root in scene.GetRootGameObjects())
                authoring.AddRange(root.GetComponentsInChildren<ASSComponent>(true));
            var targets = new HashSet<GameObject>();
            int generated = 0;
            foreach (var component in authoring)
            {
                var config = ASSConfigData.FromComponent(component);
                if (config.gesturePassword != null && config.gesturePassword.Count > 0)
                {
                    var animator = component.GetComponent<Animator>() ?? component.GetComponentInParent<Animator>();
                    var target = animator != null ? animator.gameObject : component.gameObject;
                    if (!targets.Add(target))
                        throw new InvalidOperationException(string.Format(I18n.T("build.duplicate_configuration"), target.name));
                    string folder = Constants.ASSET_FOLDER + "/StandaloneBuild_" + Guid.NewGuid().ToString("N");
                    Directory.CreateDirectory(folder);
                    AssetDatabase.Refresh();
                    var controller = StandaloneSecurityBuilder.Build(target, config, folder + "/Controller.controller");
                    (animator != null ? animator : target.AddComponent<Animator>()).runtimeAnimatorController = controller;
                    generated++;
                }
                Object.DestroyImmediate(component);
            }
            return generated;
        }
    }
}
