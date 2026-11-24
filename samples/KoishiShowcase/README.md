# KoishiShowcase — paper §5.3 case study

One host vocabulary, four independently packaged plugins, and a browser console
that is a **second Cordis application** rather than a server UI shell.

```
Contracts (shared, host-sealed)  ─┬─ Vocabulary: provides Commands + Message
                                   ├─ Database:   provides IDatabase
                                   ├─ Adapter:    injects IDatabase, provides IChatAdapter
                                   └─ Feature:    injects Adapter/Commands/Message/ApiGateway,
                                                  registers echo command + query as effects
```

Dependency layers stay distinct: `koishi.database@[1.0,2.0)` in the adapter
package is a **package dependency**; `Inject(KoishiKeys.Database)` is the
**capability dependency** that actually gates activation; both plugins carry
`--shared-assembly Koishi.Contracts` so `Key<T>` has one host-owned identity
(**contract dependency**).

## Run

```bash
bash samples/KoishiShowcase/build-and-run.sh /tmp/gocordis-koishi-showcase
bash samples/KoishiShowcase/build-and-run.sh /tmp/gocordis-koishi-showcase --run
```

The script builds the five projects plus the CLI, packs four real
`.gocordis-plugin` packages, installs them into a fresh store and writes
`gocordis.app.json`. With `--run` it starts the web platform; the console URL is
printed at startup.

## Observable verification

- All four declared fibers are `Active`; `GET /api/query/koishi/echo` returns
  `{"value":"row:demo:ping"}` — the command calls the adapter, which calls the
  database across the three shared typed contracts.
- Disable `koishi.database` through `/api/plugins/control`: adapter and feature
  become `Pending`, vocabulary stays `Active`, and the echo query is withdrawn.
- Re-enable it: both fibers reactivate and the query returns. No restart, no
  package upgrade, no manual startup ordering.
- `samples/KoishiShowcase/ConsoleApp` starts its own `Runtime`, `PluginRegistry`
  and fiber set; it reaches the server only through `ConsoleClient` HTTP calls.
  It serves its own `koishi-console` page at `/koishi` and the
  `/api/query/koishi/server` query.

`tests/Gocordis.Product.Tests/KoishiPackagedProductTests.cs` drives this chain
end to end against the real packages.
