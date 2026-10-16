using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class SidebarViewModelContextMenuTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly HornetDbContext _dbContext;

    public SidebarViewModelContextMenuTests()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new HornetDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CopyTableNameAsync_Copies_Name_To_Clipboard()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockClipboard = new Mock<IClipboardService>();
        var table = new TableOrView("customers", "crm", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { },
            mockClipboard.Object);

        await vm.CopyTableNameCommand.ExecuteAsync(table);

        mockClipboard.Verify(c => c.SetTextAsync("customers"), Times.Once);
    }

    [Fact]
    public async Task CopyQualifiedNameAsync_Formats_Schema_And_Table()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");

        var mockClipboard = new Mock<IClipboardService>();
        var table = new TableOrView("orders", "public", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { },
            mockClipboard.Object);

        await vm.CopyQualifiedNameCommand.ExecuteAsync(table);

        mockClipboard.Verify(c => c.SetTextAsync("\"public\".\"orders\""), Times.Once);
    }

    [Fact]
    public void GenerateSelectQuery_Opens_Select_Limit_100()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");

        string? openedTitle = null;
        string? openedSql = null;
        var table = new TableOrView("products", "store", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (title, sql) => { openedTitle = title; openedSql = sql; });

        vm.GenerateSelectQuery(table);

        openedTitle.Should().Be("SELECT: products");
        openedSql.Should().Be("SELECT * FROM \"store\".\"products\" LIMIT 100;");
    }

    [Fact]
    public void GenerateSelectQuery_For_SqlServer_Uses_Top_100()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.SqlServer);
        mockClient.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"[{s}]");

        string? openedSql = null;
        var table = new TableOrView("invoices", "dbo", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => openedSql = sql);

        vm.GenerateSelectQuery(table);

        openedSql.Should().Be("SELECT TOP 100 * FROM [dbo].[invoices];");
    }

    [Fact]
    public void GenerateSelectQuery_For_MongoDb_Uses_Find_Limit()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.MongoDb);

        string? openedSql = null;
        var table = new TableOrView("events", null, null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => openedSql = sql);

        vm.GenerateSelectQuery(table);

        openedSql.Should().Be("db.events.find().limit(100)");
    }

    [Fact]
    public async Task GenerateInsertQueryAsync_Generates_Columns_Template()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");
        mockClient.Setup(c => c.ListTableColumnsAsync("users", "auth", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtendedTableColumn>
            {
                new() { ColumnName = "id", DataType = "integer", IsAutoIncrement = true },
                new() { ColumnName = "email", DataType = "text" },
                new() { ColumnName = "created_at", DataType = "timestamp" }
            });

        string? openedSql = null;
        var table = new TableOrView("users", "auth", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => openedSql = sql);

        await vm.GenerateInsertQueryCommand.ExecuteAsync(table);

        openedSql.Should().Contain("INSERT INTO \"auth\".\"users\" (\"email\", \"created_at\")");
        openedSql.Should().Contain("/* text */ NULL");
    }

    [Fact]
    public async Task GenerateCreateScriptAsync_Fetches_Ddl_From_Client()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.GenerateCreateTableSqlAsync("logs", "public", It.IsAny<CancellationToken>()))
            .ReturnsAsync("CREATE TABLE \"public\".\"logs\" (id int, msg text);");

        string? openedTitle = null;
        string? openedSql = null;
        var table = new TableOrView("logs", "public", null, "table");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (title, sql) => { openedTitle = title; openedSql = sql; });

        await vm.GenerateCreateScriptCommand.ExecuteAsync(table);

        openedTitle.Should().Be("DDL: logs");
        openedSql.Should().Be("CREATE TABLE \"public\".\"logs\" (id int, msg text);");
    }

    [Fact]
    public void GenerateTruncateQuery_Uses_Delete_For_Sqlite_And_Truncate_For_Others()
    {
        var mockClientSqlite = new Mock<IDatabaseClient>();
        mockClientSqlite.Setup(c => c.ConnectionType).Returns(ConnectionType.Sqlite);
        mockClientSqlite.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");

        string? sqliteSql = null;
        var table = new TableOrView("cache", null, null, "table");

        var vmSqlite = new SidebarViewModel(
            mockClientSqlite.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => sqliteSql = sql);

        vmSqlite.GenerateTruncateQuery(table);
        sqliteSql.Should().Contain("DELETE FROM \"cache\";");

        var mockClientPg = new Mock<IDatabaseClient>();
        mockClientPg.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClientPg.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");

        string? pgSql = null;
        var vmPg = new SidebarViewModel(
            mockClientPg.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => pgSql = sql);

        vmPg.GenerateTruncateQuery(table);
        pgSql.Should().Contain("TRUNCATE TABLE \"cache\";");
    }

    [Fact]
    public void GenerateDropQuery_Distinguishes_Table_And_View()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.WrapIdentifier(It.IsAny<string>())).Returns<string>(s => $"\"{s}\"");

        string? dropTableSql = null;
        string? dropViewSql = null;

        var table = new TableOrView("accounts", "public", null, "table");
        var view = new TableOrView("active_accounts", "public", null, "view");

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, sql) => {
                if (sql.Contains("\"accounts\"")) dropTableSql = sql;
                if (sql.Contains("\"active_accounts\"")) dropViewSql = sql;
            });

        vm.GenerateDropQuery(table);
        vm.GenerateDropQuery(view);

        dropTableSql.Should().Contain("DROP TABLE \"public\".\"accounts\";");
        dropViewSql.Should().Contain("DROP VIEW \"public\".\"active_accounts\";");
    }
}
