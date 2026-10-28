# Branch workflow

- Work on `feat/animator-optimizer`, already rebased onto `master`. The seven other actively developed
  plugins are on the separate `refactor` checkout at `/workspace/unitybox-refactor-review`.
  This branch retains their baseline source; use the maintained branch manuals for current workflows.
- The user authorized committing and pushing completed, tested changes to both branches. Coordinate
  edits and final pushes; preserve remote work. Original optimizer history is backed up at
  `backup/animator-optimizer-before-rebase`.
- Preserve supplied scenes, original GUIDs and unrelated assets. Use the current checkouts and create
  worktrees or switch branches only when the user directs that workflow.
- Follow `code-simplifier.md`. Complete feature changes with native Animator, asset reload, Undo/Redo,
  build/Player or rendered-output checks, then update concrete supported workflows and measured results.
- Keep generic Unity behavior independent of optional SDK/MA/NDMF assemblies. Select parameter/platform
  semantics explicitly, preserve actual `IEditorOnly` integration, and use real dependency conditions.
- Use `scripts/unity/test_project.py` for isolated projects. `--source-package` composes the optimizer
  with the seven refactor packages for integration without merging source branches.
- Follow `.agents/skills/unitybox-testing/SKILL.md`, `Docs/UnityEnvironment.md` and `Docs/Testing.md`.
  Shared Python tools require 3.12; run their unittest suite after edits and the package test suite for
  packaging changes. Runtime, graphics and serial performance execution have separate saved scopes.
- Record genuine run identifiers, source/content fingerprints, outputs and measurement data in the
  verification/performance JSON files and keep the manuals synchronized with those artifacts.
