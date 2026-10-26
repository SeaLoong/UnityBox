# Repository workflow

- Keep the repository-wide audit and refactoring changes on `refactor`. Review the optimizer on its own feature branch after rebasing it onto `master`. Preserve local work when switching branches; do not create Git worktrees unless explicitly requested. The user's latest branch instruction supersedes earlier directions to work on `master`.
- Preserve user-authored scenes, fixtures and unrelated changes. Keep experimental ASS probes out of release packages using `.github/scripts/package_vpm.py`.
- Unity `.meta` files contain persistent asset and script identities. Preserve existing GUIDs and include metadata in packages; do not regenerate known metadata to resolve missing references.
- Run `python3 .github/scripts/test_package_vpm.py` when changing package assembly. For plugin behavior, use the relevant Unity EditMode tests and a Unity 2022.3 project with the dependencies declared in that plugin's `package.json`.
- Clearly distinguish source/syntax checks and VPM archive validation from Unity compilation, Editor tests, and VRChat runtime validation. A successful zip build does not validate a plugin's behavior.
