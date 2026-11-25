using System.Runtime.CompilerServices;
using System.Text.Json;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Example third-party Reader contributed by a separate plugin. It
/// knows only the public contracts: a JSON array of flat objects with a
/// record_id field, the same envelope the coordinator already persists.</summary>
public sealed class JsonRecordReader : IRecordReader
{
    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(file.FullPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("JSON reader expects an array of objects");
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("JSON reader expects object elements");
            }

            var values = new Dictionary<string, object?>();
            foreach (var property in item.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText(),
                };
            }
            yield return new RecordEnvelope(values, file.Identity, DateTimeOffset.UtcNow);
        }
    }
}
