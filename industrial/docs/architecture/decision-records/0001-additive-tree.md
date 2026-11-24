# ADR 0001 — additive industrial tree, no framework rewrite

- Status: accepted
- Context: `docs/collector.md` forbids putting industrial business logic into Cordis Core and forbids rewriting stable modules. The user additionally requires that existing content is not modified.
- Decision: create a new `industrial/` tree with its own `Industrial.slnx`; reference existing Cordis projects as read-only infrastructure; put all domain behavior into new contracts, persistence, core, plugins, Elsa PoC and host projects.
- Consequences: no changes to `Gocordis.sln`, Core, Platform or existing tests. Industrial product can evolve independently. Cross-project integration happens through Cordis `Key<T>` capabilities and UI contributions, not project references between plugins.
- Verification: `git status` after commit must show only new `industrial/` paths (plus nothing else); `dotnet build industrial/Industrial.slnx` and its tests prove the new tree compiles and behaves.
