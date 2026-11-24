# Chapter 5 Coverage Matrix (paper §5, maintained)

Source of truth: `docs/论文.pdf` (arXiv 2608.25512v1, 92 pages), page numbers
below are PDF page numbers from the outline. C# repo: `dotnet` branch. This is a
**behavioral evidence matrix**: an implementation counts only when a named test
exercises the consumer-visible behavior. Kernel primitives, product wiring and
paper behavior are tracked separately.

## §5.1 Core Library (p.57–64)

| Paper | Concept | C# implementation | Test evidence | Status | Semantic difference / acceptance |
| --- | --- | --- | --- | --- | --- |
| §5.1.1 p.59 | `ctx.effect`, iterator, inverse fold, self-disposal, parent composition, LIFO | `Context.EffectAsync` (`src/Gocordis.Core/Context.cs`), `IIterativeComponent.IterateAsync`, `EffectSlot`, `EffectRegistration` | `CoreSemanticsTests.EffectsUnwindInStrictLifoOrder`, `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`, `EffectFailureRetentionTests`, `CoreSemanticsTests.OverrideMetaUnwindsInStrictLifoOrderToTheDeclaredView` | done | Effect inverse witness is the component author's contract: the Runtime tracks and orders inverses but cannot prove an external side effect was actually reverted (paper §5.1.1, §6.1). Test evidence is behavioral, not a proof of arbitrary inverse correctness. |
| §5.1.2 p.60–61 | `@@store`, `@@isolate`, `@@intercept`; provision/notification; isolation and interception | `Runtime` `_providers`/`_realmProviders`/`_consumersByProvider`, `Realm`, `IsolateAsync`, `Context.DeclareMeta`/`OverrideMeta`/`Meta<T,M>` | `CoreSemanticsTests.RealmsIsolateTheSameKeyIntoIndependentProviders`, `IsolateReassignsAKeyAndIsReversible`, `MetaInterceptionIsRightBiasedAndDoesNotReload`, `PaperGapClosureTests.C07_…`, `C09_…`, `C11_…`, `C37_…` | done | Coeffect commutativity is a provider obligation; Core enforces declaration/realm rules, not semantic commutativity of arbitrary operations. |
| §5.1.3 p.61–63 | `ctx.use`, Fiber/Activation, target identity, `refresh`/`reload`/`unload`, inertial chaining, dependent drain before inverse | `Runtime.LoadAsync`, `Fiber`, `Activation`, `HandleApplyCompleted`, `HandleUnwindCompleted`, `StartUnwind`, `ConsumerOf` index | `CoreSemanticsTests.DependencyLossMovesConsumerToPendingAndRestoresIt`, `TheoremMatrixTests.Thm70_Matrix`, `Thm73_Matrix`, `RevisionSemanticsTests.RehomeRetiresOldFiberAndReinsertsWithNewIdentityAndRealm`, `AuditRegressionTests.NestedReconcileDoesNotReactivateTheStillActivatingParent`, `IsolateCannotHijackAKeyOwnedByAnotherRealm` | done | Fiber target identity is by provider Fiber+Activation, not by value: replacing a provider with an equal value still forces withdrawal/reactivation. Theorem 70/73 evidence is executable finite scenarios, not a universal proof. |
| §5.1.4 p.64 | Proxy-mediated `ctx[key]`: committed view, declared-but-unsatisfied → INACTIVE_ACCESS, undeclared → UNDECLARED_ACCESS | `Context.Require<T>(Key<T>)` + `RequireDeclared` + `EnsureReadable`; no dynamic Proxy | `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`, `UndeclaredProvideFailsTheFiber`, `DependencyLossMovesConsumerToPendingAndRestoresIt`, `CleanupCanStillRequireItsDependency`, `PaperGapClosureTests.C07_CoeffectGetSetPreconditionsAndReversibleProvision` | done (language adaptation) | C# keeps the paper's access mediation but uses a typed explicit API instead of a TS Proxy; no second access system is introduced. A declared-but-unsatisfied key throws `DependencyMissingException` unless it is read from the still-readable committed view during teardown (Theorem 70). |

