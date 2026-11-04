using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeAnimatorEvaluationTests
    {
        private Scene scene;
        private GameObject root;
        private Mesh mesh;
        private string folder;
        private PlayableGraph graph;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Viseme runtime evaluation");
            SceneManager.MoveGameObjectToScene(root, scene);
            folder = "Assets/VisemeEvaluation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            if (graph.IsValid()) graph.Destroy();
            EditorSceneManager.ClosePreviewScene(scene);
            AssetDatabase.DeleteAsset(folder);
            if (mesh != null && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
        }

        [TestCase(VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off)]
        [TestCase(VisemeBlendshapeOverrideComponent.WriteDefaultsMode.On)]
        [TestCase(VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Auto)]
        public void AnimatorEvaluatesEveryVisemeAndVoiceThreshold(VisemeBlendshapeOverrideComponent.WriteDefaultsMode mode)
        {
            var renderer = CreateRenderer(root, out mesh);
            var controller = CreateController(root, renderer, folder + "/Viseme.controller", mode);
            var generated = controller.layers.Single(layer => layer.name == VisemeBlendshapeOverrideUtils.GeneratedLayerName);
            Assert.That(generated.syncedLayerIndex, Is.EqualTo(-1));
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Viseme evaluator");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimatorControllerPlayable.Create(graph, controller);
            AnimationPlayableOutput.Create(graph, "Viseme output", animator).SetSourcePlayable(playable);
            graph.Play();

            foreach (var id in Enumerable.Range(0, 15).Concat(Enumerable.Range(0, 15).Reverse()))
            foreach (var voice in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f })
            {
                playable.SetInteger("Viseme", id);
                playable.SetFloat("Voice", voice);
                for (var frame = 0; frame < 4; frame++) graph.Evaluate(1f / 60f);
                for (var shape = 0; shape < 15; shape++)
                    Assert.That(renderer.GetBlendShapeWeight(shape),
                        Is.EqualTo(shape == id ? 80f * Mathf.InverseLerp(0.2f, 0.8f, voice) : 0f).Within(0.03f),
                        $"WD={mode}, Viseme={id}, Voice={voice}, Shape={shape}");
            }
        }

        [Test]
        public void ParameterTypeConflictLeavesSuppliedGraphIntact()
        {
            var renderer = CreateRenderer(root, out mesh);
            var config = Configure(root, renderer, VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Conflict.controller");
            controller.AddParameter("Voice", AnimatorControllerParameterType.Bool);
            var state = controller.layers[0].stateMachine.AddState("Existing");
            controller.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();
            var json = EditorJsonUtility.ToJson(controller);
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan), Is.True);
            Assert.Throws<ArgumentException>(() => VisemeBlendshapeOverrideProcessor.BuildController(controller, plan));
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(json));
            Assert.That(controller.parameters.Any(parameter => parameter.name == "Viseme"), Is.False);
        }

        public static void ConfigurePlayerProbe(GameObject root, string assetFolder)
        {
            foreach (var mode in new[] { VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off,
                         VisemeBlendshapeOverrideComponent.WriteDefaultsMode.On })
            {
                var name = "VisemeProbe" + mode;
                var avatar = new GameObject(name);
                avatar.transform.SetParent(root.transform, false);
                var renderer = CreateRenderer(avatar, out var probeMesh);
                AssetDatabase.CreateAsset(probeMesh, assetFolder + "/" + name + ".asset");
                var controller = CreateController(avatar, renderer, assetFolder + "/" + name + ".controller", mode);
                avatar.AddComponent<Animator>().runtimeAnimatorController = controller;
                UnityEngine.Object.DestroyImmediate(avatar.GetComponent<VisemeBlendshapeOverrideComponent>());
                var scenario = new PlayerScenario { name = "Viseme15AndVoice" + mode, animator_paths = new[] { name } };
                foreach (var id in Enumerable.Range(0, 15).Concat(Enumerable.Range(0, 15).Reverse()))
                foreach (var voice in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f })
                {
                    var step = new PlayerStep
                    {
                        frames = 4, delta_time = 1f / 60f,
                        inputs = new[]
                        {
                            new PlayerInput { animator_path = name, name = "Viseme", type = "int", value = id },
                            new PlayerInput { animator_path = name, name = "Voice", type = "float", value = voice },
                        },
                        checks = Enumerable.Range(0, 15).Select(shape => new PlayerCheck
                        {
                            path = name + "/Face", property = "blendshape", index = shape,
                            expected = shape == id ? 80f * Mathf.InverseLerp(0.2f, 0.8f, voice) : 0f,
                            tolerance = 0.03f,
                        }).ToArray(),
                    };
                    scenario.steps.Add(step);
                }
                root.SendMessage("AddScenarioJson", JsonUtility.ToJson(scenario), SendMessageOptions.RequireReceiver);
            }
        }

        private static SkinnedMeshRenderer CreateRenderer(GameObject avatar, out Mesh shapeMesh)
        {
            var face = new GameObject("Face");
            face.transform.SetParent(avatar.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            shapeMesh = new Mesh { name = "Viseme evaluation mesh", vertices = new[] { Vector3.zero } };
            for (var shape = 0; shape < 15; shape++)
                shapeMesh.AddBlendShapeFrame("Shape" + shape, 100f, new[] { Vector3.up }, null, null);
            renderer.sharedMesh = shapeMesh;
            return renderer;
        }

        private static VisemeBlendshapeOverrideComponent Configure(GameObject avatar, SkinnedMeshRenderer renderer,
            VisemeBlendshapeOverrideComponent.WriteDefaultsMode mode)
        {
            var config = avatar.AddComponent<VisemeBlendshapeOverrideComponent>();
            config.targetRenderer = renderer;
            config.globalWeight = 80f;
            config.voiceModulationMode = VisemeBlendshapeOverrideComponent.VoiceModulationMode.Linear;
            config.voiceMin = 0.2f;
            config.voiceMax = 0.8f;
            config.writeDefaultsMode = mode;
            for (var id = 0; id < 15; id++) config.GetBinding((VisemeBlendshapeOverrideComponent.Viseme)id).blendshapeName = "Shape" + id;
            return config;
        }

        private static AnimatorController CreateController(GameObject avatar, SkinnedMeshRenderer renderer, string path,
            VisemeBlendshapeOverrideComponent.WriteDefaultsMode mode)
        {
            Configure(avatar, renderer, mode);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var original = controller.layers[0].stateMachine.AddState("Original state");
            original.writeDefaultValues = mode != VisemeBlendshapeOverrideComponent.WriteDefaultsMode.Off;
            controller.layers[0].stateMachine.defaultState = original;
            Assert.That(VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(avatar, out var plan), Is.True);
            VisemeBlendshapeOverrideProcessor.BuildController(controller, plan);
            return controller;
        }

        [Serializable] private sealed class PlayerScenario
        {
            public string name;
            public string[] animator_paths;
            public List<PlayerStep> steps = new List<PlayerStep>();
        }
        [Serializable] private sealed class PlayerStep
        {
            public int frames;
            public float delta_time;
            public PlayerInput[] inputs;
            public PlayerCheck[] checks;
        }
        [Serializable] private sealed class PlayerInput
        {
            public string animator_path, name, type;
            public float value;
        }
        [Serializable] private sealed class PlayerCheck
        {
            public string path, property;
            public int index;
            public float expected, tolerance;
        }
    }
}
