using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem;

public static class ASSRealZipUpgrade
{
    const string Folder = "Assets/ASSUpgradeFixtures";
    const string PrefabPath = Folder + "/LegacyAvatar.prefab";
    const string ScenePath = Folder + "/LegacyAvatar.unity";
    const string Canonical = "1c51e6a139920bb4ca12700dc82627ff";
    const string Output = ".unitybox-real-upgrade";
    [Serializable] public class Evidence
    {
        public bool passed;
        public string phase, legacy_guid, current_guid, component_type, scene_guid, prefab_guid, audio_guid;
        public string scene_component_id, prefab_component_id, instance_component_id;
        public string[] checks;
        public string error;
        public float legacy_hold, legacy_tolerance, upgraded_hold, upgraded_tolerance;
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static string Identity(UnityEngine.Object value) { return GlobalObjectId.GetGlobalObjectIdSlow(value).ToString(); }
    static void Configure(ASSComponent component)
    {
        component.gesturePassword = new List<int> { 7, 1, 6, 2, 4 };
        component.useRightHand = true;
        component.countdownDuration = 23.5f;
        component.warningThreshold = 7.5f;
        component.gestureHoldTime = .22f;
        component.gestureErrorTolerance = .44f;
        component.gestureMaxHoldTime = 2.1f;
        component.disableRootChildren = false;
        component.enabledInPlaymode = true;
        component.disableOverlay = true;
        component.disableWarningSound = true;
        component.defaultEnableDefense = false;
        component.disableDefense = true;
        component.disableObfuscation = true;
        component.warningBeep = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/CustomWarning.wav");
        component.successSound = component.warningBeep;
        component.writeDefaultsMode = ASSComponent.WriteDefaultsMode.On;
        Require(component.warningBeep != null, "Custom audio asset must import");
    }
    static void VerifyConfig(ASSComponent component, float expectedDuration)
    {
        Require(component != null, "ASS component must deserialize");
        Require(component.gesturePassword.SequenceEqual(new[] { 7, 1, 6, 2, 4 }), "password preserved");
        Require(component.useRightHand && component.enabledInPlaymode, "hand/play settings preserved");
        Require(Mathf.Approximately(component.countdownDuration, expectedDuration), "countdown or prefab override preserved");
        Require(Mathf.Approximately(component.gestureHoldTime,.22f) && Mathf.Approximately(component.gestureErrorTolerance,.21f)
            && Mathf.Approximately(component.gestureMaxHoldTime,2.1f), "hold/max timing preserved; legacy tolerance explicitly normalizes from .44 to .21 under the 0.9 continuous-hold contract");
        Require(!component.disableRootChildren && component.disableOverlay && component.disableWarningSound
            && component.disableDefense && !component.defaultEnableDefense && component.disableObfuscation, "options preserved");
        var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/CustomWarning.wav");
        Require(component.warningBeep == audio && component.successSound == audio, "asset references preserved");
    }
    public static void CreateLegacy()
    {
        var result = new Evidence { phase = "actual published 0.7.24 creation" };
        try
        {
            Directory.CreateDirectory(Output);
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var template = new GameObject("Legacy prefab avatar");
            var component = template.AddComponent<ASSComponent>(); Configure(component);
            result.legacy_guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(component)));
            Require(!string.IsNullOrEmpty(result.legacy_guid) && result.legacy_guid != Canonical, "GUID must be actually generated from legacy meta omission");
            result.component_type = component.GetType().AssemblyQualifiedName;
            var prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
            UnityEngine.Object.DestroyImmediate(template);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Legacy prefab instance";
            var instanceConfig = instance.GetComponent<ASSComponent>();
            instanceConfig.countdownDuration = 19.75f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instanceConfig);
            var plain = new GameObject("Legacy scene component"); Configure(plain.AddComponent<ASSComponent>());
            Require(EditorSceneManager.SaveScene(plain.scene, ScenePath), "Scene saved");
            AssetDatabase.SaveAssets();
            result.scene_guid = AssetDatabase.AssetPathToGUID(ScenePath);
            result.prefab_guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            result.audio_guid = AssetDatabase.AssetPathToGUID(Folder + "/CustomWarning.wav");
            result.scene_component_id = Identity(plain.GetComponent<ASSComponent>());
            result.prefab_component_id = Identity(prefab.GetComponent<ASSComponent>());
            result.instance_component_id = Identity(instanceConfig);
            File.WriteAllBytes(Output + "/legacy-scene.original", File.ReadAllBytes(ScenePath));
            File.WriteAllBytes(Output + "/legacy-prefab.original", File.ReadAllBytes(PrefabPath));
            result.passed = true;
            result.checks = new[] { "old package compiled unchanged", "real random MonoScript GUID", "saved Scene and Prefab with non-default values", "saved prefab-instance override" };
            File.WriteAllText(Output + "/legacy.json", JsonUtility.ToJson(result,true));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            result.error = error.ToString(); Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/legacy.json", JsonUtility.ToJson(result,true));
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
    public static void VerifyPublished080Breakage()
    {
        var result = new Evidence { phase = "published 0.8.0 regression reproduction: expected missing components" };
        try
        {
            var legacy = JsonUtility.FromJson<Evidence>(File.ReadAllText(Output + "/legacy.json"));
            result.legacy_guid = legacy.legacy_guid;
            var currentScript = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(Canonical));
            Require(currentScript != null && currentScript.GetClass() == typeof(ASSComponent), "Current package script imports correctly");
            result.current_guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(currentScript));
            Require(result.current_guid == Canonical, "Published 0.8.0 uses the new fixed identity");
            Require(string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(legacy.legacy_guid)), "Old locally generated identity is no longer resolved");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var plain = roots.Single(item => item.name == "Legacy scene component");
            var instance = roots.Single(item => item.name == "Legacy prefab instance");
            Require(plain.GetComponent<ASSComponent>() == null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(plain) > 0,
                "The real old Scene configuration reproduces Missing Script in 0.8.0");
            Require(instance.GetComponent<ASSComponent>() == null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(instance) > 0,
                "The real prefab instance reproduces Missing Script in 0.8.0");
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { Require(prefab.GetComponent<ASSComponent>() == null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) > 0,
                "The real old Prefab configuration reproduces Missing Script in 0.8.0"); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            Require(File.ReadAllBytes(ScenePath).SequenceEqual(File.ReadAllBytes(Output + "/legacy-scene.original")), "Missing Scene still retains all original serialized configuration");
            Require(File.ReadAllBytes(PrefabPath).SequenceEqual(File.ReadAllBytes(Output + "/legacy-prefab.original")), "Missing Prefab still retains all original serialized configuration");
            result.checks = new[] { "published 0.8.0 canonical MonoScript identity confirmed", "actual legacy GUID is unresolved", "Scene, Prefab and overridden instance all reproduce Missing Script", "all serialized configuration bytes remain intact" };
            result.passed = true;
            File.WriteAllText(Output + "/broken.json", JsonUtility.ToJson(result,true));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            result.error = error.ToString(); File.WriteAllText(Output + "/broken.json", JsonUtility.ToJson(result,true));
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
    static double deadline;
    static int ticks;
    public static void VerifyAutomaticUpgrade()
    {
        deadline = EditorApplication.timeSinceStartup + 90;
        ticks = 0;
        EditorApplication.update += WaitForAutomaticUpgrade;
    }
    static void WaitForAutomaticUpgrade()
    {
        if (ticks++ < 3) return;
        var legacy = JsonUtility.FromJson<Evidence>(File.ReadAllText(Output + "/legacy.json"));
        bool changed = !File.ReadAllText(ScenePath).Contains(legacy.legacy_guid)
            && !File.ReadAllText(PrefabPath).Contains(legacy.legacy_guid);
        if (!changed && EditorApplication.timeSinceStartup < deadline) return;
        EditorApplication.update -= WaitForAutomaticUpgrade;
        var result = new Evidence { phase = "actual package replacement plus automatic startup migration", legacy_guid = legacy.legacy_guid };
        try
        {
            Require(changed, "InitializeOnLoad automatic repair must rewrite both legacy references without manual API invocation");
            Require(File.ReadAllText(ScenePath).Contains(Canonical) && File.ReadAllText(PrefabPath).Contains(Canonical), "fixed canonical identity serialized");
            Require(File.ReadAllText(ScenePath) == File.ReadAllText(Output + "/legacy-scene.original").Replace(legacy.legacy_guid, Canonical), "scene only changes script GUID");
            Require(File.ReadAllText(PrefabPath) == File.ReadAllText(Output + "/legacy-prefab.original").Replace(legacy.legacy_guid, Canonical), "prefab only changes script GUID");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var plain = roots.Single(item=>item.name == "Legacy scene component").GetComponent<ASSComponent>();
            var instance = roots.Single(item=>item.name == "Legacy prefab instance").GetComponent<ASSComponent>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<ASSComponent>();
            VerifyConfig(plain,23.5f); VerifyConfig(prefab,23.5f); VerifyConfig(instance,19.75f);
            result.legacy_hold=.22f; result.legacy_tolerance=.44f;
            result.upgraded_hold=plain.gestureHoldTime; result.upgraded_tolerance=plain.gestureErrorTolerance;
            Require(roots.Sum(item=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item))==0, "no missing components after upgrading");
            result.current_guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(plain)));
            result.scene_guid = AssetDatabase.AssetPathToGUID(ScenePath); result.prefab_guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            result.audio_guid = AssetDatabase.AssetPathToGUID(Folder + "/CustomWarning.wav");
            Require(result.current_guid==Canonical && result.scene_guid==legacy.scene_guid && result.prefab_guid==legacy.prefab_guid && result.audio_guid==legacy.audio_guid,"asset identities preserved");
            result.scene_component_id=Identity(plain);result.prefab_component_id=Identity(prefab);result.instance_component_id=Identity(instance);
            Require(result.scene_component_id==legacy.scene_component_id && result.prefab_component_id==legacy.prefab_component_id && result.instance_component_id==legacy.instance_component_id,"component file IDs preserved");
            var backups = Directory.GetFiles("UserSettings/UnityBox/ASSMigration","*",SearchOption.AllDirectories).Select(File.ReadAllBytes).ToArray();
            Require(backups.Any(bytes=>bytes.SequenceEqual(File.ReadAllBytes(Output + "/legacy-scene.original")))
                && backups.Any(bytes=>bytes.SequenceEqual(File.ReadAllBytes(Output + "/legacy-prefab.original"))),"original bytes backed up");
            result.checks = new[] { "automatic startup migration without calling RepairAssets", "scene and prefab only script-GUID changes", "all non-default password/hand/countdown/hold/maximum/options/audio preserved", "legacy tolerance explicitly normalizes .44 to .21 for hold .22 under the new contract", "prefab-instance override preserved", "asset GUIDs and component fileIDs unchanged", "zero missing ASS components", "original byte backups present" };
            result.passed=true;
            File.WriteAllText(Output + "/upgraded.json",JsonUtility.ToJson(result,true));
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            result.error=error.ToString();File.WriteAllText(Output + "/upgraded.json",JsonUtility.ToJson(result,true));
            Debug.LogException(error);EditorApplication.Exit(1);
        }
    }
}
