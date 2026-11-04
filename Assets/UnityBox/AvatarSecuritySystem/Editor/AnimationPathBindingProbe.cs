using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class AnimationPathBindingProbeBuilder
    {
        private const string OldMarkerName = "ASS_Marker_OLD";
        private const string NewMarkerName = "ASS_Marker_NEW";
        private const string ProbeStateName = "PathProbe";

        [MenuItem("Tools/UnityBox/ASS/Animation Path Binding Probe/Create & Run")]
        private static void CreateAndRun()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            const string folder = "Assets/UnityBox/AvatarSecuritySystem/Tests";
            const string scenePath = folder + "/AnimationPathBindingProbe.unity";
            const string clipPath = folder + "/AnimationPathBindingProbe.anim";
            const string controllerPath = folder + "/AnimationPathBindingProbe.controller";

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "UnityBox/AvatarSecuritySystem/Tests"));
            AssetDatabase.Refresh();

            DeleteAssetIfExists(scenePath);
            DeleteAssetIfExists(clipPath);
            DeleteAssetIfExists(controllerPath);

            var clip = new AnimationClip
            {
                name = "AnimationPathBindingProbe"
            };
            clip.SetCurve(
                OldMarkerName,
                typeof(Transform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0f, 1f, 0.2f, 2f));
            clip.SetCurve(
                "PlainMarker",
                typeof(Transform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0f, 1f, 0.2f, 2f));
            AssetDatabase.CreateAsset(clip, clipPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var layer = controller.layers[0];
            var state = layer.stateMachine.AddState(ProbeStateName);
            state.motion = clip;
            layer.stateMachine.defaultState = state;
            controller.layers = new[] { layer };
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ASS_AnimationPathBindingProbe");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var markerObject = new GameObject(OldMarkerName);
            markerObject.transform.SetParent(root.transform, false);
            markerObject.transform.localScale = Vector3.one;

            var plainMarkerObject = new GameObject("PlainMarker");
            plainMarkerObject.transform.SetParent(root.transform, false);
            plainMarkerObject.transform.localScale = Vector3.one;

            var probe = root.AddComponent<UnityBox.AvatarSecuritySystem.AnimationPathBindingProbe>();
            if (probe == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("[ASS Probe] Runtime probe component could not be added. Check script compilation.");
            }
            probe.marker = markerObject.transform;
            probe.plainMarker = plainMarkerObject.transform;
            probe.animator = animator;
            probe.mode = UnityBox.AvatarSecuritySystem.AnimationPathBindingProbe.ProbeMode.RuntimeRename;

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[ASS Probe] Created test scene and assets. Entering Play Mode.");
            EditorApplication.isPlaying = true;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }
    }
}