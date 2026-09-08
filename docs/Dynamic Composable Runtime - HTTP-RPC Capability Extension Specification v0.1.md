> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime — HTTP/RPC Capability Extension v0.1

## 0. 目标

实现一个位于 Extension Plane 的 HTTP Server Capability，验证：

```text
Runtime Fiber
    ↓
HTTP Server Component
    ↓
listen / serve
    ↓
external resource
    ↓
graceful shutdown
    ↓
Fiber Gone
```

同时验证：

```text
HTTP Provider
    ↓
Registry / Capability
    ↓
Consumer
```

以及：

```text
HTTP implementation replacement
    ↓
HMR
    ↓
new Fiber
    ↓
new HTTP resource
    ↓
old Fiber Gone
    ↓
old server shutdown
```

本阶段的核心不是 HTTP API 数量，而是：

> **真实外部资源是否严格服从 Runtime Fiber / Activation / Effect / Ownership / Dependency / Close 语义。**

---

# 1. Authority

必须保持：

| Concern | Authority |
|---|---|
| Fiber lifecycle | Kernel |
| Activation | Kernel |
| Effect | Kernel |
| Dependency | Kernel |
| Provider identity | Kernel |
| Ownership | Kernel |
| HTTP listen/serve | HTTP Extension |
| graceful shutdown | HTTP Extension |
| HTTP implementation loading | Loader |
| desired configuration | Config |
| replacement | HMR |
| endpoint/member discovery | Registry |

HTTP Extension **不得成为第二生命周期系统**。

---

# 2. Non-Goals

v0.1 不实现：

- Web framework
- routing framework
- middleware framework
- TLS certificate management
- HTTP/2 tuning
- HTTP/3
- WebSocket
- streaming RPC
- authentication
- authorization
- rate limiting
- load balancing
- service discovery
- retry policy
- circuit breaker
- distributed tracing
- metrics backend
- persistence
- Kubernetes integration
- automatic Watch → HMR
- automatic Config → HMR

只需要证明 Runtime 可以正确托管 HTTP Server。

---

# 3. 最小 API

推荐：

```go
type ServerConfig struct {
    Network string
    Address string
}

type Server struct {
    // internal
}

type ServerFactory interface {
    Create(config ServerConfig) (Component, error)
}
```

HTTP Component 必须实现现有：

```go
runtime.Component
```

不得定义第二个 Component interface。

---

# 4. Server 生命周期

HTTP server 的真实资源生命周期：

```text
Absent
  ↓
Starting
  ↓
Serving
  ↓
Stopping
  ↓
Stopped
```

但这只是 HTTP implementation 内部状态。

它**不是 Runtime Fiber state machine**。

HTTP Extension 不得公开：

```go
ServerState
```

作为第二套 Runtime lifecycle authority。

Runtime 的权威状态仍然是：

```text
Pending
Loading
Active
Unloading
Failed
Gone
```

---

# 5. Fiber / Server 对应关系

推荐：

```text
Fiber F
Activation A
    │
    └── owns HTTP Server S
```

因此：

```text
F Active
```

意味着 HTTP server 已经成功进入可服务状态。

而：

```text
F Gone
```

意味着 HTTP resource 已完成 shutdown/cleanup。

---

# 6. Start Commit Point

HTTP Component 的 `Apply` 必须满足：

```text
create server
    ↓
listen
    ↓
server serving
    ↓
record reversible cleanup
    ↓
Apply success
```

不能：

```text
Apply returns nil
    ↓
goroutine later attempts Listen
```

然后 Fiber 已经：

```text
Active
```

却 HTTP server 实际没有成功启动。

---

# 7. Port / Address Conflict

两个 Fiber：

```text
F1 → :8080
F2 → :8080
```

第二个必须：

```text
Apply → error
```

结果：

```text
F1 = Active
F2 = Failed
```

不得：

- 修改 F1
- 停止 F1
- 抢占 address
- 自动换端口

---

# 8. Listen 成功后 Effect

`Listen` 是 Runtime-managed external resource。

必须通过：

```go
ctx.Effect(...)
```

绑定 cleanup。

逻辑：

```text
Listen
  ↓
server resource created
  ↓
Effect committed
  ↓
Fiber Active
```

如果 Effect commit 失败或 Activation 已进入 unwind：

```text
server.Close / Shutdown
```

必须执行。

不得留下孤立 listener。

---

# 9. Apply Failure

以下任一失败：

- invalid network
- invalid address
- listen failure
- server setup failure
- handler setup failure

都必须：

```text
Fiber → Failed
```

并执行已经创建资源的完整逆操作。

要求：

