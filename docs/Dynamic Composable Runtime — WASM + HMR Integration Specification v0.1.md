# Dynamic Composable Runtime — WASM + HMR Integration Specification v0.1

## 0. 目标

验证以下完整链路在真实 Runtime 语义下成立：

```text
camera-v1.wasm
    ↓
WASM Backend
    ↓
Loader Module v1
    ↓
Factory
    ↓
Runtime Fiber A
    ↓
Active
    ↓
HMR Replace
    ↓
camera-v2.wasm
    ↓
WASM Backend
    ↓
Loader Module v2
    ↓
Factory
    ↓
Runtime Fiber B
    ↓
Active
    ↓
Fiber A → Gone
    ↓
old Module Release
    ↓
old Module Unload
```

本阶段是**集成验证阶段**，不是新的 Runtime 核心设计。

必须保持以下 authority：

| 能力 | 唯一 authority |
|---|---|
| Fiber 生命周期 | Runtime Kernel |
| Dependency / Provider | Runtime Kernel |
| Effect / Ownership | Runtime Kernel |
| Artifact → Module | Loader |
| BackendType → Backend | Loader |
| WASM 实现加载 | WASM Backend |
| Module.Type | Loader Module |
| Factory.Type | Config FactoryRegistry |
| Replacement policy | HMR |
| 外部变化观察 | Watch |
| Desired / Applied | Config |

**禁止新增第二套生命周期系统。**

---

# 1. 非目标

本阶段明确不实现：

- Kernel 修改
- Loader 生命周期重构
- WASM ABI 扩展
- WASI
- filesystem capability
- network capability
- sandbox policy
- state migration
- rollback transaction
- canary / blue-green deployment
- retry/backoff
- Watch → HMR 自动连接
- Config → HMR 自动连接
- Module dependency graph
- HMR persistence
- hot state transfer

如果某测试无法通过而需要修改 Kernel 生命周期语义：

> STOP，并报告 Kernel API/语义缺口。

不得自行修改 Kernel。

---

# 2. 术语

## 2.1 BackendType

描述：

> Artifact 如何被加载。

例如：

```text
builtin
wasm
```

---

## 2.2 ModuleType

描述：

> Module 在 Runtime 中代表什么逻辑实现类型。

例如：

```text
industrial.camera
industrial.sensor
```

BackendType 与 ModuleType 必须独立。

允许：

```text
wasm → industrial.camera
builtin → industrial.camera
wasm → industrial.sensor
```

禁止 HMR 自己推断：

```text
wasm → "wasm"
```

---

# 3. HMR 的唯一成功语义

HMR `Replace` 的成功定义：

```text
New Module Loaded
    ↓
New Module validated
    ↓
New Fiber created
    ↓
New Fiber Active
    ↓
Old Fiber Dispose
    ↓
Old Fiber Gone
    ↓
Old Module Release
    ↓
Old Module Unload
    ↓
Replace success
```

因此：

> **New Fiber Active 是 replacement 的安全提交点。**

在此之前：

```text
Old Fiber = Active
```

必须保持。

---

# 4. Warm Replacement

## 4.1 正常情况

假设：

```text
Target T
Old Module M1
Old Fiber F1
```

执行：

```text
Replace(T, Artifact(M2))
```

目标状态：

```text
M1 Loaded
F1 Active

M2 Loaded
F2 Active

F1 Gone

M1 Released
M1 Unloaded

M2 retained
F2 Active
```

---

# 5. Old Fiber 保留规则

在以下任何阶段失败：

```text
Artifact validation
Backend lookup
WASM load
Module validation
Factory lookup
Factory creation
Fiber creation
New Fiber Ready
```

都必须：

```text
F1 Active
M1 Loaded
binding = M1/F1
```

保持不变。

不得出现：

```text
F1 Gone
M2 Failed
```

这种 destructive-first 状态。

---

# 6. WASM Artifact

测试 Artifact 至少：

```text
camera-v1.wasm
camera-v2.wasm
camera-invalid.wasm
camera-missing-export.wasm
camera-wrong-type.wasm
sensor-v1.wasm
```

