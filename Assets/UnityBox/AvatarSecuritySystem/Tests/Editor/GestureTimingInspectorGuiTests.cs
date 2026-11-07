using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityBox.AvatarSecuritySystem.Editor;
using Object = UnityEngine.Object;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class GestureTimingInspectorGuiTests
    {
        [UnityTest, Category("UnityBoxGraphics")]
        public IEnumerator OpeningAndDrawingLegacyTimingValuesPreservesTheComponentAndPrefabBytes()
        {
            string folder = "Assets/__ASSInspectorTiming_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Legacy timing Inspector");
            SceneManager.MoveGameObjectToScene(root, scene);
            var component = root.AddComponent<ASSComponent>();
            component.gestureHoldTime = 0.15f;
            component.gestureErrorTolerance = 0.3f;
            component.gestureMaxHoldTime = 0.1f;
            component.uiLanguage = SystemLanguage.English;
            string path = folder + "/Legacy.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            byte[] source = File.ReadAllBytes(path), metadata = File.ReadAllBytes(path + ".meta");
            string original = EditorJsonUtility.ToJson(component);
            bool dirty = scene.isDirty;
            var previousLanguage = I18n.GetCurrentLanguage();
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var host = ScriptableObject.CreateInstance<ASSInspectorTestHost>();
            try
            {
                host.Inspector = inspector;
                host.position = new Rect(40, 40, 800, 1100);
                host.ShowUtility();
                for (int frame = 0; frame < 3; frame++) { host.Repaint(); yield return null; }
                host.SendEvent(new Event { type = EventType.Layout });
                host.SendEvent(new Event { type = EventType.Repaint });
                Assert.That(host.DrawCount, Is.GreaterThan(0));
                Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(original));
                Assert.That(scene.isDirty, Is.EqualTo(dirty));
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
                Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(metadata));
            }
            finally
            {
                host.Close();
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(folder);
                I18n.SetLanguage(previousLanguage);
            }
        }
    }
}
