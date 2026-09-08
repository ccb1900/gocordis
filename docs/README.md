# docs — Authority Model (2026-09-08)

## 唯一规范来源 (Sole Normative Source)

**论文原文**: [`论文.pdf`](./论文.pdf)
*A Programming Paradigm for Spatiotemporal Composability* — Yifan Shi, Wei Zhang, Tianyi Cui (PKU / DeepSeek-AI).

本仓库的目标是实现这篇论文。一切语义争议以论文原文裁决;
`stc-go` 与 JS Cordis 仅是历史参考,**不再是规范来源,也不再被引用**。

## 权威层级

```text
论文原文 (docs/论文.pdf)  >  本仓库 ADR (docs/adr/)  >  实现与测试
```

- 每项语义必须能锚定到论文的 Definition / Theorem / Section 编号;
- 论文未规定而实现所必需的操作规则,必须在 ADR 中显式声明为 operational refinement;
- 对照表与已知偏差见 [`ROADMAP.md`](./ROADMAP.md) §1。

## 已归档文档

`docs/` 下 2026-09-08 之前的全部规格/计划/评审文档(38 份,含 `plan/`、`review/`、`specs/`
子目录)均已标注 **ARCHIVED**,仅作历史参考,不再被引用。其内容(尤其定理编号 T59/T61/
T63/T66/T73、isolation/intercept 的映射表述)已被论文原文复核修正,以
[`ROADMAP.md`](./ROADMAP.md) 为准。
