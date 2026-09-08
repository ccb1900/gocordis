# broker — §6.2 Service Multiplexing (paper extension)

The paper's §6.2 pattern, as an extension: a **broker** is the entrypoint
capability injected to both backing providers and consumers, so multiple
providers coexist and the broker dispatches each call among them.

- Providers register through a **reversible effect**
  (`broker.Register` → `ctx.Effect`): unloading a provider reverts its
  registration and drops it from the routing set automatically.
- The broker's own capability binding stays put, so replacing a backing
  provider is a **rolling update** — consumers see no dependency change and
  no reload is triggered (broker_test.go `TestBrokerRollingUpdate`).
- Policies: `RoundRobin` (default), `First`, or custom `Policy[T]`.

Boundary: the broker is an extension — it holds no lifecycle authority and
never mutates fiber state; it is a component that provides one capability and
keeps a routing set (extension boundary audit, ROADMAP §1.3).

Deferred §6 discussion items (see docs/ROADMAP.md §P7): cross-process
invocation (§6.2), WASM capability-surface formalization (§6.3), dependency
typing/versioning (§6.6).
