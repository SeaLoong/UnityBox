---
name: code-simplifier
description: Refine UnityBox C# code while preserving behavior and serialized data.
---

# UnityBox code simplification

Use the branch workflow in [AGENTS.md](AGENTS.md). This repository contains Unity
C# plugins and Python package scripts; earlier React/TypeScript conventions do not apply.

- Follow surrounding C# formatting and preserve line endings. Keep Editor APIs in
  Editor assemblies and serializable components in Runtime assemblies.
- Preserve serialized field names, GUIDs, animation binding paths, parameter types
  and defaults, layer ordering, and build callback semantics.
- Simplification preserves behavior. For a confirmed bug fix, document before/after
  behavior separately and add a regression test where meaningful.
- Preserve Undo and isolate generated controllers from original assets. Check
  references before cleanup; an empty AssetDatabase path can mean an in-memory asset.
- Keep optional integrations behind assembly/version constraints. Declared minimum
  dependency versions must support the APIs used.
- Report actionable failures instead of swallowing exceptions. Preserve underlying
  test exit statuses when capturing output.
- Follow [Docs/Testing.md](Docs/Testing.md); distinguish syntax/package checks from
  Unity compilation and functional tests, and state what remains unrun.

- Keep ordinary Unity logic independent of optional platform assemblies; use explicit adapters and actual package/version constraints. Installed SDK packages alone must not enable platform input semantics.
- Preserve the real SDK `IEditorOnly` marker when available, and distinguish installed integrations from actual processing success before skipping fallback callbacks.
