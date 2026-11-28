using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Streaming CSV reader driven by the node's own configuration, so the
/// designer's delimiter/encoding/header/column settings are real runtime
/// settings rather than display-only metadata.</summary>
public sealed class CsvRecordReader : IRecordReader
{
    private readonly IReadOnlyDictionary<string, object?> _config;

    public CsvRecordReader(IReadOnlyDictionary<string, object?>? config = null) =>
        _config = config ?? new Dictionary<string, object?>();

    public int Warnings { get; private set; }

    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = new NodeConfigReader(_config);
        var delimiter = config.GetString("delimiter", ",");
        if (delimiter.Length != 1) delimiter = ",";
        var hasHeader = config.GetBool("hasHeaderRecord", true);
        var encoding = Encoding.UTF8;
        try { encoding = Encoding.GetEncoding(config.GetString("encoding", "utf-8")); }
        catch (ArgumentException) { /* fall back to UTF-8 */ }
        var recordIdColumn = config.GetString("recordIdColumn", "record_id");
        var recordTimeColumn = config.GetString("recordTimeColumn", "record_time");
        var onBadData = config.GetString("onBadData", "drop");

        Warnings = 0;
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = hasHeader,
            MissingFieldFound = null,
            BadDataFound = _ =>
            {
                Warnings++;
                if (onBadData == "fail") throw new InvalidDataException("malformed CSV row");
            },
        };

        await using var stream = File.OpenRead(file.FullPath);
        using var reader = new StreamReader(stream, encoding);
        using var csv = new CsvReader(reader, csvConfig);
        if (!await csv.ReadAsync()) yield break;
        string[] headers;
        if (hasHeader)
        {
            csv.ReadHeader();
            headers = csv.HeaderRecord ?? [];
            if (!await csv.ReadAsync()) yield break;
        }
        else
        {
            headers = Enumerable.Range(1, 64)
                .Select(index => $"Column{index}").ToArray();
        }

        var index = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            // Always read by position. With HasHeaderRecord=false there is no
            // header row for CsvHelper to resolve "Column1" by name, so the
            // synthetic labels stay dictionary keys only.
            for (var column = 0; column < headers.Length; column++)
            {
                if (csv.TryGetField<string>(column, out var field))
                {
                    values[headers[column]] = field;
                }
            }

            values.TryGetValue(recordIdColumn, out var recordId);
            var generatedId = recordId?.ToString();
            if (string.IsNullOrWhiteSpace(generatedId)) generatedId = $"{file.Identity.Key}#{index}";
            values["record_id"] = generatedId;
            var recordTime = values.TryGetValue(recordTimeColumn, out var rawTime) &&
                             DateTimeOffset.TryParse(rawTime?.ToString(), CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var parsed)
                ? parsed
                : file.SourceDate;
            values["record_time"] = recordTime.ToString("O", CultureInfo.InvariantCulture);

            yield return new RecordEnvelope(values, file.Identity, recordTime);
            index++;
            if (!await csv.ReadAsync()) break;
        }
    }
}
