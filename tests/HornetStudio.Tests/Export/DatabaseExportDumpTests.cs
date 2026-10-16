using FluentAssertions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Desktop.ViewModels.Tabs;
using Xunit;

namespace HornetStudio.Tests.Export;

public class DatabaseExportDumpTests : IAsyncDisposable
{
    private readonly string _srcDbPath;
    private readonly string _destDbPath;
    private readonly SqliteDatabaseClient _srcClient;
    private readonly SqliteDatabaseClient _destClient;
    private readonly BackupRestoreService _service;
    private readonly string _tempDir;

    public DatabaseExportDumpTests()
    {
        _srcDbPath = Path.Combine(Path.GetTempPath(), $"hornet_src_{Guid.NewGuid()}.db");
        _destDbPath = Path.Combine(Path.GetTempPath(), $"hornet_dest_{Guid.NewGuid()}.db");

        _srcClient = new SqliteDatabaseClient(new ConnectionConfig
        {
            ConnectionType = ConnectionType.Sqlite,
            DatabasePath = _srcDbPath
        });

        _destClient = new SqliteDatabaseClient(new ConnectionConfig
        {
            ConnectionType = ConnectionType.Sqlite,
            DatabasePath = _destDbPath
        });

        _service = new BackupRestoreService();
        _tempDir = Path.Combine(Path.GetTempPath(), "HornetDbDump_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        await _srcClient.DisposeAsync();
        await _destClient.DisposeAsync();
        if (File.Exists(_srcDbPath)) try { File.Delete(_srcDbPath); } catch { }
        if (File.Exists(_destDbPath)) try { File.Delete(_destDbPath); } catch { }
        if (Directory.Exists(_tempDir)) try { Directory.Delete(_tempDir, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private async Task SeedSourceDatabaseAsync()
    {
        await _srcClient.ConnectAsync();
        await _srcClient.ExecuteQueryAsync(@"
            CREATE TABLE authors (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                country TEXT
            );

            CREATE TABLE books (
                id INTEGER PRIMARY KEY,
                author_id INTEGER,
                title TEXT NOT NULL,
                price REAL
            );

            CREATE INDEX idx_books_author ON books (author_id);

            INSERT INTO authors VALUES (1, 'Stanislaw Lem', 'Poland');
            INSERT INTO authors VALUES (2, 'Philip K. Dick', 'USA');

            INSERT INTO books VALUES (10, 1, 'Solaris', 39.99);
            INSERT INTO books VALUES (20, 1, 'Cyberiad', 29.50);
            INSERT INTO books VALUES (30, 2, 'Ubik', 34.00);
        ");
    }

    [Fact]
    public async Task CanExportFullDatabase_StructureAndData_AndRestoreIntoNewDb()
    {
        await SeedSourceDatabaseAsync();

        var dumpFile = Path.Combine(_tempDir, "full_database_dump.sql");
        var options = new DatabaseExportOptions
        {
            Mode = DatabaseExportMode.StructureAndData,
            Format = DatabaseExportFormat.SqlDump,
            IncludeDropStatements = true,
            IncludeIndexes = true
        };

        await _service.ExportDatabaseAsync(_srcClient, dumpFile, options);

        File.Exists(dumpFile).Should().BeTrue();
        var sqlContent = await File.ReadAllTextAsync(dumpFile);

        sqlContent.Should().Contain("HornetStudio Database Export");
        sqlContent.Should().Contain("DROP TABLE IF EXISTS \"authors\"");
        sqlContent.Should().Contain("CREATE TABLE");
        sqlContent.Should().Contain("INSERT INTO \"authors\"");
        sqlContent.Should().Contain("Stanislaw Lem");
        sqlContent.Should().Contain("Solaris");
        sqlContent.Should().Contain("CREATE INDEX");
        sqlContent.Should().Contain("COMMIT;");

        // Now restore this dump into the destination empty database
        await _destClient.ConnectAsync();
        await _service.RestoreAsync(_destClient, dumpFile);

        var verifyAuthors = await _destClient.ExecuteQueryAsync("SELECT count(*) FROM authors");
        verifyAuthors.Rows[0][0].Should().Be(2L);

        var verifyBooks = await _destClient.ExecuteQueryAsync("SELECT count(*) FROM books");
        verifyBooks.Rows[0][0].Should().Be(3L);
    }

    [Fact]
    public async Task CanExportDatabase_StructureOnly_NoInserts()
    {
        await SeedSourceDatabaseAsync();

        var ddlFile = Path.Combine(_tempDir, "schema_only.sql");
        var options = new DatabaseExportOptions
        {
            Mode = DatabaseExportMode.StructureOnly,
            Format = DatabaseExportFormat.SqlDump
        };

        await _service.ExportDatabaseAsync(_srcClient, ddlFile, options);

        File.Exists(ddlFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(ddlFile);
        content.Should().Contain("CREATE TABLE");
        content.Should().NotContain("INSERT INTO");
    }

    [Fact]
    public async Task CanExportDatabase_SelectedTablesOnly()
    {
        await SeedSourceDatabaseAsync();

        var partialDump = Path.Combine(_tempDir, "only_authors.sql");
        var options = new DatabaseExportOptions
        {
            Mode = DatabaseExportMode.StructureAndData,
            Format = DatabaseExportFormat.SqlDump,
            SelectedTables = new List<string> { "authors" }
        };

        await _service.ExportDatabaseAsync(_srcClient, partialDump, options);

        var content = await File.ReadAllTextAsync(partialDump);
        content.Should().Contain("\"authors\"");
        content.Should().NotContain("\"books\"");
    }

    [Fact]
    public async Task BackupRestoreViewModel_LoadsTablesAndExecutesFullExport()
    {
        await SeedSourceDatabaseAsync();

        var dumpFile = Path.Combine(_tempDir, "vm_dump.sql");
        var vm = new BackupRestoreViewModel(_srcClient, _service)
        {
            BackupDestinationPath = dumpFile,
            ExportMode = DatabaseExportMode.StructureAndData
        };

        // Allow LoadTablesAsync to complete
        await Task.Delay(200);

        vm.AvailableTables.Should().NotBeEmpty();
        vm.AvailableTables.Any(t => t.TableName == "authors").Should().BeTrue();

        await vm.RunBackupAsync();

        vm.IsSuccess.Should().BeTrue();
        vm.StatusMessage.Should().Contain("Database export completed successfully");
        File.Exists(dumpFile).Should().BeTrue();
    }
}
