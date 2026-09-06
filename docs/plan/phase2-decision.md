# Phase 2 Decision — Declaration Authority & Panic Containment (ADR)

Status: Accepted (implemented)
Scope: Kernel (`runtime/`)

## 1. Declaration rule: UPPER BOUND (subset)

- An activation may `Require(key)` only if `key` is a member of its declared
  `Inject` set; may `Provide(key, value)` only if `key` is a member of its
  declared `Provide` set.
- The declared sets are **activation-local immutable copies** captured when the
  activation Context is created (from the Component declarations cached at
  Fiber creation).
- Declared-but-unexercised capabilities are allowed (subset rule): a declared
  surface is a *permission/commitment bound*, not a requirement to exercise
  every entry. Conditional/optional use stays legal.
- Duplicate declarations are tolerated (declaration is a set).
- Rationale: dependency satisfaction and withdrawal are driven by declared
  Inject edges + runtime provider registration; exact equality would forbid
  legitimate conditional use and buys no safety (an undeclared *access* is
  already rejected at the boundary).
- Errors: `ErrUndeclaredRequire`, `ErrUndeclaredProvide` (errors.Is-able).

## 2. Panic containment boundaries (Kernel-owned)

A panic at any of these boundaries is contained and converted to a lifecycle
error; the Fiber is never stranded in Loading/Unloading and no committed
Runtime-managed resource is skipped:

| Boundary | Sentinel | Behavior |
|---|---|---|
| `Component.Apply` | `ErrComponentApplyPanic` | committed effects unwind via normal failure path |
| `ctx.Effect` install | `ErrEffectInstallPanic` | Installing slot removed; no commit |
| effect inverse / Component Cleanup | `ErrInversePanic` | LIFO unwind continues past the panic |
| (existing) Config/Factory Create | config sentinel | unchanged (extension layer) |

## 3. Compatibility

- All existing honest declarations pass unchanged (full suite green after the
  change, before any test edits; new enforcement tests added).
- Only previously-undeclared `Require`/`Provide` accesses are newly rejected —
  that is the intended breaking change. Extensions were already conformant.