合法 Artifact：

```go
loader.Artifact{
    ID:          "...",
    BackendType: loader.BackendTypeWASM,
    Source:      "...",
    Version:     "...",
}
```

不要依赖：

```go
Artifact.Type == "wasm"
```

作为 ModuleType。

---

# 7. WASM ModuleType

WASM Module 必须通过已有 WASM Backend 产生：

```text
Module.Type
```

例如：

```text
industrial.camera
```

或：

```text
industrial.sensor
```

HMR 不负责解析 WASM manifest。

HMR 只能消费：

```go
loader.Module{
    ID:      ...,
    Type:    ...,
    Version: ...,
    Factory: ...,
}
```

---

# 8. HMR Type Compatibility

HMR replacement 必须检查：

```text
OldModule.Type == NewModule.Type
```

才允许默认 replacement。

例如：

```text
industrial.camera
        ↓
industrial.camera
```

允许。

而：

```text
industrial.camera
        ↓
industrial.sensor
```

必须失败。

失败时：

```text
Old Fiber = Active
Old Module = Loaded
Binding unchanged
```

不得自行将 Target 转换成另一种逻辑组件。

---

# 9. BackendType 可以变化

ModuleType 相同并不要求 BackendType 相同。

允许：

```text
builtin → industrial.camera
        ↓
wasm    → industrial.camera
```

因此 HMR compatibility 判断必须基于：

```text
Module.Type
```

而不是：

```text
Artifact.BackendType
```

---

# 10. Provider Replacement

如果 WASM Component 是 exclusive Provider：

```text
Provider(camera) → F1
Consumer → depends(camera)
```

直接 Warm Replacement 可能暂时产生：

```text
F1 Active
F2 Loading
```

从而触发：

```text
ErrDuplicateProvider
```

这是合法 Kernel 语义。

HMR 不得直接修改 Provider registry。

---

# 11. Exclusive Provider Fallback

当 Warm Replacement 因 exclusive Provider 冲突失败时：

HMR 可以通过现有 Kernel public API 执行：

```text
withdraw old
    ↓
old dependents unload
    ↓
old provider Gone
    ↓
new Fiber Load
    ↓
new Fiber Active
    ↓
dependents recover
```

但 HMR 必须：

- 通过 Kernel public API
- 不直接修改 Provider state
- 不直接修改 Fiber state
- 不创建第二 lifecycle engine

---

# 12. Provider Replacement 的安全顺序

例如：

```text
P
↑
A
↑
B
```

P 被 HMR 替换时：

```text
B → Unloading
A → Unloading
P-old → Unloading
P-old → Gone

P-new → Loading
P-new → Active

A → Loading
A → Active

B → Loading
B → Active
```

最终：

```text
P-new Active
A Active
B Active
```

所有依赖者必须绑定新 Provider identity。

---

# 13. Provider Identity

每次 HMR replacement：

```text
Old:
ProviderIdentity{
    FiberID: F1,
    ActivationID: A1,
}
```

New：

```text
ProviderIdentity{
    FiberID: F2,
    ActivationID: A2,
}
```

必须满足：

```text
F1 != F2
A1 != A2
```

不得复用：

- old Fiber
- old Activation
- old Context

---

# 14. WASM Activation Identity

每个 Runtime Activation 必须拥有独立 WASM instance。

因此：

```text
F1 / A1 → WASM instance I1
F2 / A2 → WASM instance I2
```

必须：

```text
I1 != I2
```

旧 Activation 结束后：

```text
I1 destroyed
```

新 Activation：

```text
I2 remains alive
```

---

# 15. WASM Effect Ownership

WASM instance 的创建/销毁必须继续遵守已有 WASM Backend 语义：

```text
ctx.Effect(...)
```

因此：

```text
Activation
    owns
WASM instance
```

而不是：

```text
HMR owns WASM instance lifecycle
```

HMR 只控制 Fiber replacement。

---

# 16. ModuleUsage

HMR 必须继续使用 Loader 的显式 ModuleUsage。

Replacement 前：

```text
M1 usage = HMR binding
```

加载新 Module：

