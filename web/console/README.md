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

**Trust boundary.** Registered renderers execute inside the console with
full page privileges. Only register code you would ship as a dependency —
the same rule dsh applies to its keyed renderers.
