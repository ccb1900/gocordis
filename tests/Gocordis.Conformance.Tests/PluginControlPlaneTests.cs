using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;
using Xunit.Sdk;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Real HTTP end-to-end coverage for the plugin control plane. Every state
/// change goes through WebUiServer routes with an HttpClient; no test calls
/// PluginControlService / PluginStore / PluginRegistry directly after setup.
/// The fixtures only build packages and wire the same objects a product host
/// would hand to the server.
/// </summary>
public sealed class PluginControlPlaneTests
{
    private const string HostVersion = "1.5.0";
    private const string RangeV1 = "[1.0,2.0)";
    private const string RangeV2 = "[2.0,3.0)";
    private const string RangeWide = "[1.0,3.0)";
    private const string RangeOldOnly = "[1.0,1.5)";

    private static readonly Key<string> DemoCapability = new("demo.capability");

    // -------------------------------------------------------------------------
    // 1) install -> declaration + active fiber
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InstallPersistsDeclarationAndActivatesPlugin()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");

        var install = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true }));
        AssertAccepted(install, "install alpha");
        Assert.Equal("alpha", Str(install.Data, "id"));

        var plugins = await GetDataArrayAsync(fx.Client, "/api/plugins");
        var entry = AssertPlugin(plugins, "alpha");
        Assert.Equal("Active", Str(entry, "state"));
        AssertBool(entry, "enabled", true);
        AssertBool(entry, "declared", true);

        // The declaration is persisted as an ApplicationManifest, not runtime state.
        Assert.True(File.Exists(fx.ManifestPath), "install must write the application manifest");
        Assert.Contains("alpha", await File.ReadAllTextAsync(fx.ManifestPath));
        var app = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        Assert.Contains(app.Plugins, plugin => plugin.Id == "alpha" && plugin.Enabled);

        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        AssertContainsStoreEntry(store, "alpha", "1.0.0");
    }

    // -------------------------------------------------------------------------
    // 2) config + enable/disable round trip, persisted across a control restart
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfigureEnableDisablePersists()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true })), "install alpha");

        // config write -> declaration on disk + fiber reloaded with the new config.
        var configured = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/alpha/config",
            new JsonObject { ["config"] = new JsonObject { ["value"] = "first" } }));
        AssertAccepted(configured, "configure alpha");
        var app = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        var declared = Assert.Single(app.Plugins, plugin => plugin.Id == "alpha");
        Assert.Equal("first", DeclaredConfigValue(declared));

        // disable -> fiber gone, desired declaration false and persisted.
        var disabled = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "alpha", ["enable"] = false }));
        AssertAccepted(disabled, "disable alpha");
        var disabledEntry = AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha");
        AssertBool(disabledEntry, "enabled", false);
        Assert.Contains(Str(disabledEntry, "state"), new[] { "Gone", "disabled", "Pending" });
        Assert.False(Assert.Single((await ApplicationManifest.LoadAsync(fx.ManifestPath)).Plugins,
            plugin => plugin.Id == "alpha").Enabled);

        // re-enable -> a fresh active fiber, declaration true on disk.
        var enabled = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "alpha", ["enable"] = true }));
        AssertAccepted(enabled, "enable alpha");
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha"), "state"));

        // Restart the control objects over the same manifest path. The declaration
        // (not any runtime snapshot) is the recovery source.
        await using var runtime2 = new Runtime();
        await using var registry2 = new PluginRegistry(runtime2);
        registry2.RegisterFactory("demo", _ => new NoopComponent("demo"));
        _ = new PluginControlService(fx.ManifestPath, fx.Store, registry2);
        var persisted = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        var restored = Assert.Single(persisted.Plugins, plugin => plugin.Id == "alpha");
        Assert.True(restored.Enabled);
        Assert.Equal("first", DeclaredConfigValue(restored));
    }

    // -------------------------------------------------------------------------
    // 3) typed dependencies / dependents are exposed over HTTP
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DependenciesReflectTypedBindings()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerPackage = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var providerKey = await UploadAndGetKeyAsync(fx.Client, providerPackage, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = providerKey, ["enable"] = true })), "install provider");

        var consumerPackage = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeV1)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumerPackage, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "provider"), "state"));
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "consumer"), "state"));

        var dependencyView = (await GetDataAsync(fx.Client, "/api/plugins/consumer/dependencies")) as JsonObject
                             ?? throw new XunitException("consumer dependency view must be an object");
        Assert.Equal("consumer", Str(dependencyView, "id"));

        var packageDependencies = dependencyView["packageDependencies"] as JsonArray
                                  ?? throw new XunitException("packageDependencies must be an array");
        var dependency = Assert.Single(packageDependencies.OfType<JsonObject>(), item => Str(item, "id") == "provider");
        Assert.Equal(RangeV1, Str(dependency, "versionRange"));
        Assert.Equal("1.0.0", Str(dependency, "installedVersion"));
        AssertBool(dependency, "satisfied", true);

        // Declared capability requires are projected from the consumer's package
        // dependency ids; the provider advertises the typed capability itself.
        Assert.Contains("provider", Strings(dependencyView, "capabilityRequires"));

        // A package dependency also makes the consumer a dependent of the provider.
        var providerView = (await GetDataAsync(fx.Client, "/api/plugins/provider/dependencies")) as JsonObject
                           ?? throw new XunitException("provider dependency view must be an object");
        Assert.Contains("consumer", Strings(providerView, "dependents"));
        Assert.Contains(DemoCapability.Name, Strings(providerView, "capabilityProvides"));
    }

    // -------------------------------------------------------------------------
    // 4) upgrade + uninstall close the package/declaration/fiber loop
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpgradeAndUninstallCloseLoop()
    {
        await using var fx = await Fixture.CreateAsync();

        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha v1");

        var generationBefore = fx.GenerationOf("demo");

        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        var upgraded = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "alpha", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertAccepted(upgraded, "upgrade alpha");

        var upgradedEntry = AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha");
        Assert.Equal("Active", Str(upgradedEntry, "state"));
        Assert.Contains("2.0.0", new[] { Str(upgradedEntry, "version"), Str(upgradedEntry, "installedVersion") });
        Assert.False(string.IsNullOrWhiteSpace(Str(upgradedEntry, "installedVersion")),
            "installedVersion must identify the installed package");
        Assert.True(fx.GenerationOf("demo") > generationBefore,
            "upgrade must retire the old fiber and activate a new generation");

        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        AssertContainsStoreEntry(store, "alpha", "2.0.0");
        Assert.DoesNotContain(store.OfType<JsonObject>(),
            item => Str(item, "id") == "alpha" && Str(item, "version") == "1.0.0");
        Assert.Contains(RangeV2, await File.ReadAllTextAsync(fx.ManifestPath));

        var removed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "alpha", ["version"] = "2.0.0" }));
        AssertAccepted(removed, "uninstall alpha");

        Assert.Null(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha"));
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
            item => Str(item, "id") == "alpha");
        Assert.DoesNotContain(await File.ReadAllTextAsync(fx.ManifestPath), "alpha", StringComparison.Ordinal);
        Assert.All(fx.Runtime.Snapshot().Fibers,
            fiber => Assert.NotEqual(FiberState.Active, fiber.State));
    }

    // -------------------------------------------------------------------------
    // 5) failures are visible as codes and leave declarations/store untouched
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FailuresAreObservableAndNonDestructive()
    {
        await using var fx = await Fixture.CreateAsync();

        var storeBefore = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        Assert.Empty(storeBefore);

        // A corrupt upload is rejected at the package boundary.
        var badUpload = await ReadEnvelopeAsync(await UploadAsync(fx.Client,
            Encoding.UTF8.GetBytes("not a plugin package"), "broken.gocordis-plugin"));
        Assert.Equal("invalid_package", Code(badUpload));
        Assert.Empty(await GetDataArrayAsync(fx.Client, "/api/plugins/store"));

        // Establish one good declaration for the non-destructive checks.
        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true })), "install alpha");

        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var storeBaseline = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        Assert.Single(storeBaseline);

        // Duplicate install reports a code instead of corrupting the store.
        var duplicate = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true }));
        Assert.Contains(Code(duplicate), new[] { "conflict", "invalid_request", "not_found" });

        // Unknown ids report not_found for both control and uninstall.
        var unknownControl = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "missing", ["enable"] = false }));
        Assert.Equal("not_found", Code(unknownControl));
        var unknownUninstall = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "missing" }));
        Assert.Equal("not_found", Code(unknownUninstall));

        // A rejected config write must not be persisted.
        var invalidConfig = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/alpha/config",
            new JsonObject { ["config"] = "not-an-object" }));
        Assert.Contains(Code(invalidConfig), new[] { "invalid_config", "invalid_request" });
        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));

        // Every failure above was non-destructive.
        var survivor = AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha");
        Assert.Equal("Active", Str(survivor, "state"));
        Assert.Single(await GetDataArrayAsync(fx.Client, "/api/plugins/store"));
    }

    // -------------------------------------------------------------------------
    // 6) SSE plugin-state frame
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PluginStateEventIsPublishedOverSse()
    {
        await using var fx = await Fixture.CreateAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventTask = ReadPluginStateEventAsync(fx.Client, timeout.Token, connected);
        await connected.Task.WaitAsync(timeout.Token);

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true })), "install alpha");
        await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "alpha", ["enable"] = false });

        var frame = await eventTask;
        Assert.NotNull(frame["revision"]);
        Assert.False(string.IsNullOrWhiteSpace(Str(frame, "action")));
    }

    // -------------------------------------------------------------------------
    // 7) rollback / staging / reverse dependencies / protected control plane
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FailedUpgradeRollsBackStoreAlongsideManifest()
    {
        await using var fx = await Fixture.CreateAsync();

        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha v1");

        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var revisionBefore = fx.Control.Revision;

        // v2 activates a factory whose Apply fails: the upgrade must undo both the
        // declaration edit and the store mutation.
        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "crash");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        var failed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "alpha", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertRejected(failed, "conflict", "upgrade alpha with failing Apply");

        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
        Assert.Equal(revisionBefore, fx.Control.Revision);

        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        AssertContainsStoreEntry(store, "alpha", "1.0.0");
        Assert.DoesNotContain(store.OfType<JsonObject>(),
            item => Str(item, "id") == "alpha" && Str(item, "version") == "2.0.0");

        // The restored declaration must still be usable: a later control action
        // resolves v1 from the store instead of failing with a missing package.
        var disabled = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "alpha", ["enable"] = false }));
        AssertAccepted(disabled, "disable alpha after failed upgrade");
        Assert.Equal("1.0.0", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha"), "installedVersion"));
    }

    [Fact]
    public async Task FailedInstallApplyLeavesNoUndeclaredStorePackage()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "crash");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        var revisionBefore = fx.Control.Revision;

        var failed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true }));
        AssertRejected(failed, "conflict", "install alpha with failing Apply");

        Assert.Equal(revisionBefore, fx.Control.Revision);
        Assert.DoesNotContain(await File.ReadAllTextAsync(fx.ManifestPath), "alpha", StringComparison.Ordinal);
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
            item => Str(item, "id") == "alpha");
    }

    [Fact]
    public async Task ManifestSaveFailureDuringInstallRollsBackStoreAndRuntime()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var revisionBefore = fx.Control.Revision;

        // The forward write and the old rollback shared this exact temp path.
        // Poisoning it used to abort the whole compensation chain after the
        // package had already been committed to the store.
        var poisoned = fx.ManifestPath + ".tmp";
        Directory.CreateDirectory(poisoned);
        try
        {
            var failed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
                new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true }));
            AssertRejected(failed, "unavailable", "install with poisoned manifest temp");

            Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
            Assert.Equal(revisionBefore, fx.Control.Revision);
            Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
                item => Str(item, "id") == "alpha");
            Assert.DoesNotContain(fx.Runtime.Snapshot().Fibers, fiber => fiber.Name == "alpha");
        }
        finally
        {
            Directory.Delete(poisoned, recursive: true);
        }
    }

    [Fact]
    public async Task ManifestSaveFailureDuringUpgradeLeavesOnlyOriginalPackage()
    {
        await using var fx = await Fixture.CreateAsync();

        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha v1");

        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var revisionBefore = fx.Control.Revision;
        var generationBefore = fx.GenerationOf("demo");

        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        var poisoned = fx.ManifestPath + ".tmp";
        Directory.CreateDirectory(poisoned);
        try
        {
            var failed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
                new JsonObject { ["id"] = "alpha", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
            AssertRejected(failed, "unavailable", "upgrade with poisoned manifest temp");

            Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
            Assert.Equal(revisionBefore, fx.Control.Revision);
            Assert.Equal(generationBefore, fx.GenerationOf("demo"));

            var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
            AssertContainsStoreEntry(store, "alpha", "1.0.0");
            Assert.DoesNotContain(store.OfType<JsonObject>(),
                item => Str(item, "id") == "alpha" && Str(item, "version") == "2.0.0");
        }
        finally
        {
            Directory.Delete(poisoned, recursive: true);
        }
    }

    [Fact]
    public async Task StoreInverseFailureIsReportedAndRecoverableAfterFaultClears()
    {
        await using var fx = await Fixture.CreateAsync();

        // The factory poisons the store lock temp during Apply, after the
        // package has committed and before the compensation inverse runs.
        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "crash-store");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        var revisionBefore = fx.Control.Revision;
        var poisoned = Path.Combine(fx.StoreRoot, "installed.json.tmp");

        var failed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true }));
        AssertRejected(failed, "conflict", "install with failing Apply and poisoned store temp");

        // Manifest and runtime compensation must still run even though the
        // store inverse cannot write while its lock temp is poisoned.
        Assert.DoesNotContain(await File.ReadAllTextAsync(fx.ManifestPath), "alpha", StringComparison.Ordinal);
        Assert.Equal(revisionBefore, fx.Control.Revision);
        var error = Str(failed.Data, "error") ?? string.Empty;
        Assert.Contains("compensation", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("store", error, StringComparison.OrdinalIgnoreCase);

        if (Directory.Exists(poisoned))
        {
            Directory.Delete(poisoned, recursive: true);
        }

        // Once the injected fault clears, the orphan left by the failed inverse
        // is removable through the normal control surface.
        var cleanup = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "alpha", ["version"] = "1.0.0" }));
        AssertAccepted(cleanup, "clean up orphan after store inverse failure");
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
            item => Str(item, "id") == "alpha");
    }

    [Fact]
    public async Task UploadKeyIsServerGeneratedAndUploadFileIsConsumed()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        const string fileName = "alpha-1.0.0.gocordis-plugin";
        var first = await ReadEnvelopeAsync(await UploadAsync(fx.Client, package, fileName));
        var second = await ReadEnvelopeAsync(await UploadAsync(fx.Client, package, fileName));
        var firstKey = Str(first.Data, "uploadKey");
        var secondKey = Str(second.Data, "uploadKey");
        Assert.False(string.IsNullOrWhiteSpace(firstKey));
        Assert.False(string.IsNullOrWhiteSpace(secondKey));
        Assert.NotEqual(fileName, firstKey);
        Assert.NotEqual(firstKey, secondKey);

        var uploadDirectory = Path.Combine(fx.StoreRoot, ".uploads");
        Assert.Equal(2, Directory.GetFiles(uploadDirectory).Length);

        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = firstKey, ["enable"] = true })), "install from first upload");
        Assert.Single(Directory.GetFiles(uploadDirectory));

        // A consumed key can no longer drive a mutation; the other upload is intact.
        var replay = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = firstKey, ["enable"] = true }));
        Assert.Equal("not_found", Code(replay));
        var otherKey = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = secondKey, ["enable"] = true }));
        Assert.Equal("conflict", Code(otherKey));

        // A fresh control instance discards staging files left by an older process.
        Directory.CreateDirectory(uploadDirectory);
        await File.WriteAllTextAsync(Path.Combine(uploadDirectory, "orphan.gocordis-plugin"), "stale");
        _ = new PluginControlService(fx.ManifestPath, fx.Store, fx.Registry);
        Assert.False(Directory.Exists(uploadDirectory) && Directory.EnumerateFiles(uploadDirectory).Any());
    }

    [Fact]
    public async Task UpgradeRejectsWhenInstalledDependentRangeWouldBeUnsatisfied()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerV1 = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var providerKey = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = providerKey, ["enable"] = true })), "install provider v1");

        var consumer = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeV1)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumer, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var providerV2 = await BuildPackageAsync(fx.Temp, "provider", "2.0.0", "provider",
            provides: [DemoCapability.Name]);
        var providerV2Key = await UploadAndGetKeyAsync(fx.Client, providerV2, "provider-2.0.0.gocordis-plugin");
        var rejected = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "provider", ["uploadKey"] = providerV2Key, ["versionRange"] = RangeV2 }));
        AssertRejected(rejected, "conflict", "upgrade provider past a declared dependent range");

        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        AssertContainsStoreEntry(store, "provider", "1.0.0");
        Assert.DoesNotContain(store.OfType<JsonObject>(),
            item => Str(item, "id") == "provider" && Str(item, "version") == "2.0.0");
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "consumer"), "state"));
    }

    [Fact]
    public async Task UpgradeAllowsWhenNewVersionStillSatisfiesDependentRange()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerV1 = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var key = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = key, ["enable"] = true })), "install provider v1");

        var consumer = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeWide)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumer, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        var providerV2 = await BuildPackageAsync(fx.Temp, "provider", "2.0.0", "provider",
            provides: [DemoCapability.Name]);
        var v2Key = await UploadAndGetKeyAsync(fx.Client, providerV2, "provider-2.0.0.gocordis-plugin");
        var upgraded = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "provider", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertAccepted(upgraded, "upgrade provider within dependent range");

        AssertContainsStoreEntry(await GetDataArrayAsync(fx.Client, "/api/plugins/store"), "provider", "2.0.0");
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "consumer"), "state"));
    }

    [Fact]
    public async Task UpgradeRejectsWhenDependentRangeOnlyMatchesRemovedVersion()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerV1 = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var key = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = key, ["enable"] = true })), "install provider v1");

        var consumer = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeOldOnly)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumer, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var providerV15 = await BuildPackageAsync(fx.Temp, "provider", "1.5.0", "provider",
            provides: [DemoCapability.Name]);
        var v15Key = await UploadAndGetKeyAsync(fx.Client, providerV15, "provider-1.5.0.gocordis-plugin");
        var rejected = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "provider", ["uploadKey"] = v15Key, ["versionRange"] = "[1.5,2.0)" }));
        AssertRejected(rejected, "conflict", "upgrade removing the only satisfying provider version");

        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        AssertContainsStoreEntry(store, "provider", "1.0.0");
        Assert.DoesNotContain(store.OfType<JsonObject>(),
            item => Str(item, "id") == "provider" && Str(item, "version") == "1.5.0");
    }

    [Fact]
    public async Task UpgradeAllowsSameVersionWithInstalledDependent()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerV1 = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var key = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = key, ["enable"] = true })), "install provider v1");

        var consumer = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeV1)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumer, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        var repeat = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-repeat.gocordis-plugin");
        var result = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "provider", ["uploadKey"] = repeat, ["versionRange"] = "1.0.0" }));
        AssertAccepted(result, "same-version upgrade removes nothing");
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "consumer"), "state"));
    }

    [Fact]
    public async Task UpgradeAllowsWhenAnotherInstalledVersionSatisfiesDependent()
    {
        await using var fx = await Fixture.CreateAsync();

        var providerV1 = await BuildPackageAsync(fx.Temp, "provider", "1.0.0", "provider",
            provides: [DemoCapability.Name]);
        var key = await UploadAndGetKeyAsync(fx.Client, providerV1, "provider-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = key, ["enable"] = true })), "install provider v1");

        // A second installed version is deliberately an orphan: the declaration
        // still resolves v1, but dependents can survive removal of v1.
        var providerV15 = await BuildPackageAsync(fx.Temp, "provider", "1.5.0", "provider",
            provides: [DemoCapability.Name]);
        await fx.Store.InstallAsync(fx.Temp.Path("provider-1.5.0.gocordis-plugin"));
        Assert.NotNull(await fx.Store.ResolveAsync("provider", "1.5.0"));

        var consumer = await BuildPackageAsync(fx.Temp, "consumer", "1.0.0", "consumer",
            dependencies: [new PluginDependency("provider", RangeWide)]);
        var consumerKey = await UploadAndGetKeyAsync(fx.Client, consumer, "consumer-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = consumerKey, ["enable"] = true })), "install consumer");

        var providerV2 = await BuildPackageAsync(fx.Temp, "provider", "2.0.0", "provider",
            provides: [DemoCapability.Name]);
        var v2Key = await UploadAndGetKeyAsync(fx.Client, providerV2, "provider-2.0.0.gocordis-plugin");
        var upgraded = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "provider", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertAccepted(upgraded, "upgrade with surviving provider versions");

        var store = await GetDataArrayAsync(fx.Client, "/api/plugins/store");
        // A bare NuGet range "1.0.0" means >=1.0.0, so the declaration resolved
        // v1.5 before the upgrade; v1.5 is removed while v1 and v2 survive.
        Assert.Contains(store.OfType<JsonObject>(),
            item => Str(item, "id") == "provider" && Str(item, "version") == "1.0.0");
        Assert.Contains(store.OfType<JsonObject>(),
            item => Str(item, "id") == "provider" && Str(item, "version") == "2.0.0");
        Assert.DoesNotContain(store.OfType<JsonObject>(),
            item => Str(item, "id") == "provider" && Str(item, "version") == "1.5.0");
        Assert.Equal("Active", Str(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "consumer"), "state"));
    }

    [Fact]
    public async Task UiUninstallWithDeclarationVersionRangeActuallyRemovesStorePackage()
    {
        await using var fx = await Fixture.CreateAsync();

        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha v1");

        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "alpha", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 })),
            "upgrade alpha v2");

        // The UI sends the declaration's versionRange verbatim; it is not an exact
        // store version, but the package must still be removed.
        var removed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "alpha", ["version"] = RangeV2 }));
        AssertAccepted(removed, "uninstall alpha with declaration range");

        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
            item => Str(item, "id") == "alpha");
        Assert.Null(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alpha"));
        Assert.DoesNotContain(await File.ReadAllTextAsync(fx.ManifestPath), "alpha", StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrphanStorePackageCanBeUninstalledWithoutDeclaration()
    {
        await using var fx = await Fixture.CreateAsync();

        var package = await BuildPackageAsync(fx.Temp, "orphan", "1.0.0", "demo");
        var uploadKey = await UploadAndGetKeyAsync(fx.Client, package, "orphan-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = uploadKey, ["enable"] = true })), "install orphan");

        // Drop only the declaration: the store package is still present and the UI
        // must be able to clean it up.
        var declared = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        await new ApplicationManifest(declared.Id, declared.Version, declared.HostVersion, [])
            .SaveAsync(fx.ManifestPath);
        AssertContainsStoreEntry(await GetDataArrayAsync(fx.Client, "/api/plugins/store"), "orphan", "1.0.0");

        var removed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "orphan" }));
        AssertAccepted(removed, "uninstall orphan package");
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store")).OfType<JsonObject>(),
            item => Str(item, "id") == "orphan");
    }

    [Fact]
    public async Task ProtectedPluginRejectsUpgradeAndConfigWrite()
    {
        await using var fx = await Fixture.CreateAsync();

        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha v1");

        fx.Registry.ProtectedIds.Add("alpha");
        var manifestBefore = await File.ReadAllTextAsync(fx.ManifestPath);
        var revisionBefore = fx.Control.Revision;

        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        var upgrade = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "alpha", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertRejected(upgrade, "forbidden", "upgrade protected alpha");

        var config = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/alpha/config",
            new JsonObject { ["config"] = new JsonObject { ["value"] = "blocked" } }));
        AssertRejected(config, "forbidden", "configure protected alpha");

        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(fx.ManifestPath));
        Assert.Equal(revisionBefore, fx.Control.Revision);
        AssertContainsStoreEntry(await GetDataArrayAsync(fx.Client, "/api/plugins/store"), "alpha", "1.0.0");
    }

    [Fact]
    public async Task EntryAndPluginUseTheSameEffectiveDeclarationPlan()
    {
        await using var fx = await Fixture.CreateAsync();

        foreach (var id in new[] { "alpha", "beta" })
        {
            var package = await BuildPackageAsync(fx.Temp, id, "1.0.0", "demo");
            var key = await UploadAndGetKeyAsync(fx.Client, package, $"{id}-1.0.0.gocordis-plugin");
            AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
                new JsonObject { ["uploadKey"] = key, ["enable"] = true })), $"install {id}");
        }

        // One flat declaration and one entries-only declaration. EffectivePlugins
        // is the single resolution authority for both the host and the control
        // service, so neither disappears from the list below.
        var declared = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        var alpha = Assert.Single(declared.Plugins, item => item.Id == "alpha") with
        {
            Config = new Dictionary<string, object?> { ["source"] = "flat" },
        };
        await new ApplicationManifest(declared.Id, declared.Version, declared.HostVersion,
            [alpha],
            [new EntryNode("group", Children:
            [
                new EntryNode("beta", Url: "beta@1.0.0",
                    Config: new Dictionary<string, object?> { ["source"] = "entry" }),
            ])]).SaveAsync(fx.ManifestPath);
        await fx.Control.ApplyAllAsync();

        var plugins = await GetDataArrayAsync(fx.Client, "/api/plugins");
        Assert.Equal("flat", AssertPlugin(plugins, "alpha")?["config"]?["source"]?.GetValue<string>());
        Assert.Equal("entry", AssertPlugin(plugins, "beta")?["config"]?["source"]?.GetValue<string>());

        var betaConfig = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/beta/config",
            new JsonObject { ["config"] = new JsonObject { ["source"] = "entry-changed" } }));
        AssertAccepted(betaConfig, "configure entries-only beta");
        var alphaConfig = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/alpha/config",
            new JsonObject { ["config"] = new JsonObject { ["source"] = "flat-changed" } }));
        AssertAccepted(alphaConfig, "configure flat alpha");
        var disabled = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "beta", ["enable"] = false }));
        AssertAccepted(disabled, "disable entries-only beta");

        var effective = await ApplicationLoader.EffectivePluginsAsync(
            await ApplicationManifest.LoadAsync(fx.ManifestPath));
        Assert.Equal(2, effective.Count);
        Assert.Equal("flat-changed", Assert.Single(effective, item => item.Id == "alpha")
            .Config!["source"]?.ToString());
        var betaEffective = Assert.Single(effective, item => item.Id == "beta");
        Assert.False(betaEffective.Enabled);
        Assert.Equal("entry-changed", betaEffective.Config!["source"]?.ToString());

        // The entry is edited in place: no second flat declaration is invented.
        var persisted = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        Assert.Single(persisted.Plugins, item => item.Id == "alpha");
        Assert.DoesNotContain(persisted.Plugins, item => item.Id == "beta");
        var entry = Assert.Single(persisted.Entries!);
        Assert.True(entry.Children![0].Disabled);
        Assert.Equal("entry-changed", entry.Children![0].Config!["source"]?.ToString());
    }

    [Fact]
    public async Task EntriesOnlyPluginCanBeUpgradedAndUninstalled()
    {
        await using var fx = await Fixture.CreateAsync();
        var v1 = await BuildPackageAsync(fx.Temp, "beta", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "beta-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install beta v1");

        var declared = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        await new ApplicationManifest(declared.Id, declared.Version, declared.HostVersion, [],
            [new EntryNode("group", Children: [new EntryNode("beta", Url: "beta@1.0.0")])])
            .SaveAsync(fx.ManifestPath);
        await fx.Control.ApplyAllAsync();

        var v2 = await BuildPackageAsync(fx.Temp, "beta", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "beta-2.0.0.gocordis-plugin");
        var upgrade = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "beta", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertAccepted(upgrade, "upgrade entries-only beta");

        var upgraded = Assert.Single(await ApplicationLoader.EffectivePluginsAsync(
            await ApplicationManifest.LoadAsync(fx.ManifestPath)));
        Assert.Equal(RangeV2, upgraded.VersionRange);
        Assert.Equal("beta@" + RangeV2,
            (await ApplicationManifest.LoadAsync(fx.ManifestPath)).Entries![0].Children![0].Url);
        AssertContainsStoreEntry(await GetDataArrayAsync(fx.Client, "/api/plugins/store"), "beta", "2.0.0");

        var removed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "beta", ["version"] = RangeV2 }));
        AssertAccepted(removed, "uninstall entries-only beta");
        Assert.Empty(await ApplicationLoader.EffectivePluginsAsync(
            await ApplicationManifest.LoadAsync(fx.ManifestPath)));
        Assert.Null(AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "beta"));
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store"))
            .OfType<JsonObject>(), item => Str(item, "id") == "beta");
    }

    [Fact]
    public async Task IncludeEntryConfigIsWrittenBackToItsSourceFile()
    {
        await using var fx = await Fixture.CreateAsync();
        var package = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var key = await UploadAndGetKeyAsync(fx.Client, package, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = key, ["enable"] = true })), "install alpha");

        var include = fx.Temp.Path("children.json");
        await File.WriteAllTextAsync(include,
            """[{"id":"inc-1","url":"alpha@[1.0,2.0)","config":{"source":"include"}}]""");
        var declared = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        await new ApplicationManifest(declared.Id, declared.Version, declared.HostVersion, [],
            [new EntryNode("external", Include: include)]).SaveAsync(fx.ManifestPath);
        await fx.Control.ApplyAllAsync();
        Assert.Equal("include", AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"),
            "inc-1")?["config"]?["source"]?.GetValue<string>());

        var configured = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/inc-1/config",
            new JsonObject { ["config"] = new JsonObject { ["source"] = "include-changed" } }));
        AssertAccepted(configured, "configure include entry");

        var rewritten = JsonNode.Parse(await File.ReadAllTextAsync(include))!.AsArray();
        Assert.Equal("include-changed", rewritten[0]?["config"]?["source"]?.GetValue<string>());
        var effective = await ApplicationLoader.EffectivePluginsAsync(
            await ApplicationManifest.LoadAsync(fx.ManifestPath));
        Assert.Equal("include-changed", Assert.Single(effective).Config!["source"]?.ToString());
    }

    [Fact]
    public async Task EntryIdAndPackageIdStayConsistentAcrossControlMutations()
    {
        await using var fx = await Fixture.CreateAsync();
        var v1 = await BuildPackageAsync(fx.Temp, "alpha", "1.0.0", "demo");
        var v1Key = await UploadAndGetKeyAsync(fx.Client, v1, "alpha-1.0.0.gocordis-plugin");
        AssertAccepted(await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/install",
            new JsonObject { ["uploadKey"] = v1Key, ["enable"] = true })), "install alpha");

        var declared = await ApplicationManifest.LoadAsync(fx.ManifestPath);
        await new ApplicationManifest(declared.Id, declared.Version, declared.HostVersion, [],
            [new EntryNode("alias", Url: "alpha@[1.0,2.0)")]).SaveAsync(fx.ManifestPath);
        await fx.Control.ApplyAllAsync();

        var entry = AssertPlugin(await GetDataArrayAsync(fx.Client, "/api/plugins"), "alias");
        Assert.Equal("1.0.0", Str(entry, "installedVersion"));
        var dependencies = await ReadEnvelopeAsync(await fx.Client.GetAsync("/api/plugins/alias/dependencies"));
        Assert.Equal(HttpStatusCode.OK, dependencies.StatusCode);

        var v2 = await BuildPackageAsync(fx.Temp, "alpha", "2.0.0", "demo");
        var v2Key = await UploadAndGetKeyAsync(fx.Client, v2, "alpha-2.0.0.gocordis-plugin");
        var upgrade = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/upgrade",
            new JsonObject { ["id"] = "alias", ["uploadKey"] = v2Key, ["versionRange"] = RangeV2 }));
        AssertAccepted(upgrade, "upgrade alias from package alpha");

        var effective = Assert.Single(await ApplicationLoader.EffectivePluginsAsync(
            await ApplicationManifest.LoadAsync(fx.ManifestPath)));
        Assert.Equal("alias", effective.Id);
        Assert.Equal("alpha", effective.PackageId);
        Assert.Equal(RangeV2, effective.VersionRange);

        var removed = await ReadEnvelopeAsync(await PostJsonAsync(fx.Client, "/api/plugins/uninstall",
            new JsonObject { ["id"] = "alias", ["version"] = RangeV2 }));
        AssertAccepted(removed, "uninstall alias package");
        Assert.DoesNotContain((await GetDataArrayAsync(fx.Client, "/api/plugins/store"))
            .OfType<JsonObject>(), item => Str(item, "id") == "alpha");
    }

    // -------------------------------------------------------------------------
    // helpers: HTTP
    // -------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, JsonNode body) =>
        await client.PostAsync(path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] package, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(package);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(bytes, "package", fileName);
        return await client.PostAsync("/api/plugins/packages", form);
    }

    private static async Task<string> UploadAndGetKeyAsync(HttpClient client, byte[] package, string fileName)
    {
        var envelope = await ReadEnvelopeAsync(await UploadAsync(client, package, fileName));
        var key = Str(envelope.Data, "uploadKey");
        Assert.False(string.IsNullOrWhiteSpace(key), $"upload rejected: {Code(envelope)}");
        return key!;
    }

    private static async Task<Envelope> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new XunitException($"invalid JSON response ({(int)response.StatusCode}): {text}", ex);
        }

        var body = root as JsonObject ?? new JsonObject();
        return new Envelope(response.StatusCode, body, body["data"] as JsonObject ?? new JsonObject());
    }

    private static async Task<JsonNode> GetDataAsync(HttpClient client, string path)
    {
        var text = await client.GetStringAsync(path);
        return JsonNode.Parse(text)?["data"]
               ?? throw new XunitException($"missing data envelope for {path}: {text}");
    }

    private static async Task<JsonArray> GetDataArrayAsync(HttpClient client, string path) =>
        (await GetDataAsync(client, path)).AsArray();

    private static void AssertAccepted(Envelope result, string operation)
    {
        var accepted = result.Data["accepted"] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        Assert.True(accepted,
            $"{operation} was not accepted (HTTP {(int)result.StatusCode}, code={Code(result) ?? "<none>"}, " +
            $"error={Str(result.Data, "error") ?? "<none>"})");
    }

    private static void AssertRejected(Envelope result, string code, string operation)
    {
        var accepted = result.Data["accepted"] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        Assert.False(accepted, $"{operation} must not be accepted");
        Assert.Equal(code, Code(result));
    }

    private static string? Code(Envelope envelope) => Str(envelope.Data, "code") ?? Str(envelope.Root, "code");

    // -------------------------------------------------------------------------
    // helpers: assertions / wire shapes
    // -------------------------------------------------------------------------

    private static JsonObject? AssertPlugin(JsonArray plugins, string id) =>
        plugins.OfType<JsonObject>().FirstOrDefault(item => Str(item, "id") == id);

    private static void AssertContainsStoreEntry(JsonArray store, string id, string version)
    {
        var entry = Assert.Single(store.OfType<JsonObject>(), item => Str(item, "id") == id);
        Assert.Contains(version, new[] { Str(entry, "version"), Str(entry, "installedVersion") });
    }

    private static void AssertBool(JsonObject? obj, string key, bool expected)
    {
        Assert.NotNull(obj);
        Assert.True(obj[key] is JsonValue value && value.TryGetValue<bool>(out var actual) && actual == expected,
            $"expected {key}={expected}, got {obj[key]?.ToJsonString() ?? "<missing>"}");
    }

    private static string? Str(JsonObject? obj, params string[] keys)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (obj[key] is not JsonValue value)
            {
                continue;
            }

            if (value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            if (value.TryGetValue<long>(out var number))
            {
                return number.ToString();
            }
        }

        return null;
    }

    private static string[] Strings(JsonObject obj, string key) =>
        obj[key] is JsonArray array
            ? array.OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var text) ? text : string.Empty)
                .Where(text => text.Length > 0)
                .ToArray()
            : [];

    private static string? DeclaredConfigValue(ApplicationPlugin plugin)
    {
        if (plugin.Config is null || !plugin.Config.TryGetValue("value", out var value))
        {
            return null;
        }

        return value?.ToString();
    }

    private static async Task<JsonObject> ReadPluginStateEventAsync(
        HttpClient client, CancellationToken token, TaskCompletionSource? connected = null)
    {
        try
        {
            using var response = await client.GetAsync("/api/stream", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            connected?.TrySetResult();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            string? eventName = null;

            while (!token.IsCancellationRequested)
            {
                var pending = reader.ReadLineAsync().WaitAsync(token);
                var completed = await Task.WhenAny(pending, Task.Delay(Timeout.Infinite, token));
                if (completed != pending)
                {
                    break;
                }

                var line = await pending;
                if (line is null)
                {
                    break;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal) && eventName == "plugin-state")
                {
                    return JsonNode.Parse(line["data:".Length..].Trim()) as JsonObject
                           ?? throw new XunitException("plugin-state frame has no JSON data");
                }
                else if (line.Length == 0)
                {
                    eventName = null;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // fall through to the assertion failure below
        }

        throw new XunitException("no plugin-state SSE frame was published");
    }

    // -------------------------------------------------------------------------
    // helpers: packages and fixture
    // -------------------------------------------------------------------------

    private static async Task<byte[]> BuildPackageAsync(
        TempDir root,
        string id,
        string version,
        string type,
        IReadOnlyList<PluginDependency>? dependencies = null,
        IReadOnlyList<string>? provides = null)
    {
        var source = root.Path($"source-{id}-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), $"{id}@{version}");

        var packagePath = root.Path($"{id}-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            Id: id,
            Version: version,
            Type: type,
            HostVersionRange: "[1.0,2.0)",
            DisplayName: id,
            Dependencies: dependencies ?? [],
            Provides: provides ?? []), packagePath);

        return await File.ReadAllBytesAsync(packagePath);
    }

    private sealed record Envelope(HttpStatusCode StatusCode, JsonObject Root, JsonObject Data);

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-control-{Guid.NewGuid():N}");

        public string Root { get; }

        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            TempDir temp,
            Runtime runtime,
            ConsoleHost host,
            PluginRegistry registry,
            PluginStore store,
            PluginControlService control,
            WebUiServer server,
            HttpClient client)
        {
            Temp = temp;
            Runtime = runtime;
            Host = host;
            Registry = registry;
            Store = store;
            Control = control;
            Server = server;
            Client = client;
        }

        public TempDir Temp { get; }
        public Runtime Runtime { get; }
        public ConsoleHost Host { get; }
        public PluginRegistry Registry { get; }
        public PluginStore Store { get; }
        public PluginControlService Control { get; }
        public WebUiServer Server { get; }
        public HttpClient Client { get; }
        public string ManifestPath => Temp.Path("gocordis.app.json");
        public string StoreRoot => Temp.Path("store");

        public static async Task<Fixture> CreateAsync()
        {
            var temp = new TempDir();
            var runtime = new Runtime();
            var host = new ConsoleHost(runtime);
            var registry = new PluginRegistry(runtime);
            registry.RegisterFactory("demo", _ => new NoopComponent("demo"));
            registry.RegisterFactory("crash", _ => new CrashComponent());
            registry.RegisterFactory("crash-store", _ => new StorePoisoningCrashComponent(
                Path.Combine(temp.Path("store"), "installed.json.tmp")));
            registry.RegisterFactory("provider", _ => new ProviderComponent());
            registry.RegisterFactory("consumer", _ => new ConsumerComponent());

            var store = new PluginStore(temp.Path("store"), HostVersion);
            var manifestPath = temp.Path("gocordis.app.json");
            await new ApplicationManifest("control-tests", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
            var control = new PluginControlService(manifestPath, store, registry);
            var server = new WebUiServer(host, registry: registry, control: control);
            await server.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(server.Address!) };
            return new Fixture(temp, runtime, host, registry, store, control, server, client);
        }

        public int GenerationOf(string fiberName)
        {
            var fiber = Runtime.Snapshot().Fibers.FirstOrDefault(item => item.Name.Contains(fiberName, StringComparison.Ordinal));
            return fiber?.Generation ?? -1;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Server.DisposeAsync();
            await Registry.DisposeAsync();
            await Runtime.DisposeAsync();
            Temp.Dispose();
        }
    }

    private sealed class NoopComponent(string name) : Component
    {
        public override string Name => name;
        public override ValueTask<Cleanup?> ApplyAsync(Context context) => ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class CrashComponent : Component
    {
        public override string Name => "crash";
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new InvalidOperationException("crash plugin cannot activate");
    }

    private sealed class StorePoisoningCrashComponent(string poisonPath) : Component
    {
        public override string Name => "crash-store";

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Directory.CreateDirectory(poisonPath);
            throw new InvalidOperationException("crash-store plugin cannot activate");
        }
    }

    private sealed class ProviderComponent : Component
    {
        public override string Name => "provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [DemoCapability];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(DemoCapability, "available");
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class ConsumerComponent : Component
    {
        public override string Name => "consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [DemoCapability];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _ = context.Require(DemoCapability);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