```text
M2 usage acquired
```

只有在：

```text
F1 == Gone
```

之后才：

```text
Release(M1)
```

然后：

```text
Unload(M1)
```

不得提前：

```text
Release(M1)
Unload(M1)
F1 still Active
```

---

# 17. Failed Replacement

以下全部属于 replacement failure：

```text
invalid Artifact
backend unavailable
WASM invalid
WASM missing required export
module type invalid
module type incompatible
factory unavailable
factory creation failure
new fiber creation failure
new fiber never becomes Active
```

失败后的最低保证：

```text
Old Fiber = Active
Old Module = Loaded
Old binding unchanged
```

New resources：

```text
dispose
→ Gone
→ Release
→ Unload
```

不得泄漏。

---

# 18. New Fiber Pending

如果：

```text
New Fiber = Pending
```

则：

```text
Replace = failure
```

不能把：

```text
Pending
```

视为：

```text
successful replacement
```

旧 Fiber 必须继续保持：

```text
Active
```

---

# 19. New Fiber Failed

如果 New Fiber：

```text
Failed
```

则：

```text
Replace = failure
```

旧 Fiber 保持：

```text
Active
```

新 Fiber 必须清理。

---

# 20. Old Fiber Dispose Failure

Old Fiber Dispose 本身如果报告 cleanup error：

必须：

1. 继续执行完整 cleanup
2. 等待 Kernel 的真实 lifecycle 状态
3. 不伪造 Gone
4. 不提前 Unload Module

只有确认：

```text
Old Fiber == Gone
```

之后才：

```text
Release(old module)
Unload(old module)
```

---

# 21. Binding Commit

HMR binding 的 replacement 必须具有明确 commit 点。

推荐：

```text
New Fiber Active
```

之后：

```text
binding = New Module/New Fiber
```

但实现必须保证在并发观察下不会出现：

```text
binding = New
while New Fiber != Active
```

或者：

```text
binding = Old
while Old Fiber == Gone
```

产生长期不一致。

---

# 22. Target Serializability

同一个：

```text
TargetID
```

的 Replace 必须串行。

例如：

```text
Replace(T, v2)
Replace(T, v3)
Replace(T, v4)
```

不能：

```text
v2 replace 与 v3 replace overlap
```

必须存在确定的 linearization order。

---

# 23. Different Target 并发

允许：

```text
T1 → camera
T2 → sensor
```

并行 replacement。

要求：

```text
T1 failure
```

不能影响：

```text
T2
```

不同 Target 不得共享不必要的串行 lifecycle lock。

---

# 24. Same Target 连续 Replacement

若：

```text
Replace(T, v2)
```

完成后：

```text
Replace(T, v3)
```

必须最终：

```text
Target T → v3
```

不得重新绑定 v1。

---

# 25. Same Target Concurrent Replacement

例如两个 goroutine：

```text
G1: Replace(T, v2)
G2: Replace(T, v3)
```

必须存在合法序列：

```text
v1 → v2 → v3
```

或者：

```text
v1 → v3 → v2
```

但不得出现：

```text
v2 Active
v3 Active
binding ambiguous
```

最终 binding 必须对应最后一个实际完成的 linearized replacement。

---

# 26. HMR Close

Close 后：

```text
Replace → ErrHMRClosed
Register → ErrHMRClosed
Bind → ErrHMRClosed
```

如果 replacement 正在运行：

```text
Close
```

必须：

- 拒绝新 replacement
- 等待或取消已有 HMR work
- 清理 HMR-owned ModuleUsage
- 不强杀 goroutine
- 不伪造 Fiber Gone
- 不伪造 Module Unloaded

---

# 27. CloseContext

如果：

```text
CloseContext(ctx)
```

超时：

```text
ctx.Err()
```

可以返回。

但 HMR：

```text
state = Closing
```

不得谎报：

```text
Closed
```

直到真实 cleanup 完成。

---

# 28. Stale Async Completion

必须覆盖：

```text
Replace(v2)
```

产生：

```text
F2 / Activation A2
```

随后又：

```text
Replace(v3)
```

产生：

```text
F3 / Activation A3
```

