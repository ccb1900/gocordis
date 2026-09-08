> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# UI-01 Design Note — Developer Console Observation Model v0.1

Status: DESIGN (no production code in this note)
Reference: "Dynamic Composable Runtime — Developer Console Specification v0.1" §40–§44
Scope: UI-01 only (Observation Model). No UI, no `Snapshot()` producer, no event stream, no kernel change.
Audit date: 2026-09-07, commit `16877c9` (HEAD at audit time)

## 0. TL;DR

1. **Audit result**: the Runtime has **no public Observation API**. The only
   semantic projection that exists is test-only (`observe()` in
   `runtime/obs_test.go`), which is quiescence-gated and value-less.
2. **Expressiveness**: the kernel state can express the paper-level semantic
   view for Fibers / Providers / Dependencies / Effects(counts) / Contexts
   (realm tree) directly. The model below is frozen as the UI-01 contract.
3. **Four kernel additions are required before the model can be *fully*
   produced by a Snapshot API** (UI-02), and each is a decision for the
   architect: stable Scope/Realm identity (§5.2), per-effect Type/Key metadata
   (§5.3), Component identity beyond the name (§5.1), and activation/scope
   timestamps (§5.4). None blocks UI-01 itself, because the model only
   declares what *must* be expressible.
4. Consistent-snapshot semantics and the event taxonomy are designed here as
   UI-02 / UI-03 inputs (§5.5, §6), not implemented.

---

## 1. Audit — existing observation surface

### 1.1 Public API (observation-related): none

- No `RuntimeSnapshot`, `RuntimeEvent`, `Observer`, `Subscribe`, or `Snapshot`
  exists anywhere in `runtime/` (searched all non-test files).
- Today a console could only poll single fibers:
  `Fiber.State()` (`runtime/fiber.go:84`), `Fiber.Err()` (`runtime/fiber.go:91`),
  `Fiber.ID()/Name()/Component()` (`runtime/fiber.go:75-81`), plus
  `Runtime.Load` / `Close`. There is no whole-runtime view.
- `Runtime.stateSnapshot()` is unexported (`runtime/runtime.go:192`).
- The Runtime has **no RuntimeID** (multi-instance switching per spec §27 is
  unrepresentable today).

### 1.2 Internal semantic observation (test-only)

- `observe()` (`runtime/obs_test.go`) is a canonical, sorted, value-less
  semantic projection (fibers / providers / dependencies / effect counts /
  children). It is used by the Phase A/B/C theorem harness with a
  **quiescence precondition** ("no in-flight lifecycle transition") and is not
  exported. This proves the kernel internals can express a consistent
  paper-level view; it is not a production API.

### 1.3 Extension precedent (Snapshot/Subscribe style)

- `extensions/registry` establishes the repo's observer pattern:
  `Snapshot()` at a linearization point (`extensions/registry/registry.go:143`),
  `Subscribe() → (*Subscription[T], Snapshot[T])` with snapshot-at-subscribe
  (`extensions/registry/registry.go:162`), bounded non-durable per-subscription
  buffers. UI-03's event stream should reuse this semantic contract, not invent
  a new one.

---

## 2. Kernel state inventory (the expressiveness source)

