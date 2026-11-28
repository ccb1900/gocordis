using System.Text.Json;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Tests;

/// <summary>Phase 2 gates: stage data lives in the Cordis data plane, not in
/// Elsa variables, and completed stages are reusable after a restart.</summary>
public sealed class StageOutputStoreTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    [Fact]
    public async Task RoundTripReturnsExactlyTheCommittedRecordsWithAnIntegritySidecar()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-round", "read", "fp-round", "round.csv",
            "record_id,reading\n1,10\n2,20\n3,30\n");

        Assert.Equal(3, dataset.RecordCount);
        Assert.True(File.Exists(dataset.SpoolPath + ".integrity.json"));

        var seen = new List<RecordEnvelope>();
        await foreach (var record in store.ReadAsync(dataset)) seen.Add(record);
        Assert.Equal(3, seen.Count);
        Assert.Equal(new[] { "1", "2", "3" }, seen.Select(x => x.Values["record_id"]?.ToString()).ToArray());
        Assert.Equal(new[] { "10", "20", "30" }, seen.Select(x => x.Values["reading"]?.ToString()).ToArray());
    }

    [Fact]
    public async Task TruncatedCommittedSpoolThrowsDuringEnumerationInsteadOfReturningFewerRecords()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-trunc", "read", "fp-trunc", "trunc.csv",
            "record_id,reading\n1,10\n2,20\n3,30\n");

        // Cut off the last intact line; the remaining lines are all parseable,
        // which is exactly the silent-data-loss case this check must catch.
        var bytes = File.ReadAllBytes(dataset.SpoolPath);
        var previousNewline = Array.LastIndexOf(bytes, (byte)'\n', bytes.Length - 2);
        Assert.True(previousNewline > 0);
        File.WriteAllBytes(dataset.SpoolPath, bytes[..(previousNewline + 1)]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in store.ReadAsync(dataset)) { }
        });
        Assert.Contains("SHA-256", error.Message);
    }

    [Fact]
    public async Task NullRecordLineIsSurfacedInsteadOfSilentlySkipped()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-null", "read", "fp-null", "null.csv",
            "record_id,reading\n1,10\n");
        var records = new List<RecordEnvelope>();
        await foreach (var record in store.ReadAsync(dataset)) records.Add(record);

        // No sidecar at this path: a legacy ref whose count still matches must
        // fail on the parse-failure line rather than yield one record and pass.
        var path = Path.Combine(fixture.Root, "legacy-null.jsonl");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records[0], Json) + "\nnull\n");
        var legacy = new DatasetRef("legacy-null", "instance-null", "read", "fp-null", path, 2, "batch");

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in store.ReadAsync(legacy)) { }
        });
        Assert.Contains("not a RecordEnvelope", error.Message);
    }

    [Fact]
    public async Task CorruptedJsonLineThrowsWithItsLineNumber()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-bad", "read", "fp-bad", "bad.csv",
            "record_id,reading\n1,10\n2,20\n");

        var lines = (await File.ReadAllLinesAsync(dataset.SpoolPath)).ToList();
        lines[1] = "{ this is not json";
        await File.WriteAllTextAsync(dataset.SpoolPath, string.Join("\n", lines) + "\n");

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in store.ReadAsync(dataset)) { }
        });
        Assert.Contains("line 2", error.Message);
        Assert.Contains("not valid JSON", error.Message);
    }

    [Fact]
    public async Task LegacyRefWithoutDigestUsesCountOnlyVerificationAndStillReads()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-legacy", "read", "fp-legacy", "legacy.csv",
            "record_id,reading\n7,70\n");
        var records = new List<RecordEnvelope>();
        await foreach (var record in store.ReadAsync(dataset)) records.Add(record);

        var path = Path.Combine(fixture.Root, "legacy-ok.jsonl");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records[0], Json) + "\n");
        var legacy = new DatasetRef("legacy-ok", "instance-legacy", "read", "fp-legacy", path, 1, "batch");

        var seen = new List<RecordEnvelope>();
        await foreach (var record in store.ReadAsync(legacy)) seen.Add(record);
        Assert.Single(seen);
    }

    [Fact]
    public async Task TerminalInstanceStageIsCleanedAfterRetention()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-terminal", "read", "fp-term",
            "terminal.csv", "record_id,reading\n1,10\n");
        Backdate(fixture.Root, "instance-terminal", TimeSpan.FromDays(8));

        var report = await store.CleanupAsync(
            new HashSet<string>(StringComparer.Ordinal) { "instance-terminal" }, TimeSpan.FromDays(7));

        Assert.Equal(1, report.RowsDeleted);
        Assert.Equal(1, report.Candidates);
        Assert.Equal(1, report.StageRows);
        Assert.True(report.StageBytes > 0);
        Assert.True(report.DeletedBytes > 0);
        Assert.Empty(report.Failures);
        Assert.False(File.Exists(dataset.SpoolPath));
        Assert.False(File.Exists(dataset.SpoolPath + ".integrity.json"));
        Assert.Empty(await store.ListAsync("instance-terminal"));
    }

    [Fact]
    public async Task NonTerminalInstanceStageIsRetained()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var dataset = await SpoolCsvAsync(store, fixture, "instance-active", "read", "fp-active",
            "active.csv", "record_id,reading\n1,10\n");
        Backdate(fixture.Root, "instance-active", TimeSpan.FromDays(30));

        // The ledger did not declare this instance terminal: a retry still
        // needs the committed spool even long after its commit timestamp.
        var report = await store.CleanupAsync(
            new HashSet<string>(StringComparer.Ordinal), TimeSpan.FromDays(7));

        Assert.Equal(0, report.RowsDeleted);
        Assert.Equal(0, report.Candidates);
        Assert.Contains(report.Retained, reason => reason.Contains("not terminal", StringComparison.Ordinal));
        Assert.True(File.Exists(dataset.SpoolPath));
        Assert.True(File.Exists(dataset.SpoolPath + ".integrity.json"));
        Assert.Single(await store.ListAsync("instance-active"));
    }

    [Fact]
    public async Task MissingOrCorruptSpoolDoesNotBreakCleanup()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var missing = await SpoolCsvAsync(store, fixture, "instance-missing", "read", "fp-missing",
            "missing.csv", "record_id,reading\n1,10\n");
        var corrupt = await SpoolCsvAsync(store, fixture, "instance-corrupt", "read", "fp-corrupt",
            "corrupt.csv", "record_id,reading\n2,20\n");
        File.Delete(missing.SpoolPath);
        await File.WriteAllTextAsync(corrupt.SpoolPath, "not-a-spool-at-all");
        Backdate(fixture.Root, "instance-missing", TimeSpan.FromDays(8));
        Backdate(fixture.Root, "instance-corrupt", TimeSpan.FromDays(8));

        var report = await store.CleanupAsync(
            id => id is "instance-missing" or "instance-corrupt", TimeSpan.FromDays(7));

        Assert.Equal(2, report.RowsDeleted);
        Assert.Equal(2, report.Candidates);
        Assert.Empty(report.Failures);
        Assert.Empty(await store.ListAsync("instance-missing"));
        Assert.Empty(await store.ListAsync("instance-corrupt"));
        Assert.False(File.Exists(missing.SpoolPath + ".integrity.json"));
        Assert.False(File.Exists(corrupt.SpoolPath));
    }

    [Fact]
    public async Task OrphanSpoolsAndPartialsAreReclaimedAfterSafetyWindow()
    {
        using var fixture = new Fixture();
        var store = new StageOutputStore(() => IndustrialStore.CreateStages(fixture.Root), fixture.Root);
        var referenced = await SpoolCsvAsync(store, fixture, "instance-referenced", "read",
            "fp-referenced", "referenced.csv", "record_id,reading\n1,10\n");

        var stageRoot = Path.Combine(fixture.Root, "stages");
        var orphanDirectory = Path.Combine(stageRoot, "instance-orphan");
        Directory.CreateDirectory(orphanDirectory);
        var orphanSpool = Path.Combine(orphanDirectory, "orphan.jsonl");
        var orphanSidecar = orphanSpool + ".integrity.json";
        var orphanPartial = orphanSpool + ".partial";
        await File.WriteAllTextAsync(orphanSpool, "record_id,reading\n9,90\n");
        await File.WriteAllTextAsync(orphanSidecar, "{\"digest\":\"orphan\",\"recordCount\":1}");
        await File.WriteAllTextAsync(orphanPartial, "partial");
        foreach (var path in new[] { orphanSpool, orphanSidecar, orphanPartial })
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        var report = await store.CleanupAsync(
            new HashSet<string>(StringComparer.Ordinal), TimeSpan.FromDays(7),
            default, orphanRetention: TimeSpan.FromHours(1));

        Assert.True(report.Orphans >= 3);
        Assert.True(report.OrphanBytes > 0);
        Assert.Empty(report.OrphanFailures);
        Assert.False(File.Exists(orphanSpool));
        Assert.False(File.Exists(orphanSidecar));
        Assert.False(File.Exists(orphanPartial));
        Assert.True(File.Exists(referenced.SpoolPath));
        Assert.True(File.Exists(referenced.SpoolPath + ".integrity.json"));
    }

    private static void Backdate(string root, string instanceId, TimeSpan age)
    {
        using var db = IndustrialStore.CreateStages(root);
        foreach (var row in db.StageOutputs.Where(row => row.InstanceId == instanceId))
            row.CommittedAt = DateTimeOffset.UtcNow - age;
        db.SaveChanges();
    }

    private static async Task<DatasetRef> SpoolCsvAsync(
        StageOutputStore store, Fixture fixture, string instanceId, string nodeId,
        string fingerprint, string name, string csv)
    {
        var path = Path.Combine(fixture.Root, name);
        File.WriteAllText(path, csv);
        var identity = FileIdentityFactory.Create(path, fixture.Root, "source-1");
        var file = new DiscoveredFile(identity, path, DateTimeOffset.UtcNow, "source-1");
        return await store.SpoolAsync(instanceId, nodeId, fingerprint, new CsvRecordReader().ReadAsync(file));
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