```text
no listener leak
no server goroutine leak
no partially active HTTP server
```

---

# 10. HTTP Handler

v0.1 只需要最小 Handler：

```go
type Handler interface {
    ServeHTTP(http.ResponseWriter, *http.Request)
}
```

或者直接使用：

```go
http.Handler
```

如果使用标准库类型，不得因此把 HTTP Server lifecycle 放到标准库之外管理。

---

# 11. Handler 生命周期

Handler 本身不是 Fiber。

不要创建：

```text
Handler Fiber
```

除非另有明确 Runtime Component。

HTTP Server Fiber：

```text
owns
    ↓
HTTP listener
    ↓
HTTP server
```

Handler 是 Server 的 implementation data。

---

# 12. Requests 与 Fiber

请求处理过程中：

```text
HTTP request
    ↓
Handler
```

不得改变 Fiber lifecycle。

特别禁止：

```text
request
  ↓
Dispose Fiber
```

作为默认行为。

---

# 13. In-flight Requests

Server shutdown 必须区分：

```text
listener stopped
```

和：

```text
in-flight request completed
```

正常 shutdown：

```text
stop accepting new connections
    ↓
wait for active handlers
    ↓
server cleanup complete
```

只有 cleanup 完成后 Runtime Fiber 才能最终：

```text
Gone
```

---

# 14. Graceful Shutdown

HTTP Component 必须提供：

```text
Shutdown(ctx)
```

语义：

- 停止接受新请求
- 等待 in-flight requests
- ctx 未取消时尽量完成 graceful shutdown
- ctx 超时则返回 context error
- 不伪造 cleanup completion

---

# 15. Shutdown Timeout

如果：

```text
Dispose
```

触发 shutdown：

```text
Shutdown(ctx)
```

超时：

```text
ctx.Err()
```

必须向 Runtime cleanup 层报告。

但不得：

```text
Fiber = Gone
```

仅仅因为 timeout。

Fiber 是否 Gone 由 Kernel 根据真实 cleanup 结果决定。

---

# 16. Force Close Boundary

如果标准 HTTP Server 支持：

```go
Server.Close()
```

可以作为 graceful shutdown 超时后的 implementation cleanup。

但必须明确：

> `Close()` 是资源 cleanup，不是强杀 goroutine。

不得杀死正在执行的 Go goroutine。

---

# 17. Apply / Inverse 不重叠

同一 HTTP Fiber：

```text
Apply
```

和：

```text
Inverse
```

不得并发执行。

由 Kernel 已有 lifecycle serialization 保证。

HTTP Extension 不得自己建立另一套 Fiber lock 来改变该语义。

---

# 18. Ownership

HTTP server resource：

```text
Activation A
    owns
HTTP listener L
HTTP server S
```

Activation 结束：

```text
A unwind
    ↓
S shutdown
    ↓
L closed
```

必须确保：

```text
Activation Gone
→ no Runtime-owned HTTP resource remains
```

---

# 19. Dependency Provider

HTTP Server 可以提供：

```text
HTTPServerCapability
```

例如：

```go
type ServerHandle struct {
    Address string
    // implementation-owned handle
}
```

但 Capability Key 必须使用 Runtime 的 typed capability mechanism。

禁止：

```text
"global.http.server"
```

作为无类型全局字符串状态。

---

# 20. Provider Identity

HTTP Server Fiber 激活后提供：

```text
ProviderIdentity(FiberID, ActivationID)
```

例如：

```text
HTTP Provider
    F1 / A1
```

HMR 后：

```text
HTTP Provider
    F2 / A2
```

必须：

```text
F1 != F2
A1 != A2
```

---

# 21. Consumer Dependency

Consumer：

```text
Consumer C
    requires HTTPServerCapability
```

只有 Provider：

```text
Fiber Active
```

之后 Consumer 才能进入：

```text
Active
```

如果 HTTP Provider withdraw：

```text
Consumer
    ↓
Unloading
```

必须先于：

```text
Provider
    ↓
Unloading
```

保持已有 Kernel consumer-first withdrawal。

---

# 22. Provider Replacement

正常 HTTP Provider HMR：

```text
HTTP v1
    ↓
F1 Active
```

replacement：

```text
HTTP v2
    ↓
F2 Active
    ↓
F1 Gone
```

如果 exclusive Provider 导致：

```text
F2 Load
→ ErrDuplicateProvider
```

必须使用已有 HMR sanctioned fallback：

```text
withdraw old
    ↓
old provider Gone
    ↓
new Load
    ↓
new Active
    ↓
consumer recovery
```

HMR 不得修改 Provider registry。

---

