using FluentAssertions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using Xunit;

namespace HornetStudio.Tests.Database;

public class SqliteIntegrationTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseClient _client;

    public SqliteIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hornet_sqlite_test_{Guid.NewGuid()}.db");
        var config = new ConnectionConfig
        {
            Name = "SQLite Test",
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
    public async Task CompleteLifecycle_Connect_Schema_Query_Paging()
    {
        await _client.ConnectAsync();
        _client.IsConnected.Should().BeTrue();

        var version = await _client.GetVersionAsync();
        version.Should().StartWith("3.");

        // 1. DDL: Tworzenie tabeli klientów
        var ddl = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT,
                balance REAL DEFAULT 0.0
            );
            CREATE VIEW active_customers AS SELECT * FROM customers WHERE balance > 0;
            CREATE INDEX idx_customers_email ON customers(email);
        ";
        var ddlResult = await _client.ExecuteQueryAsync(ddl);
        ddlResult.IsSuccess.Should().BeTrue();

        // 2. Metadane
        var tables = await _client.ListTablesAsync();
        tables.Should().Contain(t => t.Name == "customers");

        var views = await _client.ListViewsAsync();
        views.Should().Contain(v => v.Name == "active_customers");

        var columns = await _client.ListTableColumnsAsync("customers");
        columns.Should().HaveCount(4);
        columns.First(c => c.ColumnName == "id").IsPrimaryKey.Should().BeTrue();

        var indexes = await _client.ListTableIndexesAsync("customers");
        indexes.Should().Contain(i => i.Name == "idx_customers_email");

        // 3. Staged Changes (Inserts)
        var changes = new TableChanges
        {
            Table = "customers",
            Inserts =
            [
                new TableInsert { Values = new() { ["name"] = "Alice", ["email"] = "alice@hornet.io", ["balance"] = 150.50 } },
                new TableInsert { Values = new() { ["name"] = "Bob", ["email"] = "bob@hornet.io", ["balance"] = 0.0 } },
                new TableInsert { Values = new() { ["name"] = "Charlie", ["email"] = null, ["balance"] = 300.0 } }
            ]
        };

        var updateResult = await _client.ApplyChangesAsync(changes);
        updateResult.Success.Should().BeTrue();
        updateResult.AffectedRows.Should().Be(3);

        // 4. SelectTop z filtrami i sortowaniem
        var topOptions = new SelectTopOptions
        {
            Table = "customers",
            Limit = 10,
            Offset = 0,
            OrderBy = [new OrderBy { Field = "balance", Direction = SortDirection.Descending }],
            Filters = [new TableFilter { Field = "balance", Operator = FilterOperator.GreaterThan, Value = 50.0 }]
        };

        var selectResult = await _client.SelectTopAsync(topOptions);
        selectResult.Rows.Should().HaveCount(2); // Charlie (300) i Alice (150.5)
        selectResult.Rows[0]["name"].Should().Be("Charlie");
        selectResult.Rows[1]["name"].Should().Be("Alice");

        // 5. Test filtra IS NULL
        var nullFilterOptions = new SelectTopOptions
        {
            Table = "customers",
            Filters = [new TableFilter { Field = "email", Operator = FilterOperator.IsNull }]
        };
        var nullResult = await _client.SelectTopAsync(nullFilterOptions);
        nullResult.Rows.Should().HaveCount(1);
        nullResult.Rows[0]["name"].Should().Be("Charlie");

        // 5b. Test Raw SQL filter
        var rawFilterOptions = new SelectTopOptions
        {
            Table = "customers",
            RawWhereClause = "balance > 100 AND name LIKE 'A%'"
        };
        var rawResult = await _client.SelectTopAsync(rawFilterOptions);
        rawResult.Rows.Should().HaveCount(1);
        rawResult.Rows[0]["name"].Should().Be("Alice");

        // 5c. Test filtra IN
        var inFilterOptions = new SelectTopOptions
        {
            Table = "customers",
            Filters = [new TableFilter { Field = "name", Operator = FilterOperator.In, Value = "Alice, Bob" }]
        };
        var inResult = await _client.SelectTopAsync(inFilterOptions);
        inResult.Rows.Should().HaveCount(2);

        // 5d. Test filtrów z operatorem OR
        var orFilterOptions = new SelectTopOptions
        {
            Table = "customers",
            Filters =
            [
                new TableFilter { Field = "name", Operator = FilterOperator.Equals, Value = "Charlie" },
                new TableFilter { LogicalOp = "OR", Field = "balance", Operator = FilterOperator.LessThan, Value = 50.0 }
            ]
        };
        var orResult = await _client.SelectTopAsync(orFilterOptions);
        orResult.Rows.Should().HaveCount(2); // Charlie (300) i Bob (25.0)

        // 6. Staged Changes (Update & Delete)
        var aliceRow = selectResult.Rows[1];
        var updateChanges = new TableChanges
        {
            Table = "customers",
            Updates =
            [
                new TableUpdate
                {
                    OriginalRow = aliceRow,
                    UpdatedValues = new() { ["balance"] = 999.99 }
                }
            ],
            Deletes =
            [
                new TableDelete
                {
                    OriginalRow = selectResult.Rows[0] // Usuń Charliego
                }
            ]
        };

        var modResult = await _client.ApplyChangesAsync(updateChanges);
        modResult.Success.Should().BeTrue();

        // Sprawdź stan po modyfikacji
        var verifyQuery = await _client.ExecuteQueryAsync("SELECT count(*) FROM customers WHERE balance > 900");
        verifyQuery.Rows[0][0].Should().Be(1L);

        var verifyCount = await _client.ExecuteQueryAsync("SELECT count(*) FROM customers");
        verifyCount.Rows[0][0].Should().Be(2L); // 3 - 1 = 2
    }
}
