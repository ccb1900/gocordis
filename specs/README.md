# C# 可执行规格目录

本目录是 `docs/migration/semantic-mapping.md` 中论文条目（C01–C40）的**可执行规格占位目录**。
目前实际规格测试位于 `tests/Gocordis.Conformance.Tests/`，尚未镜像到本目录；该缺口已登记在
`docs/migration/final-acceptance.md` §4。

阶段 1 暂不复制文档：语义条目仍以 `docs/migration/semantic-mapping.md` 为唯一权威。
阶段 2 起，每迁移一个 Core 模块，在这里加入对应的规格测试实现，并在
`docs/migration/migration-coverage.md` 中把状态推进为 `已实现` / `已验证`。

规则：规格测试断言论文行为，不断言 Go 的函数签名或内部实现。
