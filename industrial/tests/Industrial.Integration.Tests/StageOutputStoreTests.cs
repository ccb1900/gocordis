using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Tests;

/// <summary>Phase 2 gates: stage data lives in the Cordis data plane, not in
/// Elsa variables, and completed stages are reusable after a restart.</summary>
public sealed class StageOutputStoreTests
{
    [Fact]
    public async Task SpooledDatasetSurvivesANewStoreInstance()
    {
        using var fixture = new Fixture();
        var identity = FileIdentityFactory.Create(
            Path.Combine(fixture.Root, "recent.csv"), fixture.Root, "source-1");
        var file = new DiscoveredFile(identity, Path.Combine(fixture.Root, "recent.csv"),
            DateTimeOffset.UtcNow, "source-1");
        var reader = new CsvRecordReader();

        var first = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await first.SpoolAsync("instance-1", "read", "fp-1", reader.ReadAsync(file));
        Assert.Equal(1, dataset.RecordCount);

        var restarted = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var cached = await restarted.TryGetAsync<DatasetRef>("instance-1", "read", "fp-1");
        Assert.NotNull(cached);
        Assert.Equal(dataset.SpoolPath, cached!.SpoolPath);
        var records = new List<RecordEnvelope>();
        await foreach (var record in restarted.ReadAsync(cached)) records.Add(record);
        Assert.Single(records);
        Assert.Equal("1", records[0].Values["record_id"]?.ToString());
        Assert.Single(await restarted.ListAsync("instance-1"));
    }

    [Fact]
    public async Task FingerprintMismatchIsRejected()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        await store.CommitAsync("instance-1", "source", "fp-1", new FileBatchRef(
            "batch-1", "instance-1", "source", "fp-1", []));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.TryGetAsync<FileBatchRef>("instance-1", "source", "fp-2").AsTask());
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "recent.csv"), "record_id,reading\n1,42\n");
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