## §5.2 Component Loader (p.64–69)

| Paper | Concept | C# implementation | Test evidence | Status | Semantic difference / acceptance |
| --- | --- | --- | --- | --- | --- |
| §5.2.1 p.65 | Entry fields `id/url/isolate/intercept/config/disabled`; configuration tree; keyed diff; field-level least-disturbance dispatch | `PluginManifest` entry fields, `EntryNode`, `ApplicationManifest.Entries`, `ApplicationLoader.EffectivePluginsAsync`, `EntryDiff`, `IConfigurableComponent`, `PluginRegistry.ApplyInPlaceAsync` | `EntryReconciliationTests` (intercept in-place, rehome, config opt-in, endpoint equivalence), `ConfigurationTreeTests` (Group keyed diff, Include graft, disable→fresh fiber) | done | `url` maps to package id@range plus `Source` payload path; `isolate` maps to host-owned `Realm` identities via `RehomeAsync`; `intercept` maps to a `MetaKey<string,string>` updated in place. Group/Include are loader-level configuration nodes, not a second Runtime or dependency container. |
| §5.2.1 p.66–67 | Managed realms: local vs shared realm, reassignment with delimiters, dependents that stay bound | `Runtime.RehomeAsync`, `HandleRehome`, `CompleteRehome`, `PluginRegistry.RealmFor` resolver in `PlatformApplication` | `EntryReconciliationTests.IsolateOnlyChangeRehomesProviderAndKeepsConsumerBound`, `RevisionSemanticsTests.*`, `PaperGapClosureTests.C34_RealmReassignmentIsARetireReinsertComposite` | done | CLR-side uses Realm reference identity and Core retire/reinsert; it does not reproduce the paper's delimiter tags literally but preserves the observable rule: dependents sharing the moved isolate stay bound, others recompute. |
| §5.2.2 p.67–68 | Phase 1 classification: stashed/externals, accepted/declined fixed point, cycles decline | `HmrEngine.Plan` | `HmrEngineTests.ClassificationAcceptsChangedModuleAndItsImporters`, `ImportCycleWithoutAnAcceptedModuleDefaultsToDeclined`, `ExternalAssemblyChangeRequiresRestartInsteadOfHotReload` | done | CLR module URLs are assembly identity/payload paths; imports are read in both directions because a changed assembly affects both its dependencies and its importers. Externals (Core, contracts) are restart boundaries. |
| §5.2.2 p.68 | Phase 2 stale-entry detection: transitive imports respecting declined boundary | `HmrEngine.Plan` dependency traversal | `HmrEngineTests.StaleDetectionRespectsDeclinedBoundary`, `SharedPrivateDependencyChangeInvalidatesAllConsumers` | done | No JS module cache walk; the host supplies the assembly graph. The semantic result — which entries are stale — is unchanged. |
| §5.2.2 p.69 | Phase 3 transactional reload: backup, invalidate, swap fibers, restore on any failure | `HmrEngine.ReloadAsync` submits ONE `PluginRegistry.ApplyAsync` batch; Core retire/reinsert and registry rollback are the only recovery authority | `HmrEngineTests.FailedSecondReloadRollsBackEveryStaleEntryToAConsistentState`, `ConcurrentHmrAndDeclarationChangeSerializeThroughTheRegistry`, `HmrManagedTests.HmrReloadReclaimsTheOldAssemblyLoadContext` | done | CLR "module cache backup" is the previous collectible ALC/payload retained by the old fiber until replacement commits. No GenericHmr state machine is used; ALC release is generation-fenced by `GenerationTracker`. |

## §5.3 Case Study: Koishi (p.69–70)

