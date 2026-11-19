using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class GenerationLifecycleTests
    {
        private Scene scene;
        private readonly List<GameObject> avatars = new List<GameObject>();
        private readonly HashSet<string> generatedFolders = new HashSet<string>();

        [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            new Processor().OnPostprocessAvatar();
            foreach (var avatar in avatars)
            {
                Processor.ReleaseBuildSnapshot(avatar);
                Object.DestroyImmediate(avatar);
            }
            avatars.Clear();
            foreach (var folder in generatedFolders) AssetDatabase.DeleteAsset(folder);
            generatedFolders.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        private GameObject Avatar(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRCAvatarDescriptor>().baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var config = root.AddComponent<ASSComponent>();
            config.disableDefense = config.disableOverlay = config.disableWarningSound = config.disableObfuscation = true;
            config.gesturePassword = new List<int> { 1, 2 };
            new GameObject("Body").transform.SetParent(root.transform, false);
            avatars.Add(root);
            return root;
        }

        private AnimatorController Process(GameObject avatar, bool ndmf)
        {
            Assert.That(Processor.ProcessAvatar(avatar, ndmf), Is.True);
            var controller = (AnimatorController)avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string folder = controllerPath.Substring(0, controllerPath.IndexOf("/", Constants.ASSET_FOLDER.Length + 1));
            generatedFolders.Add(folder);
            return controller;
        }

        private static List<AudioClip> ScanSdkAudioErrors(VRCAvatarDescriptor descriptor)
        {
            var builder = Type.GetType(
                "VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder, VRC.SDK3A.Editor", true);
            var scanner = builder.GetMethod("ScanAvatarForAnimatorPlayAudio",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(scanner, Is.Not.Null, "The real SDK audio upload validator must be available.");
            return (List<AudioClip>)scanner.Invoke(null, new object[] { descriptor });
        }

        private static Dictionary<string, byte[]> PackagedAudioBytes()
        {
            var bytes = new Dictionary<string, byte[]>();
            foreach (string resource in new[] { "PasswordSuccess", "CountdownWarning", "StepSuccess", "InputError" })
            {
                var clip = Resources.Load<AudioClip>(resource);
                Assert.That(clip, Is.Not.Null, resource);
                string path = AssetDatabase.GetAssetPath(clip);
                bytes.Add(path, File.ReadAllBytes(path));
                bytes.Add(path + ".meta", File.ReadAllBytes(path + ".meta"));
            }
            return bytes;
        }

        private static void AssertPackagedAudioUnchanged(Dictionary<string, byte[]> original)
        {
            foreach (var item in original)
                Assert.That(File.ReadAllBytes(item.Key), Is.EqualTo(item.Value), item.Key);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GeneratedFeedbackAudioPassesSdkUploadValidationWithIndependentUiOptions(
            bool disableWarningSound, bool disableOverlay)
        {
            var original = PackagedAudioBytes();
            var avatar = Avatar("Audio upload validation");
            var config = avatar.GetComponent<ASSComponent>();
            config.disableWarningSound = disableWarningSound;
            config.disableOverlay = disableOverlay;
            var controller = Process(avatar, true);
            var success = Resources.Load<AudioClip>(Constants.AUDIO_PASSWORD_SUCCESS);
            var warning = Resources.Load<AudioClip>(Constants.AUDIO_COUNTDOWN_WARNING);
            var expected = new Dictionary<AudioClip, string> { { success, Constants.GO_AUDIO_SUCCESS } };
            if (!disableWarningSound) expected.Add(warning, Constants.GO_AUDIO_WARNING);
            var behaviours = controller.GetBehaviours<VRCAnimatorPlayAudio>();
            Assert.That(behaviours.Length, Is.EqualTo(expected.Count));
            Assert.That(behaviours.SelectMany(behaviour => behaviour.Clips), Is.EquivalentTo(expected.Keys));
            foreach (var behaviour in behaviours)
            {
                var clip = behaviour.Clips.Single();
                Assert.That(clip.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
                Assert.That(clip.loadInBackground, Is.True, clip.name);
                Assert.That(behaviour.SourcePath, Is.EqualTo(expected[clip]));
                var target = avatar.transform.Find(behaviour.SourcePath);
                Assert.That(target, Is.Not.Null, behaviour.SourcePath);
                var source = target.GetComponent<AudioSource>();
                Assert.That(source, Is.Not.Null, behaviour.SourcePath);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.loop, Is.False);
            }
            Assert.That(avatar.transform.Find(Constants.GO_AUDIO_WARNING) != null, Is.EqualTo(!disableWarningSound));
            Assert.That(avatar.transform.Find(Constants.GO_OVERLAY) != null, Is.EqualTo(!disableOverlay));
            Assert.That(ScanSdkAudioErrors(avatar.GetComponent<VRCAvatarDescriptor>()), Is.Empty);
            AssertPackagedAudioUnchanged(original);
        }

        private static Texture[] RendererTextures(GameObject avatar)
        {
            return avatar.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .SelectMany(material => material.GetTexturePropertyNameIDs().Select(material.GetTexture))
                .Where(texture => texture != null).Distinct().ToArray();
        }

        private static Texture[] NonStreamingMipmappedTextures(GameObject avatar)
        {
            return RendererTextures(avatar).Where(texture =>
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                return importer != null && importer.mipmapEnabled && !importer.streamingMipmaps;
            }).ToArray();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GeneratedOverlayTexturesMeetUploadStreamingRequirementsWithoutChangingSource(
            bool ndmf, bool inactive)
        {
            var logo = Resources.Load<Texture2D>("Avatar Security System");
            Assert.That(logo, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(logo);
            byte[] imageBytes = File.ReadAllBytes(path);
            byte[] metaBytes = File.ReadAllBytes(path + ".meta");
            string guid = AssetDatabase.AssetPathToGUID(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.mipmapEnabled, Is.True, "Keep Logo mipmaps enabled, not bypassed.");
            Assert.That(importer.streamingMipmaps, Is.True, "The shipped Logo importer must enable streaming.");

            var avatar = Avatar("Overlay texture upload validation");
            avatar.GetComponent<ASSComponent>().disableOverlay = false;
            Process(avatar, ndmf);
            var overlay = avatar.transform.Find(Constants.GO_OVERLAY);
            Assert.That(overlay, Is.Not.Null);
            overlay.gameObject.SetActive(!inactive);
            Assert.That(RendererTextures(avatar), Does.Contain(logo), "The generated material must use the shipped Logo.");
            Assert.That(NonStreamingMipmappedTextures(avatar), Is.Empty,
                "All generated Renderer textures, including inactive children, must meet SDK streaming rules.");
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(imageBytes));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metaBytes),
                "Building must not rewrite the source TextureImporter.");
        }

        [Test]
        public void TextureScanDetectsNonStreamingMipmapsOnInactiveOverlayAndAcceptsStreamingControl()
        {
            var avatar = Avatar("Inactive overlay texture control");
            avatar.GetComponent<ASSComponent>().disableOverlay = false;
            Process(avatar, true);
            var overlay = avatar.transform.Find(Constants.GO_OVERLAY);
            var renderer = overlay.GetComponentInChildren<Renderer>(true);
            Assert.That(renderer.sharedMaterial.HasProperty("_G4D9"), Is.True);
            var logo = (Texture2D)renderer.sharedMaterial.GetTexture("_G4D9");
            Assert.That(logo, Is.Not.Null);
            string logoPath = AssetDatabase.GetAssetPath(logo);
            byte[] logoMeta = File.ReadAllBytes(logoPath + ".meta");
            string folder = "Assets/__ASSTextureImport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            generatedFolders.Add(folder);
            string path = folder + "/UploadControl.png";
            File.Copy(logoPath, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.SaveAndReimport();
            var control = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            renderer.sharedMaterial.SetTexture("_G4D9", control);
            overlay.gameObject.SetActive(false);
            Assert.That(NonStreamingMipmappedTextures(avatar), Is.EqualTo(new[] { control }));

            importer.streamingMipmaps = true;
            importer.SaveAndReimport();
            Assert.That(NonStreamingMipmappedTextures(avatar), Is.Empty);
            Assert.That(File.ReadAllBytes(logoPath + ".meta"), Is.EqualTo(logoMeta));
        }

        [Test]
        public void RealSdkRejectsForegroundDecompressionAndAcceptsTheSameClipAfterBackgroundImport()
        {
            var original = PackagedAudioBytes();
            var avatar = Avatar("Audio importer positive and negative controls");
            var controller = Process(avatar, true);
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            var success = Resources.Load<AudioClip>(Constants.AUDIO_PASSWORD_SUCCESS);
            var behaviour = controller.GetBehaviours<VRCAnimatorPlayAudio>()
                .Single(item => item.Clips.Contains(success));
            Assert.That(ScanSdkAudioErrors(descriptor), Is.Empty);
            string folder = "Assets/__ASSAudioImport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            generatedFolders.Add(folder);
            string path = folder + "/UploadControl.mp3";
            File.Copy(AssetDatabase.GetAssetPath(success), path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
            var control = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Assert.That(control.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(control.loadInBackground, Is.False);
            behaviour.Clips = new[] { control };
            EditorUtility.SetDirty(behaviour);
            AssetDatabase.SaveAssets();
            Assert.That(ScanSdkAudioErrors(descriptor), Is.EqualTo(new[] { control }));

            importer.loadInBackground = true;
            importer.SaveAndReimport();
            var reloaded = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Assert.That(reloaded.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(reloaded.loadInBackground, Is.True);
            Assert.That(behaviour.Clips.Single(), Is.EqualTo(reloaded));
            Assert.That(ScanSdkAudioErrors(descriptor), Is.Empty);
            AssertPackagedAudioUnchanged(original);
        }

        [Test]
        public void DifferentAvatarOutputsAndParametersSurviveAnotherAvatarGenerationAndRetry()
        {
            var first = Avatar("First");
            var second = Avatar("Second");
            var firstController = Process(first, true);
            string firstPath = AssetDatabase.GetAssetPath(firstController);
            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            var firstParameters = first.GetComponent<VRCAvatarDescriptor>().expressionParameters;
            var secondController = Process(second, true);
            Assert.That(AssetDatabase.GetAssetPath(secondController), Is.Not.EqualTo(firstPath));
            Process(second, true);
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(firstPath), Is.SameAs(firstController));
            Assert.That(AssetDatabase.AssetPathToGUID(firstPath), Is.EqualTo(firstGuid));
            Assert.That(first.GetComponent<VRCAvatarDescriptor>().expressionParameters, Is.SameAs(firstParameters));
            Assert.That(firstParameters.parameters.Any(parameter => parameter.name == Constants.PARAM_PASSWORD_CORRECT), Is.True);
            Assert.That(firstController.layers.All(layer => layer.syncedLayerIndex == -1), Is.True);
        }

        [Test]
        public void SdkPostprocessReclaimsOnlyItsOwnOutputAndPreservesOtherBuildOutputs()
        {
            var ndmfAvatar = Avatar("NdmfOutput");
            var controller = Process(ndmfAvatar, true);
            string outputPath = AssetDatabase.GetAssetPath(controller);
            var sdkAvatar = Avatar("SdkOutput");
            var sdkController = Process(sdkAvatar, false);
            string sdkPath = AssetDatabase.GetAssetPath(sdkController);
            new Processor().OnPostprocessAvatar();
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath), Is.SameAs(controller));
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimatorController>(sdkPath), Is.Null);
            Assert.That(sdkAvatar.GetComponent<VRCAvatarDescriptor>().customizeAnimationLayers, Is.False);
        }

        [Test]
        public void SupervisorControlsKeepTheirSiblingTargetsAfterLayerInsertion()
        {
            var avatar = Avatar("Targets");
            var controller = Process(avatar, true);
            var layers = controller.layers.ToList();
            layers.Insert(1, Utils.CreateLayer("Inserted user layer"));
            controller.layers = layers.ToArray();
            Processor.ProcessPlayableObfuscation(avatar, true);
            var final = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            foreach (var state in final.layers.SelectMany(layer => layer.stateMachine.states).Select(item => item.state))
            foreach (var control in state.behaviours.OfType<VRCAnimatorLayerControl>())
            {
                if (!control.debugString.StartsWith(AnimatorLifecycle.TargetLayerPrefix, StringComparison.Ordinal)) continue;
                string target = control.debugString.Substring(AnimatorLifecycle.TargetLayerPrefix.Length);
                Assert.That(final.layers[control.layer].name, Is.EqualTo(target));
                Assert.That(control.goalWeight, Is.EqualTo(1f));
            }
        }
    }
}
