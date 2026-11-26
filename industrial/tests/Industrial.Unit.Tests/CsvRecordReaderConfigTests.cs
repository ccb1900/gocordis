using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class CsvRecordReaderConfigTests
{
    [Fact]
    public async Task NodeConfigurationControlsDelimiterHeaderAndIdentityColumns()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-csv-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "20261126.csv");
            await File.WriteAllTextAsync(path, "id;value;when\n7;42;2026-11-26T01:00:00Z\n");
            var identity = FileIdentityFactory.Create(path, root, "source-1");
            var file = new DiscoveredFile(identity, path, DateTimeOffset.UtcNow, "source-1");
            var reader = new CsvRecordReader(new Dictionary<string, object?>
            {
                ["delimiter"] = ";",
                ["hasHeaderRecord"] = true,
                ["recordIdColumn"] = "id",
                ["recordTimeColumn"] = "when",
            });

            var records = new List<RecordEnvelope>();
            await foreach (var record in reader.ReadAsync(file)) records.Add(record);

            var single = Assert.Single(records);
            Assert.Equal("7", single.Values["record_id"]);
            Assert.Equal("42", single.Values["value"]);
            Assert.Equal(DateTimeOffset.Parse("2026-11-26T01:00:00Z").ToString("O"),
                single.Values["record_time"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
