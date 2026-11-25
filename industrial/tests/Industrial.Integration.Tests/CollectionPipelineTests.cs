using System.Text.Json;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Real vertical slice: local source → CSV → mapping → validation →
/// idempotent SQLite sink → archive, with file-level and target-level ledger.
/// No Cordis Core is modified; only the new industrial projects are used.</summary>
public sealed class CollectionPipelineTests
{
    [Fact]
    public async Task CollectionEmitsOpenTelemetryActivity()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var seen = new List<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Gocordis.Industrial.Collection",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref System.Diagnostics.ActivityCreationOptions<string> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => seen.Add(activity.OperationName),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        Assert.True(IndustrialTelemetry.Source.HasListeners(), "telemetry listener was not registered");
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        await coordinator.RunAsync(
            "telemetry", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new CsvRecordReader(), [new SqliteRecordSink(fixture.NewTargetDb)], null);
        Assert.Contains("collection.run", seen);
    }

    [Fact]
    public async Task FullCollectionIsIdempotentAndLedgerTracksEachTarget()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var reader = new CsvRecordReader();
        var mapping = new MappingTransform(new Dictionary<string, object?>
        {
            ["mapping"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["value"] = "reading",
            }),
        });
        var validation = new ValidationTransform(new Dictionary<string, object?>
        {
            ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
        });
        RecordEnvelope Pipeline(RecordEnvelope record)
        {
            var mapped = mapping.Apply(record);
            Assert.True(validation.TryApply(mapped, out var valid, out _));
            return valid!;
        }

        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        IAsyncEnumerable<RecordEnvelope> Transformed(DiscoveredFile file, CancellationToken ct) =>
            Transform(reader.ReadAsync(file, ct), Pipeline);

        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        var archive = new FileArchiveSink(fixture.ArchiveRoot);

        var firstRun = await coordinator.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new DelegateReader(Transformed), [sink], archive);
        var secondRun = await coordinator.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new DelegateReader(Transformed), [sink], archive);

        var targetRows = await CountTargetRows(fixture);
        Assert.Equal(2, targetRows);
        Assert.NotEqual(firstRun, secondRun);
        var files = await fixture.Ledger.ListFilesAsync(firstRun);
        Assert.Single(files);
        Assert.Equal("Succeeded", files[0].Status);
        using var ledger = fixture.NewDb();
        var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery => Assert.Equal("Succeeded", delivery.Status));
        var archived = Directory.GetFiles(Path.Combine(fixture.ArchiveRoot, "20260101"), "*.csv");
        Assert.Single(archived);
    }

    [Fact]
    public async Task ArchiveFailureIsRetriedWithoutRepeatingDatabaseDelivery()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        var brokenArchive = new ThrowingArchive();
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        var reader = new CsvRecordReader();

        await coordinator.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            reader, [sink], brokenArchive);

        using var ledger = fixture.NewDb();
        var dbDelivery = Assert.Single(ledger.FileDeliveries.AsNoTracking()
            .Where(row => row.TargetId == "sqlite"));
        var archiveDelivery = Assert.Single(ledger.FileDeliveries.AsNoTracking()
            .Where(row => row.TargetId == "archive"));
        Assert.Equal("Succeeded", dbDelivery.Status);
        Assert.Equal("Failed", archiveDelivery.Status);
        Assert.Equal(1, dbDelivery.Attempts);

        // A retry of ONLY the archive target is enough; the database row must
        // not be written again.
        var file = Assert.Single(ledger.CollectionFiles.AsNoTracking().ToList());
        var retry = await fixture.Ledger.StartDeliveryAsync(
            file.Id, "archive", $"{file.IdentityKey}->archive");
        Assert.Equal(archiveDelivery.Id, retry.Id);
        await fixture.Ledger.CompleteDeliveryAsync(retry.Id, 1);
        Assert.Equal(2, await CountTargetRows(fixture));
    }

    [Fact]
    public async Task RetryRunnerOnlyRetriesFailedTargets()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        await coordinator.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new CsvRecordReader(), [sink], new ThrowingArchive());

        var goodArchive = new FileArchiveSink(fixture.ArchiveRoot);
        var retried = await coordinator.RetryFailedAsync([sink], goodArchive);
        Assert.Equal(1, retried);
        using var ledger = fixture.NewDb();
        Assert.All(ledger.FileDeliveries.AsNoTracking().ToList(), row =>
            Assert.Equal("Succeeded", row.Status));
        Assert.Equal(2, await CountTargetRows(fixture));
    }

    [Fact]
    public async Task DatabaseDisconnectIsRecordedAndRetriedWithoutDuplicateWrites()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var offlineSink = new DelegateSink("database", (_, _, _, _) =>
            throw new IOException("database offline"));
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        await coordinator.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new CsvRecordReader(), [offlineSink], null);

        using (var ledger = fixture.NewDb())
        {
            var failed = Assert.Single(ledger.FileDeliveries.AsNoTracking().ToList());
            Assert.Equal("Failed", failed.Status);
            // BoundedRetry exhausts its 3 in-process attempts; the ledger
            // records one bounded failure episode that can be resumed later.
            Assert.Equal(1, failed.Attempts);
            Assert.Contains("database offline", failed.Error);
            Assert.Equal(0, await CountTargetRows(fixture));
        }

        var restored = new SqliteRecordSink(fixture.NewTargetDb, "database");
        Assert.Equal(1, await coordinator.RetryFailedAsync([restored], null));
        using var after = fixture.NewDb();
        Assert.All(after.FileDeliveries.AsNoTracking().ToList(), row =>
            Assert.Equal("Succeeded", row.Status));
        Assert.Equal(2, await CountTargetRows(fixture));
    }

    [Fact]
    public async Task RestartedCoordinatorResumesFailedDeliveryFromLedger()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var offlineSink = new DelegateSink("database", (_, _, _, _) =>
            throw new IOException("process died"));
        var first = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        await first.RunAsync(
            "workflow-1", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new CsvRecordReader(), [offlineSink], null);

        // A new process has no in-memory state; the ledger is the resume source.
        var restartedLedger = new CollectionLedger(fixture.NewDb);
        var restarted = new CollectionCoordinator(
            fixture.Resources, fixture.Source, restartedLedger, new CsvRecordReader());
        var restored = new SqliteRecordSink(fixture.NewTargetDb, "database");
        Assert.Equal(1, await restarted.RetryFailedAsync([restored], null));
        Assert.Equal(2, await CountTargetRows(fixture));
    }

    [Fact]
    public async Task OfflineScanBackfillsMissedFilesWithoutDuplicatingBusinessData()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var late = Path.Combine(fixture.SourceRoot, "late.csv");
        File.WriteAllText(late, "record_id,reading\nlate-1,99\n");
        File.SetLastWriteTimeUtc(late, DateTimeOffset.UtcNow.AddDays(-3).UtcDateTime);

        var planner = new HistoricalRangePlanner();
        var onlineRange = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
        var backfillRange = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(7));
        Assert.DoesNotContain(await fixture.Source.DiscoverAsync("source-1", onlineRange),
            file => file.Identity.RelativePath == "late.csv");
        Assert.Contains(await fixture.Source.DiscoverAsync("source-1", backfillRange),
            file => file.Identity.RelativePath == "late.csv");

        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        await coordinator.RunAsync("offline", "source-1", backfillRange,
            new CsvRecordReader(), [sink], null);
        await coordinator.RunAsync("offline", "source-1", backfillRange,
            new CsvRecordReader(), [sink], null);

        Assert.Equal(1, await CountTargetRows(fixture));
        using var ledger = fixture.NewDb();
        Assert.Single(ledger.CollectionFiles.AsNoTracking().ToList(),
            file => file.IdentityKey.Contains("late.csv"));
        Assert.Single(ledger.FileDeliveries.AsNoTracking().ToList(),
            delivery => delivery.IdempotencyKey.Contains("late.csv"));
    }

    [Fact]
    public async Task RecordTimeFromCsvReachesTheTarget()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        File.WriteAllText(Path.Combine(fixture.SourceRoot, "20260102.csv"),
            "record_id,reading,record_time\n3,44,2026-01-02T03:04:05+00:00\n");
        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        await coordinator.RunAsync(
            "workflow-2", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
            new CsvRecordReader(), [sink], null);

        await using var target = fixture.NewTargetDb();
        var row = Assert.Single(target.Records.AsNoTracking()
            .Where(record => record.RecordId == "3").ToList());
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), row.RecordTime);
    }

    [Fact]
    public async Task HistoricalBackfillUsesLedgerNotLastSuccessDate()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var planner = new HistoricalRangePlanner();
        var now = DateTimeOffset.UtcNow;
        var range = planner.Plan(now, TimeSpan.FromDays(400));
        Assert.Equal(now.AddDays(-400), range.FromInclusive);
        Assert.Equal(now, range.ToExclusive);

        var files = await fixture.Source.DiscoverAsync("source-1", range);
        Assert.Single(files);
        var identity = files[0].Identity;
        var same = FileIdentityFactory.Create(files[0].FullPath, fixture.SourceRoot, "source-1");
        Assert.Equal(identity.Key, same.Key);

        var otherMapping = FileIdentityFactory.Create(
            files[0].FullPath, fixture.SourceRoot, "source-1", DateTimeOffset.UtcNow);
        Assert.Equal(identity.Key, otherMapping.Key);
    }

    [Fact]
    public async Task CredentialVaultFailsFastWithoutProductionKey()
    {
        var previous = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY", null);
            using var fixture = new Fixture();
            var secretKeyMissing = false;
            try
            {
                _ = new CredentialVault(fixture.NewDb);
            }
            catch (InvalidOperationException)
            {
                secretKeyMissing = true;
            }

            Assert.True(secretKeyMissing);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY", previous);
        }
    }

    [Fact]
    public async Task CredentialVaultNeverExposesSecretsThroughViews()
    {
        using var fixture = new Fixture();
        var view = await fixture.Vault.PutAsync("cred-1", "share", "username-password", "s3cret");
        Assert.Equal("s3cret", await fixture.Vault.RevealAsync("cred-1"));
        Assert.DoesNotContain("s3cret", view.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", JsonSerializer.Serialize(view), StringComparison.Ordinal);
        var rotated = await fixture.Vault.RotateAsync("cred-1", "new-secret");
        Assert.Equal(2, rotated.Version);
        Assert.Equal("new-secret", await fixture.Vault.RevealAsync("cred-1"));
    }

    private static async Task<int> CountTargetRows(Fixture fixture)
    {
        await using var db = fixture.NewTargetDb();
        return await db.Records.AsNoTracking().CountAsync();
    }

    private static async IAsyncEnumerable<RecordEnvelope> Transform(
        IAsyncEnumerable<RecordEnvelope> source,
        Func<RecordEnvelope, RecordEnvelope> map)
    {
        await foreach (var record in source)
        {
            yield return map(record);
        }
    }

    private sealed class DelegateReader : IRecordReader
    {
        private readonly Func<DiscoveredFile, CancellationToken, IAsyncEnumerable<RecordEnvelope>> _read;
        public DelegateReader(Func<DiscoveredFile, CancellationToken, IAsyncEnumerable<RecordEnvelope>> read) => _read = read;
        public IAsyncEnumerable<RecordEnvelope> ReadAsync(
            DiscoveredFile file, CancellationToken ct = default) => _read(file, ct);
    }

    private sealed class DelegateSink : IRecordSink
    {
        private readonly Func<string, IAsyncEnumerable<RecordEnvelope>, string, CancellationToken,
            ValueTask<DeliveryResultRef>> _write;

        public DelegateSink(string targetId, Func<string, IAsyncEnumerable<RecordEnvelope>, string,
            CancellationToken, ValueTask<DeliveryResultRef>> write)
        {
            TargetId = targetId;
            _write = write;
        }

        public string TargetId { get; }

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            _write(collectionFileId, records, idempotencyKey, ct);
    }

    private sealed class ThrowingArchive : IArchiveSink
    {
        public string TargetId => "archive";
        public ValueTask ArchiveAsync(DiscoveredFile file, string idempotencyKey, CancellationToken ct = default) =>
            throw new IOException("archive target unavailable");
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            ArchiveRoot = Path.Combine(Root, "archive");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(ArchiveRoot);
            var sample = Path.Combine(SourceRoot, "20260101.csv");
            File.WriteAllText(sample, "record_id,reading\n1,42\n2,43\n");
            File.SetLastWriteTimeUtc(sample, DateTimeOffset.UtcNow.AddDays(-2).UtcDateTime);
            LedgerPath = Path.Combine(Root, "ledger.db");
            TargetPath = Path.Combine(Root, "target.db");
            using (var bootstrap = IndustrialStore.Create(LedgerPath)) { }
            using (var bootstrap = IndustrialStore.CreateTarget(TargetPath)) { }
            Resources = new ResourceRegistry(NewDb);
            Vault = new CredentialVault(NewDb, allowDevelopmentKey: true);
            Ledger = new CollectionLedger(NewDb);
            Source = new LocalFileSource(Resources);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string ArchiveRoot { get; }
        public string LedgerPath { get; }
        public string TargetPath { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);
        public IndustrialTargetDbContext NewTargetDb() => IndustrialStore.CreateTarget(TargetPath);
        public IResourceRegistry Resources { get; }
        public ICredentialVault Vault { get; }
        public ICollectionLedger Ledger { get; }
        public LocalFileSource Source { get; }

        public async Task RegisterResourceAsync() =>
            await Resources.RegisterAsync(new ResourceDefinition(
                "source-1", "filesystem.local", "Production share", "local",
                new Dictionary<string, object?> { ["root"] = SourceRoot },
                null, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
