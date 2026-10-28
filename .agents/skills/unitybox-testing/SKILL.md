---
name: unitybox-testing
description: Install and activate a real Unity cloud toolchain, execute isolated functional, graphics, Player and performance checks, and retain source-matched evidence.
---

# UnityBox real Unity verification

Read the checkout's `AGENTS.md`, branch/status, [UnityEnvironment.md](../../../Docs/UnityEnvironment.md)
and [Testing.md](../../../Docs/Testing.md). Tell the user when applying this skill for the first time.
The user authorized completing, committing and pushing tested changes on `refactor` and
`feat/animator-optimizer`; preserve those separate source branches and supplied assets/GUIDs.

## Set up the machine

- Inspect the managed runtime's current network state. Preserve inherited proxy, CA and authentication.
- Use Python 3.12+. Reuse `/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity`
  (2022.3.22f1) and `/workspace/unitybox-setup/unity/dependencies` when present.
- Otherwise run `scripts/unity/install_editor.py`, then `fetch_dependencies.py --profile core`
  and `--profile vrc`. The installers verify the pinned hashes, package graph and Editor modules.
- Current machine activation succeeded through official Hub with the user's Personal account.
  Ordinary tests use `--xdg-root /workspace/unitybox-setup/unity/interactive` and keep HOME unchanged.
- On a new machine, provide an authenticated private desktop for official Hub sign-in. Let the user
  handle credentials/terms. Use official Hub's Licenses and Installs → Locate for the old Editor.
  Keep account data private and close the external relay after the user finishes.
- For container graphics use private Xauthority and Xvfb without MIT-SHM, x11vnc `-noshm`.
  If proxy CA trust causes Hub's popup to close, use the system p11-kit NSS trust module for those
  processes while retaining TLS verification. See the environment manual for the retained helpers.

## Prepare the actual dependency scope

- Keep unique projects/evidence in `/workspace`, outside source checkouts. `/tmp` is scratch space.
- Use `test_project.py prepare --repo <checkout> --project <new-owned-path> --profile core`.
  Select packages with repeatable `--package`. Compose another checkout's package with
  `--source-package <actual-package-directory>`; do not merge branches for testing.
- Main checkout: `/workspace/UnityBox` on `feat/animator-optimizer`.
  Seven-plugin checkout: `/workspace/unitybox-refactor-review` on `refactor`.
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
   checks, successful scene stripping and source authoring configuration retention.
6. Stop other Editors before `test_project.py run ... --benchmark`. Require structured records for
   every selected plugin. Preserve sizes, warmups, sample counts, timings and memory-counter meaning.
   Player steady-state Animator.Update and Editor generation measure separate operations.
7. Parallelize at most two functional Editors. Dedicated performance execution is serial.
   Delete only rebuildable owned caches when disk space requires it, retaining imported DLLs,
   source snapshots and raw evidence. Check active projects before cleaning their caches.

## Retain reviewable results and publish the code

- Run Python unittest discovery after harness edits and `.github/scripts/test_package_vpm.py`
  for package checks. Verify the real VPM workflow's archives, CRC, exact metadata and listing hashes.
- Use `capture_evidence.py` to compare imported source bytes against final checkouts and record
  source/XML/log hashes. Store current functional, graphics, Player and performance results in
  Docs/VerificationResults.json, UnityNativeResults.json and PerformanceResults.json.
- Update workflow manuals and Docs/RefactorChanges.md with concrete changed behavior, migration
  requirements and measured effects. Scope counts overlap; report each scope separately.
- Commit and push both authorized branches. Inspect current remote tips, preserve remote work,
  then verify pushed commit SHAs and clean checkouts. Keep the original optimizer backup ref.
- Update reusable environment start instructions with final remote branches and current commands.
  Configuration draft saving and configuration publishing are separate actions in the environment UI.
