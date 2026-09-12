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
