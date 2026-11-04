using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeCoreTests
    {
        private GameObject root;
        private Mesh mesh;
        private SkinnedMeshRenderer renderer;
        private VisemeBlendshapeOverrideComponent config;
        private string controllerPath;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("VisemeCore");
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            renderer = face.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { vertices = new[] { Vector3.zero } };
            mesh.AddBlendShapeFrame("A", 100f, new[] { Vector3.up }, null, null);
            mesh.AddBlendShapeFrame("B", 100f, new[] { Vector3.right }, null, null);
            renderer.sharedMesh = mesh;
            config = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.targetRenderer = renderer;
            config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP).blendshapeName = "A";
            config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.FF).blendshapeName = "B";
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Disabled;
            controllerPath = "Assets/VisemeCore_" + Guid.NewGuid().ToString("N") + ".controller";
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(controllerPath);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SerializedPhonemeNumbersAndOverridesSurviveNormalization()
        {
            JsonUtility.FromJsonOverwrite("{\"bindings\":[{\"viseme\":1,\"blendshapeName\":\"A\",\"weight\":42}]}", config);
            config.EnsureBindings();
            Assert.That(config.bindings, Has.Count.EqualTo(15));
            Assert.That(config.bindings.Select(binding => (int)binding.viseme), Is.EqualTo(Enumerable.Range(0, 15)));
            Assert.That(config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP).blendshapeName, Is.EqualTo("A"));
            Assert.That(config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP).weight, Is.EqualTo(42));
        }

        [Test]
        public void OptionalDefaultMappingsStayLiveAndExplicitNoneWins()
        {
            var mapping = new string[15];
            mapping[1] = "A";
            var binding = config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP);
            binding.blendshapeName = "";
            Assert.That(config.ResolveBlendshapeName(mapping, binding), Is.EqualTo("A"));
            mapping[1] = "B";
            Assert.That(config.ResolveBlendshapeName(mapping, binding), Is.EqualTo("B"));
            Assert.That(binding.blendshapeName, Is.Empty);
            binding.blendshapeName = VisemeBlendshapeOverrideComponent.NoneBlendshapeValue;
            Assert.That(config.ResolveBlendshapeName(mapping, binding), Is.Empty);
        }

        [Test]
        public void BuildsPersistentUnityControllerWithoutPlatformDescriptor()
        {
            config.globalWeight = 35f;
            var controller = Build();
            var layer = controller.layers.Single(item => item.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            Assert.That(layer.stateMachine.states, Has.Length.EqualTo(15));
            Assert.That(layer.stateMachine.anyStateTransitions, Has.Length.EqualTo(15));
            Assert.That(controller.parameters.Single(parameter => parameter.name == "Viseme").type,
                Is.EqualTo(AnimatorControllerParameterType.Int));
            var state = GetState(controller, VisemeBlendshapeOverrideComponent.Viseme.PP);
            AssertCurve((AnimationClip)state.motion, "A", 35f);
            AssertCurve((AnimationClip)state.motion, "B", 0f);
            var assets = AssetDatabase.LoadAllAssetsAtPath(controllerPath);
            Assert.That(assets, Does.Contain(state));
            Assert.That(assets, Does.Contain(state.motion));
            foreach (var transition in layer.stateMachine.anyStateTransitions)
                Assert.That(assets, Does.Contain(transition));
        }

        [Test]
        public void VoiceTreeUsesNormalizedThresholdsAndZeroesEveryControlledShape()
        {
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Linear;
            config.voiceMin = 0.2f;
            config.voiceMax = 0.8f;
            var controller = Build();
            var tree = (BlendTree)GetState(controller, VisemeBlendshapeOverrideComponent.Viseme.PP).motion;
            Assert.That(tree.blendParameter, Is.EqualTo("Voice"));
            Assert.That(tree.children.Select(child => child.threshold), Is.EqualTo(new[] { 0.2f, 0.8f }));
            AssertCurve((AnimationClip)tree.children[0].motion, "A", 0f);
            AssertCurve((AnimationClip)tree.children[0].motion, "B", 0f);
            AssertCurve((AnimationClip)tree.children[1].motion, "A", 100f);
        }

        [Test]
        public void RebuildingKeepsOneGeneratedLayerAndUnrelatedLayers()
        {
            var controller = Build();
            var original = controller.layers[0].stateMachine;
            config.globalWeight = 15f;
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
            Assert.That(controller.layers.Count(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName), Is.EqualTo(1));
            Assert.That(controller.layers[0].stateMachine, Is.SameAs(original));
            AssertCurve((AnimationClip)GetState(controller, VisemeBlendshapeOverrideComponent.Viseme.PP).motion, "A", 15f);
        }

        [Test]
        public void RebuildingRetainsSharedGeneratedMotionsAndTheirNativeOutputAfterReload()
        {
            var controller = Build();
            var sharedClip = (AnimationClip)GetState(controller, VisemeBlendshapeOverrideComponent.Viseme.PP).motion;
            sharedClip.SetCurve("Marker", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 3f));
            EditorUtility.SetDirty(sharedClip);
            controller.AddLayer("User animation");
            var userLayer = controller.layers.Last();
            var userState = userLayer.stateMachine.AddState("Uses shared generated clip");
            userState.writeDefaultValues = false;
            userState.motion = sharedClip;
            userLayer.stateMachine.defaultState = userState;
            userLayer.defaultWeight = 1f;
            var layers = controller.layers; layers[layers.Length - 1] = userLayer; controller.layers = layers;
            AssetDatabase.SaveAssets();
            config.globalWeight = 15f;
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
            AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var restoredClip = controller.layers.Single(layer => layer.name == "User animation")
                .stateMachine.defaultState.motion as AnimationClip;
            Assert.That(restoredClip, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(restoredClip), Is.EqualTo(controllerPath));
            var marker = new GameObject("Marker"); marker.transform.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            for (int frame = 0; frame < 5; frame++) animator.Update(1f / 60f);
            Assert.That(marker.transform.localPosition.x, Is.EqualTo(3f).Within(0.001f));
            AssertCurve((AnimationClip)GetState(controller, VisemeBlendshapeOverrideComponent.Viseme.PP).motion, "A", 15f);
        }

        [Test]
        public void RebuildingKeepsUnrelatedPrefixedAssetsAndReclaimsTheOldExclusiveGraph()
        {
            var controller = Build();
            var unrelated = new AnimationClip { name = VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + "UserAsset" };
            AssetDatabase.AddObjectToAsset(unrelated, controller);
            AssetDatabase.SaveAssets();
            int assetCount = AssetDatabase.LoadAllAssetsAtPath(controllerPath).Length;
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            for (int generation = 0; generation < 3; generation++)
            {
                VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
                var assets = AssetDatabase.LoadAllAssetsAtPath(controllerPath);
                Assert.That(assets, Does.Contain(unrelated));
                Assert.That(assets.Length, Is.EqualTo(assetCount), "Exclusive old generated objects are reclaimed.");
            }
        }

        [Test]
        public void DisabledConfigurationCannotCreatePlan()
        {
            config.enabled = false;
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out _), Is.False);
        }

        [TestCase(1f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(float.NaN, float.NaN)]
        public void InvalidVoiceRangesBecomeFiniteDistinctThresholds(float min, float max)
        {
            VisemeBlendshapeOverrideUtils.NormalizeVoiceRange(ref min, ref max);
            Assert.That(float.IsNaN(min) || float.IsNaN(max), Is.False);
            Assert.That(min, Is.InRange(0f, 1f));
            Assert.That(max, Is.InRange(0f, 1f));
            Assert.That(max, Is.GreaterThan(min));
        }

        private AnimatorController Build()
        {
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
            return controller;
        }

        private static AnimatorState GetState(AnimatorController controller, VisemeBlendshapeOverrideComponent.Viseme viseme)
        {
            return controller.layers.Single(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName)
                .stateMachine.states.Single(state => state.state.name == VisemeBlendshapeOverrideUtils.GeneratedAssetPrefix + viseme).state;
        }

        private static void AssertCurve(AnimationClip clip, string shape, float expected)
        {
            var curve = AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape." + shape));
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.Evaluate(0f), Is.EqualTo(expected));
        }
    }
}
