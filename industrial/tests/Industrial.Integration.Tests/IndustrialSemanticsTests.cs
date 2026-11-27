using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Tests;

/// <summary>Go-parity semantics that do not need a full Elsa run: date
/// directories/filenames and metadata rules are pure, testable contracts.
/// </summary>
public sealed class IndustrialSemanticsTests
{
    [Fact]
    public void ProfileDiscoveryFiltersDateDirectoriesAndFilenames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-discovery-{Guid.NewGuid():N}");
        var dated = Path.Combine(root, "2026-11-27");
        Directory.CreateDirectory(dated);
        File.WriteAllText(Path.Combine(dated, "20261127-a.csv"), "record_id,reading\n1,42\n");
        File.WriteAllText(Path.Combine(dated, "notes.txt"), "ignore");
        try
        {
            var rootSource = FileIdentityFactory.Create(Path.Combine(dated, "20261127-a.csv"), root, "src");
            var otherSource = FileIdentityFactory.Create(Path.Combine(dated, "notes.txt"), root, "src");
            var files = new[]
            {
                new DiscoveredFile(rootSource, Path.Combine(dated, "20261127-a.csv"),
                    DateTimeOffset.UtcNow, "src"),
                new DiscoveredFile(otherSource, Path.Combine(dated, "notes.txt"),
                    DateTimeOffset.UtcNow, "src"),
            };
            var profile = new FileProfileDefinition("p", "Date profile", 1, "*.csv", "csv", ",",
                true, "utf-8", new Dictionary<string, object?>(), [], true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                "YYYY-MM-DD", "yyyyMMdd-*.csv", false, []);
            var matched = FileProfileDiscovery.Apply(profile, files, root);
            Assert.Single(matched);
            Assert.EndsWith("20261127-a.csv", matched[0].FullPath);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void DateDirectoryOverridesLateFileMtime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-late-file-{Guid.NewGuid():N}");
        var date = new DateOnly(2026, 11, 20);
        var directory = Path.Combine(root, date.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "readings.csv");
        File.WriteAllText(path, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        try
        {
            var identity = FileIdentityFactory.Create(path, root, "src");
            var file = new DiscoveredFile(identity, path, identity.LastWriteUtc, "src");
            var profile = new FileProfileDefinition("p", "Date directories", 1, "*.csv", "csv", ",",
                true, "utf-8", new Dictionary<string, object?>(), [], true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                "YYYY-MM-DD", string.Empty, false, []);
            var matched = FileProfileDiscovery.Apply(profile, [file], root);
            var matchedFile = Assert.Single(matched);
            Assert.Equal(date, DateOnly.FromDateTime(matchedFile.SourceDate.UtcDateTime));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FlatProfileReadsARootFileDirectly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-flat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "flat.csv");
        File.WriteAllText(path, "record_id,reading\n1,42\n");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var resource = new ResourceDefinition("src", "filesystem.local", "Flat source", "local",
                new Dictionary<string, object?> { ["root"] = path }, null, 1, true, now, now);
            var source = new LocalFileSource(new SingleResourceRegistry(resource));
            var discovered = await source.DiscoverAsync("src",
                new SourceDateRange(DateTimeOffset.UnixEpoch, now.AddDays(1)));
            var profile = new FileProfileDefinition("p", "Flat profile", 1, "*.csv", "csv", ",",
                true, "utf-8", new Dictionary<string, object?>(), [], true, now, now,
                string.Empty, string.Empty, true, []);
            var matched = FileProfileDiscovery.Apply(profile, discovered, path);
            Assert.Equal(path, Assert.Single(matched).FullPath);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void MappingAppliesMetadataRulesFromSourceIdentity()
    {
        var transform = new MappingTransform(new Dictionary<string, object?>
        {
            ["mapping"] = new Dictionary<string, object?> { ["value"] = "reading" },
            ["metadataRules"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["from"] = "filename", ["pattern"] = "(?<value>line-\\d+)",
                    ["key"] = "line", ["required"] = true,
                },
            },
        });
        var identity = new FileIdentity("src|2026/11/27/line-7.csv|1|HASH", "2026/11/27/line-7.csv",
            1, DateTimeOffset.UtcNow, "HASH", "src");
        var mapped = transform.Apply(new RecordEnvelope(
            new Dictionary<string, object?> { ["reading"] = "42" }, identity, DateTimeOffset.UtcNow));
        Assert.Equal("42", mapped.Values["value"]);
        Assert.Equal("line-7", mapped.Values["line"]);
    }

    private sealed class SingleResourceRegistry(ResourceDefinition resource) : IResourceRegistry
    {
        public ValueTask<ResourceDefinition> RegisterAsync(ResourceDefinition value, CancellationToken ct = default) =>
            ValueTask.FromResult(resource);

        public ValueTask<ResourceDefinition?> GetAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult<ResourceDefinition?>(id == resource.Id ? resource : null);

        public ValueTask<IReadOnlyList<ResourceDefinition>> ListAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinition>>([resource]);

        public ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult(false);

        public void BindWorkflows(IWorkflowRegistry workflows) { }

        public void UnbindWorkflows() { }
    }
}