| Entity | Internal representation | Evidence | Model-expressible today? |
|---|---|---|---|
| Fiber | `state`, `intent`, `activation`, `err`, `parent`, `children`, `realm` | `runtime/fiber.go:19` | Yes (State/Intent/Activation/Parent/Children/Err) |
| Activation | `id`, `ctx`, `deps []DependencySnapshot`, `applyErr` | `runtime/activation.go:11` | Yes (generation id, dependency snapshot, apply error) |
| Context | `fiberID`, `activationID`, `state`, `effects`, `realm` | `runtime/context.go:48` | Yes (per-activation scope + effect list) |
| Effect slot | `seq`, `state`, `inverse`; states installing/committed/undoing/undone | `runtime/context.go:31`, `runtime/context.go:20-26` | **Partial** — state yes; Type/Key no (no metadata) |
| Realm (scope) | `parent`, `own map`, `intercept`; **no stable id** | `runtime/providers.go:18` | **Partial** — tree yes; stable identity no |
| Provider record | `key`, `identity`, `value`, `retiring`; realm is not stored on the record (owned realm == owning fiber's realm, ADR §2) | `runtime/providers.go:155` | Yes (key + ProviderIdentity + retiring; scope derivable from owner fiber) |
| ProviderIdentity | `FiberID` + `ActivationID` | `runtime/dependency.go:8` | Yes |
| Dependency snapshot | per activation: `Key` → `ProviderIdentity`, captured at Loading | `runtime/dependency.go:17` | Yes |
| Dependency graph | identity-resolved consumer edges (orchestrator-owned) | `runtime/dependency_graph.go` | Yes (snapshot-time re-resolution for status) |
| Timestamps | none anywhere (no created/active/event time) | fiber/activation structs | No — §5.4 |
| Runtime ID | none | `runtime/runtime.go:13` | No — §5.6 |

Orchestrator note (matters for §5.5): all lifecycle *decisions* are serialized
by one orchestrator goroutine (`runtime/command.go`), and fiber state/intent
publications happen there; but **provider records are written on the Apply
goroutine** (`Provide` → `provideCap` → `realm.registerOwn`,
`runtime/context.go`, `runtime/providers.go:96`) and removed on the unwind
goroutine (`removeOwn`, `runtime/providers.go:112`). Realm and effect-slot
mutations therefore happen off the orchestrator under their own locks.

---

## 3. Observation Model (UI-01 frozen types)

Frozen for UI-01. Field names follow the spec; comments mark fields that
cannot be produced until a §5 kernel decision lands. All types are value
types; snapshots are cloned and sorted into canonical order (UI-02
requirement for §43 determinism, mirroring `observe()` sorting).

```go
// RuntimeID identifies one Runtime instance (§27). Produced by UI-02 once §5.6
// (RuntimeID source) is decided; today the Runtime has no identity.
type RuntimeID string

type FiberStateView = string // e.g. "Pending","Loading","Active","Unloading","Failed","Gone" (reuses runtime.FiberState.String)

// FiberSnapshot is one Fiber + its current activation generation.
// UI must keep Component / Fiber / Activation distinct (§6, §7): one Fiber is
// one Component instance; each reload creates a NEW ActivationID.
type FiberSnapshot struct {
    ID             string   // FiberID.String()
    Component      string   // component.Name(); multi-instance identity needs §5.1
    Name           string   // same as Component today (diagnostics only)
    State          string   // runtime.FiberState
    Intent         string   // runtime.Intent ("Mounted"/"Unmounted")
    Err            string   // lifecycle error, if any (diagnostics)
    ActivationID   string   // "" when no live activation (Pending/Gone/Failed-without-activation)
    ParentFiberID  string   // "" for root fibers
    ChildFiberIDs  []string
    Dependencies   []DependencyView // declared+resolved view (§13)
    Providers      []ProviderView   // capabilities this activation provides (§8)
    Effects        []EffectView     // per-slot list (§14)
    ScopeID        string           // owning realm identity — needs §5.2
    // CreatedAt / ActiveSince: needs §5.4 (or derived from UI-03 events)
}

// ProviderView preserves Provider Identity and scope — never merges sibling
// scopes with the same key (§8, §16).
type ProviderView struct {
    Key              string // CapabilityKey.String()
    OwnerFiberID     string
    OwnerActivationID string
    State            string // "Active" | "Retiring" | "Withdrawn" — derived from
                            // owner Fiber state + record.retiring at snapshot time
    ScopeID          string // needs §5.2
}

// DependencyView is one required capability of a Fiber activation.
type DependencyView struct {
    Key                string
    ProviderFiberID    string // empty while Waiting
    ProviderActivationID string
    Status             string // "Satisfied" | "Waiting" | "Withdrawn"
    Reason             string // diagnostics (§22): "Provider unavailable", apply error, ...
}

// EffectView is one effect slot of one activation (§14).
type EffectView struct {
    OwnerFiberID     string
    ActivationID     string
    Seq              uint64
    Type             string // "Provider"|"Cleanup"|"Custom" — needs §5.3 kernel metadata
    Key              string // capability key for Provider effects; else ""
    State            string // "Installing"|"Committed"|"Undoing"|"Undone"
}

// ScopeSnapshot mirrors the realm tree (§15, §16). One row per realm.
type ScopeSnapshot struct {
    ScopeID          string // needs §5.2; root = "scope:0"
    ParentScopeID    string // "" at root
    ProviderBindings []ProviderView // this scope's own map (exclusive per key)
    FiberIDs         []string       // fibers whose realm == this scope
}

// RuntimeSnapshot is the immutable, self-contained UI source of truth (§5).
type RuntimeSnapshot struct {
    RuntimeID    string // needs §5.6
    State        string // runtime.RuntimeState
    Fibers       []FiberSnapshot
    Scopes       []ScopeSnapshot
    Providers    []ProviderView // identity-level, flattened + per-scope bindings
    Dependencies []DependencyView
    Effects      []EffectView
}

// RuntimeEvent is the immutable timeline fact (§20, §21).
type RuntimeEvent struct {
    Sequence     uint64
    Timestamp    time.Time
    Type         EventType
    FiberID      string
    ActivationID string
    Data         any // typed payload, never raw internal state
}

type EventType string
```

Notes:
- The spec's top-level `Providers / Dependencies / Effects` slices (§5) and the
  per-fiber views (§13/§14) are the *same* identity-level rows; the model keeps
  one canonical row set plus per-fiber index views so the UI never merges
  sibling-scope providers (§16).
- Provider **values are never exported**; only identity + state + retiring.
  Value display is Application/Tooling layer, not console (§0/§2).

---

## 4. Expressiveness verification matrix (UI-01 gate)

| Spec requirement | Source in kernel | Model field | Verdict |
|---|---|---|---|
| Fiber list, state | `fiber.state` | FiberSnapshot.State | Direct |
| Intent | `fiber.intent` | FiberSnapshot.Intent | Direct |
| Activation generation (reload → new #) | `activation.id` per activation | FiberSnapshot.ActivationID | Direct |
| Provider key + identity + retiring | `providerRecord{key, identity, retiring}` | ProviderView | Direct |
| Provider "State" | owner `fiber.state` + `retiring` | ProviderView.State | Direct (derived) |
| Dependency edge: consumer → provider identity | `activation.deps` + graph | DependencyView | Direct |
| Edge status Valid / Withdrawn | snapshot-time `resolveDependency` | DependencyView.Status | Direct (UI-02) |
| Waiting reason | Pending fiber + `fiber.inject` fails resolution | DependencyView.Status/Reason | Direct |
| Effect lifecycle states | `effectSlot.state` (4 states) | EffectView.State | Direct |
| Effect Type / Key | **not stored** | EffectView.Type/Key | **Needs §5.3** |
| Scope boundary + sibling isolation | realm tree, own maps | ScopeSnapshot + per-scope bindings | **Needs §5.2** (stable ScopeID) |
| Component vs Fiber vs Activation | component only has `Name()` | FiberSnapshot.Component | **Partial — §5.1** |
| Context / Scope hierarchy | realm `parent` chain | ScopeSnapshot.ParentScopeID | Needs §5.2 |
| Timeline events | no event producer in kernel | RuntimeEvent | UI-03 (taxonomy §6) |
| Created At / Active Since | **no timestamps** | (none) | **Needs §5.4** or UI-03 event times |
| Config desired/effective/inherited | extension `config`, not kernel | (none in kernel snapshot) | Out of kernel scope — console later reads extensions/config |
| Runtime multi-instance | **no RuntimeID** | RuntimeSnapshot.RuntimeID | **Needs §5.6** |

---

## 5. Decisions required before UI-02 implementation

### 5.1 Component identity
The kernel does not register Component definitions (F-03 in the functional
completeness spec). A Fiber is a Component instance; two fibers of the same
component share only `Name()`. Recommendation for v0.1: treat Component as a
display label and Fiber as the row identity; never key UI state on Component
name. Upgrade to a definition registry when F-03 lands.

### 5.2 Stable Scope/Realm ID
`realm` has no id. Recommendation: assign a monotonically increasing `ScopeID`
at realm creation (root = 0) stored on the realm struct (internal, additive,
non-breaking). Snapshot rows and §16 sibling isolation become exact.

### 5.3 Effect Type/Key metadata
`effectSlot` stores only `seq/state/inverse`. Recommendation: add
`kind` + optional `key` at registration — `Provide`/`Intercept` auto-tag
(`Provider`/`Interceptor` + key); a Component `Cleanup` return is tagged
`Cleanup`; generic `Effect` is tagged `Custom`. This is the only way §14's
per-effect Type/Key view is truthful without UI-side inference (§35 forbids
UI inference).

### 5.4 Timestamps
Kernel records no times. Two options: (a) record activation-start/active time
on `activation` (kernel addition), or (b) rely on UI-03 event timestamps only.
Recommendation: (b) first — events give "Active Since / Created" derived from
the first matching event — to keep the kernel lean; revisit (a) only if the UI
needs per-activation times older than the event buffer.

### 5.5 Snapshot consistency semantics (UI-02)
A live runtime is never globally quiescent (Apply/Unwind run on goroutines and
write realms/effect slots off the orchestrator). Two candidate semantics:
- (a) Orchestrator-command snapshot: submit a snapshot command; the
  orchestrator reads all orchestrator-published state at its command boundary,
  and realm/effect-slot reads take their own locks. Result: linearizable at
  the command position, but may show a provider record mid-withdrawal next to
  a still-Active consumer (transitional skew).
- (b) Quiescence-gated snapshot: requires no Loading/Unloading fiber and empty
  command queue (the theorem harness's `semanticQuiescent`), so the view is
  exactly the paper-level semantic view; may block behind a long Apply.
Recommendation: (a) for the console with the residual skew documented, because
the UI is event-driven (§37) and Snapshot is only a reconnect/correction
anchor (§38); the paper-level *semantic* equivalence (Phase A style) remains a
theorem-harness concern, not a live-console guarantee.

### 5.6 RuntimeID source
Recommendation: `runtime.New` accepts an optional `WithRuntimeID(string)`
option; default `runtime:<n>` assigned per instance. Snapshot and every event
carry it (§27).

---

## 6. UI-03 event taxonomy (design-only)

Each event type below maps to an existing kernel decision/write point so the
event producer (UI-03) stays a pure observer of transitions that already
happen — never a new semantic layer (§35, §40.2).

| EventType | Trigger site (today) |
|---|---|
| `FiberCreated` | `newFiber` on Load / `cmdSpawnChild` (`runtime/ownership.go`) |
| `ActivationStarted` / Loading | `startActivation` (orchestrator) |
| `DependencyCaptured` | `captureDependencies` at Loading |
| `ProviderPublished` | `realm.registerOwn` (`runtime/providers.go:96`) during Apply |
| `Active` | `cmdApplyDone` → `transition(StateActive)` |
| `UnwindRequested` | `beginWithdrawal` (`runtime/reconcile.go:103`) |
| `Unloading` | `unwindAfterApply` / `maybeStartUnload` |
| `EffectCommitted` / `EffectUndoing` / `EffectUndone` | `Context.Effect`, `runInverses` (`runtime/context.go`) |
| `ProviderWithdrawn` | `markRetiringOwn` / `removeOwn` (`runtime/providers.go:112`) |
| `ActivationEnded` / `Gone` / `Failed` | `finishActivation` / `finalizeActivation` (orchestrator) |
| `Failure` (Apply/Cleanup) | `cmdApplyDone.err` / `cmdUnwindDone.err` |

Contract (UI-03): monotonic `Sequence` assigned by the producer; append-only,
immutable; bounded non-durable per-subscription buffers (registry precedent);
`Subscribe` returns the snapshot at its linearization point so reconnect is
Snapshot → resume (§38) with no gap-guessing.

---

## 7. Boundary audit

- UI-01 adds **no code**. No kernel, no runtime public API, no extension.
- The frozen model only *declares* what the console needs to express; §5 lists
  the kernel additions that UI-02's producer will require, each gated on
  architect decision.
- Everything stays behind the §36 boundary: console → Runtime Public API;
  kernel internal state (`runtime.fibers/providers/graph/orchestrator`) is
  never exposed raw; provider values and configs are not part of the kernel
  snapshot.
