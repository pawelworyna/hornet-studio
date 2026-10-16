using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using Moq;
using Xunit;

namespace HornetStudio.Tests.Services;

public sealed class ExportImportServiceExcelTests : IDisposable
{
    private readonly string _tempDir;

    public ExportImportServiceExcelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HornetStudio_ExcelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void GetColumnLetter_MapsIndicesCorrectly()
    {
        ExportImportService.GetColumnLetter(1).Should().Be("A");
        ExportImportService.GetColumnLetter(2).Should().Be("B");
        ExportImportService.GetColumnLetter(26).Should().Be("Z");
        ExportImportService.GetColumnLetter(27).Should().Be("AA");
        ExportImportService.GetColumnLetter(28).Should().Be("AB");
        ExportImportService.GetColumnLetter(52).Should().Be("AZ");
        ExportImportService.GetColumnLetter(53).Should().Be("BA");
        ExportImportService.GetColumnLetter(702).Should().Be("ZZ");
        ExportImportService.GetColumnLetter(703).Should().Be("AAA");
    }

    [Fact]
    public void EscapeXml_EscapesSpecialCharacters_AndRemovesControlCharacters()
    {
        var input = "AT&T <telecom> \"best\" 'value' \0\x05safe";
        var escaped = ExportImportService.EscapeXml(input);

        escaped.Should().Be("AT&amp;T &lt;telecom&gt; &quot;best&quot; &apos;value&apos; safe");
    }

    [Fact]
    public async Task ExportToExcelAsync_CreatesValidOpenXmlZipPackage()
    {
        var service = new ExportImportService();
        var outputPath = Path.Combine(_tempDir, "test_export.xlsx");

        var columns = new List<string> { "id", "name", "is_active", "salary", "created_at", "notes", "_hornet_hidden" };
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["id"] = 101,
                ["name"] = "Alice Smith & Co",
                ["is_active"] = true,
                ["salary"] = 8500.50,
                ["created_at"] = new DateTime(2026, 1, 15, 10, 30, 0),
                ["notes"] = null,
                ["_hornet_hidden"] = "secret"
            },
            new()
            {
                ["id"] = 102,
                ["name"] = "Bob <Dev>",
                ["is_active"] = false,
                ["salary"] = 9200,
                ["created_at"] = new DateTime(2026, 2, 20, 14, 0, 0),
                ["notes"] = "Rockstar",
                ["_hornet_hidden"] = "secret2"
            }
        };

        await service.ExportToExcelAsync(columns, rows, outputPath, sheetName: "Employees");

        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0);

        // Verify valid ZIP structure per OpenXML SpreadsheetML specification
        using var zip = ZipFile.OpenRead(outputPath);
        zip.GetEntry("[Content_Types].xml").Should().NotBeNull();
        zip.GetEntry("_rels/.rels").Should().NotBeNull();
        zip.GetEntry("xl/_rels/workbook.xml.rels").Should().NotBeNull();
        zip.GetEntry("xl/workbook.xml").Should().NotBeNull();
        zip.GetEntry("xl/styles.xml").Should().NotBeNull();

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheetEntry.Should().NotBeNull();

        using var reader = new StreamReader(sheetEntry!.Open());
        var sheetXml = await reader.ReadToEndAsync();

        // Check header row with bold style
        sheetXml.Should().Contain("<c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>id</t></is></c>");
        sheetXml.Should().Contain("<c r=\"B1\" t=\"inlineStr\" s=\"1\"><is><t>name</t></is></c>");
        sheetXml.Should().Contain("<c r=\"C1\" t=\"inlineStr\" s=\"1\"><is><t>is_active</t></is></c>");
        sheetXml.Should().NotContain("_hornet_hidden");

        // Check row 2 values
        sheetXml.Should().Contain("<c r=\"A2\"><v>101</v></c>");
        sheetXml.Should().Contain("<c r=\"B2\" t=\"inlineStr\"><is><t>Alice Smith &amp; Co</t></is></c>");
        sheetXml.Should().Contain("<c r=\"C2\" t=\"b\"><v>1</v></c>");
        sheetXml.Should().Contain("<c r=\"D2\"><v>8500.5</v></c>");

        // Check row 3 values
        sheetXml.Should().Contain("<c r=\"A3\"><v>102</v></c>");
        sheetXml.Should().Contain("<c r=\"B3\" t=\"inlineStr\"><is><t>Bob &lt;Dev&gt;</t></is></c>");
        sheetXml.Should().Contain("<c r=\"C3\" t=\"b\"><v>0</v></c>");
    }

    [Fact]
    public async Task ExportAsync_WithXlsxFormat_ExportsQueryResultSuccessfully()
    {
        var service = new ExportImportService();
        var outputPath = Path.Combine(_tempDir, "query_export.xlsx");

        var mockClient = new Mock<IDatabaseClient>();
        var queryResult = new QueryResult
        {
            IsSuccess = true,
            Columns =
            [
                new QueryResultColumn { Name = "product_id", Type = "INT", Ordinal = 0 },
                new QueryResultColumn { Name = "product_name", Type = "VARCHAR", Ordinal = 1 },
                new QueryResultColumn { Name = "price", Type = "DECIMAL", Ordinal = 2 }
            ],
            Rows =
            [
                [1, "Laptop Pro", 1299.99m],
                [2, "Wireless Mouse", 29.50m]
            ]
        };

        mockClient.Setup(c => c.ExecuteQueryAsync("SELECT * FROM products", It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        await service.ExportAsync(mockClient.Object, "SELECT * FROM products", ExportFormat.Xlsx, outputPath, "Products");

        File.Exists(outputPath).Should().BeTrue();
        using var zip = ZipFile.OpenRead(outputPath);
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
    }
}
