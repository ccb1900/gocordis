# Chapter 6 Engineering Boundaries — acceptance ledger

Companion to `docs/gocordis-paper-chapter-coverage-matrix.md` P4. Chapter 6 is a
discussion chapter: items below are boundaries and engineering acceptances, not
claims that every open research direction was implemented.

| Item | Acceptance | Evidence | Boundary |
| --- | --- | --- | --- |
| §6.2 Broker multi-implementation | Named registration, reversible withdrawal, policy selection | `BrokerObserveGapTests.BrokerNamedProvidersSupportSelectionAndReversibleRegistration`, `PaperGapClosureTests` C17 (real Core fibers) | Broker stays a library pattern; no second registry. |
| §6.2 Rolling upgrade | Member swap under concurrent calls; in-flight call drains before resource release; failed upgrade leaves routes unchanged | `RollingUpgradeBoundaryTests.BrokerMemberSwapKeepsConcurrentConsumersTransparent`, `EndpointWithdrawalDrainsAnInFlightCallBeforeReleasingTheResource`, `FailedBrokerUpgradeLeavesThePreviousRouteSetUntouched`; product `MixedBackendProductE2ETests` | Draining is provided by `DelegatingCapabilityEndpoint`'s linearization point, not by sleeping. |
| §6.2 Cross-process invocation | Real Process backend RPC, method allowlist, crash/error vocabulary | `ProcessRuntimeTests`, `CrossBackendCapabilityTests`, `BackendDispatchProductTests` | Only declared capabilities cross the backend boundary. |
| §6.3 Access control / isolation | Explicit threat model per boundary | `docs/current-status.md` “§6.3 threat model”; `WasmImportAllowlistTests`, `ProcessPermissionPolicyTests`, `ProcessRuntimeTests`, `SharedAssemblyRegistryTests`, `HmrEngineTests` (restart boundary) | Managed ALC is trusted in-process; Process has address-space isolation + RPC allowlist + OS launch policy (absolute/PATH-resolved path/no shell/env scrubbing) but NO credential drop; WASM has engine limits + declared-capability import allowlist. None is called a full sandbox. |
| §6.4 Language independence | Real Managed / Process / WASM declaration, activation, undo and error propagation | `BackendLifecycleTests.Matrix_*` (three backends), `ManagedRealComponentErrorPropagatesToItsFiberAndUnwindsItsCapability`, `MixedBackendProductE2ETests` | C39 only proves the factory contract; equivalence is claimed for the tested quadruple, not for arbitrary plugins. |
| §6.5 Dependency graphs | Package, assembly/contract and capability graphs stay distinct | `DependencyGraphSeparationTests`; `PluginDependencyPlanner` tests; `SharedAssemblyRegistryTests`; `TransitiveContractProductTests` | A package cycle does not feed the runtime capability cycle detector. Assembly references are a compile/ALC concern and are NOT part of any runtime cycle detector. |
| §6.6 Typing and versioning | Host-sealed contract identity; NuGet ranges; conflicting copies rejected; restart required on contract upgrade | `SharedAssemblyRegistryTests`, `VersioningTests`, `ControlBoundaryTests`, `HmrEngineTests` | C38 remains an accepted difference from the paper's `K×P` / peer-dependency candidate; not claimed as a full port. |

## Not claimed

- No Monad/Comonad or static Effect/Coeffect type system is added for chapters 2/7.
- No TypeScript Proxy is reproduced for §5.1.4; typed `Key<T>` + declaration guards are the accepted adaptation.
- No full Koishi ecosystem/marketplace, no formal theorem proof, no恶意插件安全沙箱.
