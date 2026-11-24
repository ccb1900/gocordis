#!/usr/bin/env bash
# Full plugin developer workflow from an EMPTY directory, using only the CLI
# and the packaged SDK (no path into the runtime repository in the plugin).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${1:-/tmp/gocordis-dev-e2e}"
rm -rf "$OUT"
mkdir -p "$OUT/feed" "$OUT/work"
# Every run packs the SDK anew. Reusing 1.0.0 would let NuGet's global cache
# keep an older package from a previous run, so the generated plugin compiles
# against a stale Core and fails at activation with MissingMethodException.
# A per-run prerelease version makes the local feed authoritative.
SDK_VERSION="1.0.0-e2e.$(date +%s%N)"
CLI=(dotnet run --project "$ROOT/tools/Gocordis.Cli/Gocordis.Cli.csproj" --no-build --)

# Match the host's Debug build: SharedAssemblyRegistry seals by full DLL
# identity (MVID/SHA), so a Release Gocordis.Extensions.dll would conflict with
# the host's Debug copy in the default ALC.
for project in Core Plugins Extensions Plugin.Sdk; do
  env -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
    dotnet pack "$ROOT/src/Gocordis.$project/Gocordis.$project.csproj" \
      -c Debug -o "$OUT/feed" -p:PackageVersion="$SDK_VERSION" --nologo -v:q
done

# 1) create a plugin project in an empty directory
created=$("${CLI[@]}" plugin create "$OUT/work/demo.plugin" \
  --id demo.plugin --name "Demo Plugin" --type demo.plugin --version 1.0.0 \
  --feed "$OUT/feed" --sdk-version "$SDK_VERSION")
echo "$created"
ENTRY="$(printf '%s' "$created" | python3 -c 'import json,sys; print(json.load(sys.stdin)["entryType"])')"

# The generated plugin consumes a real official extension capability, so this
# workflow ALSO proves the packaged-SDK + --shared-assembly identity path.
cat > "$OUT/work/demo.plugin/Plugin.cs" <<'CS'
namespace demo_plugin;

public sealed class Plugin : Component
{
    public override string Name => "Demo Plugin";

    public override IReadOnlyList<ICapabilityKey> Inject() => [ExtensionKeys.Timer];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        _ = context.Require(ExtensionKeys.Timer);
        return ValueTask.FromResult<Cleanup?>(null);
    }
}
CS

# 2) develop + build (SDK comes from the generated local NuGet.Config)
(cd "$OUT/work/demo.plugin" && dotnet build --nologo -v:q)

# 3) local authoring preflight
"${CLI[@]}" plugin validate "$OUT/work/demo.plugin"

# 4) pack a standard package. SDK package dependencies are not copied into a
#    classlib's output, so stage the shared identity DLL explicitly (the host
#    and this package must carry byte-identical Gocordis.Extensions.dll).
cp "$ROOT/src/Gocordis.Extensions/bin/Debug/net10.0/Gocordis.Extensions.dll" \
  "$OUT/work/demo.plugin/bin/Debug/net10.0/Gocordis.Extensions.dll"

"${CLI[@]}" plugin pack "$OUT/work/demo.plugin/bin/Debug/net10.0" \
  "$OUT/demo.plugin.gocordis-plugin" \
  --id demo.plugin --version 1.0.0 --type demo.plugin \
  --backend managed --entry-assembly demo.plugin.dll --entry-type "$ENTRY" \
  --shared-assembly Gocordis.Extensions \
  --capability 'require|gocordis.extension/timer|endpoint|1.0.0|application/json|'

# 5) create an application, install the plugin, declare it, validate the host
"${CLI[@]}" create "$OUT/app" --id devapp --version 1.0.0 --host-version 1.0.0 >/dev/null
"${CLI[@]}" plugin install "$OUT/demo.plugin.gocordis-plugin" \
  --store "$OUT/store" --host-version 1.0.0 >/dev/null
"${CLI[@]}" app add "$OUT/app/gocordis.app.json" demo.plugin \
  --range 1.0.0 --store "$OUT/store" --host-version 1.0.0 >/dev/null
"${CLI[@]}" app run "$OUT/app/gocordis.app.json" --store "$OUT/store" --validate-only

echo "PLUGIN DEVELOPER WORKFLOW OK: $OUT"
