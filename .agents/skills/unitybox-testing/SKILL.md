---
name: unitybox-testing
description: Prepare isolated UnityBox test projects, run real Unity EditMode tests, and distinguish compilation, licensing, and runtime test evidence.
---

# UnityBox testing

Use this skill when setting up or verifying UnityBox changes in a managed environment.
Tell the user when you begin applying it. Read [UnityEnvironment.md](../../../Docs/UnityEnvironment.md)
for the installed Editor, dependency cache, and current license limitation.

## Preserve the user's project

- Each cloud task is already isolated; do not create a Git worktree unless user explicitly requests one.
- Keep working checkouts and unique review evidence under `/workspace`; `/tmp` can be cleared when
  the environment restarts. The refactor checkout is `/workspace/unitybox-refactor-review`.
- Read the current checkout's `AGENTS.md` and inspect its branch/status. The refactor work and
  AnimatorOptimizer feature work may be in separate checkouts; do not switch branches to test them.
- Use `scripts/unity/test_project.py prepare --repo <checkout> --project <outside-checkout>`.
  Select packages with repeatable `--package`; use an independently named output per profile.
- Do not copy or modify the user's scene. Preparation copies packages using the selected checkout's
  packaging filter and keeps tests. Existing GUID metadata remains unchanged.
- Reuse only directories owned by the script. The script rejects nonempty unowned destinations.

## Choose the actual dependency scope

- Use Python 3.12 for these shared tools, including the safe tar extraction in the installer/fetcher.
  Reuse an already installed Editor and verified dependency lock/cache; do not redownload each task.
- `python scripts/unity/fetch_dependencies.py --profile core` verifies 2 real packages and 2 Editor
  modules; the completed `vrc` lock covers 17 packages and 12 modules. Both profiles are now cached
  and verified. An earlier CDN 403 was resolved after the environment's network state changed.
  The full VRC fixture pins SDK Base/Avatars 3.8.2 with MA 1.18.0 and NDMF 1.11.0: actual DLL
  inspection found SDK 3.7.0–3.7.2 lacks the two-argument VRCConstraintSource constructor used by
  MA 1.13/1.18. SDK 3.7.3 first provides it, so ACC/BGC MA adapters require at least that version.
  Original SDK 3.7 directories/archives/lock remain under the environment's
  `unity/sdk-compatibility/3.7.0/` for the narrower minimum-version evidence; do not relabel those
  results as proving the full MA integration.
  `--update-lock --profile core` updates only core and preserves VRC records;
  `--update-lock --profile vrc` records a full graph only after all downloads and hashes succeed. Respect
  `prepared_profiles`; never infer a completed VRC graph from a core-only lock.
- Deterministic HTTP denials (including 403) are not retried. Retry a denied URL only when there is
  relevant new evidence, such as an actual environment/network-policy change.
- `--profile core` embeds real UTF 1.1.33/NUnit 1.0.6 from `--dependency-cache` and tests enabled
  Unity-only assemblies. Repository VPM declarations for full workflows are recorded but do not
  force SDK installation; core success does not establish SDK/NDMF correctness.
- `--profile vrc` embeds real VRChat packages and resolves the chosen packages' declared dependencies.
  Use `--offline` to require all UPM dependencies in cache. Missing VPM dependencies must be cached.
  Preparation checks SDK >= 3.7.0, dependency ranges and prerelease ordering before writing the project.
  For full MA integration use the pinned SDK 3.8.2 fixture, not merely any SDK satisfying >= 3.7.0.
  VRC also embeds the real Editor-bundled UGUI 1.0.0 source package, explicitly declared by SDK 3.8.2.
  Core does not acquire this extra dependency.
- For NDMF, cache the real package and its dependencies, then pass `--dependency nadena.dev.ndmf`.
  Add `--dependency nadena.dev.modular-avatar` when checking ACC/BGC's optional MA integration;
  having MA in the cache alone does not add it to a prepared project.
  Honor assembly version constraints. Never generate fake SDK/NDMF types or substitute skipped
  integration tests for coverage.

## Execute and inspect evidence

1. Run `python scripts/unity/test_project.py run --project <project> --unity <Editor> --timeout 1200`.
   The default Editor is `/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity`.
2. The runner sets writable XDG locations without changing HOME. If a legitimate license is already
   configured under a writable XDG root, pass that location with `--xdg-root`.
3. Run `python scripts/unity/test_project.py report --project <project>`. Review the actual exit code,
   fresh XML, nonzero executed tests, tested assemblies, failures, skipped cases, and log blockers.
4. If there is no valid Unity license, report the license error and missing XML as a blocked run.
   Continue independent source/packaging or real-DLL compilation checks. Do not report Unity tests
   as passing merely because the Editor installed or C# compiled.
   The user has confirmed no Unity authorization is currently available; do not repeatedly relaunch
   license checks or ask again while that condition is unchanged.
5. Filtered runs may use `--assembly-names 'A;B'` and `--test-filter`; state the limited scope. After
   relevant fixes, perform the full requested scope rather than hiding failures with a narrow filter.

The runner keeps each invocation's command, logs, JSON and actual XML under the isolated project's
`.unitybox-test-results/`. Timeout cleanup targets only the process group it created. Do not activate,
submit, or change licensing credentials as part of an ordinary test run.

## Validate harness changes

Run `python -m unittest discover -s scripts/unity -p 'test_*.py' -v` when changing these tools.
For packaging rule changes also run `.github/scripts/test_package_vpm.py` as directed by
`AGENTS.md`. Label Python harness tests, package checks, Roslyn compilation against real Editor DLLs,
and Unity EditMode execution separately in the final result.

When licensing blocks Editor execution, `scripts/unity/compile_assemblies.py` can type-check the
actual source against the Editor's Roslyn/compiler references and real cached dependency packages.
Use `--tests --profile core` or `--tests --profile vrc`, an explicit `--repo`, `--editor`,
`--dependency-cache`, and an `--output` outside the checkout; select packages with `--package`.
Unlike the project preparer, the compiler's vrc profile considers every cached dependency package.
Inspect its `compile-report.json`, source hashes, errors, and omitted upstream optional references.
It does not reproduce all Unity importer, assembly resolution, platform-symbol, or IL postprocessor
rules. The current 23-assembly optimizer and 55-assembly seven-plugin integration checks passed,
but Unity import/build/EditMode execution remains blocked by licensing.
