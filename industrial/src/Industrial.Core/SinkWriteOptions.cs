using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>One configuration contract shared by every SQL sink. The designer
/// exposes these fields for all database targets; every sink must honour them.</summary>
public sealed record SinkWriteOptions(string WriteMode, string? BusinessKey, string KeyScope = "source")
{
    public const string GlobalScope = "global";
    public const string SourceScope = "source";

    public static SinkWriteOptions From(IReadOnlyDictionary<string, object?>? config)
    {
        var reader = new NodeConfigReader(config);
        var mode = reader.GetString("writeMode", "append").Trim().ToLowerInvariant();
        var scope = reader.GetString("keyScope", SourceScope).Trim().ToLowerInvariant();
        return new SinkWriteOptions(
            mode is "append" or "upsert" or "replace" ? mode : "append",
            reader.GetString("businessKey"),
            scope is GlobalScope or SourceScope ? scope : SourceScope);
    }

    public string ResolveId(RecordEnvelope record)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(BusinessKey) &&
            record.Values.TryGetValue(BusinessKey!, out var business) &&
            business is not null &&
            !string.IsNullOrWhiteSpace(business.ToString()))
        {
            raw = business.ToString()!;
        }
        else
        {
            raw = record.Values.GetValueOrDefault("record_id")?.ToString()
                  ?? throw new InvalidOperationException("record_id is required for idempotent delivery");
        }
        // Two machines may emit the same record_id for different records. The
        // default identity scope is the source file identity (resource scoped),
        // so append/upsert can never silently merge unrelated devices.
        return KeyScope == GlobalScope || string.IsNullOrWhiteSpace(record.Source.Key)
            ? raw
            : $"{record.Source.Key}|{raw}";
    }
}
