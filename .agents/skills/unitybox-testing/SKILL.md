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

## Verify manual migration, import preservation and serialized identity

- Opening/importing packages or loading assets preserves source bytes, script GUIDs, serialized fields
  and editing state. Do not install startup/import migrations or automatic OnValidate corrections.
  Normalize independent configuration/Options snapshots for generation instead of modifying source.
- The user authorized a manual component-reference migration on selected resources as one batch,
  without per-item confirmation. Do not add extra approval steps. Resolve an ambiguous old GUID once
  for its entire group; unknown/duplicate or open-asset/dependency cases stay in the report.
- Use [ComponentReferences](../../../Docs/ComponentReferences.md) for the eight-package matrix,
  six component FQCN/assembly/GUID entries and the manual tool. ACC has three production markers;
  ASS, Viseme and AO one each. BCG/Collider/Logger/PrefabPatcher have no own Avatar authoring component.
  AO 0.2 is a development snapshot, not a published legacy VPM ZIP.
- The shared Editor tool lives in `Assets/UnityBox/ComponentReferenceMigration/Editor` and has no ninth
  VPM manifest. Full source checkouts include it; VPM-only users import the standalone
  `https://sealoong.github.io/UnityBox/UnityBox-ComponentReferences.unitypackage` once.
- `Tools/UnityBox/Component References`: Use Project Selection or drag assets/folders, Analyze read-only,
  choose any ambiguous old GUID's target once, then Migrate Selected Assets to process the ready batch.
  Dependency evidence does not expand write scope. Open Scene/PrefabStage and dependencies are
  skipped; leave saving/closing to the user. Never remove or merge duplicate components automatically.
- Test all six components with actual old ACC/ASS/Viseme published ZIP imports and the old AO
  development snapshot. Preserve genuine generated script GUIDs, values, fileIDs, audio/object
  references and Prefab overrides, then install the current complete packages. Startup/import remain
  read-only; invoke the manual batch to restore selected eligible references.
- Check Analyze creates no backup or identity cache. Apply rechecks source evidence SHA and installed
  target identity, backs up every target before the first write, and only changes fixed-length GUID
  byte spans. Preserve fileIDs, all other fields/references, BOM/CRLF and project metadata. Existing
  corresponding metadata is copied to `UserSettings/UnityBox/ComponentReferenceMigration/<id>/Assets/...`.
- Cover batch scope, ambiguity mapping, unknown/valid-external/duplicate skipping, open resources and
  dependencies, changed files since scan, exact backups, idempotence and source configuration preservation.
  Record native and real-upgrade results in
  [ComponentReferences verification](../../../Docs/Verification/ComponentReferences/README.md).
- [ASSImportSafety](../../../Docs/Verification/ASSImportSafety/README.md) and
  [ASSUpgrade](../../../Docs/Verification/ASSUpgrade/README.md) retain historical 0.9.1 import safety
  and the removed 0.9.0 automatic implementation. Keep the old ASS backup directory separate from the
  new manual batches. Diagnose any Missing Script by actual m_Script GUID, script path and owning package.
- Ship stable package metadata and preserve already published ZIP bytes. This release changes only
  ACC/AO/Viseme/ASS packages; leave BCG/Collider/Logger/PrefabPatcher package files untouched unless
  their code and versions also change. Central documentation holds their no-component audit findings.

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
