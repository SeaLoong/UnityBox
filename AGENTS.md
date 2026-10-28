# Repository workflow

- Develop the seven repository plugins on `refactor`; develop Animator Optimizer on
  `feat/animator-optimizer`, already rebased onto `master`. Keep both source branches independent.
  Compose eight-package verification with `test_project.py --source-package`, using the two checkouts.
- The user authorized committing and pushing completed, tested work to both branches. Coordinate
  shared edits and final pushes; preserve any new remote work. Original optimizer history is backed up
  at `backup/animator-optimizer-before-rebase`.
- Preserve user scenes, fixtures, original GUIDs and unrelated edits. Each cloud task already has its
  checkout; use the existing directories and create worktrees only when the user requests one.
- Follow `code-simplifier.md`: implement clear input/output contracts, finish features with actual
  native behavior/build/Player checks, and document measured workflows and supported scenarios.
- Keep ordinary Unity functionality independent of optional SDK/MA/NDMF services. Preserve actual SDK
  `IEditorOnly` markers when the SDK is installed, and use real dependency/version constraints.
- Prepare owned projects outside checkouts using `scripts/unity/test_project.py`. Read
  `.agents/skills/unitybox-testing/SKILL.md`, `Docs/UnityEnvironment.md` and `Docs/Testing.md`.
  Runtime, graphics and performance scopes have explicit commands and saved artifacts.
- Shared tools require Python 3.12. Run `python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v`
  after tool edits, and `python3 .github/scripts/test_package_vpm.py -v` for packaging changes.
  Keep experimental ASS probes out of release packages and preserve known metadata.
- Record test outputs and performance data in `Docs/VerificationResults.json` and
  `Docs/PerformanceResults.json`; explain user-visible behavior and migration in the manuals.
