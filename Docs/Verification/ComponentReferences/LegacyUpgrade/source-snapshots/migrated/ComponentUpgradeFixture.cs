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
    const string BasePath = Folder + "/Base.prefab";
    const string VariantPath = Folder + "/Variant.prefab";
    const string NestedPath = Folder + "/Nested.prefab";
    const string ScenePath = Folder + "/Avatar.unity";
    const string DuplicatePath = Folder + "/Duplicate.unity";
    const string Outfit = "UnityBox.AdvancedCostumeController.ACCOutfitMarker";
    const string Part = "UnityBox.AdvancedCostumeController.ACCPartGroupMarker";
    const string Variant = "UnityBox.AdvancedCostumeController.ACCVariantMaterialOverride";
    const string Ass = "UnityBox.AvatarSecuritySystem.ASSComponent";
    const string Viseme = "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent";
    const string Optimizer = "UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent";
    const string Holder = "UnityBox.ComponentUpgradeFixtureRuntime.ComponentUpgradeReferenceHolder";
    static readonly string[] Types = { Outfit, Part, Variant, Ass, Viseme, Optimizer };
    static readonly string[] Candidates = { BasePath, VariantPath, NestedPath, ScenePath, DuplicatePath };
    static Action pending;
    static int frames;
    static Evidence current;
    static readonly List<UnityEditor.Editor> inspectors = new List<UnityEditor.Editor>();

    [Serializable] public sealed class TypeRecord { public string type, guid, scriptPath, generatedMetaSha256; }
    [Serializable] public sealed class FileRecord { public string path, sha256, guid, original; }
    [Serializable] public sealed class PropertyRecord { public string path, kind, value; }
    [Serializable] public sealed class ComponentRecord
    {
        public string asset, owner, type, globalId;
        public List<PropertyRecord> properties = new List<PropertyRecord>();
    }
    [Serializable] public sealed class State
    {
        public List<TypeRecord> types = new List<TypeRecord>();
        public List<FileRecord> files = new List<FileRecord>();
        public List<ComponentRecord> components = new List<ComponentRecord>();
        public string duplicateCurrentBytes, duplicateNewGlobalId;
    }
    [Serializable] public sealed class EntryRecord
    {
        public string asset, componentFileId, legacyGuid, targetGuid, type, status, details;
    }
    [Serializable] public sealed class Evidence
    {
        public string phase, error, unity = Application.unityVersion;
        public bool passed;
        public int missingScripts, repairedComponents, inspectorTargets;
        public string backupDirectory;
        public List<string> changedAssets = new List<string>();
        public List<string> selectedAssets = new List<string>();
        public List<string> readOnlyAssets = new List<string>();
        public List<string> checks = new List<string>();
        public List<EntryRecord> entries = new List<EntryRecord>();
        public List<string> issues = new List<string>();
    }

    public static void CreateLegacy() => Start("legacy", Create, true);
    public static void VerifyBroken() => Start("broken", Broken);
    public static void ApplyAndVerify() => Start("migrated", Apply);

    static void Start(string phase, Action action, bool immediate = false)
    {
        Directory.CreateDirectory(Output);
        current = new Evidence { phase = phase };
        EditorApplication.update += Update;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        if (immediate)
        {
            try { action(); }
            catch (Exception error) { Finish(error); }
        }
        else Later(action, 8);
    }
    static void Later(Action action, int wait = 5)
    {
        pending = action; frames = wait;
        File.WriteAllText(Output + "/" + current.phase + "-progress.txt", "Waiting for " + action.Method.Name);
        EditorApplication.QueuePlayerLoopUpdate();
    }
    static void Update()
    {
        if (EditorApplication.isCompiling) return;
        if (--frames > 0) { EditorApplication.QueuePlayerLoopUpdate(); return; }
        var action = pending; pending = null;
        if (action == null) return;
        try { action(); }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error = null)
    {
        foreach (var editor in inspectors) if (editor != null) Object.DestroyImmediate(editor);
        inspectors.Clear();
        current.passed = error == null;
        current.error = error == null ? "" : error.ToString();
        File.WriteAllText(Output + "/" + current.phase + ".json", JsonUtility.ToJson(current, true));
        EditorApplication.update -= Update;
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null)
        ?? throw new InvalidOperationException("Type is not installed: " + name);
    static FieldInfo Field(object instance, string name) => instance.GetType().GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(instance.GetType().FullName + " lacks field " + name);
    static void Set(object instance, string name, object value)
    {
        var field = Field(instance, name);
        field.SetValue(instance, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
    }
    static object Get(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property != null ? property.GetValue(instance) : Field(instance, name).GetValue(instance);
    }
    static IList List(object instance, string name) => (IList)Field(instance, name).GetValue(instance);
    static object Row(object instance, string name)
    {
        var field = Field(instance, name);
        var row = Activator.CreateInstance(field.FieldType.GetGenericArguments()[0]);
        ((IList)field.GetValue(instance)).Add(row);
        return row;
    }
    static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }
    static string Id(Object value) => value == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
    static State ReadState() => JsonUtility.FromJson<State>(File.ReadAllText(Output + "/legacy-state.json"));
    static void WriteState(State state) => File.WriteAllText(Output + "/legacy-state.json", JsonUtility.ToJson(state, true));
    static void CloseScenes()
    {
        UnityEditor.SceneManagement.StageUtility.GoToMainStage();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    static void Create()
    {
        CloseScenes();
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var source = new Material(Shader.Find("Standard")) { name = "Original material", color = Color.red };
        var replacement = new Material(Shader.Find("Standard")) { name = "Replacement material", color = Color.green };
        AssetDatabase.CreateAsset(source, Folder + "/Original.mat");
        AssetDatabase.CreateAsset(replacement, Folder + "/Replacement.mat");
        var mesh = new Mesh { name = "Legacy mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        mesh.AddBlendShapeFrame("Legacy PP", 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
        AssetDatabase.CreateAsset(mesh, Folder + "/Mesh.asset");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/User.controller");
        var clip = new AnimationClip { name = "User animation" };
        clip.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 2.75f));
        AssetDatabase.CreateAsset(clip, Folder + "/User.anim");
        controller.layers[0].stateMachine.AddState("User state").motion = clip;
        var avatar = Avatar("Base avatar", source, replacement, mesh, controller);
        PrefabUtility.SaveAsPrefabAsset(avatar, BasePath);
        Object.DestroyImmediate(avatar);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        instance.name = "Variant avatar";
        var ass = instance.GetComponent(Find(Ass)); Set(ass, "countdownDuration", 21f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ass);
        PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
        Object.DestroyImmediate(instance);

        var host = new GameObject("Nested host");
        var nested = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        nested.transform.SetParent(host.transform, false);
        PrefabUtility.SaveAsPrefabAsset(host, NestedPath);
        Object.DestroyImmediate(host);

        Avatar("Plain avatar", source, replacement, mesh, controller);
        instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        instance.name = "Prefab instance";
        ass = instance.GetComponent(Find(Ass)); Set(ass, "countdownDuration", 24f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ass);
        var part = instance.GetComponent(Find(Part)); Set(part, "GroupName", "Scene override group");
        PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NestedPath));
        Require(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath), "Could not save main scene");
        CloseScenes();
        var duplicate = new GameObject("Duplicate fixture");
        Set(duplicate.AddComponent(Find(Outfit)), "PartNamePrefixToRemove", "Old duplicate prefix");
        Require(EditorSceneManager.SaveScene(duplicate.scene, DuplicatePath), "Could not save duplicate scene");
        CloseScenes();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Later(CaptureLegacy);
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
        var outfit = configs[0]; Set(outfit, "PartNamePrefixToRemove", "Legacy_"); Set(outfit, "PartNameSuffixToRemove", "_Tail");
        Set(outfit, "PartNameRegexPattern", "^Legacy_(.*)$"); Set(outfit, "PartNameRegexReplacement", "$1");
        Set(configs[1], "Mode", 1); Set(configs[1], "GroupName", "Stored group");
        var variant = configs[2]; Set(variant, "OutfitBase", baseOutfit); List(variant, "Replacements").Clear();
        var row = Row(variant, "Replacements"); Set(row, "Source", source); Set(row, "Replacement", replacement);
        List(variant, "RendererOverrides").Clear(); row = Row(variant, "RendererOverrides");
        Set(row, "TargetRenderer", baseRenderer); Set(row, "MaterialSlot", 0); Set(row, "Source", source); Set(row, "Replacement", replacement);
        // Keep the stored false state: merely opening the current Inspector must not initialize it.
        Set(variant, "editorPreviewInitialized", false);
        var ass = configs[3]; Set(ass, "gesturePassword", new List<int> { 7, 0, 6, 2, 2 }); Set(ass, "useRightHand", true);
        Set(ass, "countdownDuration", 17.5f); Set(ass, "warningThreshold", 1.9f); Set(ass, "gestureHoldTime", 0.22f);
        Set(ass, "gestureErrorTolerance", 0.44f); Set(ass, "gestureMaxHoldTime", 2.8f); Set(ass, "writeDefaultsMode", 1);
        Set(ass, "disableDefense", true); Set(ass, "disableRootChildren", false); Set(ass, "disableOverlay", true);
        Set(ass, "disableWarningSound", true); Set(ass, "disableObfuscation", true); Set(ass, "enableDecoyLayers", false); Set(ass, "enableDecoyStates", false);
        Set(ass, "warningBeep", AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Warning.wav"));
        Set(ass, "successSound", AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Success.wav"));
        Require((Object)Field(ass, "warningBeep").GetValue(ass) != null, "Warning audio was not imported");
        var viseme = configs[4]; Set(viseme, "targetRenderer", renderer); Set(viseme, "writeDefaultsMode", 2);
        Set(viseme, "globalWeight", 42f); Set(viseme, "voiceModulationMode", 0); Set(viseme, "voiceMin", 0.2f); Set(viseme, "voiceMax", 0.8f);
        List(viseme, "bindings").Clear();
        for (int visemeIndex = 0; visemeIndex < 15; visemeIndex++)
        {
            row = Row(viseme, "bindings"); Set(row, "viseme", visemeIndex);
        }
        row = List(viseme, "bindings")[1]; Set(row, "blendshapeName", "Legacy PP");
        Set(row, "useCustomSettings", true); Set(row, "weight", 37f); Set(row, "voiceMode", 2); Set(row, "voiceMin", 0.3f); Set(row, "voiceMax", 0.7f);
        var optimizer = configs[5]; Set(optimizer, "targetMode", 2); Set(optimizer, "playableLayer", 2);
        Set(optimizer, "animator", animator); Set(optimizer, "controller", controller); Set(optimizer, "optimizeAllConvertibleLayers", false);
        Set(optimizer, "layerIndices", new List<int> { 3, 1 }); Set(optimizer, "upgradeBoolParametersToFloat", false); Set(optimizer, "upgradeIntParametersToFloat", false);
        Set(optimizer, "writeDefaultsPolicy", 0); Set(optimizer, "strictCurveConflicts", false);
        Set(optimizer, "generatedLayerName", "Legacy AO Preserve"); Set(optimizer, "weightParameterPrefix", "LegacyAO/Weight"); Set(optimizer, "maxPriorityCombinations", 128);
        Set(root.AddComponent(Find(Holder)), "references", configs);
        // Real SDK component participates in the same prefab without being a migration candidate.
        var descriptor = root.AddComponent(Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"));
        Set(descriptor, "ViewPosition", new Vector3(0.1f, 1.73f, 0.2f));
        foreach (var component in configs) EditorUtility.SetDirty(component);
        return root;
    }

    static void CaptureLegacy()
    {
        var state = new State();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        foreach (string name in Types)
        {
            var script = MonoScript.FromMonoBehaviour((MonoBehaviour)root.GetComponent(Find(name)));
            string path = AssetDatabase.GetAssetPath(script);
            state.types.Add(new TypeRecord { type = name, guid = AssetDatabase.AssetPathToGUID(path), scriptPath = path,
                generatedMetaSha256 = Hash(File.ReadAllBytes(path + ".meta")) });
        }
        Require(state.types.Select(item => item.guid).Distinct().Count() == 6, "Legacy scripts did not receive six distinct identities");
        foreach (string path in Candidates) CaptureAsset(path, state.components);
        CloseScenes();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        foreach (string path in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!File.Exists(path) || path.EndsWith(".meta", StringComparison.Ordinal)) continue;
            StoreOriginal(state, path); if (File.Exists(path + ".meta")) StoreOriginal(state, path + ".meta");
        }
        WriteState(state);
        current.checks.Add("actual old package imports created six script GUIDs; no legacy GUID was fabricated");
        current.checks.Add("non-default fields, AudioClip/Material/Renderer/Animator PPtrs, cross-component references, prefab variants/nesting and instance overrides saved");
        current.checks.Add("all component GlobalObjectIDs, asset GUIDs and original bytes recorded");
        Finish();
    }
    static void StoreOriginal(State state, string path)
    {
        string destination = Output + "/original/" + path;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, true);
        state.files.Add(new FileRecord { path = path, original = destination, sha256 = Hash(File.ReadAllBytes(path)), guid = AssetDatabase.AssetPathToGUID(path.EndsWith(".meta") ? path.Substring(0, path.Length - 5) : path) });
    }

    static string OwnerPath(Transform transform)
    {
        var names = new List<string>();
        for (var value = transform; value != null; value = value.parent) names.Add(value.name);
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
            var serialized = new SerializedObject(component); var property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = property.propertyType != SerializedPropertyType.ObjectReference;
                if (property.propertyPath.StartsWith("m_", StringComparison.Ordinal)) continue;
                string value = Value(property); if (value != null) record.properties.Add(new PropertyRecord { path = property.propertyPath, kind = property.propertyType.ToString(), value = value });
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
    static void AssertFilesUnchanged(State state, bool includeDuplicate = true)
    {
        foreach (var file in state.files)
        {
            if (!includeDuplicate && file.path == DuplicatePath) continue;
            Require(File.ReadAllBytes(file.path).SequenceEqual(File.ReadAllBytes(file.original)), "File changed without explicit apply: " + file.path);
            string path = file.path.EndsWith(".meta") ? file.path.Substring(0, file.path.Length - 5) : file.path;
            Require(AssetDatabase.AssetPathToGUID(path) == file.guid, "Asset identity changed: " + path);
        }
    }
    static int Missing(GameObject root) => root.GetComponentsInChildren<Transform>(true).Sum(value => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(value.gameObject));
    static void AssertSdkDescriptor(GameObject root)
    {
        var descriptorType = Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        foreach (Component holder in root.GetComponentsInChildren(Find(Holder), true))
        {
            var descriptor = holder.GetComponent(descriptorType);
            Require(descriptor != null, "An SDK descriptor became unavailable");
            Require((Vector3)Field(descriptor, "ViewPosition").GetValue(descriptor) == new Vector3(0.1f, 1.73f, 0.2f), "SDK descriptor configuration changed");
        }
    }

    static void Broken()
    {
        CloseScenes(); var state = ReadState(); AssertFilesUnchanged(state);
        foreach (string path in Candidates.Where(value => value != DuplicatePath))
        {
            if (path.EndsWith(".prefab"))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); int missing = Missing(root);
                Require(missing == 6, "Expected six legacy Missing Scripts in " + path + ", got " + missing);
                AssertSdkDescriptor(root); current.missingScripts += missing;
            }
            else
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int missing = scene.GetRootGameObjects().Sum(Missing);
                Require(missing == 24, "Expected 24 legacy Missing Scripts in main scene, got " + missing);
                foreach (var root in scene.GetRootGameObjects()) AssertSdkDescriptor(root);
                current.missingScripts += missing;
            }
        }
        CloseScenes(); AssertFilesUnchanged(state);
        current.checks.Add("complete current package replacement reproduced Missing Script for all six old component types");
        current.checks.Add("opening/importing did not write old assets; SDK descriptors retained their original type and configuration");
        var duplicateScene = EditorSceneManager.OpenScene(DuplicatePath, OpenSceneMode.Single);
        var duplicate = duplicateScene.GetRootGameObjects().Single(value => value.name == "Duplicate fixture");
        Require(Missing(duplicate) == 1, "Duplicate fixture did not retain its old missing component");
        var added = (MonoBehaviour)duplicate.AddComponent(Find(Outfit)); Set(added, "PartNamePrefixToRemove", "New duplicate prefix");
        EditorUtility.SetDirty(added);
        Require(EditorSceneManager.SaveScene(duplicateScene, DuplicatePath), "Could not save explicit new/old duplicate fixture");
        state.duplicateNewGlobalId = Id(added);
        state.duplicateCurrentBytes = Output + "/duplicate-before-apply.unity";
        File.Copy(DuplicatePath, state.duplicateCurrentBytes, true); WriteState(state);
        CloseScenes(); AssertFilesUnchanged(state, false);
        Later(() => { AssertFilesUnchanged(state, false); Require(File.ReadAllBytes(DuplicatePath).SequenceEqual(File.ReadAllBytes(state.duplicateCurrentBytes)), "Duplicate setup changed automatically"); Finish(); });
    }

    static void Apply()
    {
        CloseScenes(); var state = ReadState(); AssertFilesUnchanged(state, false);
        Require(File.ReadAllBytes(DuplicatePath).SequenceEqual(File.ReadAllBytes(state.duplicateCurrentBytes)), "Duplicate fixture changed before scan");
        var engine = Find("UnityBox.ComponentReferenceMigration.MigrationEngine");
        var overrides = new Dictionary<string, string> { { state.types.Single(value => value.type == Part).guid, Part } };
        var scan = engine.GetMethod("Scan", BindingFlags.Static | BindingFlags.Public);
        var duplicatePlan = scan.Invoke(null, new object[] { new[] { DuplicatePath }, overrides });
        current.readOnlyAssets.Add(DuplicatePath);
        RecordPlan(duplicatePlan, null);
        Require(current.entries.Any(value => value.asset == DuplicatePath && value.status == "Duplicate"), "New/old same-type duplicate was not reported");
        var selected = Candidates.Where(value => value != DuplicatePath).ToArray();
        current.selectedAssets.AddRange(selected);
        var plan = scan.Invoke(null, new object[] { selected, overrides });
        AssertFilesUnchanged(state, false);
        var readyTypes = new HashSet<string>();
        RecordPlan(plan, readyTypes);
        Require(Types.All(readyTypes.Contains), "Not every legacy type has a ready migration entry: " + string.Join(",", Types.Where(value => !readyTypes.Contains(value))));
        var result = engine.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { plan });
        current.repairedComponents = (int)Get(result, "RepairedComponents");
        current.backupDirectory = (string)Get(result, "BackupDirectory");
        foreach (string path in (IEnumerable)Get(result, "ChangedAssets")) current.changedAssets.Add(path);
        Require(current.repairedComponents >= 12, "Too few repaired component references");
        Require(!current.changedAssets.Contains(DuplicatePath), "Duplicate file was rewritten");
        var mapping = current.entries.Where(value => value.status == "Ready").GroupBy(value => value.legacyGuid)
            .ToDictionary(group => group.Key, group => group.First().targetGuid, StringComparer.OrdinalIgnoreCase);
        Require(mapping.Count == 6, "Expected six confirmed old-to-current script identities");
        foreach (var file in state.files)
        {
            byte[] before = File.ReadAllBytes(file.path == DuplicatePath ? state.duplicateCurrentBytes : file.original);
            byte[] expected = file.path == DuplicatePath || !Candidates.Contains(file.path) ? before : ReplaceScriptGuids(before, mapping);
            Require(File.ReadAllBytes(file.path).SequenceEqual(expected), "Apply changed bytes beyond confirmed m_Script GUID spans: " + file.path);
            string path = file.path.EndsWith(".meta") ? file.path.Substring(0, file.path.Length - 5) : file.path;
            Require(AssetDatabase.AssetPathToGUID(path) == file.guid, "Asset GUID changed: " + path);
            if (current.changedAssets.Contains(file.path))
                Require(File.ReadAllBytes(Path.Combine(current.backupDirectory, file.path)).SequenceEqual(before), "Backup differs from pre-apply bytes: " + file.path);
        }
        current.checks.Add("one explicit batch Apply; PartGroup identity override came from actual phase-one MonoScript, not guessed fields");
        current.checks.Add("only confirmed script-GUID spans changed; all fileIDs, references, metadata and other serialized bytes remained identical");
        current.checks.Add("pre-apply exact-byte backups verified; duplicate was scanned read-only outside the batch selection and neither component/file was deleted or rewritten");
        VerifyTypedState(state, false);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var component in scene.GetRootGameObjects().SelectMany(Owned))
            if (component.GetType().FullName != Holder) inspectors.Add(UnityEditor.Editor.CreateEditor(component));
        current.inspectorTargets = inspectors.Count;
        Later(() =>
        {
            VerifyTypedState(state, true);
            foreach (var editor in inspectors) if (editor != null) Object.DestroyImmediate(editor);
            inspectors.Clear();
            foreach (var file in state.files)
            {
                byte[] before = File.ReadAllBytes(file.path == DuplicatePath ? state.duplicateCurrentBytes : file.original);
                byte[] expected = file.path == DuplicatePath || !Candidates.Contains(file.path) ? before : ReplaceScriptGuids(before, mapping);
                Require(File.ReadAllBytes(file.path).SequenceEqual(expected), "Loading/Inspector creation wrote source data: " + file.path);
            }
            current.checks.Add("all original component GlobalObjectIDs, nested/variant links, instance overrides and serialized field/PPtr values restored");
            current.checks.Add("creating current Inspectors and waiting for deferred callbacks did not initialize or normalize stored configuration");
            Finish();
        }, 8);
    }

    static void RecordPlan(object plan, HashSet<string> readyTypes)
    {
        foreach (var entry in (IEnumerable)Get(plan, "Entries"))
        {
            var row = new EntryRecord { asset = (string)Get(entry, "AssetPath"), componentFileId = (string)Get(entry, "ComponentFileId"),
                legacyGuid = (string)Get(entry, "LegacyGuid"), targetGuid = (string)Get(entry, "TargetGuid"), type = (string)Get(entry, "TypeName"),
                status = Get(entry, "Status").ToString(), details = (string)Get(entry, "Details") };
            current.entries.Add(row); if (row.status == "Ready" && readyTypes != null) readyTypes.Add(row.type);
        }
        foreach (var issue in (IEnumerable)Get(plan, "Issues")) current.issues.Add((string)Get(issue, "Code") + ": " + (string)Get(issue, "Message"));
    }

    static byte[] ReplaceScriptGuids(byte[] bytes, Dictionary<string, string> mapping)
    {
        string yaml = Encoding.UTF8.GetString(bytes);
        var regex = new Regex(@"(?m)^(?<lead>[ \t]*m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*)(?<guid>[a-fA-F0-9]{32})(?<tail>,\s*type:\s*3\})");
        return Encoding.UTF8.GetBytes(regex.Replace(yaml, match => mapping.TryGetValue(match.Groups["guid"].Value, out var target)
            ? match.Groups["lead"].Value + target + match.Groups["tail"].Value : match.Value));
    }
    static void VerifyTypedState(State state, bool keepCurrentScene)
    {
        foreach (string path in Candidates.Where(value => value != DuplicatePath))
        {
            GameObject[] roots;
            if (path.EndsWith(".prefab")) roots = new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) };
            else if (keepCurrentScene && SceneManager.GetActiveScene().path == path) roots = SceneManager.GetActiveScene().GetRootGameObjects();
            else roots = EditorSceneManager.OpenScene(path, OpenSceneMode.Single).GetRootGameObjects();
            Require(roots.Sum(Missing) == 0, "Migration left missing script in " + path);
            foreach (var root in roots) AssertSdkDescriptor(root);
            var components = roots.SelectMany(Owned).ToDictionary(Id);
            foreach (var expected in state.components.Where(value => value.asset == path))
            {
                Require(components.TryGetValue(expected.globalId, out var component), "Component identity changed: " + path + " / " + expected.owner + " / " + expected.type);
                Require(component.GetType().FullName == expected.type, "Component resolved to wrong type: " + expected.globalId);
                var serialized = new SerializedObject(component);
                // Older fixture snapshots entered the native PPtr implementation.
                // Its m_FileID is an Editor instance ID. The serialized reference
                // is checked by the parent property's GlobalObjectID and raw YAML.
                var referencePaths = expected.properties.Where(value => value.kind == "ObjectReference")
                    .Select(value => value.path + ".").ToArray();
                foreach (var property in expected.properties)
                {
                    if (referencePaths.Any(prefix => property.path.StartsWith(prefix, StringComparison.Ordinal))) continue;
                    var currentProperty = serialized.FindProperty(property.path);
                    Require(currentProperty != null, "Stored field disappeared: " + expected.type + "." + property.path);
                    Require(Value(currentProperty) == property.value, "Stored value/PPtr changed: " + expected.type + "." + property.path + "; expected " + property.value + ", got " + Value(currentProperty));
                }
            }
        }
        if (!keepCurrentScene)
        {
            var scene = EditorSceneManager.OpenScene(DuplicatePath, OpenSceneMode.Single);
            var duplicate = scene.GetRootGameObjects().Single(value => value.name == "Duplicate fixture");
            Require(Missing(duplicate) == 1, "Skipped duplicate no longer retained its old missing component");
            var added = duplicate.GetComponent(Find(Outfit));
            Require(added != null && Id(added) == state.duplicateNewGlobalId, "New duplicate component identity changed");
            Require((string)Field(added, "PartNamePrefixToRemove").GetValue(added) == "New duplicate prefix", "New duplicate configuration changed");
        }
    }
}
