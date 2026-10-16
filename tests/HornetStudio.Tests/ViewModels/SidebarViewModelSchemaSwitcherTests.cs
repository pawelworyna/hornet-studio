using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage;
using HornetStudio.Desktop.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class SidebarViewModelSchemaSwitcherTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HornetDbContext _dbContext;

    public SidebarViewModelSchemaSwitcherTests()
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
    public async Task RefreshAsync_For_Postgres_Discovers_Schemas_And_Defaults_To_Public()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.Config).Returns(new ConnectionConfig { Name = "PgTest", ConnectionType = ConnectionType.PostgreSql });
        mockClient.Setup(c => c.GetSupportedFeaturesAsync()).ReturnsAsync(new SupportedFeatures { Schemas = true });
        mockClient.Setup(c => c.ListSchemasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "public", "auth", "storage" });
        mockClient.Setup(c => c.ListTablesAsync("public", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>
            {
                new("users", "public", null, "table"),
                new("orders", "public", null, "table")
            });
        mockClient.Setup(c => c.ListViewsAsync("public", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>());

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { });

        await vm.RefreshAsync();

        vm.SchemaLabel.Should().Be("Schema");
        vm.CanSwitchSchema.Should().BeTrue();
        vm.AvailableSchemas.Should().BeEquivalentTo(new[] { "public", "auth", "storage" });
        vm.SelectedSchema.Should().Be("public");
        vm.FilteredTables.Should().HaveCount(2);
        vm.FilteredTables.Select(t => t.Name).Should().Contain(new[] { "users", "orders" });
    }

    [Fact]
    public async Task Changing_SelectedSchema_Loads_Tables_For_New_Schema()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.PostgreSql);
        mockClient.Setup(c => c.Config).Returns(new ConnectionConfig { Name = "PgTest", ConnectionType = ConnectionType.PostgreSql });
        mockClient.Setup(c => c.GetSupportedFeaturesAsync()).ReturnsAsync(new SupportedFeatures { Schemas = true });
        mockClient.Setup(c => c.ListSchemasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "public", "auth" });
        mockClient.Setup(c => c.ListTablesAsync("public", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView> { new("users", "public", null, "table") });
        mockClient.Setup(c => c.ListViewsAsync("public", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>());

        mockClient.Setup(c => c.ListTablesAsync("auth", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>
            {
                new("identities", "auth", null, "table"),
                new("tokens", "auth", null, "table")
            });
        mockClient.Setup(c => c.ListViewsAsync("auth", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>());

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { });

        await vm.RefreshAsync();
        vm.SelectedSchema.Should().Be("public");
        vm.FilteredTables.Should().HaveCount(1);

        // Switch schema to auth
        await vm.LoadTablesAndViewsAsync("auth");
        vm.SelectedSchema = "auth";

        vm.FilteredTables.Should().HaveCount(2);
        vm.FilteredTables.Select(t => t.Name).Should().Contain(new[] { "identities", "tokens" });
    }

    [Fact]
    public async Task RefreshAsync_For_MySQL_Discovers_Databases()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.MySql);
        mockClient.Setup(c => c.Config).Returns(new ConnectionConfig
        {
            Name = "MySqlTest",
            ConnectionType = ConnectionType.MySql,
            DefaultDatabase = "store_db"
        });
        mockClient.Setup(c => c.GetSupportedFeaturesAsync()).ReturnsAsync(new SupportedFeatures { Schemas = false });
        mockClient.Setup(c => c.ListDatabasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "store_db", "analytics_db", "staging" });
        mockClient.Setup(c => c.ListTablesAsync("store_db", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView> { new("products", "store_db", null, "table") });
        mockClient.Setup(c => c.ListViewsAsync("store_db", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>());

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { });

        await vm.RefreshAsync();

        vm.SchemaLabel.Should().Be("Database");
        vm.CanSwitchSchema.Should().BeTrue();
        vm.AvailableSchemas.Should().BeEquivalentTo(new[] { "store_db", "analytics_db", "staging" });
        vm.SelectedSchema.Should().Be("store_db");
        vm.FilteredTables.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshAsync_For_Sqlite_Disables_Schema_Switcher()
    {
        var mockClient = new Mock<IDatabaseClient>();
        mockClient.Setup(c => c.ConnectionType).Returns(ConnectionType.Sqlite);
        mockClient.Setup(c => c.Config).Returns(new ConnectionConfig { Name = "SqliteTest", ConnectionType = ConnectionType.Sqlite });
        mockClient.Setup(c => c.GetSupportedFeaturesAsync()).ReturnsAsync(new SupportedFeatures { Schemas = false });
        mockClient.Setup(c => c.ListTablesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView> { new("notes", null, null, "table") });
        mockClient.Setup(c => c.ListViewsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableOrView>());

        var vm = new SidebarViewModel(
            mockClient.Object,
            _dbContext,
            _ => { },
            _ => { },
            (_, _) => { });

        await vm.RefreshAsync();

        vm.CanSwitchSchema.Should().BeFalse();
        vm.AvailableSchemas.Should().ContainSingle().Which.Should().Be("main");
        vm.FilteredTables.Should().HaveCount(1);
    }
}
