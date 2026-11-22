using System.Text;
using System.Text.Json;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch 3 gap closure: file-backed bundles/patches, typed CLI schema and
/// help, synchronous config schema validation, an explicit configwatch tx
/// guard, content checksum verification, and lockfile/diff support for the
/// local market and package manager. Every test exercises the public surface
/// a host would use; file side effects are confined to a fresh temp directory.
/// </summary>
public sealed class DeclarationGapTests
{
    [Fact]
    public void BundleFileExpandsAndRejectsUnknown()
    {
        var directory = NewDirectory();
        try
        {
            var tomlPath = Path.Combine(directory, "bundles.toml");
            File.WriteAllText(tomlPath, """
                [bundles.base]
                [[bundles.base.components]]
                id = "a"
                type = "echo"
                enabled = true
                [[bundles.base.components]]
                id = "b"
                type = "echo"
                enabled = true

                [bundles.extra]
                [[bundles.extra.components]]
                id = "b"
                type = "stamp"
                enabled = true
                [[bundles.extra.components]]
                id = "c"
                type = "echo"
                enabled = true
                """);

            var library = new BundleLibrary();
            var loaded = library.LoadFile(tomlPath);
            Assert.Equal(new[] { "base", "extra" }, loaded);

            var expanded = library.Expand("base", "extra");
            Assert.Equal(new[] { "a", "b", "c" }, expanded.Select(manifest => manifest.Id));
            Assert.Equal("stamp", expanded.Single(manifest => manifest.Id == "b").Type);

            var jsonPath = Path.Combine(directory, "bundles.json");
            File.WriteAllText(jsonPath, """
                {
                  "bundles": {
                    "json-base": [
                      { "id": "j1", "type": "echo", "enabled": true, "config": { "value": "one" } }
                    ],
                    "json-extra": [
                      { "id": "j1", "type": "echo", "enabled": true, "config": { "value": "two" } },
                      { "id": "j2", "type": "echo", "enabled": false }
                    ]
                  }
                }
                """);
            var jsonLibrary = new BundleLibrary();
            jsonLibrary.LoadFile(jsonPath);
            var jsonExpanded = jsonLibrary.Expand("json-base", "json-extra");
            Assert.Equal(new[] { "j1", "j2" }, jsonExpanded.Select(manifest => manifest.Id));
            Assert.Equal("two", jsonExpanded[0].Config["value"]);

            var error = Assert.Throws<InvalidOperationException>(() => library.Expand("missing"));
            Assert.Contains("missing", error.Message, StringComparison.Ordinal);
            Assert.Contains("base", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliHelpAndSchema()
    {
        var cli = new CliDispatcher();
        var schema = new CliCommandSchema(
            "greet",
            "Greets a person",
            [
                new CliOption("name", "person to greet", Required: true),
                new CliOption("loud", "shout the greeting", Default: "false"),
            ],
            ["hello"]);

        cli.RegisterTyped(schema,
            args => $"Hello {args["name"]}{(args["loud"] == "true" ? "!" : ".")}");

        Assert.Equal("Hello Ada.", cli.Dispatch("greet --name Ada"));
        Assert.Equal("Hello Ada!", cli.Dispatch("hello --name=Ada --loud true"));

        var missing = Assert.Throws<CliSchemaException>(() => cli.Dispatch("greet"));
        Assert.Contains("name", missing.Message, StringComparison.Ordinal);
        Assert.Contains("required", missing.Message, StringComparison.OrdinalIgnoreCase);

        var unknown = Assert.Throws<CliSchemaException>(() => cli.Dispatch("greet --name Ada --bogus x"));
        Assert.Contains("bogus", unknown.Message, StringComparison.Ordinal);

        var help = cli.Help();
        Assert.Contains("greet", help, StringComparison.Ordinal);
        Assert.Contains("hello", help, StringComparison.Ordinal);
        Assert.Contains("Greets a person", help, StringComparison.Ordinal);
        Assert.Contains("--name", help, StringComparison.Ordinal);
        Assert.Contains("person to greet", help, StringComparison.Ordinal);
        Assert.Contains("required", help, StringComparison.OrdinalIgnoreCase);

        var commandHelp = cli.Help("greet");
        Assert.Contains("--loud", commandHelp, StringComparison.Ordinal);
        Assert.Contains("shout the greeting", commandHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigSchemaRejectsInvalidDeclarationWithNoFiberChange()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);

        var schema = new ConfigSchema
        {
            Type = "object",
            Required = ["value"],
            Properties = new Dictionary<string, ConfigSchema>
            {
                ["value"] = new ConfigSchema { Type = "string" },
                ["batch"] = new ConfigSchema { Type = "integer" },
            },
        };
        registry.RegisterValidatedFactory("echo", schema,
            config => new Echo((string)config["value"]!));

        await registry.ApplyAsync([Manifest("a", "echo", true, "A", 4)]);
        var fiberBefore = registry.Entries["a"].Fiber;
        var before = Snapshot(registry);

        var invalid = Manifest("b", "echo", true, "B", "not-an-integer");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ApplyAsync([Manifest("a", "echo", true, "A", 4), invalid]));
        Assert.Contains("factory for 'b' failed", error.Message, StringComparison.Ordinal);
        Assert.IsType<ConfigSchemaException>(error.InnerException);

        Assert.Equal(before, Snapshot(registry));
        Assert.Same(fiberBefore, registry.Entries["a"].Fiber);
    }

    [Fact]
    public void ConfigSchemaEmitsNestedEnumAndDefault()
    {
        var schema = new ConfigSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ConfigSchema>
            {
                ["mode"] = new ConfigSchema
                {
                    Type = "object",
                    Default = new Dictionary<string, object?> { ["kind"] = "fast" },
                    Properties = new Dictionary<string, ConfigSchema>
                    {
                        ["kind"] = new ConfigSchema
                        {
                            Type = "string",
                            Enum = ["fast", "slow"],
                            Default = "fast",
                        },
                        ["level"] = new ConfigSchema
                        {
                            Type = "integer",
                            Enum = [1, 2, 3],
                            Default = 1,
                        },
                    },
                },
                ["tags"] = new ConfigSchema
                {
                    Type = "array",
                    Items = new ConfigSchema
                    {
                        Type = "string",
                        Enum = ["a", "b"],
                        Default = "a",
                    },
                },
            },
        };

        // Both the root document and every nested object/items node carry the
        // full vocabulary: enum and default must not disappear below depth 1.
        using var document = JsonDocument.Parse(schema.ToJsonSchema());
        var root = document.RootElement;
        var mode = root.GetProperty("properties").GetProperty("mode");
        Assert.True(mode.TryGetProperty("default", out _));
        var kind = mode.GetProperty("properties").GetProperty("kind");
        Assert.Contains("fast", kind.GetProperty("enum").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("fast", kind.GetProperty("default").GetString());
        var level = mode.GetProperty("properties").GetProperty("level");
        Assert.Equal(1, level.GetProperty("default").GetInt32());
        var items = root.GetProperty("properties").GetProperty("tags").GetProperty("items");
        Assert.True(items.TryGetProperty("enum", out _));
        Assert.Equal("a", items.GetProperty("default").GetString());

        // Missing optional properties are materialized from their default,
        // recursively, without overwriting values the declaration supplied.
        var config = new Dictionary<string, object?>
        {
            ["mode"] = new Dictionary<string, object?> { ["kind"] = "slow" },
        };
        schema.Validate(config);
        var filledMode = Assert.IsType<Dictionary<string, object?>>(config["mode"]);
        Assert.Equal("slow", filledMode["kind"]);
        Assert.Equal(1, filledMode["level"]);

        // A numeric enum must compare by value across CLR numeric types
        // (1L satisfies an enum declared with int 1), and still reject 9L.
        schema.Validate(new Dictionary<string, object?>
        {
            ["mode"] = new Dictionary<string, object?> { ["level"] = 1L },
        });
        var error = Assert.Throws<ConfigSchemaException>(() => schema.Validate(new Dictionary<string, object?>
        {
            ["mode"] = new Dictionary<string, object?> { ["level"] = 9L },
        }));
        Assert.Contains("enum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigWatcherTxGuardCoversReconcileAndDoesNotAdvanceRevisionOnFailure()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, ManifestToml("a"));
        var order = new List<string>();
        var depth = 0;
        var shouldFail = false;

        try
        {
            using var watcher = new ConfigWatcher(path, _ =>
            {
                order.Add("reconcile");
                order.Add("post");
                return shouldFail
                    ? Task.FromException(new InvalidOperationException("reconcile boom"))
                    : Task.CompletedTask;
            }, async operation =>
            {
                depth++;
                order.Add("guard-enter");
                try
                {
                    await operation();
                }
                finally
                {
                    order.Add("guard-exit");
                    depth--;
                }
            });

            Assert.True(await watcher.ReconcileOnceAsync());
            Assert.Equal(1, watcher.Revision);
            Assert.Equal(new[] { "guard-enter", "reconcile", "post", "guard-exit" }, order);
            Assert.Equal(0, depth);

            shouldFail = true;
            Assert.False(await watcher.ReconcileOnceAsync());
            Assert.Equal(1, watcher.Revision);
            Assert.Equal(0, depth);
            Assert.IsType<InvalidOperationException>(watcher.LastError);

            shouldFail = false;
            Assert.True(await watcher.ReconcileOnceAsync());
            Assert.Equal(2, watcher.Revision);
            Assert.Equal(0, depth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PatchFileAtomicFailureLeavesNoChange()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "patch.json");
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "patches": [
                    { "op": "remove", "id": "b" },
                    { "op": "replace", "id": "a", "component": { "id": "a", "type": "echo", "enabled": true, "config": { "value": "new" } } },
                    { "op": "insert", "id": "c", "component": { "id": "c", "type": "echo", "enabled": true } }
                  ]
                }
                """);

            var patches = PatchFile.Load(path);
            var baseRows = new[]
            {
                Manifest("a", "echo", true, "old"),
                Manifest("b", "echo", true, "b"),
            };
            var applied = PatchFile.Apply(baseRows, patches);
            Assert.Equal(new[] { "a", "c" }, applied.Select(manifest => manifest.Id));
            Assert.Equal("new", applied[0].Config["value"]);
            Assert.Equal(new[] { "a", "b" }, baseRows.Select(manifest => manifest.Id));
            Assert.Equal("old", baseRows[0].Config["value"]);

            var badPath = Path.Combine(directory, "bad.json");
            File.WriteAllText(badPath, """
                {
                  "version": 1,
                  "patches": [
                    { "op": "remove", "id": "b" },
                    { "op": "replace", "id": "missing", "component": { "id": "missing", "type": "echo", "enabled": true } }
                  ]
                }
                """);
            var bad = PatchFile.Load(badPath);
            var error = Assert.Throws<InvalidOperationException>(() => PatchFile.Apply(baseRows, bad));
            Assert.Contains("missing", error.Message, StringComparison.Ordinal);
            Assert.Equal(new[] { "a", "b" }, baseRows.Select(manifest => manifest.Id));

            var badVersion = Path.Combine(directory, "v2.json");
            File.WriteAllText(badVersion, """{"version": 2, "patches": []}""");
            Assert.Throws<ManifestParseException>(() => PatchFile.Load(badVersion));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ChecksumMismatchRejected()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "plugin.bin");
            var content = Encoding.UTF8.GetBytes("payload-v1");
            File.WriteAllBytes(path, content);
            var checksum = PluginManifestChecksum.Compute(content);

            var good = Manifest("p", "echo", true, "x") with { Checksum = checksum };
            PluginManifestChecksum.VerifyFile(good, path);
            PluginManifestChecksum.VerifyFile(good with { Checksum = "sha256:" + checksum }, path);

            var bad = good with { Checksum = new string('0', 64) };
            var error = Assert.Throws<ChecksumMismatchException>(
                () => PluginManifestChecksum.VerifyFile(bad, path));
            Assert.Contains("p", error.Message, StringComparison.Ordinal);
            Assert.Contains("plugin.bin", error.Message, StringComparison.Ordinal);
            Assert.False(PluginManifestChecksum.Matches(bad, content));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MarketInstallUpgradeUninstallAndPackageLockDiff()
    {
        var directory = NewDirectory();
        try
        {
            var market = new MarketService();
            var v1 = Manifest("alpha", "echo", true, "1") with { Version = "1.0.0" };
            var v2 = v1 with { Version = "2.0.0" };
            var artifact = Path.Combine(directory, "alpha.bin");
            var payload = Encoding.UTF8.GetBytes("alpha-v2");
            File.WriteAllBytes(artifact, payload);
            var sum = PluginManifestChecksum.Compute(payload);
            var entry = new MarketService.CatalogEntry(v2, sum, artifact);

            Assert.Throws<InvalidOperationException>(() => market.Install([v1], entry));
            var installed = market.Install([], entry);
            Assert.Equal(new[] { "alpha" }, installed.Select(manifest => manifest.Id));

            var upgraded = market.Upgrade([v1], entry);
            Assert.Equal("2.0.0", upgraded[0].Version);
            Assert.Equal("1.0.0", v1.Version);
            Assert.Throws<InvalidOperationException>(() =>
                market.Upgrade([v1], new MarketService.CatalogEntry(v1, sum, artifact)));

            var removed = market.Uninstall(upgraded, "alpha");
            Assert.Empty(removed);
            Assert.Throws<InvalidOperationException>(() => market.Uninstall(upgraded, "missing"));

            var lockPath = Path.Combine(directory, "lock.json");
            PackageLockfile.Write(lockPath, installed);
            var locked = PackageLockfile.Read(lockPath);
            Assert.Equal("alpha", Assert.Single(locked).Id);

            var manager = new PackageManager();
            var transaction = manager.Begin([v1]);
            var after = transaction.Apply([
                new PackageManager.Change("update", v2),
                new PackageManager.Change("add",
                    Manifest("beta", "echo", true, "b") with { Version = "1.0.0" }),
            ]);
            Assert.Equal(new[] { "alpha", "beta" }, after.Select(manifest => manifest.Id));
            Assert.Equal(new[] { "beta" }, transaction.Diff.Added);
            Assert.Equal(new[] { "alpha" }, transaction.Diff.Updated);
            var detail = Assert.Single(transaction.Diff.Details);
            Assert.Equal("1.0.0", detail.Before);
            Assert.Equal("2.0.0", detail.After);

            var restored = transaction.Rollback();
            Assert.Equal(new[] { "alpha" }, restored.Select(manifest => manifest.Id));
            Assert.Equal("1.0.0", restored[0].Version);

            var failing = manager.Begin([v1]);
            Assert.Throws<InvalidOperationException>(() => failing.Apply([
                new PackageManager.Change("add", v1),
            ]));
            Assert.Equal(new[] { "alpha" }, failing.Current.Select(manifest => manifest.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PluginManifest Manifest(string id, string type, bool enabled, string value, object? batch = null)
    {
        var config = new Dictionary<string, object?> { ["value"] = value };
        if (batch is not null)
        {
            config["batch"] = batch;
        }

        return new PluginManifest(id, type, enabled, config, [], []);
    }

    private static string ManifestToml(string id) =>
        $"[[components]]\nid = \"{id}\"\ntype = \"echo\"\nenabled = true\n";

    private static string Snapshot(PluginRegistry registry) => string.Join(
        "|", registry.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value.ConfigurationKey}"));

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-declaration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class Echo : Component
    {
        private readonly Key<string> _key;
        private readonly string _value;

        public Echo(string value)
        {
            _value = value;
            _key = new Key<string>($"declaration-gap-echo-{value}");
        }

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _value);
            return null;
        }
    }
}
