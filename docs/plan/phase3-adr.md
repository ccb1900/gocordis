# Phase 3 ADR — Scoped Context (realms / isolation / interception)

Status: REVISED — APPROVED DIRECTION (implementation in progress)
Scope: Kernel (`runtime/`) — breaking change to provider resolution internals; public API additive
References: paper Context Paradigm; Framework spec §4; runtime typed keys, provider generation, consumer-first withdrawal; Phase 2 declaration authority

## 0. Revision record (architect feedback folded in)

1. Realm belongs to an **explicit scope / ownership domain**, NOT one-per-activation.
   `Runtime.Load` defaults to the Runtime **root realm**; `ctx.Child` defaults to
   inheriting the parent's scope; only an explicitly derived scope creates a
   child realm. Root fibers therefore resolve each other's providers exactly as
   today (real root realm, not a per-activation artifact).
2. Child realms **may shadow ancestor bindings**; a child realm may provide a key
   that exists in an ancestor. "Exclusive" constrains only the **own map of one
   realm**. The earlier “child cannot provide key present in parent path” clause
   is removed (it contradicted override/recovery).
3. Go API fix: generic **methods** are not legal. Interception becomes a generic
   free function `runtime.Intercept(ctx, key, fn)` (internal non-generic chain +
   runtime type check; typed wrapper enforces T at compile time).
4. Per-realm exclusivity agreed, with dependency edges built from the
   **actually resolved provider identity**, never key-only.

## 1. Model

### 1.1 Realm

```text
Realm {
  parent  *Realm                    // nil at Runtime root
  own     map[CapabilityKey]*providerRecord  // exclusive within THIS realm's own map
  scopes  (explicit derivation bookkeeping)
}
```

- The Runtime owns the root realm. It lives for the Runtime's lifetime.
- A realm is created only by **explicit scope derivation** (below).
- Resolution (read): own map first, then parent chain (`lookup(realm,key)`):
  a child realm **shadows** an ancestor binding with its own record.
- Exclusivity: at most one provider per typed key inside one realm’s own map.
  Sibling realms may provide the same key in parallel and are invisible to each
  other.

### 1.2 Scope assignment (defaults)

| Case | Realm used |
|---|---|
| `Runtime.Load(component)` (root fiber) | Runtime root realm |
| `ctx.Child(component)` (no explicit scope) | parent fiber’s realm (inheritance; no new realm) |
| `ctx.Child(component, WithScope(...))` or explicit `ctx.Derive(...)` | new child realm, `parent = current realm` |

Consequence: with no explicit scoping the whole composition behaves exactly like
today’s single-realm Runtime (backward compatible). Only explicit scope
derivation introduces sibling isolation.

### 1.3 Provider registration & identity

- Every provider record keeps `ProviderIdentity{FiberID, ActivationID}` PLUS its
  realm (the realm its owning fiber resolves at activation time).
- `Provide(ctx, key, v)` writes into the realm owned/resolved by this fiber and
  is reversible via the existing Effect stack.
- Reactivation of the same fiber registers a fresh identity in the same realm
  (old record already removed by the previous unwind).

## 2. Dependency edges by resolved identity

- A consumer activation’s declared Inject edges are resolved **through its own
  realm path** at activation start.
- The orchestrator records, per dependency, the **resolved provider identity**
  (realm + fiber + activation), and builds the graph edge to that specific
  provider — never key-only.
- Withdrawal/recovery: when that provider retires, dependents whose edge points
  to it withdraw (consumer-first). A provider with the same key in a different
  sibling realm does not satisfy and does not notify this consumer.
- Exclusive-provider duplicate semantics: registering into a realm where the
  key is already in that realm’s own map fails with `ErrDuplicateProvider`
  (same as today when no scoping is used).

## 3. Paper mapping & public API

| Paper | Go v0.1 |
|---|---|
| get(k) | `runtime.Require(ctx, key)` — resolves through the activation realm path |
| set(k,v) | `runtime.Provide(ctx, key, v)` — registers in the activation realm (Effect-owned) |
| isolate | explicit scope derivation → child realm (`ctx.Derive` / `ctx.Child` with scope option) |
| intercept(k, f) | `runtime.Intercept(ctx, key, fn)` — read-time transform chain on `Require` |

```go
// New public API (illustrative; names may shift during review)
type ScopeOption func(*scopeOpts)

// Explicitly derived child scope: fresh child realm with parent = current realm.
// Default ctx.Child inherits (no new realm); WithScope(...) creates one.
func (c *Context) Derive(opts ...ScopeOption) (*Scope, error)

func (c *Context) Child(component Component, opts ...ScopeOption) (*Fiber, error)

type Interceptor[T any] func(value T, key Key[T]) (T, error)
func Intercept[T any](ctx *Context, key Key[T], fn Interceptor[T]) error
```

`Intercept` is a **generic free function** (Go methods cannot be generic). Its
internal machinery is a non-generic interceptor chain with runtime type checks;
the typed wrapper guarantees `T` at compile time.

## 4. Interception contract (read-time only)

- Interceptors attach to a scope/realm for a typed key; install is reversible
  via `ctx.Effect` (unwound in reverse install order).
- Read path: `Require` resolves the provider record through the realm path, then
  applies the chain for that key from ancestor scope to this scope; the final
  value is returned.
- Order: ancestor → descendant; within one scope, install order.
- Interceptors may only transform the returned value / reject a read; they can
  never mutate the provider registry, bypass declaration/realm checks, or change
  provider identity.
- Panics inside an interceptor go through the Phase 2 containment boundary
  (`ErrInversePanic`-style read error), never crash the process.

## 5. Visibility & recovery

- Child realm shadows ancestor key → removal of the child realm (scope owner
  activation ends) restores the ancestor binding exactly; unrelated consumers
  (in sibling/ancestor realms that never resolved through this child) are not
  reloaded.
- Siblings: invisible to each other; no shared state.
- Declarations (Phase 2) remain per-activation: Provide must be declared and
  targets the activation’s own realm; Require must be declared and resolves
  through the realm path.

## 6. Compatibility & migration

- Public existing calls (`runtime.Provide/Require/Child`, typed keys) unchanged
  in signature; behavior identical when no explicit scope is used (root realm =
  today’s whole-Runtime single realm).
- Internal global provider registry is replaced by realm resolution; extensions
  require no source change and are revalidated in Phase 6.

## 7. Tests (Phase 3 acceptance)

1. Two sibling explicit scopes provide the same logical key independently, both
   Active, mutually invisible.
2. Nested override/recovery: removing one subtree restores exactly the parent
   view and does not reload unrelated consumers.
3. Dependency edges are identity-resolved: a sibling same-key provider neither
   satisfies nor notifies the consumer.
4. Interceptor composition/order deterministic; unwinding removes them; read
   errors and panics are contained.
5. No explicit scoping ⇒ byte-for-byte today’s semantics (full existing suite
   green).
6. Race/vet/test green; architecture audit: single Fiber lifecycle authority.
