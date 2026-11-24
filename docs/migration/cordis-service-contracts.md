# Cordis-style Managed Plugin Service Contracts (maintained)

Status: **implemented** (`src/ExtensionContracts/<Name>`).

Direction decision: **Managed-first**. WASM/Process remain isolation backends for
capability envelopes, but Managed plugins do not degrade their typed services to
`ICapabilityEndpoint` for the sake of backends that are not the product path.
Plugin A publishes an interface + Key; Plugin B compiles against A's public
contract and requires the Key at runtime; Core still owns matching, lifecycle,
withdrawal and reactivation.

## The five review questions, answered with source anchors

1. **Contract ownership** — the former aggregate `ExtensionContracts.cs` put
   all eight interfaces and DTOs in ONE assembly, which mirrored the old
   ambient-extension shape rather than Cordis. In Cordis the `database`
   interface is published by the plugin that owns the service. Implemented:
   `src/ExtensionContracts/<Name>` gives each provider its own interface, DTOs
   and Key class; the implementation assembly references contracts, never the
   reverse.

2. **SharedAssemblyRegistry** — `src/Gocordis.Runtime.Managed/SharedAssemblyRegistry.cs:162-238`
   verifies name/version/culture/public-key-token/MVID/SHA-256 and loads one
   canonical copy into `AssemblyLoadContext.Default`; `:261-334` rejects a plugin
   copy that differs. That is sufficient for type identity IF the shared assembly
   contains no implementation and every consumer declares the same name via
   `--shared-assembly`. It is NOT a version-compatibility authority: a version
   change requires host restart (`:97-103`). Package version ranges and contract
   version are separate declarations; see item 3.

3. **Three dependency layers (must stay distinct)** —
   - *Package dependency*: `PluginDependency` + range, enforced by
     `PluginDependencyPlanner` / `PluginStore`; install-time only.
   - *Type dependency*: a `ProjectReference`/NuGet reference to the provider's
     public contract assembly; compile-time only.
   - *Capability dependency*: `Component.Inject()` / `context.Require(Key<T>)`;
     `Core.Runtime` reconciliation at runtime.
   `PluginManifest.Requires/Provides` (`src/Gocordis.Plugins/PluginManifest.cs`)
   describe the runtime capability graph; package dependencies describe the store.
   They MUST NOT be auto-derived from each other: an installed-but-undeclared
   provider yields `PACKAGE_DEPENDENCY_NOT_DECLARED`, never a fake activation.

4. **Typed service access** — `Context.Require<T>(Key<T>)` in
   `src/Gocordis.Core/Context.cs` already returns `T` from the shared Key. The
   Cordis-style ergonomic improvement is to expose a single typed accessor on the
   consumer's own contract usage (e.g. `ctx.Get(HttpKeys.Service)`), not to
   replace Key<T> with strings. No Proxy `ctx.foo` and no Core modification.

5. **Lifecycle coverage** — Core already handles missing provider → `Pending`,
   provider withdrawal → effect unwind + Pending, incompatible revision → new
   fiber, and strict LIFO inverses (`Runtime.cs`, `Fiber.cs`). Contract extraction
   does not touch that authority; provider components publish services through
   `Context.Provide` with the disposal inverse installed BEFORE the capability.

## Invariants for the split

- A plugin implementation assembly never references another plugin's
  implementation assembly; it references public contracts only.
- Each public contract assembly has no ASP.NET Core framework reference and no
  service implementation.
- Cross-contract dependencies are allowed and are compile-time dependencies only
  (SOCKS contract references HTTP contract because it composes with HTTP).
- The SDK may re-export several contracts for authoring convenience, but every
  packaged plugin declares only the contract assemblies it actually uses via
  `--shared-assembly <ContractName>`.
- Keys are owned by the provider's contract assembly; consumers resolve the
  static Key instance from that assembly, never construct a same-named key.
