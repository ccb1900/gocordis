using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class SinkWriteOptionsTests
{
    [Fact]
    public async Task ReadsWriteModeBusinessKeyAndResolvesBusinessIdentity()
    {
        var options = SinkWriteOptions.From(new Dictionary<string, object?>
        {
            ["writeMode"] = "upsert",
            ["businessKey"] = "serial",
        });

        Assert.Equal("upsert", options.WriteMode);
        Assert.Equal("serial", options.BusinessKey);
        var root = Path.Combine(Path.GetTempPath(), "industrial-sink-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "20261126.csv");
            await File.WriteAllTextAsync(path, "record_id,serial\n1,SN-1\n");
            var identity = FileIdentityFactory.Create(path, root, "source-1");
            var record = new RecordEnvelope(new Dictionary<string, object?>
            {
                ["record_id"] = "fallback", ["serial"] = "SN-1",
            }, identity, DateTimeOffset.UtcNow);
            Assert.EndsWith("|SN-1", options.ResolveId(record), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SameRecordIdFromTwoSourcesDoesNotCollideByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-sink-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var machineA = Path.Combine(root, "machine-a.csv");
            var machineB = Path.Combine(root, "machine-b.csv");
            await File.WriteAllTextAsync(machineA, "record_id,reading\n1,10\n");
            await File.WriteAllTextAsync(machineB, "record_id,reading\n1,20\n");
            var values = new Dictionary<string, object?> { ["record_id"] = "1", ["reading"] = "10" };
            var a = new RecordEnvelope(values,
                FileIdentityFactory.Create(machineA, root, "machine-a"), DateTimeOffset.UtcNow);
            var b = new RecordEnvelope(values,
                FileIdentityFactory.Create(machineB, root, "machine-b"), DateTimeOffset.UtcNow);

            var scoped = SinkWriteOptions.From(null);
            Assert.Equal(SinkWriteOptions.SourceScope, scoped.KeyScope);
            Assert.NotEqual(scoped.ResolveId(a), scoped.ResolveId(b));

            var global = SinkWriteOptions.From(new Dictionary<string, object?> { ["keyScope"] = "global" });
            Assert.Equal(global.ResolveId(a), global.ResolveId(b));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("bad", "append")]
    [InlineData("REPLACE", "replace")]
    [InlineData("upsert", "upsert")]
    public void NormalizesWriteMode(string supplied, string expected)
    {
        var options = SinkWriteOptions.From(new Dictionary<string, object?> { ["writeMode"] = supplied });
        Assert.Equal(expected, options.WriteMode);
    }
}
