using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Desktop.ViewModels.Tabs;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class QueryEditorViewModelExplainTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly HornetDbContext _dbContext;

    public QueryEditorViewModelExplainTests()
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
    public async Task ExplainQueryCommand_ExecutesExplainQueryAsync_AndUpdatesGrid()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();

        var explainResult = new QueryResult
        {
            IsSuccess = true,
            Columns =
            [
                new QueryResultColumn { Name = "id", Type = "Int32", Ordinal = 0 },
                new QueryResultColumn { Name = "detail", Type = "String", Ordinal = 1 }
            ],
            Rows =
            [
                [1, "SCAN TABLE users"]
            ]
        };

        mockClient.Setup(c => c.ExplainQueryAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(explainResult);

        var vm = new QueryEditorViewModel(
            mockClient.Object,
            _dbContext,
            mockExport.Object,
            initialQuery: "SELECT * FROM users;");

        await vm.ExplainQueryCommand.ExecuteAsync(null);

        vm.Columns.Should().HaveCount(2);
        vm.Rows.Should().HaveCount(1);
        vm.StatusText.Should().Contain("Explain Plan generated");
        vm.ErrorMessage.Should().BeNull();
        vm.IsExecuting.Should().BeFalse();
    }

    [Fact]
    public async Task ExplainQueryCommand_WhenClientReturnsError_SetsErrorMessage()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();

        var errorResult = new QueryResult
        {
            IsSuccess = false,
            ErrorMessage = "EXPLAIN syntax error"
        };

        mockClient.Setup(c => c.ExplainQueryAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResult);

        var vm = new QueryEditorViewModel(
            mockClient.Object,
            _dbContext,
            mockExport.Object,
            initialQuery: "SELECT * FROM invalid_table;");

        await vm.ExplainQueryCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("EXPLAIN syntax error");
        vm.StatusText.Should().Contain("Explain Plan failed");
        vm.IsExecuting.Should().BeFalse();
    }
}
