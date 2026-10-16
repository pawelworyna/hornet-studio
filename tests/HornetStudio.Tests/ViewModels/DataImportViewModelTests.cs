using FluentAssertions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Desktop.ViewModels.Tabs;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public class DataImportViewModelTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseClient _client;
    private readonly ExportImportService _service;
    private readonly string _tempDir;

    public DataImportViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hornet_importvm_{Guid.NewGuid()}.db");
        _client = new SqliteDatabaseClient(new ConnectionConfig
        {
            ConnectionType = ConnectionType.Sqlite,
            DatabasePath = _dbPath
        });
        _service = new ExportImportService();
        _tempDir = Path.Combine(Path.GetTempPath(), "HornetImportVmFiles_" + Guid.NewGuid());
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
    public async Task DataImportViewModel_AutoDetectsJsonAndImportsSuccessfully()
    {
        await _client.ConnectAsync();
        await _client.ExecuteQueryAsync(@"
            CREATE TABLE team_members (
                id INTEGER PRIMARY KEY,
                handle TEXT,
                role TEXT
            );
        ");

        var jsonPath = Path.Combine(_tempDir, "team.json");
        await File.WriteAllTextAsync(jsonPath, @"[
            { ""id"": 1, ""handle"": ""hornet_lead"", ""role"": ""Architect"" },
            { ""id"": 2, ""handle"": ""dev_green"", ""role"": ""Engineer"" }
        ]");

        var vm = new DataImportViewModel(_client, _service, "team_members")
        {
            SourceFilePath = jsonPath
        };

        vm.IsJsonFile.Should().BeTrue();
        vm.ImportButtonText.Should().Contain("JSON");

        // Test Preview
        await vm.LoadPreviewAsync();
        vm.PreviewColumns.Should().Contain(new[] { "id", "handle", "role" });
        vm.PreviewRows.Should().HaveCount(2);

        // Test Execute Import
        await vm.ExecuteImportAsync();
        vm.IsSuccess.Should().BeTrue();
        vm.RowsImported.Should().Be(2);
        vm.StatusMessage.Should().Contain("Successfully imported 2 rows from JSON");

        // Verify in DB
        var result = await _client.ExecuteQueryAsync("SELECT count(*) FROM team_members");
        result.Rows[0][0].Should().Be(2L);
    }
}
