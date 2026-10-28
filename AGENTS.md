# Branch workflow

- Work on `feat/animator-optimizer`, which has been rebased onto `master`. The repository-wide plugin fixes and current manuals are on the separate `refactor` branch; do not assume they are merged here.
- Preserve the supplied scene, original GUIDs, and unrelated assets. Do not create a worktree or change branches without the user's direction.
- Use C#/Unity conventions in `code-simplifier.md`. Keep optional NDMF tests behind dependency and test assembly constraints.
- Run `.github/scripts/test_package_vpm.py` for packaging changes. See `Docs/Testing.md` for Unity tests. Syntax and archive checks are not Unity compilation or behavior validation.

- Keep generic Unity behavior independent of optional SDK/MA/NDMF assemblies. Platform semantics (including built-in parameters) must be explicit, not inferred merely from installed packages. Preserve the real SDK `IEditorOnly` marker when its package is available.
- Use `scripts/unity/test_project.py` to prepare isolated core and integration projects outside the checkout. Follow `.agents/skills/unitybox-testing/SKILL.md` and `Docs/UnityEnvironment.md`; never substitute SDK stubs or a zero-test run for validation.
- Test tool changes with `python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v`. A successful Roslyn signature check is not a Unity import, build, or EditMode test pass.
