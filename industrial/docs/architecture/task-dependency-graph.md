# Task dependency graph (collector §16 item 8)

The first-round items (inventory, capability matrix, tech check, Elsa PoC,
plan) were prerequisites, not product features. Their executable dependency
graph into the implemented scenarios is:

```mermaid
flowchart LR
  A[Shared contracts: Key&lt;T&gt;, Resource, Credential, WorkflowDefinition] --> B[Split Cordis plugins: resource / credential / workflow / execution / collection]
  B --> C[Node catalog + Elsa activity catalog]
  B --> D[Reader broker + sink factory broker]
  C --> E[A: new Reader plugin contributes palette node and format route]
  D --> E
  D --> F[B: target Resource + shared credential resolution]
  C --> G[C: declarative page views + client module block renderer]
  E --> H[D: mapping/validation → idempotent sink → archive]
  F --> H
  G --> H
  H --> I[E: ledger failure rows + bounded retry + restart resume]
  H --> J[F: historical range scan + identity dedupe]
  B --> K[G: page/route/navigation/module lifecycle as Fiber effects]
  G --> K
  I --> L[Product proof: pack.sh → real packages → UI/API/ledger]
  J --> L
  K --> L
  E --> L
  F --> L
```

Legend: A–G are the collector §15 acceptance scenarios. Every arrow is a real
capability dependency (`Inject()` → `Provide()`), not a documentation link.

The graph is intentionally acyclic: UI contribution data never drives plugin
activation, and retry never mounts a new component during cleanup.
