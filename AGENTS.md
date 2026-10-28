# Repository workflow

- Keep the repository-wide audit and refactoring changes on `refactor`. Review the optimizer on its own feature branch after rebasing it onto `master`. Preserve local work when switching branches; do not create Git worktrees unless explicitly requested. The user's latest branch instruction supersedes earlier directions to work on `master`.
- Preserve user-authored scenes, fixtures and unrelated changes. Keep experimental ASS probes out of release packages using `.github/scripts/package_vpm.py`.
- Unity `.meta` files contain persistent asset and script identities. Preserve existing GUIDs and include metadata in packages; do not regenerate known metadata to resolve missing references.
- Run `python3 .github/scripts/test_package_vpm.py` when changing package assembly. For plugin behavior, use the relevant Unity EditMode tests and an isolated Unity 2022.3 project with the actual core/integration dependency profile described in `Docs/Testing.md`.
- Clearly distinguish source/syntax checks and VPM archive validation from Unity compilation, Editor tests, and VRChat runtime validation. A successful zip build does not validate a plugin's behavior.

- Keep generic Unity behavior independent of optional SDK/MA/NDMF assemblies. Platform semantics must be explicit, not inferred merely from installed packages. Preserve real SDK `IEditorOnly` markers when available.
- Use `scripts/unity/test_project.py` outside the checkout and follow `.agents/skills/unitybox-testing/SKILL.md` and `Docs/UnityEnvironment.md`. Never substitute SDK stubs, missing test assemblies, or zero-test runs for validation.
- Shared tools require Python 3.12; run `python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v` for changes to them. Roslyn type checks do not replace Unity import, IL postprocessing, builds, or tests.
