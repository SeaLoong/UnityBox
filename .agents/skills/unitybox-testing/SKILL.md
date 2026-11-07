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

## Verify confirmed whole-Assets upgrades and serialized identity

- Ordinary package import/startup preserves source bytes, Runtime GUIDs and stored values. Normalize build copies only.
- ACC0.5.3, AO0.3.3, ASS0.9.4 and Viseme0.2.3 embed recovery in existing owner Editor assemblies.
  Tools/UnityBox/<owner>/Upgrade Component References confirms once, then processes all Assets.
  Owner menus are Advanced Costume Controller, Animator Optimizer, ASS and Viseme Blendshape Override.
  ACC/AO Open actions are siblings; there is no selection window or per-GUID mapping UI.
- Test the exact ComponentReferenceUpgrade.Execute(Func<bool>) route: cancel before discovery/I/O,
  or confirm once then call UpgradeProjectAssets. Prove unrelated current Selection does not constrain scope.
- Identify known class names/GUIDs directly; confirm ACC Part Group from exact schema, enum and Outfit/Prefab
  ancestry, then associate the same GUID elsewhere. Generic foreign Mode/GroupName fields stay unchanged.
- Back up the complete batch before the first write; only replace supported invalid script GUID spans.
  Preserve fileIDs, fields, object references, asset metadata, UTF-8/BOM/CRLF and duplicate components.
  Per-owner backup root is UserSettings/UnityBox/<PluginFolder>/ComponentReferenceMigration/<id>/Assets.
- Test clean saved Scene/Prefab Stage suspension/restoration, selection and active scene restoration;
  preserve dirty/untitled contexts, unsaved edits and their dependencies without saving or reloading them.
  Scan(paths)/Apply(plan) remain lower-level APIs with their original open-resource protection.
- Validate the new command with actual old-derived resources in different Assets folders, per-owner and
  combined consumers, no legacy GUID overrides, no extra installer/module. Retain producer/source/ZIP/DLL
  identities and byte comparisons. Mark previous selected-resource evidence as its actual historical version.
- ACC exposes one Open command for complete Avatar generation; without SDK/MA it explains required
  integrations. No standalone Controller window or output selector. Keep the reusable animation core,
  native Animator/Player coverage, existing generated assets and Avatar settings.
- Use [ComponentReferences](../../../Docs/ComponentReferences.md) and
  [current evidence](../../../Docs/Verification/ComponentReferences/README.md) for exact commands/results.


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