# 23. HTTP Port During Warm Replacement

HTTP 是一个特别重要的 boundary：

如果 v1 和 v2 都要求：

```text
:8080
```

那么正常 Warm Replacement：

```text
F1 owns :8080
F2 attempts :8080
```

可能自然失败。

这不是错误的 Kernel 语义。

v0.1：

> **不允许端口抢占。**

因此 HMR 必须正确处理：

```text
new HTTP Fiber failed to bind
```

并保持：

```text
old HTTP Fiber Active
```

如果需要真正 zero-downtime same-port replacement：

> 作为未来独立 capability / handoff 设计，不在 v0.1 偷加。

---

# 24. HMR Different Port

测试：

```text
v1 → :18080
v2 → :18081
```

必须允许 Warm Replacement：

```text
F1 Active :18080
F2 Active :18081
F1 Gone
```

最终：

```text
:18081 = owned by F2
:18080 = released
```

---

# 25. Same-Port Replacement

测试：

```text
v1 → :18080
v2 → :18080
```

默认 Warm Replacement：

```text
v2 fails listen
```

必须：

```text
F1 Active
binding unchanged
```

不得自动：

```text
F1 shutdown
```

---

# 26. Registry Integration

推荐通过 Registry 暴露动态 HTTP endpoint/server handles。

例如：

```text
HTTP Registry
    ↓
MemberID
    ↓
ServerHandle
```

必须遵循已有 Registry 语义：

```text
Registry Provider identity stable
member churn ≠ consumer reactivation
```

因此：

```text
add server
remove server
replace member
```

不得导致依赖 Registry 的 Consumer 重新激活。

---

# 27. Registry Ownership

Server Member 必须明确：

```text
who owns listener?
who removes member?
who closes server?
```

推荐：

```text
HTTP Activation
    owns
    ↓
Registry member
    ↓
server resource
```

Activation unwind：

```text
server shutdown
    ↓
registry member removal
```

不得留下 Registry 中的 stale handle。

---

# 28. Event Integration

HTTP Extension 可以发布：

```text
ServerStarted
ServerStopped
RequestObserved
```

但 Event 是 notification，不是 lifecycle authority。

不得：

```text
Event → automatically Dispose Fiber
```

或：

```text
Event → dependency satisfaction
```

除非未来显式 adapter。

---

# 29. Scheduler Integration

HTTP Extension 不得依赖 Scheduler 才能实现基本 lifecycle。

例如：

```text
Schedule shutdown
```

不是 HTTP core requirement。

Scheduler 只是 optional extension。

---

# 30. Config Integration

Config：

```text
ComponentConfig{
    ID:   "camera-http",
    Type: "http.server",
    Config: {
        "network": "tcp",
        "address": ":18080",
    },
}
```

Config Controller：

```text
Create
→ Runtime.Load
```

HTTP Component：

```text
Apply
→ Listen
→ Active
```

必须保持：

```text
Config.Applied
```

与：

```text
Fiber.State
```

的语义独立。

---

# 31. Invalid Config

例如：

```text
address = ""
```

Factory/Create 或 Component Apply 必须失败。

要求：

```text
Config.Applied unchanged
old Fiber unchanged
```

如果是 Config replacement：

```text
old HTTP Fiber Active
new invalid HTTP config
```

必须保持 old Active。

---

# 32. Close Semantics

Runtime Close：

```text
Running
  ↓
Closing
  ↓
HTTP Fibers dispose
  ↓
HTTP servers shutdown
  ↓
Fibers Gone
  ↓
Runtime Closed
```

如果 HTTP shutdown 尚未完成：

```text
Runtime != Closed
```

不得伪造。

---

# 33. Close During Request

测试：

```text
request handler running
        ↓
Runtime.Close()
```

必须：

- 停止接受新 request
- 等待 existing request
- 完成 server cleanup
- Fiber 最终 Gone
- Runtime 最终 Closed

如果 CloseContext timeout：

```text
return ctx.Err()
Runtime remains Closing
```

---

# 34. Close During Start

并发：

```text
Fiber Apply
Runtime Close
```

必须依赖已有 Kernel linearization。

HTTP implementation 不得自行决定：

```text
started == true
```

然后偷偷绕过 Runtime lifecycle。

---

# 35. Listener Ownership

Listener 必须只有一个明确 owner：

```text
Activation
```

禁止：

```text
global listener map
```

禁止：

```text
package singleton
```

禁止：

```text
http.DefaultServeMux
```

作为 Runtime-global state。

如果使用 ServeMux：

> 必须为该 Server 实例创建独立 mux。

---

# 36. Global State Prohibition

禁止：

