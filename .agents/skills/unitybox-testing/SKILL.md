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
- ACC0.5.3, AO0.3.3, ASS0.9.7 and Viseme0.2.3 embed recovery in existing owner Editor assemblies.
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


## Verify ASS time editing and obfuscation output

- ASS0.9.7 Inspector commits delayed numeric edits with hold at least tolerance+0.01s,
  tolerance at most hold-0.01s, and maximum at least max(1s,hold+tolerance+0.05s). Preserve
  default0.5/0.3 values. Verify real SerializedObject edits and Undo/Redo, not copied arithmetic.
- Open and repaint an Inspector containing legacy invalid values; compare stored fields, dirty state,
  Prefab/source bytes and metadata. Drawing must preserve values. Exercise the explicit correction
  action separately, and require a real editor window for graphics/event checks.
- Use real saved/reloaded Controllers to check enabled/disabled obfuscation, independent decoy-layer
  and decoy-state options, declared condition parameters, all-new-state WD On, and source graph bytes.
  Standalone and SDK paths share ObfuscationGraph in the existing Unity-only Editor assembly.
- Keep the original0.9.6 word pools and Clip/state naming mappings unchanged. This update covers
  time editing and actual obfuscation-option output only. Verify decoy Clip assignment and serialized
  save/reload results without requiring new literal labels or changes to the vocabulary.
- Check metadata paths use a root absent from the actual Avatar, including a deliberate prefix
  collision control; generated metadata curves must not animate existing user objects.
- Invoke the actual late SDK PlayableObfuscationProcessor after NDMF finalization. Without Contacts,
  inspect the final renamed copy and preserved source/output behavior. With Contacts, preserve the
  third-party Playable names while retaining ASS-owned obfuscation/decoys. Keep the compatibility guard.
  A manual NDMF bake/preview has no late SDK callback and must not acquire an extra whole-graph rename.
- Store versioned records under [ASSTimingObfuscation](../../../Docs/Verification/ASSTimingObfuscation/README.md).
  Keep earlier WD/audio evidence tied to its original version and source fingerprint.

## Verify ASS WD On generation and facial reset

- ASS0.9.6 sets every newly generated state to WD On: password, countdown, audio, lifecycle,
  release, defense, decoys/obfuscation and newly created default Idle. Use a fixed On policy; do
  not match the source FX or modify copied user states. Retain serialized legacy `writeDefaultsMode`
  data while ignoring it for new-state generation. Normal build regenerates the output copy;
  import/startup does not convert saved controllers or scenes.
- Test actual Animator output with an original WD On Expression writing BlendShape85 and a sparse
  Neutral using an empty Clip or null Motion. The original graph resets to0; the former ASS Off
  graph retained85. The new generated graph must reset to0 without manually overriding its flags.
- Include original WD Off hold behavior, explicit-zero Neutral, original state/Clip/metadata bytes,
  full password/countdown/Restart and the ASS-generated state flags. Preserve user On/Off values.
- Run two full timeout/restart cycles in the native Animator. Countdown's TimeUp-to-Release must have
  a real TimeUp condition; a transition with neither Exit Time nor conditions is ignored by Unity.
  Local Restart takes priority back to Remote in Countdown and Audio, restoring the next timer and
  warning phase. Check progress returns to its default after success/timeout, timeout hides Overlay
  and audio objects, and the next cycle restores progress and warning audio.
- Check SDK/NDMF final Controller states and facial output after graph commit, including late
  ASS generation, decoy and obfuscation paths. Include native Player/build output as appropriate.
- Store current records under [ASSWriteDefaultsOn](../../../Docs/Verification/ASSWriteDefaultsOn/README.md).
  Keep the former WD Off analysis and earlier versioned evidence under their actual source identity.

## Verify ASS audio import and SDK upload validation

- Shipped audio must include stable AudioImporter `.meta` files in the actual release ZIP. ASS0.9.5
  retains all four MP3 byte streams and enables `loadInBackground`. Check real imported AudioClip
  flags, loading type, playable samples and source/metadata fingerprints; do not add audio import
  postprocessors or scan/rewrite unrelated user assets.
- Preserve option semantics: `disableWarningSound` suppresses only countdown audio; password success
  audio remains. `disableOverlay` controls the visual overlay independently. Generate actual Controllers
  for every warning/overlay combination, including both disabled, and inspect final PlayAudio clip references.
- Call the real SDK `VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder` in assembly `VRC.SDK3A.Editor`,
  invoking private static `ScanAvatarForAnimatorPlayAudio` with the generated Descriptor. Require an
  empty result for shipped audio. SDK rejects `DecompressOnLoad && !loadInBackground` as an upload error.
- Use an owned temporary Assets copy as a positive/negative control: background disabled must return
  that exact AudioClip; enable background loading and reimport, then require the SDK scan to return empty.
  Keep source package audio and user resources untouched throughout the control.
- Run the actual NDMF pass resolver and final commit path, then inspect the committed Descriptor FX
  Controller's audio references and apply the same real SDK check. Normal package update followed by
  the regular build regenerates ASS output; source configuration and existing user animations remain.
- Keep audio patch results and version-specific raw evidence in
  [ASSAudioUpload](../../../Docs/Verification/ASSAudioUpload/README.md), with actual commands and counts.
  Retain earlier whole-library and component-upgrade evidence under its original version/source identity.

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