如果 A2 的异步 completion 晚到：

```text
completion(A2)
```

不得影响：

```text
F3
```

必须通过：

```text
FiberID + ActivationID
```

等已有 Kernel identity 机制隔离 stale completion。

---

# 29. Watch Isolation

本阶段可以使用 Watch 产生：

```text
change detected
```

但 Watch 不得自动执行：

```text
HMR.Replace
```

除非测试显式创建 connector：

```text
Watch
  ↓
test adapter
  ↓
HMR.Replace
```

该 connector 不属于 HMR 或 Watch 核心。

---

# 30. Config Isolation

Config：

```text
Desired → Reconcile
```

与 HMR：

```text
Artifact → Replace
```

是两个独立 authority。

不得因为：

```text
HMR Replace
```

自动修改：

```text
Config.Applied
```

也不得因为：

```text
Config Reconcile
```

隐式调用：

```text
HMR.Replace
```

除非另行实现显式 adapter。

---

# 31. Required API Boundary

HMR 只能通过已有公开 API 使用 Runtime：

```go
rt.Load(...)
fiber.Ready(...)
fiber.Dispose(...)
fiber.Gone(...)
```

不得使用：

```go
fiber.state = ...
fiber.provider = ...
runtime.providers[...] = ...
fiber.activation = ...
```

类似内部 mutation 全部禁止。

---

# 32. Test Fixtures

至少提供：

### WASM v1

```text
module_type = industrial.camera
version = v1
```

### WASM v2

```text
module_type = industrial.camera
version = v2
```

### WASM sensor

```text
module_type = industrial.sensor
version = v1
```

### Invalid

至少：

```text
missing module_type
missing create export
missing destroy export
malformed wasm
incompatible module type
```

---

# 33. HMR + WASM Contract Tests

必须实现：

## W-HMR-01 Basic replacement

```text
WASM v1
→ Fiber A Active
→ replace
→ WASM v2
→ Fiber B Active
→ Fiber A Gone
```

---

## W-HMR-02 Identity

验证：

```text
FiberA != FiberB
ActivationA != ActivationB
WASMInstanceA != WASMInstanceB
```

---

## W-HMR-03 ModuleType preservation

验证：

```text
v1 Module.Type == v2 Module.Type
```

且：

```text
BackendType = wasm
```

不能成为 Module.Type。

---

## W-HMR-04 Backend switch

验证：

```text
builtin → industrial.camera
```

替换：

```text
wasm → industrial.camera
```

成功。

---

## W-HMR-05 Type incompatibility

验证：

```text
camera → sensor
```

失败。

最终：

```text
camera old Fiber Active
```

---

## W-HMR-06 Invalid WASM

Invalid module replacement：

```text
must fail
old Active preserved
```

---

## W-HMR-07 Missing export

必须失败并：

```text
no ModuleUsage leak
no Fiber leak
old Active preserved
```

---

## W-HMR-08 Factory failure

New Factory creation failure：

```text
old Active preserved
```

---

## W-HMR-09 New Fiber failure

验证：

```text
new Fiber != Active
```

时 replacement 失败。

---

## W-HMR-10 Old cleanup

成功 replacement 后：

```text
old Fiber Gone
old Module usage == 0
old Module unloaded
```

---

# 34. Provider Integration

## W-HMR-11 Exclusive provider

：

```text
Provider v1 Active
Consumer Active
```

替换：

```text
Provider v2
```

最终：

```text
Provider v2 Active
Consumer Active
```

Consumer 必须重新绑定：

```text
ProviderIdentity(v2)
```

---

## W-HMR-12 Consumer-first withdrawal

必须验证：

```text
Consumer Unloading
→ Provider old Unloading
```

不能：

```text
Provider old Gone
→ Consumer still Active
```

---

## W-HMR-13 Multi-level dependency

验证：

```text
P → A → B
```

P replacement 后：

```text
B old identity invalid
A old identity invalid
P new Active
A new Active
B new Active
```

最终依赖图正确。

---

# 35. Concurrency Tests

## W-HMR-14 Same Target serialization

100 concurrent replacement：

