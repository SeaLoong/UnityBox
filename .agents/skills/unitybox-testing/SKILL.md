---
name: unitybox-testing
description: Install and activate a real Unity cloud toolchain, execute isolated functional, graphics, Player and performance checks, and retain source-matched evidence.
---

# UnityBox real Unity verification

Read the checkout's `AGENTS.md`, branch/status, [UnityEnvironment.md](../../../Docs/UnityEnvironment.md)
and [Testing.md](../../../Docs/Testing.md). Tell the user when applying this skill for the first time.
All eight plugins are maintained together on `master`. The user authorized combining the reviewed
branches, improving the unified implementation, and committing and pushing completed tested changes
to `master`. Preserve supplied scenes/GUIDs, merge history and any new remote work.

## Set up the machine

- Inspect the managed runtime's current network state. Preserve inherited proxy, CA and authentication.
- Use Python 3.12+. Reuse `/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity`
  (2022.3.22f1) and `/workspace/unitybox-setup/unity/dependencies` when present.
- Otherwise run `scripts/unity/install_editor.py`, then `fetch_dependencies.py --profile core`
  and `--profile vrc`. The installers verify the pinned hashes, package graph and Editor modules.
- Reuse a machine activation completed through official Hub with the user's Personal account.
  The retained tests use `--xdg-root /workspace/unitybox-setup/unity/interactive`; keep HOME unchanged.
  Inspect actual Editor execution when continuing after a machine/session restart.
- On a new machine, provide an authenticated private desktop for official Hub sign-in. Let the user
  handle credentials/terms. Use official Hub's Licenses and Installs → Locate for the old Editor.
  Keep account data private and close the external relay after the user finishes.
- For container graphics use private Xauthority and Xvfb without MIT-SHM, x11vnc `-noshm`.
  If proxy CA trust causes Hub's popup to close, use the system p11-kit NSS trust module for those
  processes while retaining TLS verification. See the environment manual for the retained helpers.

## Prepare the actual dependency scope

- Keep unique projects/evidence in `/workspace`, outside source checkouts. `/tmp` is scratch space.
- Use `test_project.py prepare --repo <checkout> --project <new-owned-path> --profile core`.
  Omit `--package` to include all eight packages, or select a subset with repeatable `--package`.
  Use a unique owned project path for each source snapshot and dependency scope.
- Main checkout: `/workspace/UnityBox` on `master`, containing all eight plugins. The old review
  checkout and feature branches are historical sources; ordinary validation uses the main checkout.
- Core uses real UTF1.1.33/NUnit1.0.6. SDK-only uses `--profile vrc --offline` with Base/Avatars3.8.2.
  Add `--dependency nadena.dev.ndmf` and `--dependency nadena.dev.modular-avatar` for full integration.
  NDMF1.11 and MA1.18 are cached. VRC embeds actual UGUI and declares AndroidJNI for Oculus code.
- Honor actual asmdef minima: AO NDMF>=1.11; ACC/BCG MA path SDK>=3.7.3. The lock pins SDK3.8.2.
  Minimum versionDefine expressions use `3.7.0`, for example; inspect native suite discovery.
- Preparation preserves original metadata and copies package tests. The user's source scene stays
  unchanged. The ownership marker records source paths/content and expected local test assemblies.
- Freeze source before final preparation. Retain completed project snapshots; use new paths for
  changed source. Preserve logs, XML, run metadata and reports.

## Finish functionality and verify outputs

1. Run `test_project.py run --project <project> --xdg-root <activated-root> --timeout 1200`
   with `--exclude-category 'UnityBoxPerformance;UnityBoxGraphics'` for full functional scope.
2. Review real exit0, fresh NUnit XML, nonzero tests, every expected assembly and no failed/skipped
   cases. The runner always limits execution to the prepared local `-assemblyNames`.
3. Use filters to diagnose a failure, fix its cause and rerun the complete affected scope.
   Cover native Animator frames, original-vs-generated output, saved assets/reload, Undo/Redo,
   source preservation and real SDK/MA/NDMF integration as appropriate.
4. Run graphics cases with `--graphics --test-category UnityBoxGraphics`, passing the private
   desktop's environment to the subprocess. Check actual rendered pixels and saved icons.
5. Run `verify_player.py --project <core-project> --xdg-root <activated-root> --timeout 1200`.
   Require actual Linux Player build and execution, expected package providers, nonzero output
   checks, successful scene stripping, and source authoring configuration retention after reloading
   the saved scene. BuildPlayer invalidates scene object references obtained before its call.
6. Stop other Editors before `test_project.py run ... --benchmark`. Require structured records for
   every selected plugin. Preserve sizes, warmups, sample counts, timings and memory-counter meaning.
   Player steady-state Animator.Update and Editor generation measure separate operations.
7. Parallelize at most two functional Editors. Dedicated performance execution is serial.
   Delete only rebuildable owned caches when disk space requires it, retaining imported DLLs,
   source snapshots and raw evidence. Check active projects before cleaning their caches.

## Verify package-owned recovery, import preservation and serialized identity

