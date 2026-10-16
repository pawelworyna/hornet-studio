using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Desktop.ViewModels.Tabs;
using Moq;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class TableBuilderViewModelTests
{
    private static Mock<IDatabaseClient> CreateMockClient(ConnectionType connectionType)
    {
        var mock = new Mock<IDatabaseClient>();
        mock.SetupGet(c => c.ConnectionType).Returns(connectionType);

        mock.Setup(c => c.WrapIdentifier(It.IsAny<string>()))
            .Returns<string>(name => connectionType switch
            {
                ConnectionType.MySql => $"`{name}`",
                ConnectionType.SqlServer => $"[{name}]",
                _ => $"\"{name}\""
            });

        return mock;
    }

    [Fact]
    public void Constructor_InitializesWithDefaultColumns_AndGeneratesDdl_PostgreSql()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: "public", onTableCreated: () => Task.CompletedTask);

        vm.Columns.Should().HaveCount(3);
        vm.Columns[0].Name.Should().Be("id");
        vm.Columns[0].IsPrimaryKey.Should().BeTrue();
        vm.Columns[0].IsAutoIncrement.Should().BeTrue();
        vm.Columns[0].DataType.Should().Be("BIGINT");

        vm.Columns[1].Name.Should().Be("name");
        vm.Columns[1].DataType.Should().Be("VARCHAR(255)");
        vm.Columns[1].IsNullable.Should().BeFalse();

        vm.Columns[2].Name.Should().Be("created_at");
        vm.Columns[2].DefaultValue.Should().Be("CURRENT_TIMESTAMP");

        vm.GeneratedDdl.Should().Contain("CREATE TABLE \"public\".\"new_table\"");
        vm.GeneratedDdl.Should().Contain("\"id\" BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY");
    }

    [Fact]
    public void RegenerateDdl_Sqlite_GeneratesAutoincrementSyntax()
    {
        var mockClient = CreateMockClient(ConnectionType.Sqlite);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);
        vm.TableName = "items";

        vm.GeneratedDdl.Should().Contain("CREATE TABLE \"items\"");
        vm.GeneratedDdl.Should().Contain("\"id\" INTEGER PRIMARY KEY AUTOINCREMENT");
    }

    [Fact]
    public void RegenerateDdl_MySql_GeneratesAutoIncrementSyntax()
    {
        var mockClient = CreateMockClient(ConnectionType.MySql);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);
        vm.TableName = "users";

        vm.GeneratedDdl.Should().Contain("CREATE TABLE `users`");
        vm.GeneratedDdl.Should().Contain("`id` INT AUTO_INCREMENT PRIMARY KEY");
    }

    [Fact]
    public void RegenerateDdl_SqlServer_GeneratesIdentitySyntax()
    {
        var mockClient = CreateMockClient(ConnectionType.SqlServer);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: "dbo", onTableCreated: () => Task.CompletedTask);
        vm.TableName = "invoices";

        vm.GeneratedDdl.Should().Contain("CREATE TABLE [dbo].[invoices]");
        vm.GeneratedDdl.Should().Contain("[id] INT IDENTITY(1,1) PRIMARY KEY");
    }

    [Fact]
    public void RegenerateDdl_MongoDb_GeneratesCreateCollection()
    {
        var mockClient = CreateMockClient(ConnectionType.MongoDb);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);
        vm.TableName = "products";

        vm.GeneratedDdl.Should().Be("db.createCollection(\"products\")");
    }

    [Fact]
    public void AddColumnCommand_AppendsNewColumn()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);
        var initialCount = vm.Columns.Count;

        vm.AddColumnCommand.Execute(null);

        vm.Columns.Should().HaveCount(initialCount + 1);
        vm.Columns.Last().Name.Should().Be($"column_{initialCount + 1}");
        vm.GeneratedDdl.Should().Contain($"\"column_{initialCount + 1}\"");
    }

    [Fact]
    public void RemoveColumnCommand_RemovesColumn_UnlessLastRemaining()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);

        var colToRemove = vm.Columns[1];
        vm.RemoveColumnCommand.Execute(colToRemove);

        vm.Columns.Should().NotContain(colToRemove);
        vm.Columns.Should().HaveCount(2);

        // Try removing until 1 left
        vm.RemoveColumnCommand.Execute(vm.Columns[1]);
        vm.Columns.Should().HaveCount(1);

        // Should not remove the last column
        vm.RemoveColumnCommand.Execute(vm.Columns[0]);
        vm.Columns.Should().HaveCount(1);
    }

    [Fact]
    public void MoveColumnUp_And_MoveColumnDown_ReordersColumns()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);

        var firstCol = vm.Columns[0];
        var secondCol = vm.Columns[1];

        vm.MoveColumnDownCommand.Execute(firstCol);
        vm.Columns[0].Should().Be(secondCol);
        vm.Columns[1].Should().Be(firstCol);

        vm.MoveColumnUpCommand.Execute(firstCol);
        vm.Columns[0].Should().Be(firstCol);
        vm.Columns[1].Should().Be(secondCol);
    }

    [Fact]
    public async Task CreateTableAsync_ExecutesDdl_AndInvokesCallback()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        var executedDdl = string.Empty;
        mockClient.Setup(c => c.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((ddl, _) => executedDdl = ddl)
            .ReturnsAsync(new QueryResult { IsSuccess = true });

        var callbackCalled = false;
        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () =>
        {
            callbackCalled = true;
            return Task.CompletedTask;
        });

        await vm.CreateTableCommand.ExecuteAsync(null);

        executedDdl.Should().NotBeNullOrWhiteSpace();
        executedDdl.Should().Be(vm.GeneratedDdl);
        callbackCalled.Should().BeTrue();
        vm.StatusMessage.Should().Contain("created successfully");
        vm.ErrorMessage.Should().BeNull();
        vm.IsCreating.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTableAsync_HandlesException_SetsErrorMessage()
    {
        var mockClient = CreateMockClient(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Table already exists"));

        var vm = new TableBuilderViewModel(mockClient.Object, defaultSchema: null, onTableCreated: () => Task.CompletedTask);

        await vm.CreateTableCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Table already exists");
        vm.StatusMessage.Should().BeNull();
        vm.IsCreating.Should().BeFalse();
    }
}