| Paper | Concept | C# implementation | Test evidence | Status | Semantic difference / acceptance |
| --- | --- | --- | --- | --- | --- |
| §5.3 p.69 | Host contributes only domain vocabulary; plugins compose effects/coeffects | `samples/KoishiShowcase` (`Contracts`, `Vocabulary`, `Database`, `Adapter`, `Feature`) as real `.gocordis-plugin` packages | `KoishiPackagedProductTests` builds/installs/declares all four packages and asserts `koishi/echo` returns `row:demo:ping`, produced by command → adapter → database over typed shared keys | done | The 4000+ community plugins are Koishi ecosystem scale evidence, not a C# target; the sample proves composition, not ecosystem size. |
| §5.3 p.69 | Independent browser console as a second Cordis application | `samples/KoishiShowcase/ConsoleApp/KoishiConsoleApp` with its own Runtime/Registry/UI contribution, typed `ConsoleClient` HTTP boundary | `KoishiPackagedProductTests` asserts `NotSame(app.Runtime, consoleApp.Runtime)`, its own page `/koishi`, and `/api/query/koishi/server` over HTTP | done | The browser/TS UI is represented by a .NET console app serving the same UI contract; WebUiServer serves the second app's pages, not the server Runtime's UI shell. |
| §5.3 p.70 | Provider replacement, withdrawal→Pending, reactivation, unrelated plugins unaffected | Same packaged sample, disabled via control plane | `KoishiPackagedProductTests` disables `koishi.database`, asserts adapter/feature Pending and vocabulary Active, re-enables and asserts all Active | done | Business database results are in-memory for the sample; the browser-side TS reconcile loop and plugin marketplace remain explicit non-claims. |
| §5.3 p.70 | Query/command registrations revert with their fiber | Feature installs the echo command and `/api/query/koishi/echo` through effect inverses; the console bridge installs its page and server query the same way | `KoishiPackagedProductTests` asserts the query disappears while the provider is withdrawn and returns after reactivation; the console has its own contribution registry | done | The sample does not emit or subscribe to the injected `Message` key; event registration reversibility is evidenced by Core/Extension tests, not claimed here. External process side effects remain subject to C35. |

## Paper invariants retained (do not re-open)

- **Effect inverse witness**: component author obligation, not machine-proved by Runtime.
- **Coeffect commutativity**: provider obligation over declared operations.
- **Context property Proxy**: C# uses a typed explicit access API; recorded language adaptation.
- **Fiber target identity**: provider Fiber/Activation, not value equality.
- **LIFO recovery**: all tracked inverses run strictly serially in reverse install order.
- **Reload/unload chaining**: inertial transition completion then re-evaluate target; tests cover nested reconcile and rehome.
- **Theorem 70 ordering**: dependents drain before provider inverses; `Thm70_Matrix` + `CleanupCanStillRequireItsDependency`.
- **Theorem 73 progress**: no deadlock from declaration coordination; `Thm73_Matrix`.
- **Theorem 80 endpoint**: incremental reconciliation quiesces at the fresh-load endpoint; `EntryReconciliationTests.IncrementalReconciliationReachesTheSameEndpointAsAFreshLoad`.
- **Group keyed diff**: stable Entry ids, not array positions; `ConfigurationTreeTests.GroupChildrenUseKeyedDiffAndChangingOneEntryLeavesOthersAlone`.
- **Managed realms**: isolate moves through Core Rehome; `IsolateOnlyChangeRehomesProviderAndKeepsConsumerBound`.
- **HMR rollback**: failures restore declarations/fibers/ALC generations; `FailedSecondReloadRollsBackEveryStaleEntryToAConsistentState`, `HmrReloadReclaimsTheOldAssemblyLoadContext`.
- **Koishi web console**: independent application, not a server UI shell; `KoishiPackagedProductTests`.

## Verification commands

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test Gocordis.sln --nologo --no-build -m:1
bash samples/KoishiShowcase/build-and-run.sh /tmp/gocordis-koishi-verify
```
