using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Excel is a first-class import format, not a screenshot of CSV.
/// The workbooks are generated in-test as real OOXML packages and sent through
/// the same browser-facing commands as pasted CSV.</summary>
public sealed class ExcelImportTests
{
    [Fact]
    public async Task ExcelWorkbooksImportMachinesProfilesAndBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-excel-import-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api);
            Assert.True(await api.ExecuteAsync("industrial/import-machines", new JsonObject
            {
                ["xlsx"] = Workbook(
                    ["id", "name", "endpoint", "credentialId"],
                    [["excel-m-1", "Excel machine", "10.0.0.10", "excel-cred"]]),
            }));
            Assert.True(await api.ExecuteAsync("industrial/import-file-profiles", new JsonObject
            {
                ["xlsx"] = Workbook(
                    ["id", "name", "fileNamePattern", "format", "delimiter", "hasHeader", "encoding", "mapping", "required"],
                    [["excel-p-1", "Excel profile", "*.csv", "csv", ",", "true", "utf-8",
                      "value=reading", "value"]]),
            }));
            Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["xlsx"] = Workbook(
                    ["id", "name", "machineId", "workflowId", "workflowVersion", "fileProfileId",
                     "sourceResourceId", "targetResourceIds", "triggerKind", "cron"],
                    [["excel-b-1", "Excel binding", "excel-m-1", "excel-template", "2", "excel-p-1",
                      "excel-source", "target-db", "schedule", "0 4 * * *"]]),
            }));

            var machines = Assert.IsType<JsonArray>(api.Query("industrial/machines"));
            var machine = Assert.Single(machines,
                row => row!["id"]!.GetValue<string>() == "excel-m-1")!;
            Assert.Equal("Excel machine", machine["name"]!.GetValue<string>());
            Assert.Equal("10.0.0.10", machine["endpoint"]!.GetValue<string>());
            Assert.Equal("excel-cred", machine["credentialId"]!.GetValue<string>());

            var profiles = Assert.IsType<JsonArray>(api.Query("industrial/file-profiles"));
            var profile = Assert.Single(profiles,
                row => row!["id"]!.GetValue<string>() == "excel-p-1")!;
            Assert.Equal("*.csv", profile["fileNamePattern"]!.GetValue<string>());
            Assert.Equal("reading", profile["mapping"]!["value"]!.GetValue<string>());

            var bindings = Assert.IsType<JsonArray>(api.Query("industrial/bindings"));
            var binding = Assert.Single(bindings,
                row => row!["id"]!.GetValue<string>() == "excel-b-1")!;
            Assert.Equal(2, binding["workflowVersion"]!.GetValue<int>());
            Assert.Equal("schedule", binding["trigger"]!["kind"]!.GetValue<string>());
            Assert.Equal("0 4 * * *", binding["trigger"]!["cron"]!.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static string Workbook(string[] header, string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Import" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            var sheet = new StringBuilder();
            sheet.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            WriteRow(sheet, 1, header);
            for (var index = 0; index < rows.Length; index++) WriteRow(sheet, index + 2, rows[index]);
            sheet.AppendLine("</sheetData></worksheet>");
            Write(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void WriteRow(StringBuilder sheet, int rowNumber, string[] cells)
    {
        sheet.Append($"<row r=\"{rowNumber}\">");
        for (var column = 0; column < cells.Length; column++)
        {
            var cell = $"{ColumnName(column)}{rowNumber}";
            sheet.Append($"<c r=\"{cell}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(SecurityElement.Escape(cells[column]))
                .Append("</t></is></c>");
        }
        sheet.AppendLine("</row>");
    }

    private static string ColumnName(int index)
    {
        var value = string.Empty;
        do
        {
            value = (char)('A' + index % 26) + value;
            index = index / 26 - 1;
        } while (index >= 0);
        return value;
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        using var writer = new StreamWriter(entry, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static Runtime CreateRuntime(string root, out ApiGateway api)
    {
        var runtime = new Runtime();
        var console = new ConsoleHost(runtime);
        var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        runtime.LoadAsync(new ConsoleCapabilitiesComponent(console)).GetAwaiter().GetResult();
        runtime.LoadAsync(new TimerExtensionComponent()).GetAwaiter().GetResult();
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "false",
        });
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        return runtime;
    }
}
