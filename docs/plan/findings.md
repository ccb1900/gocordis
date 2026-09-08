> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Findings & Decisions

## Requirements

- Produce an executable, end-to-end improvement plan for the current Go implementation.
- Treat [the paper](https://arxiv.org/pdf/2608.25512) as the normative source; use `stc-go` and Cordis only as references.

## Research findings

- The paper's core model joins reversible effects and reactive coeffects in one Context. Its implementation mapping includes `effect`, `get/set`, `isolate`, `intercept`, fiber inject/provide, lifecycle, loader, and HMR.
- The paper's five useful implementation-level claims are preservation, temporal composability/recovery exactness, spatial composability/ordering, progress under finite/acyclic/cooperative assumptions, and conditional confluence.
- The current kernel has fresh activation Contexts, strict LIFO managed-effect unwinding, provider generations, dependency snapshots, consumer-first provider withdrawal, and a serialized orchestrator.
- Current gaps: Require/Provide do not enforce declaration membership; panic in Apply or inverse is not contained; provider resolution is Runtime-global/exclusive; isolation and interception do not exist; cycles are not rejected; confluence coverage is only a small fixed schedule test.

## Technical decisions

| Decision | Rationale |
|---|---|
| Build/test reproducibility is phase 1. | No correctness claim is credible while the test command cannot compile. |
| Declaration enforcement precedes scoped contexts. | It closes a concrete stale-dependency hole without waiting for the larger data-model change. |
| Context scoping is architectural, not an extension. | It is required by the paper's unification of effect/coeffect contexts. |
| Tests must state theorem assumptions. | Arbitrary Go callbacks can block, mutate unmanaged state, or form cycles; the paper does not eliminate those host-language limits. |

## Resources

- Paper: https://arxiv.org/pdf/2608.25512
- Go reference implementation: https://github.com/0xdenny218/stc-go
- TypeScript reference implementation: https://github.com/cordiverse/cordis
- Current core: `runtime/`
- Current extension layer: `extensions/`

## Issues encountered

| Issue | Resolution |
|---|---|
| Workspace has no usable Git worktree metadata. | Plan uses direct file/test evidence rather than a branch diff. |
| `go test -race ./...` fails before tests due to toolchain/cache version mismatch. | Make toolchain/cache bootstrap a release-blocking first phase. |