- Opening/importing packages or loading assets preserves source bytes, script GUIDs, serialized fields
  and editing state. Do not install startup/import migrations or automatic OnValidate corrections.
  Normalize independent configuration/Options snapshots for generation instead of modifying source.
- Component recovery ships with ACC0.5.2, AO0.3.2, Viseme0.2.2 and ASS0.9.3 inside each owner's existing
  Unity-only Editor assembly. Normal plugin installation/update delivers the feature; do not add an
  installer, dependency package or extra source-module copy to make recovery available.
- Project context menus use `Assets/UnityBox/<owner>/Repair Component References`; owners are
  `Advanced Costume Controller`, `Animator Optimizer`, `Avatar Security System` and
  `Viseme Blendshape Override`. Preserve the old Tools plugin-window leaf. Opening the owner window
  prepopulates Project selection and only exposes that owner's audited component types.
- The user authorized one manual batch over selected resources, without per-item confirmation.
  Resolve an ambiguous legacy GUID once for its group; ACC Mode/GroupName is a weak candidate even
  when one type is listed. Unknown/duplicate or open-asset/dependency cases remain in the report.
- Use [ComponentReferences](../../../Docs/ComponentReferences.md) for the eight-package matrix and
  six component FQCN/assembly/GUID entries. ACC has three production markers; AO, ASS and Viseme
  one each. BCG/Collider/Logger/PrefabPatcher have no own Avatar authoring component. AO0.2 is a
  development snapshot, not a published legacy VPM ZIP.
- Prepare five independent consumers with normal package preparation: ACC only, AO only, ASS only,
  Viseme only and all four together. Use `--package` for the sole owner, or repeat it four times for
  the combined case. Compile and execute using only that consumer's declared dependencies and source.
  Do not seed the old standalone directory, an extra asmdef, test assembly or installer as a workaround.
  Package recovery must remain available even when the repository's obsolete module is absent.
- In each consumer, verify its exact Project menu, owner-only targets, read-only Analyze and one
  Migrate Selected Assets batch. No startup/import action writes files. Test genuine old ACC/ASS/Viseme
  ZIP imports and the AO development snapshot, retaining generated old GUIDs and serialized values.
- Apply rechecks source evidence SHA and installed target identity, backs up every target before the
  first write, and changes only fixed-length GUID byte spans. Preserve fileIDs, fields, references,
  BOM/CRLF and project metadata. Copy existing metadata with the batch to
  `UserSettings/UnityBox/<AdvancedCostumeController|AnimatorOptimizer|AvatarSecuritySystem|VisemeBlendshapeOverride>/ComponentReferenceMigration/<id>/Assets/...`.
- Keep dependency evidence read-only and write only selected paths. Skip loaded Scene/PrefabStage
  and their dependencies without saving, closing or reloading. Cover unsaved nested Prefab and object
  references added in Prefab Mode with autoSave disabled; compare dirty state, object identities and
  original bytes. Discard only the test fixture's own edits when leaving its Stage.
- Cover ambiguity mapping, valid-external/unknown/duplicate handling, changed files since scan,
  exact backups, idempotence and owner source configuration preservation. Record five-consumer,
  native and real-upgrade evidence in
  [ComponentReferences verification](../../../Docs/Verification/ComponentReferences/README.md).
- [ASSImportSafety](../../../Docs/Verification/ASSImportSafety/README.md) and
  [ASSUpgrade](../../../Docs/Verification/ASSUpgrade/README.md) retain historical 0.9.1 import safety
  and the removed 0.9.0 automatic implementation. Preserve old global/ASS backup folders separately
  from the new owner-specific batches. Diagnose Missing Script by actual GUID, path and owning package.
- Preserve published ZIP bytes. Only the four component-bearing packages change in this release;
  keep BCG/Collider/Logger/PrefabPatcher package files unchanged unless they receive code/version changes.

## Retain reviewable results and publish the code

- Run Python unittest discovery under `scripts/unity` after harness edits. For packaging and release
  checks run `python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v`. Verify the real VPM
  workflow's archives, CRC, exact metadata, immutable version behavior and listing hashes.
- Use `capture_evidence.py` to compare imported source bytes against the final checkout and record
  source/XML/log hashes. Pass explicit `--tooling` command/log records and the actual local workflow
  report via `--vpm`; counts come from successful unittest output, and VPM payloads are checked against
  current source. Store functional, graphics, Player and performance results in
  Docs/VerificationResults.json, UnityNativeResults.json and PerformanceResults.json.
- Update workflow manuals and Docs/RefactorChanges.md with concrete changed behavior, migration
  requirements and measured effects. Scope counts overlap; report each scope separately.
- Commit and push the authorized `master` changes. Inspect the current remote tip and preserve
  remote work. Verify the exact pushed commit SHA, clean checkout and VPM workflow result.
  Keep original branch/backup refs as historical recovery points.
- Update reusable environment start instructions with the `master` workflow and current commands.
  Configuration draft saving and configuration publishing are separate actions in the environment UI.
