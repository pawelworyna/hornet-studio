using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Tests.ViewModels;

public sealed class SidebarViewModelPinTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HornetDbContext _dbContext;
    private readonly Mock<IDatabaseClient> _clientMock;
    private readonly ConnectionConfig _config;

    public SidebarViewModelPinTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new HornetDbContext(options);
        _dbContext.Database.EnsureCreated();

        _config = new ConnectionConfig
        {
            Id = Guid.NewGuid(),
            Name = "Pinning Test DB"
        };

        _clientMock = new Mock<IDatabaseClient>();
        _clientMock.Setup(c => c.Config).Returns(_config);
        _clientMock.Setup(c => c.GetSupportedFeaturesAsync())
            .ReturnsAsync(new SupportedFeatures { Schemas = true });
        _clientMock.Setup(c => c.ListSchemasAsync(default))
            .ReturnsAsync(new[] { "public" });
        _clientMock.Setup(c => c.ListTablesAsync("public", default))
            .ReturnsAsync(new[]
            {
                new TableOrView("users", "public"),
                new TableOrView("orders", "public"),
                new TableOrView("products", "public")
            });
        _clientMock.Setup(c => c.ListViewsAsync("public", default))
            .ReturnsAsync(new[]
            {
                new TableOrView("v_active_users", "public", null, "view")
            });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private SidebarViewModel CreateSidebar()
    {
        return new SidebarViewModel(
            _clientMock.Object,
            _dbContext,
            openTableData: _ => { },
            openTableProperties: _ => { },
            openQuery: (_, _) => { });
    }

    [Fact]
    public async Task TogglePinAsync_PinsTableAndSavesToDatabase()
    {
        var vm = CreateSidebar();
        await vm.RefreshAsync();

        var usersTable = vm.FilteredTables.First(t => t.Name == "users");
        vm.IsPinned(usersTable).Should().BeFalse();
        vm.HasPinnedItems.Should().BeFalse();

        await vm.TogglePinAsync(usersTable);

        vm.IsPinned(usersTable).Should().BeTrue();
        vm.HasPinnedItems.Should().BeTrue();
        vm.PinnedItems.Should().HaveCount(1);
        vm.FilteredPinnedItems.Should().HaveCount(1);
        vm.FilteredPinnedItems[0].Name.Should().Be("users");

        // Verify persistence in UserSettings table
        var settingKey = $"pinned_tables_{_config.Id}";
        var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == settingKey);
        setting.Should().NotBeNull();
        var pinnedList = JsonSerializer.Deserialize<List<string>>(setting!.Value);
        pinnedList.Should().Contain("public.users");
    }

    [Fact]
    public async Task TogglePinAsync_WhenAlreadyPinned_UnpinsAndUpdatesDatabase()
    {
        var vm = CreateSidebar();
        await vm.RefreshAsync();

        var usersTable = vm.FilteredTables.First(t => t.Name == "users");
        await vm.TogglePinAsync(usersTable);
        vm.HasPinnedItems.Should().BeTrue();

        // Unpin
        await vm.TogglePinAsync(usersTable);

        vm.IsPinned(usersTable).Should().BeFalse();
        vm.HasPinnedItems.Should().BeFalse();
        vm.PinnedItems.Should().BeEmpty();
        vm.FilteredPinnedItems.Should().BeEmpty();

        var settingKey = $"pinned_tables_{_config.Id}";
        var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == settingKey);
        var pinnedList = JsonSerializer.Deserialize<List<string>>(setting!.Value);
        pinnedList.Should().NotContain("public.users");
    }

    [Fact]
    public async Task RefreshAsync_RestoresPinnedTablesFromDatabase()
    {
        var settingKey = $"pinned_tables_{_config.Id}";
        _dbContext.UserSettings.Add(new UserSettingEntity
        {
            Key = settingKey,
            Value = JsonSerializer.Serialize(new List<string> { "public.orders", "public.v_active_users" }),
            UpdatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var vm = CreateSidebar();
        await vm.RefreshAsync();

        vm.HasPinnedItems.Should().BeTrue();
        vm.PinnedItems.Should().HaveCount(2);
        vm.FilteredPinnedItems.Select(x => x.Name).Should().Contain(new[] { "orders", "v_active_users" });
    }

    [Fact]
    public async Task FilterText_FiltersPinnedItems()
    {
        var vm = CreateSidebar();
        await vm.RefreshAsync();

        var usersTable = vm.FilteredTables.First(t => t.Name == "users");
        var ordersTable = vm.FilteredTables.First(t => t.Name == "orders");
        await vm.TogglePinAsync(usersTable);
        await vm.TogglePinAsync(ordersTable);

        vm.FilteredPinnedItems.Should().HaveCount(2);

        vm.FilterText = "ord";
        vm.FilteredPinnedItems.Should().HaveCount(1);
        vm.FilteredPinnedItems[0].Name.Should().Be("orders");

        vm.FilterText = "nonexistent";
        vm.FilteredPinnedItems.Should().BeEmpty();
        vm.HasPinnedItems.Should().BeFalse();
    }
}
