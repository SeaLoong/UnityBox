using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityBox.AvatarSecuritySystem.Editor;

[assembly: InternalsVisibleTo("UnityBox.AvatarSecuritySystem.Editor.Tests")]

namespace UnityBox.AvatarSecuritySystem
{
    /// <summary>Reconnects configurations authored with the pre-0.8 unversioned script identity.</summary>
    [InitializeOnLoad]
    public static class LegacyComponentMigration
    {
        public const string CanonicalGuid = "1c51e6a139920bb4ca12700dc82627ff";
        private const string SessionKey = "UnityBox.ASS.LegacyComponentMigration.0.9.0";
        private const string MenuPath = "Tools/UnityBox/Avatar Security System/Repair Legacy Components";
        private const string IdentityMapPath = "UserSettings/UnityBox/ASSMigration/LegacyGuids.json";
        private static readonly Regex Documents = new Regex(
            @"^--- !u!114 &[^\r\n]+\r?\nMonoBehaviour:\r?\n.*?(?=^--- !u!|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);
        private static readonly Regex Script = new Regex(
            @"^  m_Script: \{fileID: 11500000, guid: (?<guid>[a-fA-F0-9]{32}), type: 3\}",
            RegexOptions.Multiline);
        private static readonly string[] ConfigurationFields =
        {
            "gesturePassword", "useRightHand", "countdownDuration", "gestureHoldTime",
            "gestureErrorTolerance", "gestureMaxHoldTime", "disableRootChildren", "writeDefaultsMode"
        };
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
        private static bool scheduled;
        private static bool processing;
        private static int suspended;

        public sealed class MigrationResult
        {
            public int RepairedComponents { get; internal set; }
            public IReadOnlyList<string> ChangedAssets { get; internal set; } = Array.Empty<string>();
            public IReadOnlyList<string> DeferredAssets { get; internal set; } = Array.Empty<string>();
            public string BackupDirectory { get; internal set; } = "";
        }

        private sealed class Edit
        {
            public string Path;
            public byte[] Original, Replacement;
            public int Components;
        }

        [Serializable]
        private sealed class IdentityMap
        {
            public string[] guids = Array.Empty<string>();
        }

        static LegacyComponentMigration()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false)) return;
                SessionState.SetBool(SessionKey, true);
                QueueAssets(AssetDatabase.GetAllAssetPaths());
            };
        }

        public static bool TryMigrateYaml(string yaml, Func<string, bool> isResolvedGuid,
            out string migrated, out int count)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DiscoverIdentities(yaml, isResolvedGuid, known);
            return MigrateYaml(yaml, isResolvedGuid, known, out migrated, out count);
        }

        private static void DiscoverIdentities(string yaml, Func<string, bool> isResolvedGuid, HashSet<string> known)
        {
            foreach (Match document in Documents.Matches(yaml))
            {
                string body = document.Value;
                var script = Script.Match(body);
                if (!script.Success || !HasCompatibleIdentifier(body)) continue;
                string guid = script.Groups["guid"].Value;
                if (guid.Equals(CanonicalGuid, StringComparison.OrdinalIgnoreCase) || isResolvedGuid(guid)) continue;
                if (ConfigurationFields.All(field => Regex.IsMatch(body, @"^  " + field + @":", RegexOptions.Multiline)))
                    known.Add(guid);
            }
        }

        private static bool HasCompatibleIdentifier(string body)
        {
            var identifier = Regex.Match(body, @"^  m_EditorClassIdentifier:([^\r\n]*)", RegexOptions.Multiline);
            string type = identifier.Success ? identifier.Groups[1].Value.Trim().Trim('"', '\'') : "";
            string expected = typeof(ASSComponent).FullName;
            return type.Length == 0 || type == expected || type.EndsWith(":" + expected, StringComparison.Ordinal);
        }

        private static bool MigrateYaml(string yaml, Func<string, bool> isResolvedGuid, HashSet<string> known,
            out string migrated, out int count)
        {
            int repaired = 0;
            migrated = Documents.Replace(yaml, document =>
            {
                string body = document.Value;
                var script = Script.Match(body);
                if (!script.Success) return body;
                var guid = script.Groups["guid"];
                if (string.Equals(guid.Value, CanonicalGuid, StringComparison.OrdinalIgnoreCase)
                    || isResolvedGuid(guid.Value) || !known.Contains(guid.Value) || !HasCompatibleIdentifier(body)) return body;
                repaired++;
                return body.Substring(0, guid.Index) + CanonicalGuid + body.Substring(guid.Index + guid.Length);
            });
            count = repaired;
            return repaired > 0;
        }

        public static MigrationResult RepairAssets(IEnumerable<string> assetPaths, bool reloadCleanScenes = true)
        {
            var result = new MigrationResult();
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            var loaded = scenes.Where(scene => !string.IsNullOrEmpty(scene.path))
                .ToDictionary(scene => scene.path, scene => scene, StringComparer.Ordinal);
            bool anyDirty = scenes.Any(scene => scene.isDirty);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var deferred = new List<string>();
            var edits = new List<Edit>();
            string[] requested = assetPaths.Distinct(StringComparer.Ordinal).Where(IsCandidate).Where(File.Exists).ToArray();
            var files = requested.Concat(requested.SelectMany(path => AssetDatabase.GetDependencies(path, true)))
                .Distinct(StringComparer.Ordinal).Where(IsCandidate).Where(File.Exists)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            var known = File.Exists(IdentityMapPath)
                ? new HashSet<string>(JsonUtility.FromJson<IdentityMap>(File.ReadAllText(IdentityMapPath)).guids, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int previousIdentities = known.Count;
            foreach (var file in files) DiscoverIdentities(Encoding.UTF8.GetString(file.Value), GuidExists, known);
            if (known.Count > previousIdentities)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IdentityMapPath));
                File.WriteAllText(IdentityMapPath, JsonUtility.ToJson(new IdentityMap { guids = known.OrderBy(guid => guid, StringComparer.Ordinal).ToArray() }, true));
            }
            foreach (var file in files)
            {
                string path = file.Key;
                byte[] original = file.Value;
                if (!MigrateYaml(Encoding.UTF8.GetString(original), GuidExists, known, out var migrated, out int count)) continue;
                if (loaded.TryGetValue(path, out var scene) && (scene.isDirty || reloadCleanScenes && anyDirty)
                    || stage != null && stage.assetPath == path && stage.scene.isDirty)
                {
                    deferred.Add(path);
                    continue;
                }
                edits.Add(new Edit { Path = path, Original = original,
                    Replacement = Encoding.UTF8.GetBytes(migrated), Components = count });
            }
            result.DeferredAssets = deferred;
            if (edits.Count == 0) return result;
            var scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(CanonicalGuid));
            if (scriptAsset == null || scriptAsset.GetClass() != typeof(ASSComponent))
                throw new InvalidOperationException(I18n.T("migration.script_not_ready"));
            result.BackupDirectory = Path.GetFullPath("UserSettings/UnityBox/ASSMigration/"
                + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            foreach (var edit in edits)
            {
                string backup = Path.Combine(result.BackupDirectory, edit.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.WriteAllBytes(backup, edit.Original);
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var changedPaths = new HashSet<string>(edits.Select(edit => edit.Path), StringComparer.Ordinal);
            bool reloadScenes = reloadCleanScenes && !anyDirty && loaded.Keys.Any(path => changedPaths.Contains(path)
                || AssetDatabase.GetDependencies(path, true).Any(changedPaths.Contains));
            processing = true;
            try
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var edit in edits) File.WriteAllBytes(edit.Path, edit.Replacement);
                }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var edit in edits)
                    AssetDatabase.ImportAsset(edit.Path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                if (reloadScenes)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            finally { processing = false; }
            result.ChangedAssets = edits.Select(edit => edit.Path).ToArray();
            result.RepairedComponents = edits.Sum(edit => edit.Components);
            return result;
        }

        [MenuItem(MenuPath)]
        public static void RepairProject()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Report(RepairAssets(AssetDatabase.GetAllAssetPaths()));
        }

        private static bool GuidExists(string guid) => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));

        private static bool IsCandidate(string path) => path.StartsWith("Assets/", StringComparison.Ordinal)
            && (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));

        internal static void QueueAssets(IEnumerable<string> paths)
        {
            if (processing) return;
            foreach (string path in paths.Where(IsCandidate)) Pending.Add(path);
            Schedule();
        }

        private static void Schedule()
        {
            if (scheduled || suspended > 0 || Pending.Count == 0) return;
            scheduled = true;
            EditorApplication.update += RunWhenReady;
        }

        private static void RunWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RunWhenReady;
            scheduled = false;
            if (suspended > 0) return;
            string[] paths = Pending.ToArray();
            Pending.Clear();
            Report(RepairAssets(paths));
        }

        private static void Report(MigrationResult result)
        {
            if (result.RepairedComponents > 0)
                Debug.Log("[ASS] " + I18n.T("migration.restored", result.RepairedComponents, result.ChangedAssets.Count, result.BackupDirectory));
            if (result.DeferredAssets.Count > 0)
                Debug.Log("[ASS] " + I18n.T("migration.deferred", MenuPath, string.Join(", ", result.DeferredAssets)));
        }

        internal static IDisposable SuspendAutomaticRepair() => new Suspension();

        private sealed class Suspension : IDisposable
        {
            public Suspension() { suspended++; }
            public void Dispose() { suspended--; Schedule(); }
        }
    }

    internal sealed class LegacyComponentMigrationImports : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            LegacyComponentMigration.QueueAssets(imported.Concat(moved));
        }
    }
}