```text
T → versions
```

要求：

- no panic
- no race
- no overlapping same-target replacement
- final binding valid
- no leaked Fiber
- no leaked ModuleUsage

---

## W-HMR-15 Different Target concurrency

至少：

```text
T1 × 50
T2 × 50
```

验证：

```text
T1 failures isolated
T2 failures isolated
```

---

## W-HMR-16 Replacement + Close

并发：

```text
Replace
Close
```

要求：

- no panic
- no deadlock
- no fake Closed
- no leaked usage
- no stale binding

---

## W-HMR-17 Replacement + stale completion

构造延迟：

```text
v2 completion delayed
v3 completes first
v2 completion arrives later
```

验证：

```text
v2 cannot overwrite v3
```

---

# 36. Failure Preservation Property

定义：

```text
OldState = Active(Fold)
```

对于任何 replacement failure：

```text
FinalBinding = OldBinding
FinalFiber = OldFiber
FinalFiber.State = Active
```

除非 failure 本身发生在已有 old Fiber cleanup 阶段，并且该阶段已经正式进入 destructive fallback。

---

# 37. Resource Conservation Property

任意成功 replacement：

```text
old Fiber Gone
old Module usage = 0
old Module unloaded
```

任意失败 replacement：

```text
new Fiber Gone or never created
new Module usage = 0
new Module unloaded
```

不得：

```text
orphan Fiber
orphan Activation
orphan WASM instance
orphan ModuleUsage
```

---

# 38. Activation Conservation Property

对于每个成功 replacement：

```text
one old activation ends
one new activation survives
```

必须不存在：

```text
same activation reused by new implementation
```

---

# 39. Module/Fiber Orthogonality

必须验证：

```text
Module unload
```

不是：

```text
Fiber lifecycle
```

的替代品。

例如：

```text
old Module Loaded
old Fiber Active
```

期间：

```text
Module Unload
```

必须被 ModuleUsage 阻止。

反过来：

```text
Fiber Gone
```

并不自动意味着：

```text
Module unloaded
```

只有 HMR 显式 Release/Unload 才完成 Module lifecycle。

---

# 40. No Direct WASM/HMR Lifecycle

WASM Backend 不得：

```text
Dispose Fiber
Unload Fiber
replace Provider
```

HMR 不得：

```text
destroy Fiber internals
destroy Activation manually
```

所有 Fiber lifecycle 必须通过 Runtime Kernel。

---

# 41. E2E Matrix

至少实现：

```text
E2E-WH-01  WASM v1 → v2
E2E-WH-02  WASM identity change
E2E-WH-03  backend switch builtin → wasm
E2E-WH-04  incompatible ModuleType
E2E-WH-05  invalid WASM preservation
E2E-WH-06  missing export preservation
E2E-WH-07  factory failure preservation
E2E-WH-08  new Fiber failure preservation
E2E-WH-09  old Module unload after Gone
E2E-WH-10  exclusive Provider replacement
E2E-WH-11  consumer rebinding
E2E-WH-12  multi-level dependency replacement
E2E-WH-13  same Target concurrency
E2E-WH-14  different Target concurrency
E2E-WH-15  stale completion
E2E-WH-16  Close during replacement
E2E-WH-17  replacement resource conservation
E2E-WH-18  Module/Fiber orthogonality
E2E-WH-19  Watch isolation
E2E-WH-20  Config isolation
```

---

# 42. Property Tests

至少：

```text
P-WH-01 Failure Preservation
P-WH-02 Resource Conservation
P-WH-03 Activation Identity
P-WH-04 Module/Fiber Orthogonality
P-WH-05 Provider Rebinding
P-WH-06 Same-Target Serialization
P-WH-07 Stale Completion Isolation
P-WH-08 Close Truthfulness
```

---

# 43. Race / Vet

必须：

```bash
go test ./...
go test -race ./...
go vet ./...
```

并至少：

```bash
go test -race ./... -count=3
```

对于 HMR concurrency suite。

---

# 44. Required Files

推荐：

```text
extensions/hmr/
    hmr.go
    hmr_impl.go
    hmr_test.go
    hmr_wasm_test.go
    hmr_concurrency_test.go
```

