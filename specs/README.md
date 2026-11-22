# C# 可执行规格目录

本目录是 `docs/migration/semantic-mapping.md` 中论文条目（C01–C40）的**可执行规格占位目录**。
目前实际规格测试位于 `tests/Gocordis.Conformance.Tests/`，尚未镜像到本目录；
批次 C 已建立两份可证伪索引，本目录不再宣称"尚无对照"：

- `docs/migration/paper-coverage-matrix.md`：C01–C40 逐项锚点、C# 测试 file:line、状态
  （14 已覆盖 / 24 待补 / 2 接受差异），由 `PaperItemMatrixTests` 逐行校验。
- `docs/migration/theorem-matrix.md`：Thm64/68/70/73/80 的 Go→C# 场景映射（每定理 3 场景）。

语义条目仍以 `docs/migration/semantic-mapping.md` 与论文原文为唯一权威。
后续每迁移一个 Core 模块，在这里加入对应的规格测试实现，并在
`docs/migration/migration-coverage.md` 中把状态推进为 `已实现` / `已验证`。

规则：规格测试断言论文行为，不断言 Go 的函数签名或内部实现。
