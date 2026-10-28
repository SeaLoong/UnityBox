---
name: code-simplifier
description: Refine UnityBox C# code with explicit data contracts, real runtime checks and measured performance.
---

# UnityBox implementation style

Follow the branch and asset workflow in [AGENTS.md](AGENTS.md).

- Use surrounding C# conventions and preserve line endings. Editor APIs belong in Editor assemblies;
  serialized components and runtime actions belong in Runtime assemblies.
- Keep serialized field names, script GUIDs, binding paths, parameter defaults, layer order and callback
  contracts stable. Implement migrations explicitly when stored data changes.
- Give features clear input and output contracts. Follow the normal path directly; report actual input
  errors with the relevant parameter, object or asset path.
- Finish a behavior change with meaningful output checks: native Animator evaluation, save/reload,
  Undo/Redo, real build callbacks, Player execution or rendered pixels, according to the feature.
- Keep generated assets and source assets separate. Track generated ownership and preserve previous
  Build references so scene Undo restores a complete result.
- Keep Unity algorithms independent of optional SDK/MA/NDMF services. Select platform semantics
  explicitly and declare the actual assemblies and package versions used by each adapter.
- Measure hot paths with warmup and repeated samples. Publish input size, sample count, median/p95,
  environment and the corresponding correctness checks in [performance results](Docs/Performance.md).
- Write documentation as supported workflows, concrete examples, migration steps and executed results.
  Close implementation gaps through code and tests, and keep the manuals aligned with those results.
- Run [the relevant verification commands](Docs/Testing.md), retain genuine artifacts, and commit/push
  the completed changes on the authorized branch.
