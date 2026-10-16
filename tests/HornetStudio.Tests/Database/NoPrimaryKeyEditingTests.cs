using FluentAssertions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using Xunit;

namespace HornetStudio.Tests.Database;

public class NoPrimaryKeyEditingTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseClient _client;

    public NoPrimaryKeyEditingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hornet_nopk_test_{Guid.NewGuid()}.db");
        var config = new ConnectionConfig
        {
            Name = "No-PK Test",
            ConnectionType = ConnectionType.Sqlite,
            DatabasePath = _dbPath
        };
        _client = new SqliteDatabaseClient(config);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CanEditAndFilter_TableWithoutPrimaryKey_UsingRowId()
    {
        await _client.ConnectAsync();

        // 1. Tabela bez jawnego PRIMARY KEY
        var ddl = "CREATE TABLE server_logs (level TEXT, message TEXT, timestamp TEXT);";
        await _client.ExecuteQueryAsync(ddl);

        // 2. Wstawianie danych testowych
        await _client.ExecuteQueryAsync(@"
            INSERT INTO server_logs VALUES ('INFO', 'Server started', '2026-10-15 10:00:00');
            INSERT INTO server_logs VALUES ('WARN', 'Disk usage high', '2026-10-15 10:05:00');
            INSERT INTO server_logs VALUES ('ERROR', 'Database timeout', '2026-10-15 10:10:00');
        ");

        // 3. Weryfikacja metadanych: brak PK
        var columns = await _client.ListTableColumnsAsync("server_logs");
        columns.Any(c => c.IsPrimaryKey).Should().BeFalse();

        // 4. SelectTopAsync: powinien automatycznie wykryć tryb RowId i wyciągnąć _hornet_rowid
        var select = await _client.SelectTopAsync(new SelectTopOptions { Table = "server_logs" });
        select.IdentifierMode.Should().Be(RowIdentifierMode.RowId);
        select.Rows.Should().HaveCount(3);
        select.Rows[0].Should().ContainKey("_hornet_rowid");

        var secondRow = select.Rows[1];
        secondRow["message"].Should().Be("Disk usage high");
        var rowId = Convert.ToInt64(secondRow["_hornet_rowid"]);

        // 5. Edycja wiersza (Staged Update) przy użyciu RowId
        var updateChanges = new TableChanges
        {
            Table = "server_logs",
            IdentifierMode = RowIdentifierMode.RowId,
            Updates =
            [
                new TableUpdate
                {
                    RowId = rowId,
                    OriginalRow = secondRow,
                    UpdatedValues = new() { ["message"] = "Disk usage NORMAL (RESOLVED)" }
                }
            ]
        };

        var updateResult = await _client.ApplyChangesAsync(updateChanges);
        updateResult.Success.Should().BeTrue();
        updateResult.AffectedRows.Should().Be(1);

        // Weryfikacja w bazie
        var checkUpdated = await _client.ExecuteQueryAsync("SELECT message FROM server_logs WHERE rowid = " + rowId);
        checkUpdated.Rows[0][0].Should().Be("Disk usage NORMAL (RESOLVED)");

        // 6. Usunięcie wiersza (Staged Delete) przy użyciu RowId
        var deleteChanges = new TableChanges
        {
            Table = "server_logs",
            IdentifierMode = RowIdentifierMode.RowId,
            Deletes =
            [
                new TableDelete
                {
                    RowId = rowId,
                    OriginalRow = secondRow
                }
            ]
        };

        var deleteResult = await _client.ApplyChangesAsync(deleteChanges);
        deleteResult.Success.Should().BeTrue();

        var checkDeleted = await _client.ExecuteQueryAsync("SELECT count(*) FROM server_logs");
        checkDeleted.Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public async Task CanEdit_TableWithoutPrimaryKey_UsingAllColumnsMatching()
    {
        await _client.ConnectAsync();

        // Tabela bez jawnego PK w celu przetestowania AllColumns Matching
        var ddlNoPk = "CREATE TABLE metric_snapshots (metric_name TEXT, recorded_val REAL, tag TEXT);";
        await _client.ExecuteQueryAsync(ddlNoPk);

        await _client.ExecuteQueryAsync(@"
            INSERT INTO metric_snapshots VALUES ('cpu_usage', 42.5, 'host-a');
            INSERT INTO metric_snapshots VALUES ('memory_usage', 85.0, 'host-a');
        ");

        var originalRow = new Dictionary<string, object?>
        {
            ["metric_name"] = "cpu_usage",
            ["recorded_val"] = 42.5,
            ["tag"] = "host-a"
        };

        // Wymuszony tryb AllColumns Matching
        var changes = new TableChanges
        {
            Table = "metric_snapshots",
            IdentifierMode = RowIdentifierMode.AllColumns,
            Updates =
            [
                new TableUpdate
                {
                    OriginalRow = originalRow,
                    UpdatedValues = new() { ["recorded_val"] = 55.0 }
                }
            ]
        };

        var res = await _client.ApplyChangesAsync(changes);
        res.Success.Should().BeTrue();
        res.AffectedRows.Should().Be(1);

        var verify = await _client.ExecuteQueryAsync("SELECT recorded_val FROM metric_snapshots WHERE metric_name = 'cpu_usage'");
        verify.Rows[0][0].Should().Be(55.0);
    }
}
