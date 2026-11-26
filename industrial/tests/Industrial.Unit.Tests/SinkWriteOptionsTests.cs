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
            Assert.Equal("SN-1", options.ResolveId(record));
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
