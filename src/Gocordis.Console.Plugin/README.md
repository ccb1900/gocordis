# Built-in Console plugin

`gocordis-console` is a **product-owned plugin**, not a sample and not host UI
code. The host owns only the web transport, static console assets, the control
API and `/recover`. Everything console-specific ships in this package:

- `Gocordis.Console.Plugin.dll` — a real managed `Component` whose API query and
  command are reversible effects of one Core fiber;
- `ui.js` — TSX compiled by `@gocordis/plugin-kit` into one same-origin ESM;
  React/antd come from the console facade shims, so there is one React instance;
- `gocordis-ui.json` + `ui/**` — page, panel, client module, route, navigation,
  extension and resource declarations bound by `UiPackageBinder`.

Installing the package installs the UI; disabling or uninstalling it withdraws
the UI and API contributions in strict LIFO order. The host works without it
(`/recover` and the control API remain), and the package can be replaced by
another console implementation through the same control plane.

## Rebuild

```bash
bash src/Gocordis.Console.Plugin/build-and-pack.sh
```

The script builds the managed component and TSX frontend, stages a real payload
and writes `builtin/gocordis-console-1.0.0.gocordis-plugin`.

## Install

```bash
# offline store install
dotnet tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll \
  plugin install --builtin console --store <store> --host-version 1.0.0

# running product, through the one HTTP control plane
dotnet tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll \
  console install --builtin console --address http://127.0.0.1:8170
```

Deployments may point `GOCORDIS_BUILTIN_PACKAGES` at a directory containing the
same package file instead of using the checked-in build output.
