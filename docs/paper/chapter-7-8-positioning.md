# Chapters 7–8: related-work positioning and honest conclusions

Paper §7 (PDF p.77–82) and §8 (p.82) are not implementation requirements. This
document records the project's position so that chapter claims are not inferred
from a class name or a coverage count.

## §7.1 Effect and Coeffect systems

The paper relates its runtime context to static effect systems, algebraic
effects and coeffect/comonadic systems. Gocordis implements the **runtime**
counterpart: an inverse-carrying effect stack and typed declared coeffects
(`Context.EffectAsync`, `EffectSlot`, `Key<T>`, `Inject/Provide`). It does not
implement ZIO/Effekt/Effect-TS-style static effect tracking, and does not claim
their type-level guarantees. See `docs/paper/chapter-2-theory-mapping.md`.

## §7.2 Programming paradigms

Comparison points are DI, RAII/`IDisposable`, module systems, Effect Handlers and
Actor-like isolation. The C# distinction is:

- DI wires dependencies once; Gocordis re-resolves reactively on provider
  withdrawal/recovery, and moves consumers to `Pending` in between.
- `IDisposable` has no runtime-owned inverse ordering across a component's whole
  effect stack; `EffectSlot` inverses run strict serial LIFO under Core authority.
- ALC is a collectible load boundary, not an Actor mailbox nor a security sandbox.

## §7.3 Temporal composability

The paper's temporal claim is scoped to context-mediated effects. C# realizes
that scope: `C35_ReversibleBoundaryCoversOwnedEffectsButNotExternalEmission`
asserts that externally emitted side effects are NOT reverted. No claim of
automatic rollback for arbitrary native/file/network side effects.

## §7.4 Spatial composability

The paper's spatial claim is reactive dependency resolution. C# evidence:
`DependencyLossMovesConsumerToPendingAndRestoresIt`, `Thm70_Matrix`,
`VersionedKey<T>` ranges, realm isolation. No claim of an OSGi-style remote
module marketplace or a full Koishi plugin ecosystem.

## §8 Conclusion — claims we do NOT make

- Not a formal proof of Thm64/68/70/73/80; only executable bounded evidence,
  and Thm73's S(n) ≤ (K+3)(V(n)+1) upper bound is explicitly unproved.
- Not arbitrary side-effect reversibility: only effects registered through
  `Context` are tracked; inverse witness correctness is the author's contract.
- Not a full HMR for arbitrary CLR modules: shared-contract and Core upgrades are
  restart boundaries (`HmrPlan.RestartRequired`).
- Not a malicious-plugin sandbox: Managed ALC is trusted in-process; Process has
  OS launch policy but no credential drop; WASM engine limits + import allowlist
  are engine-level, not a complete security solution.
- Not a Koishi-scale ecosystem; the packaged case study proves composition and
  reversibility, not ecosystem size.

The maintained chapter matrix is `docs/paper/chapter-coverage-matrix.md`; the
semantic-item test ledger remains `docs/migration/paper-coverage-matrix.md`.
