# Repository workflow

- All eight plugins are maintained together on `master`. The user authorized continued improvement
  and committing and pushing completed tested work to `master`. Preserve remote work and the original
  reviewed feature/refactor histories as recovery points.
- Preserve supplied scenes, original GUIDs, source assets and unrelated edits. The canonical
  `Assets/UnityBox.unity` retains the user's newer temp/refactor scene bytes; the older optimizer
  example scene remains available in its original feature-branch commit history. Use the existing checkout; do not create worktrees unless
  the user requests them.
- Opening/importing packages and loading Scene/Prefab assets must preserve existing source bytes,
  GUIDs, serialized values and editing state. Do not add automatic migrations, bulk GUID replacement,
  scene reloads or OnValidate corrections of stored configuration. A general fix request does not
  authorize migration or backup restoration; those require an explicit user-selected reviewed operation.
  Normal requested generation and processing of build-scene/configuration copies remain authorized.
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
