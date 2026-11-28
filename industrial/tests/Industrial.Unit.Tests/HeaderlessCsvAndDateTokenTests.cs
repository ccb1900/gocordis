using System.Globalization;
using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class HeaderlessCsvAndDateTokenTests
{
    [Fact]
    public async Task HeaderlessCsvReadsColumnsByPositionWithSyntheticKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-headerless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "20260101.csv");
            await File.WriteAllTextAsync(path,
                "7,42,2026-01-01T01:00:00Z\n8,43,2026-01-02T01:00:00Z\n");
            var identity = FileIdentityFactory.Create(path, root, "source-1");
            var file = new DiscoveredFile(identity, path, DateTimeOffset.UtcNow, "source-1");
            var reader = new CsvRecordReader(new Dictionary<string, object?>
            {
                ["hasHeaderRecord"] = false,
                ["recordIdColumn"] = "Column1",
                ["recordTimeColumn"] = "Column3",
            });

            var records = new List<RecordEnvelope>();
            await foreach (var record in reader.ReadAsync(file)) records.Add(record);

            Assert.Equal(2, records.Count);
            Assert.Equal("7", records[0].Values["Column1"]);
            Assert.Equal("42", records[0].Values["Column2"]);
            Assert.Equal("7", records[0].Values["record_id"]);
            Assert.Equal(DateTimeOffset.Parse("2026-01-01T01:00:00Z", CultureInfo.InvariantCulture).ToString("O"),
                records[0].Values["record_time"]);
            Assert.Equal("8", records[1].Values["Column1"]);
            Assert.Equal("8", records[1].Values["record_id"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("250101", "yyMMdd", 2025, 1, 1)]
    [InlineData("991231", "YYMMdd", 1999, 12, 31)]
    [InlineData("2025-01-01", "yyyy-MM-DD", 2025, 1, 1)]
    [InlineData("20260101", "YYYYmmDD", 2026, 1, 1)]
    public void DateTokensAreCaseInsensitiveAndPivotTwoDigitYears(
        string text, string pattern, int year, int month, int day)
    {
        var parsed = FileProfileDiscovery.ParseBusinessDate(text, pattern);
        Assert.NotNull(parsed);
        Assert.Equal(new DateOnly(year, month, day), parsed!.Value);
    }
}
