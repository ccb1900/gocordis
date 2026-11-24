# Chapter 2 theory → C# runtime mapping

Paper §2 (PDF p.7–8) is theoretical background. It does **not** require a Monad,
Comonad, Graded Coeffect or algebraic-effect *language* in the C# port. What it
requires is an honest account of how the static notions become runtime
mechanisms in §3. This document is that account; it deliberately adds no new
Core abstraction.

| Paper §2 concept | What the paper says | C# runtime counterpart | Difference / boundary |
| --- | --- | --- | --- |
| §2.1 Effect system typing `Γ ⊢ t : T ! ε` | Effects are tracked in a typing judgement; handlers interpret operations | `Context.EffectAsync(install, inverse)` + `EffectSlot` + `EffectRegistration` + `Activation` inverse stack | Effect tracking is a runtime effect stack, not a static type/effect row. The inverse witness is supplied by the component author; Core orders and executes it but cannot prove arbitrary user inverses are correct. |
| Monadic sequencing of effects | Bind sequences computations and accumulates effect structure | `IIterativeComponent.IterateAsync` yields one witnessed step at a time; `Runtime.StepsAsync` returns each step to the single decision loop; in-flight steps land before any divert (C32) | Iterators are the operational analogue of monadic sequencing. There is no `Monad<T>` type or compiler plugin. |
| Algebraic operations / handlers | Operations are handled by an effect handler | `Context.EffectAsync` install/inverse delegates are the handler pair; failure is attributed to `(fiber, activation, slot, label)` via `EffectFailureRecord` | No algebraic effect grammar or dynamic handler search; the context owns one explicit inverse per installed step. |
| §2.2 Coeffect context `Σ`, `get/set` preconditions | A coeffect describes what a computation may read from its environment; `get` requires a binding, `set` requires ownership | `Key<T>` / `EventKey<T>` reference identity, `Component.Inject()/Provide()`, `Context.Require/Provide`, `Fiber.CapturedInjectKeys/CapturedProvideKeys` | Strongly typed keys replace symbolic names. Undeclared access throws `UndeclaredCapabilityException`; declared-but-unsatisfied stays `Pending` until a provider commits. |
| Comonadic / graded coeffect | Context is consumed with a structure/capability grade | `Realm` per-key routing, `Meta<T,M>` monoid with right-biased override, versioned capability ranges (`VersionedKey<T>` + NuGet ranges) | No static comonad/grade type system. Grading is expressed by runtime declaration + realm + metadata, not by a type checker. |
| §2.3 Effect ↔ Coeffect duality | Effects modify the environment; coeffects constrain what may be read; static systems cannot capture runtime changes | One `Runtime` command domain reconciles both: effects are tracked inverses, coeffects are typed requirement gates, and `Reconcile` reacts to declaration changes (dependency loss → Pending, recovery → re-activate) | This is the paper's central adaptation: static theory becomes a single runtime authority. It is NOT equivalent to a DI container: DI wires once, here bindings withdraw and re-bind reactively. |

## Evidence

- Effect side: `CoreSemanticsTests.EffectsUnwindInStrictLifoOrder`,
  `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`,
  `InverseFailureTests`, `C32_InertialIteratorLandsCancellableStepBeforeItsInverse`.
- Coeffect side: `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`,
  `UndeclaredProvideFailsTheFiber`, `DependencyLossMovesConsumerToPendingAndRestoresIt`,
  `ContextUsesCapturedDeclarationsWhenTheComponentMutatesAfterLoad`,
  `PaperGapClosureTests.C07_CoeffectGetSetPreconditionsAndReversibleProvision`.
- Duality / single authority: `PaperGapClosureTests.C01_TemporalRecoveryAndSpatialResolutionAreOneContract`,
  `PlatformConvergenceTests.PC12_PluginRegistryNeverCreatesASecondLifecycle`.

## What is deliberately absent

- No Monad/Comonad/Graded-Coeffect library (paper §2 is background, §7 explicitly
  positions related systems; the port adopts the runtime mechanism).
- No TypeScript `Proxy`-style `ctx[key]`; §5.1.4's reflective/property API is
  adapted to explicit typed `Context` methods.
- No claim that static type checking provides the paper's guarantee. Declaration
  enforcement happens at fiber creation (captured declarations) and at every
  `Context.Require/Provide` guard.
