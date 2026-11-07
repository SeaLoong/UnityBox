using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityBox.ComponentReferenceMigration
{
    public enum MigrationStatus { Ready, Ambiguous, Unavailable, Duplicate, Skipped }

    public sealed class MigrationIssue
    {
        public string AssetPath { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
    }

    public sealed class MigrationTarget
    {
        public string TypeName { get; internal set; }
        public string DisplayName { get; internal set; }
        public string ScriptGuid { get; internal set; }
        public bool Available => !string.IsNullOrEmpty(ScriptGuid);
    }

    public sealed class MigrationEntry
    {
        public string AssetPath { get; internal set; }
        public string ComponentFileId { get; internal set; }
        public string LegacyGuid { get; internal set; }
        public string TargetGuid { get; internal set; }
        public string TypeName { get; internal set; }
        public string GameObjectName { get; internal set; }
        public string EditorClassIdentifier { get; internal set; }
        public IReadOnlyList<string> SerializedFields { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> CandidateTypes { get; internal set; } = Array.Empty<string>();
        public MigrationStatus Status { get; internal set; }
        public string Details { get; internal set; }
        internal int GuidByteOffset;
        internal string Owner;
    }

    public sealed class MigrationPlan
    {
        public IReadOnlyList<MigrationEntry> Entries => entries;
        public IReadOnlyList<MigrationIssue> Issues => issues;
        public IReadOnlyList<MigrationTarget> TargetTypes => targets;
        public IReadOnlyList<string> ScannedAssets => selectedPaths;
        internal readonly List<MigrationEntry> entries = new List<MigrationEntry>();
        internal readonly List<MigrationIssue> issues = new List<MigrationIssue>();
        internal readonly List<MigrationTarget> targets = new List<MigrationTarget>();
        internal readonly List<string> selectedPaths = new List<string>();
        internal readonly Dictionary<string, Snapshot> evidence = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
    }

    public sealed class MigrationResult
    {
        public IReadOnlyList<string> ChangedAssets { get; internal set; } = Array.Empty<string>();
        public int RepairedComponents { get; internal set; }
        public string BackupDirectory { get; internal set; } = "";
        public IReadOnlyList<MigrationIssue> Issues { get; internal set; } = Array.Empty<MigrationIssue>();
    }

    internal sealed class Snapshot
    {
        public string Path, Text, Hash;
        public byte[] Bytes;
        public readonly List<Document> Documents = new List<Document>();
        public readonly Dictionary<string, Document> Objects = new Dictionary<string, Document>();
    }

    internal sealed class Document
    {
        public Snapshot File;
        public string Id, Kind, Text, Guid, Identifier, GameObjectId, SourceGuid, SourceId, PrefabInstance;
        public int GuidByteOffset;
        public bool Stripped;
        public readonly HashSet<string> Fields = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Explicit, user-invoked reference repair. No startup or asset-import callbacks.</summary>
    public static class MigrationEngine
    {
        private sealed class Rule
        {
            public string Name, Label, CanonicalGuid;
            public string[] Fields;
            public bool Strong;
        }

        private static readonly Rule[] Rules =
        {
            new Rule { Name = "UnityBox.AdvancedCostumeController.ACCOutfitMarker", Label = "ACC / Outfit Marker", CanonicalGuid = "e98f908c5fb14a479a7cc0ce3492a7d5", Strong = true,
                Fields = new[] { "PartNamePrefixToRemove", "PartNameSuffixToRemove", "PartNameRegexPattern", "PartNameRegexReplacement" } },
            new Rule { Name = "UnityBox.AdvancedCostumeController.ACCPartGroupMarker", Label = "ACC / Part Group Marker", CanonicalGuid = "594421b5520f47b892549d6e70c660fe", Strong = false,
                Fields = new[] { "Mode", "GroupName" } },
            new Rule { Name = "UnityBox.AdvancedCostumeController.ACCVariantMaterialOverride", Label = "ACC / Variant Material Override", CanonicalGuid = "a37f4c4e3ea441078fad0c0782ac99a4", Strong = true,
                Fields = new[] { "OutfitBase", "Replacements", "RendererOverrides", "editorPreviewInitialized" } },
            new Rule { Name = "UnityBox.AvatarSecuritySystem.ASSComponent", Label = "ASS / Configuration", CanonicalGuid = "1c51e6a139920bb4ca12700dc82627ff", Strong = true,
                Fields = new[] { "gesturePassword", "useRightHand", "countdownDuration", "gestureHoldTime", "gestureErrorTolerance", "gestureMaxHoldTime", "disableRootChildren", "writeDefaultsMode" } },
            new Rule { Name = "UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent", Label = "Viseme / Configuration", CanonicalGuid = "2814d8bd3adc4c539c9d1825545f6316", Strong = true,
                Fields = new[] { "targetRenderer", "writeDefaultsMode", "globalWeight", "voiceModulationMode", "voiceMin", "voiceMax", "bindings" } },
            new Rule { Name = "UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent", Label = "Animator Optimizer / Configuration", CanonicalGuid = "a157d3d35699dd84abde9e5412bd7017", Strong = true,
                Fields = new[] { "targetMode", "playableLayer", "animator", "controller", "optimizeAllConvertibleLayers", "layerIndices", "upgradeBoolParametersToFloat", "upgradeIntParametersToFloat", "writeDefaultsPolicy", "generatedLayerName", "weightParameterPrefix" } },
        };

        private static readonly Regex Documents = new Regex(@"^--- !u!(?<kind>\d+) &(?<id>-?\d+)(?<stripped> stripped)?\r?\n.*?(?=^--- !u!|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        private static readonly Regex Script = new Regex(@"^  m_Script: \{fileID: 11500000, guid: (?<guid>[a-fA-F0-9]{32}), type: 3\}", RegexOptions.Multiline);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static IReadOnlyList<MigrationTarget> GetTargetTypes() => ResolveTargets();

        public static MigrationPlan Scan(IEnumerable<string> assetPaths, Dictionary<string, string> typeOverrides = null)
        {
            if (assetPaths == null) throw new ArgumentNullException(nameof(assetPaths));
            var plan = new MigrationPlan();
            plan.targets.AddRange(ResolveTargets());
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (typeOverrides != null)
                foreach (var pair in typeOverrides)
                {
                    if (!Regex.IsMatch(pair.Key ?? "", @"\A[a-fA-F0-9]{32}\z") || !Rules.Any(rule => rule.Name == pair.Value))
                        throw new ArgumentException("Each override must map a 32-character script GUID to an audited UnityBox component type.", nameof(typeOverrides));
                    overrides[pair.Key] = pair.Value;
                }
            var selected = ExpandPaths(assetPaths, plan.issues);
            plan.selectedPaths.AddRange(selected);
            foreach (string path in selected) Read(path, plan);

            var proofs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in plan.evidence.Values.ToArray())
            foreach (var document in file.Documents.Where(item => item.Kind == "114" && item.Guid != null && !item.Stripped))
            {
                string proof = Identify(document, overrides, null);
                if (proof == null) continue;
                if (!proofs.TryGetValue(document.Guid, out var names)) proofs[document.Guid] = names = new HashSet<string>();
                names.Add(proof);
            }
            var risk = OpenAssetDependencies();
            var owners = new Dictionary<string, List<MigrationEntry>>(StringComparer.Ordinal);
            var validDocuments = new Dictionary<Document, string>();
            foreach (string path in selected)
            {
                if (!plan.evidence.TryGetValue(path, out var file)) continue;
                var knownComponents = new List<KeyValuePair<string, string>>();
                foreach (var document in file.Documents.Where(item => item.Kind == "114" && item.Guid != null))
                {
                    string existing = ResolvedType(document.Guid);
                    if (existing != null)
                    {
                        if (Rules.Any(rule => rule.Name == existing))
                        {
                            knownComponents.Add(new KeyValuePair<string, string>(Owner(document, plan), existing));
                            validDocuments[document] = existing;
                        }
                        continue;
                    }
                    string type = Identify(document, overrides, proofs);
                    if (type == null && document.Stripped)
                        type = IdentifySource(document, overrides, proofs, plan, new HashSet<string>());
                    var target = plan.targets.FirstOrDefault(item => item.TypeName == type);
                    var candidates = Rules.Where(rule => rule.Fields.All(document.Fields.Contains)).ToArray();
                    var entry = new MigrationEntry
                    {
                        AssetPath = path, ComponentFileId = document.Id, LegacyGuid = document.Guid,
                        TypeName = type ?? "", TargetGuid = target?.ScriptGuid ?? "",
                        GameObjectName = ObjectName(document, plan), EditorClassIdentifier = document.Identifier,
                        SerializedFields = document.Fields.Where(field => !field.StartsWith("m_", StringComparison.Ordinal))
                            .OrderBy(field => field, StringComparer.Ordinal).Take(12).ToArray(),
                        CandidateTypes = candidates.Select(rule => rule.Label).ToArray(),
                        GuidByteOffset = document.GuidByteOffset, Owner = Owner(document, plan),
                        Status = type == null ? MigrationStatus.Ambiguous : target == null || !target.Available ? MigrationStatus.Unavailable : MigrationStatus.Ready,
                        Details = type == null && candidates.Any(rule => !rule.Strong)
                            ? "候选 ACC Part Group Marker（Mode / GroupName）；需要一次 GUID 类型映射。 / Candidate only: choose a GUID-to-type mapping before migrating."
                            : type == null ? "No unique audited component identity. Supply one GUID-to-type mapping, or leave this reference unchanged."
                            : target == null || !target.Available ? "The target component script is not installed or has no compiled type." : "Recognized legacy component reference."
                    };
                    if (document.Identifier.Length > 0 && !Rules.Any(rule => IdentifierMatches(document.Identifier, rule.Name)))
                    {
                        entry.Status = MigrationStatus.Skipped;
                        entry.Details = "The explicit editor class identifier belongs to another type.";
                    }
                    if (!document.Stripped && (string.IsNullOrEmpty(document.GameObjectId) || document.GameObjectId == "0"))
                    {
                        entry.Status = MigrationStatus.Skipped;
                        entry.Details = "This document has no GameObject binding; the audited targets are avatar components, not ScriptableObjects.";
                    }
                    if (risk.Contains(path))
                    {
                        entry.Status = MigrationStatus.Skipped;
                        entry.Details = "An open scene or Prefab Stage uses this asset. Close the affected editing context before migrating it.";
                    }
                    plan.entries.Add(entry);
                    if (type != null && !string.IsNullOrEmpty(entry.Owner))
                    {
                        string key = entry.Owner + "|" + type;
                        if (!owners.TryGetValue(key, out var group)) owners[key] = group = new List<MigrationEntry>();
                        group.Add(entry);
                    }
                }
                foreach (var component in knownComponents.Where(item => !string.IsNullOrEmpty(item.Key)))
                {
                    string key = component.Key + "|" + component.Value;
                    if (!owners.TryGetValue(key, out var group)) continue;
                    foreach (var entry in group)
                    {
                        entry.Status = MigrationStatus.Duplicate;
                        entry.Details = "This GameObject already contains a valid component of the same type. Neither component is deleted or merged.";
                    }
                }
            }
            foreach (var group in owners.Values.Where(items => items.Count > 1))
                foreach (var entry in group)
                {
                    entry.Status = MigrationStatus.Duplicate;
                    entry.Details = "Multiple legacy components of the same type share this GameObject. No automatic merge or deletion is performed.";
                }
            foreach (var pair in validDocuments)
            {
                var document = pair.Key;
                if (document.File.Objects.TryGetValue(document.GameObjectId ?? "", out var gameObject))
                    BlockInheritedDuplicate(gameObject, pair.Value, document.File.Path, plan, new HashSet<string>());
            }
            return plan;
        }

        private static void BlockInheritedDuplicate(Document gameObject, string type, string referringAsset,
            MigrationPlan plan, HashSet<string> visited)
        {
            var source = Source(gameObject, plan);
            if (source == null || !visited.Add(source.File.Path + "|" + source.Id)) return;
            foreach (var component in source.File.Documents.Where(item => item.Kind == "114" && item.GameObjectId == source.Id))
            {
                var entry = plan.entries.FirstOrDefault(item => item.AssetPath == source.File.Path
                    && item.ComponentFileId == component.Id && item.TypeName == type && item.Status == MigrationStatus.Ready);
                if (entry == null) continue;
                entry.Status = MigrationStatus.Duplicate;
                entry.Details = "A selected instance asset already adds a valid component of this type: " + referringAsset
                    + ". Repairing the source would create a duplicate; neither configuration is merged or deleted.";
                plan.issues.Add(Issue(referringAsset, "dependent-duplicate", entry.Details));
            }
            BlockInheritedDuplicate(source, type, referringAsset, plan, visited);
        }

        public static MigrationResult Apply(MigrationPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var issues = new List<MigrationIssue>(plan.issues);
            foreach (var entry in plan.entries.Where(item => item.Status != MigrationStatus.Ready))
                issues.Add(Issue(entry.AssetPath, "component-" + entry.Status.ToString().ToLowerInvariant(),
                    "fileID " + entry.ComponentFileId + ", GUID " + entry.LegacyGuid + ": " + entry.Details));
            var result = new MigrationResult { Issues = issues };
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
            {
                issues.Add(Issue("", "editor-busy", "Finish script compilation and asset import before migrating references."));
                return result;
            }
            foreach (var evidence in plan.evidence.Values)
                if (!File.Exists(evidence.Path) || Hash(File.ReadAllBytes(evidence.Path)) != evidence.Hash)
                {
                    issues.Add(Issue(evidence.Path, "plan-stale", "An asset changed after analysis. Analyze again; no files were written."));
                    return result;
                }
            var risk = OpenAssetDependencies();
            var targets = ResolveTargets().ToDictionary(item => item.TypeName);
            var ready = plan.entries.Where(entry => entry.Status == MigrationStatus.Ready).ToArray();
            foreach (var entry in ready)
                if (!targets.TryGetValue(entry.TypeName, out var target) || target.ScriptGuid != entry.TargetGuid || ResolvedType(entry.LegacyGuid) != null)
                {
                    issues.Add(Issue(entry.AssetPath, "script-identity-changed", "A script identity changed after analysis. Analyze again; no files were written."));
                    return result;
                }
            var edits = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var group in ready.GroupBy(entry => entry.AssetPath))
            {
                if (risk.Contains(group.Key))
                {
                    issues.Add(Issue(group.Key, "asset-open", "An open scene or Prefab Stage uses this asset; it was skipped."));
                    continue;
                }
                if (!AssetDatabase.IsOpenForEdit(group.Key))
                {
                    issues.Add(Issue(group.Key, "asset-readonly", "The asset is not open for editing; check it out before migrating."));
                    continue;
                }
                byte[] replacement = (byte[])plan.evidence[group.Key].Bytes.Clone();
                foreach (var entry in group)
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes(entry.TargetGuid), 0, replacement, entry.GuidByteOffset, 32);
                edits.Add(group.Key, replacement);
            }
            if (edits.Count == 0) return result;
            string backup = Path.GetFullPath("UserSettings/UnityBox/ComponentReferenceMigration/"
                + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            result.BackupDirectory = backup;
            foreach (string path in edits.Keys)
            {
                string destination = Path.Combine(backup, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, plan.evidence[path].Bytes);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", destination + ".meta");
            }
            var report = new BatchReport
            {
                createdUtc = DateTime.UtcNow.ToString("O"),
                skipped = issues.Select(issue => new IssueReport { path = issue.AssetPath, code = issue.Code, message = issue.Message }).ToArray(),
                files = edits.Select(pair => new FileReport
                {
                    path = pair.Key, beforeSha256 = plan.evidence[pair.Key].Hash, afterSha256 = Hash(pair.Value),
                    components = ready.Where(entry => entry.AssetPath == pair.Key).Select(entry => new ComponentReport
                    { fileId = entry.ComponentFileId, oldGuid = entry.LegacyGuid, newGuid = entry.TargetGuid, type = entry.TypeName }).ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.Combine(backup, "report.json"), JsonUtility.ToJson(report, true));
            var written = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var edit in edits)
                {
                    written.Add(edit.Key);
                    File.WriteAllBytes(edit.Key, edit.Value);
                }
            }
            catch (Exception error)
            {
                foreach (string path in written) File.WriteAllBytes(path, plan.evidence[path].Bytes);
                issues.Add(Issue("", "write-failed", "The batch was rolled back to its original bytes: " + error.Message));
                return result;
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (string path in edits.Keys)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            result.ChangedAssets = edits.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            result.RepairedComponents = ready.Count(entry => edits.ContainsKey(entry.AssetPath));
            return result;
        }

        private static List<MigrationTarget> ResolveTargets()
        {
            var found = new Dictionary<string, string>();
            foreach (var rule in Rules)
            {
                string path = AssetDatabase.GUIDToAssetPath(rule.CanonicalGuid);
                var script = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass()?.FullName == rule.Name) found[rule.Name] = rule.CanonicalGuid;
            }
            if (found.Count != Rules.Length)
                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                    string name = script == null ? null : script.GetClass()?.FullName;
                    if (name != null && !found.ContainsKey(name) && Rules.Any(rule => rule.Name == name)) found[name] = guid;
                }
            return Rules.Select(rule => new MigrationTarget
            {
                TypeName = rule.Name, DisplayName = rule.Label,
                ScriptGuid = found.TryGetValue(rule.Name, out var guid) ? guid : ""
            }).ToList();
        }

        private static string ResolvedType(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            return script == null ? null : script.GetClass()?.FullName;
        }

        private static string Identify(Document document, Dictionary<string, string> overrides, Dictionary<string, HashSet<string>> proofs)
        {
            if (document.Identifier.Length > 0)
            {
                var explicitTypes = Rules.Where(rule => IdentifierMatches(document.Identifier, rule.Name)).ToArray();
                return explicitTypes.Length == 1 ? explicitTypes[0].Name : null;
            }
            if (overrides.TryGetValue(document.Guid, out string specified)) return specified;
            if (proofs != null && proofs.TryGetValue(document.Guid, out var names) && names.Count == 1)
            {
                string name = names.Single();
                var rule = Rules.Single(item => item.Name == name);
                if (document.Stripped || rule.Fields.All(document.Fields.Contains)) return name;
            }
            var candidates = Rules.Where(rule => rule.Strong && rule.Fields.All(document.Fields.Contains)).ToArray();
            return candidates.Length == 1 ? candidates[0].Name : null;
        }

        private static bool IdentifierMatches(string identifier, string name) => identifier == name
            || identifier.EndsWith(":" + name, StringComparison.Ordinal);

        private static string IdentifySource(Document document, Dictionary<string, string> overrides,
            Dictionary<string, HashSet<string>> proofs, MigrationPlan plan, HashSet<string> visited)
        {
            var source = Source(document, plan);
            if (source == null || source.Guid == null || !visited.Add(source.File.Path + "|" + source.Id)) return null;
            string existing = ResolvedType(source.Guid);
            if (existing != null) return Rules.Any(rule => rule.Name == existing) ? existing : null;
            return Identify(source, overrides, proofs) ?? (source.Stripped ? IdentifySource(source, overrides, proofs, plan, visited) : null);
        }

        private static Document Source(Document document, MigrationPlan plan)
        {
            if (string.IsNullOrEmpty(document.SourceGuid) || document.SourceId == "0") return null;
            string path = AssetDatabase.GUIDToAssetPath(document.SourceGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var file = Read(path, plan);
            return file?.Documents.FirstOrDefault(item => item.Id == document.SourceId);
        }

        private static string Owner(Document document, MigrationPlan plan)
        {
            if (!string.IsNullOrEmpty(document.GameObjectId) && document.GameObjectId != "0")
            {
                if (document.File.Objects.TryGetValue(document.GameObjectId, out var gameObject)
                    && !string.IsNullOrEmpty(gameObject.SourceGuid) && gameObject.PrefabInstance != "0")
                    return document.File.Path + "|instance:" + gameObject.PrefabInstance + "|" + gameObject.SourceGuid + "|" + gameObject.SourceId;
                return document.File.Path + "|object:" + document.GameObjectId;
            }
            if (document.Stripped && document.PrefabInstance != "0")
            {
                var source = Source(document, plan);
                if (source != null && !string.IsNullOrEmpty(source.GameObjectId) && source.GameObjectId != "0")
                    return document.File.Path + "|instance:" + document.PrefabInstance + "|" + document.SourceGuid + "|" + source.GameObjectId;
            }
            return "";
        }

        private static string ObjectName(Document document, MigrationPlan plan)
        {
            var visited = new HashSet<string>();
            var current = document;
            while (current != null && visited.Add(current.File.Path + "|" + current.Id))
            {
                Document gameObject = current.Kind == "1" ? current :
                    current.File.Objects.TryGetValue(current.GameObjectId ?? "", out var found) ? found : null;
                if (gameObject != null)
                {
                    string name = Scalar(gameObject.Text, "m_Name").Trim().Trim('"');
                    if (!string.IsNullOrEmpty(name)) return name;
                    current = Source(gameObject, plan);
                }
                else current = Source(current, plan);
            }
            return "(source GameObject unresolved)";
        }

        private static Snapshot Read(string path, MigrationPlan plan)
        {
            if (plan.evidence.TryGetValue(path, out var cached)) return cached;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text = Utf8.GetString(bytes);
                var file = new Snapshot { Path = path, Bytes = bytes, Text = text, Hash = Hash(bytes) };
                int characterCursor = 0, byteCursor = 0;
                foreach (Match match in Documents.Matches(text))
                {
                    var document = new Document { File = file, Kind = match.Groups["kind"].Value, Id = match.Groups["id"].Value,
                        Text = match.Value, Stripped = match.Groups["stripped"].Success };
                    var script = Script.Match(document.Text);
                    if (script.Success)
                    {
                        document.Guid = script.Groups["guid"].Value;
                        int absoluteIndex = match.Index + script.Groups["guid"].Index;
                        byteCursor += Utf8.GetByteCount(text.Substring(characterCursor, absoluteIndex - characterCursor));
                        characterCursor = absoluteIndex;
                        document.GuidByteOffset = byteCursor;
                    }
                    document.Identifier = Scalar(document.Text, "m_EditorClassIdentifier").Trim().Trim('"', '\'');
                    document.GameObjectId = ReferenceId(document.Text, "m_GameObject");
                    document.PrefabInstance = ReferenceId(document.Text, "m_PrefabInstance");
                    document.SourceId = ReferenceId(document.Text, "m_CorrespondingSourceObject");
                    document.SourceGuid = Regex.Match(Scalar(document.Text, "m_CorrespondingSourceObject"), @"\bguid: ([a-fA-F0-9]{32})").Groups[1].Value;
                    foreach (Match field in Regex.Matches(document.Text, @"^  (?<field>[A-Za-z_][A-Za-z0-9_]*):", RegexOptions.Multiline))
                        document.Fields.Add(field.Groups["field"].Value);
                    file.Documents.Add(document);
                    if (document.Kind == "1") file.Objects[document.Id] = document;
                }
                plan.evidence[path] = file;
                return file;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is DecoderFallbackException)
            {
                plan.issues.Add(Issue(path, "asset-read-failed", error.Message));
                return null;
            }
        }

        private static string Scalar(string document, string name) => Regex.Match(document,
            @"^  " + Regex.Escape(name) + @":([^\r\n]*)", RegexOptions.Multiline).Groups[1].Value;
        private static string ReferenceId(string document, string name) => Regex.Match(Scalar(document, name), @"\bfileID: (-?\d+)").Groups[1].Value;

        private static List<string> ExpandPaths(IEnumerable<string> requested, List<MigrationIssue> issues)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in requested.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                string path = raw.Replace('\\', '/').TrimEnd('/');
                if (path != "Assets" && (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Contains("..")))
                { issues.Add(Issue(path, "outside-assets", "Select assets or folders inside Assets; packages and external files are read-only inputs.")); continue; }
                if (Directory.Exists(path))
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Replace('\\', '/');
                        if (IsAsset(relative)) selected.Add(relative);
                    }
                else if (File.Exists(path) && IsAsset(path)) selected.Add(path);
                else issues.Add(Issue(path, "unsupported-path", "Select a .prefab, .unity, .asset or an Assets folder."));
            }
            return selected.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static bool IsAsset(string path) => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);

        private static HashSet<string> OpenAssetDependencies()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            Action<string> include = path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!result.Add(path)) return;
                foreach (string dependency in AssetDatabase.GetDependencies(path, true)) result.Add(dependency);
            };
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                include(scene.path);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        include(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject));
                    foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { root }))
                        if (dependency != null) include(AssetDatabase.GetAssetPath(dependency));
                }
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) include(stage.assetPath);
            return result;
        }

        private static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static MigrationIssue Issue(string path, string code, string message) => new MigrationIssue { AssetPath = path, Code = code, Message = message };

        [Serializable] private sealed class BatchReport { public string createdUtc; public FileReport[] files; public IssueReport[] skipped; }
        [Serializable] private sealed class FileReport { public string path, beforeSha256, afterSha256; public ComponentReport[] components; }
        [Serializable] private sealed class ComponentReport { public string fileId, oldGuid, newGuid, type; }
        [Serializable] private sealed class IssueReport { public string path, code, message; }
    }
}
