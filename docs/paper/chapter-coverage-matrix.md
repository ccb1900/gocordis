# Paper chapter coverage matrix (maintained)

Source of truth: `docs/论文.pdf` (arXiv 2608.25512v1). Repo branch `dotnet`,
verified at commit `c4f219f`. This document supersedes the review snapshot
`docs/gocordis-paper-chapter-coverage-matrix.md` (HEAD `181c56e`) as the **main
chapter matrix**; that snapshot is retained only as review input. C01–C40 stay
the separate "semantic item → test" ledger in
`docs/migration/paper-coverage-matrix.md`.

Statuses distinguish three things that must not be conflated:
**kernel primitive implemented**, **product chain wired**, **paper behavior
evidenced by a named test**.

## Chapter-by-chapter status

| Chapter | What the paper asks | C# state | Evidence / boundary |
| --- | --- | --- | --- |
| 1 Introduction | temporal + spatial composability, plugin/Agent motivation | design goal realized | `Context.EffectAsync`, `Inject/Provide`, one Runtime; `PaperGapClosureTests.C01_…`. Agent Harness is motivation, not a deliverable. |
| 2 Preliminaries | static Effect/Coeffect theory | mapped, not reimplemented | runtime Effect stack + typed `Key<T>` declarations; no Monad/Comonad library, by design. |
| 3 Revertible Effects & Reactive Coeffects | `∂Γ` inverse accumulation, Σ get/set, Realm/Meta, unified Context | implemented with language adaptation | `CoreSemanticsTests`, `PaperGapClosureTests`, strict LIFO tests, `ContextUsesCapturedDeclarationsWhenTheComponentMutatesAfterLoad`, `ObservationEquivalenceTests`. Inverse witness correctness remains the component author's contract. |
| 4 Calculus of Dynamic Composition | Fiber lifecycle, confinement, metatheory | runtime主体 implemented; theorems have executable evidence only | `TheoremMatrixTests`, `TheoremPremiseTests`, `SmallStateExplorationTests` (bounded command sequences, trace/shrink), `DependencyGraphSeparationTests`. Not a mathematical proof; Thm73 upper bound explicitly unproved. |
| 5 Implementation & Case Study | Core Library, Declarative Loader, HMR, Koishi | all three layers wired with behavioral tests | `docs/paper/chapter-5-coverage.md`: field-level Entry/Group/Include reconciliation, three-phase HMR with ALC reclaim, packaged Koishi case with independent console. |
| 6 Discussion | boundaries, multiplexing, sandboxing, language independence, versioning | selectively engineered, boundaries explicit | `docs/paper/chapter-6-boundaries.md`: rolling-upgrade/drain gates, threat model, three-backend quadruple, three dependency graphs, SharedAssembly identity. |
| 7 Related Work | positioning | documentation only | no runtime module claimed. |
| 8 Conclusion | honest summary | this matrix + `docs/current-status.md` | no claim of complete formal proof, full HMR generality, full Koishi ecosystem or malicious-plugin sandbox. |

## Review findings closed (P0–P4)

| Review item | Closure evidence |
| --- | --- |
| P0.1 declaration snapshot | `Context` guards read `Fiber.CapturedInjectKeys/CapturedProvideKeys`; `ContextUsesCapturedDeclarationsWhenTheComponentMutatesAfterLoad`. |
| P0.2 observation fields | `ObservationEquivalence` documents and compares name/state/declarations/bindings/failure type+message/cleanup and effect failures; `ObservationEquivalenceTests`. |
| P0.3 Thm73 premises | `TheoremPremiseTests` header plus `theorem-matrix.md`: finite names, finite iterator, acyclic provider precedence; **upper bound not proved**. |
| P0.4 theorem statuses | `semantic-mapping.md`, `migration-coverage.md`, `paper-coverage-matrix.md`, `current-status.md`: "executable evidence, not formal proof". |
| P0.5 cleanup inverse failure | `CleanupIncomplete` retained in snapshot, never equivalent to clean unwind; `InverseFailureTests`, `ObservationEquivalenceTests`. |
| P1.1–P1.7 declarative configuration | `EntryDiff`, `ApplicationLoader`, `PluginRegistry` tests in `EntryReconciliationTests`, `ConfigurationTreeTests`, `PluginTransactionTests`; unaffected-entry and rollback-value assertions added. |
| P2.1–P2.8 product HMR | `HmrEngine`, `HmrEngineTests`, `HmrManagedTests`; `HmrPlan.RestartRequired` makes Core/contract upgrades an explicit non-hot-swappable boundary. |
| P3.1–P3.5 metatheory | `SmallStateExplorationTests`: legal command-sequence enumeration, all required interleavings, replayable + minimized traces, rule/step counters; `ObservationEquivalenceTests` covers Def 31–37 projection and failed-fiber exclusion. |
| P4.1–P4.5 chapter 6 | `RollingUpgradeBoundaryTests`, `WasmImportAllowlistTests`, `ProcessPermissionPolicyTests`, `BackendLifecycleTests.ManagedRealComponentError…`, `DependencyGraphSeparationTests`, `chapter-6-boundaries.md`. Shared-contract identity/versioning (§6.6) was already covered by `SharedAssemblyRegistryTests` / `VersioningTests`. |

## Not claimed

- Formal proofs, Thm73 complexity bound, arbitrary cross-backend equivalence.
- Full Koishi ecosystem / plugin marketplace / browser-side TS reconcile loop.
- Malicious-plugin sandbox: Managed ALC is trusted in-process; Process has an OS launch policy but NO credential drop / privilege policy; WASM limits are engine-level plus an import allowlist.

## Verification

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
(cd web/console && npm test && npm run build)
(cd web/plugin-kit && npm test)
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase
bash samples/KoishiShowcase/build-and-run.sh /tmp/gocordis-koishi-showcase
```
