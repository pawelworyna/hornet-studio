using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Core.Storage.Security;
using HornetStudio.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HornetStudio.Tests.ViewModels;

public class ConnectionManagerViewModelTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly string _tempVaultDir;
    private readonly HornetDbContext _db;

    public ConnectionManagerViewModelTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"hornet_cm_test_{Guid.NewGuid()}.db");
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"hornet_vault_{Guid.NewGuid()}");

        var opts = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite($"Data Source={_tempDbFile}")
            .Options;
        _db = new HornetDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ReloadConnections_LoadsAndFiltersConnections()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        _db.SavedConnections.AddRange(
            new SavedConnectionEntity { Name = "Production Postgres", ConnectionType = ConnectionType.PostgreSql, Host = "prod.server" },
            new SavedConnectionEntity { Name = "Local SQLite", ConnectionType = ConnectionType.Sqlite, Host = "local.db" }
        );
        await _db.SaveChangesAsync();

        var vm = new ConnectionManagerViewModel(_db, vault, mockFactory.Object, _ => { });
        await vm.ReloadConnectionsAsync();

        vm.AllConnections.Should().HaveCount(2);
        vm.FilteredConnections.Should().HaveCount(2);
        vm.HasConnections.Should().BeTrue();

        // Test search filter
        vm.SearchQuery = "sqlite";
        vm.FilteredConnections.Should().HaveCount(1);
        vm.FilteredConnections[0].Name.Should().Be("Local SQLite");

        vm.SearchQuery = "nonexistent";
        vm.FilteredConnections.Should().BeEmpty();
        vm.HasNoConnections.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteConnection_RemovesFromDatabase()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var entity = new SavedConnectionEntity { Name = "To Delete", ConnectionType = ConnectionType.MySql };
        _db.SavedConnections.Add(entity);
        await _db.SaveChangesAsync();

        var vm = new ConnectionManagerViewModel(_db, vault, mockFactory.Object, _ => { });
        await vm.ReloadConnectionsAsync();

        var item = vm.AllConnections.First();
        await vm.DeleteConnectionAsync(item);

        vm.AllConnections.Should().BeEmpty();
        (await _db.SavedConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RestoreLastSession_IdentifiesLastActiveConnection()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var clientMock = new Mock<IDatabaseClient>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<ConnectionConfig>())).Returns(clientMock.Object);

        var connId = Guid.NewGuid();
        var entity = new SavedConnectionEntity { Id = connId, Name = "Session Database", ConnectionType = ConnectionType.Sqlite };
        _db.SavedConnections.Add(entity);
        _db.UserSettings.Add(new UserSettingEntity { Key = "session:last_active_connection_id", Value = connId.ToString() });
        await _db.SaveChangesAsync();

        IDatabaseClient? connectedClient = null;
        var vm = new ConnectionManagerViewModel(_db, vault, mockFactory.Object, client => connectedClient = client);
        await vm.ReloadConnectionsAsync();

        vm.HasLastSession.Should().BeTrue();
        vm.LastSessionConnection.Should().NotBeNull();
        vm.LastSessionConnection!.Name.Should().Be("Session Database");

        await vm.RestoreLastSessionAsync();
        connectedClient.Should().Be(clientMock.Object);
    }
}
