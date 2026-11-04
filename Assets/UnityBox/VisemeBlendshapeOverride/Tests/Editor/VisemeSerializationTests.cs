using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBox.VisemeBlendshapeOverride.Tests
{
    public sealed class VisemeSerializationTests
    {
        [TestCase(VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Global)]
        [TestCase(VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Disabled)]
        [TestCase(VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Linear)]
        public void CurrentPrefabWithoutLegacyFieldPreservesItsExplicitMode(
            VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride mode)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Current voice settings");
            SceneManager.MoveGameObjectToScene(root, scene);
            var config = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            var binding = config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP);
            binding.useCustomSettings = true;
            binding.voiceMode = mode;
            var path = "Assets/VisemeCurrent_" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), @"(?m)^\s*legacyVoiceMode:.*\r?\n", ""));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<VisemeBlendshapeOverrideComponent>();
                Assert.That(loaded.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP).voiceMode, Is.EqualTo(mode));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(0, VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Disabled)]
        [TestCase(1, VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride.Linear)]
        public void LegacyPrefabVoiceFieldsMigrateToEquivalentCurrentSettings(int legacyMode,
            VisemeBlendshapeOverrideComponent.VisemeBinding.VoiceModeOverride expected)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Legacy voice settings");
            SceneManager.MoveGameObjectToScene(root, scene);
            var config = root.AddComponent<VisemeBlendshapeOverrideComponent>();
            var binding = config.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP);
            binding.blendshapeName = "LegacyRow";
            binding.useCustomSettings = true;
            binding.weight = 42f;
            binding.voiceMin = 0.3f;
            binding.voiceMax = 0.7f;
            var path = "Assets/VisemeLegacy_" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var yaml = File.ReadAllText(path);
                var rowStart = yaml.IndexOf("- viseme: 1\n", StringComparison.Ordinal);
                var rowEnd = yaml.IndexOf("- viseme: 2\n", rowStart, StringComparison.Ordinal);
                Assert.That(rowStart, Is.GreaterThanOrEqualTo(0));
                Assert.That(rowEnd, Is.GreaterThan(rowStart));
                var row = yaml.Substring(rowStart, rowEnd - rowStart);
                row = Regex.Replace(row, @"voiceMode: \d+", "voiceModulationMode: " + legacyMode);
                row = Regex.Replace(row, @"(?m)^\s*legacyVoiceMode:.*\r?\n", "");
                row = row.Replace("useCustomSettings: 1", "overrideGlobalVoiceSettings: 1");
                File.WriteAllText(path, yaml.Substring(0, rowStart) + row + yaml.Substring(rowEnd));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<VisemeBlendshapeOverrideComponent>();
                var migrated = loaded.GetBinding(VisemeBlendshapeOverrideComponent.Viseme.PP);
                Assert.That(migrated.voiceMode, Is.EqualTo(expected));
                Assert.That(migrated.useCustomSettings, Is.True);
                Assert.That(migrated.weight, Is.EqualTo(42f));
                Assert.That(migrated.voiceMin, Is.EqualTo(0.3f));
                Assert.That(migrated.voiceMax, Is.EqualTo(0.7f));
                Assert.That(migrated.blendshapeName, Is.EqualTo("LegacyRow"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
