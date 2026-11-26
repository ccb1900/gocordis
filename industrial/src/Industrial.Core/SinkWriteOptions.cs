using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>One configuration contract shared by every SQL sink. The designer
/// exposes these fields for all database targets; every sink must honour them.</summary>
public sealed record SinkWriteOptions(string WriteMode, string? BusinessKey)
{
    public static SinkWriteOptions From(IReadOnlyDictionary<string, object?>? config)
    {
        var reader = new NodeConfigReader(config);
        var mode = reader.GetString("writeMode", "append").Trim().ToLowerInvariant();
        return new SinkWriteOptions(
            mode is "append" or "upsert" or "replace" ? mode : "append",
            reader.GetString("businessKey"));
    }

    public string ResolveId(RecordEnvelope record)
    {
        if (!string.IsNullOrWhiteSpace(BusinessKey) &&
            record.Values.TryGetValue(BusinessKey!, out var business) &&
            business is not null &&
            !string.IsNullOrWhiteSpace(business.ToString()))
        {
            return business.ToString()!;
        }
        return record.Values.GetValueOrDefault("record_id")?.ToString()
               ?? throw new InvalidOperationException("record_id is required for idempotent delivery");
    }
}