```go
var globalServer *http.Server
```

禁止：

```go
var globalRegistry map[string]*Server
```

所有 Runtime-managed HTTP state 必须 instance-scoped。

---

# 37. Panic Isolation

HTTP handler panic：

不得：

```text
panic → Runtime process panic
```

至少应由 HTTP implementation / net/http 的标准 panic isolation 机制处理。

Handler panic 不得直接破坏其他 Fiber。

Factory panic 同样必须转为正常 error。

---

# 38. Resource Leak Property

任意失败路径：

```text
Create
Listen
Apply
Dispose
HMR
Runtime Close
```

最终不得存在：

```text
orphan listener
orphan http.Server
orphan serving goroutine
orphan Registry member
```

对于测试环境，可以通过：

- address rebinding
- active server counters
- explicit handles
- shutdown markers

验证资源已释放。

---

# 39. HTTP Component Identity

每次新的 Runtime Activation：

```text
F1 / A1
```

产生：

```text
Server S1
```

再次 Activation：

```text
F2 / A2
```

必须：

```text
S1 != S2
```

不得复用旧 HTTP Server instance。

---

# 40. HMR HTTP Instance

成功：

```text
v1 → v2
```

必须：

```text
FiberA != FiberB
ActivationA != ActivationB
ServerA != ServerB
```

最终：

```text
ServerA stopped
ServerB serving
```

---

# 41. Required Tests

## HTTP-01 Basic Start

```text
Load HTTP component
→ Fiber Active
→ HTTP endpoint responds
```

## HTTP-02 Stop

```text
Dispose
→ endpoint unavailable
→ Fiber Gone
```

## HTTP-03 Port Conflict

```text
F1 :18080 Active
F2 :18080 Failed
F1 unchanged
```

## HTTP-04 Apply Failure Cleanup

Listen succeeds partially, later setup fails：

```text
no listener leak
```

## HTTP-05 Graceful Request

long-running handler：

```text
request starts
→ Dispose
→ request finishes
→ Fiber Gone
```

## HTTP-06 Shutdown Timeout

handler intentionally remains active：

```text
shutdown timeout
→ error
→ not falsely Gone
```

## HTTP-07 Restart

```text
F1 Gone
→ F2 Load
→ Active
```

same address allowed only after old listener is actually released.

## HTTP-08 Activation Identity

repeated activation：

```text
different Fiber
different Activation
different Server
```

---

# 42. Dependency Tests

## HTTP-09 Consumer Dependency

```text
HTTP Provider
    ↓
Consumer
```

Consumer Active only after Provider Active.

## HTTP-10 Provider Withdrawal

```text
Consumer Unloading
→ Provider Unloading
```

## HTTP-11 Provider Recovery

```text
Provider replacement
→ Consumer recovers
```

## HTTP-12 Multi-Level

```text
HTTP
 ↑
A
 ↑
B
```

withdrawal/recovery order必须正确。

---

# 43. Registry Tests

## HTTP-13 Stable Registry

添加/删除 HTTP server member：

```text
Consumer Registry dependency remains Active
```

## HTTP-14 Member Cleanup

Server Gone：

```text
Registry member removed
```

## HTTP-15 Provider Replacement

Registry Provider replacement：

```text
Provider identity changes
```

但普通 member churn：

```text
Provider identity unchanged
```

---

# 44. HMR Tests

## HTTP-16 Different Port Warm Replacement

```text
v1 :18080 Active
v2 :18081 Active
v1 Gone
```

## HTTP-17 Same Port Failure Preservation

```text
v1 :18080 Active
v2 :18080
→ replacement failure
→ v1 remains Active
```

## HTTP-18 HMR Instance Identity

```text
Server1 != Server2
Fiber1 != Fiber2
Activation1 != Activation2
```

## HTTP-19 HMR Cleanup

成功 replacement：

```text
old server stopped
old listener released
old Module usage released
```

## HTTP-20 HMR Failure

invalid/new listen failure：

```text
old server still responding
```

---

# 45. Config / E2E Tests

## HTTP-21 Config → HTTP

```text
Config
→ Factory
→ HTTP Component
→ Fiber Active
→ actual HTTP response
```

## HTTP-22 Invalid Config Preservation

```text
Active v1
→ invalid desired config
→ v1 remains
```

## HTTP-23 Config Replacement

```text
v1 :18080
→ desired v2 :18081
→ v1 Gone
→ v2 Active
```

---

# 46. Runtime Close Tests

## HTTP-24 Close

```text
Runtime.Close
→ HTTP shutdown
→ Fiber Gone
→ Runtime Closed
```

## HTTP-25 Close During Request

