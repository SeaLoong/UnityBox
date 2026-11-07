# Repository workflow

- All eight plugins are maintained together on `master`. The user authorized continued improvement
  and committing and pushing completed tested work to `master`. Preserve remote work and the original
  reviewed feature/refactor histories as recovery points.
- Preserve supplied scenes, original GUIDs, source assets and unrelated edits. The canonical
  `Assets/UnityBox.unity` retains the user's newer temp/refactor scene bytes; the older optimizer
  example scene remains available in its original feature-branch commit history. Use the existing checkout; do not create worktrees unless
  the user requests them.
- Opening/importing packages and loading Scene/Prefab assets preserve source bytes, GUIDs, stored
  values and editing state. Do not add startup/import migration, automatic scene reloads or OnValidate
  corrections of stored configuration; normalize independent build/configuration copies instead.
- Each affected plugin ships recovery inside its existing Unity-only Editor assembly through normal
  plugin updates. Do not add a standalone installer, shared package or extra bootstrap module.
- The user authorized a plugin Tools menu with one confirmation followed by automatic upgrade of
  that owner's components across the entire Assets tree. No resource selection, mapping picker or
  recovery panel. Use supported serialized identity, GUID cohorts and ACC Outfit/Prefab context.
  Skip foreign/unknown and duplicate components; back up all files before changing GUID byte spans.
- A confirmed project upgrade may temporarily suspend and restore affected clean saved Scene/Prefab
  contexts. Never save unrelated user edits; preserve dirty/untitled contexts and their dependencies.
  Ordinary startup/import remains read-only. ACC has one Open entry for complete Avatar generation; no standalone Controller output UI.
- Follow `code-simplifier.md`. Use explicit input/output contracts and complete behavior changes with
  native Animator, saved-asset reload, Undo/Redo, Player/build or rendered-output checks as appropriate.
- Keep generic Unity algorithms independent of optional SDK, MA and NDMF assemblies. Preserve real
  SDK `IEditorOnly` markers and declare actual dependency/version requirements.
- Read `.agents/skills/unitybox-testing/SKILL.md`, `Docs/UnityEnvironment.md` and `Docs/Testing.md`.
  Prepare isolated projects outside the checkout with `scripts/unity/test_project.py --repo`.
- Shared tools require Python 3.12. Run their unittest suite after tool edits and the packaging suite
  for package changes. At most two functional Editors may run concurrently; final performance and
  Player measurements run serially with other Editors stopped.
- Record real commands, scopes, source fingerprints and artifacts in `Docs/VerificationResults.json`
  and `Docs/PerformanceResults.json`; update every affected manual and current-branch instruction.
- Coordinate shared edits and Git operations. Review changes, run the relevant combined-source checks,
  then push `master` without rewriting existing remote history.
