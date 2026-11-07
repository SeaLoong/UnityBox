using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

public static class AssWdAudit
{
    [Serializable] public class Row
    {
        public bool userWriteDefaults;
        public string neutralMotion, addedAssMode;
        public float initial, expression, neutral;
        public string[] assBindings;
        public int assStates, assWdOnStates;
    }
    [Serializable] public class Report
    {
        public string unity;
        public bool completed;
        public string error;
        public List<Row> rows = new List<Row>();
    }
    public static void Run()
    {
        var report = new Report { unity = Application.unityVersion };
        string folder = "Assets/__AssWdAudit_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        try
        {
            foreach (bool userWd in new[] { true, false })
            foreach (string neutral in new[] { "empty-clip", "null-motion", "explicit-zero" })
            foreach (string ass in new[] { "none", "current-off", "ass-only-on" })
                report.rows.Add(Evaluate(folder, report.rows.Count, userWd, neutral, ass));
            report.completed = true;
        }
        catch (Exception e) { report.error = e.ToString(); Debug.LogException(e); }
        finally
        {
            File.WriteAllText("/workspace/unitybox-setup/unity/wd-audit/result.json", JsonUtility.ToJson(report, true));
            AssetDatabase.DeleteAsset(folder);
            EditorApplication.Exit(report.completed ? 0 : 1);
        }
    }
    private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var state in machine.states) yield return state.state;
        foreach (var nested in machine.stateMachines)
        foreach (var state in States(nested.stateMachine)) yield return state;
    }
    private static Row Evaluate(string folder, int index, bool wd, string neutralMode, string assMode)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Audit" + index);
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var face = new GameObject("Face"); face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var zero = new Vector3[3];
            mesh.AddBlendShapeFrame("Expression", 100f, zero, zero, zero);
            AssetDatabase.CreateAsset(mesh, folder + "/Mesh" + index + ".asset");
            renderer.sharedMesh = mesh;
            var source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source" + index + ".controller");
            var empty = new AnimationClip { name = "Empty" };
            AssetDatabase.CreateAsset(empty, folder + "/Empty" + index + ".anim");
            var baseState = source.layers[0].stateMachine.AddState("Base");
            baseState.writeDefaultValues = wd; baseState.motion = empty;
            source.layers[0].stateMachine.defaultState = baseState;
            source.AddLayer("Face");
            var layers = source.layers; layers[1].defaultWeight = 1f; source.layers = layers;
            var machine = source.layers[1].stateMachine;
            var expression = machine.AddState("Expression"); expression.writeDefaultValues = wd;
            var exprClip = new AnimationClip { name = "Expression" };
            exprClip.SetCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Expression", AnimationCurve.Constant(0f, 1f, 85f));
            AssetDatabase.CreateAsset(exprClip, folder + "/Expression" + index + ".anim");
            expression.motion = exprClip;
            var neutral = machine.AddState("Neutral"); neutral.writeDefaultValues = wd;
            if (neutralMode == "empty-clip") neutral.motion = empty;
            else if (neutralMode == "explicit-zero")
            {
                var clip = new AnimationClip { name = "Neutral" };
                clip.SetCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Expression", AnimationCurve.Constant(0f, 1f, 0f));
                AssetDatabase.CreateAsset(clip, folder + "/Neutral" + index + ".anim");
                neutral.motion = clip;
            }
            machine.defaultState = neutral;
            AssetDatabase.SaveAssets();
            var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = source;
            var output = source;
            if (assMode != "none")
            {
                var config = new ASSConfigData
                {
                    disableObfuscation = true, disableDefense = true, disableOverlay = true,
                    disableWarningSound = true, gesturePassword = new List<int> { 1, 2 },
                    countdownDuration = 30f, gestureHoldTime = 0.5f, gestureErrorTolerance = 0.3f,
                };
                output = StandaloneSecurityBuilder.Build(root, config, folder + "/Output" + index + ".controller");
                if (assMode == "ass-only-on")
                    foreach (var layer in output.layers.Skip(2))
                    foreach (var state in States(layer.stateMachine)) state.writeDefaultValues = true;
            }
            var row = new Row { userWriteDefaults = wd, neutralMotion = neutralMode, addedAssMode = assMode };
            var assLayers = output.layers.Skip(2).ToArray();
            row.assStates = assLayers.SelectMany(layer => States(layer.stateMachine)).Count();
            row.assWdOnStates = assLayers.SelectMany(layer => States(layer.stateMachine)).Count(state => state.writeDefaultValues);
            row.assBindings = assLayers.SelectMany(layer => States(layer.stateMachine)).Select(state => state.motion).OfType<AnimationClip>()
                .SelectMany(AnimationUtility.GetCurveBindings).Select(binding => binding.path + "|" + binding.type.Name + "|" + binding.propertyName).Distinct().OrderBy(s => s).ToArray();
            animator.runtimeAnimatorController = null;
            renderer.SetBlendShapeWeight(0, 0f);
            animator.runtimeAnimatorController = output;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            row.initial = renderer.GetBlendShapeWeight(0);
            animator.Play("Face.Expression", 1, 0f);
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            row.expression = renderer.GetBlendShapeWeight(0);
            animator.Play("Face.Neutral", 1, 0f);
            for (int frame = 0; frame < 8; frame++) animator.Update(1f / 60f);
            row.neutral = renderer.GetBlendShapeWeight(0);
            if (Math.Abs(row.expression - 85f) > 0.001f)
                throw new Exception("Expression did not evaluate: " + JsonUtility.ToJson(row));
            return row;
        }
        finally
        {
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
