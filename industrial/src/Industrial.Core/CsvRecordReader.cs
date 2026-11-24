using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Streaming CSV reader based on CsvHelper. It yields record refs, not
/// bulk variables; large files never enter the workflow engine's variable bag.</summary>
public sealed class CsvRecordReader : IRecordReader
{
    private readonly IReadOnlyDictionary<string, object?> _config;

    public CsvRecordReader(IReadOnlyDictionary<string, object?>? config = null) =>
        _config = config ?? new Dictionary<string, object?>();

    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var delimiter = _config.TryGetValue("delimiter", out var separator) && separator is string text && text.Length > 0
            ? text[0]
            : ',';
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = true,
            MissingFieldFound = null,
        };

        await using var stream = File.OpenRead(file.FullPath);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        var index = 0;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                values[header] = csv.GetField(header);
            }

            values.TryGetValue("record_id", out var recordId);
            values["record_id"] = recordId?.ToString() ?? $"{file.Identity.Key}#{index}";
            var recordTime = values.TryGetValue("record_time", out var rawTime) &&
                             DateTimeOffset.TryParse(rawTime?.ToString(), CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var parsed)
                ? parsed
                : file.SourceDate;
            values["record_time"] = recordTime.ToString("O", CultureInfo.InvariantCulture);

            yield return new RecordEnvelope(values, file.Identity, recordTime);
            index++;
        }
    }
}
