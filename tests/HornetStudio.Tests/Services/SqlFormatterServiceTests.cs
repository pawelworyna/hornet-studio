using System;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Desktop.ViewModels.Tabs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HornetStudio.Tests.Services;

public sealed class SqlFormatterServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HornetDbContext _dbContext;

    public SqlFormatterServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
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
    public void Format_SimpleSelect_UppercasesKeywords_And_FormatsClauses()
    {
        var formatter = new SqlFormatterService();
        var raw = "select id, name, email from users where active = true order by id desc;";
        var formatted = formatter.Format(raw);

        formatted.Should().Contain("SELECT");
        formatted.Should().Contain("FROM");
        formatted.Should().Contain("WHERE");
        formatted.Should().Contain("ORDER BY");
        formatted.Should().Contain("DESC;");
        formatted.Should().Contain("\n");
    }

    [Fact]
    public void Format_ComplexQueryWithJoins_BreaksClausesProperly()
    {
        var formatter = new SqlFormatterService();
        var raw = "select o.id, c.name from orders o inner join customers c on o.cust_id = c.id where o.amount > 100 and c.active = 1 group by o.id, c.name limit 10;";
        var formatted = formatter.Format(raw);

        formatted.Should().Contain("INNER JOIN");
        formatted.Should().Contain("GROUP BY");
        formatted.Should().Contain("LIMIT 10;");
        formatted.Should().Contain("AND");
    }

    [Fact]
    public void Format_PreservesStringLiterals_And_Comments()
    {
        var formatter = new SqlFormatterService();
        var raw = "-- Fetch active customers\nselect id, 'Hello, World!' as greeting from users where name = 'O''Connor';";
        var formatted = formatter.Format(raw);

        formatted.Should().Contain("-- Fetch active customers");
        formatted.Should().Contain("'Hello, World!'");
        formatted.Should().Contain("'O''Connor'");
    }

    [Fact]
    public void Format_JsonPayload_PrettifiesIndent()
    {
        var formatter = new SqlFormatterService();
        var raw = "{\"query\":{\"status\":\"active\"},\"limit\":50}";
        var formatted = formatter.Format(raw);

        formatted.Should().Contain("\n");
        formatted.Should().Contain("  \"query\": {");
        formatted.Should().Contain("  \"limit\": 50");
    }

    [Fact]
    public void Format_MongoDbQuery_PrettifiesInnerJson()
    {
        var formatter = new SqlFormatterService();
        var raw = "db.users.find({\"status\":\"active\",\"age\":{\"$gt\":21}})";
        var formatted = formatter.Format(raw);

        formatted.Should().StartWith("db.users.find(");
        formatted.Should().Contain("\n");
        formatted.Should().Contain("\"status\": \"active\"");
    }

    [Fact]
    public void QueryEditorViewModel_FormatQuery_UpdatesQueryText_And_Status()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();
        var formatter = new SqlFormatterService();

        var vm = new QueryEditorViewModel(
            mockClient.Object,
            _dbContext,
            mockExport.Object,
            initialQuery: "select a, b from table1 where a = 1;",
            title: "Test Tab",
            sqlFormatter: formatter);

        vm.FormatQuery();

        vm.QueryText.Should().Contain("SELECT");
        vm.QueryText.Should().Contain("FROM");
        vm.QueryText.Should().Contain("WHERE");
        vm.StatusText.Should().Be("✨ SQL formatted");
    }
}
