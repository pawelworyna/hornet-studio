using FluentAssertions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using Xunit;

namespace HornetStudio.Tests.Export;

public class ExportImportTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseClient _client;
    private readonly ExportImportService _service;
    private readonly string _tempDir;

    public ExportImportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hornet_exp_test_{Guid.NewGuid()}.db");
        _client = new SqliteDatabaseClient(new ConnectionConfig
        {
            ConnectionType = ConnectionType.Sqlite,
            DatabasePath = _dbPath
        });
        _service = new ExportImportService();
        _tempDir = Path.Combine(Path.GetTempPath(), "HornetExpFiles_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        if (File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { }
        if (Directory.Exists(_tempDir)) try { Directory.Delete(_tempDir, true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CanExport_AndImportCsv()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT,
                description TEXT,
                price REAL
            );
            INSERT INTO products VALUES (1, 'Hornet Drone', 'Fast, green drone', 499.99);
            INSERT INTO products VALUES (2, 'SQL Battery', 'Long-lasting ""pro"" power', 89.50);
        ");

        var csvFile = Path.Combine(_tempDir, "products.csv");
        await _service.ExportAsync(_client, "SELECT * FROM products", ExportFormat.Csv, csvFile);

        File.Exists(csvFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(csvFile);
        content.Should().Contain("Hornet Drone");
        content.Should().Contain("\"Fast, green drone\"");
        content.Should().Contain("\"Long-lasting \"\"pro\"\" power\"");

        // Import do nowej tabeli
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE products_copy (
                id INTEGER,
                name TEXT,
                description TEXT,
                price REAL
            );
        ");

        var importedRows = await _service.ImportCsvAsync(_client, "products_copy", null, csvFile);
        importedRows.Should().Be(2);

        var verify = await _client.ExecuteQueryAsync("SELECT count(*) FROM products_copy");
        verify.Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public async Task CanExport_ToJsonAndSql()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE tags (id INT, tag_name TEXT);
            INSERT INTO tags VALUES (1, 'database'), (2, 'avalonia');
        ");

        var jsonFile = Path.Combine(_tempDir, "tags.json");
        await _service.ExportAsync(_client, "SELECT * FROM tags", ExportFormat.Json, jsonFile);
        File.Exists(jsonFile).Should().BeTrue();
        var json = await File.ReadAllTextAsync(jsonFile);
        json.Should().Contain("\"tag_name\": \"database\"");

        var sqlFile = Path.Combine(_tempDir, "tags.sql");
        await _service.ExportAsync(_client, "SELECT * FROM tags", ExportFormat.Sql, sqlFile, "imported_tags");
        File.Exists(sqlFile).Should().BeTrue();
        var sql = await File.ReadAllTextAsync(sqlFile);
        sql.Should().Contain("INSERT INTO \"imported_tags\"");
        sql.Should().Contain("'database'");
    }

    [Fact]
    public async Task CanImportJson_FromArrayOfObjects()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                points REAL
            );
        ");

        var jsonPath = Path.Combine(_tempDir, "customers.json");
        var jsonContent = @"[
            { ""id"": 10, ""name"": ""John Hornet"", ""email"": ""john@hornet.io"", ""points"": 150.5 },
            { ""id"": 20, ""name"": ""Jane Developer"", ""email"": ""jane@test.org"", ""points"": 320.0 }
        ]";
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        // Preview JSON
        var (headers, previewRows) = await _service.PreviewJsonAsync(jsonPath, maxRows: 5);
        headers.Should().Contain(new[] { "id", "name", "email", "points" });
        previewRows.Should().HaveCount(2);

        // Import JSON
        var importedCount = await _service.ImportJsonAsync(_client, "customers", null, jsonPath);
        importedCount.Should().Be(2);

        var check = await _client.ExecuteQueryAsync("SELECT * FROM customers ORDER BY id");
        check.Rows.Should().HaveCount(2);
        check.Rows[0][1].Should().Be("John Hornet");
        check.Rows[1][1].Should().Be("Jane Developer");
    }

    [Fact]
    public async Task CanImportJson_FromWrappedObject()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE telemetry (
                metric TEXT,
                value REAL
            );
        ");

        var jsonPath = Path.Combine(_tempDir, "telemetry.json");
        var jsonContent = @"{
            ""status"": ""success"",
            ""data"": [
                { ""metric"": ""cpu_load"", ""value"": 42.5 },
                { ""metric"": ""mem_load"", ""value"": 68.1 }
            ]
        }";
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        var count = await _service.ImportJsonAsync(_client, "telemetry", null, jsonPath);
        count.Should().Be(2);

        var check = await _client.ExecuteQueryAsync("SELECT count(*) FROM telemetry");
        check.Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public async Task CanExportToSql_AndExecuteDirectly()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE employees (id INT, full_name TEXT, department TEXT);
            INSERT INTO employees VALUES (1, 'Eve O''Connor', 'Engineering');
            INSERT INTO employees VALUES (2, 'Bob Builder', NULL);
        ");

        var sqlPath = Path.Combine(_tempDir, "employees.sql");
        await _service.ExportTableAsync(_client, "employees", null, ExportFormat.Sql, sqlPath);

        File.Exists(sqlPath).Should().BeTrue();
        var sql = await File.ReadAllTextAsync(sqlPath);
        sql.Should().Contain("BEGIN TRANSACTION;");
        sql.Should().Contain("COMMIT;");
        sql.Should().Contain("INSERT INTO \"employees\"");
        sql.Should().Contain("Eve O''Connor"); // Escaped apostrophe
        sql.Should().Contain("NULL");

        // Now test executing this script into a new table
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE employees_restored (id INT, full_name TEXT, department TEXT);
        ");
        var sqlToRun = sql.Replace("\"employees\"", "\"employees_restored\"");
        await _client.ExecuteQueryAsync(sqlToRun);

        var verify = await _client.ExecuteQueryAsync("SELECT count(*) FROM employees_restored");
        verify.Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public async Task CanExportTable_WithFilters()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE inventory (id INT, item TEXT, qty INT);
            INSERT INTO inventory VALUES (1, 'Apples', 50);
            INSERT INTO inventory VALUES (2, 'Oranges', 10);
            INSERT INTO inventory VALUES (3, 'Bananas', 80);
        ");

        var filteredJsonPath = Path.Combine(_tempDir, "inventory_filtered.json");
        var filters = new List<TableFilter>
        {
            new TableFilter { Field = "qty", Operator = FilterOperator.GreaterThan, Value = "20" }
        };

        await _service.ExportTableAsync(_client, "inventory", null, ExportFormat.Json, filteredJsonPath, filters);

        File.Exists(filteredJsonPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(filteredJsonPath);
        json.Should().Contain("Apples");
        json.Should().Contain("Bananas");
        json.Should().NotContain("Oranges");
    }
}
