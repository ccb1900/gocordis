using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class MappingMetadataTests
{
    [Fact]
    public void MetadataRuleReadsRelativePathRatherThanParsingTheKey()
    {
        // The key deliberately carries a misleading date; the identity's
        // RelativePath is the only path metadata should inspect.
        var source = new FileIdentity(
            "source-1|wrong/19990101.csv|42|hash", "correct/20260101.csv",
            42, DateTimeOffset.UnixEpoch, "hash", "source-1");
        var record = new RecordEnvelope(
            new Dictionary<string, object?> { ["record_id"] = "1" }, source, DateTimeOffset.UtcNow);
        var transform = new MappingTransform(new Dictionary<string, object?>
        {
            ["mapping"] = new Dictionary<string, string>(),
            ["metadataRules"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["from"] = "path", ["pattern"] = "(?<value>\\d{8})",
                    ["key"] = "businessDate", ["required"] = false,
                },
            },
        });

        var mapped = transform.Apply(record);

        Assert.Equal("20260101", mapped.Values["businessDate"]);
    }

    [Fact]
    public void MetadataRegexIsBoundedSoAnOperatorPatternCannotStallACollection()
    {
        var source = new FileIdentity(
            "source-1|data.csv|42|hash", new string('a', 40) + ".csv",
            42, DateTimeOffset.UnixEpoch, "hash", "source-1");
        var record = new RecordEnvelope(
            new Dictionary<string, object?>(), source, DateTimeOffset.UtcNow);
        var transform = new MappingTransform(new Dictionary<string, object?>
        {
            ["mapping"] = new Dictionary<string, string>(),
            ["metadataRules"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["from"] = "name", ["pattern"] = "(a+)+$",
                    ["key"] = "never", ["required"] = false,
                },
            },
        });

        var mapped = transform.Apply(record);

        Assert.False(mapped.Values.ContainsKey("never"));
    }
}
