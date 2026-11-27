using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Production persistence must not silently fall back to a temp root.
/// Development/test hosts opt in explicitly with allowDevelopmentKey.</summary>
public sealed class ProductionRootTests
{
    [Fact]
    public async Task ElsaLedgerAndStageUseTheSameNormalizedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-root-shared-{Guid.NewGuid():N}");
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "false",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());
        var paths = Assert.IsType<JsonObject>(console.Api.Query("industrial/storage-paths"));
        Assert.Equal(Path.GetFullPath(root), paths["root"]!.GetValue<string>());
        Assert.Equal(Path.GetFullPath(root), paths["stageRoot"]!.GetValue<string>());
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "industrial-elsa.db"),
            paths["elsaDb"]!.GetValue<string>());
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "industrial-ledger.db"),
            paths["ledgerDb"]!.GetValue<string>());
    }

    [Fact]
    public void ElsaRootRejectsTemporaryRootInProduction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"industrial-elsa-root-{Guid.NewGuid():N}");
        Assert.Throws<InvalidOperationException>(() =>
            Industrial.Core.IndustrialPaths.NormalizeRoot(tempRoot, allowDevelopmentKey: false));
        Assert.Equal(Path.GetFullPath(tempRoot),
            Industrial.Core.IndustrialPaths.NormalizeRoot(tempRoot, allowDevelopmentKey: true));
    }

    [Fact]
    public void TemporaryRootIsRejectedUnlessExplicitlyAllowed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"gocordis-root-test-{Guid.NewGuid():N}");
        Assert.Throws<InvalidOperationException>(
            () => IndustrialCapabilityComponent.EnsurePersistentRoot(tempRoot, allowDevelopmentKey: false));
        Assert.Equal(Path.GetFullPath(tempRoot),
            IndustrialCapabilityComponent.EnsurePersistentRoot(tempRoot, allowDevelopmentKey: true));
    }

    [Fact]
    public void PersistentRootIsAcceptedInProduction()
    {
        var persistent = Path.Combine(Directory.GetCurrentDirectory(), "gocordis-production-data");
        Assert.Equal(Path.GetFullPath(persistent),
            IndustrialCapabilityComponent.EnsurePersistentRoot(persistent, allowDevelopmentKey: false));
    }
}
