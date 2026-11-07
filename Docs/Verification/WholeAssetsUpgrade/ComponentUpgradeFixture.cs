using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ComponentUpgradeFixture
{
    const string Folder = "Assets/ComponentUpgradeFixtures";
    const string Output = ".unitybox-component-upgrade";
    const string BasePath = "Assets/OldCostumes/Base.prefab";
    const string VariantPath = "Assets/OtherCollection/Variant.prefab";
    const string NestedPath = "Assets/Containers/Nested.prefab";
    const string ScenePath = "Assets/Scenes/Avatar.unity";
    const string Outfit = "UnityBox.AdvancedCostumeController.ACCOutfitMarker";
    const string Part = "UnityBox.AdvancedCostumeController.ACCPartGroupMarker";
    const string Variant = "UnityBox.AdvancedCostumeController.ACCVariantMaterialOverride";
    const string Ass = "UnityBox.AvatarSecuritySystem.ASSComponent";
    const string Viseme = "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent";
    const string Optimizer = "UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent";
    const string Holder = "UnityBox.ComponentUpgradeFixtureRuntime.ComponentUpgradeReferenceHolder";
    static readonly string[] MainAssets = { BasePath, VariantPath, NestedPath, ScenePath };
    sealed class Owner
    {
        public string folder, package, duplicateType, overrideType, overrideField;
        public string[] types;
        public object variantValue, instanceValue;
        public string api => "UnityBox." + folder + ".ComponentMigration.MigrationEngine";
        public string duplicatePath => Folder + "/Duplicate-" + folder + ".unity";
    }
    static readonly Owner[] AllOwners =
    {
        new Owner { folder = "AdvancedCostumeController", package = "top.sealoong.unitybox.advanced-costume-controller", types = new[] { Outfit, Part, Variant }, duplicateType = Outfit, overrideType = Part, overrideField = "GroupName", variantValue = "Variant stored group", instanceValue = "Scene override group" },
        new Owner { folder = "AvatarSecuritySystem", package = "top.sealoong.unitybox.avatar-security-system", types = new[] { Ass }, duplicateType = Ass, overrideType = Ass, overrideField = "countdownDuration", variantValue = 21f, instanceValue = 24f },
        new Owner { folder = "VisemeBlendshapeOverride", package = "top.sealoong.unitybox.viseme-blendshape-override", types = new[] { Viseme }, duplicateType = Viseme, overrideType = Viseme, overrideField = "globalWeight", variantValue = 31f, instanceValue = 27f },
        new Owner { folder = "AnimatorOptimizer", package = "top.sealoong.unitybox.animator-optimizer", types = new[] { Optimizer }, duplicateType = Optimizer, overrideType = Optimizer, overrideField = "generatedLayerName", variantValue = "Variant AO override", instanceValue = "Scene AO override" }
    };
    [Serializable] sealed class Config { public string scope; public string[] owners; }
    static Config config;
    static Config Configuration => config ?? (config = JsonUtility.FromJson<Config>(File.ReadAllText(Output + "/config.json")));
    static Owner[] Owners => AllOwners.Where(owner => Configuration.owners.Contains(owner.folder)).ToArray();
    static string[] Types => Owners.SelectMany(owner => owner.types).ToArray();
    static string[] Candidates => MainAssets.Concat(Owners.Select(owner => owner.duplicatePath)).ToArray();
    static Action pending;
    static int frames;
    static Evidence current;
    static readonly List<UnityEditor.Editor> inspectors = new List<UnityEditor.Editor>();

    [Serializable] public sealed class TypeRecord { public string owner, type, guid, scriptPath, generatedMetaSha256; }
    [Serializable] public sealed class FileRecord { public string path, sha256, guid, original; }
    [Serializable] public sealed class PropertyRecord { public string path, kind, value; }
    [Serializable] public sealed class ComponentRecord
    {
        public string asset, owner, type, globalId;
        public List<PropertyRecord> properties = new List<PropertyRecord>();
    }
    [Serializable] public sealed class DuplicateRecord { public string owner, asset, type, currentBytes, newGlobalId, markerField, markerValue; }
    [Serializable] public sealed class State
    {
        public string scope;
        public List<TypeRecord> types = new List<TypeRecord>();
        public List<FileRecord> files = new List<FileRecord>();
        public List<ComponentRecord> components = new List<ComponentRecord>();
        public List<DuplicateRecord> duplicates = new List<DuplicateRecord>();
    }
    [Serializable] public sealed class EntryRecord { public string owner, asset, componentFileId, legacyGuid, targetGuid, type, status, details; }
    [Serializable] public sealed class FileHash { public string path, sha256; }
    [Serializable] public sealed class NativeOwner
    {
        public string owner, package, engineType, assembly, assemblyPath, assemblySha256;
        public List<FileHash> sourceFiles = new List<FileHash>();
    }
    [Serializable] public sealed class BatchRecord
    {
        public string owner, engineType, backupDirectory;
        public int applyInvocations, repairedComponents, confirmationInvocations, cancellationInvocations;
        public List<string> changedAssets = new List<string>();
        public List<string> readyTypes = new List<string>();
        public List<string> foreignLegacyGuidsPreserved = new List<string>();
        public List<FileHash> beforeFiles = new List<FileHash>();
        public List<FileHash> afterFiles = new List<FileHash>();
    }
    [Serializable] public sealed class ComparisonPass
    {
        public string name;
        public int components, properties, objectReferences, duplicateCurrentComponents;
    }
    [Serializable] public sealed class Evidence
    {
        public string phase, scope, error, unity = Application.unityVersion;
        public bool passed;
        public int missingScripts, repairedComponents, inspectorTargets;
        public List<string> changedAssets = new List<string>();
        public List<string> selectedAssets = new List<string>();
        public List<string> readOnlyAssets = new List<string>();
        public List<string> checks = new List<string>();
        public List<EntryRecord> entries = new List<EntryRecord>();
        public List<string> issues = new List<string>();
        public List<NativeOwner> nativeOwners = new List<NativeOwner>();
        public List<BatchRecord> batches = new List<BatchRecord>();
        public List<ComparisonPass> comparisonPasses = new List<ComparisonPass>();
    }

    const string InitialPhasePending = "UnityBox.ComponentUpgradeFixture.PendingExplicitInitialPhase";
    const string LegacyCapturePending = "UnityBox.ComponentUpgradeFixture.PendingLegacyCapture";
    static bool initialActionPending;
    [InitializeOnLoadMethod]
    static void ResumeExplicitFixtureAfterDomainReload()
    {
        string requested = SessionState.GetString(InitialPhasePending, "");
        if (requested == "broken" || requested == "migrated")
        {
            EditorApplication.delayCall += () =>
            {
                if (current != null) return;
                Start(requested, requested == "broken" ? (Action)Broken : Replay);
                current.checks.Add("explicit initial phase resumed after startup domain reload; token cleared before action");
            };
        }
        if (!SessionState.GetBool(LegacyCapturePending, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(Output + "/legacy-state.json")) { SessionState.EraseBool(LegacyCapturePending); return; }
            Start("legacy", CaptureLegacy);
            current.checks.Add("legacy capture resumed after SDK first-import domain reload");
        };
    }
    public static void CreateLegacy() => Start("legacy", Create, true);
    public static void VerifyBroken() => Start("broken", Broken);
    public static void ApplyAndVerify() => Start("migrated", Apply);
    public static void ReplayWholeAssets() => Start("migrated", Replay);
    static void Replay()
    {
        AssertIsolation(true); CloseScenes(); var state = ReadState(); AssertExpectedBytes(state);
        foreach (string path in MainAssets)
        {
            if (path.EndsWith(".prefab"))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); int missing = Missing(root);
                Require(missing == Types.Length, "Wrong historical Missing count: " + path + "=" + missing);
                AssertSdkDescriptor(root); current.missingScripts += missing;
            }
            else
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int missing = scene.GetRootGameObjects().Sum(Missing);
                Require(missing == 4 * Types.Length, "Wrong historical scene Missing count: " + missing);
                foreach (var root in scene.GetRootGameObjects()) AssertSdkDescriptor(root);
                current.missingScripts += missing;
            }
        }
        Require(current.missingScripts == 7 * Types.Length, "Wrong complete historical Missing count");
        CloseScenes();
        current.checks.Add("Current-only cold import reproduced genuine historical GUID failures from immutable producer resources");
        Apply();
    }
    static void Start(string phase, Action action, bool immediate = false)
    {
        Directory.CreateDirectory(Output);
        current = new Evidence { phase = phase, scope = Configuration.scope };
        EditorApplication.update += Update;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        initialActionPending = phase != "legacy";
        if (initialActionPending) SessionState.SetString(InitialPhasePending, phase);
        if (immediate) { try { action(); } catch (Exception error) { Finish(error); } }
        else Later(action, 8);
    }
    static void Later(Action action, int wait = 5)
    {
        pending = action; frames = wait;
        if (current.phase == "legacy" && action == (Action)CaptureLegacy) SessionState.SetBool(LegacyCapturePending, true);
        File.WriteAllText(Output + "/" + current.phase + "-progress.txt", "Waiting for " + action.Method.Name);
        EditorApplication.QueuePlayerLoopUpdate();
    }
    static void Update()
    {
        if (EditorApplication.isCompiling) return;
        if (--frames > 0) { EditorApplication.QueuePlayerLoopUpdate(); return; }
        var action = pending; pending = null;
        if (action == null) return;
        if (initialActionPending) { initialActionPending = false; SessionState.EraseString(InitialPhasePending); }
        try { action(); } catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error = null)
    {
        foreach (var editor in inspectors) if (editor != null) Object.DestroyImmediate(editor);
        inspectors.Clear();
        SessionState.EraseString(InitialPhasePending);
        if (current.phase == "legacy") SessionState.EraseBool(LegacyCapturePending);
        current.passed = error == null;
        current.error = error == null ? "" : error.ToString();
        File.WriteAllText(Output + "/" + current.phase + ".json", JsonUtility.ToJson(current, true));
        EditorApplication.update -= Update;
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static Type Optional(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);
    static Type Find(string name) => Optional(name) ?? throw new InvalidOperationException("Type is not installed: " + name);
    static FieldInfo Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(instance.GetType().FullName + " lacks field " + name);
    static void Set(object instance, string name, object value)
    {
        var field = Field(instance, name); field.SetValue(instance, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
    }
    static object Get(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property != null ? property.GetValue(instance) : Field(instance, name).GetValue(instance);
    }
    static IList List(object instance, string name) => (IList)Field(instance, name).GetValue(instance);
    static object Row(object instance, string name)
    {
        var field = Field(instance, name); var row = Activator.CreateInstance(field.FieldType.GetGenericArguments()[0]);
        ((IList)field.GetValue(instance)).Add(row); return row;
    }
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2"))); }
    static string Id(Object value) => value == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
    static State ReadState() => JsonUtility.FromJson<State>(File.ReadAllText(Output + "/legacy-state.json"));
    static void WriteState(State state) => File.WriteAllText(Output + "/legacy-state.json", JsonUtility.ToJson(state, true));
    static void CloseScenes() { UnityEditor.SceneManagement.StageUtility.GoToMainStage(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    static void AssertIsolation(bool currentPackages)
    {
        Require(Owners.Length > 0 && Owners.Length == Configuration.owners.Length, "Unexpected fixture owner selection");
        Require(!Directory.Exists("Assets/UnityBox/ComponentReferenceMigration") && !Directory.Exists("Assets/UnityBoxComponentMigration"), "A standalone source module was copied into this consumer");
        Require(Optional("UnityBox.ComponentReferenceMigration.MigrationEngine") == null, "Standalone migration engine is installed");
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "UnityBox.ComponentReferenceMigration.Editor"), "Standalone migration assembly is loaded");
        foreach (var owner in AllOwners)
        {
            bool selected = Configuration.owners.Contains(owner.folder);
            foreach (string type in owner.types) Require((Optional(type) != null) == selected, "Unexpected peer/absent runtime component: " + type);
            if (!selected) Require(Optional(owner.api) == null, "Unselected plugin migration API is present: " + owner.api);
            else if (currentPackages)
            {
                var engine = Find(owner.api);
                Require(engine.GetMethod("Scan", new[] { typeof(IEnumerable<string>), typeof(Dictionary<string, string>) }) != null, "Embedded Scan API is missing: " + owner.api);
                Require(engine.GetMethods(BindingFlags.Public | BindingFlags.Static).Count(method => method.Name == "Apply") == 1, "Embedded Apply API is missing/ambiguous: " + owner.api);
                var proof = new NativeOwner { owner = owner.folder, package = owner.package, engineType = owner.api,
                    assembly = engine.Assembly.GetName().Name, assemblyPath = engine.Assembly.Location, assemblySha256 = Hash(File.ReadAllBytes(engine.Assembly.Location)) };
                string root = "Packages/" + owner.package;
                string ownNamespace = "UnityBox." + owner.folder + ".ComponentMigration";
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(v => v, StringComparer.Ordinal))
                    if (!file.Contains("/Tests/") && File.ReadAllText(file).Contains(ownNamespace))
                    {
                        proof.sourceFiles.Add(new FileHash { path = file, sha256 = Hash(File.ReadAllBytes(file)) });
                        if (File.Exists(file + ".meta")) proof.sourceFiles.Add(new FileHash { path = file + ".meta", sha256 = Hash(File.ReadAllBytes(file + ".meta")) });
                    }
                Require(proof.sourceFiles.Count > 0, "Compiled embedded API lacks package-owned source: " + owner.api);
                current.nativeOwners.Add(proof);
            }
        }
        current.checks.Add("only selected UnityBox plugins are installed; migration source/API is embedded in each current plugin package");
    }
    static void Override(GameObject root, Owner owner, bool variant)
    {
        var component = root.GetComponent(Find(owner.overrideType));
        Set(component, owner.overrideField, variant ? owner.variantValue : owner.instanceValue);
        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }
    static string DuplicateField(Owner owner) => owner.folder == "AdvancedCostumeController" ? "PartNamePrefixToRemove" : owner.overrideField;
    static void SetDuplicateMarker(Component component, Owner owner, bool added)
    {
        object value = owner.folder == "AdvancedCostumeController" || owner.folder == "AnimatorOptimizer"
            ? (object)(added ? "New duplicate value" : "Old duplicate value") : added ? (object)58.5f : 13.5f;
        Set(component, DuplicateField(owner), value);
    }
    static void Create()
    {
        AssertIsolation(false); CloseScenes(); Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var source = new Material(Shader.Find("Standard")) { name = "Original material", color = Color.red };
        var replacement = new Material(Shader.Find("Standard")) { name = "Replacement material", color = Color.green };
        AssetDatabase.CreateAsset(source, Folder + "/Original.mat"); AssetDatabase.CreateAsset(replacement, Folder + "/Replacement.mat");
        var mesh = new Mesh { name = "Legacy mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        mesh.AddBlendShapeFrame("Legacy PP", 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
        AssetDatabase.CreateAsset(mesh, Folder + "/Mesh.asset");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/User.controller");
        var clip = new AnimationClip { name = "User animation" };
        clip.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 2.75f));
        AssetDatabase.CreateAsset(clip, Folder + "/User.anim"); controller.layers[0].stateMachine.AddState("User state").motion = clip;
        var avatar = Avatar("Base avatar", source, replacement, mesh, controller);
        PrefabUtility.SaveAsPrefabAsset(avatar, BasePath); Object.DestroyImmediate(avatar);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        instance.name = "Variant avatar";
        foreach (var owner in Owners) Override(instance, owner, true);
        PrefabUtility.SaveAsPrefabAsset(instance, VariantPath); Object.DestroyImmediate(instance);
        var host = new GameObject("Nested host");
        var nested = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        nested.transform.SetParent(host.transform, false); PrefabUtility.SaveAsPrefabAsset(host, NestedPath); Object.DestroyImmediate(host);
        Avatar("Plain avatar", source, replacement, mesh, controller);
        instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath)); instance.name = "Prefab instance";
        foreach (var owner in Owners) Override(instance, owner, false);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NestedPath));
        Require(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath), "Could not save main scene"); CloseScenes();
        foreach (var owner in Owners)
        {
            var duplicate = new GameObject("Duplicate " + owner.folder);
            SetDuplicateMarker(duplicate.AddComponent(Find(owner.duplicateType)), owner, false);
            Require(EditorSceneManager.SaveScene(duplicate.scene, owner.duplicatePath), "Could not save duplicate scene: " + owner.folder); CloseScenes();
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); Later(CaptureLegacy);
    }
    static GameObject Avatar(string name, Material source, Material replacement, Mesh mesh, RuntimeAnimatorController controller)
    {
        var root = new GameObject(name);
        var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
        var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
        var renderer = body.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.sharedMaterial = source;
        var baseOutfit = new GameObject("Outfit base"); baseOutfit.transform.SetParent(root.transform, false);
        var baseRenderer = baseOutfit.AddComponent<MeshRenderer>(); baseRenderer.sharedMaterial = source;
        var configs = Types.Select(type => (MonoBehaviour)root.AddComponent(Find(type))).ToArray();
        var byType = configs.ToDictionary(component => component.GetType().FullName);
        if (byType.TryGetValue(Outfit, out var outfit))
        {
            Set(outfit, "PartNamePrefixToRemove", "Legacy_"); Set(outfit, "PartNameSuffixToRemove", "_Tail");
            Set(outfit, "PartNameRegexPattern", "^Legacy_(.*)$"); Set(outfit, "PartNameRegexReplacement", "$1");
        }
        if (byType.TryGetValue(Part, out var part)) { Set(part, "Mode", 1); Set(part, "GroupName", "Stored group"); }
        if (byType.TryGetValue(Variant, out var variant))
        {
            Set(variant, "OutfitBase", baseOutfit); List(variant, "Replacements").Clear();
            var row = Row(variant, "Replacements"); Set(row, "Source", source); Set(row, "Replacement", replacement);
            List(variant, "RendererOverrides").Clear(); row = Row(variant, "RendererOverrides");
            Set(row, "TargetRenderer", baseRenderer); Set(row, "MaterialSlot", 0); Set(row, "Source", source); Set(row, "Replacement", replacement);
            Set(variant, "editorPreviewInitialized", false);
        }
        if (byType.TryGetValue(Ass, out var ass))
        {
            Set(ass, "gesturePassword", new List<int> { 7, 0, 6, 2, 2 }); Set(ass, "useRightHand", true);
            Set(ass, "countdownDuration", 17.5f); Set(ass, "warningThreshold", 1.9f); Set(ass, "gestureHoldTime", 0.22f);
            Set(ass, "gestureErrorTolerance", 0.44f); Set(ass, "gestureMaxHoldTime", 2.8f); Set(ass, "writeDefaultsMode", 1);
            Set(ass, "disableDefense", true); Set(ass, "disableRootChildren", false); Set(ass, "disableOverlay", true);
            Set(ass, "disableWarningSound", true); Set(ass, "disableObfuscation", true); Set(ass, "enableDecoyLayers", false); Set(ass, "enableDecoyStates", false);
            Set(ass, "warningBeep", AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Warning.wav"));
            Set(ass, "successSound", AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Success.wav"));
            Require((Object)Field(ass, "warningBeep").GetValue(ass) != null, "Warning audio was not imported");
        }
        if (byType.TryGetValue(Viseme, out var viseme))
        {
            Set(viseme, "targetRenderer", renderer); Set(viseme, "writeDefaultsMode", 2);
            Set(viseme, "globalWeight", 42f); Set(viseme, "voiceModulationMode", 0); Set(viseme, "voiceMin", 0.2f); Set(viseme, "voiceMax", 0.8f);
            List(viseme, "bindings").Clear();
            for (int index = 0; index < 15; index++) Set(Row(viseme, "bindings"), "viseme", index);
            var row = List(viseme, "bindings")[1]; Set(row, "blendshapeName", "Legacy PP"); Set(row, "useCustomSettings", true);
            Set(row, "weight", 37f); Set(row, "voiceMode", 2); Set(row, "voiceMin", 0.3f); Set(row, "voiceMax", 0.7f);
        }
        if (byType.TryGetValue(Optimizer, out var optimizer))
        {
            Set(optimizer, "targetMode", 2); Set(optimizer, "playableLayer", 2); Set(optimizer, "animator", animator); Set(optimizer, "controller", controller);
            Set(optimizer, "optimizeAllConvertibleLayers", false); Set(optimizer, "layerIndices", new List<int> { 3, 1 });
            Set(optimizer, "upgradeBoolParametersToFloat", false); Set(optimizer, "upgradeIntParametersToFloat", false);
            Set(optimizer, "writeDefaultsPolicy", 0); Set(optimizer, "strictCurveConflicts", false); Set(optimizer, "generatedLayerName", "Legacy AO Preserve");
            Set(optimizer, "weightParameterPrefix", "LegacyAO/Weight"); Set(optimizer, "maxPriorityCombinations", 128);
        }
        Set(root.AddComponent(Find(Holder)), "references", configs);
        var descriptor = root.AddComponent(Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"));
        Set(descriptor, "ViewPosition", new Vector3(0.1f, 1.73f, 0.2f));
        foreach (var component in configs) EditorUtility.SetDirty(component);
        return root;
    }
    static void CaptureLegacy()
    {
        AssertIsolation(false); var state = new State { scope = Configuration.scope };
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        foreach (var owner in Owners)
        {
            foreach (string name in owner.types)
            {
                var script = MonoScript.FromMonoBehaviour((MonoBehaviour)root.GetComponent(Find(name))); string path = AssetDatabase.GetAssetPath(script);
                state.types.Add(new TypeRecord { owner = owner.folder, type = name, guid = AssetDatabase.AssetPathToGUID(path), scriptPath = path, generatedMetaSha256 = Hash(File.ReadAllBytes(path + ".meta")) });
            }
            state.duplicates.Add(new DuplicateRecord { owner = owner.folder, asset = owner.duplicatePath, type = owner.duplicateType });
        }
        Require(state.types.Select(item => item.guid).Distinct().Count() == Types.Length, "Legacy scripts did not receive distinct identities");
        foreach (string path in Candidates) CaptureAsset(path, state.components);
        CloseScenes(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        foreach (string path in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
        {
            if (path.EndsWith(".meta", StringComparison.Ordinal)) continue;
            StoreOriginal(state, path); if (File.Exists(path + ".meta")) StoreOriginal(state, path + ".meta");
        }
        WriteState(state);
        current.checks.Add("actual selected old package imports generated " + Types.Length + " script identities; no legacy GUID was invented");
        current.checks.Add("non-default fields, AudioClip/Material/Renderer/Animator PPtrs, variant/nested/instance overrides and per-owner duplicate scenes saved");
        current.checks.Add("all component GlobalObjectIDs, asset GUIDs and original bytes recorded"); Finish();
    }
    static void StoreOriginal(State state, string path)
    {
        string destination = Output + "/original/" + path; Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, true);
        state.files.Add(new FileRecord { path = path, original = destination, sha256 = Hash(File.ReadAllBytes(path)), guid = AssetDatabase.AssetPathToGUID(path.EndsWith(".meta") ? path.Substring(0, path.Length - 5) : path) });
    }
    static string OwnerPath(Transform transform)
    {
        var names = new List<string>(); for (var value = transform; value != null; value = value.parent) names.Add(value.name);
        names.Reverse(); return string.Join("/", names);
    }
    static IEnumerable<MonoBehaviour> Owned(GameObject root) => root.GetComponentsInChildren<MonoBehaviour>(true)
        .Where(component => component != null && (Types.Contains(component.GetType().FullName) || component.GetType().FullName == Holder));
    static void CaptureAsset(string path, List<ComponentRecord> records)
    {
        if (path.EndsWith(".prefab")) CaptureRoot(AssetDatabase.LoadAssetAtPath<GameObject>(path), path, records);
        else
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects()) CaptureRoot(root, path, records);
        }
    }
    static void CaptureRoot(GameObject root, string path, List<ComponentRecord> records)
    {
        foreach (var component in Owned(root))
        {
            var record = new ComponentRecord { asset = path, owner = OwnerPath(component.transform), type = component.GetType().FullName, globalId = Id(component) };
            var serialized = new SerializedObject(component); var property = serialized.GetIterator(); bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = property.propertyType != SerializedPropertyType.ObjectReference;
                if (property.propertyPath.StartsWith("m_", StringComparison.Ordinal)) continue;
                string value = Value(property);
                if (value != null) record.properties.Add(new PropertyRecord { path = property.propertyPath, kind = property.propertyType.ToString(), value = value });
            }
            records.Add(record);
        }
    }
    static string Value(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: case SerializedPropertyType.ArraySize: return property.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
            case SerializedPropertyType.Float: return property.floatValue.ToString("R", CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.ObjectReference: return Id(property.objectReferenceValue);
            default: return null;
        }
    }
    static byte[] Baseline(State state, FileRecord file)
    {
        var duplicate = state.duplicates.FirstOrDefault(value => value.asset == file.path);
        return File.ReadAllBytes(duplicate != null && !string.IsNullOrEmpty(duplicate.currentBytes) ? duplicate.currentBytes : file.original);
    }
    static Dictionary<string, byte[]> CurrentBytes(State state) => state.files.ToDictionary(file => file.path, file => File.ReadAllBytes(file.path), StringComparer.Ordinal);
    static void AssertExpectedBytes(State state, Dictionary<string, string> mapping = null)
    {
        foreach (var file in state.files)
        {
            byte[] expected = Baseline(state, file);
            if (mapping != null && MainAssets.Contains(file.path)) expected = ReplaceScriptGuids(expected, mapping);
            Require(File.ReadAllBytes(file.path).SequenceEqual(expected), "Unexpected source bytes: " + file.path);
            string path = file.path.EndsWith(".meta") ? file.path.Substring(0, file.path.Length - 5) : file.path;
            Require(AssetDatabase.AssetPathToGUID(path) == file.guid, "Asset GUID changed: " + path);
        }
    }
    static int Missing(GameObject root) => root.GetComponentsInChildren<Transform>(true).Sum(value => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(value.gameObject));
    static void AssertSdkDescriptor(GameObject root)
    {
        var descriptorType = Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        foreach (Component holder in root.GetComponentsInChildren(Find(Holder), true))
        {
            var descriptor = holder.GetComponent(descriptorType); Require(descriptor != null, "SDK descriptor became unavailable");
            Require((Vector3)Field(descriptor, "ViewPosition").GetValue(descriptor) == new Vector3(0.1f, 1.73f, 0.2f), "SDK descriptor configuration changed");
        }
    }
    static void Broken()
    {
        AssertIsolation(true); CloseScenes(); var state = ReadState(); AssertExpectedBytes(state);
        foreach (var type in state.types) current.checks.Add("legacy GUID path after current package import: " + type.type + "=" + AssetDatabase.GUIDToAssetPath(type.guid));
        foreach (string path in MainAssets)
        {
            if (path.EndsWith(".prefab"))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); int missing = Missing(root);
                Require(missing == Types.Length, "Wrong legacy Missing count in " + path + ": " + missing);
                AssertSdkDescriptor(root); current.missingScripts += missing;
            }
            else
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); int missing = scene.GetRootGameObjects().Sum(Missing);
                Require(missing == 4 * Types.Length, "Wrong legacy Missing count in main scene: " + missing);
                foreach (var root in scene.GetRootGameObjects()) AssertSdkDescriptor(root);
                current.missingScripts += missing;
            }
        }
        Require(current.missingScripts == 7 * Types.Length, "Wrong total legacy Missing count");
        CloseScenes(); AssertExpectedBytes(state);
        current.checks.Add("updating only selected plugin ZIPs reproduced Missing Script; importing/opening retained original YAML, metadata and SDK descriptor configuration");
        foreach (var record in state.duplicates)
        {
            var owner = Owners.Single(value => value.folder == record.owner);
            var scene = EditorSceneManager.OpenScene(record.asset, OpenSceneMode.Single);
            var duplicate = scene.GetRootGameObjects().Single(value => value.name == "Duplicate " + owner.folder);
            Require(Missing(duplicate) == 1, "Duplicate fixture lost its old missing component: " + owner.folder);
            var added = (MonoBehaviour)duplicate.AddComponent(Find(record.type)); SetDuplicateMarker(added, owner, true); EditorUtility.SetDirty(added);
            Require(EditorSceneManager.SaveScene(scene, record.asset), "Could not save explicit old/new duplicate: " + owner.folder);
            record.newGlobalId = Id(added); record.markerField = DuplicateField(owner);
            record.markerValue = Value(new SerializedObject(added).FindProperty(record.markerField));
            record.currentBytes = Output + "/duplicate-before-apply-" + owner.folder + ".unity";
            File.Copy(record.asset, record.currentBytes, true); CloseScenes(); AssertExpectedBytes(state);
        }
        WriteState(state);
        Later(() => { AssertExpectedBytes(state); Finish(); });
    }
    static void Apply()
    {
        AssertIsolation(true); CloseScenes(); var state = ReadState(); AssertExpectedBytes(state);
        current.selectedAssets.Add("Assets (menu command, no selection restriction)");
        Selection.activeObject = AssetDatabase.LoadMainAssetAtPath("Assets/SelectionSentinel.txt");
        Require(Selection.activeObject != null, "Unrelated selection sentinel was not imported");
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var processedOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in Owners)
        {
            CloseScenes(); AssertExpectedBytes(state, mapping);
            var engine = Find(owner.api);
            var scan = engine.GetMethod("Scan", new[] { typeof(IEnumerable<string>), typeof(Dictionary<string, string>) });
            var overrides = new Dictionary<string, string>();
            Require(overrides.Count == 0, "Whole project route must not require caller GUID mappings");
            var duplicate = state.duplicates.Single(value => value.owner == owner.folder);
            current.readOnlyAssets.Add(duplicate.asset);
            var duplicatePlan = scan.Invoke(null, new object[] { new[] { duplicate.asset }, overrides });
            var duplicateEntries = RecordPlan(duplicatePlan, owner);
            Require(duplicateEntries.Any(row => row.asset == duplicate.asset && row.type == duplicate.type && row.status == "Duplicate"), "Owner did not report its old/new duplicate: " + owner.folder);
            Require(!duplicateEntries.Any(row => row.status == "Ready"), "Duplicate plan unexpectedly permits a write: " + owner.folder);
            AssertExpectedBytes(state, mapping);
            var plan = scan.Invoke(null, new object[] { MainAssets, overrides });
            var entries = RecordPlan(plan, owner);
            var ready = entries.Where(row => row.status == "Ready").ToArray();
            Require(ready.All(row => owner.types.Contains(row.type)), "Owner catalog offered to modify a peer plugin: " + owner.folder);
            Require(owner.types.All(type => ready.Any(row => row.type == type)), "Owner catalog did not recognize all its legacy types: " + owner.folder);
            var ownMapping = ready.GroupBy(row => row.legacyGuid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().targetGuid, StringComparer.OrdinalIgnoreCase);
            Require(ownMapping.Count == owner.types.Length, "Unexpected owner GUID mapping count: " + owner.folder);
            AssertExpectedBytes(state, mapping);
            var before = CurrentBytes(state);
            var batch = new BatchRecord { owner = owner.folder, engineType = owner.api };
            batch.readyTypes.AddRange(ready.Select(row => row.type).Distinct().OrderBy(value => value, StringComparer.Ordinal));
            foreach (var item in before) batch.beforeFiles.Add(new FileHash { path = item.Key, sha256 = Hash(item.Value) });
            current.batches.Add(batch);
            batch.applyInvocations++;
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath("Assets/SelectionSentinel.txt");
            Require(Selection.activeObject != null, "Menu-time selection sentinel missing");
            var action = Find("UnityBox." + owner.folder + ".ComponentMigration.ComponentReferenceUpgrade");
            var execute = action.GetMethod("Execute", new[] { typeof(Func<bool>) });
            Require(execute != null, "Exact menu execution route is missing");
            var cancelled = execute.Invoke(null, new object[] { new Func<bool>(() => { batch.cancellationInvocations++; return false; }) });
            Require(cancelled == null && batch.cancellationInvocations == 1, "Cancel did not stop the menu action");
            foreach (var item in before) Require(File.ReadAllBytes(item.Key).SequenceEqual(item.Value), "Cancel wrote an asset");
            var result = execute.Invoke(null, new object[] { new Func<bool>(() => { batch.confirmationInvocations++; return true; }) });
            Require(batch.confirmationInvocations == 1, "Menu requested more than one confirmation");
            Require(AssetDatabase.GetAssetPath(Selection.activeObject) == "Assets/SelectionSentinel.txt", "Unrelated selection was not preserved");
            batch.repairedComponents = (int)Get(result, "RepairedComponents"); batch.backupDirectory = (string)Get(result, "BackupDirectory");
            foreach (string path in (IEnumerable)Get(result, "ChangedAssets")) batch.changedAssets.Add(path);
            Require(batch.applyInvocations == 1 && batch.repairedComponents == 2 * owner.types.Length, "Wrong explicit batch count/repair count: " + owner.folder);
            Require(new HashSet<string>(batch.changedAssets).SetEquals(new[] { BasePath, ScenePath }), "Owner wrote an unexpected asset set: " + owner.folder);
            string expectedBackupRoot = Path.GetFullPath("UserSettings/UnityBox/" + owner.folder + "/ComponentReferenceMigration") + Path.DirectorySeparatorChar;
            Require(Path.GetFullPath(batch.backupDirectory).StartsWith(expectedBackupRoot, StringComparison.Ordinal), "Backup belongs to another plugin: " + owner.folder);
            foreach (string path in batch.changedAssets)
                Require(File.ReadAllBytes(Path.Combine(batch.backupDirectory, path)).SequenceEqual(before[path]), "Backup differs from immediate pre-owner bytes: " + owner.folder + "/" + path);
            foreach (var pair in ownMapping) { Require(!mapping.ContainsKey(pair.Key), "An earlier owner already changed this legacy GUID"); mapping.Add(pair.Key, pair.Value); }
            processedOwners.Add(owner.folder);
            AssertExpectedBytes(state, mapping);
            foreach (var foreign in state.types.Where(type => !processedOwners.Contains(type.owner)))
            {
                foreach (var file in state.files)
                    Require(CountScriptGuid(before[file.path], foreign.guid) == CountScriptGuid(File.ReadAllBytes(file.path), foreign.guid), "Owner changed a peer's legacy GUID bytes: " + owner.folder + " -> " + foreign.type);
                batch.foreignLegacyGuidsPreserved.Add(foreign.guid);
            }
            foreach (var file in state.files) batch.afterFiles.Add(new FileHash { path = file.path, sha256 = Hash(File.ReadAllBytes(file.path)) });
            current.repairedComponents += batch.repairedComponents;
            foreach (string path in batch.changedAssets) if (!current.changedAssets.Contains(path)) current.changedAssets.Add(path);
        }
        Require(current.batches.Count == Owners.Length && current.repairedComponents == 2 * Types.Length, "Not every installed owner completed one explicit batch");
        current.checks.Add("one confirmation through the exact Tools action per owner; automatic whole-Assets upgrade without resource selection or GUID overrides");
        current.checks.Add("every batch preserved peer legacy GUID bytes, used an owner-local exact pre-write backup, and changed only its confirmed m_Script GUID spans");
        current.checks.Add("whole-Assets duplicate cases were reported and preserved; no old/current component was deleted or merged");
        current.comparisonPasses.Add(VerifyTypedState(state, false, "after owner batches"));
        var mainScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var component in mainScene.GetRootGameObjects().SelectMany(Owned))
            if (component.GetType().FullName != Holder) inspectors.Add(UnityEditor.Editor.CreateEditor(component));
        current.inspectorTargets = inspectors.Count;
        Require(current.inspectorTargets == 4 * Types.Length, "Wrong Inspector target count");
        Later(() =>
        {
            current.comparisonPasses.Add(VerifyTypedState(state, true, "after Inspector OnEnable/deferred callbacks"));
            foreach (var editor in inspectors) if (editor != null) Object.DestroyImmediate(editor);
            inspectors.Clear(); AssertExpectedBytes(state, mapping);
            current.checks.Add("all typed component identities, fields, parent ObjectReference GlobalObjectIDs, variant/nested/instance overrides and SDK descriptor configuration preserved");
            current.checks.Add("creating current Inspectors and waiting for deferred callbacks did not initialize or normalize source configuration"); Finish();
        }, 8);
    }
    static List<EntryRecord> RecordPlan(object plan, Owner owner)
    {
        var rows = new List<EntryRecord>();
        foreach (var entry in (IEnumerable)Get(plan, "Entries"))
        {
            var row = new EntryRecord { owner = owner.folder, asset = (string)Get(entry, "AssetPath"), componentFileId = (string)Get(entry, "ComponentFileId"),
                legacyGuid = (string)Get(entry, "LegacyGuid"), targetGuid = (string)Get(entry, "TargetGuid"), type = (string)Get(entry, "TypeName"), status = Get(entry, "Status").ToString(), details = (string)Get(entry, "Details") };
            current.entries.Add(row); rows.Add(row);
        }
        foreach (var issue in (IEnumerable)Get(plan, "Issues")) current.issues.Add(owner.folder + ":" + (string)Get(issue, "Code") + ": " + (string)Get(issue, "Message"));
        return rows;
    }
    static readonly Regex ScriptGuid = new Regex(@"(?m)^(?<lead>[ \t]*m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*)(?<guid>[a-fA-F0-9]{32})(?<tail>,\s*type:\s*3\})");
    static byte[] ReplaceScriptGuids(byte[] bytes, Dictionary<string, string> mapping) => Encoding.UTF8.GetBytes(ScriptGuid.Replace(Encoding.UTF8.GetString(bytes), match =>
        mapping.TryGetValue(match.Groups["guid"].Value, out var target) ? match.Groups["lead"].Value + target + match.Groups["tail"].Value : match.Value));
    static int CountScriptGuid(byte[] bytes, string guid) => ScriptGuid.Matches(Encoding.UTF8.GetString(bytes)).Cast<Match>()
        .Count(match => string.Equals(match.Groups["guid"].Value, guid, StringComparison.OrdinalIgnoreCase));
    static ComparisonPass VerifyTypedState(State state, bool keepCurrentScene, string name)
    {
        var pass = new ComparisonPass { name = name };
        foreach (string path in MainAssets)
        {
            GameObject[] roots;
            if (path.EndsWith(".prefab")) roots = new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) };
            else if (keepCurrentScene && SceneManager.GetActiveScene().path == path) roots = SceneManager.GetActiveScene().GetRootGameObjects();
            else roots = EditorSceneManager.OpenScene(path, OpenSceneMode.Single).GetRootGameObjects();
            Require(roots.Sum(Missing) == 0, "Missing Script remained in repaired asset: " + path);
            foreach (var root in roots) AssertSdkDescriptor(root);
            var components = roots.SelectMany(Owned).ToDictionary(Id);
            foreach (var expected in state.components.Where(value => value.asset == path))
            {
                Require(components.TryGetValue(expected.globalId, out var component), "Component identity changed: " + path + "/" + expected.owner + "/" + expected.type);
                Require(component.GetType().FullName == expected.type, "Component resolved to another type: " + expected.globalId);
                var serialized = new SerializedObject(component); pass.components++;
                foreach (var property in expected.properties)
                {
                    var actual = serialized.FindProperty(property.path);
                    Require(actual != null, "Stored field disappeared: " + expected.type + "." + property.path);
                    Require(Value(actual) == property.value, "Stored value/PPtr changed: " + expected.type + "." + property.path + "; expected " + property.value + ", got " + Value(actual));
                    pass.properties++;
                    if (property.kind == "ObjectReference") pass.objectReferences++;
                }
            }
        }
        Require(pass.components == state.components.Count(component => MainAssets.Contains(component.asset)), "Not every original selected component was compared");
        if (!keepCurrentScene)
        {
            foreach (var record in state.duplicates)
            {
                var scene = EditorSceneManager.OpenScene(record.asset, OpenSceneMode.Single);
                var root = scene.GetRootGameObjects().Single(value => value.name == "Duplicate " + record.owner);
                Require(Missing(root) == 1, "Read-only duplicate lost its missing old component: " + record.owner);
                var added = root.GetComponent(Find(record.type));
                Require(added != null && Id(added) == record.newGlobalId, "Current duplicate component identity changed: " + record.owner);
                Require(Value(new SerializedObject(added).FindProperty(record.markerField)) == record.markerValue, "Current duplicate configuration changed: " + record.owner);
                Require(File.ReadAllBytes(record.asset).SequenceEqual(File.ReadAllBytes(record.currentBytes)), "Read-only duplicate bytes changed: " + record.owner);
                pass.duplicateCurrentComponents++;
            }
            Require(pass.duplicateCurrentComponents == Owners.Length, "Not every owner duplicate was preserved"); CloseScenes();
        }
        return pass;
    }
}
