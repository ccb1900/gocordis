# cordis console

The console is the second Cordis application: domain-free, it renders
whatever the composition declares. Pages are declarative view stacks
(`views` on a page/panel), data comes from hub named queries
(`/api/query/<name>`), actions go through hub commands
(`/api/command/<name>`), and the observation stream (`/api/stream`)
only invalidates — it never carries state.

## View blocks

A page/panel view block is a declarative row:

```toml
[[components]]
id = "ui-page-collections"
type = "ui-page"

[components.config]
  [[components.config.views]]
  kind = "table"          # any kind in the renderer registry
  query = "collections"   # hub named query answering the data
  [components.config.params]
  date = "$yesterday"     # $today / $yesterday / $focus.sourceId / $focus.date
```

Built-in kinds: `table`, `kv`, `list`, `stats`, `trend`, `query-table`.

### Interest domains (`domain`)

Every block re-queries when its declared `domain` moves, not on every
observation event:

| domain          | moves on                                   |
| --------------- | ------------------------------------------ |
| `collection`    | collection work (default)                  |
| `composition`   | `composition.*` lifecycle events           |
| `source:<id>`   | events scoped to one source unit           |
| `all`           | any event                                  |

A composition edit therefore no longer re-fetches every data table.

## Registering a custom renderer

The palette is a registry, not a switch statement. A client module can add
a new block kind (or a page/panel renderer) before first render:

```tsx
import { registerBlockRenderer } from "./views/registry";

registerBlockRenderer("gauge", function GaugeBlock({ block, ctx }) {
  // block: the declared view row; ctx: hubQuery/hubCommand/focus/busy
  ...
});
```

Custom kinds immediately appear everywhere kinds are enumerated — including
the plugin config editor's kind dropdown (`registeredBlockKinds()`).
Page-level and panel-level renderers work the same way via
`registerPageRenderer` / `registerPanelRenderer`.

Unknown kinds never break a page: the console renders an explicit fallback
showing the raw declaration.

### Widgets (composable components)

A plugin can contribute reusable components; declarative views compose them
by name through the built-in `widget` block kind — params resolve `$focus`
references like every query block:

```js
// in the plugin module's register function
api.widgets.register("alarm-summary", function AlarmSummary({ ctx, params }) {
  // ctx: hubQuery/hubCommand/focus; params: the block's resolved params
});
```

```toml
[[components.config.views]]
kind = "widget"
[components.config.params]
name = "alarm-summary"
source = "$focus.sourceId"
```

An unregistered widget name renders an explicit fallback (the declaration is
shown, never dropped).

### Settings (per-plugin config UI)

A plugin may replace the generic structured config editor in the plugin
explorer with its own settings UI — registered against its own module name:

```js
api.settings.register(function AlarmSettings({ pluginId, config, onSaved }) {
  // custom editing UX; persist via onSaved(config) (api.setPluginConfig)
});
```

The slot owns the config round-trip (fetch + save) so every settings UI
stays a pure view of its plugin's config. Without a registration the
explorer falls back to the built-in editor.

### Actions (context operations)

Plugins can surface context-bound operations in the shell — a dropdown above
the page card, filtered by the live context:

```js
api.actions.register({
  id: "alarm-rerun", title: "重跑当日采集",
  when: ({ page }) => page?.id === "collections", // optional filter
  run: ({ ctx }) => ctx.hubCommand("rerun", {}),
});
```

`when`/`run` receive the same context declarative views use (hub queries and
commands, focus, busy). Everything a module registers through the facade is
unwound when the plugin deactivates.

**Trust model.** Registered renderers execute inside the console with
full page privileges — they are frontend code, never sandboxed. The
same rule dsh applies to its keyed renderers.

## Plugin client modules (runtime loading)

The frontend is homogeneous: a plugin's renderer module is frontend
code regardless of the plugin backend (in-process Go, out-of-process
executable, WASM). Modules are distributed with the plugin, served
same-origin by the console server, and loaded at boot — before first
render — by `lib/client-modules.ts`. Each module's default export is a
register function receiving a facade:

```js
// configs/client-modules/alarm-demo.js (no build step required)
export default function register(m) {
  const { React, antd, api, registerPageRenderer } = m;
  registerPageRenderer("alarm-console", function AlarmConsole() {
    // fully custom page: own layout, own state, data via the hub
    ...
  });
}
```

Declare modules on the `ui` component:

```toml
[[components.config.client_modules]]
name = "alarm-demo"
path = "./configs/client-modules/alarm-demo.js"
```

Loading is composition-governed: a module is declared by a `ui-client`
component (default entry `plugins/<name>/ui.js`), shows up in the plugin
explorer, honors the `enabled` switch, and uninstall/reconcile removes it
from the manifest — a file on disk never loads by itself. The server
publishes the manifest at `GET /api/ui/client-modules` and serves each
plugin directory same-origin under `/client-modules/<name>/`; the entry
URL is directory-shaped so vendored libraries load via plain relative
imports. Trust = operator install, exactly like the plugin binary
itself; an out-of-process plugin already runs with host privileges, so
its client module adds no new risk.

### Module lifecycle

The module manager (`lib/client-modules.ts`) reconciles the active module
set against the manifest on every `composition.changed` observation —
mirroring the runtime's activation semantics:

- **Disable/uninstall** removes the module from the manifest → the manager
  disposes the activation: the register function's returned dispose runs
  first (the "last effect", as `runtime/component.go` prescribes for the
  Apply-returned Cleanup), then every renderer the module registered
  through the facade unwinds LIFO. The page disappears without a reload.
- **Re-enable** re-executes `register` on the cached ESM — the module body
  stays in the browser's module cache; only the registrations re-run.
- **Content update** changes the manifest URL's `?v=<content hash>` → the
  old activation disposes and the module re-imports (in-session hot update).
- **Failure** (import or register throwing) rolls back what was already
  registered, is never remembered, and retries on the next reconcile —
  one broken module never blocks the console or its siblings. A failing
  manifest *fetch* keeps current activations: unknown desired state is not
  an empty desired state.

Contract: register through the facade and the framework owns the cleanup;
resources created outside the facade must be covered by the optional
dispose your register function returns. Full details:
`docs/guide/ui-plugin-lifecycle.md`.
