using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;
public static class AssFeedbackAudit
{
    [Serializable] public class Point
    {
        public string mode, phase;
        public bool overlay, warning, success, correct, timedOut;
        public float materialProgress, blockProgress, countdownWeight; public string countdownState;
    }
    [Serializable] public class Report { public bool passed; public string error; public List<Point> points = new List<Point>(); }
    static Report report = new Report();
    public static void Run()
    {
        string folder = "Assets/__ASSFeedbackAudit_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        try { Evaluate(folder, false); Evaluate(folder, true); report.passed = true; }
        catch(Exception e) { report.error = e.ToString(); Debug.LogException(e); }
        finally
        {
            File.WriteAllText("/workspace/unitybox-setup/unity/wdon-feedback/result.json", JsonUtility.ToJson(report,true));
            AssetDatabase.DeleteAsset(folder); EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
    static IEnumerable<AnimatorState> States(AnimatorStateMachine sm)
    {
        foreach(var child in sm.states) yield return child.state;
        foreach(var nested in sm.stateMachines) foreach(var state in States(nested.stateMachine)) yield return state;
    }
    static void Evaluate(string folder, bool on)
    {
        var scene=EditorSceneManager.NewPreviewScene(); var root=new GameObject("Feedback"); SceneManager.MoveGameObjectToScene(root,scene);
        try
        {
            var body=new GameObject("Body");body.transform.SetParent(root.transform,false);
            var animator=root.AddComponent<Animator>();
            var config=new ASSConfigData { disableObfuscation=true,disableDefense=true,disableOverlay=false,disableWarningSound=false,
                gesturePassword=new List<int>{1,2},gestureHoldTime=0.12f,gestureErrorTolerance=0.04f,countdownDuration=2f,warningThreshold=1f };
            var output=StandaloneSecurityBuilder.Build(root,config,folder+"/"+(on?"On":"Off")+".controller");
            foreach(var layer in output.layers)foreach(var state in States(layer.stateMachine))state.writeDefaultValues=on;
            animator.runtimeAnimatorController=null;animator.runtimeAnimatorController=output;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
            Action<int> advance=frames=>{for(int i=0;i<frames;i++)animator.Update(1f/60f);};
            var overlay=root.transform.Find(Constants.GO_OVERLAY).gameObject;
            var renderer=overlay.GetComponentInChildren<MeshRenderer>(true);
            var warning=root.transform.Find(Constants.GO_AUDIO_WARNING).gameObject;var success=root.transform.Find(Constants.GO_AUDIO_SUCCESS).gameObject;
            Action<string> record=phase=>{var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);report.points.Add(new Point{
                mode=on?"on":"off",phase=phase,overlay=overlay.activeSelf,warning=warning.activeSelf,success=success.activeSelf,
                correct=animator.GetBool(Constants.PARAM_PASSWORD_CORRECT),timedOut=animator.GetBool(Constants.PARAM_TIME_UP),
                materialProgress=renderer.sharedMaterial.GetFloat("_C9D4"),blockProgress=block.GetFloat("_C9D4"), countdownWeight=animator.GetLayerWeight(Array.FindIndex(output.layers,l=>l.name==Constants.LAYER_COUNTDOWN)), countdownState=animator.GetCurrentAnimatorStateInfo(Array.FindIndex(output.layers,l=>l.name==Constants.LAYER_COUNTDOWN)).fullPathHash.ToString()});};
            advance(5);record("input-start");if(!overlay.activeSelf)throw new Exception("Overlay must show during input");
            advance(25);record("countdown");
            animator.SetInteger(Constants.PARAM_GESTURE_LEFT,1);advance(20);animator.SetInteger(Constants.PARAM_GESTURE_LEFT,2);advance(20);
            record("success");if(!animator.GetBool(Constants.PARAM_PASSWORD_CORRECT))throw new Exception("Password did not succeed");
            advance(180);record("released");if(overlay.activeSelf||warning.activeSelf||success.activeSelf)throw new Exception("Feedback did not release");
            animator.SetInteger(Constants.PARAM_GESTURE_LEFT,0);root.GetComponent<StandaloneSecurityInput>().Restart();advance(6);record("restart");
            if(!overlay.activeSelf||animator.GetBool(Constants.PARAM_PASSWORD_CORRECT))throw new Exception("Restart did not restore input");
            advance(160);record("timeout");if(!animator.GetBool(Constants.PARAM_TIME_UP))throw new Exception("Timeout did not occur");
            root.GetComponent<StandaloneSecurityInput>().Restart();advance(6);record("timeout-restart");
            if(!overlay.activeSelf||animator.GetBool(Constants.PARAM_TIME_UP))throw new Exception("Timeout restart did not restore input");
            advance(24);record("timeout-restart-later");
            if(!warning.activeSelf)throw new Exception("Warning audio did not rearm");
            var progress=new MaterialPropertyBlock();renderer.GetPropertyBlock(progress);
            if(progress.GetFloat("_C9D4")>=0.99f)throw new Exception("Countdown progress did not restart");
            advance(160);record("second-timeout");
            if(!animator.GetBool(Constants.PARAM_TIME_UP))throw new Exception("Second timeout did not occur");
        }
        finally { Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene); }
    }
}
