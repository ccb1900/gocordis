using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

public sealed class SqliteSinkDuplicateTests
{
    [Fact]
    public async Task DuplicateRecordIdsInOneBatchAreInsertedOnce()
    {
        using var temp = new TempRoot();
        var identity = new FileIdentity("source-1|data.csv|1|hash", "data.csv", 1,
            DateTimeOffset.UnixEpoch, "hash", "source-1");
        var sink = new SqliteRecordSink(() => IndustrialStore.CreateTarget(temp.TargetPath));

        var result = await sink.WriteAsync("file-1", Duplicates(identity), "key-1");

        Assert.Equal(1, result.Records);
        await using var db = IndustrialStore.CreateTarget(temp.TargetPath);
        Assert.Equal(1, await db.Records.AsNoTracking().CountAsync());
    }

    private static async IAsyncEnumerable<RecordEnvelope> Duplicates(FileIdentity identity)
    {
        await ValueTask.CompletedTask;
        for (var index = 0; index < 3; index++)
        {
            yield return new RecordEnvelope(
                new Dictionary<string, object?> { ["record_id"] = "duplicate", ["reading"] = index },
                identity, DateTimeOffset.UnixEpoch);
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-sink-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            TargetPath = Path.Combine(Root, "target.db");
        }

        public string Root { get; }
        public string TargetPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
