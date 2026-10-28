using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeControllerCopyTests
    {
        private string folder;
        private readonly List<UnityEngine.Object> transient = new List<UnityEngine.Object>();

        [SetUp] public void SetUp()
        {
            folder = "Assets/VisemeCopy_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        }

        [TearDown] public void TearDown()
        {
            AssetDatabase.DeleteAsset(folder);
            foreach (var item in transient.Where(item => item != null && !EditorUtility.IsPersistent(item)))
                UnityEngine.Object.DestroyImmediate(item);
            transient.Clear();
        }

        [Test]
        public void OverrideCopyPreservesClipIdentityAndIndependentBlendTree()
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Source.controller");
            controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            var a = Clip("Same name", 10f, "/A.anim");
            var b = Clip("Same name", 20f, "/B.anim");
            var replacement = Clip("Same name", 75f, "/Replacement.anim");
            var state = controller.layers[0].stateMachine.AddState("Original");
            var tree = new BlendTree { name = "Shared source tree", blendParameter = "Blend", blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false };
            tree.AddChild(a, 0f);
            tree.AddChild(b, 1f);
            AssetDatabase.AddObjectToAsset(tree, controller);
            state.motion = tree;
            controller.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();
            var bytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(controller));
            var source = Track(new AnimatorOverrideController(controller));
            source[a] = replacement;

            var copy = VisemeControllerCopy.CreateEditableCopy(source, folder + "/Copy.controller");
            var copiedState = copy.layers[0].stateMachine.defaultState;
            var copiedTree = (BlendTree)copiedState.motion;
            Assert.That(copiedTree, Is.Not.SameAs(tree));
            Assert.That(copiedTree.children[0].motion, Is.SameAs(replacement));
            Assert.That(copiedTree.children[1].motion, Is.SameAs(b), "Duplicate clip names do not share overrides.");
            Assert.That(tree.children[0].motion, Is.SameAs(a));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(controller)), Is.EqualTo(bytes));
            Assert.That(AssetDatabase.GetAssetPath(copiedState), Is.EqualTo(folder + "/Copy.controller"));
            Assert.That(AssetDatabase.GetAssetPath(copiedTree), Is.EqualTo(folder + "/Copy.controller"));
        }

        [Test]
        public void OverridesCanSwapTwoClipsWithoutApplyingOverridesTwice()
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Swap.controller");
            var a = Clip("A", 10f, "/A.anim");
            var b = Clip("B", 20f, "/B.anim");
            controller.layers[0].stateMachine.AddState("A").motion = a;
            controller.layers[0].stateMachine.AddState("B").motion = b;
            var source = Track(new AnimatorOverrideController(controller));
            source.ApplyOverrides(new[] { new KeyValuePair<AnimationClip, AnimationClip>(a, b),
                new KeyValuePair<AnimationClip, AnimationClip>(b, a) });

            var copy = VisemeControllerCopy.CreateEditableCopy(source, folder + "/Swapped.controller");

            Assert.That(copy.layers[0].stateMachine.states.Single(child => child.state.name == "A").state.motion, Is.SameAs(b));
            Assert.That(copy.layers[0].stateMachine.states.Single(child => child.state.name == "B").state.motion, Is.SameAs(a));
        }

        [Test]
        public void MemoryGraphsAndTransientClipsBecomeSelfContainedAssets()
        {
            var controller = Track(new AnimatorController { name = "Transient source" });
            var machine = Track(new AnimatorStateMachine());
            var state = Track(new AnimatorState { name = "Transient state" });
            var clip = Track(Clip("Transient clip", 42f, null));
            state.motion = clip;
            machine.states = new[] { new ChildAnimatorState { state = state } };
            machine.defaultState = state;
            controller.layers = new[] { new AnimatorControllerLayer { name = "Memory", stateMachine = machine,
                syncedLayerIndex = -1, defaultWeight = 1f } };

            var copy = VisemeControllerCopy.CreateEditableCopy(controller, folder + "/Memory.controller");
            var copiedState = copy.layers[0].stateMachine.defaultState;
            Assert.That(copiedState, Is.Not.SameAs(state));
            Assert.That(copiedState.motion, Is.Not.SameAs(clip));
            Assert.That(AssetDatabase.GetAssetPath(copiedState.motion), Is.EqualTo(folder + "/Memory.controller"));
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)copiedState.motion,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.A")).Evaluate(0f), Is.EqualTo(42f));
            Assert.That(controller.layers[0].stateMachine, Is.SameAs(machine));
            Assert.That(state.motion, Is.SameAs(clip));
        }

        private AnimationClip Clip(string name, float value, string suffix)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.A"), AnimationCurve.Constant(0f, 1f, value));
            if (suffix != null) AssetDatabase.CreateAsset(clip, folder + suffix);
            return clip;
        }

        private T Track<T>(T item) where T : UnityEngine.Object { transient.Add(item); return item; }
    }
}