或者按现有项目结构组织。

不得为了测试修改 Kernel production semantics。

---

# 45. Real WASM Execution Boundary

本阶段必须明确记录：

> HMR 集成是否真正执行 WASM instruction，与 HMR replacement 语义是两个独立 gate。

如果当前 WASM Backend 已经具有真实 WASM execution：

必须使用真实 runtime fixture。

如果当前 WASM Backend 仍然只是 binary/module validation：

不得伪称：

```text
real WASM execution PASS
```

可以验证：

```text
WASM Artifact
→ Backend
→ Module
→ Factory
→ Fiber
→ HMR
```

但报告中必须单独标记：

```text
REAL-WASM-EXECUTION = PASS / PENDING
```

不得与 HMR integration PASS 混为一个结论。

---

# 46. Acceptance Gate

本阶段完整 PASS 必须同时满足：

```text
W-HMR-01 ~ W-HMR-17        PASS
E2E-WH-01 ~ E2E-WH-20      PASS
P-WH-01 ~ P-WH-08          PASS
go test ./...              PASS
go test -race ./...        PASS
go vet ./...               PASS
```

并且：

```text
Kernel production changes = 0
```

除非明确报告为：

```text
BLOCKED: Kernel API gap
```

---

# 47. 最终判定

### PASS

满足：

```text
WASM Backend
    ↓
Loader
    ↓
Factory
    ↓
Runtime
    ↓
HMR
```

完整组合成立，并且：

- replacement safety 成立
- failure preservation 成立
- provider rebinding 成立
- ModuleUsage 无泄漏
- Activation identity 正确
- stale completion 隔离
- same-target serialization 成立
- close truthfulness 成立
- race/vet/test 全绿

### CONDITIONAL PASS

功能测试全绿，但：

```text
REAL-WASM-EXECUTION = PENDING
```

或存在不影响 v0.1 语义的明确环境边界。

### BLOCKED

出现以下任一情况：

- 需要修改 Kernel lifecycle semantics
- HMR 直接修改 Fiber/Provider internals
- old Fiber 被提前销毁导致 failure preservation 破坏
- ModuleUsage 泄漏
- WASM instance 生命周期脱离 Activation
- stale completion 能修改新 Activation
- same Target replacement overlap
- race detector failure
- close 后状态伪造为 Closed

---

# 48. Code Agent 执行规则

Code Agent 不得重新设计上述语义。

执行顺序：

```text
1. Inspect current implementation
2. Map existing HMR ↔ Loader ↔ WASM APIs
3. Identify API gaps
4. Implement only missing integration
5. Add contract tests
6. Add E2E tests
7. Add concurrency/property tests
8. Run test/race/vet
9. Report exact evidence
```

如果发现：

```text
API gap
```

先 STOP 并报告：

```text
GAP:
CURRENT:
REQUIRED:
WHY EXISTING PUBLIC API IS INSUFFICIENT:
PROPOSED MINIMAL API CHANGE:
```

不要直接修改 Kernel。

---

# 49. Completion Report Format

完成后必须报告：

```text
WASM + HMR INTEGRATION v0.1 — PASS / CONDITIONAL PASS / BLOCKED

W-HMR-01 ~ 17:
E2E-WH-01 ~ 20:
P-WH-01 ~ 08:

go test ./...:
go test -race ./...:
go vet ./...:

REAL-WASM-EXECUTION:
Kernel production changes:

Files changed:

Known limitations:

Semantic deviations:
```

尤其必须单独回答：

```text
1. New Fiber 是否一定在 Old Fiber destructive cleanup 前达到 Active？
2. Old Module 是否一定等 Old Fiber Gone 后才 Release/Unload？
3. HMR failure 是否保持 Old Active？
4. Provider replacement 是否完全通过 Kernel semantics？
5. WASM instance 是否绑定 Activation？
6. stale completion 是否无法影响新 Activation？
7. 同 Target replacement 是否严格串行？
8. 是否存在任何第二套 lifecycle system？
```

以上 8 项必须逐项给出证据。