必须等待 request cleanup。

## HTTP-26 Close Timeout

必须：

```text
ErrContext
Runtime != Closed
```

直到真实 cleanup 完成。

---

# 47. Concurrency Tests

## HTTP-27 Concurrent Start

100 Fiber attempts：

- no panic
- no race
- conflicting addresses deterministic
- exactly one successful owner per address

## HTTP-28 Start / Stop

并发：

```text
Load
Dispose
```

必须遵守 Kernel linearization。

## HTTP-29 HMR / Request

replacement 与 requests 并发：

旧 server cleanup 后：

```text
new server responds
```

不得出现：

```text
new Fiber Active
but old server still unexpectedly owns resource
```

## HTTP-30 Close Storm

多个 goroutine：

```text
Close
Close
CloseContext
```

必须：

- idempotent
- no panic
- no race
- no double close corruption

---

# 48. Properties

至少：

```text
P-HTTP-01 Lifecycle Conservation
P-HTTP-02 Resource Conservation
P-HTTP-03 Ownership Conservation
P-HTTP-04 Provider Withdrawal Safety
P-HTTP-05 Provider Recovery
P-HTTP-06 HMR Failure Preservation
P-HTTP-07 Activation Isolation
P-HTTP-08 Close Truthfulness
P-HTTP-09 Address Exclusivity
P-HTTP-10 Registry Stability
```

---

# 49. Race / Vet

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

---

# 50. Required Implementation Boundary

推荐：

```text
extensions/http/
    http.go
    server.go
    component.go
    http_test.go
    http_concurrency_test.go
```

如果项目已有其他命名，以现有结构为准。

不得修改：

```text
runtime/
```

除非发现明确 Kernel API gap。

---

# 51. STOP Conditions

遇到以下任一情况立即 STOP：

1. 必须修改 Kernel Fiber state machine
2. 必须直接修改 Provider registry
3. 必须创建第二套 lifecycle
4. 必须依赖 global HTTP server
5. 无法表达 listener ownership
6. Fiber Active 时 HTTP server 尚未真正 serving
7. Fiber Gone 时 HTTP resource 仍由 Runtime-owned state 持有
8. graceful shutdown 无法与 Fiber cleanup 对齐
9. HMR 必须先破坏 old Fiber 才能避免 same-port conflict
10. 需要新增 Kernel API 才能完成正确语义

报告：

```text
GAP:
CURRENT:
REQUIRED:
WHY EXISTING PUBLIC API IS INSUFFICIENT:
MINIMAL PROPOSED CHANGE:
```

不得自行修改 Kernel。

---

# 52. Acceptance Gate

完整 PASS：

```text
HTTP-01 ~ HTTP-30      PASS
P-HTTP-01 ~ P-HTTP-10  PASS
go test ./...           PASS
go test -race ./...     PASS
go vet ./...            PASS
```

并：

```text
Kernel production changes = 0
```

---

# 53. 最终必须回答

Code Agent 完成后必须逐项回答：

```text
1. Fiber Active 时 HTTP server 是否已经真实 serving？
2. Fiber Gone 前 listener/server 是否已经完成 cleanup？
3. listener 是否严格属于 Activation ownership？
4. Apply failure 是否完整 unwind？
5. graceful shutdown 是否与 Fiber lifecycle 对齐？
6. in-flight request 是否安全处理？
7. same-address 是否保持独占？
8. HMR failure 是否保持 old server Active？
9. HMR 是否产生新的 Fiber/Activation/Server？
10. Provider withdrawal/recovery 是否完全由 Kernel authority 完成？
11. Registry member churn 是否不会导致 consumer reactivation？
12. Runtime Close 是否 truthful？
13. 是否存在 global HTTP state？
14. 是否存在第二套 lifecycle system？
15. 是否修改了 Kernel production code？
```

每项必须给出测试证据。

---

# 54. Completion Report

最终报告格式：

```text
HTTP/RPC CAPABILITY v0.1 — PASS / CONDITIONAL PASS / BLOCKED

HTTP-01 ~ HTTP-30:
P-HTTP-01 ~ P-HTTP-10:

go test ./...:
go test -race ./...:
go vet ./...:

Kernel production changes:

Files changed:

Known limitations:

Semantic deviations:

Answers 1-15:
```

特别注意：

> **不要为了实现 same-port zero-downtime 而修改 Runtime 或 HMR 语义。**

v0.1 的安全原则是：

```text
new resource first
old resource remains
new resource cannot acquire occupied address
→ replacement fails safely
```

真正的 socket handoff / listener inheritance / zero-downtime deployment 应当作为未来独立 capability 设计。