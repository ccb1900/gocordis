using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Control-plane boundary: a runtime install cannot widen the host-owned
/// contract set, and the failure is an explicit restart requirement rather
/// than a generic loader conflict.
/// </summary>
public sealed class ControlBoundaryTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task RuntimeInstallOfUnsealedSharedContractRequiresHostRestart()
    {
        using var temp = new TempDir();
        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("boundary-app", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
        var store = new PluginStore(temp.At("store"), HostVersion);
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var control = new PluginControlService(
            manifestPath, store, registry,
            SharedAssemblyApproval: names => names.All(name => name == "Gocordis.Core"));

        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "Unsealed.Contract.dll"), "contract");
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "plugin");
        var package = temp.At("sealed.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "unsealed-consumer", "1.0.0", "unsealed-consumer", "[1.0,)",
            Backend: "builtin",
            SharedAssemblies: ["Unsealed.Contract"]), package);

        var upload = await control.UploadAsync(File.OpenRead(package), "sealed.gocordis-plugin");
        var result = await control.InstallAsync(upload.UploadKey);

        Assert.False(result.Accepted);
        Assert.Equal("host_restart_required", result.Code);
        Assert.Contains("Unsealed.Contract", result.Error);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task InstalledVersionMatchesTheHighestResolvedPackageNotTheFirstListed()
    {
        using var temp = new TempDir();
        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("versions-app", "1.0.0", HostVersion,
            [new ApplicationPlugin("multi-version", "[1.0,3.0)", true)]).SaveAsync(manifestPath);
        var store = new PluginStore(temp.At("store"), HostVersion);
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var control = new PluginControlService(manifestPath, store, registry);

        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var source = temp.At("source-" + version);
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), version);
            var package = temp.At($"multi-version-{version}.gocordis-plugin");
            await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
                "multi-version", version, "multi-version", "[1.0,)"), package);
            await store.InstallAsync(package);
        }

        var entries = await control.ListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("[1.0,3.0)", entry.Version);
        Assert.Equal("2.0.0", entry.InstalledVersion);
    }

    [Fact]
    public async Task DeclaredCapabilitiesAreSeparateFromPackageDependencies()
    {
        using var temp = new TempDir();
        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("dto-app", "1.0.0", HostVersion,
            [new ApplicationPlugin("dto-plugin", "1.0.0", true)]).SaveAsync(manifestPath);
        var store = new PluginStore(temp.At("store"), HostVersion);
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var control = new PluginControlService(manifestPath, store, registry);

        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("dto-plugin.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "dto-plugin", "1.0.0", "dto-plugin", "[1.0,)",
            Backend: "builtin",
            Capabilities: ["require|org.example/a|endpoint||application/json|"],
            Dependencies: []), package);
        await store.InstallAsync(package);

        var entry = Assert.Single(await control.ListAsync());
        Assert.Empty(entry.Requires); // package dependency ids stay separate
        Assert.Equal(
            ["require|org.example/a|endpoint||application/json|"],
            entry.DeclaredCapabilityRequires);

        var view = await control.DependenciesAsync("dto-plugin");
        Assert.NotNull(view);
        Assert.Contains("require|org.example/a|endpoint||application/json|", view!.DeclaredCapabilityRequires);
        Assert.Empty(view.DeclaredCapabilityProvides);
    }

    [Fact]
    public async Task ConflictingConfigSchemaForOnePluginTypeIsRejected()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterSchema("demo", JsonNode.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}}}""")!);

        Assert.Throws<ConfigSchemaException>(() => registry.RegisterSchema("demo", JsonNode.Parse(
            """{"type":"object","properties":{"value":{"type":"integer"}}}""")!));

        // Re-registering the same document with a different property order is a
        // no-op: schema identity is canonical, not textual.
        registry.RegisterSchema("demo", JsonNode.Parse(
            """{"properties":{"value":{"type":"string"}},"type":"object"}""")!);
    }

    [PosixOnlyFact]
    public async Task FailedActivationRollsBackStoreManifestRegistryAndCatalogTogether()
    {
        using var temp = new TempDir();
        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("consistency-app", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
        var store = new PluginStore(temp.At("store"), HostVersion);
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        var catalog = new CapabilityCatalog();
        async Task<IDeclarationTransaction> PlanAsync(IReadOnlyList<string> wire)
        {
            var previous = catalog.Declarations;
            try
            {
                catalog.Replace(wire.Select(CapabilityDeclaration.Parse));
                catalog.ValidateCompatibility();
            }
            catch
            {
                catalog.Replace(previous);
                throw;
            }

            return new TestDeclarationTransaction(catalog, previous);
        }

        var control = new PluginControlService(manifestPath, store, registry,
            ComponentFactory: manifest => manifest.Backend == "process"
                ? new Gocordis.Runtime.Process.ProcessCapabilityComponent(
                    Path.Combine(manifest.Source!, manifest.EntryAssembly!), catalog,
                    [CapabilityDeclaration.Parse(
                        "provide|org.example.dead/value|endpoint|1.0.0|text/plain|")])
                : throw new InvalidOperationException("unreachable"),
            DeclarationPlan: PlanAsync);

        var source = temp.At("source");
        Directory.CreateDirectory(source);
        var script = Path.Combine(source, "plugin.py");
        await File.WriteAllTextAsync(script, "#!/usr/bin/env python3\nimport sys\nsys.exit(1)\n");
        PlatformFiles.MarkExecutable(script);
        var package = temp.At("dead.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "dead-provider", "1.0.0", "dead-provider", "[1.0,)",
            Backend: "process", EntryAssembly: "plugin.py",
            Capabilities: ["provide|org.example.dead/value|endpoint|1.0.0|text/plain|"]), package);

        var upload = await control.UploadAsync(File.OpenRead(package), "dead.gocordis-plugin");
        var result = await control.InstallAsync(upload.UploadKey);

        Assert.False(result.Accepted);
        // The whole transaction closes: manifest bytes, store versions, runtime
        // declarations and the capability catalog are back to the baseline.
        Assert.True((await File.ReadAllBytesAsync(manifestPath)).SequenceEqual(manifestBytes));
        Assert.Empty(await store.ListAsync());
        Assert.Empty(registry.Entries);
        Assert.DoesNotContain(runtime.Snapshot().Fibers, fiber =>
            fiber.Name.StartsWith("process-", StringComparison.Ordinal));
        Assert.Empty(catalog.Declarations);
        Assert.False(catalog.IsDeclared(new CapabilityId("org.example.dead", "value")));
    }

    [PosixOnlyFact]
    public async Task TransactionBaselineDetectsAnUnrestoredDeclarationFingerprint()
    {
        using var temp = new TempDir();
        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("fingerprint-app", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
        var store = new PluginStore(temp.At("store"), HostVersion);
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);

        var reads = 0;
        var control = new PluginControlService(manifestPath, store, registry,
            ComponentFactory: manifest => new Gocordis.Runtime.Process.ProcessCapabilityComponent(
                Path.Combine(manifest.Source!, manifest.EntryAssembly!), new CapabilityCatalog(),
                [CapabilityDeclaration.Parse(
                    "provide|org.example.dead/value|endpoint|1.0.0|text/plain|")]))
        {
            DeclarationFingerprint = () => (++reads).ToString(),
        };

        var source = temp.At("source");
        Directory.CreateDirectory(source);
        var script = Path.Combine(source, "plugin.py");
        await File.WriteAllTextAsync(script, "#!/usr/bin/env python3\nimport sys\nsys.exit(1)\n");
        PlatformFiles.MarkExecutable(script);
        var package = temp.At("dead.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "dead-fp", "1.0.0", "dead-fp", "[1.0,)",
            Backend: "process", EntryAssembly: "plugin.py",
            Capabilities: ["provide|org.example.dead/value|endpoint|1.0.0|text/plain|"]), package);
        var upload = await control.UploadAsync(File.OpenRead(package), "dead.gocordis-plugin");

        var result = await control.InstallAsync(upload.UploadKey);
        Assert.False(result.Accepted);
        Assert.Contains("capability declarations differ after rollback", result.Error);
    }

    private sealed class TestDeclarationTransaction : IDeclarationTransaction
    {
        private readonly CapabilityCatalog _catalog;
        private readonly IReadOnlyList<CapabilityDeclaration> _previous;

        public TestDeclarationTransaction(
            CapabilityCatalog catalog, IReadOnlyList<CapabilityDeclaration> previous)
        {
            _catalog = catalog;
            _previous = previous;
        }

        public Task RollbackAsync()
        {
            _catalog.Replace(_previous);
            return Task.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-control-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
