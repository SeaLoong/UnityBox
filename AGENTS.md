# Branch workflow

- Work on `feat/animator-optimizer`, which has been rebased onto `master`. The repository-wide plugin fixes and current manuals are on the separate `refactor` branch; do not assume they are merged here.
- Preserve the supplied scene, original GUIDs, and unrelated assets. Do not create a worktree or change branches without the user's direction.
- Use C#/Unity conventions in `code-simplifier.md`. Keep optional NDMF tests behind dependency and test assembly constraints.
- Run `.github/scripts/test_package_vpm.py` for packaging changes. See `Docs/Testing.md` for Unity tests. Syntax and archive checks are not Unity compilation or behavior validation.
